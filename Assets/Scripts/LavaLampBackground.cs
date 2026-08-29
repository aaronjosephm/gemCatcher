using UnityEngine;

/// <summary>
/// Procedural lava lamp background. Updates a low-res texture each frame with
/// drifting color blobs, applied to a fullscreen quad using URP/Unlit shader.
/// </summary>
public class LavaLampBackground : MonoBehaviour
{
    private const int TexWidth = 128;
    private const int TexHeight = 128;
    private const float Speed = 0.3f;

    private Texture2D tex;
    private Color32[] pixels;
    private Material material;
    private GameObject quad;

    void Start()
    {
        tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGB24, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        pixels = new Color32[TexWidth * TexHeight];

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        material = new Material(shader);
        material.SetTexture("_BaseMap", tex);
        material.mainTexture = tex;

        // Create fullscreen quad
        quad = new GameObject("LavaLampQuad");
        MeshFilter mf = quad.AddComponent<MeshFilter>();
        mf.sharedMesh = MakeQuadMesh();
        MeshRenderer mr = quad.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Camera cam = Camera.main;
        if (cam != null)
        {
            float height = cam.orthographicSize * 2f;
            float width = height * cam.aspect;
            quad.transform.position = new Vector3(
                cam.transform.position.x,
                cam.transform.position.y,
                cam.transform.position.z + 50f);
            quad.transform.localScale = new Vector3(width * 1.2f, height * 1.2f, 1f);
        }

        UpdateTexture(0f);
    }

    void Update()
    {
        UpdateTexture(Time.time);
    }

    void UpdateTexture(float time)
    {
        float t = time * Speed;

        // Blob centers
        float c1x = 0.5f + 0.3f * Mathf.Sin(t * 0.7f + 1f);
        float c1y = 0.5f + 0.35f * Mathf.Cos(t * 0.5f);
        float c2x = 0.5f + 0.35f * Mathf.Cos(t * 0.6f + 2f);
        float c2y = 0.5f + 0.3f * Mathf.Sin(t * 0.8f + 1.5f);
        float c3x = 0.5f + 0.25f * Mathf.Sin(t * 0.9f + 3f);
        float c3y = 0.5f + 0.4f * Mathf.Cos(t * 0.4f + 0.5f);
        float c4x = 0.5f + 0.4f * Mathf.Cos(t * 0.5f + 4f);
        float c4y = 0.5f + 0.25f * Mathf.Sin(t * 0.7f + 2.5f);
        float c5x = 0.5f + 0.3f * Mathf.Sin(t * 0.35f + 5f);
        float c5y = 0.5f + 0.3f * Mathf.Cos(t * 0.55f + 3.5f);

        // Pulsing radii
        float r1 = 0.28f + 0.08f * Mathf.Sin(t * 1.1f);
        float r2 = 0.25f + 0.07f * Mathf.Cos(t * 0.9f + 1f);
        float r3 = 0.22f + 0.06f * Mathf.Sin(t * 1.3f + 2f);
        float r4 = 0.20f + 0.05f * Mathf.Cos(t * 1f + 3f);
        float r5 = 0.26f + 0.07f * Mathf.Sin(t * 0.8f + 1.5f);

        // Cycling colors
        float col1r = 0.5f + 0.5f * Mathf.Sin(t * 0.3f);
        float col1g = 0.2f + 0.3f * Mathf.Sin(t * 0.4f + 2f);
        float col1b = 0.8f + 0.2f * Mathf.Cos(t * 0.35f + 1f);

        float col2r = 0.9f + 0.1f * Mathf.Sin(t * 0.25f + 1f);
        float col2g = 0.2f + 0.2f * Mathf.Cos(t * 0.3f + 3f);
        float col2b = 0.3f + 0.2f * Mathf.Sin(t * 0.4f + 2f);

        float col3r = 0.1f + 0.2f * Mathf.Cos(t * 0.35f + 2f);
        float col3g = 0.7f + 0.3f * Mathf.Sin(t * 0.3f + 1f);
        float col3b = 0.4f + 0.3f * Mathf.Cos(t * 0.4f);

        float col4r = 0.9f + 0.1f * Mathf.Sin(t * 0.2f);
        float col4g = 0.6f + 0.3f * Mathf.Cos(t * 0.35f + 1.5f);
        float col4b = 0.1f + 0.1f * Mathf.Sin(t * 0.3f + 3f);

        float col5r = 0.6f + 0.3f * Mathf.Sin(t * 0.28f + 4f);
        float col5g = 0.1f + 0.2f * Mathf.Cos(t * 0.32f + 2f);
        float col5b = 0.8f + 0.2f * Mathf.Sin(t * 0.38f + 1f);

        float invW = 1f / TexWidth;
        float invH = 1f / TexHeight;

        for (int y = 0; y < TexHeight; y++)
        {
            float uy = y * invH;
            int row = y * TexWidth;
            for (int x = 0; x < TexWidth; x++)
            {
                float ux = x * invW;

                float b1 = Blob(ux, uy, c1x, c1y, r1);
                float b2 = Blob(ux, uy, c2x, c2y, r2);
                float b3 = Blob(ux, uy, c3x, c3y, r3);
                float b4 = Blob(ux, uy, c4x, c4y, r4);
                float b5 = Blob(ux, uy, c5x, c5y, r5);

                float r = 0.02f + col1r * b1 * 0.7f + col2r * b2 * 0.6f + col3r * b3 * 0.5f + col4r * b4 * 0.6f + col5r * b5 * 0.5f;
                float g = 0.01f + col1g * b1 * 0.7f + col2g * b2 * 0.6f + col3g * b3 * 0.5f + col4g * b4 * 0.6f + col5g * b5 * 0.5f;
                float bl = 0.04f + col1b * b1 * 0.7f + col2b * b2 * 0.6f + col3b * b3 * 0.5f + col4b * b4 * 0.6f + col5b * b5 * 0.5f;

                pixels[row + x] = new Color32(
                    (byte)(Mathf.Clamp01(r) * 255),
                    (byte)(Mathf.Clamp01(g) * 255),
                    (byte)(Mathf.Clamp01(bl) * 255),
                    255);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    static float Blob(float ux, float uy, float cx, float cy, float radius)
    {
        float dx = ux - cx;
        float dy = uy - cy;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        float edge = radius * 0.1f;
        if (d >= radius) return 0f;
        if (d <= edge) return 1f;
        return (radius - d) / (radius - edge);
    }

    static Mesh MakeQuadMesh()
    {
        Mesh m = new Mesh { name = "LavaLampQuad" };
        m.vertices = new[] {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        };
        m.uv = new[] {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateNormals();
        return m;
    }

    void OnDestroy()
    {
        if (quad != null) Destroy(quad);
        if (material != null) Destroy(material);
        if (tex != null) Destroy(tex);
    }
}
