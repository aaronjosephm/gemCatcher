using UnityEngine;

/// <summary>
/// Detects when the app loses foreground (home button, incoming call, screen
/// lock, app switcher) and freezes the game until the player explicitly taps
/// "resume". Without this, a single phone notification can cost a player their
/// entire run because gems keep falling while the app is in the background.
///
/// We cover three distinct lifecycle events because Android and iOS deliver
/// them inconsistently:
///   - <see cref="OnApplicationPause"/>(true)  — app moved to background
///   - <see cref="OnApplicationFocus"/>(false) — lost input focus
///   - Returning to focus does NOT auto-unpause; the player must tap "resume"
///     so they aren't surprised by mid-air gems the moment they switch back.
///
/// Time scale is the master control. While paused, <c>Time.timeScale = 0</c>
/// freezes every gameplay coroutine and physics update; we cache the previous
/// value so coexisting effects like the game-over slow-mo can be restored on
/// resume.
/// </summary>
public class PauseHandler : MonoBehaviour
{
    public static PauseHandler Instance { get; private set; }

    /// <summary>True while the game is auto-paused after backgrounding.</summary>
    public static bool IsPaused { get; private set; }

    private float cachedTimeScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null) return;
        if (FindObjectOfType<PauseHandler>() != null) return;

        GameObject go = new GameObject("PauseHandler (auto)");
        go.AddComponent<PauseHandler>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnApplicationPause(bool pause)
    {
        if (pause) RequestPause();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // OnApplicationPause covers most cases on Android, but on some devices
        // (and on iOS when sliding the control center halfway down) only the
        // focus event fires. Treat both as a request to pause.
        if (!hasFocus) RequestPause();
    }

    /// <summary>
    /// Pause an active gameplay session. No-op if the game isn't currently
    /// playing, is already paused, or is on a menu / game-over screen.
    /// </summary>
    public void RequestPause()
    {
        if (IsPaused) return;
        if (!GameState.IsPlaying) return;
        // Don't pause if the game-over flow is already running; otherwise we'd
        // capture the hit-stop's reduced timeScale into cachedTimeScale and
        // restore the wrong value on resume, leaving the game in slow-mo.
        if (GemCatcher.IsGameOver) return;

        cachedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        IsPaused = true;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowPauseOverlay();
        }
    }

    /// <summary>
    /// Resume after a pause. Hooked up to the resume button in the pause
    /// overlay; safe to call from anywhere.
    /// </summary>
    public void Resume()
    {
        if (!IsPaused) return;

        Time.timeScale = cachedTimeScale > 0f ? cachedTimeScale : 1f;
        IsPaused = false;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.HidePauseOverlay();
        }
    }
}
