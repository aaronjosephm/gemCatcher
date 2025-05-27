using UnityEngine;
using System.Collections.Generic;

public class SoundManager : MonoBehaviour
{
  [System.Serializable]
  public class SoundEffect
  {
    public string name;
    public AudioClip clip;
    [Range(0f, 1f)]
    public float volume = 1f;
    [Range(0.5f, 1.5f)]
    public float pitch = 1f;
    public bool loop = false;

    [HideInInspector]
    public AudioSource source;
  }

  public SoundEffect[] soundEffects;

  // Singleton instance
  public static SoundManager Instance { get; private set; }

  // Dictionary for quick lookup of sound effects by name
  private Dictionary<string, SoundEffect> soundDictionary;

  void Awake()
  {
    // Singleton pattern
    if (Instance == null)
    {
      Instance = this;
      DontDestroyOnLoad(gameObject);

      // Initialize the sound dictionary
      soundDictionary = new Dictionary<string, SoundEffect>();
      foreach (SoundEffect sound in soundEffects)
      {
        // Create an AudioSource component for each sound
        sound.source = gameObject.AddComponent<AudioSource>();
        sound.source.clip = sound.clip;
        sound.source.volume = sound.volume;
        sound.source.pitch = sound.pitch;
        sound.source.loop = sound.loop;

        // Add to dictionary
        soundDictionary[sound.name] = sound;
      }

      // Subscribe to game events
      GemCatcher.OnScoreChanged += PlayGemCaughtSound;
    }
    else
    {
      Destroy(gameObject);
    }
  }

  void Start()
  {
    // Play background music if available
    Play("BackgroundMusic");
  }

  // Play a sound by name
  public void Play(string name)
  {
    if (soundDictionary.TryGetValue(name, out SoundEffect sound))
    {
      sound.source.Play();
    }
  }

  // Stop a sound by name
  public void Stop(string name)
  {
    if (soundDictionary.TryGetValue(name, out SoundEffect sound))
    {
      sound.source.Stop();
    }
  }

  // Play a sound with random pitch variation
  public void PlayWithRandomPitch(string name, float minPitch = 0.9f, float maxPitch = 1.1f)
  {
    if (soundDictionary.TryGetValue(name, out SoundEffect sound))
    {
      sound.source.pitch = Random.Range(minPitch, maxPitch);
      sound.source.Play();
    }
  }

  // Event handlers
  void PlayGemCaughtSound(int newScore)
  {
    PlayWithRandomPitch("GemCaught");
  }

  public void PlayGameOverSound()
  {
    Play("GameOver");
  }

  public void PlayWinSound()
  {
    Play("Win");
  }

  void OnDestroy()
  {
    // Unsubscribe from events
    if (Instance == this)
    {
      GemCatcher.OnScoreChanged -= PlayGemCaughtSound;
    }
  }
}
