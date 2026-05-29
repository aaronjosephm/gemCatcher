using UnityEngine;

/// <summary>
/// One-shot particle pop spawned when a gem is caught. Rendered procedurally so no
/// asset setup is required; just call <see cref="Spawn(Vector3, Color)"/>.
///
/// The burst lives for ~0.6s, scales with screen size via the orthographic camera, and
/// destroys itself when the particle system finishes.
/// </summary>
public static class CatchBurst
{
  public static void Spawn(Vector3 worldPosition, Color color)
  {
    GameObject go = new GameObject("CatchBurst");
    go.transform.position = worldPosition;

    ParticleSystem ps = go.AddComponent<ParticleSystem>();
    // Stop before configuring so the changes apply atomically.
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    var main = ps.main;
    main.duration = 0.6f;
    main.loop = false;
    main.startLifetime = 0.55f;
    main.startSpeed = 4.5f;
    main.startSize = 0.18f;
    main.startColor = color;
    main.maxParticles = 60;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.gravityModifier = 1.4f;
    main.playOnAwake = false;
    main.stopAction = ParticleSystemStopAction.Destroy;

    var emission = ps.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Sphere;
    shape.radius = 0.05f;

    var sizeOverLifetime = ps.sizeOverLifetime;
    sizeOverLifetime.enabled = true;
    AnimationCurve sizeCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.4f, 0.85f),
        new Keyframe(1f, 0f));
    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

    var colorOverLifetime = ps.colorOverLifetime;
    colorOverLifetime.enabled = true;
    Gradient g = new Gradient();
    g.SetKeys(
        new[]
        {
            new GradientColorKey(color, 0f),
            new GradientColorKey(color, 0.7f),
            new GradientColorKey(new Color(color.r, color.g, color.b, 0f), 1f)
        },
        new[]
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(0.85f, 0.5f),
            new GradientAlphaKey(0f, 1f)
        });
    colorOverLifetime.color = g;

    // Built-in particle material — small bright sprites that work in the BiRP and URP
    // without dragging in any project assets.
    ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
    if (renderer != null)
    {
      Shader spriteShader = Shader.Find("Sprites/Default");
      if (spriteShader != null) renderer.material = new Material(spriteShader);
    }

    ps.Play();
  }
}
