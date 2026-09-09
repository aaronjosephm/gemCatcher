using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable particle pop played when a gem is caught. Instances and their material
/// are prewarmed so catches do not construct or destroy rendering objects at runtime.
/// </summary>
public sealed class CatchBurst : MonoBehaviour
{
  private const int InitialPoolSize = 12;

  private static readonly List<CatchBurst> Pool = new List<CatchBurst>(16);
  private static GameObject poolRoot;
  private static Material sharedMaterial;

  private ParticleSystem particles;
  private bool initialized;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStaticState()
  {
    Pool.Clear();
    poolRoot = null;
    sharedMaterial = null;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  private static void Prewarm()
  {
    EnsurePool();
  }

  public static void Spawn(Vector3 worldPosition, Color color)
  {
    EnsurePool();

    CatchBurst burst = null;
    for (int i = 0; i < Pool.Count; i++)
    {
      if (!Pool[i].gameObject.activeSelf)
      {
        burst = Pool[i];
        break;
      }
    }

    if (burst == null)
    {
      burst = CreateBurst();
    }

    burst.Play(worldPosition, color);
  }

  private static void EnsurePool()
  {
    if (poolRoot != null) return;

    poolRoot = new GameObject("Catch Burst Pool");
    poolRoot.hideFlags = HideFlags.DontSave;
    DontDestroyOnLoad(poolRoot);

    Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
      ?? Shader.Find("Sprites/Default");
    if (shader != null)
    {
      sharedMaterial = new Material(shader)
      {
        name = "Catch Burst Shared Material",
        hideFlags = HideFlags.DontSave,
      };
    }

    for (int i = 0; i < InitialPoolSize; i++)
    {
      CreateBurst();
    }
  }

  private static CatchBurst CreateBurst()
  {
    GameObject go = new GameObject($"CatchBurst_{Pool.Count + 1}");
    go.transform.SetParent(poolRoot.transform, false);
    go.SetActive(false);

    ParticleSystem particleSystem = go.AddComponent<ParticleSystem>();
    CatchBurst burst = go.AddComponent<CatchBurst>();
    burst.Initialize(particleSystem);
    Pool.Add(burst);
    return burst;
  }

  private void Initialize(ParticleSystem particleSystem)
  {
    if (initialized) return;

    particles = particleSystem;
    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

    ParticleSystem.MainModule main = particles.main;
    main.duration = 0.6f;
    main.loop = false;
    main.startLifetime = 0.55f;
    main.startSpeed = 4.5f;
    main.startSize = 0.18f;
    main.startColor = Color.white;
    main.maxParticles = 60;
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.gravityModifier = 1.4f;
    main.playOnAwake = false;
    main.stopAction = ParticleSystemStopAction.Callback;

    ParticleSystem.EmissionModule emission = particles.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

    ParticleSystem.ShapeModule shape = particles.shape;
    shape.shapeType = ParticleSystemShapeType.Sphere;
    shape.radius = 0.05f;

    ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
    sizeOverLifetime.enabled = true;
    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
      1f,
      new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.4f, 0.85f),
        new Keyframe(1f, 0f)));

    ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
    colorOverLifetime.enabled = true;
    Gradient fade = new Gradient();
    fade.SetKeys(
      new[]
      {
        new GradientColorKey(Color.white, 0f),
        new GradientColorKey(Color.white, 1f),
      },
      new[]
      {
        new GradientAlphaKey(1f, 0f),
        new GradientAlphaKey(0.85f, 0.5f),
        new GradientAlphaKey(0f, 1f),
      });
    colorOverLifetime.color = fade;

    ParticleSystemRenderer particleRenderer = GetComponent<ParticleSystemRenderer>();
    if (particleRenderer != null && sharedMaterial != null)
    {
      particleRenderer.sharedMaterial = sharedMaterial;
    }

    initialized = true;
  }

  private void Play(Vector3 worldPosition, Color color)
  {
    transform.position = worldPosition;

    ParticleSystem.MainModule main = particles.main;
    main.startColor = color;

    gameObject.SetActive(true);
    particles.Play(true);
  }

  private void OnParticleSystemStopped()
  {
    gameObject.SetActive(false);
  }
}
