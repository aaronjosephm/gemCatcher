using UnityEngine;

/// <summary>
/// Drop-in camera shake. Auto-attaches to <c>Camera.main</c> on scene load and exposes a
/// static <see cref="Shake"/> API so any gameplay code can request a kick:
///
/// <code>CameraShake.Shake(0.15f, 0.25f);</code>
///
/// The component additively offsets the camera each frame using the layered sum of all
/// active shake requests, then snaps the camera back to its original local position when
/// the shake ends. Multiple overlapping requests stack naturally — a miss + a near-
/// simultaneous game-over hit-stop look chunkier than either alone.
/// </summary>
[DefaultExecutionOrder(1000)] // Run after gameplay scripts so we offset their final pose.
public class CameraShake : MonoBehaviour
{
  private static CameraShake instance;

  // Active shake state — magnitude (in world units), seconds remaining, and the
  // running per-shake age so we can fade it out over its duration.
  private struct ShakeRequest { public float magnitude; public float duration; public float age; }
  private readonly System.Collections.Generic.List<ShakeRequest> shakes =
      new System.Collections.Generic.List<ShakeRequest>(4);

  private Vector3 baseLocalPosition;
  private bool baseCaptured;

  // Auto-attach to the main camera so callers don't need to wire anything up.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void EnsureInstance()
  {
    if (instance != null) return;
    Camera cam = Camera.main;
    if (cam == null) return;
    instance = cam.gameObject.GetComponent<CameraShake>();
    if (instance == null) instance = cam.gameObject.AddComponent<CameraShake>();
  }

  /// <summary>Add a shake on top of any currently-running shakes.</summary>
  /// <param name="magnitude">Peak offset in world units. ~0.05 = subtle, 0.2 = chunky, 0.4+ = cinematic.</param>
  /// <param name="duration">How long the shake fades out over.</param>
  public static void Shake(float magnitude, float duration)
  {
    if (instance == null)
    {
      EnsureInstance();
      if (instance == null) return;
    }
    if (magnitude <= 0f || duration <= 0f) return;
    instance.shakes.Add(new ShakeRequest { magnitude = magnitude, duration = duration, age = 0f });
  }

  void LateUpdate()
  {
    if (!baseCaptured)
    {
      baseLocalPosition = transform.localPosition;
      baseCaptured = true;
    }

    if (shakes.Count == 0)
    {
      // Make sure we leave the camera exactly where the rest of the world expects it.
      transform.localPosition = baseLocalPosition;
      return;
    }

    // Use unscaled time so the shake still plays during a hit-stop (Time.timeScale dip).
    float dt = Time.unscaledDeltaTime;

    Vector3 offset = Vector3.zero;
    for (int i = shakes.Count - 1; i >= 0; i--)
    {
      ShakeRequest s = shakes[i];
      s.age += dt;
      float t = Mathf.Clamp01(s.age / s.duration);
      // Quadratic falloff so the shake feels like it has weight up front and tails off.
      float falloff = 1f - t * t;
      float amp = s.magnitude * falloff;
      // Pseudo-random per-axis perlin noise — smoother than Random.Range for shakes.
      float seed = i * 13.37f + s.age * 32f;
      float ox = (Mathf.PerlinNoise(seed, 0f) - 0.5f) * 2f;
      float oy = (Mathf.PerlinNoise(0f, seed) - 0.5f) * 2f;
      offset.x += ox * amp;
      offset.y += oy * amp;

      if (t >= 1f) shakes.RemoveAt(i);
      else shakes[i] = s;
    }

    transform.localPosition = baseLocalPosition + offset;
  }

  void OnDisable()
  {
    if (baseCaptured) transform.localPosition = baseLocalPosition;
    shakes.Clear();
  }
}
