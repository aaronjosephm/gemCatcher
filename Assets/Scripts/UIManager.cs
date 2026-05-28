using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro; // Add TextMeshPro namespace

public class UIManager : MonoBehaviour
{
  [Header("UI Elements")]
  public Text scoreText;
  public Text highScoreText;
  public GameObject gameOverPanel;
  public Text finalScoreText;
  public Button restartButton;
  public Text placementTimerText; // Text at the bottom for catcher placement timer

  [Header("Gem Speedup Timer")]
  public TextMeshProUGUI gemSpeedupTimerText; // TextMeshPro component for the countdown
  public float fadeOutDuration = 0.5f; // Duration of the fade out animation in seconds

  [Header("HUD (auto-created if not assigned)")]
  [Tooltip("Top-right score tracker. Auto-created on a screen-space canvas if left blank.")]
  public TextMeshProUGUI scoreDisplay;
  [Tooltip("Top-left lives tracker. Auto-created on a screen-space canvas if left blank.")]
  public TextMeshProUGUI livesDisplay;
  [Tooltip("Screen-space canvas used for the score tracker and floating score pop-ups. " +
           "Auto-created if left blank and no other canvas is in the scene.")]
  public Canvas hudCanvas;

  // RectTransform that fills Screen.safeArea (sized by SafeAreaFitter). All UI we
  // build at runtime is parented to this so it never sits under the notch / camera
  // cutout / gesture region. Falls back to hudCanvas.transform if not yet built.
  private RectTransform safeAreaRoot;
  private Transform UiRoot => safeAreaRoot != null ? safeAreaRoot.transform : (hudCanvas != null ? hudCanvas.transform : null);

  [Header("Game Settings")]
  public float gameOverDelay = 1.0f;

  private int highScore = 0;
  private bool gameIsOver = false;
  private bool isFadingOut = false;
  private float fadeTimer = 0f;
  private Color originalTextColor;

  // TMP-equivalent of `finalScoreText` for the auto-built game over panel. Now only
  // shows the headline "Final Score: X" line; the gem breakdown lives in the icon list.
  private TextMeshProUGUI autoFinalScoreText;

  // Container that holds one `[icon][× N]` row per caught gem on the auto-built panel.
  // Rebuilt every time we show the game-over screen.
  private RectTransform autoGemIconsContainer;
  private readonly List<GameObject> spawnedIconRows = new List<GameObject>();

  [Header("Menu Panels (auto-created if not assigned)")]
  [Tooltip("Main menu shown at scene load. Hosts Play / Leaderboard / Help / Exit buttons.")]
  public GameObject mainMenuPanel;
  [Tooltip("Top-scores panel reached from the main menu. Populated with mock data for now.")]
  public GameObject leaderboardPanel;
  [Tooltip("How-to-play panel reached from the main menu.")]
  public GameObject helpPanel;

  // Mock leaderboard. Replace with real persistence (PlayerPrefs/server) when ready.
  private static readonly (string name, int score)[] MockLeaderboard = new[]
  {
    ("ACE", 1280),
    ("BEX",  940),
    ("CYR",  720),
    ("DIA",  580),
    ("EVA",  460),
    ("FOX",  380),
    ("GUS",  290),
    ("HAL",  220),
    ("IVY",  160),
    ("JAX",  100),
  };

  private ObjectPooler objectPooler;

  void Start()
  {
    // Initialize UI
    if (gameOverPanel != null)
    {
      gameOverPanel.SetActive(false);
    }

    // Load high score from PlayerPrefs
    highScore = PlayerPrefs.GetInt("HighScore", 0);
    UpdateHighScoreText();

    // Subscribe to score change events
    GemCatcher.OnScoreChanged += UpdateScore;
    GemCatcher.OnLivesChanged += UpdateLives;
    GemCatcher.OnGameOver += HandleGameOverEvent;
    GemCatcher.OnGemCaught += HandleGemCaught;
    GemCatcher.OnGemMissed += HandleGemMissed;

    // Make sure we have a top-right score tracker, top-left lives tracker, and
    // a game-over panel even if nothing was wired up in the Inspector.
    EnsureHudCanvas();
    EnsureScoreDisplay();
    EnsureLivesDisplay();
    EnsureGameOverPanel();
    EnsureMainMenuPanel();
    EnsureLeaderboardPanel();
    EnsureHelpPanel();
    UpdateLives(GemCatcher.Lives);

    // Subscribe to placement-phase events from the pooler.
    objectPooler = FindObjectOfType<ObjectPooler>();
    if (objectPooler != null)
    {
      objectPooler.PlacementPhaseStarted += OnPlacementPhaseStarted;
      objectPooler.PlacementTimerUpdated += OnPlacementTimerUpdated;
      objectPooler.PlacementPhaseEnded += OnPlacementPhaseEnded;
    }

    // Add listener to restart button
    if (restartButton != null)
    {
      restartButton.onClick.AddListener(RestartGame);
    }

    // Initialize the gem speedup timer text
    InitializeGemSpeedupTimer();

    // "Try Again" reloads the scene and sets SkipMainMenuOnLoad=true so the player
    // jumps straight into a new round; "Main Menu" reloads without that flag and we
    // land here.
    if (GameState.SkipMainMenuOnLoad)
    {
      GameState.SkipMainMenuOnLoad = false;
      ShowGameplay();
    }
    else
    {
      ShowMainMenu();
    }
  }

  void InitializeGemSpeedupTimer()
  {
    // Check if we have a reference to the timer text
    if (gemSpeedupTimerText != null)
    {
      // Store the original color for fading
      originalTextColor = gemSpeedupTimerText.color;

      gemSpeedupTimerText.gameObject.SetActive(true);
    }
    else
    {
      Debug.LogError("gemSpeedupTimerText is not assigned! Please assign the TextMeshPro component in the Inspector.");
    }
  }

