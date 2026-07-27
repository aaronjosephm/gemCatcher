using UnityEngine;

/// <summary>
/// Procedural "fiery aura" particle system attached to a power-up gem. The
/// flame is the unmistakable visual signal that a falling gem isn't a normal
/// catch — it's a power-up. Each power-up's flame is tinted to its theme
/// color (green for Double Points, blue for Wide Catcher, yellow for Shield,
/// magenta for Extra Life) so a glance at the color tells the player which
/// buff is on the line, while the fire itself communicates "this gem is
/// special."
///
/// <para>Implemented as a static helper (no MonoBehaviour) that builds a
/// child <c>ParticleSystem</c> on the supplied gem and tags it so the same
/// helper can find + remove it on cleanup. This keeps the gem prefab clean —
/// the fire is added at spawn time and torn down when the gem returns to the
/// pool, so the same pooled instance can be a power-up this cycle and a
/// plain gem the next.</para>
/// </summary>
public static class PowerUpFireEffect
{
  // Sentinel name we put on the child GameObject so Detach() can find it
  // again without storing a back-reference on the gem. Picked specifically
  // so it can't collide with anything an artist would name in a prefab.
  private const string ChildName = "__PowerUpFire";

  /// <summary>
  /// Attach (or refresh) a fiery aura to <paramref name="gem"/>, tinted with
  /// <paramref name="tint"/>. If a previous aura is still on the gem (e.g.
  /// the gem was already a power-up last cycle), it's torn down first so the
  /// flame doesn't accumulate / desync between pool reuses.
  /// </summary>
  public static void Attach(GameObject gem, Color tint)
  {
    if (gem == null) return;

    // Always start fresh — pooled gems can carry leftover flame children
    // from a previous power-up cycle.
    Detach(gem);

    GameObject fire = new GameObject(ChildName);
    fire.transform.SetParent(gem.transform, worldPositionStays: false);
    fire.transform.localPosition = Vector3.zero;
    fire.transform.localRotation = Quaternion.identity;
    // Flame intentionally rendered at world-scale unit (counter-scaled
    // against the gem's localScale). Gems can spawn shrunk by the score-
    // driven gem-shrink (down to 0.5x) and bombs can spawn at 2x, but the
    // flame should read at a consistent fingerprint size so "this gem is on
    // fire" lands at every difficulty tier. We compensate here with the
    // inverse of the gem's localScale.
    Vector3 gemScale = gem.transform.localScale;
    Vector3 invScale = new Vector3(
        gemScale.x != 0f ? 1f / gemScale.x : 1f,
        gemScale.y != 0f ? 1f / gemScale.y : 1f,
        gemScale.z != 0f ? 1f / gemScale.z : 1f);
    fire.transform.localScale = invScale;

    ParticleSystem ps = fire.AddComponent<ParticleSystem>();
    ParticleSystemRenderer psr = fire.GetComponent<ParticleSystemRenderer>();

    // Stop the system before configuring — Unity instantiates the
    // ParticleSystem in a playing state on AddComponent, and changing
    // properties on a running system can produce a one-frame burst at the
    // default settings.
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    var main = ps.main;
    main.duration = 1.0f;
    main.loop = true;
    main.startLifetime = 0.55f;
    main.startSpeed = 1.4f;
    main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
    // Slight randomization so the flame doesn't look like a single-particle
    // pulse — variation in lifetime + size makes the licks feel organic.
    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.maxParticles = 80;
    main.gravityModifier = -0.5f; // negative = particles drift UPWARD like real fire

    var emission = ps.emission;
    emission.rateOverTime = 32f;

    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Sphere;
    shape.radius = 0.45f;
    shape.radiusThickness = 0.6f; // emit from a hollow shell so the flame
                                  // licks AROUND the gem, not through its
                                  // center
    var velOverLife = ps.velocityOverLifetime;
    velOverLife.enabled = true;
    velOverLife.space = ParticleSystemSimulationSpace.Local;
    // All three axes MUST be assigned a MinMaxCurve of the SAME mode. Setting
    // only Y leaves X/Z at their default Constant(0) mode while Y becomes
    // TwoConstants(1,2), which trips Unity's runtime check and logs
    // "Particle Velocity curves must all be in the same mode" the moment
    // the effect spawns. Zero-out X and Z explicitly in the same
    // TwoConstants mode so particles still drift only upward.
    velOverLife.x = new ParticleSystem.MinMaxCurve(0f, 0f);
    velOverLife.y = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
    velOverLife.z = new ParticleSystem.MinMaxCurve(0f, 0f);

    // Gradient: hot core (white/yellow) → tinted middle → fade to transparent.
    // Using the supplied tint at the middle keypoint makes each power-up's
    // flame instantly identifiable without losing the universal "fire"
    // silhouette: every flame still has a hot bright core, just a different
    // colored body and tail.
    Color core = Color.Lerp(tint, Color.white, 0.7f);
    core.a = 1f;
    Color body = tint;
    body.a = 1f;
    Color tail = tint;
    tail.a = 0f;

    var colorOverLife = ps.colorOverLifetime;
    colorOverLife.enabled = true;
    Gradient grad = new Gradient();
    grad.SetKeys(
      new[]
      {
        new GradientColorKey(core, 0f),
        new GradientColorKey(body, 0.45f),
        new GradientColorKey(tail, 1f),
      },
      new[]
      {
        new GradientAlphaKey(0.0f, 0f),
        new GradientAlphaKey(0.95f, 0.15f),
        new GradientAlphaKey(0.6f, 0.55f),
        new GradientAlphaKey(0.0f, 1f),
      });
    colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

    var sizeOverLife = ps.sizeOverLifetime;
    sizeOverLife.enabled = true;
    AnimationCurve sizeCurve = new AnimationCurve(
      new Keyframe(0f, 0.4f),
      new Keyframe(0.4f, 1.0f),
      new Keyframe(1f, 0.1f));
    sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

    // Additive renderer so the flame brightens whatever gem color is behind
    // it — this is what reads as "glowing fire" rather than "colored cloud."
    Shader additive = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                   ?? Shader.Find("Particles/Standard Unlit")
                   ?? Shader.Find("Sprites/Default");
    if (additive != null)
    {
      Material mat = new Material(additive);
      // Enable additive blending mode if the shader supports it. Particles/
      // Standard Unlit uses _Mode = 4 for Additive in Built-in RP; on URP/HDRP
      // the keyword path differs but the fallback (Sprites/Default) still
      // looks acceptable.
      if (mat.HasProperty("_Mode"))      mat.SetFloat("_Mode", 4f); // Additive
      if (mat.HasProperty("_BlendOp"))   mat.SetFloat("_BlendOp", 0f);
      if (mat.HasProperty("_SrcBlend"))  mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
      if (mat.HasProperty("_DstBlend"))  mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
      mat.EnableKeyword("_ALPHABLEND_ON");
      mat.EnableKeyword("_EMISSION");
      if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", body * 1.4f);
      psr.material = mat;
    }

    psr.renderMode = ParticleSystemRenderMode.Billboard;
    // Render the flame ABOVE the gem mesh in transparent draw order. The
    // gem itself uses an opaque material, so the additive flame layers on
    // top automatically; the renderingLayerMask + sortingOrder bump is
    // belt-and-suspenders for any custom render pipelines.
    psr.sortingOrder = 5;

    ps.Play();
  }

  /// <summary>
  /// Strip the fiery aura (if any) off <paramref name="gem"/>. Safe to call
  /// on gems that were never a power-up — it's a no-op in that case. Called
  /// when a pooled gem is reset for a fresh spawn (so the next spawn doesn't
  /// inherit last cycle's flame) and from <see cref="FallingObject"/> when
  /// the power-up state is cleared.
  /// </summary>
  public static void Detach(GameObject gem)
  {
    if (gem == null) return;
    Transform t = gem.transform.Find(ChildName);
    if (t != null)
    {
      // Use immediate destroy in editor so duplicate flames don't pile up
      // across a single frame's edit-mode tinkering, but stick with the
      // standard Destroy at runtime to respect Unity's lifecycle.
      if (Application.isPlaying)
      {
        Object.Destroy(t.gameObject);
      }
      else
      {
        Object.DestroyImmediate(t.gameObject);
      }
    }
  }
}
