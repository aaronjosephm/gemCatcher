using UnityEngine;

/// <summary>
/// Procedural sunset/twilight sky with city skyline silhouette.
/// Renders a warm gradient (orange horizon → deep purple sky) with
/// a dark city skyline at the bottom and twinkling stars up top.
/// </summary>
public class LavaLampBackground : MonoBehaviour
{
    private const int TexWidth = 256;
    private const int TexHeight = 256;

    private Texture2D tex;
    private Color32[] pixels;
    private Material material;
    private GameObject quad;
    private float[] starX;
    private float[] starY;
    private float[] starPhase;
    private int starCount = 40;

    // Skyline heights per column (0..1 range, fraction of screen)
    private float[] skyline;

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

        quad = new GameObject("TwilightSkyQuad");
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

        GenerateSkyline();
        GenerateStars();
        UpdateTexture(0f);
    }

    void GenerateSkyline()
    {
        skyline = new float[TexWidth];
        float baseHeight = 0.18f;

        // Generate buildings as blocks
        int x = 0;
        while (x < TexWidth)
        {
            int buildingWidth = Random.Range(3, 12);
            float buildingHeight = baseHeight + Random.Range(0.02f, 0.22f);

            // Occasional tall skyscraper
            if (Random.value < 0.15f)
                buildingHeight = baseHeight + Random.Range(0.18f, 0.32f);

            for (int bx = 0; bx < buildingWidth && x + bx < TexWidth; bx++)
                skyline[x + bx] = buildingHeight;

            x += buildingWidth;

            // Gap between buildings
            int gap = Random.Range(1, 3);
            for (int gx = 0; gx < gap && x + gx < TexWidth; gx++)
                skyline[x + gx] = baseHeight;
            x += gap;
        }
    }

    void GenerateStars()
    {
        starX = new float[starCount];
        starY = new float[starCount];
        starPhase = new float[starCount];
        for (int i = 0; i < starCount; i++)
        {
            starX[i] = Random.Range(0f, 1f);
            starY[i] = Random.Range(0.55f, 0.97f); // upper sky only
            starPhase[i] = Random.Range(0f, Mathf.PI * 2f);
        }
    }

    void Update()
    {
        UpdateTexture(Time.time);
    }

    void UpdateTexture(float time)
    {
        float invW = 1f / TexWidth;
        float invH = 1f / TexHeight;

        // Sky gradient colors (subtle time shift for living feel)
        float ts = time * 0.05f;
        Color horizonColor = new Color(
            0.95f + 0.05f * Mathf.Sin(ts),
            0.45f + 0.1f * Mathf.Sin(ts * 0.7f + 1f),
            0.15f + 0.05f * Mathf.Cos(ts * 0.5f));
        Color midColor = new Color(
            0.6f + 0.1f * Mathf.Sin(ts * 0.8f + 2f),
            0.2f + 0.05f * Mathf.Cos(ts * 0.6f),
            0.45f + 0.1f * Mathf.Sin(ts * 0.4f + 1f));
        Color topColor = new Color(
            0.08f + 0.03f * Mathf.Sin(ts * 0.3f),
            0.02f + 0.02f * Mathf.Cos(ts * 0.4f + 1f),
            0.18f + 0.05f * Mathf.Sin(ts * 0.5f + 2f));

        // Skyline silhouette color (dark with hint of warm light)
        Color buildingColor = new Color(0.03f, 0.02f, 0.05f);
        // Window lights color
        Color windowColor = new Color(0.9f, 0.75f, 0.4f);

        for (int y = 0; y < TexHeight; y++)
        {
            float uy = y * invH;
            int row = y * TexWidth;

            // Three-stop gradient: horizon(0) → mid(0.35) → top(1)
            Color skyColor;
            if (uy < 0.35f)
            {
                float blend = uy / 0.35f;
                skyColor = Color.Lerp(horizonColor, midColor, blend);
            }
            else
            {
                float blend = (uy - 0.35f) / 0.65f;
                skyColor = Color.Lerp(midColor, topColor, blend);
            }

            for (int x = 0; x < TexWidth; x++)
            {
                float ux = x * invW;

                // Check if pixel is below skyline
                if (uy < skyline[x])
                {
                    // Building silhouette with occasional window lights
                    bool isWindow = false;
                    if (uy > 0.05f && uy < skyline[x] - 0.02f)
                    {
                        int wx = x % 3;
                        int wy = Mathf.FloorToInt(uy * 80f) % 4;
                        if (wx == 1 && wy < 2)
                        {
                            // Randomly lit windows (use position as seed)
                            float hash = Mathf.Abs(Mathf.Sin(x * 12.9898f + Mathf.Floor(uy * 80f) * 78.233f) * 43758.5453f);
                            hash -= Mathf.Floor(hash);
                            // Some windows flicker
                            float flicker = 0.5f + 0.5f * Mathf.Sin(time * 2f + x * 3f + y * 7f);
                            if (hash < 0.35f && flicker > 0.3f)
                                isWindow = true;
                        }
                    }

                    if (isWindow)
                    {
                        float brightness = 0.5f + 0.5f * Mathf.Sin(time * 1.5f + x * 0.5f);
                        Color wc = Color.Lerp(buildingColor, windowColor, brightness * 0.6f);
                        pixels[row + x] = ToColor32(wc);
                    }
                    else
                    {
                        pixels[row + x] = ToColor32(buildingColor);
                    }
                }
                else
                {
                    pixels[row + x] = ToColor32(skyColor);
                }
            }
        }

        // Stars (twinkling)
        for (int i = 0; i < starCount; i++)
        {
            int sx = Mathf.Clamp(Mathf.RoundToInt(starX[i] * (TexWidth - 1)), 0, TexWidth - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(starY[i] * (TexHeight - 1)), 0, TexHeight - 1);
            if (sy * invH >= skyline[sx])
            {
                float twinkle = 0.4f + 0.6f * Mathf.Sin(time * 2f + starPhase[i]);
                twinkle = Mathf.Clamp01(twinkle);
                byte b = (byte)(twinkle * 255);
                pixels[sy * TexWidth + sx] = new Color32(b, b, (byte)(b * 0.9f), 255);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    static Color32 ToColor32(Color c)
    {
        return new Color32(
            (byte)(Mathf.Clamp01(c.r) * 255),
            (byte)(Mathf.Clamp01(c.g) * 255),
            (byte)(Mathf.Clamp01(c.b) * 255), 255);
    }

    static Mesh MakeQuadMesh()
    {
        Mesh m = new Mesh { name = "TwilightQuad" };
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
