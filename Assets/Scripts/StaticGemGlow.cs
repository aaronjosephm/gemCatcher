using UnityEngine;

/// <summary>
/// Adds a soft radial glow halo behind a static decorative gem (e.g., gems
/// embedded in menu rocks). Uses the same procedural gradient as GemGlowVolume
/// but without any FallingObject dependency.
///
/// Attach to each decorative gem GameObject and set glowColor in the Inspector.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class StaticGemGlow : MonoBehaviour
{
    [Header("Glow Settings")]
    [Tooltip("Color of the glow halo.")]
    public Color glowColor = Color.green;

    [Tooltip("Radius of the glow halo in world units.")]
    public float glowRadius = 0.45f;

    [Tooltip("Opacity of the glow at center.")]
    [Range(0f, 1f)]
    public float glowAlpha = 0.75f;

    [Tooltip("Z offset behind the gem (positive = further from camera).")]
    public float zOffset = 0.05f;

    [Header("Pulse Animation")]
    [Tooltip("How much the radius oscillates (±). 0 = no pulse.")]
    public float pulseAmount = 0.15f;

    [Tooltip("Pulse speed in cycles per second.")]
    public float pulseSpeed = 0.8f;

    [Tooltip("Random time offset so gems don't pulse in sync.")]
    public bool randomizePhase = true;

    private GameObject glowQuad;
    private Material glowMat;
    private float pulsePhase;
    private static Texture2D s_glowTexture;
    private static Mesh s_quadMesh;

    void OnEnable()
    {
        if (randomizePhase)
            pulsePhase = Random.Range(0f, Mathf.PI * 2f);

        if (glowQuad == null)
            CreateGlow();
        else
            glowQuad.SetActive(true);
    }

    void OnDisable()
    {
        if (glowQuad != null) glowQuad.SetActive(false);
    }

    void OnDestroy()
    {
        if (glowQuad != null)
        {
            if (Application.isPlaying)
                Destroy(glowQuad);
            else
                DestroyImmediate(glowQuad);
        }
    }

    void LateUpdate()
    {
        if (glowQuad == null) return;

        float pulse = Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f + pulsePhase) * pulseAmount;
        float currentRadius = glowRadius + pulse;

        glowQuad.transform.position = transform.position + new Vector3(0f, 0f, zOffset);
        glowQuad.transform.localScale = Vector3.one * currentRadius * 2f;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Live-update in editor when tweaking Inspector values.
        if (glowQuad != null)
        {
            glowQuad.transform.localScale = Vector3.one * glowRadius * 2f;
            if (glowMat != null)
            {
                Color c = glowColor;
                c.a = glowAlpha;
                glowMat.SetColor("_BaseColor", c);
            }
        }
    }
#endif

    void CreateGlow()
    {
        glowQuad = new GameObject("StaticGlowHalo");
        glowQuad.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        glowQuad.transform.position = transform.position + new Vector3(0f, 0f, zOffset);
        glowQuad.transform.localScale = Vector3.one * glowRadius * 2f;

        MeshFilter mf = glowQuad.AddComponent<MeshFilter>();
        mf.sharedMesh = GetQuadMesh();

        MeshRenderer mr = glowQuad.AddComponent<MeshRenderer>();
        glowMat = CreateGlowMaterial();
        mr.sharedMaterial = glowMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    Material CreateGlowMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        Material mat = new Material(shader);

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;

        Color c = glowColor;
        c.a = glowAlpha;
        mat.SetColor("_BaseColor", c);
        mat.mainTexture = GetGlowTexture();

        return mat;
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
        s_quadMesh = new Mesh { name = "StaticGlowQuad" };
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
