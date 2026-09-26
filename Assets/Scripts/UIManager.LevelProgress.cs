using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class UIManager
{
    private RectTransform levelProgressRoot;
    private RectTransform levelProgressFill;
    private TextMeshProUGUI levelProgressLabel;
    private Image levelProgressTint;
    private float displayedLevelProgress;
    private int lastLevelProgressScore = -1;
    private bool levelCompleted;
    private bool levelCompletionOpen;
    private GameObject levelCompletionPanel;
    private static readonly Color ProgressCyan = new Color(0.25f, 0.9f, 1f);
    private static readonly Color CompletionGold = new Color(1f, 0.82f, 0.27f);

    void TickLevelProgress()
    {
        bool visible = GameState.Mode == GameState.GameMode.Rush
            && !GameState.IsTutorial && !GemCatcher.IsGameOver
            && (GameState.IsPlaying || levelCompletionOpen);
        if (visible && levelProgressRoot == null) EnsureLevelProgress();
        if (levelProgressRoot == null) return;
        levelProgressRoot.gameObject.SetActive(visible);
        if (!visible) return;

        int target = LevelManager.GetFinishLineScore();
        int score = GemCatcher.Score;
        float progress = target > 0 ? Mathf.Clamp01((float)score / target) : 1f;
        displayedLevelProgress = Mathf.MoveTowards(displayedLevelProgress, progress,
            Time.unscaledDeltaTime * 1.5f);
        levelProgressFill.anchorMax = new Vector2(displayedLevelProgress, 1f);
        levelProgressTint.color = levelCompleted || target == 0 ? CompletionGold : ProgressCyan;
        if (score == lastLevelProgressScore) return;
        lastLevelProgressScore = score;
        string status = target == 0 ? "FINAL LEVEL · ENDLESS"
            : levelCompleted ? "COMPLETE · BONUS PLAY"
            : score >= target ? "CROSS THE FINISH LINE!"
            : $"{score:N0} / {target:N0} PTS";
        levelProgressLabel.text = $"{LevelManager.CurrentConfig.displayName.ToUpperInvariant()}\n{status}";
    }

    void EnsureLevelProgress()
    {
        if (UiRoot == null) return;
        Image backing = ProgressImage("LevelProgress", UiRoot, new Color(0.025f, 0.06f, 0.13f, 0.85f));
        levelProgressRoot = backing.rectTransform;
        levelProgressRoot.anchorMin = levelProgressRoot.anchorMax = Vector2.one;
        levelProgressRoot.pivot = Vector2.one;
        levelProgressRoot.anchoredPosition = new Vector2(-40f, -228f);
        levelProgressRoot.sizeDelta = new Vector2(500f, 106f);
        CrystalButtonStyle.Apply(backing.gameObject, new Color(0.045f, 0.12f, 0.20f));
        levelProgressLabel = ProgressText("ProgressLabel", levelProgressRoot, "", 27f, Color.white);
        RectTransform label = levelProgressLabel.rectTransform;
        label.anchorMin = new Vector2(0f, 0.36f);
        label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(18f, 0f);
        label.offsetMax = new Vector2(-18f, -8f);
        levelProgressLabel.alignment = TextAlignmentOptions.MidlineLeft;
        levelProgressLabel.enableWordWrapping = false;

        Image track = ProgressImage("Track", levelProgressRoot, new Color(0.01f, 0.025f, 0.06f, 0.95f));
        track.rectTransform.anchorMin = new Vector2(0f, 0f);
        track.rectTransform.anchorMax = new Vector2(1f, 0f);
        track.rectTransform.pivot = new Vector2(0.5f, 0f);
        track.rectTransform.sizeDelta = new Vector2(-36f, 12f);
        track.rectTransform.anchoredPosition = new Vector2(0f, 15f);
        levelProgressTint = ProgressImage("Fill", track.transform, ProgressCyan);
        levelProgressFill = levelProgressTint.rectTransform;
        levelProgressFill.anchorMax = new Vector2(0f, 1f);
        Image highlight = ProgressImage("Highlight", levelProgressFill, new Color(1f, 1f, 1f, 0.28f));
        highlight.rectTransform.anchorMin = new Vector2(0f, 0.65f);
    }

    public void ShowLevelComplete(LevelManager.LevelId nextLevel)
    {
        if (levelCompleted || GemCatcher.IsGameOver || sceneTransitionPending) return;
        EnsureHudCanvas();
        if (hudCanvas == null) return;
        levelCompleted = true;
        levelCompletionOpen = true;
        lastLevelProgressScore = -1;
        Time.timeScale = 0f;
        GameState.IsPlaying = false;
        CreditGameOverProgress(GemCatcher.Score);

        levelCompletionPanel = BuildFullScreenPanel("LevelComplete",
            new Color(0.01f, 0.025f, 0.08f, 0.83f), out Transform content);
        Image card = ProgressImage("CelebrationCard", content, Color.white);
        RectTransform cardRect = card.rectTransform;
        cardRect.anchorMin = new Vector2(0.08f, 0.5f);
        cardRect.anchorMax = new Vector2(0.92f, 0.5f);
        cardRect.sizeDelta = new Vector2(0f, 680f);
        CrystalButtonStyle.Apply(card.gameObject, new Color(0.065f, 0.14f, 0.25f));

        CompletionText(cardRect, "LEVEL COMPLETE!", 55f, CompletionGold, 245f, 80f);
        CompletionText(cardRect, LevelManager.CurrentConfig.displayName,
            36f, Color.white, 170f, 65f);
        CompletionText(cardRect, $"{GemCatcher.Score:N0} POINTS\nReady for {LevelManager.GetConfig(nextLevel).displayName}?",
            30f, new Color(0.72f, 0.88f, 1f), 75f, 100f);
        Button next = BuildPanelButton(cardRect, "NextLevel", "Next level",
            new Color(0.1f, 0.50f, 0.62f), Vector2.zero, new Vector2(500f, 100f),
            () => StartNextLevel(nextLevel));
        PositionCompletionButton(next, -50f);
        Button keep = BuildPanelButton(cardRect, "KeepPlaying", "Keep playing this level",
            new Color(0.19f, 0.25f, 0.36f), Vector2.zero, new Vector2(500f, 100f), KeepPlayingCompletedLevel);
        PositionCompletionButton(keep, -175f);
        CompletionText(cardRect, "Keep playing for a higher score until you lose your lives.",
            24f, new Color(0.7f, 0.78f, 0.86f), -275f, 65f);
        StartCoroutine(SpawnConfetti());
    }

    void KeepPlayingCompletedLevel()
    {
        if (sceneTransitionPending || !levelCompletionOpen) return;
        levelCompletionOpen = false;
        Destroy(levelCompletionPanel);
        GameState.IsPlaying = true;
        Time.timeScale = 1f;
        HandleComboChanged(ComboManager.CurrentCombo, ComboManager.CurrentMultiplier);
    }

    void StartNextLevel(LevelManager.LevelId nextLevel)
    {
        if (sceneTransitionPending || !levelCompletionOpen) return;
        sceneTransitionPending = true;
        foreach (Button button in levelCompletionPanel.GetComponentsInChildren<Button>())
            button.interactable = false;
        SoundManager.StopAll();
        void LoadNext()
        {
            // Bank against the old level before changing the selection. This is
            // a fresh run, with fresh lives/combo and one rewarded revival.
            CreditGameOverProgress(GemCatcher.Score);
            LevelManager.SelectedLevel = nextLevel;
            GemCatcher.ResetScore();
            GemCatcher.ResetLives();
            ComboManager.ClearSilently();
            GameState.Mode = GameState.GameMode.Rush;
            GameState.SkipMainMenuOnLoad = true;
            Time.timeScale = 1f;
            SceneTransitionCurtain.LoadScene(LevelManager.GameplaySceneName);
        }
        if (AdsManager.Instance != null)
            AdsManager.Instance.ShowLevelTransitionInterstitial(LoadNext);
        else LoadNext();
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
        RectTransform rect = ProgressText("Label", parent, text, size, color).rectTransform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(-60f, height);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    static void PositionCompletionButton(Button button, float y)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.08f, 0.5f);
        rect.anchorMax = new Vector2(0.92f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(0f, 100f);
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.enableAutoSizing = true;
        label.fontSizeMin = 26f;
        label.fontSizeMax = 42f;
        label.enableWordWrapping = false;
        rect.anchoredPosition = new Vector2(0f, y);
    }
}
