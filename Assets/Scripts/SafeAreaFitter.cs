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
  [Tooltip(
    "Extra pixels of inset at the top BEYOND Screen.safeArea. " +
    "Use this to defend against hardware that the OS doesn't report as part of " +
    "the notch — front-facing camera lenses on some Android phones sit inside " +
    "the reported safe area and would otherwise overlap top-aligned UI.")]
  public float extraTopPixels = 0f;

  private RectTransform rt;
  private Rect lastSafeArea;
  private Vector2Int lastScreenSize;
  private ScreenOrientation lastOrientation;
  private float lastExtraTopPixels;

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
    // multitasking-window resize, runtime tweaks to extraTopPixels, etc.).
    if (Screen.safeArea != lastSafeArea
        || Screen.width != lastScreenSize.x
        || Screen.height != lastScreenSize.y
        || Screen.orientation != lastOrientation
        || !Mathf.Approximately(extraTopPixels, lastExtraTopPixels))
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

    // Apply extra top inset by shrinking the safe-area rect from the top edge.
    // safeArea is in pixel space with origin at bottom-left, so the top edge is
    // y + height — pulling height down by extraTopPixels moves the top edge
    // down without affecting the bottom.
    if (extraTopPixels > 0f)
    {
      float reduce = Mathf.Min(extraTopPixels, safe.height);
      safe = new Rect(safe.x, safe.y, safe.width, safe.height - reduce);
    }

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

    lastSafeArea = Screen.safeArea;
    lastScreenSize = new Vector2Int(Screen.width, Screen.height);
    lastOrientation = Screen.orientation;
    lastExtraTopPixels = extraTopPixels;
  }
}
