using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One pooled mesh of windblown flakes between gameplay and the snow backdrop.</summary>
[DisallowMultipleComponent]
public sealed class SnowAmbience : MonoBehaviour
{
    private const int Count = 84;
    private struct Flake
    {
        public Vector3 position;
        public float size, fallSpeed, phase, angle, spin, windResponse;
    }
    private readonly Flake[] flakes = new Flake[Count];
    private readonly Vector3[] vertices = new Vector3[Count * 12];
    private readonly Color[] colors = new Color[Count * 12];
    // Cosmetic randomness must not alter gameplay's UnityEngine.Random sequence.
    private readonly System.Random random = new System.Random();
    private Mesh mesh;
    private Material material;
    private Camera view;
    private float age;
    private float left, right, bottom, top;

    float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    void Start()
    {
        view = Camera.main;
        Shader shader = Resources.Load<Shader>("Shaders/Snowflakes");
        if (shader == null || view == null)
        {
            Debug.LogWarning("[SnowAmbience] Snow shader or gameplay camera missing.", this);
            enabled = false;
            return;
        }
        material = new Material(shader) { name = "Snowflake ambience", hideFlags = HideFlags.DontSave };
        mesh = new Mesh { name = "Windblown snow", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        var filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        int[] triangles = new int[Count * 18];
        for (int q = 0; q < Count * 3; q++)
        {
            int v = q * 4, t = q * 6;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        ReadBounds();
        for (int i = 0; i < Count; i++)
        {
            float proximity = Range(0f, 1f);
            flakes[i] = new Flake
            {
                position = new Vector3(Range(left, right), Range(bottom, top), Mathf.Lerp(1.8f, .9f, proximity)),
                size = Mathf.Lerp(.025f, .075f, proximity),
                fallSpeed = Mathf.Lerp(.18f, .50f, proximity),
                phase = Range(0f, Mathf.PI * 2f), angle = Range(0f, Mathf.PI),
                spin = Range(-.8f, .8f), windResponse = Range(.65f, 1.25f)
            };
            Color tint = new Color(.85f, .94f, 1f, Mathf.Lerp(.25f, .65f, proximity));
            for (int n = 0; n < 12; n++) colors[i * 12 + n] = tint;
        }
        WriteVertices();
        mesh.triangles = triangles;
        mesh.colors = colors;
    }

    void ReadBounds()
    {
        float h = view.orthographicSize, w = h * view.aspect;
        left = view.transform.position.x - w - .2f;
        right = view.transform.position.x + w + .2f;
        bottom = view.transform.position.y - h - .2f;
        top = view.transform.position.y + h + .2f;
    }

    void Update()
    {
        if (mesh == null || view == null || !GameState.IsPlaying || GemCatcher.IsGameOver || Time.timeScale <= 0f) return;
        float dt = Time.deltaTime;
        age += dt;
        ReadBounds();
        // Slowly changing shared gusts, with individual eddies so flakes don't move in lockstep.
        float wind = (Mathf.PerlinNoise(age * .10f, 7.3f) - .5f) * 2.2f + .20f;
        for (int i = 0; i < Count; i++)
        {
            Flake f = flakes[i];
            f.position.x += (wind * f.windResponse + Mathf.Sin(age * 1.1f + f.phase) * .18f) * dt;
            f.position.y += (-f.fallSpeed + Mathf.Cos(age * .85f + f.phase) * .12f) * dt;
            f.angle += (f.spin + wind * .3f) * dt;
            if (f.position.y < bottom)
            {
                f.position.y = top;
                f.position.x = Range(left, right);
            }
            if (f.position.y > top) f.position.y = bottom;
            if (f.position.x > right) f.position.x = left;
            if (f.position.x < left) f.position.x = right;
            flakes[i] = f;
        }
        WriteVertices();
    }

    void WriteVertices()
    {
        for (int i = 0; i < Count; i++)
        {
            Flake f = flakes[i];
            // Three crossed slender bars make a six-point snow crystal.
            for (int arm = 0; arm < 3; arm++)
            {
                float a = f.angle + arm * Mathf.PI / 3f;
                Vector3 along = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * f.size;
                Vector3 across = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f) * f.size * .11f;
                int v = i * 12 + arm * 4;
                vertices[v] = transform.InverseTransformPoint(f.position - along - across);
                vertices[v + 1] = transform.InverseTransformPoint(f.position - along + across);
                vertices[v + 2] = transform.InverseTransformPoint(f.position + along + across);
                vertices[v + 3] = transform.InverseTransformPoint(f.position + along - across);
            }
        }
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
