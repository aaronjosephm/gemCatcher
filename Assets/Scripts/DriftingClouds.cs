using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns soft procedural cloud wisps that drift slowly across the top
/// portion of the screen. Only active on levels that enable it (Jungle Falls).
/// Each cloud is a transparent quad with a soft radial gradient that fades
/// at the edges, giving a natural misty/cloud look.
/// </summary>
public class DriftingClouds : MonoBehaviour
{
    [Header("Cloud Settings")]
    [Tooltip("Number of cloud wisps to spawn.")]
    public int cloudCount = 6;

    [Tooltip("Vertical band where clouds appear (0=bottom, 1=top of screen).")]
    public float minViewportY = 0.55f;
    public float maxViewportY = 0.95f;

    [Tooltip("Horizontal drift speed range (world units/sec).")]
    public float minSpeed = 0.15f;
    public float maxSpeed = 0.4f;

    [Tooltip("Cloud opacity range.")]
    [Range(0f, 1f)]
    public float minAlpha = 0.15f;
    [Range(0f, 1f)]
    public float maxAlpha = 0.4f;

    [Tooltip("Cloud size range (world units).")]
    public float minSize = 2f;
    public float maxSize = 5f;

    [Tooltip("Z depth (between background and gameplay).")]
    public float cloudZ = 1.8f;

    private struct CloudWisp
    {
        public GameObject go;
        public float speed;
        public float width;
        public float yPos;
    }

    private CloudWisp[] clouds;
    private Camera cam;
    private static Texture2D s_cloudTexture;
    private static Mesh s_quadMesh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        ApplyForLevel();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyForLevel();
    }

    static void ApplyForLevel()
    {
        // Destroy any existing instance.
        var existing = Object.FindAnyObjectByType<DriftingClouds>();
        if (existing != null) Destroy(existing.gameObject);

        // Only show clouds on Jungle level.
        if (LevelManager.SelectedLevel != LevelManager.LevelId.Jungle) return;

        GameObject go = new GameObject("DriftingClouds");
        DontDestroyOnLoad(go);
        go.AddComponent<DriftingClouds>();
    }

    void Start()
    {
        cam = Camera.main;
        if (cam == null) return;

        clouds = new CloudWisp[cloudCount];
        for (int i = 0; i < cloudCount; i++)
        {
            clouds[i] = CreateCloud(randomizeX: true);
        }
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || clouds == null) return;

        float halfW = cam.orthographicSize * cam.aspect;
        float leftEdge = cam.transform.position.x - halfW;
        float rightEdge = cam.transform.position.x + halfW;

        for (int i = 0; i < clouds.Length; i++)
        {
            var c = clouds[i];
            if (c.go == null) continue;

            Vector3 pos = c.go.transform.position;
            pos.x += c.speed * Time.deltaTime;
            c.go.transform.position = pos;

            // Wrap around when fully off-screen
            if (c.speed > 0 && pos.x > rightEdge + c.width)
            {
                pos.x = leftEdge - c.width;
                c.go.transform.position = pos;
            }
            else if (c.speed < 0 && pos.x < leftEdge - c.width)
            {
                pos.x = rightEdge + c.width;
                c.go.transform.position = pos;
            }
        }
    }

    void OnDestroy()
    {
        if (clouds == null) return;
        foreach (var c in clouds)
        {
            if (c.go != null) Destroy(c.go);
        }
    }

    CloudWisp CreateCloud(bool randomizeX)
    {
        float size = Random.Range(minSize, maxSize);
        float alpha = Random.Range(minAlpha, maxAlpha);
        float speed = Random.Range(minSpeed, maxSpeed) * (Random.value > 0.5f ? 1f : -1f);
        float viewY = Random.Range(minViewportY, maxViewportY);

        // Stretch horizontally for a wispy look
        float scaleX = size * Random.Range(1.5f, 3f);
        float scaleY = size;

        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        float yPos = cam.transform.position.y - halfH + viewY * halfH * 2f;
        float xPos;
        if (randomizeX)
            xPos = cam.transform.position.x + Random.Range(-halfW, halfW);
        else
            xPos = (speed > 0)
                ? cam.transform.position.x - halfW - scaleX
                : cam.transform.position.x + halfW + scaleX;

        GameObject go = new GameObject("CloudWisp");
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(xPos, yPos, cloudZ);
        go.transform.localScale = new Vector3(scaleX, scaleY, 1f);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = GetQuadMesh();

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = CreateCloudMaterial(alpha);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        return new CloudWisp
        {
            go = go,
            speed = speed,
            width = scaleX * 0.5f,
            yPos = yPos,
        };
    }

    Material CreateCloudMaterial(float alpha)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent");
        Material mat = new Material(shader);

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 2999;

        Color c = Color.white;
        c.a = alpha;
        mat.SetColor("_BaseColor", c);
        mat.mainTexture = GetCloudTexture();

        return mat;
    }

    static Texture2D GetCloudTexture()
    {
        if (s_cloudTexture != null) return s_cloudTexture;

        // Procedural soft cloud: elliptical falloff with noise-like bumps.
        int size = 128;
        s_cloudTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        s_cloudTexture.filterMode = FilterMode.Bilinear;
        s_cloudTexture.wrapMode = TextureWrapMode.Clamp;
        float center = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                // Soft falloff with slight noise
                float a = Mathf.Pow(Mathf.Max(0f, 1f - dist), 1.8f);
                // Add subtle variation
                float noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.3f + 0.7f;
                a *= noise;
                s_cloudTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        s_cloudTexture.Apply(false, true);
        return s_cloudTexture;
    }

    static Mesh GetQuadMesh()
    {
        if (s_quadMesh != null) return s_quadMesh;
        s_quadMesh = new Mesh { name = "CloudQuad" };
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
