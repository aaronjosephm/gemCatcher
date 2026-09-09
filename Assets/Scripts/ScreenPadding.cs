using UnityEngine;

/// <summary>
/// Single source of truth for the *gameplay* play area in world coordinates, taking
/// into account the platform's safe area (camera notch, rounded corners, gesture
/// regions on Android/iOS) plus a configurable extra padding for taste.
///
/// Other gameplay scripts query <see cref="WorldLeft"/>, <see cref="WorldRight"/>,
/// <see cref="WorldTop"/>, <see cref="WorldBottom"/> instead of using
/// <c>Camera.main.orthographicSize</c> directly. This way:
/// - On a phone with a front-camera notch at the top, gems spawn just below the
///   notch instead of behind it.
/// - The catcher sits above the gesture/home-bar area instead of touching the
///   bottom of the screen.
/// - Adjustable extras let you give the game even more breathing room without
///   having to touch every script.
///
/// A <see cref="ScreenPadding"/> MonoBehaviour is auto-instantiated on scene load
/// using default extras. Drop one into the scene if you want to tweak the values
/// in the Inspector.
/// </summary>
public class ScreenPadding : MonoBehaviour
{
  [Header("Extra padding (in world units, on top of Screen.safeArea)")]
  [Tooltip("Lifts the catcher and the gem-miss line away from the bottom of the screen.")]
  public float extraBottom = 0.6f;
  [Tooltip(
    "Lowers gem spawn / play-area top below the safe area's top edge so gems don't appear behind a notch, Dynamic Island, or front-camera lens. " +
    "Tuned to 1.2 world units (~12% of a 10-unit-tall view) so a gem's full body — not just its center — clears every iPhone's top hardware, from the original notch through iPhone 17 Pro's Dynamic Island.")]
  public float extraTop = 1.2f;
  [Tooltip("Inset on each side, useful for phones with curved screens.")]
  public float extraSide = 0.0f;

  // Static mirrors of the Inspector fields. Other scripts read these.
  public static float ExtraBottom = 0.6f;
  public static float ExtraTop = 1.2f;
  public static float ExtraSide = 0.0f;

  private static Camera cachedCamera;
  private static Rect cachedSafeArea;
  private static int cachedScreenWidth;
  private static int cachedScreenHeight;
  private static bool cachedOrthographic;
  private static float cachedOrthographicSize;
  private static float cachedFieldOfView;
  private static float cachedAspect;
  private static float cachedNearClipPlane;
  private static float cachedExtraBottom;
  private static float cachedExtraTop;
  private static float cachedExtraSide;
  private static float cachedWorldBottom;
  private static float cachedWorldTop;
  private static float cachedWorldLeft;
  private static float cachedWorldRight;
  private static bool boundsValid;
  private static int lastBoundsCheckFrame = -1;

  void Awake()
  {
    Apply();
  }

  void OnValidate()
  {
    // Keep the statics in sync when editing the prefab/instance in the Inspector.
    Apply();
  }

  void Apply()
  {
    ExtraBottom = Mathf.Max(0f, extraBottom);
    ExtraTop = Mathf.Max(0f, extraTop);
    ExtraSide = Mathf.Max(0f, extraSide);
    InvalidateBounds();
  }

  // Auto-bootstrap a default instance so other scripts always have padding values
  // available, even in scenes that don't include this component.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void EnsureInstance()
  {
    if (FindObjectOfType<ScreenPadding>() != null) return;
    GameObject go = new GameObject("ScreenPadding (auto)");
    go.AddComponent<ScreenPadding>();
  }

  // Reset on Play Mode entry so a stale value from a prior session doesn't leak in
  // when Domain Reload is disabled.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStaticState()
  {
    ExtraBottom = 0.6f;
    ExtraTop = 1.2f;
    ExtraSide = 0.0f;
    cachedCamera = null;
    boundsValid = false;
    lastBoundsCheckFrame = -1;
  }

  // ---------------------------------------------------------------------------
  // World-space bounds derived from Screen.safeArea + extra padding.
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Invalidates the cached bounds after a deliberate camera-layout change.
  /// Screen, safe-area, projection, and padding changes are detected automatically.
  /// </summary>
  public static void InvalidateBounds()
  {
    boundsValid = false;
    lastBoundsCheckFrame = -1;
  }

