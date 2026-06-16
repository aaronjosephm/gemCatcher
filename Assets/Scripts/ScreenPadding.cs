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
  }

  // ---------------------------------------------------------------------------
  // World-space bounds derived from Screen.safeArea + extra padding.
  // ---------------------------------------------------------------------------

  /// <summary>World-space Y coordinate of the bottom of the playable area.</summary>
  public static float WorldBottom
  {
    get
    {
      Camera cam = Camera.main;
      if (cam == null) return -5f + ExtraBottom;
      Rect safe = Screen.safeArea;
      Vector3 world = cam.ScreenToWorldPoint(new Vector3(safe.xMin, safe.yMin, cam.nearClipPlane));
      return world.y + ExtraBottom;
    }
  }

  /// <summary>World-space Y coordinate of the top of the playable area.</summary>
  public static float WorldTop
  {
    get
    {
      Camera cam = Camera.main;
      if (cam == null) return 5f - ExtraTop;
      Rect safe = Screen.safeArea;
      Vector3 world = cam.ScreenToWorldPoint(new Vector3(safe.xMax, safe.yMax, cam.nearClipPlane));
      return world.y - ExtraTop;
    }
  }

  /// <summary>World-space X coordinate of the left edge of the playable area.</summary>
  public static float WorldLeft
  {
    get
    {
      Camera cam = Camera.main;
      if (cam == null)
      {
        return -((cam != null ? cam.aspect : 1.78f) * 5f) + ExtraSide;
      }
      Rect safe = Screen.safeArea;
      Vector3 world = cam.ScreenToWorldPoint(new Vector3(safe.xMin, safe.yMin, cam.nearClipPlane));
      return world.x + ExtraSide;
    }
  }

  /// <summary>World-space X coordinate of the right edge of the playable area.</summary>
  public static float WorldRight
  {
    get
    {
      Camera cam = Camera.main;
      if (cam == null)
      {
        return ((cam != null ? cam.aspect : 1.78f) * 5f) - ExtraSide;
      }
      Rect safe = Screen.safeArea;
      Vector3 world = cam.ScreenToWorldPoint(new Vector3(safe.xMax, safe.yMax, cam.nearClipPlane));
      return world.x - ExtraSide;
    }
  }

  public static float WorldWidth => WorldRight - WorldLeft;
  public static float WorldHeight => WorldTop - WorldBottom;
}
