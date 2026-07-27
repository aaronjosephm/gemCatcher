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

    void Awake()
    {
        catcherCollider = GetComponent<BoxCollider>();
    }

    void Update()
    {
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

        // Power-up gems short-circuit variant routing — no points, no combo change.
        if (fo.isPowerUp)
        {
            HandlePowerUpCatch(fo.powerUpType, catchPosition);
            fo.gameObject.SetActive(false);
            return;
        }

        SpecialGemType variant = fo.specialType;
        HandleVariantCatch(variant, catchPosition, fo.gameObject);

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

    private void HandleVariantCatch(SpecialGemType variant, Vector3 catchPosition, GameObject gemObject)
    {
        RoundManager rm = RoundManager.Instance;

        // Bombs hurt — short-circuit everything else.
        if (variant == SpecialGemType.Bomb)
        {
            ApplyBombHit(catchPosition);
            return;
        }

        // Register the catch for combo tracking.
        ComboManager.RegisterCatch();
        float comboMultiplier = ComboManager.CurrentMultiplier;
        int comboAfterCatch = ComboManager.CurrentCombo;

        // Base points per variant.
        int basePoints;
        switch (variant)
        {
            case SpecialGemType.GoldBar: basePoints = RoundManager.POINTS_PER_GOLD_BAR_CATCH; break;
            case SpecialGemType.Golden:  basePoints = RoundManager.POINTS_PER_GOLDEN_CATCH; break;
            default:                     basePoints = RoundManager.POINTS_PER_CATCH; break;
        }

        // 2× SCORE power-up stacks multiplicatively with combo.
        int awarded = Mathf.RoundToInt(basePoints * comboMultiplier * PowerUpManager.DoubleScoreMultiplier);
        rm.AddScore(awarded);
        rm.NotifyGemCaught(awarded, catchPosition);

        // Every third consecutive catch grants +1 life.
        if (comboAfterCatch > 0 && comboAfterCatch % 3 == 0)
        {
            rm.AddLives(1);
        }

        // Gold Bar jackpot fan-out.
        if (variant == SpecialGemType.GoldBar)
        {
            rm.NotifyGoldBarCaught(catchPosition);
        }

        // Record for game-over breakdown.
        string gemName = gemObject.name.Replace("(Clone)", "").Trim();
        rm.RecordCatch(gemName);

        PlayCatchEffect(gemObject);
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
        ComboManager.Break();

        rm.NotifyBombHit(worldPosition);
        CameraShake.Shake(0.30f, 0.45f);
        rm.DeductLife();
    }

    // ---- Catch effect -------------------------------------------------------

    private void PlayCatchEffect(GameObject gemObject)
    {
        Color burstColor = new Color(1f, 0.95f, 0.7f);
        Renderer rend = gemObject.GetComponentInChildren<Renderer>();
        if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_Color"))
        {
            Color c = rend.sharedMaterial.color;
            burstColor = new Color(
                Mathf.Lerp(c.r, 1f, 0.25f),
                Mathf.Lerp(c.g, 1f, 0.25f),
                Mathf.Lerp(c.b, 1f, 0.25f),
                1f);
        }
        CatchBurst.Spawn(gemObject.transform.position, burstColor);
    }
}
