using UnityEngine;

/// <summary>
/// Centralised haptic-feedback (vibration) controller. Listens to the same
/// gameplay events the UI and audio systems do, and triggers a short, intent-
/// matched vibration on each one — light tick on catch, sharper thump on miss,
/// heavy hit on bomb, success ladder on heart / milestone, etc.
///
/// Backend selection per platform:
///   - Android API 26+ : <c>VibrationEffect.createOneShot</c> with explicit
///     amplitude (0–255) so a "light tick" feels different from a "heavy hit".
///   - Older Android   : deprecated <c>Vibrator.vibrate(long)</c> — duration
///     only, no amplitude control. Still feels distinct on duration alone.
///   - Other platforms : <see cref="Handheld.Vibrate"/> binary thump fallback.
///   - Editor          : no-op (avoids burning the editor preview on iteration).
///
/// User preference is persisted via PlayerPrefs key <see cref="PrefKey"/>;
/// settings UI flips it through <see cref="HapticsEnabled"/>.
/// </summary>
public class HapticManager : MonoBehaviour
{
    public const string PrefKey = "HapticsEnabled";

    public static HapticManager Instance { get; private set; }

    /// <summary>
    /// Master enable toggle. Set true/false from the settings panel and the
    /// preference is written to PlayerPrefs immediately.
    /// </summary>
    public static bool HapticsEnabled
    {
        get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Intent-classified haptic strengths.</summary>
    public enum Intensity
    {
        /// <summary>~10ms low-amplitude tap (gem caught).</summary>
        Light,
        /// <summary>~25ms medium thump (gem missed).</summary>
        Medium,
        /// <summary>~50ms heavy hit (bomb collected, game over).</summary>
        Heavy,
        /// <summary>Two-pulse confirmation (heart gem, bonus life, milestone).</summary>
        Success,
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject vibratorService;
    private bool hasAmplitudeControl;
    private int sdkInt;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null) return;
        if (FindObjectOfType<HapticManager>() != null) return;

        GameObject go = new GameObject("HapticManager (auto)");
        go.AddComponent<HapticManager>();
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

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                vibratorService = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            using (AndroidJavaClass build = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                sdkInt = build.GetStatic<int>("SDK_INT");
            }

            // VibrationEffect (with amplitude control) is API 26+. Below that we
            // fall back to the deprecated vibrate(long) overload — duration only.
            hasAmplitudeControl = sdkInt >= 26 && vibratorService != null
                && vibratorService.Call<bool>("hasAmplitudeControl");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HapticManager] Android vibrator init failed: {ex.Message}");
            vibratorService = null;
        }
#endif

        SubscribeToGameEvents();
    }

    void OnDestroy()
    {
        UnsubscribeFromGameEvents();
        if (Instance == this) Instance = null;
    }

    // -- Event wiring ---------------------------------------------------------

    void SubscribeToGameEvents()
    {
        GemCatcher.OnGemCaught += HandleGemCaught;
        GemCatcher.OnGemMissed += HandleGemMissed;
        GemCatcher.OnBombHit += HandleBombHit;
        GemCatcher.OnBonusLifeAwarded += HandleBonusLife;
        GemCatcher.OnGameOver += HandleGameOver;
        MilestoneTracker.OnMilestoneReached += HandleMilestone;
        PowerUpManager.OnActivated += HandlePowerUpActivated;
    }

    void UnsubscribeFromGameEvents()
    {
        GemCatcher.OnGemCaught -= HandleGemCaught;
        GemCatcher.OnGemMissed -= HandleGemMissed;
        GemCatcher.OnBombHit -= HandleBombHit;
        GemCatcher.OnBonusLifeAwarded -= HandleBonusLife;
        GemCatcher.OnGameOver -= HandleGameOver;
        MilestoneTracker.OnMilestoneReached -= HandleMilestone;
        PowerUpManager.OnActivated -= HandlePowerUpActivated;
    }

    void HandleGemCaught(int amount, Vector3 worldPosition) => Trigger(Intensity.Light);
    void HandleGemMissed(int amount, Vector3 worldPosition) => Trigger(Intensity.Medium);
    void HandleBombHit(Vector3 worldPosition) => Trigger(Intensity.Heavy);
    void HandleBonusLife(int newLifeTotal) => Trigger(Intensity.Success);
    void HandleGameOver() => Trigger(Intensity.Heavy);
    void HandleMilestone(MilestoneTracker.Milestone _) => Trigger(Intensity.Success);
    void HandlePowerUpActivated(PowerUpType _, float __) => Trigger(Intensity.Light);

    // -- Public trigger -------------------------------------------------------

    /// <summary>
    /// Fire a haptic pulse. No-op if haptics are disabled in settings or the
    /// platform has no working vibrator.
    /// </summary>
    public void Trigger(Intensity intensity)
    {
        if (!HapticsEnabled) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        TriggerAndroid(intensity);
#elif UNITY_IOS && !UNITY_EDITOR
        // Handheld.Vibrate on iOS triggers the standard short vibration; for
        // anything richer the project would need to integrate an iOS-specific
        // taptic-engine plugin. Keeping it simple for now.
        Handheld.Vibrate();
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    void TriggerAndroid(Intensity intensity)
    {
        if (vibratorService == null) return;

        // Duration in ms, amplitude 1..255 (only used on API 26+ with amplitude
        // support; older devices feel only the duration).
        int duration;
        int amplitude;
        long[] pattern = null;

        switch (intensity)
        {
            case Intensity.Light:
                duration = 12;
                amplitude = 80;
                break;
            case Intensity.Medium:
                duration = 28;
                amplitude = 160;
                break;
            case Intensity.Heavy:
                duration = 55;
                amplitude = 255;
                break;
            case Intensity.Success:
                // Two short pulses with a brief gap — distinctly "good news"
                // versus the single pulse used for catch/miss.
                pattern = new long[] { 0L, 14L, 60L, 28L };
                duration = 0;
                amplitude = 0;
                break;
            default:
                return;
        }

        try
        {
            if (sdkInt >= 26)
            {
                using (AndroidJavaClass effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                {
                    AndroidJavaObject effect;
                    if (pattern != null)
                    {
                        // -1 = no repeat. Amplitudes default to DEFAULT_AMPLITUDE
                        // when using createWaveform(long[], int).
                        effect = effectClass.CallStatic<AndroidJavaObject>(
                            "createWaveform", pattern, -1);
                    }
                    else if (hasAmplitudeControl)
                    {
                        effect = effectClass.CallStatic<AndroidJavaObject>(
                            "createOneShot", (long)duration, amplitude);
                    }
                    else
                    {
                        // Device doesn't expose amplitude control — submit a
                        // duration-only one-shot so we still get something.
                        effect = effectClass.CallStatic<AndroidJavaObject>(
                            "createOneShot", (long)duration, 255);
                    }
                    vibratorService.Call("vibrate", effect);
                }
            }
            else
            {
                // Pre-O: deprecated vibrate(long) or vibrate(long[], int).
                if (pattern != null)
                {
                    vibratorService.Call("vibrate", pattern, -1);
                }
                else
                {
                    vibratorService.Call("vibrate", (long)duration);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[HapticManager] Vibrate failed: {ex.Message}");
        }
    }
#endif
}
