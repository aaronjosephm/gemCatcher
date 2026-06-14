using System.Collections.Generic;
using UnityEngine;

public class GemCatcher : MonoBehaviour
{
    private Transform catcher; // Reference to the catcher (Box)
    private BoxCollider catcherCollider; // Catcher's BoxCollider
    private SphereCollider gemCollider; // Gem's SphereCollider

    private Vector3 catcherSize;
    private Vector3 catcherCenter;

    // Scoring rules.
    public const int POINTS_PER_CATCH = 20;
    public const int POINTS_PER_MISS = -10;
    public const int POINTS_PER_GOLDEN_CATCH = 100;
    public const int POINTS_PER_GOLD_BAR_CATCH = 500;

    // Lives rules.
    public const int STARTING_LIVES = 3;
    // Hard ceiling on total lives. Hearts, milestone gifts, and the per-100
    // bonus all stop being awarded once the player is at this cap.
    public const int MAX_LIVES = 10;

    // Static score & lives tracking.
    public static int Score { get; private set; }
    public static int Lives { get; private set; } = STARTING_LIVES;
    public static bool IsGameOver { get; private set; }

    // Per-gem catch counts. Keyed by the prefab name (with the "(Clone)" suffix stripped).
    // Read by UIManager when the game over panel is shown.
    public static Dictionary<string, int> CatchesByGemName { get; private set; }
        = new Dictionary<string, int>();

    // Event for score changes (any source).
    public delegate void ScoreChangedDelegate(int newScore);
    public static event ScoreChangedDelegate OnScoreChanged;

    // Event for life-count changes.
    public static event ScoreChangedDelegate OnLivesChanged;

    // Fired exactly once when the player runs out of lives.
    public static event System.Action OnGameOver;

    // Event fired when a gem is caught. Subscribers (e.g. UIManager) use this to spawn
    // the floating "+20" pop-up at the catch location.
    public delegate void GemCaughtDelegate(int amount, Vector3 worldPosition);
    public static event GemCaughtDelegate OnGemCaught;

    // Event fired when a gem falls off-screen uncaught. Same shape as OnGemCaught so
    // listeners can render a matching "-10" pop-up at the miss location.
    public static event GemCaughtDelegate OnGemMissed;

    // Fired when the player crosses one (or more) bonus-life thresholds. `count` is
    // the number of lives awarded by this single AddScore call (almost always 1, but
    // could be 2+ if a single delta jumps multiple thresholds). UI/SFX subscribe to
    // this to show an "EXTRA LIFE!" banner and play a celebratory sound.
    public delegate void BonusLifeDelegate(int count);
    public static event BonusLifeDelegate OnBonusLifeAwarded;

    // Fired when the player catches a Bomb gem (and the shield didn't absorb it).
    // UIManager listens for a distinct "BOOM!" floating text + red flash; SoundManager
    // plays a heavier impact sound.
    public static event System.Action<Vector3> OnBombHit;

    // Fired when the player catches a Gold Bar (the +500 jackpot variant). UIManager
    // shows a celebratory banner + screen flash; SoundManager plays a triumphant
    // arpeggio; HapticManager fires a success pulse. Distinct from OnGemCaught so
    // each subsystem can give it the bigger "you hit the jackpot" treatment.
    public static event System.Action<Vector3> OnGoldBarCaught;

    // Internal helper so non-catcher code (e.g. FallingObject's bottom-boundary check)
    // can report a miss without needing to know about both the lives update and the event.
    // Misses cost a life but no longer deduct points; the event amount is left at the
    // POINTS_PER_MISS constant for any listener that wants the conventional value.
    public static void ReportGemMissed(Vector3 worldPosition)
    {
        if (IsGameOver) return;

        // Shield power-up absorbs the miss — no life lost, and every other
        // active power-up keeps running. Only the shield charge is consumed.
        if (PowerUpManager.TryConsumeShield(worldPosition))
        {
            return;
        }

        // Without a shield, a miss revokes the whole power-up stack at once.
        // This is the only way active power-ups expire — they never time out.
        PowerUpManager.RevokeAllOnMiss();

        // Same rule for the combo: shield protects it, otherwise the streak
        // resets to zero. ComboManager.Break is a no-op if there was no streak.
        ComboManager.Break();

        OnGemMissed?.Invoke(POINTS_PER_MISS, worldPosition);
        // Subtle camera shake gives the miss some weight without being disruptive.
        CameraShake.Shake(0.12f, 0.22f);
        ChangeLives(-1);
    }

