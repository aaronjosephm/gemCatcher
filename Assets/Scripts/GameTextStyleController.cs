using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the shared game typography to scene text and runtime-created labels.
/// </summary>
[DefaultExecutionOrder(-11000)]
public sealed class GameTextStyleController : MonoBehaviour
{
    private static GameTextStyleController instance;
    private bool isApplyingStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        instance = null;
        GameTextStyle.ResetCache();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        new GameObject(nameof(GameTextStyleController)).AddComponent<GameTextStyleController>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyToLoadedText();
    }

    void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToLoadedText();
    }

    void OnTextChanged(Object changedObject)
    {
        if (!isApplyingStyle && changedObject is TMP_Text text)
        {
            Apply(text);
        }
    }

    void ApplyToLoadedText()
    {
        TMP_Text[] texts = FindObjectsByType<TMP_Text>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        isApplyingStyle = true;
        foreach (TMP_Text text in texts)
        {
            GameTextStyle.Apply(text);
        }
        isApplyingStyle = false;
    }

    void Apply(TMP_Text text)
    {
        isApplyingStyle = true;
        GameTextStyle.Apply(text);
        isApplyingStyle = false;
    }
}
