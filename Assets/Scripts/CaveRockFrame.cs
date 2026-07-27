using UnityEngine;

/// <summary>
/// Spawns rock prefabs around the screen edges to create an immersive
/// cave frame. Rocks sit in the foreground (Z &lt; 0, closer to camera)
/// so they partially overlap the gameplay area at the margins.
///
/// The layout adapts to any screen aspect ratio by computing world-space
/// edge positions from the orthographic camera.
/// </summary>
[DisallowMultipleComponent]
public class CaveRockFrame : MonoBehaviour
{
    [Header("Rock Prefabs (loaded from Resources at runtime if empty)")]
    [Tooltip("Drag rock prefabs here, or leave empty to auto-load from Resources.")]
    public GameObject[] rockPrefabs;

    [Header("Placement")]
    [Tooltip("Z depth for foreground rocks. Negative = in front of gameplay.")]
    public float rockZ = -0.5f;

    [Tooltip("How much rocks are pushed outward beyond the screen edge (world units).")]
    public float edgeInset = 0.5f;

    [Tooltip("Scale multiplier for placed rocks.")]
    public float rockScale = 0.6f;

    [Tooltip("Darken foreground rocks so they feel like shadowed cave walls.")]
    [Range(0f, 1f)]
    public float rockDarkness = 0.3f;

    private GameObject rockParent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInstance()
    {
        if (FindAnyObjectByType<CaveRockFrame>() != null) return;

        GameObject go = new GameObject("CaveRockFrame (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<CaveRockFrame>();
    }

    void Awake()
    {
        if (rockPrefabs == null || rockPrefabs.Length == 0)
            LoadPrefabsFromAssets();

        if (rockPrefabs == null || rockPrefabs.Length == 0)
        {
            Debug.LogWarning("CaveRockFrame: No rock prefabs found.");
            return;
        }

        SpawnRocks();
    }

    void LoadPrefabsFromAssets()
    {
        // Try loading from the asset path via Resources.
        // The prefabs aren't in Resources, so we'll use a hardcoded list of names
        // and load them at edit-time via the Inspector. For runtime auto-load,
        // we search for any GameObjects tagged or named "Rock" in the scene.
        // Fallback: find prefabs already placed in scene by name.
        var sceneRocks = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        System.Collections.Generic.List<GameObject> found = new();
        foreach (var mr in sceneRocks)
        {
            if (mr.gameObject.name.StartsWith("Rock") && mr.gameObject.scene.IsValid())
            {
                // Use this as a template — we'll instantiate copies.
                if (!found.Contains(mr.gameObject))
                    found.Add(mr.gameObject);
            }
        }
        if (found.Count > 0)
        {
            rockPrefabs = found.ToArray();
        }
    }

    void SpawnRocks()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float ortho = cam.orthographicSize;
        float aspect = cam.aspect;
        float halfH = ortho;
        float halfW = ortho * aspect;

        rockParent = new GameObject("CaveRocks");
        rockParent.transform.SetParent(transform);
        DontDestroyOnLoad(rockParent);

        // Define rock placements: position offset from center, rotation, scale modifier.
        // Bottom-left, bottom-right, top-left, top-right, and side rocks.
        var placements = new[]
        {
            // Bottom-left corner
            new RockPlacement(new Vector3(-halfW - edgeInset, -halfH + 0.5f, rockZ), new Vector3(0, 30, 10), 1.2f),
            new RockPlacement(new Vector3(-halfW + 0.5f, -halfH - edgeInset, rockZ), new Vector3(5, -20, 0), 1.0f),

            // Bottom-right corner
            new RockPlacement(new Vector3(halfW + edgeInset, -halfH + 0.5f, rockZ), new Vector3(0, -30, -10), 1.2f),
            new RockPlacement(new Vector3(halfW - 0.5f, -halfH - edgeInset, rockZ), new Vector3(5, 20, 0), 1.0f),

            // Bottom center
            new RockPlacement(new Vector3(0f, -halfH - edgeInset * 0.5f, rockZ), new Vector3(10, 0, 0), 0.8f),
            new RockPlacement(new Vector3(-halfW * 0.4f, -halfH + 0.2f, rockZ), new Vector3(0, 45, 5), 0.7f),
            new RockPlacement(new Vector3(halfW * 0.4f, -halfH + 0.2f, rockZ), new Vector3(0, -45, -5), 0.7f),

            // Left edge
            new RockPlacement(new Vector3(-halfW - edgeInset, -halfH * 0.3f, rockZ), new Vector3(0, 15, 5), 1.0f),
            new RockPlacement(new Vector3(-halfW - edgeInset, halfH * 0.3f, rockZ), new Vector3(0, -10, -5), 0.9f),

            // Right edge
            new RockPlacement(new Vector3(halfW + edgeInset, -halfH * 0.3f, rockZ), new Vector3(0, -15, -5), 1.0f),
            new RockPlacement(new Vector3(halfW + edgeInset, halfH * 0.3f, rockZ), new Vector3(0, 10, 5), 0.9f),

            // Top-left corner
            new RockPlacement(new Vector3(-halfW - edgeInset, halfH - 0.5f, rockZ), new Vector3(0, 25, 180), 1.1f),
            new RockPlacement(new Vector3(-halfW + 0.5f, halfH + edgeInset, rockZ), new Vector3(180, 20, 0), 0.9f),

            // Top-right corner
            new RockPlacement(new Vector3(halfW + edgeInset, halfH - 0.5f, rockZ), new Vector3(0, -25, 180), 1.1f),
            new RockPlacement(new Vector3(halfW - 0.5f, halfH + edgeInset, rockZ), new Vector3(180, -20, 0), 0.9f),

            // Top center
            new RockPlacement(new Vector3(0f, halfH + edgeInset * 0.5f, rockZ), new Vector3(180, 0, 0), 0.8f),
        };

        for (int i = 0; i < placements.Length; i++)
        {
            var p = placements[i];
            GameObject prefab = rockPrefabs[i % rockPrefabs.Length];
            GameObject rock = Instantiate(prefab, rockParent.transform);
            rock.name = $"CaveRock_{i}";

            rock.transform.position = p.position;
            rock.transform.eulerAngles = p.rotation;
            float s = rockScale * p.scaleMod;
            rock.transform.localScale = Vector3.one * s;

            // Darken the rocks to look like shadowed cave walls.
            DarkenRenderers(rock);

            // Disable any colliders so rocks don't interfere with gameplay.
            foreach (var col in rock.GetComponentsInChildren<Collider>())
                col.enabled = false;
        }
    }

    void DarkenRenderers(GameObject rock)
    {
        Color dark = Color.Lerp(Color.white, Color.black, rockDarkness);
        foreach (var mr in rock.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in mr.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * dark);
                else if (mat.HasProperty("_Color"))
                    mat.color = mat.color * dark;
            }
        }
    }

    struct RockPlacement
    {
        public Vector3 position;
        public Vector3 rotation;
        public float scaleMod;

        public RockPlacement(Vector3 pos, Vector3 rot, float scale)
        {
            position = pos;
            rotation = rot;
            scaleMod = scale;
        }
    }
}
