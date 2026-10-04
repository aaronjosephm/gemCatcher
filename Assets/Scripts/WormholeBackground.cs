using UnityEngine;

/// <summary>Procedural full-screen tunnel, with no scrolling/stretching background texture.</summary>
[DisallowMultipleComponent]
public sealed class WormholeBackground : MonoBehaviour
{
    private MeshRenderer backdrop;
    private Material originalMaterial;
    private Material wormholeMaterial;
    private float animationTime;
    private Camera backgroundCamera;
    private CameraClearFlags originalClearFlags;
    private Color originalCameraColor;
    private bool cameraConfigured;
    private static readonly int AnimationTime = Shader.PropertyToID("_AnimationTime");

    public void Apply()
    {
        if (backdrop == null) backdrop = GetComponent<MeshRenderer>();
        if (backdrop == null) return;
        if (!cameraConfigured)
        {
            backgroundCamera = Camera.main;
            if (backgroundCamera != null)
            {
                originalClearFlags = backgroundCamera.clearFlags;
                originalCameraColor = backgroundCamera.backgroundColor;
                cameraConfigured = true;
                backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
                backgroundCamera.backgroundColor = Color.black;
            }
        }
        if (wormholeMaterial == null)
        {
            // Loading from Resources also retains the shader in player builds.
            Shader shader = Resources.Load<Shader>("Shaders/Wormhole");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError("[WormholeBackground] Wormhole shader missing or unsupported.", this);
                return;
            }
            originalMaterial = backdrop.sharedMaterial;
            wormholeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        }
        backdrop.sharedMaterial = wormholeMaterial;
        enabled = true;
    }

    void LateUpdate()
    {
        if (wormholeMaterial == null || backdrop == null) return;
        // Animate previews as well as gameplay; scaled time still respects pause.
        if (!GemCatcher.IsGameOver)
            animationTime += Time.deltaTime;
        wormholeMaterial.SetFloat(AnimationTime, animationTime);
        backdrop.sharedMaterial = wormholeMaterial;
    }

    void OnDisable()
    {
        if (cameraConfigured && backgroundCamera != null)
        {
            backgroundCamera.clearFlags = originalClearFlags;
            backgroundCamera.backgroundColor = originalCameraColor;
        }
        cameraConfigured = false;
        if (backdrop != null && backdrop.sharedMaterial == wormholeMaterial)
            backdrop.sharedMaterial = originalMaterial;
    }

    void OnDestroy()
    {
        if (wormholeMaterial != null) Destroy(wormholeMaterial);
    }
}