  /// <summary>Returns all cached world-space play bounds in one call.</summary>
  public static void GetWorldBounds(
    out float left,
    out float right,
    out float bottom,
    out float top)
  {
    EnsureBounds();
    left = cachedWorldLeft;
    right = cachedWorldRight;
    bottom = cachedWorldBottom;
    top = cachedWorldTop;
  }

  private static void EnsureBounds()
  {
    int frame = Time.frameCount;
    if (boundsValid && lastBoundsCheckFrame == frame) return;
    lastBoundsCheckFrame = frame;

    Camera cam = cachedCamera;
    if (cam == null || !cam.isActiveAndEnabled)
    {
      cam = Camera.main;
    }

    Rect safeArea = Screen.safeArea;
    int screenWidth = Screen.width;
    int screenHeight = Screen.height;

    bool projectionChanged =
      cam != cachedCamera
      || (cam != null
        && (cam.orthographic != cachedOrthographic
          || !Mathf.Approximately(cam.orthographicSize, cachedOrthographicSize)
          || !Mathf.Approximately(cam.fieldOfView, cachedFieldOfView)
          || !Mathf.Approximately(cam.aspect, cachedAspect)
          || !Mathf.Approximately(cam.nearClipPlane, cachedNearClipPlane)));

    bool displayChanged =
      screenWidth != cachedScreenWidth
      || screenHeight != cachedScreenHeight
      || safeArea != cachedSafeArea;

    bool paddingChanged =
      !Mathf.Approximately(ExtraBottom, cachedExtraBottom)
      || !Mathf.Approximately(ExtraTop, cachedExtraTop)
      || !Mathf.Approximately(ExtraSide, cachedExtraSide);

    if (boundsValid && !projectionChanged && !displayChanged && !paddingChanged)
    {
      return;
    }

    cachedCamera = cam;
    cachedSafeArea = safeArea;
    cachedScreenWidth = screenWidth;
    cachedScreenHeight = screenHeight;
    cachedExtraBottom = ExtraBottom;
    cachedExtraTop = ExtraTop;
    cachedExtraSide = ExtraSide;

    if (cam == null)
    {
      const float fallbackHalfHeight = 5f;
      const float fallbackAspect = 1.78f;
      cachedWorldBottom = -fallbackHalfHeight + ExtraBottom;
      cachedWorldTop = fallbackHalfHeight - ExtraTop;
      cachedWorldLeft = -(fallbackAspect * fallbackHalfHeight) + ExtraSide;
      cachedWorldRight = (fallbackAspect * fallbackHalfHeight) - ExtraSide;
      boundsValid = true;
      return;
    }

    cachedOrthographic = cam.orthographic;
    cachedOrthographicSize = cam.orthographicSize;
    cachedFieldOfView = cam.fieldOfView;
    cachedAspect = cam.aspect;
    cachedNearClipPlane = cam.nearClipPlane;

    Vector3 bottomLeft = cam.ScreenToWorldPoint(
      new Vector3(safeArea.xMin, safeArea.yMin, cam.nearClipPlane));
    Vector3 topRight = cam.ScreenToWorldPoint(
      new Vector3(safeArea.xMax, safeArea.yMax, cam.nearClipPlane));

    cachedWorldBottom = bottomLeft.y + ExtraBottom;
    cachedWorldTop = topRight.y - ExtraTop;
    cachedWorldLeft = bottomLeft.x + ExtraSide;
    cachedWorldRight = topRight.x - ExtraSide;
    boundsValid = true;
  }

  /// <summary>World-space Y coordinate of the bottom of the playable area.</summary>
  public static float WorldBottom
  {
    get
    {
      EnsureBounds();
      return cachedWorldBottom;
    }
  }

  /// <summary>World-space Y coordinate of the top of the playable area.</summary>
  public static float WorldTop
  {
    get
    {
      EnsureBounds();
      return cachedWorldTop;
    }
  }

  /// <summary>World-space X coordinate of the left edge of the playable area.</summary>
  public static float WorldLeft
  {
    get
    {
      EnsureBounds();
      return cachedWorldLeft;
    }
  }

  /// <summary>World-space X coordinate of the right edge of the playable area.</summary>
  public static float WorldRight
  {
    get
    {
      EnsureBounds();
      return cachedWorldRight;
    }
  }

  public static float WorldWidth
  {
    get
    {
      EnsureBounds();
      return cachedWorldRight - cachedWorldLeft;
    }
  }

  public static float WorldHeight
  {
    get
    {
      EnsureBounds();
      return cachedWorldTop - cachedWorldBottom;
    }
  }
}
