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

    void TickLevelProgress()
    {
        if (!levelCompleted && GameState.IsPlaying && !GemCatcher.IsGameOver
            && RoundManager.Instance != null && RoundManager.Instance.HasCompletedLevel)
            ShowLevelComplete(LevelManager.GetNextLevel());

        if (levelCompletionOpen && levelCompletionCard != null)
        {
            RectTransform parent = (RectTransform)levelCompletionCard.parent;
            float scale = Mathf.Min(parent.rect.width * 0.9f / 860f, parent.rect.height * 0.86f / 800f);
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
        // A single gold face, with no stacked rims, bevel sprites, or outline components.
        Image outer = ProgressImage("GoldFace", content, new Color(1f, 0.83f, 0.25f));
        levelCompletionCard = outer.rectTransform;
        levelCompletionCard.anchorMin = levelCompletionCard.anchorMax = new Vector2(0.5f, 0.5f);
        levelCompletionCard.sizeDelta = new Vector2(860f, 800f);
        levelCompletionCard.anchoredPosition = new Vector2(0f, 15f);
        RectTransform card = outer.rectTransform;
        CompletionText(card, nextLevel.HasValue ? "LEVEL\nCOMPLETE!" : "ALL LEVELS\nCOMPLETE!",
            78f, Color.white, 210f, 190f);
        CompletionText(card, LevelManager.CurrentConfig.displayName, 38f,
            new Color(0.38f, 0.16f, 0.025f), 70f, 65f);
        CompletionText(card, $"{GemCatcher.Score:N0}", 60f, Color.white, -5f, 80f);
        if (nextLevel.HasValue)
        {
            Button next = BuildPanelButton(card, "NextLevel", "Next level",
                new Color(0.12f, 0.65f, 0.24f), Vector2.zero, new Vector2(500f, 108f),
                () => LeaveCompletedLevel(nextLevel));
            PositionCompletionButton(next, -125f);
        }
        Button menu = BuildPanelButton(card, "MainMenu", "Main menu",
            new Color(0.04f, 0.42f, 0.86f), Vector2.zero, new Vector2(500f, 108f),
            () => LeaveCompletedLevel(null));
        PositionCompletionButton(menu, nextLevel.HasValue ? -260f : -160f);
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
        // Match the warm brown lettering outline of the illustrated ad card.
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
        rect.sizeDelta = new Vector2(0f, 108f);
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.enableAutoSizing = true;
        label.fontSizeMin = 30f;
        label.fontSizeMax = 48f;
        label.enableWordWrapping = false;
        rect.anchoredPosition = new Vector2(0f, y);
    }
}
