using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class UIManager
{
    private RectTransform levelProgressRoot;
    private RectTransform levelProgressFill;
    private float displayedLevelProgress;
    private bool levelCompleted;
    private bool levelCompletionOpen;
    private GameObject levelCompletionPanel;
    private RectTransform levelCompletionCard;
    private TextMeshProUGUI lightningChargeTmp;
    private static Sprite completionRoundedSprite;

    void TickLevelProgress()
    {
        if (!levelCompleted && GameState.IsPlaying && !GemCatcher.IsGameOver
            && RoundManager.Instance != null && RoundManager.Instance.HasCompletedLevel)
            ShowLevelComplete(LevelManager.GetNextLevel());

        if (levelCompletionOpen && levelCompletionCard != null)
        {
            RectTransform parent = (RectTransform)levelCompletionCard.parent;
            float scale = Mathf.Min(parent.rect.width * 0.88f / 760f, parent.rect.height * 0.86f / 820f);
            levelCompletionCard.localScale = Vector3.one * Mathf.Max(0.1f, scale);
        }
        bool visible = GameState.Mode == GameState.GameMode.Rush
            && !GameState.IsTutorial && !GemCatcher.IsGameOver && GameState.IsPlaying;
        if (visible && levelProgressRoot == null) EnsureLevelProgress();
        if (levelProgressRoot == null) return;
        levelProgressRoot.gameObject.SetActive(visible);
        if (!visible) return;
        float progress = Mathf.Clamp01((float)GemCatcher.Score / LevelManager.GetFinishLineScore());
        displayedLevelProgress = Mathf.MoveTowards(displayedLevelProgress, progress,
            Time.unscaledDeltaTime * 1.5f);
        levelProgressFill.anchorMax = new Vector2(displayedLevelProgress, 1f);
    }

    void EnsureLevelProgress()
    {
        if (UiRoot == null) return;
        // A slim green counterpart to the combo bar, directly below the score.
        Image track = ProgressImage("LevelProgress", UiRoot, new Color(0.025f, 0.09f, 0.045f, 0.85f));
        levelProgressRoot = track.rectTransform;
        levelProgressRoot.anchorMin = new Vector2(0.28f, 1f);
        levelProgressRoot.anchorMax = new Vector2(0.72f, 1f);
        levelProgressRoot.pivot = new Vector2(0.5f, 1f);
        levelProgressRoot.sizeDelta = new Vector2(0f, 12f);
        levelProgressRoot.anchoredPosition = new Vector2(0f, -114f);
        Outline rim = track.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(0.6f, 1f, 0.65f, 0.32f);
        rim.effectDistance = new Vector2(1f, -1f);
        Image fill = ProgressImage("Fill", track.transform, new Color(0.2f, 0.9f, 0.32f));
        levelProgressFill = fill.rectTransform;
        levelProgressFill.anchorMax = new Vector2(0f, 1f);
        Image shine = ProgressImage("Shine", fill.transform, new Color(0.75f, 1f, 0.7f, 0.55f));
        shine.rectTransform.anchorMin = new Vector2(0f, 0.66f);
    }

    public void ShowLevelComplete(LevelManager.LevelId? nextLevel)
    {
        if (levelCompleted || GemCatcher.IsGameOver || sceneTransitionPending
            || RoundManager.Instance == null || !RoundManager.Instance.HasCompletedLevel) return;
        EnsureHudCanvas();
        if (hudCanvas == null) return;
        levelCompleted = true;
        levelCompletionOpen = true;
        Time.timeScale = 0f;
        GameState.IsPlaying = false;
        ComboLightning.Instance?.Cancel();
        SoundManager.StopAll();
        CreditGameOverProgress(GemCatcher.Score);
        AdsManager.Instance?.NotifyLevelCompleted();
        if (nextLevel.HasValue) LevelManager.UnlockLevel(nextLevel.Value);
        SetGameplayHudVisible(false);

        levelCompletionPanel = BuildFullScreenPanel("LevelComplete",
            new Color(0f, 0f, 0f, 0.52f), out Transform content);
        Image face = ProgressImage("GoldFace", content, new Color(1f, 0.82f, 0.32f));
        face.sprite = CompletionRoundedSprite();
        face.type = Image.Type.Sliced;
        face.raycastTarget = true;
        levelCompletionCard = face.rectTransform;
        levelCompletionCard.anchorMin = levelCompletionCard.anchorMax = new Vector2(0.5f, 0.5f);
        levelCompletionCard.sizeDelta = new Vector2(760f, 820f);
        levelCompletionCard.anchoredPosition = Vector2.zero;
        Shadow shadow = face.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.06f, 0.025f, 0f, 0.24f);
        shadow.effectDistance = new Vector2(0f, -12f);
        RectTransform card = levelCompletionCard;
        Color ink = new Color(0.23f, 0.12f, 0.035f);
        Color mutedInk = new Color(0.43f, 0.27f, 0.08f);
        CompletionText(card, $"LEVEL {(int)LevelManager.SelectedLevel + 1:00} CLEARED",
            27f, mutedInk, 310f, 44f);
        CompletionText(card, nextLevel.HasValue ? "LEVEL COMPLETE!" : "YOU DID IT!",
            58f, ink, 237f, 84f);
        CompletionText(card, LevelManager.CurrentConfig.displayName, 32f,
            mutedInk, 169f, 48f);
        CompletionText(card, $"{GemCatcher.Score:N0}", 88f, ink, 70f, 110f);
        CompletionText(card, "POINTS COLLECTED", 23f, mutedInk, 0f, 34f);
        Image divider = ProgressImage("Divider", card, new Color(0.43f, 0.27f, 0.08f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0.16f, 0.5f);
        divider.rectTransform.anchorMax = new Vector2(0.84f, 0.5f);
        divider.rectTransform.sizeDelta = new Vector2(0f, 2f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -49f);
        CompletionText(card, nextLevel.HasValue
            ? $"UP NEXT: {LevelManager.GetConfig(nextLevel.Value).displayName}"
            : "Every level conquered!", 27f, mutedInk, -96f, 46f);
        if (nextLevel.HasValue)
        {
            Button next = BuildPanelButton(card, "NextLevel", "Next level",
                new Color(0.08f, 0.28f, 0.20f), Vector2.zero, new Vector2(500f, 96f),
                () => LeaveCompletedLevel(nextLevel));
            PositionCompletionButton(next, -186f);
            StyleCompletionButton(next, new Color(0.08f, 0.28f, 0.20f), new Color(1f, 0.97f, 0.85f));
        }
        Button menu = BuildPanelButton(card, "MainMenu", "Main menu",
            Color.clear, Vector2.zero, new Vector2(500f, 96f),
            () => LeaveCompletedLevel(null));
        PositionCompletionButton(menu, nextLevel.HasValue ? -297f : -186f);
        StyleCompletionButton(menu, nextLevel.HasValue ? Color.clear : new Color(0.08f, 0.28f, 0.20f),
            nextLevel.HasValue ? ink : new Color(1f, 0.97f, 0.85f));
        StartCoroutine(SpawnConfetti());
    }

    void LeaveCompletedLevel(LevelManager.LevelId? destination)
    {
        if (sceneTransitionPending || !levelCompletionOpen) return;
        sceneTransitionPending = true;
        foreach (Button button in levelCompletionPanel.GetComponentsInChildren<Button>())
            button.interactable = false;
        SoundManager.StopAll();
        // The curtain is opaque before the ad opens, and stays opaque after
        // it closes until the new scene has rendered. No old-background flash.
        SceneTransitionCurtain.LoadSceneWithGate(LevelManager.GameplaySceneName,
            done =>
            {
                if (destination.HasValue && AdsManager.Instance != null)
                    AdsManager.Instance.ShowLevelTransitionInterstitial(done);
                else done();
            },
            () =>
            {
                CreditGameOverProgress(GemCatcher.Score);
                if (destination.HasValue) LevelManager.SelectedLevel = destination.Value;
                GemCatcher.ResetScore();
                GemCatcher.ResetLives();
                ComboManager.ClearSilently();
                GameState.Mode = GameState.GameMode.Rush;
                GameState.SkipMainMenuOnLoad = destination.HasValue;
                Time.timeScale = 1f;
            });
    }

    static Sprite CompletionRoundedSprite()
    {
        if (completionRoundedSprite != null) return completionRoundedSprite;
        const int size = 128;
        const float radius = 24f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Completion Rounded Face";
        texture.hideFlags = HideFlags.DontSave;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        completionRoundedSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, Vector4.one * 26f);
        completionRoundedSprite.hideFlags = HideFlags.DontSave;
        return completionRoundedSprite;
    }

    static void StyleCompletionButton(Button button, Color background, Color text)
    {
        Image image = button.GetComponent<Image>();
        image.sprite = CompletionRoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = background;
        foreach (Outline outline in button.GetComponents<Outline>()) outline.enabled = false;
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        GameTextStyle.ApplyCelebration(label);
        label.color = text;
    }

    static Image ProgressImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        return image;
    }

    static TextMeshProUGUI ProgressText(string name, Transform parent, string text, float size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = size * 0.75f;
        label.fontSizeMax = size;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        GameTextStyle.Apply(label);
        return label;
    }

    static void CompletionText(Transform parent, string text, float size, Color color, float y, float height)
    {
        TextMeshProUGUI label = ProgressText("Label", parent, text, size, color);
        // Clean dark lettering lets the gold card breathe.
        GameTextStyle.ApplyCelebration(label);
        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(-70f, height);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    static void PositionCompletionButton(Button button, float y)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.14f, 0.5f);
        rect.anchorMax = new Vector2(0.86f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(0f, 96f);
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.enableAutoSizing = true;
        label.fontSizeMin = 30f;
        label.fontSizeMax = 38f;
        label.enableWordWrapping = false;
        rect.anchoredPosition = new Vector2(0f, y);
    }
}
