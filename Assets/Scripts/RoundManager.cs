using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized round state and gameplay events. Singleton MonoBehaviour that
/// owns score, lives, game-over flag, and per-gem catch counts for the current
/// round. All gameplay systems subscribe to events here rather than coupling to
/// static fields scattered across multiple classes.
///
/// <para>Auto-bootstrapped on scene load (no manual scene setup required).
/// Survives scene reloads via DontDestroyOnLoad.</para>
///
/// <para>Instance-based design means the state can be swapped or mocked in
/// tests without relying on static resets.</para>
/// </summary>
public class RoundManager : MonoBehaviour
{
    // ---- Scoring rules (constants) -----------------------------------------

    public const int POINTS_PER_CATCH = 20;
    public const int POINTS_PER_MISS = -10;
    public const int POINTS_PER_GOLDEN_CATCH = 100;
    public const int POINTS_PER_GOLD_BAR_CATCH = 500;

    // ---- Lives rules -------------------------------------------------------

    public const int STARTING_LIVES = 3;
    public const int MAX_LIVES = 10;

    // ---- Singleton ---------------------------------------------------------

    public static RoundManager Instance { get; private set; }

    // ---- Round state (instance-based) --------------------------------------

    public int Score { get; private set; }
    public int Lives { get; private set; } = STARTING_LIVES;
    public bool IsGameOver { get; private set; }

    /// <summary>
    /// Per-gem catch counts keyed by prefab name (with "(Clone)" stripped).
    /// Read by UIManager when the game-over panel is shown.
    /// </summary>
    public Dictionary<string, int> CatchesByGemName { get; private set; }
        = new Dictionary<string, int>();

    // ---- Events ------------------------------------------------------------

    public event System.Action<int> OnScoreChanged;

    public event System.Action<int> OnLivesChanged;

    /// <summary>Fired exactly once when the player runs out of lives or EndGame is called.</summary>
    public event System.Action OnGameOver;

    /// <summary>Fired when a gem is caught. Subscribers use this to spawn floating score pop-ups.</summary>
    public delegate void GemCaughtDelegate(int amount, Vector3 worldPosition);
    public event GemCaughtDelegate OnGemCaught;

    /// <summary>Fired when a gem falls off-screen uncaught.</summary>
    public event GemCaughtDelegate OnGemMissed;

    /// <summary>
    /// Fired when the player crosses bonus-life thresholds. count = lives actually awarded.
    /// </summary>
    public event System.Action<int> OnBonusLifeAwarded;

    /// <summary>Fired when the player catches a Bomb gem (shield didn't absorb it).</summary>
    public event System.Action<Vector3> OnBombHit;

    /// <summary>Fired when the player catches a Gold Bar (+500 jackpot).</summary>
    public event System.Action<Vector3> OnGoldBarCaught;

    // ---- Public API --------------------------------------------------------

    /// <summary>
    /// Report a gem missed (fell off-screen). Shield absorbs if available;
    /// otherwise costs a life, revokes power-ups, and breaks combo.
    /// </summary>
    public void ReportGemMissed(Vector3 worldPosition)
    {
        if (IsGameOver) return;

        if (PowerUpManager.TryConsumeShield(worldPosition))
        {
            return;
        }

        PowerUpManager.RevokeAllOnMiss();
        ComboManager.Break();

        OnGemMissed?.Invoke(POINTS_PER_MISS, worldPosition);
        CameraShake.Shake(0.12f, 0.22f);
        ChangeLives(-1);
    }

    /// <summary>
    /// Award N free lives (from combo bonus or ExtraLife power-up).
    /// Caps at MAX_LIVES and reports the count actually granted.
    /// </summary>
    public void AddLives(int count)
    {
        if (IsGameOver || count <= 0) return;
        int room = Mathf.Max(0, MAX_LIVES - Lives);
        int actual = Mathf.Min(count, room);
        if (actual <= 0) return;
        ChangeLives(actual);
        OnBonusLifeAwarded?.Invoke(actual);
    }

    /// <summary>Records a successful catch of the given gem name for the game-over breakdown.</summary>
    public void RecordCatch(string gemName)
    {
        if (string.IsNullOrEmpty(gemName)) return;
        CatchesByGemName.TryGetValue(gemName, out int count);
        CatchesByGemName[gemName] = count + 1;
    }

    /// <summary>Add (or subtract) points. Score is clamped at 0.</summary>
    public void AddScore(int delta)
    {
        if (IsGameOver) return;
        Score = Mathf.Max(0, Score + delta);
        OnScoreChanged?.Invoke(Score);
    }

    /// <summary>
    /// Force the round to end without losing all lives. Used by the Daily
    /// Challenge when the gem cap is reached. Idempotent.
    /// </summary>
    public void EndGame()
    {
        if (IsGameOver) return;
        IsGameOver = true;
        OnGameOver?.Invoke();
    }

    /// <summary>Invoke the OnGemCaught event (called by CatchZone after scoring).</summary>
    public void NotifyGemCaught(int amount, Vector3 worldPosition)
    {
        OnGemCaught?.Invoke(amount, worldPosition);
    }

    /// <summary>Invoke the OnBombHit event (called by CatchZone).</summary>
    public void NotifyBombHit(Vector3 worldPosition)
    {
        OnBombHit?.Invoke(worldPosition);
    }

    /// <summary>Invoke the OnGoldBarCaught event (called by CatchZone).</summary>
    public void NotifyGoldBarCaught(Vector3 worldPosition)
    {
        OnGoldBarCaught?.Invoke(worldPosition);
    }

    /// <summary>
    /// Deduct a single life directly. Used by CatchZone for bomb hits where
    /// power-ups have already been revoked separately.
    /// </summary>
    public void DeductLife()
    {
        if (IsGameOver) return;
        ChangeLives(-1);
    }

    public void ResetScore()
    {
        Score = 0;
        CatchesByGemName.Clear();
        OnScoreChanged?.Invoke(Score);
    }

    public void ResetLives()
    {
        Lives = STARTING_LIVES;
        IsGameOver = false;
        OnLivesChanged?.Invoke(Lives);
    }

    // ---- Private -----------------------------------------------------------

    private void ChangeLives(int delta)
    {
        int previousLives = Lives;
        Lives = Mathf.Clamp(Lives + delta, 0, MAX_LIVES);
        OnLivesChanged?.Invoke(Lives);

        if (Lives <= 0 && previousLives > 0 && !IsGameOver)
        {
            IsGameOver = true;
            CameraShake.Shake(0.35f, 0.55f);
            OnGameOver?.Invoke();
        }
    }

    // ---- Bootstrap ---------------------------------------------------------

    private static bool applicationQuitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        applicationQuitting = false;
    }

    // Runs at AfterSceneLoad like other managers (PowerUpManager, SoundManager).
    // Early subscribers are handled by the GemCatcher facade's lazy RM accessor.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureInstance()
    {
        if (applicationQuitting) return;
        if (Instance != null) return;
        if (FindObjectOfType<RoundManager>() != null) return;

        GameObject go = new GameObject("RoundManager (auto)");
        go.AddComponent<RoundManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Fresh state on first bootstrap
        Score = 0;
        Lives = STARTING_LIVES;
        IsGameOver = false;
        if (CatchesByGemName == null) CatchesByGemName = new Dictionary<string, int>();
        else CatchesByGemName.Clear();
    }

    void OnApplicationQuit()
    {
        applicationQuitting = true;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
