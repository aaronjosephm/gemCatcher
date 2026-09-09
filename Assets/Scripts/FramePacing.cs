using UnityEngine;

/// <summary>
/// Establishes consistent frame pacing before any scene starts. Mobile platforms
/// ignore vSyncCount, so targetFrameRate must be set explicitly.
/// </summary>
public static class FramePacing
{
  public const int TargetFrameRate = 60;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  private static void Configure()
  {
    QualitySettings.vSyncCount = 0;
    Application.targetFrameRate = TargetFrameRate;
  }
}
