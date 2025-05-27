using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
  [Header("UI Elements")]
  public Text scoreText;
  public Text highScoreText;
  public GameObject gameOverPanel;
  public Text finalScoreText;
  public Button restartButton;
  public Text placementTimerText; // Text at the bottom for catcher placement timer
  public Text gemSpeedupTimerText; // New text at the top for gem speedup timer

  [Header("Game Settings")]
  public int scoreToWin = 100;
  public float gameOverDelay = 1.0f;

  [Header("Timer Text Settings")]
  public int timerFontSize = 36; // Large font size for the timer
  public Color timerTextColor = Color.white; // White color for better visibility

  private int highScore = 0;
  private bool gameIsOver = false;
  private Canvas mainCanvas;

  void Start()
  {
    // Find the main canvas
    mainCanvas = FindObjectOfType<Canvas>();
    if (mainCanvas == null)
    {
      Debug.LogError("No Canvas found in the scene!");
    }

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

    // Add listener to restart button
    if (restartButton != null)
    {
      restartButton.onClick.AddListener(RestartGame);
    }

    // Create the gem speedup timer text if it doesn't exist
    CreateGemSpeedupTimer();
  }

  void CreateGemSpeedupTimer()
  {
    // If we already have a reference to the timer text, just configure it
    if (gemSpeedupTimerText != null)
    {
      ConfigureTimerText();
      return;
    }

    // If we don't have a reference and there's no canvas, we can't create the text
    if (mainCanvas == null)
    {
      Debug.LogError("Cannot create timer text: No Canvas found!");
      return;
    }

    // Create a new GameObject for the timer
    GameObject timerObj = new GameObject("GemSpeedupTimer");
    timerObj.transform.SetParent(mainCanvas.transform, false);

    // Add a RectTransform component
    RectTransform rectTransform = timerObj.AddComponent<RectTransform>();
    rectTransform.anchorMin = new Vector2(0.5f, 1f);
    rectTransform.anchorMax = new Vector2(0.5f, 1f);
    rectTransform.pivot = new Vector2(0.5f, 1f);
    rectTransform.anchoredPosition = new Vector2(0, -50); // 50 pixels down from the top
    rectTransform.sizeDelta = new Vector2(400, 50);

    // Add a Text component
    gemSpeedupTimerText = timerObj.AddComponent<Text>();

    // Configure the text
    ConfigureTimerText();

    Debug.Log("Created new GemSpeedupTimer text object");
  }

  void ConfigureTimerText()
  {
    if (gemSpeedupTimerText != null)
    {
      // Set font size and color
      gemSpeedupTimerText.fontSize = timerFontSize;
      gemSpeedupTimerText.color = timerTextColor;
      gemSpeedupTimerText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
      gemSpeedupTimerText.alignment = TextAnchor.MiddleCenter;
      gemSpeedupTimerText.text = "TIMER TEXT TEST";

      // Make sure it has an outline for better visibility
      Outline outline = gemSpeedupTimerText.GetComponent<Outline>();
      if (outline == null)
      {
        outline = gemSpeedupTimerText.gameObject.AddComponent<Outline>();
      }
      outline.effectColor = Color.black;
      outline.effectDistance = new Vector2(2, 2);

      // Make sure it's active
      gemSpeedupTimerText.gameObject.SetActive(true);

      Debug.Log("Timer text configured: " + gemSpeedupTimerText.text);
    }
    else
    {
      Debug.LogError("gemSpeedupTimerText is null in ConfigureTimerText");
    }
  }

  void UpdateScore(int newScore)
  {
    // Update score text
    if (scoreText != null)
    {
      scoreText.text = "Score: " + newScore;
    }

    // Check for win condition
    if (newScore >= scoreToWin && !gameIsOver)
    {
      GameWon();
    }

    // Check for new high score
    if (newScore > highScore)
    {
      highScore = newScore;
      PlayerPrefs.SetInt("HighScore", highScore);
      UpdateHighScoreText();
    }
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

  void GameWon()
  {
    if (!gameIsOver)
    {
      gameIsOver = true;

      // Show game over panel with win message
      Invoke("ShowWinPanel", gameOverDelay);
    }
  }

  void ShowGameOverPanel()
  {
    if (gameOverPanel != null)
    {
      gameOverPanel.SetActive(true);

      // Update final score text
      if (finalScoreText != null)
      {
        finalScoreText.text = "Final Score: " + GemCatcher.Score;
      }
    }
  }

  void ShowWinPanel()
  {
    if (gameOverPanel != null)
    {
      gameOverPanel.SetActive(true);

      // Update final score text with win message
      if (finalScoreText != null)
      {
        finalScoreText.text = "You Win!\nFinal Score: " + GemCatcher.Score;
      }
    }
  }

  void RestartGame()
  {
    // Reset static score using the public method
    GemCatcher.ResetScore();

    // Reload the current scene
    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
  }

  // Called by ObjectPooler when a new placement phase starts
  void OnPlacementPhaseStarted(float duration)
  {
    // If the timer text doesn't exist, create it
    if (gemSpeedupTimerText == null)
    {
      CreateGemSpeedupTimer();
    }

    // Show the gem speedup timer at the top of the screen
    if (gemSpeedupTimerText != null)
    {
      gemSpeedupTimerText.gameObject.SetActive(true);
      gemSpeedupTimerText.text = "GEM SPEED UP IN: " + duration.ToString("F1") + "s";

      Debug.Log("Placement phase started, timer activated: " + gemSpeedupTimerText.text);
    }
    else
    {
      Debug.LogError("gemSpeedupTimerText is still null in OnPlacementPhaseStarted");
    }
  }

  // Called by ObjectPooler when the placement timer is updated
  void OnPlacementTimerUpdated(float remainingTime)
  {
    // Update the gem speedup timer text
    if (gemSpeedupTimerText != null && gemSpeedupTimerText.gameObject.activeInHierarchy)
    {
      gemSpeedupTimerText.text = "GEM SPEED UP IN: " + remainingTime.ToString("F1") + "s";
    }
  }

  // Called by ObjectPooler when the placement phase ends
  void OnPlacementPhaseEnded()
  {
    // Hide the gem speedup timer
    if (gemSpeedupTimerText != null)
    {
      gemSpeedupTimerText.gameObject.SetActive(false);
      Debug.Log("Placement phase ended, timer hidden");
    }
  }

  void OnDestroy()
  {
    // Unsubscribe from events
    GemCatcher.OnScoreChanged -= UpdateScore;

    // Remove button listener
    if (restartButton != null)
    {
      restartButton.onClick.RemoveListener(RestartGame);
    }
  }
}
