using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns a large flat disc (quad) with the Earth texture at the bottom of the screen
/// for the Deep Space level. Rotates the disc around its forward axis to simulate
/// viewing Earth's rotation from orbit above.
/// </summary>
public class OrbitingEarth : MonoBehaviour
{
    private static OrbitingEarth instance;
    private Transform earthTransform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        TrySpawn();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TrySpawn();
    }

    static void TrySpawn()
    {
        if (LevelManager.SelectedLevel != LevelManager.LevelId.Space)
        {
            if (instance != null) Destroy(instance.gameObject);
            return;
        }

        if (instance != null) return;

        var go = new GameObject("OrbitingEarth");
        instance = go.AddComponent<OrbitingEarth>();
    }

    void Awake()
    {
        Texture2D tex = Resources.Load<Texture2D>("Textures/Earth");
        if (tex == null) { Destroy(gameObject); return; }

        // Use a Quad — perfectly flat, zero depth
        GameObject earthGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        earthGo.name = "EarthDisc";
        earthGo.transform.SetParent(transform, false);

        // Quads have no collider issues but remove just in case
        Collider col = earthGo.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }

        earthTransform = earthGo.transform;

        // Unlit material with earth texture, alpha cutout for circular edge
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);

        // Enable alpha clipping to cut away transparent pixels (no white ring)
        mat.SetFloat("_Cutoff", 0.5f);
        mat.SetFloat("_Surface", 0); // opaque
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = 2450; // AlphaTest queue

        earthGo.GetComponent<Renderer>().material = mat;

        PositionEarth();
    }

    void PositionEarth()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float orthoSize = cam.orthographicSize;
        float screenWidth = orthoSize * 2f * cam.aspect;

        // Earth diameter: 2x screen width for massive scale
        float earthSize = screenWidth * 2f;
        earthTransform.localScale = new Vector3(earthSize, earthSize, 1f);

        // Place at bottom of screen, peeking up. Z=1 is between camera and background.
        float bottomY = -orthoSize;
        earthTransform.localPosition = new Vector3(0f, bottomY - earthSize * 0.25f, 1f);
    }

    void Update()
    {
        if (earthTransform == null) return;
        // Rotate around the forward axis (Z) — spins the earth image in place
        earthTransform.Rotate(0f, 0f, -1f * Time.deltaTime, Space.Self);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
