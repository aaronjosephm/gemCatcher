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

    // Lives rules.
    public const int STARTING_LIVES = 3;
    // Hard ceiling on total lives. Hearts, milestone gifts, and the per-100
    // bonus all stop being awarded once the player is at this cap.
    public const int MAX_LIVES = 10;
    // Players earn one extra life every time their score crosses a multiple of this value.
    public const int POINTS_PER_BONUS_LIFE = 100;

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

    // Fired when the player catches a Heart gem. Lets the UI show a +1 ♥ pop-up
    // anchored at the catch site, distinct from the per-100-points bonus-life banner.
    public static event System.Action<Vector3> OnHeartGemCaught;

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
    /// Award N free lives. Used by MilestoneTracker (and any other "gift" path)
    /// that wants to hand out lives without going through a score threshold.
    /// Fires OnBonusLifeAwarded so the UI/SFX react identically to the per-100
    /// bonus path. Caps lives at <see cref="MAX_LIVES"/> so stacks of bonuses
    /// can't push the heart count past the ceiling.
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
    // Awards bonus lives whenever the score crosses a POINTS_PER_BONUS_LIFE threshold.
    public static void AddScore(int delta)
    {
        if (IsGameOver) return;

        int previousScore = Score;
        Score = Mathf.Max(0, Score + delta);
        OnScoreChanged?.Invoke(Score);

        // Bonus lives are a Normal-mode mechanic only. The Daily Challenge runs
    // with a locked life count so every player faces the same survival
    // pressure regardless of skill.
    if (delta > 0 && GameState.Mode == GameState.GameMode.Normal)
        {
            int previousTier = previousScore / POINTS_PER_BONUS_LIFE;
            int newTier = Score / POINTS_PER_BONUS_LIFE;
            if (newTier > previousTier)
            {
                // AddLives handles the MAX_LIVES cap and only fires
                // OnBonusLifeAwarded for the lives actually granted, so a player
                // already at the ceiling doesn't see a misleading banner.
                AddLives(newTier - previousTier);
            }
        }
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
        OnHeartGemCaught = null;
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

            // What variant did the player just touch? Drives every branch
            // below — bombs hurt, hearts heal, gold doubles points, normal
            // is the standard +20 path. Reads from the FallingObject the
            // gem was spawned with; defaults to Normal if missing.
            FallingObject fo = GetComponent<FallingObject>();
            SpecialGemType variant = fo != null ? fo.specialType : SpecialGemType.Normal;

            HandleVariantCatch(variant, catchPosition);

            // Deactivate the gem
            gameObject.SetActive(false);
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

        // Base points per variant. Hearts award the standard catch value AND
        // a free life (handled below).
        int basePoints = variant == SpecialGemType.Golden
            ? POINTS_PER_GOLDEN_CATCH
            : POINTS_PER_CATCH;

        // 2× SCORE power-up stacks multiplicatively with the combo multiplier.
        int awarded = Mathf.RoundToInt(basePoints * comboMultiplier * PowerUpManager.DoubleScoreMultiplier);
        AddScore(awarded);
        OnGemCaught?.Invoke(awarded, catchPosition);

        // Heart gems also give a life on top of the points. We add the life
        // but DON'T fire OnBonusLifeAwarded — that event is reserved for the
        // per-100-point bonus and the UI shows a different floating-text
        // (+1 ♥) for hearts.
        if (variant == SpecialGemType.Heart)
        {
            ChangeLives(+1);
            OnHeartGemCaught?.Invoke(catchPosition);
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