  void Update()
  {
    // Handle fade out animation if active
    if (isFadingOut && gemSpeedupTimerText != null)
    {
      fadeTimer += Time.deltaTime;
      float normalizedTime = Mathf.Clamp01(fadeTimer / fadeOutDuration);

      // Update alpha based on time
      Color newColor = originalTextColor;
      newColor.a = Mathf.Lerp(1f, 0f, normalizedTime);
      gemSpeedupTimerText.color = newColor;

      // When fade is complete, hide the text and reset
      if (normalizedTime >= 1f)
      {
        gemSpeedupTimerText.gameObject.SetActive(false);
        isFadingOut = false;
        fadeTimer = 0f;

        // Reset the color for next time
        newColor.a = 1f;
        gemSpeedupTimerText.color = newColor;
      }
    }
  }

  void UpdateScore(int newScore)
  {
    // Update score text
    if (scoreText != null)
    {
      scoreText.text = "Score: " + newScore;
    }
    if (scoreDisplay != null)
    {
      scoreDisplay.text = "Score: " + newScore;
    }

    // Check for new high score
    if (newScore > highScore)
    {
      highScore = newScore;
      PlayerPrefs.SetInt("HighScore", highScore);
      UpdateHighScoreText();
    }
  }

  // Green "+20" pop-up at the catch location.
  void HandleGemCaught(int amount, Vector3 worldPosition)
  {
    SpawnFloatingScore(amount, worldPosition);
  }

  // Red "-1 ♥" pop-up at the miss location. Misses no longer deduct points; the
  // pop-up indicates that a life was lost instead.
  void HandleGemMissed(int amount, Vector3 worldPosition)
  {
    SpawnFloatingText("-1 \u2665", new Color(1.00f, 0.45f, 0.45f), worldPosition);
  }

  // Build a screen-space overlay canvas if there isn't one assigned and none exists in
  // the scene. This lets the score tracker + floating text work with zero setup.
  void EnsureHudCanvas()
  {
    if (hudCanvas == null)
    {
      Canvas existing = FindObjectOfType<Canvas>();
      if (existing != null && existing.renderMode == RenderMode.ScreenSpaceOverlay)
      {
        hudCanvas = existing;
      }
      else
      {
        GameObject canvasGo = new GameObject("HUD Canvas (auto)",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        hudCanvas = canvasGo.GetComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
      }
    }

    EnsureSafeAreaRoot();
  }

  // Builds a full-bleed RectTransform inside the HUD canvas that the SafeAreaFitter
  // resizes every frame to match Screen.safeArea. All HUD/menu elements parent to it
  // (via UiRoot) so they automatically dodge phone notches and gesture regions.
  void EnsureSafeAreaRoot()
  {
    if (safeAreaRoot != null || hudCanvas == null) return;

    GameObject go = new GameObject("SafeAreaContent (auto)",
        typeof(RectTransform), typeof(SafeAreaFitter));
    go.transform.SetParent(hudCanvas.transform, false);
    safeAreaRoot = go.GetComponent<RectTransform>();
    safeAreaRoot.anchorMin = Vector2.zero;
    safeAreaRoot.anchorMax = Vector2.one;
    safeAreaRoot.offsetMin = Vector2.zero;
    safeAreaRoot.offsetMax = Vector2.zero;
  }

  // Auto-create a TMP score display anchored to the top right if none is wired up.
  void EnsureScoreDisplay()
  {
    if (scoreDisplay != null || hudCanvas == null) return;

    GameObject go = new GameObject("ScoreDisplay (auto)", typeof(RectTransform));
    go.transform.SetParent(UiRoot, false);

    RectTransform rect = go.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(1f, 1f);
    rect.anchorMax = new Vector2(1f, 1f);
    rect.pivot = new Vector2(1f, 1f);
    rect.anchoredPosition = new Vector2(-40f, -40f);
    rect.sizeDelta = new Vector2(500f, 100f);

    scoreDisplay = go.AddComponent<TextMeshProUGUI>();
    scoreDisplay.alignment = TextAlignmentOptions.TopRight;
    scoreDisplay.fontSize = 64f;
    scoreDisplay.fontStyle = FontStyles.Bold;
    scoreDisplay.color = Color.white;
    scoreDisplay.text = "Score: " + GemCatcher.Score;
  }

  // Auto-create a TMP lives display anchored to the top left if none is wired up.
  void EnsureLivesDisplay()
  {
    if (livesDisplay != null || hudCanvas == null) return;

    GameObject go = new GameObject("LivesDisplay (auto)", typeof(RectTransform));
    go.transform.SetParent(UiRoot, false);

    RectTransform rect = go.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0f, 1f);
    rect.anchorMax = new Vector2(0f, 1f);
    rect.pivot = new Vector2(0f, 1f);
    rect.anchoredPosition = new Vector2(40f, -40f);
    rect.sizeDelta = new Vector2(500f, 100f);

    livesDisplay = go.AddComponent<TextMeshProUGUI>();
    livesDisplay.alignment = TextAlignmentOptions.TopLeft;
    livesDisplay.fontSize = 64f;
    livesDisplay.fontStyle = FontStyles.Bold;
    livesDisplay.color = Color.white;
  }

  void UpdateLives(int newLives)
  {
    if (livesDisplay == null) return;
    // Use the standard Black Heart Suit char so we don't depend on emoji fonts.
    livesDisplay.text = "Lives: " + new string('\u2665', Mathf.Max(0, newLives));
    // Tint red when only one life is left for a bit of urgency.
    livesDisplay.color = newLives <= 1 ? new Color(1f, 0.45f, 0.45f) : Color.white;
  }

  void HandleGameOverEvent()
  {
    GameOver();
  }

