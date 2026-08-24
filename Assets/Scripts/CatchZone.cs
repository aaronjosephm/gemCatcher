using UnityEngine;

/// <summary>
/// Centralized catch detection running on the catcher. Each frame, checks all
/// active FallingObjects against the catcher's bounds. Replaces the old per-gem
/// GemCatcher.Update() approach with a single check point on the catcher side.
///
/// <para>Uses direct transform-based bounds checking (identical math to the
/// original) rather than Unity's physics system, since gem prefabs may not have
/// physics colliders configured for overlap queries.</para>
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CatchZone : MonoBehaviour
{
    private BoxCollider catcherCollider;

    // Cached list of all FallingObject instances. Refreshed each frame to
    // handle pooled gems being activated/deactivated.
    private FallingObject[] activeFallingObjects;

    // Invincibility after hazard hit
    private bool isInvincible = false;
    private float invincibilityTimer = 0f;
    private const float InvincibilityDuration = 3f;
    private Renderer[] catcherRenderers;
    private float flashTimer = 0f;

    void Awake()
    {
        catcherCollider = GetComponent<BoxCollider>();
    }

    void Start()
    {
        catcherRenderers = GetComponentsInChildren<Renderer>(true);
    }

    void Update()
    {
        if (isInvincible)
        {
            invincibilityTimer -= Time.deltaTime;
            if (invincibilityTimer <= 0f)
            {
                EndInvincibility();
            }
            else
            {
                UpdateFlash();
            }
        }

        // Grab current active FallingObjects. With object pooling, typically
        // only 1-2 gems are active at any time, so this is very lightweight.
        activeFallingObjects = FindObjectsByType<FallingObject>(FindObjectsSortMode.None);

        Vector3 catcherCenter = transform.TransformPoint(catcherCollider.center);
        Vector3 catcherSize = Vector3.Scale(catcherCollider.size, transform.lossyScale);

        for (int i = 0; i < activeFallingObjects.Length; i++)
        {
            FallingObject fo = activeFallingObjects[i];
            if (fo == null || !fo.gameObject.activeInHierarchy) continue;

            if (IsWithinBounds(fo, catcherCenter, catcherSize))
            {
                ProcessCatch(fo);
            }
        }
    }

    private bool IsWithinBounds(FallingObject fo, Vector3 catcherCenter, Vector3 catcherSize)
    {
        Vector3 gemPosition = fo.transform.position;

        // Use SphereCollider radius if available, same fallback as original.
        SphereCollider sc = fo.GetComponent<SphereCollider>();
        float gemRadius = sc != null ? sc.radius * fo.transform.localScale.x : 0.1f;

        bool withinX = Mathf.Abs(gemPosition.x - catcherCenter.x) <= (catcherSize.x / 2 + gemRadius);
        bool withinY = Mathf.Abs(gemPosition.y - catcherCenter.y) <= (catcherSize.y / 2 + gemRadius);
        bool withinZ = Mathf.Abs(gemPosition.z - catcherCenter.z) <= (catcherSize.z / 2 + gemRadius);

        return withinX && withinY && withinZ;
    }

    private void ProcessCatch(FallingObject fo)
    {
        if (!fo.gameObject.activeInHierarchy) return;

        RoundManager rm = RoundManager.Instance;
        if (rm == null) return;

        Vector3 catchPosition = fo.transform.position;

        // Hazards (rocks) hurt — same as bombs.
        if (fo.isHazard)
        {
            if (isInvincible)
            {
                fo.gameObject.SetActive(false);
                return;
            }
            ApplyBombHit(catchPosition);
            fo.gameObject.SetActive(false);
            StartInvincibility();
            return;
        }

        // Poison gems look like gems but cost a life.
        if (fo.isPoisonGem)
        {
            if (isInvincible)
            {
                fo.gameObject.SetActive(false);
                return;
            }
            ApplyBombHit(catchPosition);
            fo.gameObject.SetActive(false);
            StartInvincibility();
            return;
        }

        // During invincibility, no points or catches.
        if (isInvincible)
        {
            fo.gameObject.SetActive(false);
            return;
        }

        // Rush Mode heart gem — awards extra life, no points.
        if (fo.isRushHeart)
        {
            RoundManager rm2 = RoundManager.Instance;
            if (rm2 != null) rm2.AddLives(1);
            PlayCatchEffect(fo);
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlayWithPitch("GemCaught", 1.5f);
            fo.gameObject.SetActive(false);
            return;
        }

        // Power-up gems short-circuit variant routing — no points, no combo change.
        if (fo.isPowerUp)
        {
            HandlePowerUpCatch(fo.powerUpType, catchPosition);
            fo.gameObject.SetActive(false);
            return;
        }

        SpecialGemType variant = fo.specialType;
        HandleVariantCatch(variant, catchPosition, fo);

        fo.gameObject.SetActive(false);
    }

    // ---- Power-up catch path -----------------------------------------------

    private void HandlePowerUpCatch(PowerUpType type, Vector3 catchPosition)
    {
        PowerUpManager.Activate(type);

        Color burstColor = PowerUpPickup.ColorForType(type);
        CatchBurst.Spawn(catchPosition, burstColor);

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.Play("PowerUp");
        }
    }

    // ---- Variant catch routing ---------------------------------------------

    private void HandleVariantCatch(SpecialGemType variant, Vector3 catchPosition, FallingObject fo)
    {
        RoundManager rm = RoundManager.Instance;

        // Bombs hurt — short-circuit everything else.
        if (variant == SpecialGemType.Bomb)
        {
            ApplyBombHit(catchPosition);
            return;
        }

        // MasterGem caught — the player wins!
        if (variant == SpecialGemType.MasterGem)
        {
            PlayCatchEffect(fo);
            // Play catch sound at higher pitch for the ultimate catch.
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayWithPitch("GemCaught", 1.8f);
            }
            rm.WinGame();
            return;
        }

        // Register the catch for combo tracking (disabled in Rush Mode).
        bool isRush = GameState.Mode == GameState.GameMode.Rush;
        if (!isRush) ComboManager.RegisterCatch();
        float comboMultiplier = isRush ? 1f : ComboManager.CurrentMultiplier;
        int comboAfterCatch = isRush ? 0 : ComboManager.CurrentCombo;

        // Base points per variant.
        int basePoints;
        switch (variant)
        {
            case SpecialGemType.Golden:  basePoints = RoundManager.POINTS_PER_GOLDEN_CATCH; break;
            default:                     basePoints = RoundManager.POINTS_PER_CATCH; break;
        }

        // 2× SCORE power-up stacks multiplicatively with combo.
        int awarded = Mathf.RoundToInt(basePoints * comboMultiplier * PowerUpManager.DoubleScoreMultiplier);
        rm.AddScore(awarded);
        rm.NotifyGemCaught(awarded, catchPosition);

        // Every third consecutive catch grants +1 life (disabled in Rush — hearts only).
        if (!isRush && comboAfterCatch > 0 && comboAfterCatch % 3 == 0)
        {
            rm.AddLives(1);
        }

        // Record for game-over breakdown.
        string gemName = fo.gameObject.name.Replace("(Clone)", "").Trim();
        rm.RecordCatch(gemName);

        PlayCatchEffect(fo);
    }

    // ---- Bomb handling -----------------------------------------------------

    private void ApplyBombHit(Vector3 worldPosition)
    {
        RoundManager rm = RoundManager.Instance;
        if (rm.IsGameOver) return;

        if (PowerUpManager.TryConsumeShield(worldPosition))
        {
            return;
        }

        PowerUpManager.RevokeAllOnMiss();
        if (GameState.Mode != GameState.GameMode.Rush) ComboManager.Break();

        rm.NotifyBombHit(worldPosition);
        CameraShake.Shake(0.30f, 0.45f);
        rm.DeductLife();
    }

    // ---- Catch effect -------------------------------------------------------

    private void PlayCatchEffect(FallingObject fo)
    {
        Color burstColor = fo.GetBurstColor();
        CatchBurst.Spawn(fo.transform.position, burstColor);
    }

    // ---- Invincibility --------------------------------------------------------

    private void StartInvincibility()
    {
        isInvincible = true;
        invincibilityTimer = InvincibilityDuration;
        flashTimer = 0f;
        if (catcherRenderers == null || catcherRenderers.Length == 0)
            catcherRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void EndInvincibility()
    {
        isInvincible = false;
        SetRenderersVisible(true);
    }

    private void UpdateFlash()
    {
        // Flash rate accelerates as invincibility wears off (slow → fast).
        float remaining = invincibilityTimer / InvincibilityDuration; // 1→0
        float flashRate = Mathf.Lerp(16f, 3f, remaining); // starts slow (3 Hz), ends fast (16 Hz)
        flashTimer += Time.deltaTime * flashRate;
        bool visible = Mathf.Sin(flashTimer * Mathf.PI * 2f) > 0f;
        SetRenderersVisible(visible);
    }

    private void SetRenderersVisible(bool visible)
    {
        if (catcherRenderers == null) return;
        for (int i = 0; i < catcherRenderers.Length; i++)
        {
            if (catcherRenderers[i] != null)
                catcherRenderers[i].enabled = visible;
        }
    }
}
