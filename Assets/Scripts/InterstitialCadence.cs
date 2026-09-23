using System;
using UnityEngine;

/// <summary>
/// Persists the player-friendly cadence for forced full-screen ads.
/// Runtime flags track the current run; counters and timestamps survive restarts.
/// </summary>
public sealed class InterstitialCadence
{
    public const int FirstEligibleCompletedRun = 4;
    public const int RunsBetweenInterstitials = 3;
    public const double FullScreenCooldownSeconds = 120d;

    private const string CompletedRunsKey = "Ads.Interstitial.CompletedRuns";
    private const string LastInterstitialRunKey = "Ads.Interstitial.LastShownRun";
    private const string LastFullScreenUtcTicksKey = "Ads.FullScreen.LastShownUtcTicks";

    private bool roundActive;
    private bool rewardedAdShownThisRun;
    private bool lastCompletedRunHadRewardedAd;

    public void BeginRound()
    {
        roundActive = true;
        rewardedAdShownThisRun = false;
        lastCompletedRunHadRewardedAd = false;
    }

    public void MarkRewardedAdShown(DateTime utcNow)
    {
        rewardedAdShownThisRun = true;
        SaveLastFullScreenTime(utcNow);
    }

    public void CompleteRound()
    {
        if (!roundActive) return;

        roundActive = false;
        lastCompletedRunHadRewardedAd = rewardedAdShownThisRun;

        int completedRuns = Mathf.Max(0, PlayerPrefs.GetInt(CompletedRunsKey, 0));
        PlayerPrefs.SetInt(CompletedRunsKey, completedRuns + 1);
        PlayerPrefs.Save();
    }

    public bool CanShowInterstitial(DateTime utcNow)
    {
        int completedRuns = Mathf.Max(0, PlayerPrefs.GetInt(CompletedRunsKey, 0));
        int lastInterstitialRun =
            Mathf.Max(0, PlayerPrefs.GetInt(LastInterstitialRunKey, 0));

        double secondsSinceFullScreenAd = GetSecondsSinceLastFullScreenAd(utcNow);
        return IsEligible(
            completedRuns,
            lastInterstitialRun,
            lastCompletedRunHadRewardedAd,
            secondsSinceFullScreenAd);
    }

    public void MarkInterstitialShown(DateTime utcNow)
    {
        int completedRuns = Mathf.Max(0, PlayerPrefs.GetInt(CompletedRunsKey, 0));
        PlayerPrefs.SetInt(LastInterstitialRunKey, completedRuns);
        SaveLastFullScreenTime(utcNow);
        lastCompletedRunHadRewardedAd = false;
    }

    internal static bool IsEligible(
        int completedRuns,
        int lastInterstitialRun,
        bool lastCompletedRunHadRewardedAd,
        double secondsSinceFullScreenAd)
    {
        if (lastCompletedRunHadRewardedAd) return false;
        if (completedRuns < FirstEligibleCompletedRun) return false;
        if (secondsSinceFullScreenAd < FullScreenCooldownSeconds) return false;

        return lastInterstitialRun <= 0
            || completedRuns - lastInterstitialRun >= RunsBetweenInterstitials;
    }

    private static double GetSecondsSinceLastFullScreenAd(DateTime utcNow)
    {
        string rawTicks = PlayerPrefs.GetString(LastFullScreenUtcTicksKey, "");
        if (string.IsNullOrEmpty(rawTicks)) return double.PositiveInfinity;

        if (!long.TryParse(rawTicks, out long savedTicks)
            || savedTicks <= 0
            || savedTicks > DateTime.MaxValue.Ticks)
        {
            Debug.LogWarning(
                "[InterstitialCadence] Invalid saved full-screen ad timestamp; "
                + "resetting the cooldown.");
            PlayerPrefs.DeleteKey(LastFullScreenUtcTicksKey);
            PlayerPrefs.Save();
            return 0d;
        }

        long nowTicks = utcNow.ToUniversalTime().Ticks;
        if (nowTicks <= savedTicks) return 0d;
        return TimeSpan.FromTicks(nowTicks - savedTicks).TotalSeconds;
    }

    private static void SaveLastFullScreenTime(DateTime utcNow)
    {
        PlayerPrefs.SetString(
            LastFullScreenUtcTicksKey,
            utcNow.ToUniversalTime().Ticks.ToString());
        PlayerPrefs.Save();
    }
}
