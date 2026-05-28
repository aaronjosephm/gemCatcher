using System.Collections.Generic;
using UnityEngine;

public class GemCatcher : MonoBehaviour
{
    private Transform catcher; // Reference to the catcher (Box)
    private BoxCollider catcherCollider; // Catcher's BoxCollider
    private SphereCollider gemCollider; // Gem's SphereCollider

    private Vector3 catcherSize;
    private Vector3 catcherCenter;

    // Scoring rules.
    public const int POINTS_PER_CATCH = 20;
    public const int POINTS_PER_MISS = -10;

    // Lives rules.
    public const int STARTING_LIVES = 3;
    // Players earn one extra life every time their score crosses a multiple of this value.
    public const int POINTS_PER_BONUS_LIFE = 100;

    // Static score & lives tracking.
    public static int Score { get; private set; }
    public static int Lives { get; private set; } = STARTING_LIVES;
    public static bool IsGameOver { get; private set; }

    // Per-gem catch counts. Keyed by the prefab name (with the "(Clone)" suffix stripped).
    // Read by UIManager when the game over panel is shown.
    public static Dictionary<string, int> CatchesByGemName { get; private set; }
        = new Dictionary<string, int>();

    // Event for score changes (any source).
    public delegate void ScoreChangedDelegate(int newScore);
    public static event ScoreChangedDelegate OnScoreChanged;

    // Event for life-count changes.
    public static event ScoreChangedDelegate OnLivesChanged;

    // Fired exactly once when the player runs out of lives.
    public static event System.Action OnGameOver;

    // Event fired when a gem is caught. Subscribers (e.g. UIManager) use this to spawn
    // the floating "+20" pop-up at the catch location.
    public delegate void GemCaughtDelegate(int amount, Vector3 worldPosition);
    public static event GemCaughtDelegate OnGemCaught;

    // Event fired when a gem falls off-screen uncaught. Same shape as OnGemCaught so
    // listeners can render a matching "-10" pop-up at the miss location.
    public static event GemCaughtDelegate OnGemMissed;

    // Internal helper so non-catcher code (e.g. FallingObject's bottom-boundary check)
    // can report a miss without needing to know about both the lives update and the event.
    // Misses cost a life but no longer deduct points; the event amount is left at the
    // POINTS_PER_MISS constant for any listener that wants the conventional value.
    public static void ReportGemMissed(Vector3 worldPosition)
    {
        if (IsGameOver) return;

        OnGemMissed?.Invoke(POINTS_PER_MISS, worldPosition);
        ChangeLives(-1);
    }

    // Records a successful catch of the given gem name (used to populate the game-over breakdown).
    public static void RecordCatch(string gemName)
    {
        if (string.IsNullOrEmpty(gemName)) return;
        CatchesByGemName.TryGetValue(gemName, out int count);
        CatchesByGemName[gemName] = count + 1;
    }

    // Add (or subtract) points. Score is clamped at 0; misses never push it negative.
    // Awards bonus lives whenever the score crosses a POINTS_PER_BONUS_LIFE threshold.
    public static void AddScore(int delta)
    {
        if (IsGameOver) return;

        int previousScore = Score;
        Score = Mathf.Max(0, Score + delta);
        OnScoreChanged?.Invoke(Score);

        if (delta > 0)
        {
            int previousTier = previousScore / POINTS_PER_BONUS_LIFE;
            int newTier = Score / POINTS_PER_BONUS_LIFE;
            if (newTier > previousTier)
            {
                ChangeLives(newTier - previousTier);
            }
        }
    }

    private static void ChangeLives(int delta)
    {
        int previousLives = Lives;
        Lives = Mathf.Max(0, Lives + delta);
        OnLivesChanged?.Invoke(Lives);

        if (Lives <= 0 && previousLives > 0 && !IsGameOver)
        {
            IsGameOver = true;
            OnGameOver?.Invoke();
        }
    }

    public static void ResetScore()
    {
        Score = 0;
        CatchesByGemName.Clear();
        OnScoreChanged?.Invoke(Score);
    }

    public static void ResetLives()
    {
        Lives = STARTING_LIVES;
        IsGameOver = false;
        OnLivesChanged?.Invoke(Lives);
    }

    // Reset static state when entering Play Mode. Required because Unity's "Domain Reload"
    // option may be disabled, leaving statics dirty across play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Score = 0;
        Lives = STARTING_LIVES;
        IsGameOver = false;
        if (CatchesByGemName == null) CatchesByGemName = new Dictionary<string, int>();
        else CatchesByGemName.Clear();
        OnScoreChanged = null;
        OnLivesChanged = null;
        OnGameOver = null;
        OnGemCaught = null;
        OnGemMissed = null;
    }

    void Start()
    {
        // Cache the gem's collider if it exists
        gemCollider = GetComponent<SphereCollider>();

        // Find the catcher at start
        FindCatcher();
    }

    void Update()
    {
        // Check if the gem crosses the catcher's boundaries
        if (IsGemWithinCatcherBounds())
        {
            Vector3 catchPosition = transform.position;

            AddScore(POINTS_PER_CATCH);
            OnGemCaught?.Invoke(POINTS_PER_CATCH, catchPosition);

            // "(Clone)" is appended by Instantiate; strip it for nicer display in the
            // game-over breakdown.
            string gemName = gameObject.name.Replace("(Clone)", "").Trim();
            RecordCatch(gemName);

            PlayCatchEffect();

            // Deactivate the gem
            gameObject.SetActive(false);
        }
    }

    void PlayCatchEffect()
    {
#if UNITY_EDITOR
        Debug.Log($"Gem caught! Score: {Score}");
#endif
    }

    void FindCatcher()
    {
        GameObject catcherRef = GameObject.FindWithTag("Catcher");
        if (catcherRef)
        {
            catcher = catcherRef.transform;
            catcherCollider = catcher.GetComponent<BoxCollider>();
            UpdateCatcherBounds();
        }
    }

    void UpdateCatcherBounds()
    {
        if (catcher != null && catcherCollider != null)
        {
            // Scale by lossyScale so parent transforms are respected, and convert the local-space
            // collider center into world space rather than naively adding it to position.
            catcherSize = Vector3.Scale(catcherCollider.size, catcher.lossyScale);
            catcherCenter = catcher.TransformPoint(catcherCollider.center);
        }
    }

    bool IsGemWithinCatcherBounds()
    {
        // If we don't have a catcher reference, try to find it
        if (catcher == null)
        {
            FindCatcher();
            if (catcher == null) return false;
        }

        // Update the catcher bounds in case it moved
        UpdateCatcherBounds();

        // Get the gem's current position
        Vector3 gemPosition = transform.position;

        // Get the gem's radius if it has a SphereCollider
        float gemRadius = gemCollider != null ? gemCollider.radius * transform.localScale.x : 0.1f;

        // Check if the gem's position is within the catcher's bounds
        bool isWithinHorizontalBounds = Mathf.Abs(gemPosition.x - catcherCenter.x) <= (catcherSize.x / 2 + gemRadius);
        bool isWithinVerticalBounds = Mathf.Abs(gemPosition.y - catcherCenter.y) <= (catcherSize.y / 2 + gemRadius);

        // Optionally check for the z-axis if you're working in 3D
        bool isWithinDepthBounds = Mathf.Abs(gemPosition.z - catcherCenter.z) <= (catcherSize.z / 2 + gemRadius);

        // Return true if the gem is within all bounds
        return isWithinHorizontalBounds && isWithinVerticalBounds && isWithinDepthBounds;
    }
}
