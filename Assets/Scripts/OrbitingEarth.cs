using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns a large Earth sprite at the bottom of the screen for the
/// Deep Space level. The UV offset scrolls slowly to simulate rotation,
/// giving the illusion that Catchy is orbiting Earth.
/// </summary>
public class OrbitingEarth : MonoBehaviour
{
    private static OrbitingEarth instance;
    private Material earthMat;

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

        // Create a quad for the earth
        GameObject earthGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        earthGo.name = "EarthSprite";
        earthGo.transform.SetParent(transform, false);
        Destroy(earthGo.GetComponent<Collider>());

        // Set up transparent unlit material
        earthMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        earthMat.SetTexture("_BaseMap", tex);
        earthMat.SetColor("_BaseColor", Color.white);
        earthMat.SetFloat("_Surface", 1f);
        earthMat.SetFloat("_Blend", 0f);
        earthMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        earthMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        earthMat.SetFloat("_ZWrite", 0f);
        earthMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        earthMat.renderQueue = 2999;
        earthGo.GetComponent<Renderer>().material = earthMat;

        PositionEarth(earthGo);
    }

    void PositionEarth(GameObject earthGo)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float orthoSize = cam.orthographicSize;
        // Earth diameter: about 1.5x the screen width for that massive orbital feel
        float screenWidth = orthoSize * 2f * cam.aspect;
        float earthSize = screenWidth * 1.5f;

        earthGo.transform.localScale = new Vector3(earthSize, earthSize, 1f);

        // Position at the bottom — center the earth so the top curve peeks above
        // the bottom edge. Push it about 60% below the bottom of the screen.
        float bottomY = -orthoSize;
        earthGo.transform.localPosition = new Vector3(0f, bottomY - earthSize * 0.3f, 1.5f);
    }

    void Update()
    {
        // Rotate the earth around its Z axis (facing camera) for a slow spin
        transform.Rotate(0f, 0f, -2f * Time.deltaTime); // ~2 degrees/sec
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
