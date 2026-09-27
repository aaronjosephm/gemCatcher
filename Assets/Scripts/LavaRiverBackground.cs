using UnityEngine;

/// <summary>Runs the masked lava flow on the fitted backdrop using paused gameplay time.</summary>
[DisallowMultipleComponent]
public sealed class LavaRiverBackground : MonoBehaviour
{
    private static readonly int FlowTimeId = Shader.PropertyToID("_FlowTime");
    private MeshRenderer backdrop;
    private Material originalMaterial;
    private Material riverMaterial;
    private float flowTime;
    private MaterialPropertyBlock flowProperties;

    public void Apply(Texture2D texture)
    {
        if (texture == null) return;
        if (backdrop == null) backdrop = GetComponent<MeshRenderer>();
        if (backdrop == null) return;
        if (riverMaterial == null)
        {
            // Referenced material keeps the shader included in mobile builds.
            Material template = Resources.Load<Material>("Materials/MoltenRiver");
            if (template == null) return;
            originalMaterial = backdrop.sharedMaterial;
            riverMaterial = new Material(template) { hideFlags = HideFlags.DontSave };
        }
        // Match the shared plane's upright UV orientation, including authored flips.
        if (originalMaterial != null)
        {
            riverMaterial.mainTextureScale = originalMaterial.mainTextureScale;
            riverMaterial.mainTextureOffset = originalMaterial.mainTextureOffset;
        }
        riverMaterial.mainTexture = texture;
        riverMaterial.SetFloat(FlowTimeId, flowTime);
        backdrop.sharedMaterial = riverMaterial;
        enabled = true;
    }

    void Update()
    {
        if (riverMaterial == null || !GameState.IsPlaying || GemCatcher.IsGameOver || Time.timeScale <= 0f) return;
        flowTime += Time.deltaTime;
        // Write to the renderer too, so a material instance created by another
        // background component cannot silently detach the animation clock.
        if (flowProperties == null) flowProperties = new MaterialPropertyBlock();
        backdrop.GetPropertyBlock(flowProperties);
        flowProperties.SetFloat(FlowTimeId, flowTime);
        backdrop.SetPropertyBlock(flowProperties);
        riverMaterial.SetFloat(FlowTimeId, flowTime);
    }

    void OnDisable()
    {
        if (backdrop != null && flowProperties != null)
        {
            backdrop.GetPropertyBlock(flowProperties);
            flowProperties.SetFloat(FlowTimeId, 0f);
            backdrop.SetPropertyBlock(flowProperties);
        }
        if (backdrop != null && backdrop.sharedMaterial == riverMaterial)
            backdrop.sharedMaterial = originalMaterial;
    }

    void OnDestroy()
    {
        if (riverMaterial != null) Destroy(riverMaterial);
    }
}
