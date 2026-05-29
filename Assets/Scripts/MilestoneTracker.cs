using UnityEngine;

/// <summary>
/// Watches the player's score and fires "milestone reached" events at the
/// thresholds below. Each milestone shows a celebratory banner AND grants a
/// gameplay reward (a free power-up, a life, etc.). Static; auto-bootstraps
/// from RuntimeInitialize so it works regardless of scene setup.
///
/// Reward rules:
///   - Power-up rewards activate the same flag as catching a pickup —
///     persistent until the next unshielded miss, just like normal pickups.
///   - Life rewards bypass the per-100 throttle and stack up to a hard cap
///     enforced by GemCatcher.AddLives.
///   - Daily mode skips life rewards (the daily challenge intentionally locks
///     the player at 3 lives), but milestone banners + power-up gifts still
///     fire so daily players still get the celebration.
/// </summary>
public static class MilestoneTracker
{
  /// <summary>One milestone definition.</summary>
  public struct Milestone
  {
    public int score;
    public string title;
    /// <summary>Power-ups to auto-activate when this milestone is reached. Use null/empty for no power-up reward.</summary>
    public PowerUpType[] powerUpRewards;
    /// <summary>Lives to gift on this milestone. 0 = none.</summary>
    public int bonusLives;
  }

  // Tuned so each milestone feels like a "graduation". The big one at 10k
  // grants every remaining power-up at once — huge dopamine hit for the whales.
  private static readonly Milestone[] milestones = new Milestone[]
  {
    new Milestone {
      score = 500,
      title = "HEATING UP!",
      powerUpRewards = new[] { PowerUpType.Shield },
      bonusLives = 0,
    },
    new Milestone {
      score = 1000,
      title = "ON FIRE!",
      powerUpRewards = new[] { PowerUpType.WiderCatcher },
      bonusLives = 1,
    },
    new Milestone {
      score = 2500,
      title = "UNSTOPPABLE!",
      powerUpRewards = new[] { PowerUpType.DoubleScore },
      bonusLives = 1,
    },
    new Milestone {
      score = 5000,
      title = "LEGENDARY!",
      powerUpRewards = new[] { PowerUpType.WiderCatcher, PowerUpType.Shield },
      bonusLives = 1,
    },
    new Milestone {
      score = 10000,
      title = "GODMODE!",
      powerUpRewards = new[] {
        PowerUpType.WiderCatcher,
        PowerUpType.DoubleScore,
        PowerUpType.Shield,
      },
      bonusLives = 2,
    },
  };

  // Highest milestone INDEX awarded this round. -1 means none yet. Reset by
  // ResetForNewRound at round start. We don't track by score directly because
  // future tunings might insert/reorder thresholds.
  private static int highestAwardedIndex = -1;

  /// <summary>
  /// Fires when a milestone is reached. Subscribers (UIManager) show a
  /// celebratory banner + screen flash. The reward (power-up activation, life
  /// gift) is applied BEFORE this event fires so the UI sees the new state.
  /// </summary>
  public static event System.Action<Milestone> OnMilestoneReached;

  /// <summary>
  /// Reset all "highest reached" bookkeeping. Called by ObjectPooler at the
  /// start of each new round.
  /// </summary>
  public static void ResetForNewRound()
  {
    highestAwardedIndex = -1;
  }

  // Score-change handler. Walks the milestone list once and awards every
  // unawarded milestone whose threshold has been crossed by the new score.
  // (One AddScore call could cross multiple thresholds, e.g. a Golden gem
  // with a 5× combo netting +1000 in a single delta.)
  private static void HandleScoreChanged(int newScore)
  {
    for (int i = 0; i < milestones.Length; i++)
    {
      if (i <= highestAwardedIndex) continue;
      if (newScore >= milestones[i].score)
      {
        highestAwardedIndex = i;
        ApplyReward(milestones[i]);
        OnMilestoneReached?.Invoke(milestones[i]);
      }
    }
  }

  private static void ApplyReward(Milestone m)
  {
    if (m.powerUpRewards != null)
    {
      foreach (PowerUpType type in m.powerUpRewards)
      {
        PowerUpManager.Activate(type);
      }
    }

    // Daily mode locks lives so milestone life-gifts are skipped.
    if (m.bonusLives > 0 && GameState.Mode == GameState.GameMode.Normal)
    {
      GemCatcher.AddLives(m.bonusLives);
    }
  }

  // ---- Bootstrap ---------------------------------------------------------

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStaticState()
  {
    highestAwardedIndex = -1;
    OnMilestoneReached = null;
  }

  // Subscribe AFTER scene load so GemCatcher's events have been initialized
  // (well, they're static so always present, but this ordering also matches
  // PowerUpManager and reads cleaner).
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void Subscribe()
  {
    GemCatcher.OnScoreChanged -= HandleScoreChanged; // dedupe across reloads
    GemCatcher.OnScoreChanged += HandleScoreChanged;
  }
}
