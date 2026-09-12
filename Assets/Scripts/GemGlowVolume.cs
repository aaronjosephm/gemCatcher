using UnityEngine;

/// <summary>
/// Adds a localized glow halo around a falling gem. Attach to each gem
/// prefab or let FallingObject add it at runtime. The glow is a procedural
/// soft radial gradient quad that sits behind the gem and tints to the
/// gem's burst color.
///
/// This replaces the old global URP Bloom approach which caused a full-screen
/// haze. Per-gem sprites give a tight, controlled glow with zero impact on
/// the rest of the scene.
/// </summary>
[DisallowMultipleComponent]
public class GemGlowVolume : MonoBehaviour
{
    [Header("Glow Settings")]
    [Tooltip("Radius of the glow halo in world units.")]
    public float glowRadius = 0.9f;

    [Tooltip("Opacity of the glow at center.")]
    [Range(0f, 1f)]
    public float glowAlpha = 0.85f;

    [Tooltip("Color override. If left white, auto-detects from gem name.")]
    public Color glowColor = Color.white;

    private GameObject glowQuad;
    private MeshRenderer glowRenderer;
    private static Texture2D s_glowTexture;
    private static Mesh s_quadMesh;
    private static Material s_glowMaterial;
    private static MaterialPropertyBlock s_colorProperties;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    void Start()
    {
        // Auto-detect color from gem name if not overridden.
        if (glowColor == Color.white)
        {
            var falling = GetComponent<FallingObject>();
            if (falling != null)
            {
                glowColor = falling.GetBurstColor();
            }
        }

        glowQuad = new GameObject("GlowHalo");
        // Don't parent to the gem — we follow position only so the halo
        // stays camera-facing and doesn't spin with the gem's rotation.
        glowQuad.transform.position = transform.position + new Vector3(0f, 0f, 0.1f);
        glowQuad.transform.localScale = Vector3.one * glowRadius * 2f;

        MeshFilter mf = glowQuad.AddComponent<MeshFilter>();
        mf.sharedMesh = GetQuadMesh();

        glowRenderer = glowQuad.AddComponent<MeshRenderer>();
        glowRenderer.sharedMaterial = GetGlowMaterial();
        glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;
        ApplyColor();

        // Hide immediately if this is a bomb — Start() runs after SetActive(true)
        // so OnEnable's check would have seen glowQuad as null and done nothing.
        var fo = GetComponent<FallingObject>();
        if (fo != null && fo.specialType == SpecialGemType.Bomb)
        {
            glowQuad.SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (glowQuad != null)
        {
            glowQuad.transform.position = transform.position + new Vector3(0f, 0f, 0.1f);
        }
    }

    void OnEnable()
    {
        // Don't show the glow if the component was disabled (e.g. for bombs).
        if (glowQuad != null && enabled)
        {
            var falling = GetComponent<FallingObject>();
            bool isBomb = falling != null && falling.specialType == SpecialGemType.Bomb;
            glowQuad.SetActive(!isBomb);
        }
    }

    void OnDisable()
    {
        if (glowQuad != null) glowQuad.SetActive(false);
    }

    void OnDestroy()
    {
        if (glowQuad != null) Destroy(glowQuad);
    }

    /// <summary>
    /// Call after a gem is recycled/recolored to update the halo tint.
    /// </summary>
    public void RefreshColor(Color c)
    {
        glowColor = c;
        ApplyColor();
    }

    void ApplyColor()
    {
        if (glowRenderer == null) return;

        if (s_colorProperties == null)
            s_colorProperties = new MaterialPropertyBlock();

        Color c = glowColor;
        c.a = glowAlpha;
        s_colorProperties.Clear();
        s_colorProperties.SetColor(BaseColorId, c);
        s_colorProperties.SetColor(ColorId, c);
        glowRenderer.SetPropertyBlock(s_colorProperties);
    }

    static Material GetGlowMaterial()
    {
        if (s_glowMaterial != null) return s_glowMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        s_glowMaterial = new Material(shader)
        {
            name = "Gem Glow Shared Material",
            hideFlags = HideFlags.DontSave,
        };

        s_glowMaterial.SetFloat("_Surface", 1f);
        s_glowMaterial.SetFloat("_Blend", 0f);
        s_glowMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        s_glowMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        s_glowMaterial.SetFloat("_ZWrite", 0f);
        s_glowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        s_glowMaterial.renderQueue = 3000;
        s_glowMaterial.SetColor(BaseColorId, Color.white);
        s_glowMaterial.SetColor(ColorId, Color.white);
        s_glowMaterial.mainTexture = GetGlowTexture();

        return s_glowMaterial;
    }

    static Texture2D GetGlowTexture()
    {
        if (s_glowTexture != null) return s_glowTexture;

        int size = 128;
        s_glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        s_glowTexture.filterMode = FilterMode.Bilinear;
        s_glowTexture.wrapMode = TextureWrapMode.Clamp;
        float center = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Pow(Mathf.Max(0f, 1f - dist), 2.5f);
                s_glowTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        s_glowTexture.Apply(false, true);
        return s_glowTexture;
    }

    static Mesh GetQuadMesh()
    {
        if (s_quadMesh != null) return s_quadMesh;
        s_quadMesh = new Mesh { name = "GlowQuad" };
        s_quadMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
        };
        s_quadMesh.uv = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1),
        };
        s_quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        s_quadMesh.RecalculateNormals();
        return s_quadMesh;
    }
}
