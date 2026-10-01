using UnityEngine;

/// <summary>
/// Applies legacy score-threshold rewards in non-Rush modes.
/// Score-rank banners and their audiovisual celebrations have been removed.
/// </summary>
public static class MilestoneTracker
{
  /// <summary>One milestone definition.</summary>
  public struct Milestone
  {
    public int score;
    /// <summary>Power-ups to auto-activate when this milestone is reached. Use null/empty for no power-up reward.</summary>
    public PowerUpType[] powerUpRewards;
    /// <summary>Lives to gift on this milestone. 0 = none.</summary>
    public int bonusLives;
  }

  // Preserve existing non-Rush reward balancing without displaying score ranks.
  private static readonly Milestone[] milestones = new Milestone[]
  {
    new Milestone {
      score = 500,
      powerUpRewards = new[] { PowerUpType.Shield },
      bonusLives = 0,
    },
    new Milestone {
      score = 1000,
      powerUpRewards = new[] { PowerUpType.WiderCatcher },
      bonusLives = 0,
    },
    new Milestone {
      score = 2500,
      powerUpRewards = new[] { PowerUpType.DoubleScore },
      bonusLives = 0,
    },
    new Milestone {
      score = 5000,
      powerUpRewards = new[] { PowerUpType.WiderCatcher, PowerUpType.Shield },
      bonusLives = 0,
    },
    new Milestone {
      score = 10000,
      powerUpRewards = new[] {
        PowerUpType.WiderCatcher,
        PowerUpType.DoubleScore,
        PowerUpType.Shield,
      },
      bonusLives = 0,
    },
  };

  // Highest milestone INDEX awarded this round. -1 means none yet. Reset by
  // ResetForNewRound at round start. We don't track by score directly because
  // future tunings might insert/reorder thresholds.
  private static int highestAwardedIndex = -1;

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
      }
    }
  }

  private static void ApplyReward(Milestone m)
  {
    // No power-up rewards in Rush Mode.
    if (m.powerUpRewards != null && GameState.Mode != GameState.GameMode.Rush)
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
