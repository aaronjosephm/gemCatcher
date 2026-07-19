using UnityEngine;

/// <summary>
/// Scales the cave backdrop to COVER the camera: full phone screen, no
/// empty margins. Crops only as much as the device aspect requires.
/// Recomputing on Retry yields the same size for the same camera, so the
/// backdrop does not appear to "jump" between runs.
/// </summary>
[DisallowMultipleComponent]
public class CaveBackgroundFit : MonoBehaviour
{
  const float PlaneMeshSize = 10f;

  public float wallZ = 2f;

  // Cave art is 576×1024 → width/height.
  [SerializeField] float textureAspect = 576f / 1024f;

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
    ReadAspectFromMaterial();
    if (cam != null)
    {
      cam.backgroundColor = new Color(0.05f, 0.06f, 0.12f, 1f);
    }
    FitCover();
  }

  void LateUpdate()
  {
    // Only rewrite transform when the camera frustum actually changes
    // (rotation / window resize). Same aspect → same scale on Retry.
    if (cam == null) cam = Camera.main;
    if (cam == null) return;
    if (Mathf.Approximately(cam.aspect, lastAspect)
        && Mathf.Approximately(cam.orthographicSize, lastOrtho))
    {
      return;
    }
    FitCover();
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

    // COVER: smallest size that still fills the whole view.
    // Match height first; if that leaves gaps on the sides, match width instead.
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
