using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Staggered crystal glints behind gameplay, concentrated on the cave edges.</summary>
[DisallowMultipleComponent]
public sealed class DiamondCaveAmbience : MonoBehaviour
{
    private const int Count = 40;
    private struct Glint
    {
        public Vector2 viewport;
        public float age, duration, size, angle;
    }
    private readonly Glint[] glints = new Glint[Count];
    private readonly Vector3[] vertices = new Vector3[Count * 4];
    private readonly Color[] colors = new Color[Count * 4];
    private readonly System.Random random = new System.Random();
    private Mesh mesh;
    private Material material;
    private Camera view;
    private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    void Start()
    {
        view = Camera.main;
        Shader shader = Resources.Load<Shader>("Shaders/CrystalTwinkle");
        if (view == null || shader == null)
        {
            Debug.LogWarning("[DiamondCaveAmbience] Camera or twinkle shader missing.", this);
            enabled = false; return;
        }
        material = new Material(shader) { name = "Crystal twinkles", hideFlags = HideFlags.DontSave };
        mesh = new Mesh { name = "Cave glints", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var triangles = new int[Count * 6];
        var uv = new Vector2[Count * 4];
        for (int i = 0; i < Count; i++)
        {
            Renew(i); glints[i].age = Range(0f, glints[i].duration);
            int v = i * 4, t = i * 6;
            uv[v] = Vector2.zero; uv[v + 1] = Vector2.up;
            uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.right;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        Draw(); mesh.triangles = triangles; mesh.uv = uv;
    }

    void Renew(int i)
    {
        // Keep the central falling lanes clear; crystals line the image's sides.
        float x = Range(.035f, .23f);
        if (random.Next(2) == 0) x = 1f - x;
        glints[i] = new Glint
        {
            viewport = new Vector2(x, Range(.035f, .96f)),
            age = 0f, duration = Range(2.8f, 6.5f),
            size = Range(.055f, .14f), angle = Range(-.25f, .25f)
        };
    }

    void Update()
    {
        if (mesh == null || view == null || !GameState.IsPlaying || GemCatcher.IsGameOver || Time.timeScale <= 0f) return;
        for (int i = 0; i < Count; i++)
        {
            glints[i].age += Time.deltaTime;
            if (glints[i].age >= glints[i].duration) Renew(i);
        }
        Draw();
    }

    void Draw()
    {
        float halfHeight = view.orthographicSize, halfWidth = halfHeight * view.aspect;
        for (int i = 0; i < Count; i++)
        {
            Glint g = glints[i];
            float phase = g.age / g.duration;
            // A smooth single glint followed by darkness, never a harsh flashing overlay.
            float pulse = phase < .65f ? Mathf.Pow(Mathf.Sin(phase / .65f * Mathf.PI), 3f) : 0f;
            Vector3 p = new Vector3(view.transform.position.x + (g.viewport.x * 2f - 1f) * halfWidth,
                view.transform.position.y + (g.viewport.y * 2f - 1f) * halfHeight, 1.2f);
            float size = g.size * Mathf.Lerp(.65f, 1f, pulse);
            Vector3 along = new Vector3(Mathf.Cos(g.angle), Mathf.Sin(g.angle), 0f) * size;
            Vector3 across = new Vector3(-Mathf.Sin(g.angle), Mathf.Cos(g.angle), 0f) * size;
            int v = i * 4;
            vertices[v] = transform.InverseTransformPoint(p - along - across);
            vertices[v + 1] = transform.InverseTransformPoint(p - along + across);
            vertices[v + 2] = transform.InverseTransformPoint(p + along + across);
            vertices[v + 3] = transform.InverseTransformPoint(p + along - across);
            for (int n = 0; n < 4; n++) colors[v + n] = new Color(.8f, .93f, 1f, pulse * .85f);
        }
        mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
