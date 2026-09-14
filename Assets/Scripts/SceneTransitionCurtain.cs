using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keeps an opaque cover alive while one scene is replaced by another, then
/// reveals the destination only after its first frame is ready.
/// </summary>
public sealed class SceneTransitionCurtain : MonoBehaviour
{
    private const float FadeToBlackDuration = 0.16f;
    private const float FadeFromBlackDuration = 0.20f;
    private static SceneTransitionCurtain activeTransition;

    private Image curtainImage;

    public static void LoadScene(string sceneName)
    {
        if (activeTransition != null) return;

        GameObject root = new GameObject(
            "Scene Transition Curtain",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(SceneTransitionCurtain));
        DontDestroyOnLoad(root);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);

        GameObject cover = new GameObject(
            "Black Cover",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        cover.transform.SetParent(root.transform, false);
        RectTransform coverRect = cover.GetComponent<RectTransform>();
        coverRect.anchorMin = Vector2.zero;
        coverRect.anchorMax = Vector2.one;
        coverRect.offsetMin = Vector2.zero;
        coverRect.offsetMax = Vector2.zero;

        SceneTransitionCurtain transition = root.GetComponent<SceneTransitionCurtain>();
        transition.curtainImage = cover.GetComponent<Image>();
        transition.curtainImage.color = Color.clear;
        transition.curtainImage.raycastTarget = true;
        activeTransition = transition;
        transition.StartCoroutine(transition.Run(sceneName));
    }

    private IEnumerator Run(string sceneName)
    {
        yield return Fade(0f, 1f, FadeToBlackDuration);

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (load == null)
        {
            Debug.LogError($"Unable to start scene transition to '{sceneName}'.");
            Destroy(gameObject);
            yield break;
        }

        while (!load.isDone)
            yield return null;

        // Let the destination scene finish Awake/Start and submit one covered
        // frame before revealing it.
        yield return null;
        yield return null;
        yield return Fade(1f, 0f, FadeFromBlackDuration);
        Destroy(gameObject);
    }

    private IEnumerator Fade(float fromAlpha, float toAlpha, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = progress * progress * (3f - 2f * progress);
            curtainImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(fromAlpha, toAlpha, eased));
            yield return null;
        }

        curtainImage.color = new Color(0f, 0f, 0f, toAlpha);
    }

    private void OnDestroy()
    {
        if (activeTransition == this)
            activeTransition = null;
    }
}
