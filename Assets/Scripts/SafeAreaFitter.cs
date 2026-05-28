using UnityEngine;

/// <summary>
/// Resizes its <see cref="RectTransform"/> to match <see cref="Screen.safeArea"/>
/// every frame. Use as the parent of UI that should never sit under a notch,
/// rounded corner, or gesture-bar area.
///
/// In the editor (or on devices without a notch) <c>Screen.safeArea</c> equals the
/// full screen, so this component is a no-op there — making it safe to leave
/// enabled at all times.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class SafeAreaFitter : MonoBehaviour
{
  private RectTransform rt;
  private Rect lastSafeArea;
  private Vector2Int lastScreenSize;
  private ScreenOrientation lastOrientation;

  void Awake()
  {
    rt = GetComponent<RectTransform>();
  }

  void OnEnable()
  {
    Apply();
  }

  void Update()
  {
    // Cheap to check; only re-apply on actual change (rotation, fold/unfold,
    // multitasking-window resize, etc.).
    if (Screen.safeArea != lastSafeArea
        || Screen.width != lastScreenSize.x
        || Screen.height != lastScreenSize.y
        || Screen.orientation != lastOrientation)
    {
      Apply();
    }
  }

  void Apply()
  {
    if (rt == null) rt = GetComponent<RectTransform>();
    if (rt == null) return;

    Rect safe = Screen.safeArea;
    if (Screen.width <= 0 || Screen.height <= 0) return;

    // Convert pixel-space safeArea to anchor-space (0..1).
    Vector2 anchorMin = safe.position;
    Vector2 anchorMax = safe.position + safe.size;
    anchorMin.x /= Screen.width;
    anchorMin.y /= Screen.height;
    anchorMax.x /= Screen.width;
    anchorMax.y /= Screen.height;

    rt.anchorMin = anchorMin;
    rt.anchorMax = anchorMax;
    rt.offsetMin = Vector2.zero;
    rt.offsetMax = Vector2.zero;

    lastSafeArea = safe;
    lastScreenSize = new Vector2Int(Screen.width, Screen.height);
    lastOrientation = Screen.orientation;
  }
}