    /// <summary>
    /// Award N free lives. The two callers are (1) the every-third-catch
    /// combo award in <see cref="HandleVariantCatch"/> and (2) the ExtraLife
    /// power-up in <see cref="PowerUpManager.Activate"/>. Fires
    /// <see cref="OnBonusLifeAwarded"/> so the UI/SFX/haptic plumbing reacts
    /// identically regardless of the source. Caps lives at <see cref="MAX_LIVES"/>
    /// so stacks of bonuses can't push the heart count past the ceiling, and
    /// reports the count actually granted (e.g. a player at 9/10 lives
    /// catching the +3 ExtraLife power-up sees a +1 award, not +3).
    /// </summary>
    public static void AddLives(int count)
    {
        if (IsGameOver || count <= 0) return;
        int room = Mathf.Max(0, MAX_LIVES - Lives);
        int actual = Mathf.Min(count, room);
        if (actual <= 0) return;
        ChangeLives(actual);
        OnBonusLifeAwarded?.Invoke(actual);
    }

    // Records a successful catch of the given gem name (used to populate the game-over breakdown).
    public static void RecordCatch(string gemName)
    {
        if (string.IsNullOrEmpty(gemName)) return;
        CatchesByGemName.TryGetValue(gemName, out int count);
        CatchesByGemName[gemName] = count + 1;
    }

    // Add (or subtract) points. Score is clamped at 0; misses never push it negative.
    // Lives are no longer tied to score — they only come from the ExtraLife
    // power-up and the every-third-catch combo award (see HandleVariantCatch).
    public static void AddScore(int delta)
    {
        if (IsGameOver) return;

        Score = Mathf.Max(0, Score + delta);
        OnScoreChanged?.Invoke(Score);
    }

    /// <summary>
    /// Force the round to end without losing all lives. Used by the Daily
    /// Challenge when the gem cap is reached so the same OnGameOver handlers
    /// (UIManager game-over panel, etc.) drive the end-of-round flow.
    /// Idempotent — safe to call when already in game-over state.
    /// </summary>
    public static void EndGame()
    {
        if (IsGameOver) return;
        IsGameOver = true;
        OnGameOver?.Invoke();
    }

    private static void ChangeLives(int delta)
    {
        int previousLives = Lives;
        // Defensive clamp — every caller routes through here, so this is the
        // single source of truth for the [0, MAX_LIVES] life range.
        Lives = Mathf.Clamp(Lives + delta, 0, MAX_LIVES);
        OnLivesChanged?.Invoke(Lives);

        if (Lives <= 0 && previousLives > 0 && !IsGameOver)
        {
            IsGameOver = true;
            // Heavier kick + a brief hit-stop on game over, the way most action games do it.
            CameraShake.Shake(0.35f, 0.55f);
            OnGameOver?.Invoke();
        }
    }

    public static void ResetScore()
    {
        Score = 0;
        CatchesByGemName.Clear();
        OnScoreChanged?.Invoke(Score);
    }

    public static void ResetLives()
    {
        Lives = STARTING_LIVES;
        IsGameOver = false;
        OnLivesChanged?.Invoke(Lives);
    }

