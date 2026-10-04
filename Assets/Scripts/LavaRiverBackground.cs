using UnityEngine;

/// <summary>Binds the river material; the shader uses Unity scaled time so pause still freezes it.</summary>
[DisallowMultipleComponent]
public sealed class LavaRiverBackground : MonoBehaviour
{
    private MeshRenderer backdrop;
    private Material originalMaterial;
    private Material riverMaterial;

    public void Apply(Texture2D texture)
    {
        if (texture == null) return;
        if (backdrop == null) backdrop = GetComponent<MeshRenderer>();
        if (backdrop == null) return;
        if (riverMaterial == null)
        {
            // Referenced material keeps the shader included in mobile builds.
            Material template = Resources.Load<Material>("Materials/MoltenRiver");
            if (template == null || template.shader == null || !template.shader.isSupported)
            {
                Debug.LogError("[LavaRiverBackground] MoltenRiver material/shader is missing or unsupported. Check shader compilation errors.", this);
                return;
            }
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
        backdrop.sharedMaterial = riverMaterial;
        enabled = true;
    }

    void LateUpdate()
    {
        // Keep background setup/preview code from leaving a static material on the plane.
        if (riverMaterial != null && backdrop != null && backdrop.sharedMaterial != riverMaterial)
            backdrop.sharedMaterial = riverMaterial;
    }

    void OnDisable()
    {
        if (backdrop != null && backdrop.sharedMaterial == riverMaterial)
            backdrop.sharedMaterial = originalMaterial;
    }

    void OnDestroy()
    {
        if (riverMaterial != null) Destroy(riverMaterial);
    }
}
