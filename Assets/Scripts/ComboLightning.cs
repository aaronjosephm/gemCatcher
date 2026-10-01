using UnityEngine;

/// <summary>
/// Lightning reward for Gem Rush (10 seconds) or Black Hole Ultra Rush (20 seconds). Uses normal catch routing, so each remotely caught gem
/// is retired once and awards the same points/combo as a contact catch.
/// </summary>
[RequireComponent(typeof(CatchZone))]
public sealed class ComboLightning : MonoBehaviour
{
    public static ComboLightning Instance { get; private set; }
    public int Tier { get; private set; }
    public float Remaining { get; private set; }
    public bool Active => Tier > 0 && Remaining > 0f;
    public const float GemRushDuration = 10f;
    public bool IsUltra { get; private set; }
    public float Strength => IsUltra ? 2f : 1f;
    public Color ChargeColor => IsUltra ? ComboManager.UltraRushColor : new Color(0.45f, 0.85f, 1f);
    public static float DurationForTier(int tier) => tier == 4 ? GemRushDuration * (ComboManager.IsUltraRushLevel ? 2f : 1f) : 0f;
    // Use boulder-slot spacing so reach scales with the playfield.
    public static float RangeInColumns(int tier) => 2f * Mathf.Clamp(tier, 1, 4);

    private CatchZone catchZone;
    private BoxCollider body;
    private float zapTimer;
    private float rippleTimer;

    public static float ZapIntervalForTier(int tier)
    {
        switch (Mathf.Clamp(tier, 1, 4))
        {
            case 1: return 0.5f;
            case 2: return 0.2f;
            case 3: return 0.125f;
            default: return 0.1f;
        }
    }

    void Awake()
    {
        Instance = this;
        catchZone = GetComponent<CatchZone>();
        body = GetComponent<BoxCollider>();
        ComboManager.OnComboTierUp += Charge;
        ComboManager.OnComboBroken += OnComboBroken;
    }

    void Charge(int count, float multiplier)
    {
        if (GameState.Mode != GameState.GameMode.Rush || !GameState.IsPlaying
            || !ComboManager.IsGemRush || Active) return;
        Tier = 4;
        IsUltra = ComboManager.IsUltraRush;
        Remaining = DurationForTier(Tier); // Earlier achievements grant no lightning; catches cannot refresh it.
        zapTimer = 0f;
        rippleTimer = Random.Range(0.06f, 0.13f);
        Ripple(true);
        CatchBurst.Spawn(Origin, ChargeColor);
    }

    void OnComboBroken(int count, float multiplier) => Cancel();

    public void Cancel()
    {
        IsUltra = false;
        Tier = 0;
        Remaining = 0f;
        LightningSpawnEffect.ClearComboEffects();
    }

    Vector3 Origin => body != null ? body.bounds.center : transform.position;

    void Update()
    {
        if (!Active) return;
        if (!GameState.IsPlaying || GemCatcher.IsGameOver
            || (RoundManager.Instance != null && RoundManager.Instance.HasCompletedLevel))
        {
            Cancel();
            return;
        }
        if (Time.timeScale <= 0f) return;
        Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
        if (Remaining <= 0f)
        {
            bool finalTier = Tier == 4;
            Color completedColor = ChargeColor;
            Cancel();
            if (finalTier)
            {
                ComboManager.CompleteChargeCycle();
                UIManager.Instance?.SpawnBannerNotification("CHARGE COMPLETE!", completedColor);
            }
            return;
        }

        rippleTimer -= Time.deltaTime;
        if (rippleTimer <= 0f)
        {
            rippleTimer = Random.Range(0.06f, 0.13f);
            Ripple(false);
        }
        zapTimer -= Time.deltaTime;
        if (zapTimer > 0f) return;
        // Stay ready when no gem is in range. Never burst several catches in one frame.
        if (ZapNearestGem()) zapTimer = ZapIntervalForTier(Tier) / Strength;
    }

    void Ripple(bool audible)
    {
        Vector3 center = Origin;
        float radius = (body != null ? body.bounds.extents.magnitude : 0.6f)
            + 0.12f + Tier * 0.07f;
        int count = Random.Range(2, 5);
        for (int i = 0; i < count; i++)
        {
            // Each short arc has its own orientation and sweep on the sphere.
            Vector3 start = Random.onUnitSphere;
            Vector3 tangent = Vector3.Cross(start,
                Mathf.Abs(start.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            tangent = Quaternion.AngleAxis(Random.Range(0f, 360f), start) * tangent;
            LightningSpawnEffect.SphericalArc(center, radius, start, tangent,
                Random.Range(0.4f, 1.3f), 0.035f + Tier * 0.01f,
                audible && i == 0, transform, IsUltra);
        }
    }

    bool ZapNearestGem()
    {
        float columnWidth = RushColumns.ColumnWidth;
        float range = columnWidth * RangeInColumns(Tier) * Strength;
        float nearestDistance = range * range;
        FallingObject nearest = null;
        Vector3 center = body != null ? body.bounds.center : transform.position;
        // No allocation and no mutation until after target selection.
        foreach (FallingObject gem in FallingObject.ActiveInstances)
        {
            if (!catchZone.CanLightningCatch(gem)) continue;
            Vector3 position = gem.transform.position;
            if (position.y < center.y - 0.2f || position.y > ScreenPadding.WorldTop) continue;
            float distance = ((Vector2)(position - center)).sqrMagnitude;
            if (distance > nearestDistance) continue;
            nearestDistance = distance;
            nearest = gem;
        }
        if (nearest == null) return false;
        Vector3 target = nearest.transform.position;
        // Tier can change during the catch; use the current tier for this bolt.
        float width = 0.08f + Tier * 0.025f;
        Vector3 origin = Origin;
        bool ultra = IsUltra; // Catch routing may reset the combo or finish the level.
        if (!catchZone.TryLightningCatch(nearest)) return false;
        LightningSpawnEffect.Arc(origin, target, width, true, transform, true, ultra);
        return true;
    }

    void OnDestroy()
    {
        ComboManager.OnComboTierUp -= Charge;
        ComboManager.OnComboBroken -= OnComboBroken;
        Cancel();
        if (Instance == this) Instance = null;
    }
}
