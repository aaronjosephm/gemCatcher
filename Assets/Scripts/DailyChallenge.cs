using System;
using System.Globalization;
using UnityEngine;

// =============================================================================
//  DailyChallenge
// -----------------------------------------------------------------------------
//  Date / seed / streak logic for the once-per-day challenge mode. Pure C# —
//  no MonoBehaviour, no scene presence. Stores its state in PlayerPrefs so the
//  streak survives across sessions.
//
//  Day boundaries are UTC so every player worldwide sees the same challenge on
//  the same calendar date. The seed is derived from "yyyy-MM-dd" via a stable
//  hash so iOS, Android, and standalone all generate the identical RNG state
//  for a given day.
//
//  Player commitment model (v1): the day is "used" the moment the player taps
//  Daily Challenge and enters gameplay. Force-quitting mid-round still costs
//  them their daily attempt. Streak is only awarded on actual completion.
// =============================================================================
public static class DailyChallenge
{
  // ---- Tuning constants ----------------------------------------------------

  /// <summary>Number of gems spawned in a single daily round.</summary>
  public const int GemsPerRound = 30;

  /// <summary>Lives the player starts the daily round with. Locked — no bonus lives.</summary>
  public const int LivesPerRound = 3;

  /// <summary>Launch epoch — used to compute "Day N" labels in the UI.</summary>
  private static readonly DateTime DayEpochUtc =
      new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

  // ---- PlayerPrefs keys ----------------------------------------------------

  private const string LastStartedDateKey = "Daily.LastStarted";
  private const string LastCompletedDateKey = "Daily.LastCompleted";
  private const string LastScoreKey = "Daily.LastScore";
  private const string CurrentStreakKey = "Daily.Streak";
  private const string BestStreakKey = "Daily.BestStreak";
  private const string TotalCompletionsKey = "Daily.TotalCompletions";

  // ---- Date utilities ------------------------------------------------------

  /// <summary>Today's UTC date as "yyyy-MM-dd". Used as the seed input.</summary>
  public static string TodayKey
      => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

  /// <summary>Deterministic 32-bit RNG seed for today's challenge.</summary>
  public static int TodaySeed => SeedFromDate(TodayKey);

  /// <summary>"Day 217" style label — useful for UI.</summary>
  public static int DayNumber
  {
    get
    {
      // Days since launch + 1 so the very first day is "Day 1".
      int days = (int)(DateTime.UtcNow.Date - DayEpochUtc.Date).TotalDays;
      return Mathf.Max(1, days + 1);
    }
  }

  /// <summary>Time remaining until UTC midnight, when the next challenge unlocks.</summary>
  public static TimeSpan TimeUntilNextChallenge
  {
    get
    {
      DateTime nowUtc = DateTime.UtcNow;
      DateTime nextUtcMidnight = nowUtc.Date.AddDays(1);
      TimeSpan diff = nextUtcMidnight - nowUtc;
      return diff < TimeSpan.Zero ? TimeSpan.Zero : diff;
    }
  }

  // ---- State accessors -----------------------------------------------------

  /// <summary>True if the player has already started today's challenge.</summary>
  public static bool HasPlayedToday
      => PlayerPrefs.GetString(LastStartedDateKey, "") == TodayKey;

  /// <summary>True if the player has completed (finished) today's challenge.</summary>
  public static bool HasCompletedToday
      => PlayerPrefs.GetString(LastCompletedDateKey, "") == TodayKey;

  public static int CurrentStreak => PlayerPrefs.GetInt(CurrentStreakKey, 0);

  public static int BestStreak => PlayerPrefs.GetInt(BestStreakKey, 0);

  /// <summary>Score from today's run if completed, otherwise 0.</summary>
  public static int LastScore
      => HasCompletedToday ? PlayerPrefs.GetInt(LastScoreKey, 0) : 0;

  public static int TotalCompletions => PlayerPrefs.GetInt(TotalCompletionsKey, 0);

