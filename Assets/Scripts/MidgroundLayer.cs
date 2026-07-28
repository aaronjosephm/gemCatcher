using UnityEngine;

/// <summary>
/// Spawns a transparent midground image in front of the cave background
/// but behind the gameplay elements (gems, catcher). Uses the same
/// cover-fit logic as CaveBackgroundFit so it always fills the screen.
/// </summary>
[DisallowMultipleComponent]
public class MidgroundLayer : MonoBehaviour
{
    const float PlaneMeshSize = 10f;

    [Tooltip("Z depth. Must be between background (2) and gameplay (~0). Lower = closer to camera.")]
    public float midgroundZ = 1.5f;

    [Tooltip("Vertical offset in world units. Negative = shift image down.")]
    public float verticalOffset = -2f;

    private Camera cam;
    private float lastAspect = -1f;
    private float lastOrtho = -1f;
    private float textureAspect = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInstance()
    {
        if (FindAnyObjectByType<MidgroundLayer>() != null) return;

        Texture2D tex = Resources.Load<Texture2D>("Backgrounds/MidgroundCave");
        if (tex == null) return;

        GameObject go = new GameObject("MidgroundPlane");
        DontDestroyOnLoad(go);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.mesh = CreateQuadMesh();

        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        // Use URP Unlit with transparent blend so the alpha shows through.
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent");
        Material mat = new Material(shader);
        mat.mainTexture = tex;

        // Enable transparency on URP Unlit.
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f);   // Alpha blend
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        // Set base color to white with full alpha so texture alpha drives transparency.
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);

        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        go.AddComponent<MidgroundLayer>();
    }

    void Awake()
    {
        cam = Camera.main;
        ReadAspectFromMaterial();
        FitCover();
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (Mathf.Approximately(cam.aspect, lastAspect)
            && Mathf.Approximately(cam.orthographicSize, lastOrtho))
            return;
        FitCover();
    }

    void ReadAspectFromMaterial()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null || mr.sharedMaterial == null) return;
        Texture t = mr.sharedMaterial.mainTexture;
        if (t != null && t.height > 0)
            textureAspect = t.width / (float)t.height;
    }

    void FitCover()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        float aspect = cam.aspect;
        float ortho = cam.orthographicSize;
        lastAspect = aspect;
        lastOrtho = ortho;

        float viewH = ortho * 2f;
        float viewW = viewH * aspect;
        float texAspect = Mathf.Max(0.01f, textureAspect);

        float worldH = viewH;
        float worldW = worldH * texAspect;
        if (worldW < viewW)
        {
            worldW = viewW;
            worldH = worldW / texAspect;
        }

        transform.localScale = new Vector3(worldW / PlaneMeshSize, 1f, worldH / PlaneMeshSize);
        transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y + verticalOffset, midgroundZ);
        transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
    }

    static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh { name = "MidgroundQuad" };
        float h = PlaneMeshSize / 2f;
        mesh.vertices = new Vector3[]
        {
            new Vector3(-h, 0, -h),
            new Vector3( h, 0, -h),
            new Vector3( h, 0,  h),
            new Vector3(-h, 0,  h)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };
        mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }
}
