using UnityEngine;

/// <summary>
/// Slow, subtle "breathing" pulse + tiny rotation wobble for the main-menu logo.
/// Anything attached to this just lifts gently in scale and sways back and
/// forth a fraction of a degree, which is enough to make a static title feel
/// like a finished game logo instead of plain text.
///
/// Runs on unscaled time so a paused / hit-stopped game doesn't freeze the
/// menu animation.
/// </summary>
[DisallowMultipleComponent]
public class TitlePulse : MonoBehaviour
{
  [Tooltip("Pulse cycles per second. ~0.4 Hz = one breath every ~2.5 seconds.")]
  public float frequency = 0.35f;

  [Tooltip("Peak scale deviation around the base scale. 0.04 = ±4 percent.")]
  [Range(0f, 0.2f)]
  public float scaleAmplitude = 0.04f;

  [Tooltip("Peak rotation in degrees around Z. Adds a subtle sway.")]
  [Range(0f, 5f)]
  public float rotationAmplitude = 1.0f;

  [Tooltip("Random phase so multiple pulsing elements don't move in lockstep.")]
  public bool randomizePhase = true;

  private Vector3 baseScale;
  private float baseZRotation;
  private float phaseOffset;

  void Awake()
  {
    baseScale = transform.localScale;
    baseZRotation = transform.localEulerAngles.z;
    phaseOffset = randomizePhase ? Random.value * Mathf.PI * 2f : 0f;
  }

  void OnEnable()
  {
    // Re-cache in case the parent rebuilt the title rect between disables —
    // means the pulse always swings around whatever scale the layout settled on.
    baseScale = transform.localScale;
    baseZRotation = transform.localEulerAngles.z;
  }

  void Update()
  {
    float t = Time.unscaledTime * frequency * Mathf.PI * 2f + phaseOffset;
    float pulse = 1f + Mathf.Sin(t) * scaleAmplitude;
    transform.localScale = baseScale * pulse;

    // Cosine on rotation so the sway lags the scale by a quarter period —
    // makes the motion feel less mechanical than syncing both axes.
    float sway = Mathf.Cos(t) * rotationAmplitude;
    Vector3 euler = transform.localEulerAngles;
    euler.z = baseZRotation + sway;
    transform.localEulerAngles = euler;
  }
}
