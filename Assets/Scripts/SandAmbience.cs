using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Soft dust wisps and small windblown grains, behind the playable plane.</summary>
[DisallowMultipleComponent]
public sealed class SandAmbience : MonoBehaviour
{
    private const int Count = 76;
    private const int GrainCount = 64;
    private struct Mote
    {
        public Vector3 position;
        public float width, height, speed, phase;
    }
    private readonly Mote[] motes = new Mote[Count];
    private readonly Vector3[] vertices = new Vector3[Count * 4];
    private readonly System.Random random = new System.Random();
    private Mesh mesh;
    private Material material;
    private Camera view;
    private float age, left, right, bottom, top;
    private float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

    void Start()
    {
        view = Camera.main;
        Shader shader = Resources.Load<Shader>("Shaders/SandDust");
        if (view == null || shader == null)
        {
            Debug.LogWarning("[SandAmbience] Gameplay camera or sand shader missing.", this);
            enabled = false;
            return;
        }
        material = new Material(shader) { name = "Desert dust", hideFlags = HideFlags.DontSave };
        mesh = new Mesh { name = "Windblown sand", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var triangles = new int[Count * 6];
        var uv = new Vector2[Count * 4];
        var colors = new Color[Count * 4];
        ReadBounds();
        for (int i = 0; i < Count; i++)
        {
            bool grain = i < GrainCount;
            motes[i] = new Mote
            {
                position = new Vector3(Range(left, right), Range(bottom, grain ? top : Mathf.Lerp(bottom, top, .65f)), grain ? 1.15f : 1.65f),
                width = grain ? Range(.025f, .065f) : Range(.55f, 1.2f),
                height = grain ? Range(.009f, .018f) : Range(.09f, .20f),
                speed = grain ? Range(.7f, 1.4f) : Range(.35f, .65f),
                phase = Range(0f, Mathf.PI * 2f)
            };
            int v = i * 4, t = i * 6;
            uv[v] = Vector2.zero; uv[v + 1] = Vector2.up;
            uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.right;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            Color tint = grain ? new Color(1f, .84f, .54f, .60f) : new Color(.82f, .58f, .30f, .16f);
            for (int n = 0; n < 4; n++) colors[v + n] = tint;
        }
        WriteVertices(); mesh.triangles = triangles; mesh.uv = uv; mesh.colors = colors;
    }

    void ReadBounds()
    {
        float h = view.orthographicSize, w = h * view.aspect;
        left = view.transform.position.x - w - 1.3f;
        right = view.transform.position.x + w + 1.3f;
        bottom = view.transform.position.y - h - .3f;
        top = view.transform.position.y + h + .3f;
    }

    void Update()
    {
        if (mesh == null || view == null || !GameState.IsPlaying || GemCatcher.IsGameOver || Time.timeScale <= 0f) return;
        float dt = Time.deltaTime; age += dt; ReadBounds();
        float gust = .65f + Mathf.PerlinNoise(age * .16f, 4.7f) * 1.8f;
        for (int i = 0; i < Count; i++)
        {
            Mote m = motes[i];
            m.position.x += m.speed * gust * dt;
            m.position.y += (Mathf.Sin(age * .8f + m.phase) * .16f + .035f) * dt;
            if (m.position.x > right || m.position.y > top || m.position.y < bottom)
            {
                m.position.x = left;
                m.position.y = Range(bottom, i < GrainCount ? top : Mathf.Lerp(bottom, top, .65f));
            }
            motes[i] = m;
        }
        WriteVertices();
    }

    void WriteVertices()
    {
        for (int i = 0; i < Count; i++)
        {
            Mote m = motes[i];
            float angle = Mathf.Sin(age * .65f + m.phase) * .12f;
            Vector3 along = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * m.width;
            Vector3 across = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f) * m.height;
            int v = i * 4;
            vertices[v] = transform.InverseTransformPoint(m.position - along - across);
            vertices[v + 1] = transform.InverseTransformPoint(m.position - along + across);
            vertices[v + 2] = transform.InverseTransformPoint(m.position + along + across);
            vertices[v + 3] = transform.InverseTransformPoint(m.position + along - across);
        }
        mesh.vertices = vertices; mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
