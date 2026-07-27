using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Bootstraps a subtle URP Bloom effect at runtime. The bloom threshold is
/// set high enough (default 1.0) that only HDR-bright surfaces glow -- in
/// practice, only the gem shader's additive pass exceeds this, so the
/// catcher and background stay unaffected.
///
/// Attach to any GameObject or let the RuntimeInitializeOnLoadMethod
/// auto-create it.
/// </summary>
[DisallowMultipleComponent]
public class GemGlowVolume : MonoBehaviour
{
    [Header("Bloom Settings")]
    [Tooltip("Minimum brightness before bloom kicks in. 1.0 means only HDR pixels glow.")]
    public float threshold = 0.2f;

    [Tooltip("Bloom intensity. Keep low for a subtle gem glow.")]
    public float intensity = 3.0f;

    [Tooltip("How far the glow spreads. Higher = softer, wider glow.")]
    [Range(1, 10)]
    public float scatter = 7f;

    private Volume volume;
    private VolumeProfile profile;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInstance()
    {
        if (FindAnyObjectByType<GemGlowVolume>() != null) return;

        GameObject go = new GameObject("GemGlowVolume (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<GemGlowVolume>();
    }

    void Awake()
    {
        // Ensure the main camera has post-processing and HDR enabled.
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.allowHDR = true;

            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
                camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
        }

        // Create a runtime Volume Profile so we don't need an asset on disk.
        profile = ScriptableObject.CreateInstance<VolumeProfile>();

        Bloom bloom = profile.Add<Bloom>(overrides: true);
        bloom.threshold.value      = threshold;
        bloom.intensity.value      = intensity;
        bloom.scatter.value        = scatter;
        bloom.threshold.overrideState = true;
        bloom.intensity.overrideState = true;
        bloom.scatter.overrideState   = true;

        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10;
        volume.profile  = profile;
    }

    void OnDestroy()
    {
        if (profile != null)
        {
            DestroyImmediate(profile);
        }
    }
}
