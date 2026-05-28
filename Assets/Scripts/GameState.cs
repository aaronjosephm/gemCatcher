using UnityEngine;

/// <summary>
/// Global gameplay state flags shared between the main-menu/UI layer and the gameplay
/// systems (ObjectPooler, etc.). Static so callers don't need to thread a reference
/// through every system.
/// </summary>
public static class GameState
{
  /// <summary>
  /// True while the player is actively in a round. ObjectPooler skips spawning while
  /// this is false, so the catcher just sits idle behind the main menu until the
  /// player presses Play.
  /// </summary>
  public static bool IsPlaying = false;

  /// <summary>
  /// Set by "Try Again" on the game-over screen so the next scene load skips the main
  /// menu and drops the player straight back into a fresh round. Reset to false after
  /// UIManager.Start consumes it. Static fields persist across SceneManager.LoadScene
  /// (Unity only resets them on Play Mode entry), which is what makes this work.
  /// </summary>
  public static bool SkipMainMenuOnLoad = false;

  // Reset on Play Mode entry so a leftover IsPlaying=true from a previous editor
  // session doesn't accidentally skip the menu the first time we run.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStaticState()
  {
    IsPlaying = false;
    SkipMainMenuOnLoad = false;
  }
}
