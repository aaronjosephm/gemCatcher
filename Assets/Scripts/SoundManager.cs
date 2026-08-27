using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

// Centralised audio playback.
//
// Named sounds the game expects:
//   "GemCaught", "GemMissed", "Bounce", "ObstacleBounce", "WallBounce",
//   "CatcherMove", "GameOver", "Win", "BonusLife", "PowerUp",
//   "Bomb", "Milestone",
//   "BackgroundMusic" (looping — only while GameState.IsPlaying and not game-over)
public class SoundManager : MonoBehaviour
{
    [Serializable]
    public class SoundEffect
    {
        public string name;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.5f, 1.5f)] public float pitch = 1f;
        public bool loop = false;

        [HideInInspector] public AudioSource source;
    }

    public SoundEffect[] soundEffects;

    [Tooltip("If true, sounds without an assigned AudioClip are filled in with simple " +
             "procedurally-generated synth tones so the game always has audio.")]
    public bool generateProceduralFallbacks = true;

    public static SoundManager Instance { get; private set; }

    public const string MusicVolumePrefKey = "MusicVolume";
    public const string SfxVolumePrefKey = "SfxVolume";

    /// <summary>Legacy mute key — if set to 0 on first run of the new prefs, volumes start at 0.</summary>
    public const string SoundPrefKey = "SoundEnabled";

    /// <summary>0–1 music volume (background track only). Persisted in PlayerPrefs.</summary>
    public static float MusicVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePrefKey, DefaultMusicVolume()));
        set
        {
            PlayerPrefs.SetFloat(MusicVolumePrefKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            if (Instance != null) Instance.ApplyVolumes();
        }
    }

    /// <summary>0–1 sound-effects volume. Persisted in PlayerPrefs.</summary>
    public static float SfxVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey, DefaultSfxVolume()));
        set
        {
            PlayerPrefs.SetFloat(SfxVolumePrefKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            if (Instance != null) Instance.ApplyVolumes();
        }
    }

    // Kept for any old callers; maps to both volumes being non-zero.
    public static bool SoundEnabled
    {
        get => MusicVolume > 0.001f || SfxVolume > 0.001f;
        set
        {
            if (value)
            {
                if (MusicVolume < 0.001f) MusicVolume = 0.7f;
                if (SfxVolume < 0.001f) SfxVolume = 1f;
            }
            else
            {
                MusicVolume = 0f;
                SfxVolume = 0f;
            }
        }
    }

    private Dictionary<string, SoundEffect> soundDictionary;
    private bool musicWasWanted;

    static float DefaultMusicVolume()
    {
        // Honor a previous global mute if the new keys were never written.
        if (!PlayerPrefs.HasKey(MusicVolumePrefKey) && PlayerPrefs.GetInt(SoundPrefKey, 1) == 0)
            return 0f;
        return 0.7f;
    }

    static float DefaultSfxVolume()
    {
        if (!PlayerPrefs.HasKey(SfxVolumePrefKey) && PlayerPrefs.GetInt(SoundPrefKey, 1) == 0)
            return 0f;
        return 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null) return;
        if (FindObjectOfType<SoundManager>() != null) return;

        GameObject go = new GameObject("SoundManager (auto)");
        go.AddComponent<SoundManager>();
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

        // Per-bus volumes — do not mute the whole AudioListener.
        AudioListener.volume = 1f;

        soundDictionary = new Dictionary<string, SoundEffect>();

        if (soundEffects != null)
        {
            foreach (SoundEffect sound in soundEffects)
            {
                if (sound == null || string.IsNullOrEmpty(sound.name)) continue;
                soundDictionary[sound.name] = sound;
            }
        }

        if (generateProceduralFallbacks)
        {
            RegisterFallbacks();
        }

        EnsureBackgroundMusicFromResources();
        EnsureMenuMusicFromResources();

        // Re-check background music on scene reload (level switch).
        SceneManager.sceneLoaded += OnSceneLoadedMusic;

        foreach (SoundEffect sound in soundDictionary.Values)
        {
            if (sound.source != null) continue;
            sound.source = gameObject.AddComponent<AudioSource>();
            sound.source.clip = sound.clip;
            sound.source.pitch = sound.pitch;
            sound.source.loop = sound.loop;
            sound.source.playOnAwake = false;
        }

        ApplyVolumes();

        GemCatcher.OnGemCaught += HandleGemCaught;
        GemCatcher.OnGemMissed += HandleGemMissed;
        GemCatcher.OnBonusLifeAwarded += HandleBonusLifeAwarded;
        GemCatcher.OnBombHit += HandleBombHit;
        MilestoneTracker.OnMilestoneReached += HandleMilestoneReached;
        GemCatcher.OnGameOver += HandleGameOver;
    }

    void Update()
    {
        SyncGameplayMusic();
        SyncMenuMusic();
    }

    // ----- Public API -----

    public void Play(string soundName)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
            if (IsMusic(soundName)) return; // music is driven by SyncGameplayMusic only
            sound.source.volume = sound.volume * SfxVolume;
            sound.source.pitch = 1f;
            sound.source.Play();
        }
    }

    /// <summary>Play a sound effect at a specific pitch.</summary>
    public void PlayWithPitch(string soundName, float pitch)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
            sound.source.volume = sound.volume * SfxVolume;
            sound.source.pitch = pitch;
            sound.source.Play();
        }
    }

    public void Stop(string soundName)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
            sound.source.Stop();
        }
    }

    /// <summary>
    /// Stops all audio sources immediately. Call before scene transitions to
    /// prevent brief music blips caused by state resets.
    /// </summary>
    public static void StopAll()
    {
        if (Instance == null || Instance.soundDictionary == null) return;
        foreach (var kvp in Instance.soundDictionary)
        {
            if (kvp.Value.source != null) kvp.Value.source.Stop();
        }
    }

    public void PlayWithRandomPitch(string soundName, float minPitch = 0.9f, float maxPitch = 1.1f)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
            sound.source.volume = sound.volume * SfxVolume;
            sound.source.pitch = Random.Range(minPitch, maxPitch);
            sound.source.Play();
        }
    }

    public void PlayGameOverSound() => Play("GameOver");
    public void PlayWinSound() => Play("Win");

    public void ApplyVolumes()
    {
        if (soundDictionary == null) return;
        foreach (var kv in soundDictionary)
        {
            SoundEffect sound = kv.Value;
            if (sound.source == null) continue;
            float bus = IsMusic(kv.Key) ? MusicVolume : SfxVolume;
            sound.source.volume = sound.volume * bus;
        }
    }

    // ----- Music gating -----

    void SyncGameplayMusic()
    {
        if (soundDictionary == null) return;
        if (!soundDictionary.TryGetValue("BackgroundMusic", out SoundEffect bgm)
            || bgm.source == null || bgm.clip == null)
        {
            return;
        }

        bool wantMusic = GameState.IsPlaying && !GemCatcher.IsGameOver && !GameState.IsTutorial;
        bgm.source.volume = bgm.volume * MusicVolume;

        if (wantMusic)
        {
            if (!bgm.source.isPlaying)
            {
                // Fresh start each time a round begins (menu → play, or after game over).
                if (!musicWasWanted) bgm.source.time = 0f;
                bgm.source.Play();
            }
        }
        else if (bgm.source.isPlaying)
        {
            bgm.source.Stop();
        }

        musicWasWanted = wantMusic;
    }

    static bool IsMusic(string soundName) =>
        string.Equals(soundName, "BackgroundMusic", StringComparison.Ordinal)
        || string.Equals(soundName, "MenuMusic", StringComparison.Ordinal);

    /// <summary>
    /// Plays menu music when the player is on the main menu or settings
    /// (i.e. not actively playing and not game over). Stops when gameplay starts.
    /// </summary>
    void SyncMenuMusic()
    {
        if (soundDictionary == null) return;
        if (!soundDictionary.TryGetValue("MenuMusic", out SoundEffect menu)
            || menu.source == null || menu.clip == null)
        {
            return;
        }

        bool wantMenu = (!GameState.IsPlaying && !GemCatcher.IsGameOver) || GameState.IsTutorial;
        menu.source.volume = menu.volume * MusicVolume;

        if (wantMenu)
        {
            if (!menu.source.isPlaying)
            {
                menu.source.Play();
            }
        }
        else if (menu.source.isPlaying)
        {
            menu.source.Stop();
        }
    }

    // ----- Event handlers -----

    void HandleGemCaught(int amount, Vector3 worldPosition)
    {
        float comboPitch = 1f + Mathf.Clamp01(ComboManager.CurrentCombo / 10f) * 0.6f;
        PlayWithFixedPitch("GemCaught", comboPitch);
    }

    private void PlayWithFixedPitch(string soundName, float pitch)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
            sound.source.volume = sound.volume * SfxVolume;
            sound.source.pitch = pitch;
            sound.source.Play();
        }
    }

    void HandleGemMissed(int amount, Vector3 worldPosition)
    {
        PlayWithRandomPitch("GemMissed", 0.85f, 1.05f);
    }

    void HandleBonusLifeAwarded(int count)
    {
        Play("BonusLife");
    }

    void HandleBombHit(Vector3 worldPosition)
    {
        PlayWithRandomPitch("Bomb", 0.85f, 1.05f);
    }

    void HandleMilestoneReached(MilestoneTracker.Milestone milestone)
    {
        Play("Milestone");
    }

    void HandleGameOver()
    {
        // Stop BGM immediately; SyncGameplayMusic will also catch this next frame.
        Stop("BackgroundMusic");
        musicWasWanted = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            GemCatcher.OnGemCaught -= HandleGemCaught;
            GemCatcher.OnGemMissed -= HandleGemMissed;
            GemCatcher.OnBonusLifeAwarded -= HandleBonusLifeAwarded;
            GemCatcher.OnBombHit -= HandleBombHit;
            MilestoneTracker.OnMilestoneReached -= HandleMilestoneReached;
            GemCatcher.OnGameOver -= HandleGameOver;
            Instance = null;
        }
    }

    private void EnsureBackgroundMusicFromResources()
    {
        string desiredPath = LevelManager.CurrentConfig.musicResource ?? "Audio/BackgroundMusic";
        AudioClip clip = Resources.Load<AudioClip>(desiredPath);
        if (clip == null)
            clip = Resources.Load<AudioClip>("Audio/BackgroundMusic");
        if (clip == null)
        {
            Debug.LogWarning("[SoundManager] No background music found — BGM skipped.");
            return;
        }

        // If the clip is already the correct one, just ensure it loops.
        if (soundDictionary.TryGetValue("BackgroundMusic", out SoundEffect existing)
            && existing.clip == clip)
        {
            existing.loop = true;
            return;
        }

        // Stop any currently playing BGM before swapping the clip.
        Stop("BackgroundMusic");

        SoundEffect bgm = existing ?? new SoundEffect
        {
            name = "BackgroundMusic",
            volume = 0.55f,
            pitch = 1f,
        };
        bgm.clip = clip;
        bgm.loop = true;
        if (bgm.volume <= 0f) bgm.volume = 0.55f;
        soundDictionary["BackgroundMusic"] = bgm;
    }

    private void OnSceneLoadedMusic(Scene scene, LoadSceneMode mode)
    {
        EnsureBackgroundMusicFromResources();
        // Ensure the new clip has an AudioSource configured.
        if (soundDictionary.TryGetValue("BackgroundMusic", out SoundEffect bgm) && bgm.source != null)
        {
            bgm.source.clip = bgm.clip;
        }
    }

    private void EnsureMenuMusicFromResources()
    {
        if (soundDictionary.TryGetValue("MenuMusic", out SoundEffect existing)
            && existing.clip != null)
        {
            existing.loop = true;
            return;
        }

        AudioClip clip = Resources.Load<AudioClip>("Audio/MenuMusic");
        if (clip == null)
        {
            Debug.LogWarning("[SoundManager] No Resources/Audio/MenuMusic found — menu music skipped.");
            return;
        }

        SoundEffect menu = existing ?? new SoundEffect
        {
            name = "MenuMusic",
            volume = 0.45f,
            pitch = 1f,
        };
        menu.clip = clip;
        menu.loop = true;
        if (menu.volume <= 0f) menu.volume = 0.45f;
        soundDictionary["MenuMusic"] = menu;
    }

    // ----- Procedural fallback audio -----

    private void RegisterFallbacks()
    {
        RegisterFallback("GemCaught",     () => CreateChord(new[] { 880f, 1320f }, 0.18f, 0.30f));
        RegisterFallback("GemMissed",     () => CreateSweep(440f, 110f, 0.35f, 0.30f));
        RegisterFallback("Bounce",        () => CreateBeep(520f, 0.06f, 0.22f));
        RegisterFallback("ObstacleBounce", () => CreateBeep(330f, 0.07f, 0.22f));
        RegisterFallback("WallBounce",    () => CreateBeep(400f, 0.08f, 0.24f));
        RegisterFallback("CatcherMove",   () => CreateBeep(660f, 0.04f, 0.18f));
        RegisterFallback("GameOver",      () => CreateSweep(330f, 80f, 0.75f, 0.32f));
        RegisterFallback("Win",           () => CreateArpeggio(new[] { 523f, 659f, 784f, 1046f }, 0.45f, 0.30f));
        RegisterFallback("BonusLife",     () => CreateArpeggio(new[] { 659f, 880f, 1175f, 1568f }, 0.35f, 0.32f));
        RegisterFallback("PowerUp",       () => CreateArpeggio(new[] { 880f, 1175f, 1568f, 2093f }, 0.40f, 0.30f));
        RegisterFallback("Bomb",          () => CreateSweep(220f, 60f, 0.55f, 0.40f));
        RegisterFallback("Milestone",     () => CreateArpeggio(new[] { 523f, 698f, 880f, 1175f, 1568f }, 0.65f, 0.32f));
        RegisterFallback("StageUp",       () => CreateSweep(60f, 30f, 1.2f, 0.50f));  // Deep rumble
    }

    private void RegisterFallback(string soundName, Func<AudioClip> generator)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect existing) && existing.clip != null)
        {
            return;
        }

        AudioClip generated = generator();
        if (existing == null)
        {
            existing = new SoundEffect { name = soundName, volume = 1f, pitch = 1f, loop = false };
            soundDictionary[soundName] = existing;
        }
        existing.clip = generated;
    }

    private const int SampleRate = 44100;

    private static AudioClip CreateBeep(float frequency, float duration, float volume)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(SampleRate * duration));
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            samples[i] = volume * Envelope(t, duration) * Mathf.Sin(2f * Mathf.PI * frequency * t);
        }
        AudioClip clip = AudioClip.Create($"beep_{frequency:F0}_{duration:F2}", count, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateChord(float[] frequencies, float duration, float volume)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(SampleRate * duration));
        float[] samples = new float[count];
        float perVoice = volume / Mathf.Max(1, frequencies.Length);
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float env = Envelope(t, duration);
            float sample = 0f;
            for (int v = 0; v < frequencies.Length; v++)
            {
                sample += Mathf.Sin(2f * Mathf.PI * frequencies[v] * t);
            }
            samples[i] = perVoice * env * sample;
        }
        AudioClip clip = AudioClip.Create($"chord_{duration:F2}", count, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateSweep(float startFreq, float endFreq, float duration, float volume)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(SampleRate * duration));
        float[] samples = new float[count];
        float phase = 0f;
        float invSampleRate = 1f / SampleRate;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float u = t / duration;
            float freq = Mathf.Lerp(startFreq, endFreq, u);
            phase += 2f * Mathf.PI * freq * invSampleRate;
            samples[i] = volume * Envelope(t, duration) * Mathf.Sin(phase);
        }
        AudioClip clip = AudioClip.Create($"sweep_{startFreq:F0}_{endFreq:F0}", count, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateArpeggio(float[] frequencies, float duration, float volume)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(SampleRate * duration));
        float[] samples = new float[count];
        float stepDuration = duration / frequencies.Length;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            int step = Mathf.Clamp((int)(t / stepDuration), 0, frequencies.Length - 1);
            float localT = t - step * stepDuration;
            float env = Envelope(localT, stepDuration);
            samples[i] = volume * env * Mathf.Sin(2f * Mathf.PI * frequencies[step] * t);
        }
        AudioClip clip = AudioClip.Create($"arp_{duration:F2}", count, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static float Envelope(float t, float duration)
    {
        const float attack = 0.005f;
        if (t < attack) return t / attack;
        float decayT = (t - attack) / Mathf.Max(0.0001f, duration - attack);
        return Mathf.Exp(-3.5f * decayT);
    }
}