  // Builds a basic dimmed-overlay game-over panel with a "Try Again" button if the developer
  // hasn't wired one up in the Inspector. The panel starts deactivated; ShowGameOverPanel
  // toggles it on after the delay.
  void EnsureGameOverPanel()
  {
    if (gameOverPanel != null)
    {
      gameOverPanel.SetActive(false);
      return;
    }

    EnsureHudCanvas();
    if (hudCanvas == null) return;

    // Full-screen dim overlay.
    GameObject panel = new GameObject("GameOverPanel (auto)", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    panel.transform.SetParent(UiRoot, false);
    RectTransform panelRect = panel.GetComponent<RectTransform>();
    panelRect.anchorMin = Vector2.zero;
    panelRect.anchorMax = Vector2.one;
    panelRect.offsetMin = Vector2.zero;
    panelRect.offsetMax = Vector2.zero;
    Image bg = panel.GetComponent<Image>();
    bg.color = new Color(0f, 0f, 0f, 0.65f);

    // Title — anchored to the top of the screen so it's always above the breakdown.
    GameObject titleGo = new GameObject("Title", typeof(RectTransform));
    titleGo.transform.SetParent(panel.transform, false);
    RectTransform titleRect = titleGo.GetComponent<RectTransform>();
    titleRect.anchorMin = new Vector2(0.5f, 1f);
    titleRect.anchorMax = new Vector2(0.5f, 1f);
    titleRect.pivot = new Vector2(0.5f, 1f);
    titleRect.anchoredPosition = new Vector2(0f, -30f);
    titleRect.sizeDelta = new Vector2(800f, 130f);
    TextMeshProUGUI title = titleGo.AddComponent<TextMeshProUGUI>();
    title.text = "Game Over";
    title.fontSize = 110f;
    title.fontStyle = FontStyles.Bold;
    title.alignment = TextAlignmentOptions.Center;
    title.color = Color.white;

    // Headline "Final Score: X" — one line, anchored just below the title.
    GameObject scoreGo = new GameObject("FinalScore", typeof(RectTransform));
    scoreGo.transform.SetParent(panel.transform, false);
    RectTransform scoreRect = scoreGo.GetComponent<RectTransform>();
    scoreRect.anchorMin = new Vector2(0.5f, 1f);
    scoreRect.anchorMax = new Vector2(0.5f, 1f);
    scoreRect.pivot = new Vector2(0.5f, 1f);
    scoreRect.anchoredPosition = new Vector2(0f, -180f);
    scoreRect.sizeDelta = new Vector2(900f, 90f);
    autoFinalScoreText = scoreGo.AddComponent<TextMeshProUGUI>();
    autoFinalScoreText.text = "Final Score: 0";
    autoFinalScoreText.fontSize = 64f;
    autoFinalScoreText.fontStyle = FontStyles.Bold;
    autoFinalScoreText.alignment = TextAlignmentOptions.Center;
    autoFinalScoreText.color = Color.white;

    // "Gems Caught:" subhead.
    GameObject labelGo = new GameObject("GemsCaughtLabel", typeof(RectTransform));
    labelGo.transform.SetParent(panel.transform, false);
    RectTransform labelRect = labelGo.GetComponent<RectTransform>();
    labelRect.anchorMin = new Vector2(0.5f, 1f);
    labelRect.anchorMax = new Vector2(0.5f, 1f);
    labelRect.pivot = new Vector2(0.5f, 1f);
    labelRect.anchoredPosition = new Vector2(0f, -290f);
    labelRect.sizeDelta = new Vector2(900f, 60f);
    TextMeshProUGUI gemsCaughtLabel = labelGo.AddComponent<TextMeshProUGUI>();
    gemsCaughtLabel.text = "Gems Caught";
    gemsCaughtLabel.fontSize = 44f;
    gemsCaughtLabel.fontStyle = FontStyles.Bold;
    gemsCaughtLabel.alignment = TextAlignmentOptions.Center;
    gemsCaughtLabel.color = new Color(0.85f, 0.85f, 0.85f);

    // Vertical icon list — one row per gem type. Stretches between the subhead at top
    // and the retry button at the bottom; rows are stacked by VerticalLayoutGroup.
    GameObject iconsGo = new GameObject("GemIconsContainer",
        typeof(RectTransform), typeof(VerticalLayoutGroup));
    iconsGo.transform.SetParent(panel.transform, false);
    autoGemIconsContainer = iconsGo.GetComponent<RectTransform>();
    autoGemIconsContainer.anchorMin = new Vector2(0.5f, 0f);
    autoGemIconsContainer.anchorMax = new Vector2(0.5f, 1f);
    autoGemIconsContainer.pivot = new Vector2(0.5f, 1f);
    // Reserve ~360 px at the top (title + final score + label + gap) and ~300 px at
    // the bottom for the stacked Try Again + Main Menu buttons.
    autoGemIconsContainer.offsetMin = new Vector2(-380f, 300f);
    autoGemIconsContainer.offsetMax = new Vector2(380f, -360f);
    VerticalLayoutGroup vlg = iconsGo.GetComponent<VerticalLayoutGroup>();
    vlg.childAlignment = TextAnchor.UpperCenter;
    vlg.spacing = 12f;
    vlg.childControlWidth = false;
    vlg.childControlHeight = false;
    vlg.childForceExpandWidth = false;
    vlg.childForceExpandHeight = false;

    // "Try Again" + "Main Menu" stacked at the bottom of the panel. Try Again is
    // primary (blue), Main Menu is secondary (gray).
    Button retryBtn = BuildPanelButton(
        panel.transform, "RetryButton", "Try Again",
        new Color(0.20f, 0.55f, 0.85f), new Vector2(0f, 175f), new Vector2(360f, 100f),
        RestartGame);
    restartButton = retryBtn;

    BuildPanelButton(
        panel.transform, "MainMenuButton", "Main Menu",
        new Color(0.35f, 0.35f, 0.40f), new Vector2(0f, 55f), new Vector2(360f, 100f),
        ReturnToMainMenu);

    gameOverPanel = panel;
    gameOverPanel.SetActive(false);
  }

  // Reusable factory for a labeled, anchored-to-bottom-center button on a panel.
  // anchoredPosition is measured from the bottom-center of the parent.
  Button BuildPanelButton(Transform parent, string name, string label, Color bgColor,
      Vector2 anchoredPosition, Vector2 size, UnityAction onClick)
  {
    GameObject btnGo = new GameObject(name,
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
    btnGo.transform.SetParent(parent, false);
    RectTransform rect = btnGo.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0.5f, 0f);
    rect.anchorMax = new Vector2(0.5f, 0f);
    rect.pivot = new Vector2(0.5f, 0f);
    rect.anchoredPosition = anchoredPosition;
    rect.sizeDelta = size;
    Image bg = btnGo.GetComponent<Image>();
    bg.color = bgColor;

    GameObject lblGo = new GameObject("Label", typeof(RectTransform));
    lblGo.transform.SetParent(btnGo.transform, false);
    RectTransform lblRect = lblGo.GetComponent<RectTransform>();
    lblRect.anchorMin = Vector2.zero;
    lblRect.anchorMax = Vector2.one;
    lblRect.offsetMin = Vector2.zero;
    lblRect.offsetMax = Vector2.zero;
    TextMeshProUGUI tmp = lblGo.AddComponent<TextMeshProUGUI>();
    tmp.text = label;
    tmp.fontSize = 44f;
    tmp.fontStyle = FontStyles.Bold;
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.color = Color.white;

    Button btn = btnGo.GetComponent<Button>();
    btn.targetGraphic = bg;
    btn.onClick.AddListener(onClick);
    return btn;
  }

  // ---------------------------------------------------------------------------
  // Menu panels (main menu + leaderboard + help)
  // ---------------------------------------------------------------------------

  // Builds the main menu — title + four buttons (Play / Leaderboard / Help / Exit) —
  // if no panel was wired up in the Inspector. The panel starts hidden; ShowMainMenu
  // toggles it on.
  void EnsureMainMenuPanel()
  {
    if (mainMenuPanel != null)
    {
      mainMenuPanel.SetActive(false);
      return;
    }
    EnsureHudCanvas();
    if (hudCanvas == null) return;

    GameObject panel = BuildFullScreenPanel("MainMenuPanel (auto)", new Color(0.05f, 0.07f, 0.10f, 0.95f));

    // Title at the top — gradient-ish gold for a bit of "arcade" flavour.
    GameObject titleGo = new GameObject("Title", typeof(RectTransform));
    titleGo.transform.SetParent(panel.transform, false);
    RectTransform titleRect = titleGo.GetComponent<RectTransform>();
    titleRect.anchorMin = new Vector2(0.5f, 1f);
    titleRect.anchorMax = new Vector2(0.5f, 1f);
    titleRect.pivot = new Vector2(0.5f, 1f);
    titleRect.anchoredPosition = new Vector2(0f, -110f);
    titleRect.sizeDelta = new Vector2(1200f, 180f);
    TextMeshProUGUI title = titleGo.AddComponent<TextMeshProUGUI>();
    title.text = "GEM CATCHER";
    title.fontSize = 140f;
    title.fontStyle = FontStyles.Bold;
    title.alignment = TextAlignmentOptions.Center;
    title.color = new Color(1f, 0.85f, 0.35f);

    // Subtitle / tagline.
    GameObject subGo = new GameObject("Tagline", typeof(RectTransform));
    subGo.transform.SetParent(panel.transform, false);
    RectTransform subRect = subGo.GetComponent<RectTransform>();
    subRect.anchorMin = new Vector2(0.5f, 1f);
    subRect.anchorMax = new Vector2(0.5f, 1f);
    subRect.pivot = new Vector2(0.5f, 1f);
    subRect.anchoredPosition = new Vector2(0f, -300f);
    subRect.sizeDelta = new Vector2(1200f, 60f);
    TextMeshProUGUI sub = subGo.AddComponent<TextMeshProUGUI>();
    sub.text = "Catch the gems. Don't miss.";
    sub.fontSize = 38f;
    sub.fontStyle = FontStyles.Italic;
    sub.alignment = TextAlignmentOptions.Center;
    sub.color = new Color(0.8f, 0.8f, 0.85f);

    // Centered button stack — anchored to vertical center for stable layout across resolutions.
    GameObject stackGo = new GameObject("ButtonStack",
        typeof(RectTransform), typeof(VerticalLayoutGroup));
    stackGo.transform.SetParent(panel.transform, false);
    RectTransform stackRect = stackGo.GetComponent<RectTransform>();
    stackRect.anchorMin = new Vector2(0.5f, 0.5f);
    stackRect.anchorMax = new Vector2(0.5f, 0.5f);
    stackRect.pivot = new Vector2(0.5f, 0.5f);
    stackRect.anchoredPosition = new Vector2(0f, -50f);
    stackRect.sizeDelta = new Vector2(520f, 520f);
    VerticalLayoutGroup vlg = stackGo.GetComponent<VerticalLayoutGroup>();
    vlg.childAlignment = TextAnchor.MiddleCenter;
    vlg.spacing = 22f;
    vlg.childControlWidth = false;
    vlg.childControlHeight = false;
    vlg.childForceExpandWidth = false;
    vlg.childForceExpandHeight = false;

    BuildStackedMenuButton(stackGo.transform, "PlayButton",        "Play",        new Color(0.20f, 0.60f, 0.35f), OnPlayClicked);
    BuildStackedMenuButton(stackGo.transform, "LeaderboardButton", "Leaderboard", new Color(0.20f, 0.45f, 0.80f), OnLeaderboardClicked);
    BuildStackedMenuButton(stackGo.transform, "HelpButton",        "Help",        new Color(0.45f, 0.30f, 0.65f), OnHelpClicked);
    BuildStackedMenuButton(stackGo.transform, "ExitButton",        "Exit",        new Color(0.55f, 0.20f, 0.20f), OnExitClicked);

    mainMenuPanel = panel;
    mainMenuPanel.SetActive(false);
  }

  // Builds the leaderboard sub-panel: title + mock score list + Back button.
  void EnsureLeaderboardPanel()
  {
    if (leaderboardPanel != null)
    {
      leaderboardPanel.SetActive(false);
      return;
    }
    EnsureHudCanvas();
    if (hudCanvas == null) return;

    GameObject panel = BuildFullScreenPanel("LeaderboardPanel (auto)", new Color(0.05f, 0.07f, 0.10f, 0.97f));

    // Title.
    AddPanelTitle(panel.transform, "TOP SCORES", new Color(1f, 0.85f, 0.35f), 100f);

    // Score list — anchored between the title and the Back button.
    GameObject listGo = new GameObject("ScoreList", typeof(RectTransform));
    listGo.transform.SetParent(panel.transform, false);
    RectTransform listRect = listGo.GetComponent<RectTransform>();
    listRect.anchorMin = new Vector2(0.5f, 0f);
    listRect.anchorMax = new Vector2(0.5f, 1f);
    listRect.pivot = new Vector2(0.5f, 0.5f);
    listRect.anchoredPosition = Vector2.zero;
    listRect.offsetMin = new Vector2(-360f, 220f);
    listRect.offsetMax = new Vector2(360f, -260f);
    TextMeshProUGUI listTmp = listGo.AddComponent<TextMeshProUGUI>();
    listTmp.alignment = TextAlignmentOptions.Top;
    listTmp.fontSize = 42f;
    listTmp.color = Color.white;
    listTmp.enableWordWrapping = false;
    listTmp.text = BuildLeaderboardText();

    BuildPanelButton(panel.transform, "BackButton", "Back",
        new Color(0.35f, 0.35f, 0.40f), new Vector2(0f, 80f), new Vector2(280f, 90f),
        OnLeaderboardBackClicked);

    leaderboardPanel = panel;
    leaderboardPanel.SetActive(false);
  }

  // Builds the help sub-panel: title + instructions text + Back button.
  void EnsureHelpPanel()
  {
    if (helpPanel != null)
    {
      helpPanel.SetActive(false);
      return;
    }
    EnsureHudCanvas();
    if (hudCanvas == null) return;

    GameObject panel = BuildFullScreenPanel("HelpPanel (auto)", new Color(0.05f, 0.07f, 0.10f, 0.97f));

    AddPanelTitle(panel.transform, "HOW TO PLAY", new Color(1f, 0.85f, 0.35f), 100f);

    string helpText =
        "Catch the falling gems with your glass cube catcher.\n\n" +
        "<b>Controls</b>\n" +
        "  • During the placement countdown, click any slot at the bottom of the screen to position the catcher.\n" +
        "  • You can reposition the catcher as many times as you want before the countdown ends.\n\n" +
        "<b>Scoring</b>\n" +
        "  • Catching a gem: <color=#7FE787>+20 points</color>.\n" +
        "  • Missing a gem: <color=#FF7373>-1 life</color>.\n" +
        "  • Every <b>100 points</b> earns you a bonus life.\n\n" +
        "<b>Game Over</b>\n" +
        "  • You start with <b>3 lives</b>. The game ends when you run out.\n\n" +
        "Good luck!";

    BuildScrollableTextBlock(
        panel.transform, "HelpScroll", helpText,
        // Reserve ~240 px at the top (title + a bit of breathing room) and ~210 px
        // at the bottom for the Back button and its margin.
        offsetMin: new Vector2(-560f, 210f),
        offsetMax: new Vector2(560f, -240f));

    BuildPanelButton(panel.transform, "BackButton", "Back",
        new Color(0.35f, 0.35f, 0.40f), new Vector2(0f, 80f), new Vector2(280f, 90f),
        OnHelpBackClicked);

    helpPanel = panel;
    helpPanel.SetActive(false);
  }

  // Builds a vertically-scrollable text block: ScrollRect → Viewport (with RectMask2D
  // for clipping) → Content (with ContentSizeFitter and a TMP child sized by the
  // layout). The text content can be any length — if it's taller than the viewport,
  // the user can scroll (mouse wheel / touch drag) to see the rest. If it fits, the
  // ScrollRect just sits there inert.
  void BuildScrollableTextBlock(Transform parent, string name, string text,
      Vector2 offsetMin, Vector2 offsetMax)
  {
    // Outer ScrollRect — anchored stretch between top/bottom of the parent.
    GameObject scrollGo = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
    scrollGo.transform.SetParent(parent, false);
    RectTransform scrollRect = scrollGo.GetComponent<RectTransform>();
    scrollRect.anchorMin = new Vector2(0.5f, 0f);
    scrollRect.anchorMax = new Vector2(0.5f, 1f);
    scrollRect.pivot = new Vector2(0.5f, 0.5f);
    scrollRect.anchoredPosition = Vector2.zero;
    scrollRect.offsetMin = offsetMin;
    scrollRect.offsetMax = offsetMax;

    // Viewport — clips content via RectMask2D (rectangular scissor clip; doesn't care
    // about alpha, unlike Mask which would hide everything when paired with a
    // transparent graphic). A transparent Image is added purely so the viewport area
    // catches mouse-wheel raycasts and forwards them to the ScrollRect.
    GameObject viewportGo = new GameObject("Viewport",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
    viewportGo.transform.SetParent(scrollGo.transform, false);
    RectTransform viewportRect = viewportGo.GetComponent<RectTransform>();
    viewportRect.anchorMin = Vector2.zero;
    viewportRect.anchorMax = Vector2.one;
    viewportRect.pivot = new Vector2(0.5f, 0.5f);
    // Small inner padding so text doesn't kiss the edges of the scroll area.
    viewportRect.offsetMin = new Vector2(20f, 10f);
    viewportRect.offsetMax = new Vector2(-20f, -10f);
    Image viewportImg = viewportGo.GetComponent<Image>();
    viewportImg.color = new Color(0f, 0f, 0f, 0f);
    viewportImg.raycastTarget = true;

    // Content — anchored to the top of the viewport so it grows downward; the
    // ContentSizeFitter gives it the height TMP says the wrapped text needs.
    GameObject contentGo = new GameObject("Content",
        typeof(RectTransform), typeof(ContentSizeFitter));
    contentGo.transform.SetParent(viewportGo.transform, false);
    RectTransform contentRect = contentGo.GetComponent<RectTransform>();
    contentRect.anchorMin = new Vector2(0f, 1f);
    contentRect.anchorMax = new Vector2(1f, 1f);
    contentRect.pivot = new Vector2(0.5f, 1f);
    contentRect.anchoredPosition = Vector2.zero;
    contentRect.sizeDelta = Vector2.zero;
    ContentSizeFitter csf = contentGo.GetComponent<ContentSizeFitter>();
    csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

    // The TMP component lives on the Content itself — TMP implements ILayoutElement
    // so the ContentSizeFitter can read its preferred height directly.
    TextMeshProUGUI body = contentGo.AddComponent<TextMeshProUGUI>();
    body.alignment = TextAlignmentOptions.TopLeft;
    body.fontSize = 32f;
    body.color = Color.white;
    body.enableWordWrapping = true;
    body.text = text;

    ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
    sr.viewport = viewportRect;
    sr.content = contentRect;
    sr.horizontal = false;
    sr.vertical = true;
    sr.movementType = ScrollRect.MovementType.Elastic;
    sr.elasticity = 0.1f;
    sr.scrollSensitivity = 40f;
  }

  // Builds a full-screen, near-opaque panel that hosts a single menu screen.
  GameObject BuildFullScreenPanel(string name, Color bgColor)
  {
    GameObject panel = new GameObject(name,
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    panel.transform.SetParent(UiRoot, false);
    RectTransform rect = panel.GetComponent<RectTransform>();
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
    Image bg = panel.GetComponent<Image>();
    bg.color = bgColor;
    return panel;
  }

  // Helper used by the leaderboard and help panels for their headline text.
  void AddPanelTitle(Transform parent, string text, Color color, float topOffset)
  {
    GameObject titleGo = new GameObject("Title", typeof(RectTransform));
    titleGo.transform.SetParent(parent, false);
    RectTransform rect = titleGo.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0.5f, 1f);
    rect.anchorMax = new Vector2(0.5f, 1f);
    rect.pivot = new Vector2(0.5f, 1f);
    rect.anchoredPosition = new Vector2(0f, -topOffset);
    rect.sizeDelta = new Vector2(1200f, 140f);
    TextMeshProUGUI tmp = titleGo.AddComponent<TextMeshProUGUI>();
    tmp.text = text;
    tmp.fontSize = 96f;
    tmp.fontStyle = FontStyles.Bold;
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.color = color;
  }

  // Stacked menu buttons share width/height so they line up nicely under VerticalLayoutGroup.
  // VerticalLayoutGroup ignores anchoredPosition (it positions children itself), so we just
  // give each child a fixed sizeDelta and let the group handle stacking.
  Button BuildStackedMenuButton(Transform parent, string name, string label, Color bgColor, UnityAction onClick)
  {
    GameObject btnGo = new GameObject(name,
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
    btnGo.transform.SetParent(parent, false);
    RectTransform rect = btnGo.GetComponent<RectTransform>();
    rect.sizeDelta = new Vector2(440f, 100f);
    LayoutElement le = btnGo.GetComponent<LayoutElement>();
    le.preferredWidth = 440f;
    le.preferredHeight = 100f;

    Image bg = btnGo.GetComponent<Image>();
    bg.color = bgColor;

    GameObject lblGo = new GameObject("Label", typeof(RectTransform));
    lblGo.transform.SetParent(btnGo.transform, false);
    RectTransform lblRect = lblGo.GetComponent<RectTransform>();
    lblRect.anchorMin = Vector2.zero;
    lblRect.anchorMax = Vector2.one;
    lblRect.offsetMin = Vector2.zero;
    lblRect.offsetMax = Vector2.zero;
    TextMeshProUGUI tmp = lblGo.AddComponent<TextMeshProUGUI>();
    tmp.text = label;
    tmp.fontSize = 46f;
    tmp.fontStyle = FontStyles.Bold;
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.color = Color.white;

    Button btn = btnGo.GetComponent<Button>();
    btn.targetGraphic = bg;
    btn.onClick.AddListener(onClick);
    return btn;
  }

  string BuildLeaderboardText()
  {
    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < MockLeaderboard.Length; i++)
    {
      var entry = MockLeaderboard[i];
      // Right-align the rank to a fixed width so the columns line up. Score is also
      // right-padded (TMP renders monospace digits well enough for this scale).
      sb.AppendFormat("<b>{0,2}.</b>  {1}   <color=#FFD66B>{2}</color>",
          i + 1, entry.name, entry.score);
      if (i < MockLeaderboard.Length - 1) sb.Append('\n');
    }
    return sb.ToString();
  }

  // ---------------------------------------------------------------------------
  // Menu navigation
  // ---------------------------------------------------------------------------

  void ShowMainMenu()
  {
    GameState.IsPlaying = false;
    SetGameplayHudVisible(false);
    if (gameOverPanel != null) gameOverPanel.SetActive(false);
    if (leaderboardPanel != null) leaderboardPanel.SetActive(false);
    if (helpPanel != null) helpPanel.SetActive(false);
    if (mainMenuPanel != null)
    {
      mainMenuPanel.SetActive(true);
      mainMenuPanel.transform.SetAsLastSibling();
    }
  }

  void ShowGameplay()
  {
    if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    if (leaderboardPanel != null) leaderboardPanel.SetActive(false);
    if (helpPanel != null) helpPanel.SetActive(false);
    if (gameOverPanel != null) gameOverPanel.SetActive(false);

    SetGameplayHudVisible(true);
    GameState.IsPlaying = true;
    gameIsOver = false;
  }

  // Toggles the score/lives HUD so they don't bleed through the menu panels.
  void SetGameplayHudVisible(bool visible)
  {
    if (scoreDisplay != null) scoreDisplay.gameObject.SetActive(visible);
    if (livesDisplay != null) livesDisplay.gameObject.SetActive(visible);
  }

  void OnPlayClicked()
  {
    ShowGameplay();
  }

  void OnLeaderboardClicked()
  {
    if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    if (leaderboardPanel != null)
    {
      leaderboardPanel.SetActive(true);
      leaderboardPanel.transform.SetAsLastSibling();
    }
  }

  void OnLeaderboardBackClicked()
  {
    if (leaderboardPanel != null) leaderboardPanel.SetActive(false);
    ShowMainMenu();
  }

  void OnHelpClicked()
  {
    if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    if (helpPanel != null)
    {
      helpPanel.SetActive(true);
      helpPanel.transform.SetAsLastSibling();
      // Force layout pass now so the ContentSizeFitter inside our scroll view sizes
      // the content to the wrapped TMP text on the same frame the panel becomes
      // visible (otherwise the very first frame can show empty / mis-sized content).
      RectTransform rt = helpPanel.transform as RectTransform;
      if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }
  }

  void OnHelpBackClicked()
  {
    if (helpPanel != null) helpPanel.SetActive(false);
    ShowMainMenu();
  }

  void OnExitClicked()
  {
#if UNITY_EDITOR
    UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
  }

  // Game over → "Main Menu". Reload the scene without setting SkipMainMenuOnLoad so we
  // land on the main menu on the next scene start.
  void ReturnToMainMenu()
  {
    GemCatcher.ResetScore();
    GemCatcher.ResetLives();
    GameState.SkipMainMenuOnLoad = false;
    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
  }

  // Spawn a "+20" / "-N" numeric pop-up at the given world position, on the HUD canvas.
  public void SpawnFloatingScore(int amount, Vector3 worldPosition)
  {
    GameObject go = CreateFloatingTextHost(worldPosition);
    if (go == null) return;
    go.AddComponent<FloatingScoreText>().Initialize(amount);
  }

  // Spawn an arbitrary-text pop-up with the given color (used for miss "-1 ♥" pop-ups).
  public void SpawnFloatingText(string text, Color color, Vector3 worldPosition)
  {
    GameObject go = CreateFloatingTextHost(worldPosition);
    if (go == null) return;
    go.AddComponent<FloatingScoreText>().Initialize(text, color);
  }

  // Builds the shared RectTransform / TextMeshProUGUI scaffold that every floating-text
  // pop-up uses. Returns null if the HUD canvas or main camera isn't available.
  GameObject CreateFloatingTextHost(Vector3 worldPosition)
  {
    EnsureHudCanvas();
    if (hudCanvas == null) return null;

    Camera cam = Camera.main;
    if (cam == null) return null;

    GameObject go = new GameObject("FloatingScore", typeof(RectTransform));
    Transform parent = UiRoot;
    if (parent == null) return null;
    go.transform.SetParent(parent, false);

    RectTransform rect = go.GetComponent<RectTransform>();
    // Center-anchor the text so its anchoredPosition is in parent-local coordinates
    // with origin at the parent rect centre — matches ScreenPointToLocalPointInRectangle's output.
    rect.anchorMin = new Vector2(0.5f, 0.5f);
    rect.anchorMax = new Vector2(0.5f, 0.5f);
    rect.pivot = new Vector2(0.5f, 0.5f);
    rect.sizeDelta = new Vector2(300f, 120f);

    Vector2 screenPos = cam.WorldToScreenPoint(worldPosition);
    RectTransform parentRect = parent as RectTransform;
    Vector2 localPoint;
    RectTransformUtility.ScreenPointToLocalPointInRectangle(
        parentRect, screenPos, null, out localPoint);
    rect.anchoredPosition = localPoint;

    TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.fontSize = 72f;
    tmp.fontStyle = FontStyles.Bold;

    return go;
  }

  void UpdateHighScoreText()
  {
    if (highScoreText != null)
    {
      highScoreText.text = "High Score: " + highScore;
    }
  }

  public void GameOver()
  {
    if (!gameIsOver)
    {
      gameIsOver = true;

      // Show game over panel after a short delay
      Invoke("ShowGameOverPanel", gameOverDelay);
    }
  }

  void ShowGameOverPanel()
  {
    if (gameOverPanel != null)
    {
      gameOverPanel.SetActive(true);
    }

    // Manual UI.Text fallback still gets the full text breakdown — users who hand-wired
    // a single Text element on a custom panel keep working without changes.
    if (finalScoreText != null) finalScoreText.text = BuildGameOverSummary();
    // Auto panel: headline gets only the score; the icon list handles the breakdown.
    if (autoFinalScoreText != null) autoFinalScoreText.text = "Final Score: " + GemCatcher.Score;
    RebuildGemIcons();

    if (SoundManager.Instance != null)
    {
      SoundManager.Instance.PlayGameOverSound();
    }
  }

  // Rebuilds the auto-panel's vertical "[icon] × N" list from GemCatcher.CatchesByGemName.
  // No-ops on manually-wired panels (which don't expose an icons container).
  void RebuildGemIcons()
  {
    if (autoGemIconsContainer == null) return;

    // Tear down any rows from a previous game so re-shows don't duplicate entries.
    for (int i = 0; i < spawnedIconRows.Count; i++)
    {
      if (spawnedIconRows[i] != null) Destroy(spawnedIconRows[i]);
    }
    spawnedIconRows.Clear();

    // Map prefab name -> prefab so we can look up which prefab to render for each
    // entry in CatchesByGemName (which stores names with the "(Clone)" suffix stripped).
    Dictionary<string, GameObject> prefabsByName = new Dictionary<string, GameObject>();
    if (objectPooler != null && objectPooler.objectPrefabs != null)
    {
      foreach (GameObject prefab in objectPooler.objectPrefabs)
      {
        if (prefab != null) prefabsByName[prefab.name] = prefab;
      }
    }

    Dictionary<string, int> catches = GemCatcher.CatchesByGemName;
    if (catches == null || catches.Count == 0)
    {
      BuildEmptyRow();
      return;
    }

    // Sort by count desc so the most-caught gem appears at the top.
    foreach (var entry in catches.OrderByDescending(kv => kv.Value))
    {
      prefabsByName.TryGetValue(entry.Key, out GameObject prefab);
      Sprite icon = prefab != null ? GemIconRenderer.GetOrCapture(prefab) : null;
      BuildIconRow(icon, entry.Key, entry.Value);
    }
  }

  // Build one row: [gem icon] × N. Icon is a square Image; count uses TMP for crisp text.
  void BuildIconRow(Sprite icon, string fallbackName, int count)
  {
    GameObject row = new GameObject("Row_" + fallbackName,
        typeof(RectTransform), typeof(HorizontalLayoutGroup));
    row.transform.SetParent(autoGemIconsContainer, false);
    RectTransform rowRect = row.GetComponent<RectTransform>();
    rowRect.sizeDelta = new Vector2(420f, 96f);

    HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
    hlg.childAlignment = TextAnchor.MiddleCenter;
    hlg.spacing = 18f;
    hlg.childControlWidth = false;
    hlg.childControlHeight = false;
    hlg.childForceExpandWidth = false;
    hlg.childForceExpandHeight = false;

    // Icon — falls back to the gem name in italics if rendering wasn't possible
    // (e.g. the prefab couldn't be located in objectPrefabs).
    if (icon != null)
    {
      GameObject iconGo = new GameObject("Icon",
          typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
      iconGo.transform.SetParent(row.transform, false);
      RectTransform iconRect = iconGo.GetComponent<RectTransform>();
      iconRect.sizeDelta = new Vector2(80f, 80f);
      Image iconImg = iconGo.GetComponent<Image>();
      iconImg.sprite = icon;
      iconImg.preserveAspect = true;
    }
    else
    {
      GameObject nameGo = new GameObject("Name", typeof(RectTransform));
      nameGo.transform.SetParent(row.transform, false);
      RectTransform nameRect = nameGo.GetComponent<RectTransform>();
      nameRect.sizeDelta = new Vector2(220f, 80f);
      TextMeshProUGUI nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
      nameTmp.text = fallbackName;
      nameTmp.fontSize = 36f;
      nameTmp.fontStyle = FontStyles.Italic;
      nameTmp.alignment = TextAlignmentOptions.MidlineRight;
      nameTmp.color = Color.white;
    }

    GameObject countGo = new GameObject("Count", typeof(RectTransform));
    countGo.transform.SetParent(row.transform, false);
    RectTransform countRect = countGo.GetComponent<RectTransform>();
    countRect.sizeDelta = new Vector2(140f, 80f);
    TextMeshProUGUI countTmp = countGo.AddComponent<TextMeshProUGUI>();
    countTmp.text = "\u00D7 " + count;
    countTmp.fontSize = 52f;
    countTmp.fontStyle = FontStyles.Bold;
    countTmp.alignment = TextAlignmentOptions.MidlineLeft;
    countTmp.color = Color.white;

    spawnedIconRows.Add(row);
  }

  // Friendly placeholder when the player didn't catch any gems.
  void BuildEmptyRow()
  {
    GameObject row = new GameObject("Row_None", typeof(RectTransform));
    row.transform.SetParent(autoGemIconsContainer, false);
    RectTransform rowRect = row.GetComponent<RectTransform>();
    rowRect.sizeDelta = new Vector2(420f, 80f);
    TextMeshProUGUI tmp = row.AddComponent<TextMeshProUGUI>();
    tmp.text = "None";
    tmp.fontSize = 40f;
    tmp.fontStyle = FontStyles.Italic;
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.color = new Color(0.7f, 0.7f, 0.7f);
    spawnedIconRows.Add(row);
  }

  // "Final Score: 240\n\nGems Caught:\n  RedGem ×8\n  BlueGem ×4"
  string BuildGameOverSummary()
  {
    StringBuilder sb = new StringBuilder();
    sb.Append("Final Score: ").Append(GemCatcher.Score);

    Dictionary<string, int> catches = GemCatcher.CatchesByGemName;
    sb.Append("\n\nGems Caught:");
    if (catches == null || catches.Count == 0)
    {
      sb.Append("\n  None");
    }
    else
    {
      // Sort highest count first for a nicer reveal.
      foreach (var entry in catches.OrderByDescending(kv => kv.Value))
      {
        sb.Append("\n  ").Append(entry.Key).Append(" \u00D7").Append(entry.Value);
      }
    }
    return sb.ToString();
  }

  void RestartGame()
  {
    // Reset score & lives before reloading so the next session starts clean even if Unity's
    // Domain Reload is disabled (statics persist across scene reloads otherwise).
    GemCatcher.ResetScore();
    GemCatcher.ResetLives();

    // "Try Again": skip the main menu on the next scene start and drop the player
    // straight into a fresh round.
    GameState.SkipMainMenuOnLoad = true;
    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
  }

  // Called by ObjectPooler when a new placement phase starts
  public void OnPlacementPhaseStarted(float duration)
  {
    // Show the gem speedup timer at the top of the screen
    if (gemSpeedupTimerText != null)
    {
      // Reset any ongoing fade
      isFadingOut = false;
      fadeTimer = 0f;

      // Reset color to full opacity
      Color resetColor = originalTextColor;
      resetColor.a = 1f;
      gemSpeedupTimerText.color = resetColor;

      // Show the timer and set initial value
      gemSpeedupTimerText.gameObject.SetActive(true);
      UpdateCountdownDisplay(duration);
    }
    else
    {
      Debug.LogError("gemSpeedupTimerText is not assigned! Please assign the TextMeshPro component in the Inspector.");
    }
  }

  // Called by ObjectPooler when the placement timer is updated
  public void OnPlacementTimerUpdated(float remainingTime)
  {
    if (gemSpeedupTimerText != null && gemSpeedupTimerText.gameObject.activeInHierarchy && !isFadingOut)
    {
      UpdateCountdownDisplay(remainingTime);
    }
  }

  // Update the countdown display with the current time
  void UpdateCountdownDisplay(float remainingTime)
  {
    // Round to the nearest integer for a cleaner countdown
    int countdownValue = Mathf.CeilToInt(remainingTime);

    // Display the countdown number in large text
    gemSpeedupTimerText.text = countdownValue.ToString();

    // Scale the text based on the remaining time within each second
    float pulseScale = 1.0f + 0.2f * (1.0f - (remainingTime - Mathf.Floor(remainingTime)));
    gemSpeedupTimerText.transform.localScale = new Vector3(pulseScale, pulseScale, 1.0f);
  }

  // Called by ObjectPooler when the placement phase ends
  public void OnPlacementPhaseEnded()
  {
    // Start the fade out animation
    if (gemSpeedupTimerText != null && gemSpeedupTimerText.gameObject.activeInHierarchy)
    {
      isFadingOut = true;
      fadeTimer = 0f;
    }
  }

  void OnDestroy()
  {
    // Unsubscribe from events
    GemCatcher.OnScoreChanged -= UpdateScore;
    GemCatcher.OnLivesChanged -= UpdateLives;
    GemCatcher.OnGameOver -= HandleGameOverEvent;
    GemCatcher.OnGemCaught -= HandleGemCaught;
    GemCatcher.OnGemMissed -= HandleGemMissed;

    if (objectPooler != null)
    {
      objectPooler.PlacementPhaseStarted -= OnPlacementPhaseStarted;
      objectPooler.PlacementTimerUpdated -= OnPlacementTimerUpdated;
      objectPooler.PlacementPhaseEnded -= OnPlacementPhaseEnded;
    }

    // Remove button listener
    if (restartButton != null)
    {
      restartButton.onClick.RemoveListener(RestartGame);
    }
  }
}
