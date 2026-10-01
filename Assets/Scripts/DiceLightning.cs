using UnityEngine;

/// <summary>Red spherical ripples surrounding the enhanced Black Hole dice pickup.</summary>
[DisallowMultipleComponent]
public sealed class DiceLightning : MonoBehaviour
{
    private Collider body;
    private float rippleTimer;

    void Awake() { body = GetComponent<Collider>(); }
    void OnEnable() { rippleTimer = 0f; }

    void Update()
    {
        if (!GameState.IsPlaying || GemCatcher.IsGameOver || Time.timeScale <= 0f) return;
        if (RoundManager.Instance != null && RoundManager.Instance.HasCompletedLevel) return;
        rippleTimer -= Time.deltaTime;
        if (rippleTimer > 0f) return;
        rippleTimer = Random.Range(0.06f, 0.13f);
        Vector3 center = body != null ? body.bounds.center : transform.position;
        float radius = (body != null ? body.bounds.extents.magnitude : 0.65f) + 0.15f;
        int count = Random.Range(2, 5);
        for (int i = 0; i < count; i++)
        {
            Vector3 start = Random.onUnitSphere;
            Vector3 tangent = Vector3.Cross(start,
                Mathf.Abs(start.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            tangent = Quaternion.AngleAxis(Random.Range(0f, 360f), start) * tangent;
            LightningSpawnEffect.SphericalArc(center, radius, start, tangent,
                Random.Range(0.4f, 1.3f), 0.065f, false, transform, true, false);
        }
    }

    void OnDisable() { LightningSpawnEffect.ClearFollowingEffects(transform); }
}
