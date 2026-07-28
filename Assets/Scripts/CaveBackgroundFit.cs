using UnityEngine;

/// <summary>
/// Scales the backdrop to COVER the camera: full phone screen, no
/// empty margins. Crops only as much as the device aspect requires.
/// Also swaps the texture to match the currently selected level.
/// </summary>
[DisallowMultipleComponent]
public class CaveBackgroundFit : MonoBehaviour
{
  const float PlaneMeshSize = 10f;

  public float wallZ = 2f;

  [SerializeField] float textureAspect = 1024f / 1536f;

  private Camera cam;
  private float lastAspect = -1f;
  private float lastOrtho = -1f;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void EnsureInstance()
  {
    GameObject plane = GameObject.Find("Plane");
    if (plane == null) return;
    if (plane.GetComponent<CaveBackgroundFit>() == null)
    {
      plane.AddComponent<CaveBackgroundFit>();
    }
  }

  void Awake()
  {
    cam = Camera.main;
    ApplyLevelBackground();
    FitCover();
  }

  void LateUpdate()
  {
    if (cam == null) cam = Camera.main;
    if (cam == null) return;
    if (Mathf.Approximately(cam.aspect, lastAspect)
        && Mathf.Approximately(cam.orthographicSize, lastOrtho))
    {
      return;
    }
    FitCover();
  }

  /// <summary>
  /// Loads the background texture for the currently selected level and
  /// applies it to this plane's material.
  /// </summary>
  void ApplyLevelBackground()
  {
    var cfg = LevelManager.CurrentConfig;

    MeshRenderer mr = GetComponent<MeshRenderer>();
    if (mr == null) return;

    Texture2D tex = Resources.Load<Texture2D>(cfg.backgroundResource);
    if (tex != null && mr.material != null)
    {
      mr.material.mainTexture = tex;
    }

    ReadAspectFromMaterial();

    if (cam != null)
    {
      cam.backgroundColor = cfg.cameraColor;
    }
  }

  void ReadAspectFromMaterial()
  {
    MeshRenderer mr = GetComponent<MeshRenderer>();
    if (mr == null || mr.sharedMaterial == null) return;
    Texture t = mr.sharedMaterial.mainTexture;
    if (t != null && t.height > 0)
    {
      textureAspect = t.width / (float)t.height;
    }
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
    transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, wallZ);
    transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
  }
}