  // ---- Lifecycle hooks (called from gameplay) ------------------------------

  /// <summary>
  /// Mark today as "started" — this is the moment the daily attempt is
  /// committed. Subsequent taps of Daily Challenge will hit the cooldown.
  /// </summary>
  public static void MarkStarted()
  {
    PlayerPrefs.SetString(LastStartedDateKey, TodayKey);
    PlayerPrefs.Save();
  }

  /// <summary>
  /// Record a completed daily run. Updates streak based on continuity from the
  /// previously completed date and persists the new score.
  /// Returns the streak count after this completion (≥ 1).
  /// </summary>
  public static int RecordCompletion(int score)
  {
    // Don't double-count if the player somehow finishes twice on the same day.
    if (HasCompletedToday) return CurrentStreak;

    int newStreak = ComputeStreakAfterCompletion();

    PlayerPrefs.SetString(LastCompletedDateKey, TodayKey);
    PlayerPrefs.SetInt(LastScoreKey, Mathf.Max(0, score));
    PlayerPrefs.SetInt(CurrentStreakKey, newStreak);
    PlayerPrefs.SetInt(TotalCompletionsKey, TotalCompletions + 1);
    if (newStreak > BestStreak)
    {
      PlayerPrefs.SetInt(BestStreakKey, newStreak);
    }
    PlayerPrefs.Save();
    return newStreak;
  }

  // Streak rule: +1 if last completion was yesterday; reset to 1 otherwise.
  // Future-dated lastCompleted (clock manipulation) is treated as "no
  // continuity" — the player gets a fresh streak of 1 when they actually
  // complete a real day's challenge.
  private static int ComputeStreakAfterCompletion()
  {
    string lastCompleted = PlayerPrefs.GetString(LastCompletedDateKey, "");
    if (string.IsNullOrEmpty(lastCompleted)) return 1;

    if (!DateTime.TryParseExact(
            lastCompleted, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
            out DateTime parsed))
    {
      return 1;
    }

    int daysSince = (int)(DateTime.UtcNow.Date - parsed.ToUniversalTime().Date).TotalDays;
    if (daysSince == 1) return CurrentStreak + 1;
    if (daysSince == 0) return CurrentStreak;     // shouldn't hit (HasCompletedToday guards it)
    if (daysSince < 0) return 1;                  // future date — reset, don't reward
    return 1;                                     // skipped a day — reset
  }

  // ---- Seed helper ---------------------------------------------------------

  /// <summary>
  /// Stable 32-bit hash of "yyyy-MM-dd". We don't use string.GetHashCode
  /// because .NET doesn't guarantee stability across processes/runtimes —
  /// a custom rolling hash gives bit-identical seeds on every platform.
  /// </summary>
  public static int SeedFromDate(string dateStr)
  {
    if (string.IsNullOrEmpty(dateStr)) return 0;
    unchecked
    {
      int hash = 17;
      for (int i = 0; i < dateStr.Length; i++)
      {
        hash = hash * 31 + dateStr[i];
      }
      return hash;
    }
  }

  // ---- Editor helpers ------------------------------------------------------

  /// <summary>
  /// Clear all daily-challenge state. Editor-only convenience for testing
  /// without waiting 24 hours.
  /// </summary>
  [System.Diagnostics.Conditional("UNITY_EDITOR")]
  public static void DebugReset()
  {
    PlayerPrefs.DeleteKey(LastStartedDateKey);
    PlayerPrefs.DeleteKey(LastCompletedDateKey);
    PlayerPrefs.DeleteKey(LastScoreKey);
    PlayerPrefs.DeleteKey(CurrentStreakKey);
    PlayerPrefs.DeleteKey(BestStreakKey);
    PlayerPrefs.DeleteKey(TotalCompletionsKey);
    PlayerPrefs.Save();
    Debug.Log("[DailyChallenge] Reset all state.");
  }
}
