using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Small decorative fish and pooled bubble trails, behind the gameplay plane.</summary>
public sealed class UnderwaterAmbience : MonoBehaviour
{
    private const int FishCount = 9;
    private const int BubbleCount = 24;
    private const float Depth = 1.25f; // Gameplay is at z=0, fitted backdrop at z=2.
    private readonly Transform[] fish = new Transform[FishCount];
    private readonly Transform[] tails = new Transform[FishCount];
    private readonly float[] fishSpeed = new float[FishCount];
    private readonly float[] fishY = new float[FishCount];
    private readonly float[] fishPhase = new float[FishCount];
    private readonly Transform[] bubbles = new Transform[BubbleCount];
    private readonly float[] bubbleSpeed = new float[BubbleCount];
    private readonly float[] bubbleX = new float[BubbleCount];
    private Mesh bodyMesh;
    private Mesh tailMesh;
    private Material fishMaterial;
    private Material bubbleMaterial;
    private float age;
    private float nextBubbles = 2f;
    private bool initialized;

    void Start()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader == null) { enabled = false; return; }
        fishMaterial = MakeMaterial(shader, new Color(0.06f, 0.29f, 0.43f, 0.65f));
        bubbleMaterial = MakeMaterial(shader, new Color(0.55f, 0.9f, 1f, 0.22f));
        bodyMesh = new Mesh { name = "Reef Fish Body", hideFlags = HideFlags.DontSave };
        Vector3[] vertices = new Vector3[13];
        int[] triangles = new int[36];
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.21f, 0f);
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % 12 + 1;
        }
        bodyMesh.vertices = vertices;
        bodyMesh.triangles = triangles;
        bodyMesh.RecalculateBounds();
        tailMesh = new Mesh { name = "Reef Fish Tail", hideFlags = HideFlags.DontSave };
        tailMesh.vertices = new[] { Vector3.zero, new Vector3(-0.35f, 0.24f, 0f), new Vector3(-0.35f, -0.24f, 0f) };
        tailMesh.triangles = new[] { 0, 1, 2 };
        tailMesh.RecalculateBounds();
        for (int i = 0; i < FishCount; i++)
        {
            fish[i] = MeshPart("Fish", transform, bodyMesh);
            tails[i] = MeshPart("Tail", fish[i], tailMesh);
            tails[i].localPosition = new Vector3(-0.4f, 0f, 0f);
            float size = Random.Range(0.22f, 0.48f);
            float direction = i % 2 == 0 ? 1f : -1f;
            fish[i].localScale = new Vector3(size * direction, size, size);
            fishSpeed[i] = Random.Range(0.18f, 0.38f) * direction;
            fishY[i] = Random.Range(0.2f, 0.8f);
            fishPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            fish[i].position = new Vector3(Mathf.Lerp(ScreenPadding.WorldLeft, ScreenPadding.WorldRight,
                Random.value), Mathf.Lerp(ScreenPadding.WorldBottom, ScreenPadding.WorldTop, fishY[i]), Depth);
        }
        for (int i = 0; i < BubbleCount; i++)
        {
            GameObject go = new GameObject("Bubble");
            go.transform.SetParent(transform, false);
            bubbles[i] = go.transform;
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = bubbleMaterial;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 20;
            line.startWidth = line.endWidth = 0.014f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int n = 0; n < 20; n++)
            {
                float angle = n * Mathf.PI * 2f / 20f;
                line.SetPosition(n, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.1f);
            }
            go.SetActive(false);
        }
        initialized = true;
    }

    Transform MeshPart(string name, Transform parent, Mesh mesh)
    {
        GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = fishMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go.transform;
    }

    static Material MakeMaterial(Shader shader, Color color)
    {
        Material mat = new Material(shader) { hideFlags = HideFlags.DontSave };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mat.SetInt("_Cull", (int)CullMode.Off);
        mat.SetFloat("_Surface", 1f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        return mat;
    }

    void Update()
    {
        if (!initialized || !GameState.IsPlaying || GemCatcher.IsGameOver || Time.timeScale <= 0f) return;
        age += Time.deltaTime;
        for (int i = 0; i < FishCount; i++)
        {
            Vector3 p = fish[i].position;
            p.x += fishSpeed[i] * Time.deltaTime;
            p.y = Mathf.Lerp(ScreenPadding.WorldBottom, ScreenPadding.WorldTop, fishY[i])
                + Mathf.Sin(age * 0.8f + fishPhase[i]) * 0.1f;
            if (fishSpeed[i] > 0f && p.x > ScreenPadding.WorldRight + 0.7f) p.x = ScreenPadding.WorldLeft - 0.7f;
            if (fishSpeed[i] < 0f && p.x < ScreenPadding.WorldLeft - 0.7f) p.x = ScreenPadding.WorldRight + 0.7f;
            fish[i].position = p;
            tails[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(age * 7f + fishPhase[i]) * 28f, 0f);
        }
        nextBubbles -= Time.deltaTime;
        if (nextBubbles <= 0f)
        {
            nextBubbles = Random.Range(3f, 6f);
            float fraction = Random.value < 0.5f ? Random.Range(0.08f, 0.25f) : Random.Range(0.75f, 0.92f);
            float x = Mathf.Lerp(ScreenPadding.WorldLeft, ScreenPadding.WorldRight, fraction);
            int count = Random.Range(3, 6);
            for (int i = 0; i < BubbleCount && count > 0; i++)
            {
                if (bubbles[i].gameObject.activeSelf) continue;
                count--;
                bubbleX[i] = x + Random.Range(-0.12f, 0.12f);
                bubbleSpeed[i] = Random.Range(0.65f, 1.1f);
                bubbles[i].position = new Vector3(bubbleX[i], ScreenPadding.WorldBottom - count * 0.18f, Depth - 0.1f);
                bubbles[i].localScale = Vector3.one * Random.Range(0.35f, 0.85f);
                bubbles[i].gameObject.SetActive(true);
            }
        }
        for (int i = 0; i < BubbleCount; i++)
        {
            if (!bubbles[i].gameObject.activeSelf) continue;
            Vector3 p = bubbles[i].position;
            p.y += bubbleSpeed[i] * Time.deltaTime;
            p.x = bubbleX[i] + Mathf.Sin(age * 1.6f + i) * 0.1f;
            bubbles[i].position = p;
            if (p.y > ScreenPadding.WorldTop + 0.3f) bubbles[i].gameObject.SetActive(false);
        }
    }

    void OnDestroy()
    {
        if (bodyMesh != null) Destroy(bodyMesh);
        if (tailMesh != null) Destroy(tailMesh);
        if (fishMaterial != null) Destroy(fishMaterial);
        if (bubbleMaterial != null) Destroy(bubbleMaterial);
    }
}
