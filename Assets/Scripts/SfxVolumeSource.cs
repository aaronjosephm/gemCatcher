using UnityEngine;

/// <summary>
/// Routes a scene-authored AudioSource through the game's sound-effects volume.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class SfxVolumeSource : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float baseVolume = 1f;

    private AudioSource audioSource;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    void OnEnable()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        SoundManager.OnSfxVolumeChanged += ApplyVolume;
        ApplyVolume(SoundManager.SfxVolume);
    }

    void OnDisable()
    {
        SoundManager.OnSfxVolumeChanged -= ApplyVolume;
    }

    private void ApplyVolume(float sfxVolume)
    {
        audioSource.volume = baseVolume * Mathf.Clamp01(sfxVolume);
    }
}