    // Reset static state when entering Play Mode. Required because Unity's "Domain Reload"
    // option may be disabled, leaving statics dirty across play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Score = 0;
        Lives = STARTING_LIVES;
        IsGameOver = false;
        if (CatchesByGemName == null) CatchesByGemName = new Dictionary<string, int>();
        else CatchesByGemName.Clear();
        OnScoreChanged = null;
        OnLivesChanged = null;
        OnGameOver = null;
        OnGemCaught = null;
        OnGemMissed = null;
        OnBonusLifeAwarded = null;
        OnBombHit = null;
        OnGoldBarCaught = null;
    }

    void Start()
    {
        // Cache the gem's collider if it exists
        gemCollider = GetComponent<SphereCollider>();

        // Find the catcher at start
        FindCatcher();
    }

    void Update()
    {
        // Check if the gem crosses the catcher's boundaries
        if (IsGemWithinCatcherBounds())
        {
            Vector3 catchPosition = transform.position;

            // Power-up gems short-circuit the variant routing — they award
            // no points, don't touch combo, and dispatch to PowerUpManager
            // for activation. Read isPowerUp BEFORE the variant switch so a
            // power-up gem (which is internally a Normal-variant gem with
            // an isPowerUp flag) doesn't accidentally also run the +20
            // points / combo path.
            FallingObject fo = GetComponent<FallingObject>();
            if (fo != null && fo.isPowerUp)
            {
                HandlePowerUpCatch(fo.powerUpType, catchPosition);
                gameObject.SetActive(false);
                return;
            }

            // What variant did the player just touch? Drives every branch
            // below — bombs hurt, hearts heal, gold doubles points, normal
            // is the standard +20 path. Reads from the FallingObject the
            // gem was spawned with; defaults to Normal if missing.
            SpecialGemType variant = fo != null ? fo.specialType : SpecialGemType.Normal;

            HandleVariantCatch(variant, catchPosition);

            // Deactivate the gem
            gameObject.SetActive(false);
        }
    }

    // Power-up gem catch: activate the buff, fire a tinted catch burst, no
    // score, no combo change, no record-keeping in the gem-breakdown. We
    // explicitly DON'T call ComboManager.RegisterCatch because a power-up
    // is not a "scoring catch" — its reward is the buff itself, not points,
    // and reusing the combo system would let a redundant power-up roll
    // (e.g. Wide while Wide is already active) inflate the combo for free.
    // Conversely we don't break combo either, matching the silent-miss
    // policy on the off-screen path: power-ups are combo-neutral.
    private void HandlePowerUpCatch(PowerUpType type, Vector3 catchPosition)
    {
        PowerUpManager.Activate(type);

        // Tinted catch burst gives an instant catch confirmation in the
        // power-up's theme color. PowerUpManager.Activate fires its own
        // banner / vignette / lives-pop on top of this through the
        // UIManager subscriptions, so the burst is just the local "you
        // caught it" sparkle anchored at the catch site.
        Color burstColor = PowerUpPickup.ColorForType(type);
        CatchBurst.Spawn(catchPosition, burstColor);

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.Play("PowerUp");
        }
    }

    // Routes a caught gem through the right scoring / lives / FX path based
    // on its variant. Pulled out of Update() so it stays readable as we add
    // more variants.
    private void HandleVariantCatch(SpecialGemType variant, Vector3 catchPosition)
    {
        // Bombs are the only catch that hurts — short-circuits everything
        // else. Shield can absorb the bomb the same way it absorbs a miss.
        if (variant == SpecialGemType.Bomb)
        {
            ApplyBombHit(catchPosition);
            return;
        }

        // For all other variants the catch counts toward the streak. Register
        // the catch FIRST so the multiplier on this very catch reflects the
        // tier the catch itself unlocks (combo 3 → ×1.5 applies to that 3rd).
        ComboManager.RegisterCatch();
        float comboMultiplier = ComboManager.CurrentMultiplier;
        int comboAfterCatch = ComboManager.CurrentCombo;

        // Base points per variant. Gold Bar is the jackpot tier.
        int basePoints;
        switch (variant)
        {
            case SpecialGemType.GoldBar: basePoints = POINTS_PER_GOLD_BAR_CATCH; break;
            case SpecialGemType.Golden:  basePoints = POINTS_PER_GOLDEN_CATCH; break;
            default:                     basePoints = POINTS_PER_CATCH; break;
        }

        // 2× SCORE power-up stacks multiplicatively with the combo multiplier.
        int awarded = Mathf.RoundToInt(basePoints * comboMultiplier * PowerUpManager.DoubleScoreMultiplier);
        AddScore(awarded);
        OnGemCaught?.Invoke(awarded, catchPosition);

        // Combo bonus-life: every third consecutive catch (combo 3, 6, 9, 12,
        // …) grants +1 life. Combo 10 — the moment the multiplier ladder
        // tops out at ×5 — does NOT grant a life because 10 % 3 != 0, which
        // is the explicit "max combo doesn't award" carve-out from the
        // design. This and the ExtraLife power-up are now the only two ways
        // to gain a life in normal play. Routed through AddLives so the
        // MAX_LIVES cap, OnLivesChanged event, and "EXTRA LIFE" banner /
        // SFX / haptic plumbing all fire through the same path the
        // power-up takes.
        if (comboAfterCatch > 0 && comboAfterCatch % 3 == 0)
        {
            AddLives(1);
        }

        // Jackpot fan-out — UI banner, gold-bar audio cue, and a beefy
        // haptic all subscribe to this. Fired AFTER OnGemCaught so the
        // floating "+N" pop-up from the regular catch flow appears, then
        // the celebratory banner stacks above it.
        if (variant == SpecialGemType.GoldBar)
        {
            OnGoldBarCaught?.Invoke(catchPosition);
        }

        // "(Clone)" is appended by Instantiate; strip it for nicer display in the
        // game-over breakdown.
        string gemName = gameObject.name.Replace("(Clone)", "").Trim();
        RecordCatch(gemName);

        PlayCatchEffect();
    }

    // Bomb-on-catch: shield absorbs if available, otherwise costs a life and
    // breaks the combo. Power-ups also revoke (same rule as a regular miss),
    // so a bomb is effectively a "self-inflicted miss" from the player.
    private void ApplyBombHit(Vector3 worldPosition)
    {
        if (IsGameOver) return;

        if (PowerUpManager.TryConsumeShield(worldPosition))
        {
            return; // Shield ate it — combo and power-ups survive.
        }

        PowerUpManager.RevokeAllOnMiss();
        ComboManager.Break();

        OnBombHit?.Invoke(worldPosition);
        // Bigger shake than a miss — the player actively did something wrong.
        CameraShake.Shake(0.30f, 0.45f);
        ChangeLives(-1);
    }

    void PlayCatchEffect()
    {
        // Particle pop tinted by the gem's own colour (so a red gem bursts red, etc.).
        // Falls back to a warm white if the gem prefab doesn't have a renderer.
        Color burstColor = new Color(1f, 0.95f, 0.7f);
        Renderer rend = GetComponentInChildren<Renderer>();
        if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_Color"))
        {
            Color c = rend.sharedMaterial.color;
            // Boost saturation/brightness so the burst pops against the play area.
            burstColor = new Color(
                Mathf.Lerp(c.r, 1f, 0.25f),
                Mathf.Lerp(c.g, 1f, 0.25f),
                Mathf.Lerp(c.b, 1f, 0.25f),
                1f);
        }
        CatchBurst.Spawn(transform.position, burstColor);
    }

    void FindCatcher()
    {
        GameObject catcherRef = GameObject.FindWithTag("Catcher");
        if (catcherRef)
        {
            catcher = catcherRef.transform;
            catcherCollider = catcher.GetComponent<BoxCollider>();
            UpdateCatcherBounds();
        }
    }

    void UpdateCatcherBounds()
    {
        if (catcher != null && catcherCollider != null)
        {
            // Scale by lossyScale so parent transforms are respected, and convert the local-space
            // collider center into world space rather than naively adding it to position.
            catcherSize = Vector3.Scale(catcherCollider.size, catcher.lossyScale);
            catcherCenter = catcher.TransformPoint(catcherCollider.center);
        }
    }

    bool IsGemWithinCatcherBounds()
    {
        // If we don't have a catcher reference, try to find it
        if (catcher == null)
        {
            FindCatcher();
            if (catcher == null) return false;
        }

        // Update the catcher bounds in case it moved
        UpdateCatcherBounds();

        // Get the gem's current position
        Vector3 gemPosition = transform.position;

        // Get the gem's radius if it has a SphereCollider
        float gemRadius = gemCollider != null ? gemCollider.radius * transform.localScale.x : 0.1f;

        // Check if the gem's position is within the catcher's bounds
        bool isWithinHorizontalBounds = Mathf.Abs(gemPosition.x - catcherCenter.x) <= (catcherSize.x / 2 + gemRadius);
        bool isWithinVerticalBounds = Mathf.Abs(gemPosition.y - catcherCenter.y) <= (catcherSize.y / 2 + gemRadius);

        // Optionally check for the z-axis if you're working in 3D
        bool isWithinDepthBounds = Mathf.Abs(gemPosition.z - catcherCenter.z) <= (catcherSize.z / 2 + gemRadius);

        // Return true if the gem is within all bounds
        return isWithinHorizontalBounds && isWithinVerticalBounds && isWithinDepthBounds;
    }
}
