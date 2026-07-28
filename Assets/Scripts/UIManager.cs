using System;
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

  // The "real" score we're tweening toward (set by UpdateScore from the GemCatcher event)
  // and the value currently being rendered. Update() interpolates displayedScore toward
  // targetScore and rewrites the score text once the displayed integer changes.
  private int targetScore = 0;
  private float displayedScoreFloat = 0f;
  private int lastRenderedScore = -1;

  // Active panel fades — each panel transitions from fromAlpha to toAlpha over duration
  // seconds, then is optionally deactivated. We use unscaled time so menu transitions
  // are unaffected by the game-over hit-stop.
  private struct PanelFade
  {
    public CanvasGroup group;
    public GameObject panelObject;
    public float duration, age, fromAlpha, toAlpha;
    public bool deactivateOnEnd;
  }
  private readonly List<PanelFade> activeFades = new List<PanelFade>();

  // TMP-equivalent of `finalScoreText` for the auto-built game over panel. Now only
  // shows the headline "Final Score: X" line; the gem breakdown lives in the icon list.
  private TextMeshProUGUI autoFinalScoreText;

  // Container that holds one `[icon][× N]` row per caught gem on the auto-built panel.
  // Rebuilt every time we show the game-over screen.
  private RectTransform autoGemIconsContainer;
  private readonly List<GameObject> spawnedIconRows = new List<GameObject>();

  [Header("Menu Panels (auto-created if not assigned)")]
  [Tooltip("Main menu shown at scene load. Hosts Play / Daily Challenge / Help buttons.")]
  public GameObject mainMenuPanel;
  [Tooltip("How-to-play panel reached from the main menu.")]
  public GameObject helpPanel;

  // Auto-created in EnsureSettingsPanel, opened from a small gear button on
  // the main menu. Hosts Music / SFX volume sliders and Haptics toggle.
  private GameObject settingsPanel;

  // Auto-created on first request. Shown when the OS backgrounds the app
  // (incoming call, home button, app switcher) so the player can resume on
  // their own terms. Time.timeScale is forced to 0 while it's visible.
  private GameObject pausePanel;

  // Full-screen colored Image used by FlashVignette to play a brief tinted
  // wash on power-up activation. Single shared Image (no GC churn between
  // pickups) modulated via Color.a in Update.
  private Image vignetteOverlay;
  private Color vignetteColor;
  private float vignetteAge;
  private float vignetteDuration;
  private float vignettePeakAlpha;

  // Tween state for the count-up number on the game-over panel. Avoids
  // depending on a tween library; lerped in Update each frame.
  private float finalScoreTweenAge;
  private float finalScoreTweenDuration;
  private int finalScoreTweenTarget;
  private bool finalScoreTweenActive;

  // ---- Daily Challenge UI references --------------------------------------
  // Cached so we can refresh the menu button label, hide retry on daily
  // game-over, and tick the live countdown on the cooldown screen.
  private Button dailyChallengeMenuButton;
  private TextMeshProUGUI dailyChallengeButtonLabel;
  private Image dailyChallengeButtonBg;

  // Cooldown panel — shown when the player taps Daily Challenge but has
  // already played today.
  private GameObject dailyCooldownPanel;
  private TextMeshProUGUI dailyCooldownDayTmp;
  private TextMeshProUGUI dailyCooldownStreakTmp;
  private TextMeshProUGUI dailyCooldownScoreTmp;
  private TextMeshProUGUI dailyCooldownBestTmp;
  private TextMeshProUGUI dailyCooldownTimerTmp;

  // Daily-mode flavor for the shared game-over panel — title swap and a
  // subtitle that shows "Day N · Streak X". Cached at panel-build time.
  private TextMeshProUGUI gameOverTitleTmp;
  private TextMeshProUGUI gameOverDailySubtitleTmp;

  // ---- Power-up HUD ------------------------------------------------------
  // Vertical stack of slot rows on the upper-left, one per PowerUpType. Each
  // row is a colored background + a TMP label like "WIDE  5.2s". Active slots
  // show their full color; inactive slots are hidden. Built lazily.
  private RectTransform powerUpHudContainer;
  private GameObject[] powerUpSlotRoots;
  private TextMeshProUGUI[] powerUpSlotLabels;
  private Image[] powerUpSlotBgs;

  // ---- Test-mode overlay -------------------------------------------------
  // DEV-ONLY top-of-screen banner that announces "TEST MODE • NEXT: <label>"
  // while ObjectPooler.powerUpOnlyTestMode is on. The label portion is tinted
  // to the upcoming power-up's theme color so the developer can preview the
  // pickup's hue before it drops. Built lazily the first time test mode is
  // detected; toggled visible via TickTestModeOverlay each frame.
  private GameObject testModeOverlayRoot;
  private TextMeshProUGUI testModeOverlayLabel;

  // ---- Combo HUD ---------------------------------------------------------
  // Lives directly below the score, top-right. Hidden when combo == 0; shows
  // "COMBO ×N" otherwise, with color/scale escalation as the multiplier grows.
  private RectTransform comboDisplayRoot;
  private TextMeshProUGUI comboDisplayTmp;
  // Smoothed scale target so OnComboChanged can pop the HUD without us writing
  // an animation system — Update() interpolates current scale toward this value
  // every frame using unscaled time.
  private float comboTargetScale = 1f;
  // Set by OnComboTierUp; Update() pulses the HUD on the frame after.
  private bool comboTierUpPending = false;

  private ObjectPooler objectPooler;

  /// <summary>
  /// Singleton handle other systems use to drive the UI (pause overlay,
  /// vignette flashes, etc.). Assigned in Awake so it's available as soon
  /// as auto-bootstrapped managers come online.
  /// </summary>
  public static UIManager Instance { get; private set; }

  void Awake()
  {
    if (Instance != null && Instance != this) return;
    Instance = this;
  }

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
    GemCatcher.OnBonusLifeAwarded += HandleBonusLifeAwarded;

    // Power-up events: banner + HUD slot reveal on activate, slot hide on
    // expire, special "SHIELDED!" floating text when a miss is absorbed.
    PowerUpManager.OnActivated += HandlePowerUpActivated;
    PowerUpManager.OnExpired += HandlePowerUpExpired;
    PowerUpManager.OnShieldConsumed += HandleShieldConsumed;

    // Combo / streak — live HUD updates plus a celebratory "tier up" pulse.
    ComboManager.OnComboChanged += HandleComboChanged;
    ComboManager.OnComboTierUp += HandleComboTierUp;
    ComboManager.OnComboBroken += HandleComboBroken;

    // Score milestones — full-screen banner + power-up gift.
    MilestoneTracker.OnMilestoneReached += HandleMilestoneReached;

    // Bomb special-gem events — distinct floating text +
    // extra fx beyond the standard catch / miss visuals.
    GemCatcher.OnBombHit += HandleBombHit;

    // Make sure we have a top-right score tracker, top-left lives tracker, and
    // a game-over panel even if nothing was wired up in the Inspector.
    EnsureHudCanvas();
    EnsureScoreDisplay();
    EnsureLivesDisplay();
    EnsurePowerUpHud();
    EnsureComboDisplay();
    EnsureGameOverPanel();
    EnsureMainMenuPanel();
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
    // Auto-build a big top-center countdown number on the safe-area-aware HUD if the
    // developer didn't wire one up in the Inspector. This lets the placement-phase
    // countdown work zero-config and avoids the every-frame error spam we used to
    // emit when the field was null.
    if (gemSpeedupTimerText == null)
    {
      EnsureHudCanvas();
      if (UiRoot == null) return;

      GameObject go = new GameObject("PlacementCountdown (auto)", typeof(RectTransform));
      go.transform.SetParent(UiRoot, false);
      RectTransform rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0.5f, 1f);
      rt.anchorMax = new Vector2(0.5f, 1f);
      rt.pivot = new Vector2(0.5f, 1f);
      rt.anchoredPosition = new Vector2(0f, -180f);
      rt.sizeDelta = new Vector2(400f, 220f);

      gemSpeedupTimerText = go.AddComponent<TextMeshProUGUI>();
      gemSpeedupTimerText.alignment = TextAlignmentOptions.Center;
      gemSpeedupTimerText.fontSize = 200f;
      gemSpeedupTimerText.fontStyle = FontStyles.Bold;
      gemSpeedupTimerText.color = new Color(1f, 0.92f, 0.5f);
      gemSpeedupTimerText.text = "";
      gemSpeedupTimerText.gameObject.SetActive(false);
    }

    // Hide the countdown until the placement phase actually starts.
    gemSpeedupTimerText.text = "";
    gemSpeedupTimerText.gameObject.SetActive(false);

    originalTextColor = gemSpeedupTimerText.color;
  }

  void Update()
  {
    TickScoreCountUp();
    TickMenuFades();
    TickDailyCooldown();
    RefreshPowerUpHud();
    TickComboDisplay();
    TickVignetteFlash();
    TickFinalScoreCountUp();
    TickTestModeOverlay();

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
    // Tween toward the new score instead of snapping — Update() drives the display.
    targetScore = newScore;

    // Check for new high score immediately (the tween is just visual flavour).
    if (newScore > highScore)
    {
      highScore = newScore;
      PlayerPrefs.SetInt("HighScore", highScore);
      UpdateHighScoreText();
    }
  }

  // Drives the visual tween of the score display each frame. Slow on small deltas, fast
  // on large ones so a "+20" pops a bit but a "+100" doesn't take seconds to roll.
  void TickScoreCountUp()
  {
    bool atRest = Mathf.Approximately(displayedScoreFloat, targetScore) && lastRenderedScore == targetScore;

    if (!atRest)
    {
      float diff = targetScore - displayedScoreFloat;
      // Scale rate by the magnitude of the gap so big jumps catch up fast.
      float rate = Mathf.Max(40f, Mathf.Abs(diff) * 6f);
      if (diff > 0) displayedScoreFloat = Mathf.Min(targetScore, displayedScoreFloat + rate * Time.unscaledDeltaTime);
      else if (diff < 0) displayedScoreFloat = Mathf.Max(targetScore, displayedScoreFloat - rate * Time.unscaledDeltaTime);

      int rendered = Mathf.RoundToInt(displayedScoreFloat);
      if (rendered != lastRenderedScore)
      {
        lastRenderedScore = rendered;
        if (scoreText != null) scoreText.text = "Score: " + rendered;
        if (scoreDisplay != null) scoreDisplay.text = "Score: " + rendered;

        // Quick scale pop on the score display when the number changes — a tiny detail
        // that makes the HUD feel alive. The ease-back below pulls it to 1.0 again.
        if (scoreDisplay != null)
        {
          scoreDisplay.transform.localScale = Vector3.one * 1.18f;
        }
      }
    }

    // Always ease the score display's scale back to 1.0 — even when "at rest" we may
    // still be recovering from a prior pop.
    if (scoreDisplay != null && scoreDisplay.transform.localScale != Vector3.one)
    {
      scoreDisplay.transform.localScale = Vector3.Lerp(
          scoreDisplay.transform.localScale, Vector3.one,
          Mathf.Clamp01(12f * Time.unscaledDeltaTime));
    }

    // Same ease-back for the lives display. HandleBonusLifeAwarded pops it up to 1.4x;
    // this brings it back to rest over ~6 frames at 60fps.
    if (livesDisplay != null && livesDisplay.transform.localScale != Vector3.one)
    {
      livesDisplay.transform.localScale = Vector3.Lerp(
          livesDisplay.transform.localScale, Vector3.one,
          Mathf.Clamp01(10f * Time.unscaledDeltaTime));
    }
  }

  // ---------------------------------------------------------------------------
  // Panel fade transitions — replaces raw SetActive() so menus glide in/out.
  // ---------------------------------------------------------------------------

  void TickMenuFades()
  {
    if (activeFades.Count == 0) return;
    float dt = Time.unscaledDeltaTime;
    for (int i = activeFades.Count - 1; i >= 0; i--)
    {
      PanelFade f = activeFades[i];
      f.age += dt;
      float t = f.duration > 0f ? Mathf.Clamp01(f.age / f.duration) : 1f;
      // Smoothstep for a softer ease-in / ease-out feel.
      float eased = t * t * (3f - 2f * t);
      if (f.group != null) f.group.alpha = Mathf.Lerp(f.fromAlpha, f.toAlpha, eased);

      if (t >= 1f)
      {
        if (f.deactivateOnEnd && f.panelObject != null) f.panelObject.SetActive(false);
        activeFades.RemoveAt(i);
      }
      else
      {
        activeFades[i] = f;
      }
    }
  }

  // Fade `panel` in or out via its CanvasGroup. Adds the CanvasGroup if missing. While
  // hiding, the panel's CanvasGroup is set non-interactable immediately so users can't
  // click a fading-out button.
  void FadePanel(GameObject panel, bool show, float duration = 0.18f)
  {
    if (panel == null) return;
    CanvasGroup cg = panel.GetComponent<CanvasGroup>();
    if (cg == null) cg = panel.AddComponent<CanvasGroup>();

    // Cancel any in-flight fade for this group so we don't double-tween.
    for (int i = activeFades.Count - 1; i >= 0; i--)
    {
      if (activeFades[i].group == cg) activeFades.RemoveAt(i);
    }

    if (show)
    {
      if (!panel.activeSelf)
      {
        cg.alpha = 0f;
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
      }
      cg.interactable = true;
      cg.blocksRaycasts = true;
      activeFades.Add(new PanelFade
      {
        group = cg,
        panelObject = panel,
        duration = duration,
        age = 0f,
        fromAlpha = cg.alpha,
        toAlpha = 1f,
        deactivateOnEnd = false,
      });
    }
    else
    {
      // Block interaction immediately on hide.
      cg.interactable = false;
      cg.blocksRaycasts = false;
      if (!panel.activeSelf) return;
      activeFades.Add(new PanelFade
      {
        group = cg,
        panelObject = panel,
        duration = duration,
        age = 0f,
        fromAlpha = cg.alpha,
        toAlpha = 0f,
        deactivateOnEnd = true,
      });
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

    // Many Android phones place the front camera lens INSIDE the safe area
    // that the OS reports — Unity has no way to detect this and our top-
    // anchored UI (Game Over title, Daily Done banner, etc.) ends up overlapping
    // the lens. Add an extra top inset so anything anchored to the top of the
    // safe area is pushed below the camera bezel.
    SafeAreaFitter fitter = go.GetComponent<SafeAreaFitter>();
    if (fitter != null)
    {
#if UNITY_ANDROID
      fitter.extraTopPixels = 60f;
#endif
    }
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

  // -------------------------------------------------------------------------
  // Power-up HUD — vertical stack of slot rows on the upper-left, below the
  // lives display. Each PowerUpType gets one slot; visible only while the
  // power-up is active.
  // -------------------------------------------------------------------------

  void EnsurePowerUpHud()
  {
    if (powerUpHudContainer != null || hudCanvas == null) return;
    if (UiRoot == null) return;

    // Container anchored top-left, dropped below the lives display (which
    // ends around y = -140). Width sized so it never crowds the score on
    // narrower devices; rows stack vertically inside.
    GameObject container = new GameObject("PowerUpHud (auto)", typeof(RectTransform));
    container.transform.SetParent(UiRoot, false);
    powerUpHudContainer = container.GetComponent<RectTransform>();
    powerUpHudContainer.anchorMin = new Vector2(0f, 1f);
    powerUpHudContainer.anchorMax = new Vector2(0f, 1f);
    powerUpHudContainer.pivot = new Vector2(0f, 1f);
    powerUpHudContainer.anchoredPosition = new Vector2(40f, -160f);
    powerUpHudContainer.sizeDelta = new Vector2(280f, 230f);

    PowerUpType[] order = new[]
    {
      PowerUpType.WiderCatcher,
      PowerUpType.Shield,
      PowerUpType.DoubleScore,
    };
    powerUpSlotRoots = new GameObject[order.Length];
    powerUpSlotLabels = new TextMeshProUGUI[order.Length];
    powerUpSlotBgs = new Image[order.Length];

    const float rowHeight = 50f;
    const float rowSpacing = 8f;

    for (int i = 0; i < order.Length; i++)
    {
      GameObject row = new GameObject(
          "PowerUpSlot_" + order[i],
          typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
      row.transform.SetParent(container.transform, false);
      RectTransform rect = row.GetComponent<RectTransform>();
      rect.anchorMin = new Vector2(0f, 1f);
      rect.anchorMax = new Vector2(1f, 1f);
      rect.pivot = new Vector2(0.5f, 1f);
      rect.anchoredPosition = new Vector2(0f, -i * (rowHeight + rowSpacing));
      rect.sizeDelta = new Vector2(0f, rowHeight);

      Image bg = row.GetComponent<Image>();
      Color tint = PowerUpPickup.ColorForType(order[i]);
      // Slightly darker, semi-transparent backdrop so the white label pops on
      // any in-game background.
      bg.color = new Color(tint.r * 0.55f, tint.g * 0.55f, tint.b * 0.55f, 0.85f);

      GameObject labelGo = new GameObject("Label", typeof(RectTransform));
      labelGo.transform.SetParent(row.transform, false);
      RectTransform labelRect = labelGo.GetComponent<RectTransform>();
      labelRect.anchorMin = Vector2.zero;
      labelRect.anchorMax = Vector2.one;
      labelRect.offsetMin = new Vector2(16f, 0f);
      labelRect.offsetMax = new Vector2(-16f, 0f);

      TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
      label.alignment = TextAlignmentOptions.MidlineLeft;
      label.fontStyle = FontStyles.Bold;
      label.color = Color.white;
      label.enableAutoSizing = true;
      label.fontSizeMin = 18f;
      label.fontSizeMax = 32f;
      label.enableWordWrapping = false;

      powerUpSlotRoots[i] = row;
      powerUpSlotLabels[i] = label;
      powerUpSlotBgs[i] = bg;

      // Hidden by default; revealed by RefreshPowerUpHud once a timer is set.
      row.SetActive(false);
    }
  }

  // Refreshes the visible / hidden state of each slot. Power-ups don't tick
  // down anymore (they're held until the first unshielded miss), so each slot
  // just shows its label while the corresponding state flag is true.
  void RefreshPowerUpHud()
  {
    if (powerUpSlotRoots == null) return;

    UpdatePowerUpSlot(0, PowerUpType.WiderCatcher, PowerUpManager.WiderCatcherActive);
    UpdatePowerUpSlot(1, PowerUpType.Shield, PowerUpManager.HasShield);
    UpdatePowerUpSlot(2, PowerUpType.DoubleScore, PowerUpManager.DoubleScoreActive);
  }

  void UpdatePowerUpSlot(int idx, PowerUpType type, bool active)
  {
    if (powerUpSlotRoots == null || idx >= powerUpSlotRoots.Length) return;
    GameObject root = powerUpSlotRoots[idx];
    if (root == null) return;

    if (root.activeSelf != active) root.SetActive(active);
    if (!active) return;

    string text = PowerUpPickup.LabelForType(type);
    if (powerUpSlotLabels[idx] != null && powerUpSlotLabels[idx].text != text)
    {
      powerUpSlotLabels[idx].text = text;
    }
  }

  // -------------------------------------------------------------------------
  // Test-mode overlay (DEV-ONLY)
  // -------------------------------------------------------------------------
  // Displayed only while ObjectPooler.powerUpOnlyTestMode is on. Auto-creates
  // a top-center TMP label that reads "TEST MODE • NEXT: <upcoming type>"
  // with the type label tinted to that power-up's theme color so you can
  // preview the magenta-fire ExtraLife / blue Wide / yellow Shield / green
  // 2× look before the pickup actually drops. Polled in Update so the
  // overlay reacts immediately when the dev toggles the field at runtime.
  void TickTestModeOverlay()
  {
    if (objectPooler == null) return;

    bool active = objectPooler.powerUpOnlyTestMode;

    // Lazy-build the overlay only when test mode actually flips on, so a
    // shipping build with the toggle off never instantiates the GameObject.
    if (active && testModeOverlayLabel == null)
    {
      EnsureTestModeOverlay();
    }
    if (testModeOverlayRoot == null) return;

    if (testModeOverlayRoot.activeSelf != active)
    {
      testModeOverlayRoot.SetActive(active);
    }
    if (!active) return;

    PowerUpType nextType = objectPooler.TestModeNextPowerUp;
    string label = PowerUpPickup.LabelForType(nextType);
    Color tint = PowerUpPickup.ColorForType(nextType);
    string hex = ColorUtility.ToHtmlStringRGB(tint);
    // Two-tone rich-text: muted gray prefix + theme-colored label so the
    // upcoming power-up's HUE is visible at a glance, not just its name.
    testModeOverlayLabel.text =
        "<color=#888888>TEST MODE \u2022 NEXT:</color> " +
        "<color=#" + hex + ">" + label + "</color>";
  }

  void EnsureTestModeOverlay()
  {
    if (testModeOverlayRoot != null || hudCanvas == null) return;
    if (UiRoot == null) return;

    // Anchor TOP-CENTER, hugging the very top of the screen so it tucks
    // into the empty horizontal band between the top-left lives display
    // and the top-right score display. Kept thin (50px high) so it doesn't
    // crowd the play area, and centered so it never overlaps either of
    // those text rows even on narrow phones.
    testModeOverlayRoot = new GameObject(
        "TestModeOverlay (auto)",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    testModeOverlayRoot.transform.SetParent(UiRoot, false);

    RectTransform rect = testModeOverlayRoot.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0.5f, 1f);
    rect.anchorMax = new Vector2(0.5f, 1f);
    rect.pivot = new Vector2(0.5f, 1f);
    rect.anchoredPosition = new Vector2(0f, -10f);
    rect.sizeDelta = new Vector2(560f, 50f);

    // Subtle dark backdrop so the rich-text colors stay legible against any
    // in-game background. Alpha low enough that it doesn't obscure the
    // catcher / falling gems behind it. raycastTarget=false on both the
    // backdrop AND the label so touch input passes straight through to the
    // catcher / drag handlers — the dev overlay never steals taps.
    Image bg = testModeOverlayRoot.GetComponent<Image>();
    bg.color = new Color(0f, 0f, 0f, 0.55f);
    bg.raycastTarget = false;

    GameObject labelGo = new GameObject("Label", typeof(RectTransform));
    labelGo.transform.SetParent(testModeOverlayRoot.transform, false);
    RectTransform labelRect = labelGo.GetComponent<RectTransform>();
    labelRect.anchorMin = Vector2.zero;
    labelRect.anchorMax = Vector2.one;
    labelRect.offsetMin = new Vector2(20f, 4f);
    labelRect.offsetMax = new Vector2(-20f, -4f);

    testModeOverlayLabel = labelGo.AddComponent<TextMeshProUGUI>();
    testModeOverlayLabel.alignment = TextAlignmentOptions.Center;
    testModeOverlayLabel.fontSize = 28f;
    testModeOverlayLabel.fontStyle = FontStyles.Bold;
    testModeOverlayLabel.color = Color.white;
    testModeOverlayLabel.richText = true;
    testModeOverlayLabel.enableWordWrapping = false;
    testModeOverlayLabel.raycastTarget = false;
    testModeOverlayLabel.text = "<color=#888888>TEST MODE</color>";

    // Hidden by default — TickTestModeOverlay flips it on the first frame
    // it observes the toggle as true.
    testModeOverlayRoot.SetActive(false);
  }

  void HandlePowerUpActivated(PowerUpType type, float duration)
  {
    EnsurePowerUpHud();
    string banner;
    Color color = PowerUpPickup.ColorForType(type);
    switch (type)
    {
      case PowerUpType.WiderCatcher: banner = "WIDER CATCHER!"; break;
      case PowerUpType.Shield: banner = "SHIELD UP!"; break;
      case PowerUpType.DoubleScore: banner = "DOUBLE SCORE!"; break;
      default: return;
    }
    SpawnBannerNotification(banner, color);

    // Brief screen-wide tint in the power-up's color. Sells the activation as
    // a real "moment" instead of just another banner. Peak alpha is kept low
    // so it never obscures the falling gems.
    FlashVignette(color, duration: 0.55f, peakAlpha: 0.30f);
  }

  void HandlePowerUpExpired(PowerUpType type)
  {
    // No banner on expire — the HUD slot fading away is feedback enough, and
    // expiry can fire several at once (game-over) which would stack banners.
    RefreshPowerUpHud();
  }

  // Floating "SHIELDED!" pop-up at the would-have-been miss site. Keeps the
  // visual focus where the gem fell so the player connects the absorption to
  // that specific gem.
  void HandleShieldConsumed(Vector3 worldPosition)
  {
    SpawnFloatingText("SHIELDED!", new Color(1f, 0.85f, 0.35f), worldPosition);
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

    // Full-screen dim overlay. Stays full-screen (extends behind notch / home
    // indicator) so the entire screen darkens, not just the safe area.
    GameObject panel = new GameObject("GameOverPanel (auto)", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    panel.transform.SetParent(UiRoot, false);
    RectTransform panelRect = panel.GetComponent<RectTransform>();
    panelRect.anchorMin = Vector2.zero;
    panelRect.anchorMax = Vector2.one;
    panelRect.offsetMin = Vector2.zero;
    panelRect.offsetMax = Vector2.zero;
    Image bg = panel.GetComponent<Image>();
    bg.color = new Color(0f, 0f, 0f, 0.65f);

    // Safe-area inset wrapper. All readable content (title, score, buttons)
    // hangs off this so its top/bottom anchors land BELOW the Dynamic Island
    // / front-camera lens (top) and ABOVE the home-indicator gesture bar
    // (bottom), on every iPhone and iPad. SafeAreaFitter recomputes anchors
    // each frame from Screen.safeArea, so this also adapts to device rotation
    // and to runtime safe-area changes (split-view on iPad, etc.).
    GameObject safeAreaGo = new GameObject("SafeArea", typeof(RectTransform));
    safeAreaGo.transform.SetParent(panel.transform, false);
    RectTransform safeRect = safeAreaGo.GetComponent<RectTransform>();
    safeRect.anchorMin = Vector2.zero;
    safeRect.anchorMax = Vector2.one;
    safeRect.offsetMin = Vector2.zero;
    safeRect.offsetMax = Vector2.zero;
    safeAreaGo.AddComponent<SafeAreaFitter>();
    Transform contentParent = safeAreaGo.transform;

    // Title — anchored to the top of the safe area so it's always above the
    // breakdown AND below the Dynamic Island / camera lens.
    GameObject titleGo = new GameObject("Title", typeof(RectTransform));
    titleGo.transform.SetParent(contentParent, false);
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
    gameOverTitleTmp = title;

    // Daily-mode subtitle — empty / hidden when ShowGameOverPanel runs in
    // Normal mode. Sits between the title and "Final Score" line. Stretches
    // to the panel width so the streak text doesn't overflow on narrow screens.
    GameObject dailySubGo = new GameObject("DailySubtitle", typeof(RectTransform));
    dailySubGo.transform.SetParent(contentParent, false);
    RectTransform dailySubRect = dailySubGo.GetComponent<RectTransform>();
    dailySubRect.anchorMin = new Vector2(0f, 1f);
    dailySubRect.anchorMax = new Vector2(1f, 1f);
    dailySubRect.pivot = new Vector2(0.5f, 1f);
    dailySubRect.sizeDelta = new Vector2(-120f, 50f);
    dailySubRect.anchoredPosition = new Vector2(0f, -130f);
    gameOverDailySubtitleTmp = dailySubGo.AddComponent<TextMeshProUGUI>();
    gameOverDailySubtitleTmp.text = "";
    gameOverDailySubtitleTmp.fontStyle = FontStyles.Bold;
    gameOverDailySubtitleTmp.alignment = TextAlignmentOptions.Center;
    gameOverDailySubtitleTmp.color = new Color(1f, 0.85f, 0.35f);
    gameOverDailySubtitleTmp.enableAutoSizing = true;
    gameOverDailySubtitleTmp.fontSizeMin = 22f;
    gameOverDailySubtitleTmp.fontSizeMax = 38f;
    gameOverDailySubtitleTmp.enableWordWrapping = false;
    dailySubGo.SetActive(false);

    // Headline "Final Score: X" — one line, anchored just below the title.
    GameObject scoreGo = new GameObject("FinalScore", typeof(RectTransform));
    scoreGo.transform.SetParent(contentParent, false);
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
    labelGo.transform.SetParent(contentParent, false);
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
    iconsGo.transform.SetParent(contentParent, false);
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
        contentParent, "RetryButton", "Try Again",
        new Color(0.20f, 0.55f, 0.85f), new Vector2(0f, 175f), new Vector2(360f, 100f),
        RestartGame);
    restartButton = retryBtn;

    BuildPanelButton(
        contentParent, "MainMenuButton", "Main Menu",
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

    CrystalButtonStyle.Apply(btnGo, bgColor);
    return btn;
  }

  // ---------------------------------------------------------------------------
  // Menu panels (main menu + help)
  // ---------------------------------------------------------------------------

  // Builds the main menu — title + three buttons (Play / Daily / Help) —
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

    GameObject panel = BuildFullScreenPanel("MainMenuPanel (auto)", new Color(0.05f, 0.07f, 0.10f, 0.95f), out Transform contentParent);

    // ---- Logo --------------------------------------------------------------
    // Wrapper so the pulse animation scales/rotates the inner title without
    // moving the layout-anchored RectTransform. Layered structure:
    //   TitleAnchor (full-width strip, fixed top-anchor)
    //     └─ TitleLogo (pulses; pivot center)
    //          ├─ TitleShadow (back-rendered black silhouette, soft blur)
    //          └─ TitleFace   (gradient + outline + glow)
    // The shadow is a separate TMP object instead of using TMP's underlay so
    // we don't depend on the project's font asset having underlay configured.
    GameObject anchor = new GameObject("TitleAnchor", typeof(RectTransform));
    anchor.transform.SetParent(contentParent, false);
    RectTransform anchorRect = anchor.GetComponent<RectTransform>();
    anchorRect.anchorMin = new Vector2(0f, 1f);
    anchorRect.anchorMax = new Vector2(1f, 1f);
    anchorRect.pivot = new Vector2(0.5f, 1f);
    anchorRect.sizeDelta = new Vector2(-100f, 220f);
    anchorRect.anchoredPosition = new Vector2(0f, -90f);

    GameObject logo = new GameObject("TitleLogo", typeof(RectTransform));
    logo.transform.SetParent(anchor.transform, false);
    RectTransform logoRect = logo.GetComponent<RectTransform>();
    logoRect.anchorMin = Vector2.zero;
    logoRect.anchorMax = Vector2.one;
    logoRect.offsetMin = Vector2.zero;
    logoRect.offsetMax = Vector2.zero;
    logoRect.pivot = new Vector2(0.5f, 0.5f);

    BuildLogoTitle(logo.transform);

    // Pulse animation on the wrapper, not the TMP itself, so the inner text
    // can keep its baseline positioning and auto-size logic without the
    // animation fighting it.
    logo.AddComponent<TitlePulse>();

    // ---- Tagline ----------------------------------------------------------
    // Refined tagline: italic, slightly tighter letter spacing, dropped a
    // bit further down so it doesn't crowd the bigger logo above.
    GameObject subGo = new GameObject("Tagline", typeof(RectTransform));
    subGo.transform.SetParent(contentParent, false);
    RectTransform subRect = subGo.GetComponent<RectTransform>();
    subRect.anchorMin = new Vector2(0f, 1f);
    subRect.anchorMax = new Vector2(1f, 1f);
    subRect.pivot = new Vector2(0.5f, 1f);
    subRect.sizeDelta = new Vector2(-120f, 60f);
    subRect.anchoredPosition = new Vector2(0f, -325f);
    TextMeshProUGUI sub = subGo.AddComponent<TextMeshProUGUI>();
    sub.text = "Catch the gems. Don't miss.";
    sub.fontStyle = FontStyles.Italic;
    sub.alignment = TextAlignmentOptions.Center;
    sub.color = new Color(0.85f, 0.82f, 0.72f);
    sub.characterSpacing = 6f;
    sub.enableAutoSizing = true;
    sub.fontSizeMin = 22f;
    sub.fontSizeMax = 38f;
    sub.enableWordWrapping = false;

    // Best score chip — only visible if the player has a saved high score, so the
    // first run isn't cluttered with "BEST: 0".
    if (highScore > 0)
    {
      GameObject bestGo = new GameObject("BestScore", typeof(RectTransform));
      bestGo.transform.SetParent(contentParent, false);
      RectTransform bestRect = bestGo.GetComponent<RectTransform>();
      bestRect.anchorMin = new Vector2(0f, 1f);
      bestRect.anchorMax = new Vector2(1f, 1f);
      bestRect.pivot = new Vector2(0.5f, 1f);
      bestRect.sizeDelta = new Vector2(-120f, 50f);
      bestRect.anchoredPosition = new Vector2(0f, -395f);
      TextMeshProUGUI best = bestGo.AddComponent<TextMeshProUGUI>();
      best.text = "BEST  " + highScore;
      best.fontStyle = FontStyles.Bold;
      best.alignment = TextAlignmentOptions.Center;
      best.color = new Color(1f, 0.85f, 0.35f);
      best.characterSpacing = 8f;
      best.enableAutoSizing = true;
      best.fontSizeMin = 22f;
      best.fontSizeMax = 36f;
      best.enableWordWrapping = false;
    }

    // Centered button stack — anchored to vertical center for stable layout across resolutions.
    GameObject stackGo = new GameObject("ButtonStack",
        typeof(RectTransform), typeof(VerticalLayoutGroup));
    stackGo.transform.SetParent(contentParent, false);
    RectTransform stackRect = stackGo.GetComponent<RectTransform>();
    stackRect.anchorMin = new Vector2(0.5f, 0.5f);
    stackRect.anchorMax = new Vector2(0.5f, 0.5f);
    stackRect.pivot = new Vector2(0.5f, 0.5f);
    // Sized for 3 menu buttons: 3 * 100px tall + 2 * 22px spacing ≈ 344px.
    stackRect.anchoredPosition = new Vector2(0f, -50f);
    stackRect.sizeDelta = new Vector2(520f, 400f);
    VerticalLayoutGroup vlg = stackGo.GetComponent<VerticalLayoutGroup>();
    vlg.childAlignment = TextAnchor.MiddleCenter;
    vlg.spacing = 22f;
    vlg.childControlWidth = false;
    vlg.childControlHeight = false;
    vlg.childForceExpandWidth = false;
    vlg.childForceExpandHeight = false;

    BuildStackedMenuButton(stackGo.transform, "PlayButton",        "Play",        new Color(0.20f, 0.60f, 0.35f), OnPlayClicked);
    // Daily Challenge — second button. The label is rebuilt every time the
    // menu is shown (NEW / STREAK X / etc.), see RefreshDailyChallengeButton.
    dailyChallengeMenuButton = BuildStackedMenuButton(
        stackGo.transform, "DailyChallengeButton", "Daily Challenge",
        new Color(0.85f, 0.55f, 0.15f), OnDailyChallengeClicked);
    dailyChallengeButtonBg = dailyChallengeMenuButton.GetComponent<Image>();
    dailyChallengeButtonLabel = dailyChallengeMenuButton.GetComponentInChildren<TextMeshProUGUI>();
    BuildStackedMenuButton(stackGo.transform, "HelpButton",        "Help",        new Color(0.45f, 0.30f, 0.65f), OnHelpClicked);

    // Small gear button anchored to the top-right corner of the main menu
    // for sound / haptics toggles. Doubles as the only entry point to the
    // settings panel. Parented to the SAFE AREA so the gear stays inside the
    // visible play area on iPhones with a Dynamic Island.
    BuildSettingsCornerButton(contentParent);

    mainMenuPanel = panel;
    mainMenuPanel.SetActive(false);
  }

  // Builds the styled main-menu logo. Layered TMP setup that produces a "real
  // game logo" look without depending on a custom font / underlay-enabled
  // material:
  //   1. A back-rendered TitleShadow TMP draws a slightly larger, soft,
  //      semi-transparent black silhouette behind the face — a fake drop
  //      shadow that works on every default TMP font asset.
  //   2. A TitleFace TMP renders the actual letters with a top-to-bottom
  //      gold→amber→deep-orange vertex gradient and a dark outline / face
  //      dilate where the font asset supports it.
  //   3. The shared parent rect stretches to fill its TitleAnchor strip and
  //      uses TMP auto-sizing so the logo scales gracefully on portrait
  //      phones the same way the old single-line title did.
  // Returns the face TMP so callers can grab a reference if they want.
  TextMeshProUGUI BuildLogoTitle(Transform parent)
  {
    const string text = "GEM CATCH";
    const float fontSizeMin = 64f;
    const float fontSizeMax = 168f;
    const float characterSpacing = 14f;

    // ---- Drop shadow ------------------------------------------------------
    GameObject shadowGo = new GameObject("TitleShadow", typeof(RectTransform));
    shadowGo.transform.SetParent(parent, false);
    RectTransform shadowRect = shadowGo.GetComponent<RectTransform>();
    shadowRect.anchorMin = Vector2.zero;
    shadowRect.anchorMax = Vector2.one;
    shadowRect.offsetMin = Vector2.zero;
    shadowRect.offsetMax = Vector2.zero;
    // Offset the shadow down-right by a few pixels so the face sits slightly
    // proud of it. Sized in anchored coords so the offset scales with the
    // panel.
    shadowRect.anchoredPosition = new Vector2(6f, -8f);

    TextMeshProUGUI shadow = shadowGo.AddComponent<TextMeshProUGUI>();
    shadow.text = text;
    shadow.fontStyle = FontStyles.Bold;
    shadow.alignment = TextAlignmentOptions.Center;
    shadow.color = new Color(0f, 0f, 0f, 0.55f);
    shadow.characterSpacing = characterSpacing;
    shadow.enableAutoSizing = true;
    shadow.fontSizeMin = fontSizeMin;
    shadow.fontSizeMax = fontSizeMax;
    shadow.enableWordWrapping = false;
    shadow.raycastTarget = false;

    // ---- Face -------------------------------------------------------------
    GameObject faceGo = new GameObject("TitleFace", typeof(RectTransform));
    faceGo.transform.SetParent(parent, false);
    RectTransform faceRect = faceGo.GetComponent<RectTransform>();
    faceRect.anchorMin = Vector2.zero;
    faceRect.anchorMax = Vector2.one;
    faceRect.offsetMin = Vector2.zero;
    faceRect.offsetMax = Vector2.zero;

    TextMeshProUGUI face = faceGo.AddComponent<TextMeshProUGUI>();
    face.text = text;
    face.fontStyle = FontStyles.Bold;
    face.alignment = TextAlignmentOptions.Center;
    face.color = Color.white; // base; vertex gradient multiplies in over this
    face.characterSpacing = characterSpacing;
    face.enableAutoSizing = true;
    face.fontSizeMin = fontSizeMin;
    face.fontSizeMax = fontSizeMax;
    face.enableWordWrapping = false;
    face.raycastTarget = false;

    // Top-to-bottom metallic-jewel gradient — bright cream up top fading into
    // a deep orange at the baseline. Both top corners and both bottom corners
    // share a row color so the gradient is purely vertical.
    face.enableVertexGradient = true;
    face.colorGradient = new VertexGradient(
        new Color(1.00f, 0.96f, 0.62f), // top-left
        new Color(1.00f, 0.96f, 0.62f), // top-right
        new Color(1.00f, 0.55f, 0.10f), // bottom-left
        new Color(1.00f, 0.55f, 0.10f)  // bottom-right
    );

    // Outline + face dilate on a private material instance so we don't mutate
    // the shared TMP default. Wrapped in try/catch because some font assets
    // ship without these shader properties — the logo still looks good
    // without the outline thanks to the gradient + drop shadow.
    try
    {
      Material runtimeMat = new Material(face.fontMaterial);
      face.fontMaterial = runtimeMat;

      if (runtimeMat.HasProperty(ShaderUtilities.ID_OutlineWidth))
      {
        runtimeMat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
      }
      if (runtimeMat.HasProperty(ShaderUtilities.ID_OutlineColor))
      {
        runtimeMat.SetColor(ShaderUtilities.ID_OutlineColor,
            new Color(0.20f, 0.10f, 0.02f, 1.0f));
      }
      if (runtimeMat.HasProperty(ShaderUtilities.ID_FaceDilate))
      {
        runtimeMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.10f);
      }
    }
    catch
    {
      // Material customization is purely cosmetic — failures shouldn't break
      // the menu.
    }

    return face;
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

    GameObject panel = BuildFullScreenPanel("HelpPanel (auto)", new Color(0.05f, 0.07f, 0.10f, 0.97f), out Transform contentParent);

    AddPanelTitle(contentParent, "HOW TO PLAY", new Color(1f, 0.85f, 0.35f), 100f);

    string helpText =
        "Catch the falling gems with your glass cube catcher.\n\n" +

        "<b>Controls</b>\n" +
        "  • During the placement countdown, tap any slot at the bottom of the screen to position the catcher — or drag left/right to slide it.\n" +
        "  • You can reposition the catcher as many times as you want before the countdown ends.\n\n" +

        "<b>Scoring</b>\n" +
        "  • Catch a gem: <color=#7FE787>+20 points</color>.\n" +
        "  • Miss a gem: <color=#FF7373>-1 life</color>.\n" +
        "  • Every <b>100 points</b> earns you a bonus life (capped at <b>10</b>).\n\n" +

        "<b>Combo Streak</b>\n" +
        "  Catch gems in a row to build a streak \u2014 bigger streaks multiply every catch.\n" +
        "  • <color=#FFE885>\u00d71.5</color> at 3 catches\n" +
        "  • <color=#FFC065>\u00d72</color> at 5 catches\n" +
        "  • <color=#FF8B40>\u00d73</color> at 7 catches\n" +
        "  • <color=#FF4F4F>\u00d75</color> at 10 catches\n" +
        "  A miss or a caught bomb breaks the streak. <color=#FFD86A>Shield</color> saves it.\n\n" +

        "<b>Special Gems</b>\n" +
        "  • <color=#FFD86A>Golden gem</color> (rare): <color=#7FE787>+100 points</color>.\n" +
        "  • <color=#FFE066>Gold bar</color> (jackpot, very rare): <color=#7FE787>+500 points</color> \u2014 unmistakable wide gold shape.\n" +
        "  • <color=#FF6B5B>Bomb gem</color>: <color=#FF7373>DON'T CATCH IT</color> \u2014 costs a life and breaks your streak. Let it fall past you.\n" +
        "  • <color=#FF8FB8>Heart gem</color> (very rare): <color=#7FE787>+1 life</color> on catch.\n\n" +

        "<b>Power-Ups</b>\n" +
        "  After your first <b>10 drops</b>, a glowing pickup arrives every <b>10 drops</b> like clockwork. Catch it to activate \u2014 each effect stays on until your next miss.\n" +
        "  • <color=#6FD9FF>WIDE CATCHER</color> \u2014 your cube grows wider.\n" +
        "  • <color=#FFD86A>SHIELD</color> \u2014 absorbs your next miss without penalty.\n" +
        "  • <color=#7FE787>2\u00d7 SCORE</color> \u2014 every catch is worth double.\n\n" +

        "<b>Milestones</b>\n" +
        "  Hitting <b>500 / 1000 / 2500 / 5000 / 10000</b> points triggers a celebration banner plus a free reward \u2014 power-ups, bonus lives, or both. The big one at 10000 gives you everything.\n\n" +

        "<b>Difficulty</b>\n" +
        "  • Gems fall faster as your score climbs.\n" +
        "  • At <b>1000 points</b> gems shrink to half size \u2014 keep your eyes sharp.\n" +
        "  • At <b>2000 points</b> your catcher also shrinks to half size \u2014 tighter aim required.\n\n" +

        "<b>Daily Challenge</b>\n" +
        "  • One run per day with a fixed sequence \u2014 every player faces the same gems.\n" +
        "  • Locked at <b>3 lives</b>, no bonus lives, ends after 30 gems. Build a streak to climb the daily score.\n\n" +

        "<b>Game Over</b>\n" +
        "  • You start with <b>3 lives</b> (max <b>10</b>). The game ends when you run out.\n\n" +

        "Good luck!";

    BuildScrollableTextBlock(
        contentParent, "HelpScroll", helpText,
        // Reserve ~240 px at the top (title + a bit of breathing room) and ~210 px
        // at the bottom for the Back button and its margin.
        offsetMin: new Vector2(-560f, 210f),
        offsetMax: new Vector2(560f, -240f));

    BuildPanelButton(contentParent, "BackButton", "Back",
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
  // The returned panel's RectTransform fills the WHOLE screen (so background
  // dim/color extends behind the Dynamic Island, notch, home indicator, etc.),
  // but a SafeArea child is automatically created and returned via
  // <paramref name="contentParent"/>. Callers MUST parent readable content
  // (titles, buttons, text blocks) to <paramref name="contentParent"/> — not
  // to the panel directly — or that content will sit under the device's
  // hardware cutouts on iPhone (and modern iPad in some split views).
  GameObject BuildFullScreenPanel(string name, Color bgColor, out Transform contentParent)
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

    // Safe-area inset wrapper. SafeAreaFitter rewrites anchorMin/Max each
    // frame to match Screen.safeArea — so a child anchored to anchorMax=(.5,1)
    // ends up at the top edge of the SAFE AREA, not the top of the screen,
    // keeping titles out from under the Dynamic Island / notch / camera lens.
    GameObject safeAreaGo = new GameObject("SafeArea", typeof(RectTransform));
    safeAreaGo.transform.SetParent(panel.transform, false);
    RectTransform safeRect = safeAreaGo.GetComponent<RectTransform>();
    safeRect.anchorMin = Vector2.zero;
    safeRect.anchorMax = Vector2.one;
    safeRect.offsetMin = Vector2.zero;
    safeRect.offsetMax = Vector2.zero;
    safeAreaGo.AddComponent<SafeAreaFitter>();
    contentParent = safeAreaGo.transform;

    return panel;
  }

  // Helper used by sub-panels (help, daily cooldown) for their headline text. Stretches
  // horizontally with 60px side margins (sizeDelta.x = -120 in stretched mode) and uses
  // TMP auto-sizing so the font shrinks on narrow screens.
  void AddPanelTitle(Transform parent, string text, Color color, float topOffset)
  {
    GameObject titleGo = new GameObject("Title", typeof(RectTransform));
    titleGo.transform.SetParent(parent, false);
    RectTransform rect = titleGo.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0f, 1f);
    rect.anchorMax = new Vector2(1f, 1f);
    rect.pivot = new Vector2(0.5f, 1f);
    rect.sizeDelta = new Vector2(-120f, 140f);
    rect.anchoredPosition = new Vector2(0f, -topOffset);
    TextMeshProUGUI tmp = titleGo.AddComponent<TextMeshProUGUI>();
    tmp.text = text;
    tmp.fontStyle = FontStyles.Bold;
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.color = color;
    tmp.enableAutoSizing = true;
    tmp.fontSizeMin = 48f;
    tmp.fontSizeMax = 96f;
    tmp.enableWordWrapping = false;
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

    CrystalButtonStyle.Apply(btnGo, bgColor);
    return btn;
  }

  // ---------------------------------------------------------------------------
  // Menu navigation
  // ---------------------------------------------------------------------------

  void ShowMainMenu()
  {
    GameState.IsPlaying = false;
    SetGameplayHudVisible(false);
    FadePanel(gameOverPanel, false);
    FadePanel(helpPanel, false);
    FadePanel(dailyCooldownPanel, false);
    FadePanel(settingsPanel, false);
    // Refresh the daily button label/color in case the streak / lockout state
    // changed since last menu visit (e.g. UTC midnight rolled over while the
    // app was foregrounded).
    RefreshDailyChallengeButton();
    FadePanel(mainMenuPanel, true, 0.25f);
  }

  void ShowGameplay()
  {
    FadePanel(mainMenuPanel, false);
    FadePanel(helpPanel, false);
    FadePanel(gameOverPanel, false);
    FadePanel(dailyCooldownPanel, false);

    SetGameplayHudVisible(true);
    GameState.IsPlaying = true;
    gameIsOver = false;

    // In Daily mode show a brief "DAILY · DAY N" banner so the player knows
    // they're in the special run. Reuses the existing banner system.
    if (GameState.Mode == GameState.GameMode.Daily)
    {
      SpawnBannerNotification(
          "DAILY \u2022 DAY " + DailyChallenge.DayNumber,
          new Color(1f, 0.85f, 0.35f));
    }
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

  void OnHelpClicked()
  {
    FadePanel(mainMenuPanel, false);
    FadePanel(helpPanel, true);
    if (helpPanel != null)
    {
      // Force layout pass now so the ContentSizeFitter inside our scroll view sizes
      // the content to the wrapped TMP text on the same frame the panel becomes
      // visible (otherwise the very first frame can show empty / mis-sized content).
      RectTransform rt = helpPanel.transform as RectTransform;
      if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }
  }

  void OnHelpBackClicked()
  {
    FadePanel(helpPanel, false);
    ShowMainMenu();
  }

  // ---------------------------------------------------------------------------
  // Daily Challenge
  // ---------------------------------------------------------------------------

  // Updates the menu button label/color based on whether the player has
  // already played today. Called every time the main menu is shown so the
  // badge always reflects the current streak / status.
  void RefreshDailyChallengeButton()
  {
    if (dailyChallengeButtonLabel == null) return;

    if (DailyChallenge.HasPlayedToday)
    {
      // Already played — neutral green color, label shows the streak.
      int streak = DailyChallenge.CurrentStreak;
      dailyChallengeButtonLabel.text = streak > 0
          ? "Daily \u2022 Streak " + streak
          : "Daily \u2022 Done";
      if (dailyChallengeButtonBg != null)
      {
        dailyChallengeButtonBg.color = new Color(0.25f, 0.55f, 0.30f);
      }
    }
    else
    {
      // Available — bright orange "play me" call-to-action.
      dailyChallengeButtonLabel.text = "Daily \u2022 Day " + DailyChallenge.DayNumber;
      if (dailyChallengeButtonBg != null)
      {
        dailyChallengeButtonBg.color = new Color(0.85f, 0.55f, 0.15f);
      }
    }
  }

  // Click handler for the Daily Challenge menu button.
  // Already played today → cooldown panel.
  // First play of the day → commit the attempt and reload into Daily mode.
  void OnDailyChallengeClicked()
  {
    if (DailyChallenge.HasPlayedToday)
    {
      ShowDailyCooldownPanel();
      return;
    }

    // Commit the daily attempt up front. From this moment on the player can't
    // retry today even if they force-quit mid-round (the strict-Wordle rule).
    DailyChallenge.MarkStarted();

    // Reset score & lives, set Daily mode, and reload the scene. ObjectPooler.Start
    // reads GameState.Mode to seed its RNG deterministically, so we MUST reload
    // (we can't just flip the flag mid-session — the pooler already initialized).
    GemCatcher.ResetScore();
    GemCatcher.ResetLives();
    GameState.Mode = GameState.GameMode.Daily;
    GameState.SkipMainMenuOnLoad = true;
    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
  }

  // Show the "come back tomorrow" panel (built lazily on first show).
  void ShowDailyCooldownPanel()
  {
    EnsureDailyCooldownPanel();
    if (dailyCooldownPanel == null) return;
    RefreshDailyCooldownPanel();
    FadePanel(mainMenuPanel, false);
    FadePanel(dailyCooldownPanel, true);
  }

  void OnDailyCooldownBackClicked()
  {
    FadePanel(dailyCooldownPanel, false);
    ShowMainMenu();
  }

  // Builds the cooldown panel: streak / day / today's score / countdown.
  // Layout mirrors the help panel so the visual feel is consistent.
  void EnsureDailyCooldownPanel()
  {
    if (dailyCooldownPanel != null) return;
    EnsureHudCanvas();
    if (hudCanvas == null) return;

    GameObject panel = BuildFullScreenPanel("DailyCooldownPanel (auto)", new Color(0.05f, 0.07f, 0.10f, 0.97f), out Transform contentParent);
    AddPanelTitle(contentParent, "DAILY DONE", new Color(0.55f, 0.85f, 0.55f), 100f);

    // "Day 217" subtitle.
    dailyCooldownDayTmp = AddCooldownLine(contentParent, "DayTmp",
        anchoredY: -260f, fontSizeMin: 28f, fontSizeMax: 44f,
        color: new Color(0.85f, 0.85f, 0.9f), bold: false);

    // Big streak number — the hero element of this screen.
    dailyCooldownStreakTmp = AddCooldownLine(contentParent, "StreakTmp",
        anchoredY: -360f, fontSizeMin: 48f, fontSizeMax: 96f,
        color: new Color(1f, 0.85f, 0.35f), bold: true);

    // Today's score and best streak — two smaller secondary lines.
    dailyCooldownScoreTmp = AddCooldownLine(contentParent, "ScoreTmp",
        anchoredY: -490f, fontSizeMin: 24f, fontSizeMax: 40f,
        color: Color.white, bold: false);
    dailyCooldownBestTmp = AddCooldownLine(contentParent, "BestTmp",
        anchoredY: -550f, fontSizeMin: 22f, fontSizeMax: 36f,
        color: new Color(0.7f, 0.7f, 0.75f), bold: false);

    // Live countdown to next reset — UPDATED EVERY FRAME by TickDailyCooldown.
    dailyCooldownTimerTmp = AddCooldownLine(contentParent, "TimerTmp",
        anchoredY: -680f, fontSizeMin: 24f, fontSizeMax: 40f,
        color: new Color(0.6f, 0.85f, 1f), bold: true);

    BuildPanelButton(contentParent, "BackButton", "Back",
        new Color(0.35f, 0.35f, 0.40f), new Vector2(0f, 80f), new Vector2(280f, 90f),
        OnDailyCooldownBackClicked);

    dailyCooldownPanel = panel;
    dailyCooldownPanel.SetActive(false);
  }

  // Helper for the cooldown panel: stretches a TMP line full-width with side
  // margins, anchored from the top edge of the panel.
  TextMeshProUGUI AddCooldownLine(Transform parent, string name, float anchoredY,
      float fontSizeMin, float fontSizeMax, Color color, bool bold)
  {
    GameObject go = new GameObject(name, typeof(RectTransform));
    go.transform.SetParent(parent, false);
    RectTransform rt = go.GetComponent<RectTransform>();
    rt.anchorMin = new Vector2(0f, 1f);
    rt.anchorMax = new Vector2(1f, 1f);
    rt.pivot = new Vector2(0.5f, 1f);
    rt.sizeDelta = new Vector2(-120f, fontSizeMax * 1.4f);
    rt.anchoredPosition = new Vector2(0f, anchoredY);
    TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.color = color;
    if (bold) tmp.fontStyle = FontStyles.Bold;
    tmp.enableAutoSizing = true;
    tmp.fontSizeMin = fontSizeMin;
    tmp.fontSizeMax = fontSizeMax;
    tmp.enableWordWrapping = false;
    return tmp;
  }

  // Pulls the latest values from DailyChallenge into the cooldown panel TMPs.
  // Called once when the panel opens; the live timer is then updated each frame
  // by TickDailyCooldown.
  void RefreshDailyCooldownPanel()
  {
    if (dailyCooldownPanel == null) return;

    if (dailyCooldownDayTmp != null)
    {
      dailyCooldownDayTmp.text = "Day " + DailyChallenge.DayNumber;
    }
    if (dailyCooldownStreakTmp != null)
    {
      int streak = DailyChallenge.CurrentStreak;
      dailyCooldownStreakTmp.text = streak > 0
          ? "STREAK  " + streak
          : "STREAK  --";
    }
    if (dailyCooldownScoreTmp != null)
    {
      int last = DailyChallenge.LastScore;
      dailyCooldownScoreTmp.text = last > 0
          ? "Today's Score: " + last
          : "Today: not finished";
    }
    if (dailyCooldownBestTmp != null)
    {
      int best = DailyChallenge.BestStreak;
      dailyCooldownBestTmp.text = best > 0 ? "Best Streak: " + best : "";
    }
    UpdateDailyCooldownTimer();
  }

  // Throttle the cooldown timer to one repaint per second — the format is
  // whole-second granularity so per-frame updates would just thrash TMP.
  private float dailyCooldownTickAccumulator;
  void TickDailyCooldown()
  {
    if (dailyCooldownPanel == null || !dailyCooldownPanel.activeInHierarchy) return;
    dailyCooldownTickAccumulator += Time.unscaledDeltaTime;
    if (dailyCooldownTickAccumulator < 1f) return;
    dailyCooldownTickAccumulator = 0f;
    UpdateDailyCooldownTimer();
  }

  // Repaints just the live countdown — cheap, runs every frame while the
  // cooldown panel is visible. Falls back to "Resets soon" once the timer
  // is below a second to keep the format short.
  void UpdateDailyCooldownTimer()
  {
    if (dailyCooldownTimerTmp == null) return;
    TimeSpan ts = DailyChallenge.TimeUntilNextChallenge;
    string text;
    if (ts.TotalSeconds < 1)
    {
      text = "New challenge available now!";
    }
    else if (ts.TotalHours >= 1)
    {
      text = string.Format("Next challenge in {0}h {1:00}m {2:00}s",
          (int)ts.TotalHours, ts.Minutes, ts.Seconds);
    }
    else
    {
      text = string.Format("Next challenge in {0:00}m {1:00}s",
          ts.Minutes, ts.Seconds);
    }
    dailyCooldownTimerTmp.text = text;
  }

  // Game over → "Main Menu". Reload the scene without setting SkipMainMenuOnLoad so we
  // land on the main menu on the next scene start. Always reset Mode to Normal —
  // a daily run is one-and-done, and a normal run obviously stays normal.
  void ReturnToMainMenu()
  {
    GemCatcher.ResetScore();
    GemCatcher.ResetLives();
    GameState.SkipMainMenuOnLoad = false;
    GameState.Mode = GameState.GameMode.Normal;
    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
  }

  // Spawn a "+20" / "-N" numeric pop-up at the given world position, on the HUD canvas.
  public void SpawnFloatingScore(int amount, Vector3 worldPosition)
  {
    GameObject go = CreateFloatingTextHost(worldPosition);
    if (go == null) return;
    go.AddComponent<FloatingScoreText>().Initialize(amount);
  }

  // -------------------------------------------------------------------------
  // Bonus-life notification — fired by GemCatcher when AddLives runs (the
  // every-third-catch combo award and the ExtraLife power-up are the two
  // sources). Banner text adapts to the count actually granted, so a player
  // near the MAX_LIVES cap doesn't see "EXTRA LIVES +3" when only +1 fit.
  // -------------------------------------------------------------------------

  void HandleBonusLifeAwarded(int count)
  {
    string text = count == 1
        ? "EXTRA LIFE  +1 \u2665"
        : "EXTRA LIVES  +" + count + " \u2665";
    SpawnBannerNotification(text, new Color(1f, 0.85f, 0.35f));

    // Quick scale pop on the lives counter so the heart count visibly reacts.
    if (livesDisplay != null)
    {
      livesDisplay.transform.localScale = Vector3.one * 1.4f;
    }
  }

  // Spawns a top-of-screen, auto-sizing banner with the given text/color. Self-
  // destructs after the BannerNotification component finishes its in/hold/out
  // animation. Positioned below the placement countdown area so the two don't
  // collide.
  public void SpawnBannerNotification(string text, Color color)
  {
    EnsureHudCanvas();
    if (UiRoot == null) return;

    GameObject go = new GameObject("BannerNotification (auto)", typeof(RectTransform));
    go.transform.SetParent(UiRoot, false);
    RectTransform rt = go.GetComponent<RectTransform>();
    rt.anchorMin = new Vector2(0f, 1f);
    rt.anchorMax = new Vector2(1f, 1f);
    rt.pivot = new Vector2(0.5f, 1f);
    // sizeDelta.x = -120 with stretched anchors leaves a 60px margin on each side, so
    // long messages auto-fit any portrait/landscape phone via TMP auto-sizing.
    rt.sizeDelta = new Vector2(-120f, 110f);
    // Below the placement countdown number (which lives at -180 to -400) so a banner
    // shown during a placement phase doesn't sit directly on top of the countdown.
    rt.anchoredPosition = new Vector2(0f, -460f);

    TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
    tmp.text = text;
    tmp.color = color;
    tmp.fontStyle = FontStyles.Bold;
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.enableAutoSizing = true;
    tmp.fontSizeMin = 40f;
    tmp.fontSizeMax = 90f;
    tmp.enableWordWrapping = false;
    // Render above the placement countdown if both ever overlap.
    rt.SetAsLastSibling();

    go.AddComponent<BannerNotification>().Initialize(text, color);
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
    if (gameIsOver) return;
    gameIsOver = true;
    StartCoroutine(GameOverSequence());
  }

  // Hit-stop on the killing blow followed by the panel reveal. Uses unscaled time so
  // the timing is consistent regardless of the time-scale dip during the hit-stop.
  // CameraShake also runs on unscaled time, so its shake keeps playing through the dip.
  System.Collections.IEnumerator GameOverSequence()
  {
    const float hitStopScale = 0.12f;
    const float hitStopDuration = 0.42f;

    float prevScale = Time.timeScale;
    Time.timeScale = hitStopScale;
    yield return new WaitForSecondsRealtime(hitStopDuration);
    // Recover from our own dip but don't stomp on a user-initiated pause that may have
    // happened in the meantime.
    if (Mathf.Approximately(Time.timeScale, hitStopScale))
    {
      Time.timeScale = prevScale > 0f ? prevScale : 1f;
    }

    float remaining = Mathf.Max(0f, gameOverDelay - hitStopDuration);
    if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);

    ShowGameOverPanel();
  }

  void ShowGameOverPanel()
  {
    bool isDaily = GameState.Mode == GameState.GameMode.Daily;
    int finalScore = GemCatcher.Score;

    if (isDaily)
    {
      // Daily runs commit their result here. RecordCompletion advances the
      // streak (or resets it) and persists the score for the cooldown panel.
      DailyChallenge.RecordCompletion(finalScore);
    }

    // Title swap + daily subtitle. The subtitle GameObject lives in the same
    // panel and is just toggled on/off based on mode.
    if (gameOverTitleTmp != null)
    {
      gameOverTitleTmp.text = isDaily ? "Daily Done!" : "Game Over";
    }
    if (gameOverDailySubtitleTmp != null)
    {
      if (isDaily)
      {
        int streak = DailyChallenge.CurrentStreak;
        gameOverDailySubtitleTmp.text = string.Format(
            "Day {0}  \u2022  Streak {1}", DailyChallenge.DayNumber, streak);
        gameOverDailySubtitleTmp.gameObject.SetActive(true);
      }
      else
      {
        gameOverDailySubtitleTmp.gameObject.SetActive(false);
      }
    }

    // Hide "Try Again" in daily mode — one attempt per day is the whole point
    // of the mode. Main Menu remains as the only path off the screen.
    if (restartButton != null)
    {
      restartButton.gameObject.SetActive(!isDaily);
    }

    // Populate the breakdown BEFORE fading in so layout is settled when the panel appears.
    if (finalScoreText != null) finalScoreText.text = BuildGameOverSummary();
    RebuildGemIcons();

    // Slow fade-in for the game-over panel — feels more like a curtain than a snap.
    FadePanel(gameOverPanel, true, 0.45f);

    // Count the final-score number up from 0 to its real value as the panel
    // fades in. Reads as more dramatic / earned than a static number snapping
    // into place at the same moment as the title.
    StartFinalScoreCountUp(finalScore, 1.1f);

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

    // "Try Again" only exists for Normal mode (the button is hidden in daily
    // game-over). Defensively force Mode back to Normal so the next round
    // can never accidentally re-enter the daily seed.
    GameState.Mode = GameState.GameMode.Normal;

    // "Try Again": skip the main menu on the next scene start and drop the player
    // straight into a fresh round.
    GameState.SkipMainMenuOnLoad = true;
    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
  }

  // Called by ObjectPooler when a new placement phase starts
  public void OnPlacementPhaseStarted(float duration)
  {
    if (gemSpeedupTimerText == null) return;

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

  // ---------------------------------------------------------------------------
  // Polish layer — pause overlay, power-up vignette flash, settings panel,
  // and the count-up tween on the game-over screen. All auto-built on first
  // demand so the project doesn't need any inspector wiring.
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Show the auto-paused overlay with a Resume button. Called from
  /// <see cref="PauseHandler"/> when the OS backgrounds the app. Safe to call
  /// repeatedly; the panel is built once and reused.
  /// </summary>
  public void ShowPauseOverlay()
  {
    EnsurePausePanel();
    if (pausePanel == null) return;
    // Use unscaled-time fade because Time.timeScale is already 0 here.
    FadePanel(pausePanel, true, 0.18f);
  }

  /// <summary>Hide the pause overlay (called when the player taps Resume).</summary>
  public void HidePauseOverlay()
  {
    if (pausePanel == null) return;
    FadePanel(pausePanel, false, 0.15f);
  }

  void EnsurePausePanel()
  {
    if (pausePanel != null) return;
    EnsureHudCanvas();
    if (hudCanvas == null) return;

    GameObject panel = BuildFullScreenPanel(
        "PausePanel (auto)", new Color(0f, 0f, 0f, 0.78f), out Transform contentParent);

    AddPanelTitle(contentParent, "Paused", new Color(1f, 0.95f, 0.85f), 200f);

    GameObject hintGo = new GameObject("PauseHint", typeof(RectTransform));
    hintGo.transform.SetParent(contentParent, false);
    RectTransform hintRect = hintGo.GetComponent<RectTransform>();
    hintRect.anchorMin = new Vector2(0.5f, 0.5f);
    hintRect.anchorMax = new Vector2(0.5f, 0.5f);
    hintRect.pivot = new Vector2(0.5f, 0.5f);
    hintRect.sizeDelta = new Vector2(700f, 80f);
    hintRect.anchoredPosition = new Vector2(0f, 40f);
    TextMeshProUGUI hint = hintGo.AddComponent<TextMeshProUGUI>();
    hint.text = "The game paused while you were away.";
    hint.alignment = TextAlignmentOptions.Center;
    hint.fontStyle = FontStyles.Italic;
    hint.color = new Color(0.78f, 0.80f, 0.85f);
    hint.enableAutoSizing = true;
    hint.fontSizeMin = 22f;
    hint.fontSizeMax = 38f;
    hint.enableWordWrapping = false;

    BuildPanelButton(contentParent, "ResumeButton", "Resume",
        new Color(0.20f, 0.60f, 0.35f),
        new Vector2(0f, 240f), new Vector2(360f, 100f),
        OnPauseResumeClicked);

    pausePanel = panel;
    pausePanel.SetActive(false);
  }

  void OnPauseResumeClicked()
  {
    if (PauseHandler.Instance != null) PauseHandler.Instance.Resume();
    else HidePauseOverlay();
  }

  /// <summary>
  /// Briefly tint the screen with <paramref name="color"/>. Used on power-up
  /// activation to sell the "something cool just happened" moment without
  /// blocking gameplay or eating much performance budget.
  /// </summary>
  public void FlashVignette(Color color, float duration = 0.55f, float peakAlpha = 0.32f)
  {
    EnsureVignetteOverlay();
    if (vignetteOverlay == null) return;

    vignetteColor = color;
    vignetteDuration = Mathf.Max(0.05f, duration);
    vignettePeakAlpha = Mathf.Clamp01(peakAlpha);
    vignetteAge = 0f;

    Color c = color;
    c.a = 0f;
    vignetteOverlay.color = c;
    vignetteOverlay.gameObject.SetActive(true);
  }

  void EnsureVignetteOverlay()
  {
    if (vignetteOverlay != null) return;
    EnsureHudCanvas();
    if (hudCanvas == null) return;

    // Anchored to the canvas root (not the safe-area inset) so the wash
    // really does cover the whole display, including the top notch area.
    Transform parent = hudCanvas.transform;
    GameObject go = new GameObject("VignetteOverlay (auto)",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    go.transform.SetParent(parent, false);
    // Render below settings/menu panels but above gameplay HUD.
    go.transform.SetSiblingIndex(0);
    RectTransform rect = go.GetComponent<RectTransform>();
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;
    Image img = go.GetComponent<Image>();
    img.color = new Color(1f, 1f, 1f, 0f);
    img.raycastTarget = false; // never block taps on the catcher / slot area
    vignetteOverlay = img;
    go.SetActive(false);
  }

  void TickVignetteFlash()
  {
    if (vignetteOverlay == null) return;
    if (!vignetteOverlay.gameObject.activeSelf) return;

    vignetteAge += Time.unscaledDeltaTime;
    float t = Mathf.Clamp01(vignetteAge / vignetteDuration);

    // Symmetric ease-in-out: rise to peak around t=0.25, then ease out. Smoothstep
    // keeps both edges soft so it never feels like a hard flash.
    float envelope;
    if (t < 0.25f)
    {
      float k = t / 0.25f;
      envelope = Mathf.SmoothStep(0f, 1f, k);
    }
    else
    {
      float k = (t - 0.25f) / 0.75f;
      envelope = Mathf.SmoothStep(1f, 0f, k);
    }

    Color c = vignetteColor;
    c.a = vignettePeakAlpha * envelope;
    vignetteOverlay.color = c;

    if (t >= 1f) vignetteOverlay.gameObject.SetActive(false);
  }

  // -- Settings panel --------------------------------------------------------

  /// <summary>
  /// Build the settings panel on first demand. Music / SFX volume sliders and
  /// Haptics toggle, all backed by PlayerPrefs.
  /// </summary>
  void EnsureSettingsPanel()
  {
    if (settingsPanel != null) return;
    EnsureHudCanvas();
    if (hudCanvas == null) return;

    GameObject panel = BuildFullScreenPanel(
        "SettingsPanel (auto)", new Color(0.05f, 0.07f, 0.10f, 0.97f), out Transform contentParent);

    AddPanelTitle(contentParent, "Settings", new Color(1f, 0.85f, 0.35f), 100f);

    GameObject stackGo = new GameObject("ToggleStack",
        typeof(RectTransform), typeof(VerticalLayoutGroup));
    stackGo.transform.SetParent(contentParent, false);
    RectTransform stackRect = stackGo.GetComponent<RectTransform>();
    stackRect.anchorMin = new Vector2(0.5f, 0.5f);
    stackRect.anchorMax = new Vector2(0.5f, 0.5f);
    stackRect.pivot = new Vector2(0.5f, 0.5f);
    stackRect.anchoredPosition = new Vector2(0f, 40f);
    stackRect.sizeDelta = new Vector2(720f, 420f);
    VerticalLayoutGroup vlg = stackGo.GetComponent<VerticalLayoutGroup>();
    vlg.childAlignment = TextAnchor.MiddleCenter;
    vlg.spacing = 22f;
    vlg.childControlWidth = false;
    vlg.childControlHeight = false;
    vlg.childForceExpandWidth = false;
    vlg.childForceExpandHeight = false;

    BuildSettingsVolumeRow(stackGo.transform, "Music", SoundManager.MusicVolume,
        v => SoundManager.MusicVolume = v);
    BuildSettingsVolumeRow(stackGo.transform, "Sound FX", SoundManager.SfxVolume,
        v => SoundManager.SfxVolume = v);
    BuildSettingsToggleRow(stackGo.transform, "Haptics", HapticManager.HapticsEnabled,
        v => HapticManager.HapticsEnabled = v);

    BuildPanelButton(contentParent, "SettingsBackButton", "Back",
        new Color(0.35f, 0.35f, 0.40f), new Vector2(0f, 80f), new Vector2(280f, 90f),
        OnSettingsBackClicked);

    settingsPanel = panel;
    settingsPanel.SetActive(false);
  }

  // "Music  [========·---]  70%" — label, slider, percent readout.
  void BuildSettingsVolumeRow(Transform parent, string label, float initial, System.Action<float> onChange)
  {
    GameObject row = new GameObject(label + "VolumeRow",
        typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
    row.transform.SetParent(parent, false);
    RectTransform rowRect = row.GetComponent<RectTransform>();
    rowRect.sizeDelta = new Vector2(720f, 90f);
    LayoutElement le = row.GetComponent<LayoutElement>();
    le.preferredWidth = 720f;
    le.preferredHeight = 90f;
    HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
    hlg.childAlignment = TextAnchor.MiddleCenter;
    hlg.spacing = 16f;
    hlg.childControlWidth = false;
    hlg.childControlHeight = false;
    hlg.childForceExpandWidth = false;
    hlg.childForceExpandHeight = false;

    GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement));
    labelGo.transform.SetParent(row.transform, false);
    LayoutElement labelLe = labelGo.GetComponent<LayoutElement>();
    labelLe.preferredWidth = 200f;
    labelLe.preferredHeight = 90f;
    TextMeshProUGUI labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
    labelTmp.text = label;
    labelTmp.alignment = TextAlignmentOptions.MidlineRight;
    labelTmp.fontStyle = FontStyles.Bold;
    labelTmp.color = Color.white;
    labelTmp.fontSize = 40f;

    // Slider root
    GameObject sliderGo = new GameObject(label + "Slider",
        typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
    sliderGo.transform.SetParent(row.transform, false);
    LayoutElement sliderLe = sliderGo.GetComponent<LayoutElement>();
    sliderLe.preferredWidth = 360f;
    sliderLe.preferredHeight = 48f;
    RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
    sliderRect.sizeDelta = new Vector2(360f, 48f);

    Sprite whiteSprite = CreateUiWhiteSprite();

    // Background track
    GameObject bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    bgGo.transform.SetParent(sliderGo.transform, false);
    RectTransform bgRect = bgGo.GetComponent<RectTransform>();
    bgRect.anchorMin = new Vector2(0f, 0.25f);
    bgRect.anchorMax = new Vector2(1f, 0.75f);
    bgRect.offsetMin = Vector2.zero;
    bgRect.offsetMax = Vector2.zero;
    Image bgImg = bgGo.GetComponent<Image>();
    bgImg.sprite = whiteSprite;
    bgImg.color = new Color(0.25f, 0.27f, 0.32f, 1f);
    bgImg.type = Image.Type.Simple;

    // Fill area + fill
    GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
    fillArea.transform.SetParent(sliderGo.transform, false);
    RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
    fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
    fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
    fillAreaRect.offsetMin = new Vector2(6f, 0f);
    fillAreaRect.offsetMax = new Vector2(-6f, 0f);

    GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    fillGo.transform.SetParent(fillArea.transform, false);
    RectTransform fillRect = fillGo.GetComponent<RectTransform>();
    fillRect.anchorMin = Vector2.zero;
    fillRect.anchorMax = Vector2.one;
    fillRect.offsetMin = Vector2.zero;
    fillRect.offsetMax = Vector2.zero;
    Image fillImg = fillGo.GetComponent<Image>();
    fillImg.sprite = whiteSprite;
    fillImg.color = new Color(0.25f, 0.65f, 0.95f, 1f);

    // Handle
    GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
    handleArea.transform.SetParent(sliderGo.transform, false);
    RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
    handleAreaRect.anchorMin = Vector2.zero;
    handleAreaRect.anchorMax = Vector2.one;
    handleAreaRect.offsetMin = new Vector2(12f, 0f);
    handleAreaRect.offsetMax = new Vector2(-12f, 0f);

    GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    handleGo.transform.SetParent(handleArea.transform, false);
    RectTransform handleRect = handleGo.GetComponent<RectTransform>();
    handleRect.sizeDelta = new Vector2(36f, 36f);
    Image handleImg = handleGo.GetComponent<Image>();
    handleImg.sprite = whiteSprite;
    handleImg.color = Color.white;

    // Percent label
    GameObject pctGo = new GameObject("Percent", typeof(RectTransform), typeof(LayoutElement));
    pctGo.transform.SetParent(row.transform, false);
    LayoutElement pctLe = pctGo.GetComponent<LayoutElement>();
    pctLe.preferredWidth = 100f;
    pctLe.preferredHeight = 90f;
    TextMeshProUGUI pctTmp = pctGo.AddComponent<TextMeshProUGUI>();
    pctTmp.alignment = TextAlignmentOptions.MidlineLeft;
    pctTmp.fontStyle = FontStyles.Bold;
    pctTmp.color = new Color(0.85f, 0.85f, 0.9f);
    pctTmp.fontSize = 36f;

    Slider slider = sliderGo.GetComponent<Slider>();
    slider.targetGraphic = handleImg;
    slider.fillRect = fillRect;
    slider.handleRect = handleRect;
    slider.direction = Slider.Direction.LeftToRight;
    slider.minValue = 0f;
    slider.maxValue = 1f;
    slider.wholeNumbers = false;
    slider.value = Mathf.Clamp01(initial);

    System.Action refreshPct = () =>
    {
      pctTmp.text = Mathf.RoundToInt(slider.value * 100f) + "%";
    };
    refreshPct();

    slider.onValueChanged.AddListener(v =>
    {
      onChange?.Invoke(v);
      refreshPct();
    });
  }

  static Sprite s_uiWhiteSprite;
  static Sprite CreateUiWhiteSprite()
  {
    if (s_uiWhiteSprite != null) return s_uiWhiteSprite;
    Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
    Color[] pixels = new Color[16];
    for (int i = 0; i < 16; i++) pixels[i] = Color.white;
    tex.SetPixels(pixels);
    tex.Apply(false, true);
    s_uiWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    return s_uiWhiteSprite;
  }

  // Visual: "Haptics  [ ON ]" / "Haptics  [ OFF ]" — a label and a toggle button
  // colored green when on, gray when off. Cheap and reads instantly.
  void BuildSettingsToggleRow(Transform parent, string label, bool initial, System.Action<bool> onChange)
  {
    GameObject row = new GameObject(label + "Row",
        typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
    row.transform.SetParent(parent, false);
    RectTransform rowRect = row.GetComponent<RectTransform>();
    rowRect.sizeDelta = new Vector2(640f, 90f);
    LayoutElement le = row.GetComponent<LayoutElement>();
    le.preferredWidth = 640f;
    le.preferredHeight = 90f;
    HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
    hlg.childAlignment = TextAnchor.MiddleCenter;
    hlg.spacing = 28f;
    hlg.childControlWidth = false;
    hlg.childControlHeight = false;
    hlg.childForceExpandWidth = false;
    hlg.childForceExpandHeight = false;

    GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement));
    labelGo.transform.SetParent(row.transform, false);
    LayoutElement labelLe = labelGo.GetComponent<LayoutElement>();
    labelLe.preferredWidth = 280f;
    labelLe.preferredHeight = 90f;
    TextMeshProUGUI labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
    labelTmp.text = label;
    labelTmp.alignment = TextAlignmentOptions.MidlineRight;
    labelTmp.fontStyle = FontStyles.Bold;
    labelTmp.color = Color.white;
    labelTmp.fontSize = 46f;

    GameObject btnGo = new GameObject(label + "Toggle",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
    btnGo.transform.SetParent(row.transform, false);
    LayoutElement btnLe = btnGo.GetComponent<LayoutElement>();
    btnLe.preferredWidth = 220f;
    btnLe.preferredHeight = 90f;
    Image bg = btnGo.GetComponent<Image>();

    GameObject lblGo = new GameObject("Label", typeof(RectTransform));
    lblGo.transform.SetParent(btnGo.transform, false);
    RectTransform lblRect = lblGo.GetComponent<RectTransform>();
    lblRect.anchorMin = Vector2.zero;
    lblRect.anchorMax = Vector2.one;
    lblRect.offsetMin = Vector2.zero;
    lblRect.offsetMax = Vector2.zero;
    TextMeshProUGUI lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
    lblTmp.fontSize = 42f;
    lblTmp.fontStyle = FontStyles.Bold;
    lblTmp.alignment = TextAlignmentOptions.Center;
    lblTmp.color = Color.white;

    bool current = initial;
    System.Action apply = () =>
    {
      lblTmp.text = current ? "ON" : "OFF";
      bg.color = current ? new Color(0.20f, 0.55f, 0.30f) : new Color(0.45f, 0.20f, 0.20f);
    };
    apply();

    Button btn = btnGo.GetComponent<Button>();
    btn.targetGraphic = bg;
    btn.onClick.AddListener(() =>
    {
      current = !current;
      onChange?.Invoke(current);
      apply();
    });
  }

  void OnSettingsButtonClicked()
  {
    EnsureSettingsPanel();
    FadePanel(mainMenuPanel, false);
    FadePanel(settingsPanel, true, 0.2f);
  }

  void OnSettingsBackClicked()
  {
    FadePanel(settingsPanel, false, 0.15f);
    ShowMainMenu();
  }

  // Builds a small "Settings" pill anchored to the top-right of the main-menu
  // panel. Uses plain text for the label so it renders cleanly with any font
  // asset (the unicode gear glyph isn't guaranteed in the default TMP atlas).
  void BuildSettingsCornerButton(Transform parent)
  {
    GameObject btnGo = new GameObject("SettingsButton",
        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
    btnGo.transform.SetParent(parent, false);
    RectTransform rect = btnGo.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(1f, 1f);
    rect.anchorMax = new Vector2(1f, 1f);
    rect.pivot = new Vector2(1f, 1f);
    rect.anchoredPosition = new Vector2(-30f, -30f);
    rect.sizeDelta = new Vector2(220f, 80f);
    Image bg = btnGo.GetComponent<Image>();
    bg.color = new Color(0.20f, 0.22f, 0.28f, 0.85f);

    GameObject lblGo = new GameObject("Label", typeof(RectTransform));
    lblGo.transform.SetParent(btnGo.transform, false);
    RectTransform lblRect = lblGo.GetComponent<RectTransform>();
    lblRect.anchorMin = Vector2.zero;
    lblRect.anchorMax = Vector2.one;
    lblRect.offsetMin = Vector2.zero;
    lblRect.offsetMax = Vector2.zero;
    TextMeshProUGUI tmp = lblGo.AddComponent<TextMeshProUGUI>();
    tmp.text = "Settings";
    tmp.fontSize = 32f;
    tmp.fontStyle = FontStyles.Bold;
    tmp.alignment = TextAlignmentOptions.Center;
    tmp.color = Color.white;
    tmp.characterSpacing = 4f;

    Button btn = btnGo.GetComponent<Button>();
    btn.targetGraphic = bg;
    btn.onClick.AddListener(OnSettingsButtonClicked);
  }

  // -- Final-score count-up tween -------------------------------------------

  /// <summary>
  /// Start a count-up animation on the auto-built final score TMP. Called
  /// from <see cref="ShowGameOverPanel"/> right after the panel begins fading
  /// in, so the viewer's eye is drawn to a number that "earns" itself onto
  /// the screen instead of just snapping in.
  /// </summary>
  void StartFinalScoreCountUp(int targetScore, float duration = 1.1f)
  {
    if (autoFinalScoreText == null) return;

    finalScoreTweenTarget = Mathf.Max(0, targetScore);
    finalScoreTweenDuration = Mathf.Max(0.1f, duration);
    finalScoreTweenAge = 0f;
    finalScoreTweenActive = true;
    autoFinalScoreText.text = "Final Score: 0";
  }

  void TickFinalScoreCountUp()
  {
    if (!finalScoreTweenActive || autoFinalScoreText == null) return;

    finalScoreTweenAge += Time.unscaledDeltaTime;
    float t = Mathf.Clamp01(finalScoreTweenAge / finalScoreTweenDuration);
    // EaseOutCubic — fast climb, soft landing on the final value.
    float eased = 1f - Mathf.Pow(1f - t, 3f);
    int value = Mathf.RoundToInt(finalScoreTweenTarget * eased);
    autoFinalScoreText.text = "Final Score: " + value;

    if (t >= 1f)
    {
      autoFinalScoreText.text = "Final Score: " + finalScoreTweenTarget;
      finalScoreTweenActive = false;
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
    GemCatcher.OnBonusLifeAwarded -= HandleBonusLifeAwarded;
    PowerUpManager.OnActivated -= HandlePowerUpActivated;
    PowerUpManager.OnExpired -= HandlePowerUpExpired;
    PowerUpManager.OnShieldConsumed -= HandleShieldConsumed;
    ComboManager.OnComboChanged -= HandleComboChanged;
    ComboManager.OnComboTierUp -= HandleComboTierUp;
    ComboManager.OnComboBroken -= HandleComboBroken;
    MilestoneTracker.OnMilestoneReached -= HandleMilestoneReached;
    GemCatcher.OnBombHit -= HandleBombHit;

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

    if (Instance == this) Instance = null;
  }

  // ----------------------------------------------------------------------
  // Combo HUD — top-right, just under the score. Hidden at combo == 0,
  // grows / changes color as the multiplier climbs.
  // ----------------------------------------------------------------------

  void EnsureComboDisplay()
  {
    if (comboDisplayTmp != null || hudCanvas == null || UiRoot == null) return;

    GameObject go = new GameObject("ComboDisplay (auto)", typeof(RectTransform));
    go.transform.SetParent(UiRoot, false);
    comboDisplayRoot = go.GetComponent<RectTransform>();
    comboDisplayRoot.anchorMin = new Vector2(1f, 1f);
    comboDisplayRoot.anchorMax = new Vector2(1f, 1f);
    comboDisplayRoot.pivot = new Vector2(1f, 1f);
    // Sit just below the score. Score occupies y = -40 to ~-140; combo
    // anchors at -150 so the two never overlap on portrait phones.
    comboDisplayRoot.anchoredPosition = new Vector2(-40f, -150f);
    comboDisplayRoot.sizeDelta = new Vector2(500f, 70f);

    comboDisplayTmp = go.AddComponent<TextMeshProUGUI>();
    comboDisplayTmp.alignment = TextAlignmentOptions.TopRight;
    comboDisplayTmp.fontSize = 48f;
    comboDisplayTmp.fontStyle = FontStyles.Bold;
    comboDisplayTmp.color = Color.white;
    comboDisplayTmp.text = string.Empty;
    go.SetActive(false);
  }

  void HandleComboChanged(int combo, float multiplier)
  {
    if (comboDisplayTmp == null) return;

    if (combo <= 0)
    {
      // Hidden state — combo broken or never started.
      comboDisplayRoot.gameObject.SetActive(false);
      return;
    }

    comboDisplayRoot.gameObject.SetActive(true);
    // Show "x3" only once the multiplier has actually kicked in. Below that
    // tier the player still sees their streak count climbing — useful for
    // the next-tier anticipation — but no false multiplier promise.
    if (multiplier > 1f)
    {
      comboDisplayTmp.text = $"COMBO ×{multiplier:0.#}  ({combo})";
    }
    else
    {
      comboDisplayTmp.text = $"STREAK {combo}";
    }
    comboDisplayTmp.color = ColorForMultiplier(multiplier);

    // Quick scale pop on every catch — the ticker eases it back to 1.
    comboTargetScale = 1.18f;
  }

  void HandleComboTierUp(int combo, float newMultiplier)
  {
    // Bigger pop and a banner when the multiplier itself goes up.
    comboTierUpPending = true;
    SpawnBannerNotification($"×{newMultiplier:0.#}  STREAK!", ColorForMultiplier(newMultiplier));
  }

  void HandleComboBroken(int lostCombo, float lostMultiplier)
  {
    // Only show a "lost" banner for streaks the player actually invested in
    // (multiplier was active). Tiny 1- or 2-streaks just disappear silently
    // so a normal early-round miss doesn't get framed as a "loss".
    if (lostMultiplier > 1f)
    {
      SpawnBannerNotification(
          $"STREAK LOST  ×{lostMultiplier:0.#}",
          new Color(0.85f, 0.85f, 0.85f));
    }
  }

  void TickComboDisplay()
  {
    if (comboDisplayRoot == null) return;

    // Pop on tier-up: oversize once, then ease back down.
    if (comboTierUpPending)
    {
      comboTargetScale = 1.4f;
      comboTierUpPending = false;
    }

    Vector3 current = comboDisplayRoot.localScale;
    Vector3 target = Vector3.one * comboTargetScale;
    comboDisplayRoot.localScale = Vector3.Lerp(current, target,
        Mathf.Clamp01(14f * Time.unscaledDeltaTime));

    // Ease the target itself back to 1 so the next pop has somewhere to go.
    comboTargetScale = Mathf.Lerp(comboTargetScale, 1f,
        Mathf.Clamp01(8f * Time.unscaledDeltaTime));
  }

  static Color ColorForMultiplier(float mult)
  {
    if (mult >= 5f) return new Color(1.00f, 0.30f, 0.35f); // bright red
    if (mult >= 3f) return new Color(1.00f, 0.55f, 0.25f); // orange
    if (mult >= 2f) return new Color(1.00f, 0.85f, 0.30f); // amber
    if (mult >= 1.5f) return new Color(1.00f, 1.00f, 0.55f); // pale yellow
    return Color.white;
  }

  // ----------------------------------------------------------------------
  // Milestone celebrations — full-screen banner + tinted flash + a quick
  // CatchBurst at the catcher position.
  // ----------------------------------------------------------------------

  void HandleMilestoneReached(MilestoneTracker.Milestone milestone)
  {
    Color tint = ColorForMilestoneScore(milestone.score);
    // Big banner — the existing helper handles the auto-sizing layout.
    SpawnBannerNotification(milestone.title, tint);

    // Particle pop at the catcher so the eye is drawn down to the play area.
    GameObject catcher = GameObject.FindWithTag("Catcher");
    if (catcher != null)
    {
      CatchBurst.Spawn(catcher.transform.position, tint);
    }

    // Camera shake scaled to the milestone — bigger crossings hit harder.
    float intensity = Mathf.Lerp(0.18f, 0.40f, Mathf.Clamp01(milestone.score / 10000f));
    CameraShake.Shake(intensity, 0.45f);
  }

  static Color ColorForMilestoneScore(int score)
  {
    if (score >= 10000) return new Color(1.00f, 0.30f, 0.85f); // hot pink for godmode
    if (score >= 5000) return new Color(0.55f, 0.85f, 1.00f); // ice blue legendary
    if (score >= 2500) return new Color(1.00f, 0.40f, 0.40f); // red
    if (score >= 1000) return new Color(1.00f, 0.65f, 0.20f); // orange
    return new Color(1.00f, 0.90f, 0.40f); // warm yellow
  }

  // ----------------------------------------------------------------------
  // Special-gem reactions
  // ----------------------------------------------------------------------

  void HandleBombHit(Vector3 worldPosition)
  {
    SpawnFloatingText("BOOM!  -1 \u2665", new Color(1.00f, 0.35f, 0.30f), worldPosition);
    // Heavy red burst at the impact point.
    CatchBurst.Spawn(worldPosition, new Color(1.00f, 0.25f, 0.20f));
  }

}
