using UnityEngine;

/// <summary>
/// Physics-based catch detection. Attach this to the catcher GameObject (the one
/// tagged "Catcher" with a BoxCollider). Each FixedUpdate, performs a single
/// Physics.OverlapBox query to find gems inside the catch volume, then routes
/// them through scoring/variant/power-up logic.
///
/// <para>Replaces the old per-gem Update() AABB check with a single query on the
/// catcher — O(1) detection point instead of O(gems) doing checks per frame.</para>
///
/// <para>Uses an active overlap query rather than passive OnTriggerEnter because
/// gems move via Transform.Translate (not Rigidbody.MovePosition), which can
/// skip trigger callbacks if the gem traverses the collider volume between
/// physics ticks.</para>
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CatchZone : MonoBehaviour
{
    private BoxCollider catcherCollider;

    // Reusable buffer for overlap results — avoids allocation per frame.
    private readonly Collider[] overlapBuffer = new Collider[8];

    void Awake()
    {
        catcherCollider = GetComponent<BoxCollider>();
    }

    void FixedUpdate()
    {
        // Sync transforms so the query sees gem positions from the most recent
        // Update frame (gems move via Transform.Translate in Update).
        Physics.SyncTransforms();

        Vector3 center = transform.TransformPoint(catcherCollider.center);
        Vector3 halfExtents = Vector3.Scale(catcherCollider.size * 0.5f, transform.lossyScale);

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, transform.rotation);

        for (int i = 0; i < count; i++)
        {
            ProcessCatch(overlapBuffer[i]);
        }
    }

    private void ProcessCatch(Collider other)
    {
        // Only process falling gems/power-ups.
        FallingObject fo = other.GetComponent<FallingObject>();
        if (fo == null) return;

        // Ignore gems that are already deactivated (pooled objects can linger
        // in the overlap buffer on the frame they're disabled).
        if (!other.gameObject.activeInHierarchy) return;

        RoundManager rm = RoundManager.Instance;
        if (rm == null) return;

        Vector3 catchPosition = other.transform.position;

        // Power-up gems short-circuit variant routing — no points, no combo change.
        if (fo.isPowerUp)
        {
            HandlePowerUpCatch(fo.powerUpType, catchPosition);
            other.gameObject.SetActive(false);
            return;
        }

        SpecialGemType variant = fo.specialType;
        HandleVariantCatch(variant, catchPosition, other.gameObject);

        other.gameObject.SetActive(false);
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
