using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Manages the interactive tutorial: a practice level with guided pop-ups that
/// pause gameplay, explain rules, then demo them one at a time. The player
/// cannot die during the tutorial (infinite lives). Each step has a dialogue
/// popup followed by a live demo of the mechanic being explained.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    private Canvas tutorialCanvas;
    private GameObject popupPanel;
    private TextMeshProUGUI dialogueText;
    private Button continueButton;
    private TextMeshProUGUI continueButtonText;
    private Button menuButton;

    private ObjectPooler objectPooler;
    private bool waitingForCatch = false;
    private bool waitingForMiss = false;
    private bool firstCatchHandled = false;

    // Tutorial steps
    private enum TutorialPhase
    {
        Intro,
        DemoFirstGem,
        AfterFirstCatch,
        ExplainBlinking,
        DemoSecondGem,
        ExplainGoldenGem,
        DemoGoldenGem,
        ExplainBomb,
        DemoBomb,
        ExplainPowerUps,
        DemoPowerUp,
        ExplainCombo,
        DemoCombo,
        Outro,
    }

    private TutorialPhase phase = TutorialPhase.Intro;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GameState.IsTutorial = true;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            GameState.IsTutorial = false;
        }
        // Unsubscribe
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnGemCaught -= OnGemCaught;
            RoundManager.Instance.OnGemMissed -= OnGemMissed;
            RoundManager.Instance.OnBombHit -= OnBombHit;
        }
        PowerUpManager.OnActivated -= OnPowerUpCaught;
    }

    void Start()
    {
        objectPooler = FindObjectOfType<ObjectPooler>();

        // Subscribe to catch/miss events
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnGemCaught += OnGemCaught;
            RoundManager.Instance.OnGemMissed += OnGemMissed;
            RoundManager.Instance.OnBombHit += OnBombHit;
        }
        PowerUpManager.OnActivated += OnPowerUpCaught;

        // Give infinite lives
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.ResetLives();
        }

        BuildUI();

        // Start tutorial after a short delay
        StartCoroutine(BeginTutorial());
    }

    IEnumerator BeginTutorial()
    {
        yield return new WaitForSeconds(0.5f);
        GameState.IsPlaying = false; // Pause spawning until we're ready
        ShowPhase(TutorialPhase.Intro);
    }

    void BuildUI()
    {
        // Create canvas
        GameObject canvasGo = new GameObject("TutorialCanvas");
        tutorialCanvas = canvasGo.AddComponent<Canvas>();
        tutorialCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        tutorialCanvas.sortingOrder = 200;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1080, 1920);
        canvasGo.GetComponent<CanvasScaler>().matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Popup panel — centered, semi-transparent dark background
        popupPanel = new GameObject("PopupPanel", typeof(RectTransform));
        popupPanel.transform.SetParent(canvasGo.transform, false);
        Image panelBg = popupPanel.AddComponent<Image>();
        panelBg.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        RectTransform panelRect = popupPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.05f, 0.25f);
        panelRect.anchorMax = new Vector2(0.95f, 0.75f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Dialogue text
        GameObject textGo = new GameObject("DialogueText", typeof(RectTransform));
        textGo.transform.SetParent(popupPanel.transform, false);
        dialogueText = textGo.AddComponent<TextMeshProUGUI>();
        dialogueText.alignment = TextAlignmentOptions.Center;
        dialogueText.fontSize = 42f;
        dialogueText.fontStyle = FontStyles.Normal;
        dialogueText.color = Color.white;
        dialogueText.enableWordWrapping = true;
        dialogueText.enableAutoSizing = true;
        dialogueText.fontSizeMin = 28f;
        dialogueText.fontSizeMax = 46f;
        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.05f, 0.22f);
        textRect.anchorMax = new Vector2(0.95f, 0.95f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // Continue button — crystal style matching main menu
        GameObject btnGo = new GameObject("ContinueButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(popupPanel.transform, false);
        continueButton = btnGo.GetComponent<Button>();
        continueButton.targetGraphic = btnGo.GetComponent<Image>();
        RectTransform btnRect = btnGo.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.15f, 0.03f);
        btnRect.anchorMax = new Vector2(0.85f, 0.18f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;

        GameObject btnTextGo = new GameObject("BtnText", typeof(RectTransform));
        btnTextGo.transform.SetParent(btnGo.transform, false);
        continueButtonText = btnTextGo.AddComponent<TextMeshProUGUI>();
        continueButtonText.text = "Continue";
        continueButtonText.alignment = TextAlignmentOptions.Center;
        continueButtonText.fontSize = 48f;
        continueButtonText.fontStyle = FontStyles.Bold;
        continueButtonText.color = Color.white;
        RectTransform btnTextRect = btnTextGo.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;

        CrystalButtonStyle.Apply(btnGo, new Color(0.20f, 0.60f, 0.35f));
        continueButton.onClick.AddListener(OnContinueClicked);

        // Menu button (top-left) — crystal style
        GameObject menuGo = new GameObject("MenuButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        menuGo.transform.SetParent(canvasGo.transform, false);
        menuButton = menuGo.GetComponent<Button>();
        menuButton.targetGraphic = menuGo.GetComponent<Image>();
        RectTransform menuRect = menuGo.GetComponent<RectTransform>();
        menuRect.anchorMin = new Vector2(0.03f, 0.91f);
        menuRect.anchorMax = new Vector2(0.28f, 0.96f);
        menuRect.offsetMin = Vector2.zero;
        menuRect.offsetMax = Vector2.zero;

        GameObject menuTextGo = new GameObject("MenuText", typeof(RectTransform));
        menuTextGo.transform.SetParent(menuGo.transform, false);
        TextMeshProUGUI menuTmp = menuTextGo.AddComponent<TextMeshProUGUI>();
        menuTmp.text = "Menu";
        menuTmp.alignment = TextAlignmentOptions.Center;
        menuTmp.fontSize = 38f;
        menuTmp.fontStyle = FontStyles.Bold;
        menuTmp.color = Color.white;
        RectTransform menuTextRect = menuTextGo.GetComponent<RectTransform>();
        menuTextRect.anchorMin = Vector2.zero;
        menuTextRect.anchorMax = Vector2.one;
        menuTextRect.offsetMin = Vector2.zero;
        menuTextRect.offsetMax = Vector2.zero;

        CrystalButtonStyle.Apply(menuGo, new Color(0.35f, 0.35f, 0.42f));
        menuButton.onClick.AddListener(OnMenuClicked);

        popupPanel.SetActive(false);
    }

    void ShowPopup(string message, string buttonLabel = "Continue")
    {
        dialogueText.text = message;
        continueButtonText.text = buttonLabel;
        popupPanel.SetActive(true);
        GameState.IsPlaying = false; // Pause spawning while popup is visible
        Time.timeScale = 0f; // Freeze everything
    }

    void HidePopup()
    {
        popupPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    void ShowPhase(TutorialPhase newPhase)
    {
        phase = newPhase;

        switch (phase)
        {
            case TutorialPhase.Intro:
                ShowPopup(
                    "Hi I'm <b>Catchy!</b> And I'm here to teach you the rules of <b>Gem Catching!</b>\n\n" +
                    "Out here in the wild world, we're hunting magic gems. These gems appear spontaneously " +
                    "with a bolt of energy! Our job is to catch as many as we can.\n\n" +
                    "Ultimately we want to find the <color=#FFD86A>Master Gem</color>, " +
                    "but all dreams have to start somewhere, right?\n\n" +
                    "Allow me to demonstrate what catching a gem is like!");
                break;

            case TutorialPhase.DemoFirstGem:
                HidePopup();
                GameState.IsPlaying = true;
                waitingForCatch = true;
                firstCatchHandled = false;
                // Force spawn a normal gem
                StartCoroutine(ForceSpawnGem(SpecialGemType.Normal));
                break;

            case TutorialPhase.AfterFirstCatch:
                ShowPopup(
                    "Wow, beginner's luck! I remember my first gem.\n\n" +
                    "As you can see, you only have a limited time to place me (the catcher). " +
                    "While the gem is <b>blinking</b>, you can <b>tap</b> or <b>drag</b> to reposition me.\n\n" +
                    "Once it goes solid - it's falling fast! Let's try one more.");
                break;

            case TutorialPhase.DemoSecondGem:
                HidePopup();
                GameState.IsPlaying = true;
                waitingForCatch = true;
                StartCoroutine(ForceSpawnGem(SpecialGemType.Normal));
                break;

            case TutorialPhase.ExplainGoldenGem:
                ShowPopup(
                    "Nice work! Now here's where it gets interesting.\n\n" +
                    "Sometimes a <color=#FFD86A><b>Golden Gem</b></color> appears! " +
                    "These beauties are worth <color=#FFD86A><b>100 points</b></color> " +
                    "instead of the usual 20.\n\n" +
                    "They're rare, so don't let them slip past! Watch for one now...");
                break;

            case TutorialPhase.DemoGoldenGem:
                HidePopup();
                GameState.IsPlaying = true;
                waitingForCatch = true;
                StartCoroutine(ForceSpawnGem(SpecialGemType.Golden));
                break;

            case TutorialPhase.ExplainBomb:
                ShowPopup(
                    "Excellent catch!\n\n" +
                    "But beware - not everything that falls is worth catching! " +
                    "<color=#FF5555><b>Bomb Gems</b></color> look dark and dangerous.\n\n" +
                    "<color=#FF5555><b>DON'T catch them!</b></color> Let them fall through. " +
                    "If you catch a bomb, you lose a life and your combo streak breaks.\n\n" +
                    "Let me show you one - just let it fall past!");
                break;

            case TutorialPhase.DemoBomb:
                HidePopup();
                GameState.IsPlaying = true;
                waitingForMiss = true;
                StartCoroutine(ForceSpawnBomb());
                break;

            case TutorialPhase.ExplainPowerUps:
                ShowPopup(
                    "Perfect, you let it pass!\n\n" +
                    "Now for the good stuff: <b>Power-Ups!</b>\n\n" +
                    "<color=#55CCFF>Wider Catcher</color> - makes me bigger!\n" +
                    "<color=#FFDD55>Shield</color> - protects from one miss\n" +
                    "<color=#55FF77>Double Score</color> - 2x points per catch!\n" +
                    "<color=#FF55DD>Extra Life</color> - +3 lives\n\n" +
                    "Power-ups stay active until you miss a gem (shield absorbs one miss). " +
                    "Watch for a glowing gem with a fiery aura!");
                break;

            case TutorialPhase.DemoPowerUp:
                HidePopup();
                GameState.IsPlaying = true;
                waitingForCatch = true;
                StartCoroutine(ForceSpawnPowerUpWithRetry());
                break;

            case TutorialPhase.ExplainCombo:
                ShowPopup(
                    "Power-up activated!\n\n" +
                    "One more thing: <b>Combos!</b>\n\n" +
                    "Every gem you catch in a row builds your <color=#FFD86A><b>combo streak</b></color>. " +
                    "At 3 catches your score multiplier increases:\n\n" +
                    "3 in a row = <b>x1.5</b>\n" +
                    "6 in a row = <b>x2.0</b>\n" +
                    "10+ in a row = <b>x3.0</b>\n\n" +
                    "Every 3 catches also grants a <color=#FF55DD>bonus life!</color>\n" +
                    "Missing a gem or catching a bomb breaks the combo.");
                break;

            case TutorialPhase.DemoCombo:
                // Skip demo for combo — just explain and move to outro
                ShowPhase(TutorialPhase.Outro);
                break;

            case TutorialPhase.Outro:
                ShowPopup(
                    "You're a natural! That's everything you need to know.\n\n" +
                    "- Catch gems for points\n" +
                    "- Avoid bombs\n" +
                    "- Grab power-ups\n" +
                    "- Build combos\n" +
                    "- Don't run out of lives!\n\n" +
                    "Now get out there and find that <color=#FFD86A>Master Gem!</color>\n\n" +
                    "Good luck, gem catcher!",
                    "Back to Menu");
                break;
        }
    }

    void OnContinueClicked()
    {
        switch (phase)
        {
            case TutorialPhase.Intro:
                ShowPhase(TutorialPhase.DemoFirstGem);
                break;
            case TutorialPhase.AfterFirstCatch:
                ShowPhase(TutorialPhase.DemoSecondGem);
                break;
            case TutorialPhase.ExplainGoldenGem:
                ShowPhase(TutorialPhase.DemoGoldenGem);
                break;
            case TutorialPhase.ExplainBomb:
                ShowPhase(TutorialPhase.DemoBomb);
                break;
            case TutorialPhase.ExplainPowerUps:
                ShowPhase(TutorialPhase.DemoPowerUp);
                break;
            case TutorialPhase.ExplainCombo:
                ShowPhase(TutorialPhase.DemoCombo);
                break;
            case TutorialPhase.Outro:
                OnMenuClicked();
                break;
            default:
                // During demo phases, continue just advances to next explanation
                AdvanceAfterDemo();
                break;
        }
    }

    void AdvanceAfterDemo()
    {
        // Fallback — shouldn't normally be called during demo phases
        switch (phase)
        {
            case TutorialPhase.DemoFirstGem:
                ShowPhase(TutorialPhase.AfterFirstCatch);
                break;
            case TutorialPhase.DemoSecondGem:
                ShowPhase(TutorialPhase.ExplainGoldenGem);
                break;
            case TutorialPhase.DemoGoldenGem:
                ShowPhase(TutorialPhase.ExplainBomb);
                break;
            case TutorialPhase.DemoBomb:
                ShowPhase(TutorialPhase.ExplainPowerUps);
                break;
            case TutorialPhase.DemoPowerUp:
                ShowPhase(TutorialPhase.ExplainCombo);
                break;
        }
    }

    void OnMenuClicked()
    {
        Time.timeScale = 1f;
        GameState.IsTutorial = false;
        GameState.IsPlaying = false;
        GameState.SkipMainMenuOnLoad = false;
        SceneManager.LoadScene("SampleScene");
    }

    // ---- Event handlers ----

    void OnGemCaught(int amount, Vector3 pos)
    {
        if (!waitingForCatch) return;
        waitingForCatch = false;

        // Give lives back in case anything was deducted
        if (RoundManager.Instance != null && RoundManager.Instance.Lives < 99)
            RoundManager.Instance.ResetLives();

        StartCoroutine(DelayedAdvance(0.6f));
    }

    void OnGemMissed(int amount, Vector3 pos)
    {
        if (waitingForMiss)
        {
            waitingForMiss = false;
            // Restore lives — can't die in tutorial
            if (RoundManager.Instance != null)
                RoundManager.Instance.ResetLives();
            StartCoroutine(DelayedAdvance(0.6f));
            return;
        }

        // If they missed during a catch demo, just respawn
        if (waitingForCatch)
        {
            // Restore lives
            if (RoundManager.Instance != null)
                RoundManager.Instance.ResetLives();
            // Respawn
            StartCoroutine(RespawnAfterMiss());
        }
    }

    void OnBombHit(Vector3 pos)
    {
        // If they accidentally caught the bomb, restore lives and respawn
        if (waitingForMiss)
        {
            if (RoundManager.Instance != null)
                RoundManager.Instance.ResetLives();
            waitingForMiss = false;
            StartCoroutine(ShowBombCaughtMessage());
        }
    }

    void OnPowerUpCaught(PowerUpType type, float duration)
    {
        if (!waitingForCatch) return;
        waitingForCatch = false;

        if (RoundManager.Instance != null)
            RoundManager.Instance.ResetLives();

        StartCoroutine(DelayedAdvance(0.6f));
    }

    IEnumerator ShowBombCaughtMessage()
    {
        yield return new WaitForSeconds(0.3f);
        ShowPopup(
            "Oops! You caught the bomb! Remember - <color=#FF5555><b>let bombs fall through!</b></color>\n\n" +
            "Don't worry, no harm done in practice. Let's try again!");
        // Override continue to re-demo
        phase = TutorialPhase.ExplainBomb;
    }

    IEnumerator RespawnAfterMiss()
    {
        yield return new WaitForSeconds(1.0f);
        if (!waitingForCatch) yield break;
        if (phase == TutorialPhase.DemoFirstGem || phase == TutorialPhase.DemoSecondGem)
            StartCoroutine(ForceSpawnGem(SpecialGemType.Normal));
        else if (phase == TutorialPhase.DemoGoldenGem)
            StartCoroutine(ForceSpawnGem(SpecialGemType.Golden));
    }

    IEnumerator DelayedAdvance(float delay)
    {
        yield return new WaitForSeconds(delay);
        AdvanceAfterDemo();
    }

    IEnumerator ForceSpawnGem(SpecialGemType type)
    {
        yield return new WaitForSeconds(0.5f);

        if (objectPooler == null) yield break;

        // Use reflection or the public spawn API — trigger a manual spawn
        objectPooler.TutorialSpawnGem(type);
    }

    IEnumerator ForceSpawnPowerUp()
    {
        yield return new WaitForSeconds(0.5f);

        if (objectPooler == null) yield break;

        objectPooler.TutorialSpawnPowerUp(PowerUpType.WiderCatcher);
    }

    /// <summary>
    /// Spawns a power-up and keeps respawning if the player misses it
    /// (power-up misses are silent — no OnGemMissed event).
    /// </summary>
    IEnumerator ForceSpawnPowerUpWithRetry()
    {
        while (waitingForCatch)
        {
            yield return new WaitForSeconds(0.5f);
            if (!waitingForCatch) yield break;

            if (objectPooler == null) yield break;
            objectPooler.TutorialSpawnPowerUp(PowerUpType.WiderCatcher);

            // Wait for the gem to resolve (caught or fell through)
            yield return new WaitForSeconds(0.3f);
            GameObject gem = objectPooler.CurrentActiveGem;
            while (gem != null && gem.activeInHierarchy)
            {
                yield return null;
                if (!waitingForCatch) yield break; // Caught via OnGemCaught
            }

            // If still waiting, it was missed silently — loop and respawn
            if (waitingForCatch)
            {
                if (RoundManager.Instance != null)
                    RoundManager.Instance.ResetLives();
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    IEnumerator ForceSpawnBomb()
    {
        yield return new WaitForSeconds(0.5f);

        if (objectPooler == null) yield break;

        objectPooler.TutorialSpawnGem(SpecialGemType.Bomb);

        // Bombs are silently retired (no OnGemMissed event), so we poll
        // for the gem becoming inactive to advance the tutorial.
        yield return new WaitForSeconds(0.3f);
        GameObject gem = objectPooler.CurrentActiveGem;
        while (gem != null && gem.activeInHierarchy)
        {
            yield return null;
        }

        // Bomb fell through or was caught (OnBombHit handles caught case)
        if (waitingForMiss)
        {
            waitingForMiss = false;
            if (RoundManager.Instance != null)
                RoundManager.Instance.ResetLives();
            yield return new WaitForSeconds(0.5f);
            AdvanceAfterDemo();
        }
    }
}
