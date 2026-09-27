using UnityEngine;

/// <summary>
/// Rush tier rewards. Uses normal catch routing, so each remotely caught gem
/// is retired once and awards the same points/combo as a contact catch.
/// </summary>
[RequireComponent(typeof(CatchZone))]
public sealed class ComboLightning : MonoBehaviour
{
    public static ComboLightning Instance { get; private set; }
    public int Tier { get; private set; }
    public float Remaining { get; private set; }
    public bool Active => Tier > 0 && Remaining > 0f;
    public static float DurationForTier(int tier) => 5f + 3f * (Mathf.Clamp(tier, 1, 4) - 1);
    public static float RangeInColumns(int tier) => 0.75f + 0.25f * (Mathf.Clamp(tier, 1, 4) - 1);

    private CatchZone catchZone;
    private BoxCollider body;
    private float zapTimer;
    private float rippleTimer;
    private float rippleAngle;

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
        if (GameState.Mode != GameState.GameMode.Rush || !GameState.IsPlaying) return;
        Tier = count >= 30 ? 4 : count >= 20 ? 3 : count >= 10 ? 2 : 1;
        Remaining = DurationForTier(Tier); // Upgrade replaces, rather than stacks, the timer.
        zapTimer = 0.12f;
        rippleTimer = 0f;
        Ripple(true);
        CatchBurst.Spawn(Origin, new Color(0.45f, 0.85f, 1f));
    }

    void OnComboBroken(int count, float multiplier) => Cancel();

    public void Cancel()
    {
        Tier = 0;
        Remaining = 0f;
        LightningSpawnEffect.ClearComboEffects();
    }

    Vector3 Origin => body != null
        ? new Vector3(body.bounds.center.x, body.bounds.center.y, body.bounds.min.z - 0.1f)
        : transform.position + Vector3.back * 0.5f;

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
            Cancel();
            if (finalTier)
            {
                ComboManager.CompleteChargeCycle();
                UIManager.Instance?.SpawnBannerNotification("CHARGE COMPLETE!", new Color(0.4f, 0.9f, 1f));
            }
            return;
        }

        rippleTimer -= Time.deltaTime;
        if (rippleTimer <= 0f)
        {
            rippleTimer = 0.16f;
            Ripple(false);
        }
        zapTimer -= Time.deltaTime;
        if (zapTimer > 0f) return;
        zapTimer = Mathf.Lerp(0.48f, 0.24f, (Tier - 1) / 3f);
        ZapNearestGem();
    }

    void Ripple(bool audible)
    {
        Vector3 center = Origin;
        float radius = (body != null ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.y) : 0.4f)
            + 0.12f + Tier * 0.07f;
        rippleAngle += 0.7f;
        for (int i = 0; i < 3; i++)
        {
            float angle = rippleAngle + i * Mathf.PI * 2f / 3f;
            Vector3 from = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            Vector3 to = center + new Vector3(Mathf.Cos(angle + 1.5f), Mathf.Sin(angle + 1.5f), 0f) * radius;
            LightningSpawnEffect.Arc(from, to, 0.045f + Tier * 0.015f, audible && i == 0, transform);
        }
    }

    void ZapNearestGem()
    {
        float columnWidth = RushColumns.ColumnWidth;
        float range = columnWidth * RangeInColumns(Tier);
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
        if (nearest == null) return;
        Vector3 target = nearest.transform.position;
        // Tier can change during the catch; use the current tier for this bolt.
        float width = 0.08f + Tier * 0.025f;
        if (catchZone.TryLightningCatch(nearest))
            LightningSpawnEffect.Arc(Origin, new Vector3(target.x, target.y, Origin.z), width, true);
    }

    void OnDestroy()
    {
        ComboManager.OnComboTierUp -= Charge;
        ComboManager.OnComboBroken -= OnComboBroken;
        Cancel();
        if (Instance == this) Instance = null;
    }
}
