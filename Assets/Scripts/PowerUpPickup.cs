using UnityEngine;

/// <summary>
/// A falling collectible that grants a power-up when caught. Spawned by
/// <see cref="ObjectPooler"/> at intervals; falls straight down (subject to
/// the slow-motion multiplier just like gems); on overlap with the catcher it
/// activates the power-up and self-destructs.
///
/// Has its own catcher-overlap test rather than reusing FallingObject so the
/// pickup behavior stays decoupled from the gem code path. Pickups don't
/// bounce off walls, deduct points on miss, or count toward the daily gem cap.
/// </summary>
public class PowerUpPickup : MonoBehaviour
{
  public PowerUpType type;
  public float fallSpeed = 2.5f;

  // Cached catcher reference; refreshed lazily if the catcher hasn't been built
  // yet (CatcherManager creates it on first PlaceCatcherInSlot, which may run
  // a frame after the pickup spawns).
  private Transform catcher;
  private BoxCollider catcherCollider;
  private Vector3 catcherSize;
  private Vector3 catcherCenter;
  private const float pickupRadius = 0.45f;

  // Latches once a pickup is consumed so the same instance can't double-fire
  // its activation between catch and Destroy.
  private bool consumed;

  void Start()
  {
    FindCatcher();
  }

  void FindCatcher()
  {
    GameObject catcherGo = GameObject.FindWithTag("Catcher");
    if (catcherGo != null)
    {
      catcher = catcherGo.transform;
      catcherCollider = catcher.GetComponent<BoxCollider>();
    }
  }

  void Update()
  {
    if (consumed) return;

    if (GemCatcher.IsGameOver)
    {
      Destroy(gameObject);
      return;
    }

    float dt = Time.deltaTime;
    transform.Translate(Vector3.down * fallSpeed * dt, Space.World);

    // Idle rotation so the pickup is unmistakable as a special object.
    transform.Rotate(45f * dt, 90f * dt, 30f * dt, Space.World);

    if (transform.position.y < ScreenPadding.WorldBottom - 0.5f)
    {
      // Off-screen: silent miss — no penalty.
      Destroy(gameObject);
      return;
    }

    if (IsInCatcher())
    {
      consumed = true;
      PowerUpManager.Activate(type);
      CatchBurst.Spawn(transform.position, ColorForType(type));
      if (SoundManager.Instance != null)
      {
        SoundManager.Instance.Play("PowerUp");
      }
      Destroy(gameObject);
    }
  }

  bool IsInCatcher()
  {
    if (catcher == null || catcherCollider == null) FindCatcher();
    if (catcher == null || catcherCollider == null) return false;

    catcherSize = Vector3.Scale(catcherCollider.size, catcher.lossyScale);
    catcherCenter = catcher.TransformPoint(catcherCollider.center);

    Vector3 p = transform.position;
    return Mathf.Abs(p.x - catcherCenter.x) <= catcherSize.x / 2f + pickupRadius
        && Mathf.Abs(p.y - catcherCenter.y) <= catcherSize.y / 2f + pickupRadius
        && Mathf.Abs(p.z - catcherCenter.z) <= catcherSize.z / 2f + pickupRadius;
  }

  /// <summary>Color used for the pickup body, trail, and catch burst.</summary>
  public static Color ColorForType(PowerUpType type)
  {
    switch (type)
    {
      case PowerUpType.WiderCatcher: return new Color(0.40f, 0.85f, 1.00f);
      case PowerUpType.Shield: return new Color(1.00f, 0.85f, 0.35f);
      case PowerUpType.DoubleScore: return new Color(0.45f, 1.00f, 0.55f);
      default: return Color.white;
    }
  }

  /// <summary>Short label shown in the HUD slot for this power-up.</summary>
  public static string LabelForType(PowerUpType type)
  {
    switch (type)
    {
      case PowerUpType.WiderCatcher: return "WIDE";
      case PowerUpType.Shield: return "SHIELD";
      case PowerUpType.DoubleScore: return "2\u00d7 SCORE";
      default: return "";
    }
  }

  /// <summary>
  /// Builds a procedurally-styled pickup at the given world position. The
  /// returned GameObject has a glowing colored sphere, a trail, and a
  /// PowerUpPickup component already configured with <paramref name="type"/>.
  /// </summary>
  public static GameObject Create(PowerUpType type, Vector3 position)
  {
    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    go.name = "PowerUp_" + type;
    go.transform.position = position;
    go.transform.localScale = Vector3.one * 0.55f;

    // Drop the auto-added physics collider — we do our own bounds check and
    // don't want the pickup to interact with gems/obstacles.
    Collider col = go.GetComponent<Collider>();
    if (col != null) Destroy(col);

    Color c = ColorForType(type);

    Renderer rend = go.GetComponent<Renderer>();
    if (rend != null)
    {
      Material mat = new Material(Shader.Find("Standard"));
      mat.color = c;
      mat.SetColor("_EmissionColor", c * 1.6f);
      mat.EnableKeyword("_EMISSION");
      mat.SetFloat("_Glossiness", 0.85f);
      mat.SetFloat("_Metallic", 0.20f);
      rend.material = mat;
    }

    // Bright trail makes the pickup easy to spot among gems and obstacles.
    TrailRenderer trail = go.AddComponent<TrailRenderer>();
    trail.time = 0.45f;
    trail.startWidth = 0.40f;
    trail.endWidth = 0.05f;
    trail.minVertexDistance = 0.05f;
    Shader spriteShader = Shader.Find("Sprites/Default");
    if (spriteShader != null)
    {
      trail.material = new Material(spriteShader);
    }
    Gradient grad = new Gradient();
    grad.SetKeys(
      new[]
      {
        new GradientColorKey(c, 0f),
        new GradientColorKey(c, 1f),
      },
      new[]
      {
        new GradientAlphaKey(0.85f, 0f),
        new GradientAlphaKey(0f, 1f),
      });
    trail.colorGradient = grad;

    PowerUpPickup pickup = go.AddComponent<PowerUpPickup>();
    pickup.type = type;
    return go;
  }
}
