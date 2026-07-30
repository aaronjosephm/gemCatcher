using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns a large rotating Earth sphere at the bottom of the screen
/// for the Deep Space level, giving the illusion of orbiting Earth.
/// Uses a 3D sphere primitive for real globe rotation.
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

        // Use a sphere so rotation works naturally
        GameObject earthGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        earthGo.name = "EarthSphere";
        earthGo.transform.SetParent(transform, false);
        Destroy(earthGo.GetComponent<Collider>());
        earthTransform = earthGo.transform;

        // Opaque unlit material — the sphere geometry provides the circle
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        earthGo.GetComponent<Renderer>().material = mat;

        PositionEarth();
    }

    void PositionEarth()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float orthoSize = cam.orthographicSize;
        float screenWidth = orthoSize * 2f * cam.aspect;
        // Earth diameter: 1.5x screen width for that massive orbital feel
        float earthSize = screenWidth * 1.5f;

        earthTransform.localScale = new Vector3(earthSize, earthSize, earthSize);

        // Position below screen — top curve peeks above the bottom edge
        float bottomY = -orthoSize;
        earthTransform.localPosition = new Vector3(0f, bottomY - earthSize * 0.3f, 1.5f);
    }

    void Update()
    {
        if (earthTransform == null) return;
        // Spin the globe around its Y axis
        earthTransform.Rotate(0f, 3f * Time.deltaTime, 0f, Space.Self);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
