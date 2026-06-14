using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// Centralised audio playback.
//
// Sounds can be configured two ways:
//   1. Drag AudioClips into the `soundEffects` array in the Inspector.
//   2. Leave them empty and rely on the procedural fallbacks below — short synth tones
//      generated at runtime so the game has audible feedback without any asset setup.
//      Set `generateProceduralFallbacks = false` to disable.
//
// Named sounds the game expects:
//   "GemCaught", "GemMissed", "Bounce", "ObstacleBounce", "WallBounce",
//   "CatcherMove", "GameOver", "Win", "BonusLife", "PowerUp",
//   "Bomb", "Milestone",
//   "BackgroundMusic" (looping, no fallback)
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

    /// <summary>
    /// PlayerPrefs key for the sound on/off toggle exposed in the settings panel.
    /// </summary>
    public const string SoundPrefKey = "SoundEnabled";

    /// <summary>
    /// Master sound toggle. Backed by PlayerPrefs and mirrored onto
    /// <see cref="AudioListener.volume"/> so a single flag silences both
    /// the procedural fallbacks and any inspector-assigned clips without
    /// having to gate every Play() call.
    /// </summary>
    public static bool SoundEnabled
    {
        get => PlayerPrefs.GetInt(SoundPrefKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(SoundPrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
            AudioListener.volume = value ? 1f : 0f;
        }
    }

    private Dictionary<string, SoundEffect> soundDictionary;

    // Bootstraps a SoundManager into the scene after load if the developer hasn't
    // placed one manually. Means the game gets audio without any scene setup.
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

        // Apply any previously-saved mute preference before we start playing
        // anything (e.g. background music in Start()).
        AudioListener.volume = SoundEnabled ? 1f : 0f;

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

        // Create an AudioSource per sound now that the dictionary is fully populated.
        foreach (SoundEffect sound in soundDictionary.Values)
        {
            if (sound.source != null) continue;
            sound.source = gameObject.AddComponent<AudioSource>();
            sound.source.clip = sound.clip;
            sound.source.volume = sound.volume;
            sound.source.pitch = sound.pitch;
            sound.source.loop = sound.loop;
            sound.source.playOnAwake = false;
        }

        GemCatcher.OnGemCaught += HandleGemCaught;
        GemCatcher.OnGemMissed += HandleGemMissed;
        GemCatcher.OnBonusLifeAwarded += HandleBonusLifeAwarded;
        GemCatcher.OnBombHit += HandleBombHit;
        GemCatcher.OnGoldBarCaught += HandleGoldBarCaught;
        MilestoneTracker.OnMilestoneReached += HandleMilestoneReached;
    }

    void Start()
    {
        // Play background music only if a clip was actually assigned (no procedural music).
        if (soundDictionary.TryGetValue("BackgroundMusic", out SoundEffect bgm) && bgm.clip != null)
        {
            bgm.source.Play();
        }
    }

    // ----- Public API -----

    public void Play(string soundName)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
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

    public void PlayWithRandomPitch(string soundName, float minPitch = 0.9f, float maxPitch = 1.1f)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
            sound.source.pitch = Random.Range(minPitch, maxPitch);
            sound.source.Play();
        }
    }

    public void PlayGameOverSound() => Play("GameOver");
    public void PlayWinSound() => Play("Win");

    // ----- Event handlers -----

    void HandleGemCaught(int amount, Vector3 worldPosition)
    {
        // Pitch climbs with the combo so a 10-streak literally sounds higher
        // and brighter than a single catch. Capped at 1.6x so it never gets
        // chipmunk-level shrill.
        float comboPitch = 1f + Mathf.Clamp01(ComboManager.CurrentCombo / 10f) * 0.6f;
        PlayWithFixedPitch("GemCaught", comboPitch);
    }

    // Plays a sound at a specific pitch. Used for pitch-laddering combos so
    // the shift isn't random.
    private void PlayWithFixedPitch(string soundName, float pitch)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundEffect sound) && sound.source != null)
        {
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

    void HandleGoldBarCaught(Vector3 worldPosition)
    {
        // Distinct triumphant cue — longer and brighter than the regular catch
        // sound so the +500 jackpot reads as the moment it is.
        Play("GoldBar");
    }

    void HandleMilestoneReached(MilestoneTracker.Milestone milestone)
    {
        Play("Milestone");
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            GemCatcher.OnGemCaught -= HandleGemCaught;
            GemCatcher.OnGemMissed -= HandleGemMissed;
            GemCatcher.OnBonusLifeAwarded -= HandleBonusLifeAwarded;
            GemCatcher.OnBombHit -= HandleBombHit;
            GemCatcher.OnGoldBarCaught -= HandleGoldBarCaught;
            MilestoneTracker.OnMilestoneReached -= HandleMilestoneReached;
            Instance = null;
        }
    }

    // ----- Procedural fallback audio -----
    //
    // These produce simple, short synth tones (sine + exponential decay envelope). They're
    // intended as scaffolding; replace with proper SFX by assigning AudioClips in the Inspector.

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
        // Quick rising major-third chime — short and celebratory; pairs with the
        // "EXTRA LIFE!" banner without stepping on the regular catch sound.
        RegisterFallback("BonusLife",     () => CreateArpeggio(new[] { 659f, 880f, 1175f, 1568f }, 0.35f, 0.32f));
        // Brighter, faster fanfare for power-up pickups so it's distinctly more
        // exciting than a normal catch but doesn't feel as triumphant as a bonus life.
        RegisterFallback("PowerUp",       () => CreateArpeggio(new[] { 880f, 1175f, 1568f, 2093f }, 0.40f, 0.30f));
        // Heavy descending impact — bomb explosion. Lower start frequency than
        // GemMissed and longer duration so it reads as "you really screwed up".
        RegisterFallback("Bomb",          () => CreateSweep(220f, 60f, 0.55f, 0.40f));
        // Five-note rising fanfare for milestone celebrations. Brighter and
        // longer than the bonus-life chime so the player notices it through
        // any other audio chaos.
        RegisterFallback("Milestone",     () => CreateArpeggio(new[] { 523f, 698f, 880f, 1175f, 1568f }, 0.65f, 0.32f));
        // Six-note descending-then-resolving jackpot fanfare for Gold Bar
        // catches. Deliberately distinct from the regular catch chord and
        // longer than the bonus-life chime — the +500 should sound like
        // hitting a slot-machine win, not just another catch.
        RegisterFallback("GoldBar",       () => CreateArpeggio(new[] { 1175f, 880f, 1318f, 1568f, 1976f, 2349f }, 0.85f, 0.36f));
    }

    private void RegisterFallback(string soundName, Func<AudioClip> generator)
    {
        // Don't override an Inspector-assigned clip.
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
            // Logarithmic frequency interpolation for a natural-sounding sweep.
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

    // Short linear attack + exponential decay. Prevents the audible "click" you'd get from
    // starting/ending a sine wave at non-zero amplitude.
    private static float Envelope(float t, float duration)
    {
        const float attack = 0.005f;
        if (t < attack) return t / attack;
        float decayT = (t - attack) / Mathf.Max(0.0001f, duration - attack);
        return Mathf.Exp(-3.5f * decayT);
    }
}
