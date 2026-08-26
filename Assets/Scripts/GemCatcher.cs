using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Legacy facade — delegates all state and events to <see cref="RoundManager"/>.
/// Kept so existing prefabs that still have this component don't throw missing-script
/// errors. No per-frame work; catch detection is now handled by <see cref="CatchZone"/>
/// on the catcher via OnTriggerEnter.
///
/// All static accessors route through RoundManager.Instance.  New code should reference
/// RoundManager directly.
/// </summary>
public class GemCatcher : MonoBehaviour
{
    // ---- Constants (kept for backward compat with any code referencing GemCatcher.POINTS_*) ----
    public const int POINTS_PER_CATCH = RoundManager.POINTS_PER_CATCH;
    public const int POINTS_PER_MISS = RoundManager.POINTS_PER_MISS;
    public const int POINTS_PER_GOLDEN_CATCH = RoundManager.POINTS_PER_GOLDEN_CATCH;
    public const int STARTING_LIVES = RoundManager.STARTING_LIVES;
    public const int MAX_LIVES = RoundManager.MAX_LIVES;

    // ---- Static property delegates -----------------------------------------

    public static int Score => RoundManager.Instance != null ? RoundManager.Instance.Score : 0;
    public static int Lives => RoundManager.Instance != null ? RoundManager.Instance.Lives : STARTING_LIVES;
    public static bool IsGameOver => RoundManager.Instance != null && RoundManager.Instance.IsGameOver;
    public static Dictionary<string, int> CatchesByGemName =>
        RoundManager.Instance != null ? RoundManager.Instance.CatchesByGemName : new Dictionary<string, int>();

    // ---- Event delegates (subscribers attach here, forwarded to RoundManager) ----
    // Named delegate types retained for backward compat — all match System.Action<int>
    // or the GemCaughtDelegate(int, Vector3) shape used by RoundManager.

    private static RoundManager RM
    {
        get
        {
            if (RoundManager.Instance == null) RoundManager.EnsureInstance();
            return RoundManager.Instance;
        }
    }

    public static event System.Action<int> OnScoreChanged
    {
        add { if (RM != null) RM.OnScoreChanged += value; }
        remove { if (RM != null) RM.OnScoreChanged -= value; }
    }

    public static event System.Action<int> OnLivesChanged
    {
        add { if (RM != null) RM.OnLivesChanged += value; }
        remove { if (RM != null) RM.OnLivesChanged -= value; }
    }

    public static event System.Action OnGameOver
    {
        add { if (RM != null) RM.OnGameOver += value; }
        remove { if (RM != null) RM.OnGameOver -= value; }
    }

    public static event System.Action OnGameWon
    {
        add { if (RM != null) RM.OnGameWon += value; }
        remove { if (RM != null) RM.OnGameWon -= value; }
    }

    public static event RoundManager.GemCaughtDelegate OnGemCaught
    {
        add { if (RM != null) RM.OnGemCaught += value; }
        remove { if (RM != null) RM.OnGemCaught -= value; }
    }

    public static event RoundManager.GemCaughtDelegate OnGemMissed
    {
        add { if (RM != null) RM.OnGemMissed += value; }
        remove { if (RM != null) RM.OnGemMissed -= value; }
    }

    public static event System.Action<int> OnBonusLifeAwarded
    {
        add { if (RM != null) RM.OnBonusLifeAwarded += value; }
        remove { if (RM != null) RM.OnBonusLifeAwarded -= value; }
    }

    public static event System.Action<Vector3> OnBombHit
    {
        add { if (RM != null) RM.OnBombHit += value; }
        remove { if (RM != null) RM.OnBombHit -= value; }
    }

    // ---- Static method delegates -------------------------------------------

    public static void ReportGemMissed(Vector3 worldPosition)
    {
        if (RoundManager.Instance != null) RoundManager.Instance.ReportGemMissed(worldPosition);
    }

    public static void AddLives(int count)
    {
        if (RoundManager.Instance != null) RoundManager.Instance.AddLives(count);
    }

    public static void RecordCatch(string gemName)
    {
        if (RoundManager.Instance != null) RoundManager.Instance.RecordCatch(gemName);
    }

    public static void AddScore(int delta)
    {
        if (RoundManager.Instance != null) RoundManager.Instance.AddScore(delta);
    }

    public static void EndGame()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.EndGame();
    }

    public static void ResetScore()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.ResetScore();
    }

    public static void ResetLives()
    {
        if (RoundManager.Instance != null) RoundManager.Instance.ResetLives();
    }
}
