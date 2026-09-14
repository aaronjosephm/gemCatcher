using System;
using System.Collections;
using System.Threading;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using UnityEngine;

/// <summary>
/// Owns AdMob interstitial and rewarded-ad lifecycles. Forced interstitials
/// are disabled by Remove Ads; optional rewarded ads remain available because
/// they are shown only after an explicit player action and grant a direct reward.
/// </summary>
public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance { get; private set; }

    // Google's official test ad unit IDs. Development builds always use these.
    private const string TestInterstitialIdAndroid = "ca-app-pub-3940256099942544/1033173712";
    private const string TestInterstitialIdIOS = "ca-app-pub-3940256099942544/4411468910";
    private const string TestRewardedIdAndroid = "ca-app-pub-3940256099942544/5224354917";
    private const string TestRewardedIdIOS = "ca-app-pub-3940256099942544/1712485313";

    // Replace all four production ad unit IDs before shipping.
    private const string ProductionInterstitialIdAndroid = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_UNIT";
    private const string ProductionInterstitialIdIOS = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_UNIT";
    private const string ProductionRewardedIdAndroid = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_REWARDED_UNIT";
    private const string ProductionRewardedIdIOS = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_REWARDED_UNIT";

    private const float RewardedRetryDelaySeconds = 10f;
    private const int RewardedMaxAutomaticRetries = 5;

    private static bool sdkInitialized;
    private static bool sdkInitializing;
    private static bool adUnitModeLogged;

    private InterstitialAd interstitialAd;
    private RewardedAd rewardedAd;
    private bool rewardedAdLoading;
    private int rewardedRetryAttempt;
    private Coroutine rewardedRetryCoroutine;
    private bool fullScreenAdAudioSuspended;

    public static event Action<bool> OnRewardedAvailabilityChanged;

    public bool IsRewardedContinueReady
    {
        get
        {
#if UNITY_EDITOR
            return true;
#else
            return rewardedAd != null && rewardedAd.CanShowAd();
#endif
        }
    }

    private static string InterstitialAdUnitId
    {
        get
        {
#if UNITY_ANDROID
            return SelectAdUnitId(TestInterstitialIdAndroid, ProductionInterstitialIdAndroid);
#elif UNITY_IOS
            return SelectAdUnitId(TestInterstitialIdIOS, ProductionInterstitialIdIOS);
#else
            return TestInterstitialIdAndroid;
#endif
        }
    }

    private static string RewardedAdUnitId
    {
        get
        {
#if UNITY_ANDROID
            return SelectAdUnitId(TestRewardedIdAndroid, ProductionRewardedIdAndroid);
#elif UNITY_IOS
            return SelectAdUnitId(TestRewardedIdIOS, ProductionRewardedIdIOS);
#else
            return TestRewardedIdAndroid;
#endif
        }
    }

    private static string SelectAdUnitId(string testId, string productionId)
    {
        return Debug.isDebugBuild || !IsConfiguredProductionAdUnitId(productionId)
            ? testId
            : productionId;
    }

    private static bool IsConfiguredProductionAdUnitId(string adUnitId)
    {
        return !string.IsNullOrWhiteSpace(adUnitId)
            && adUnitId.StartsWith("ca-app-pub-", StringComparison.Ordinal)
            && adUnitId.IndexOf('/') >= 0
            && adUnitId.IndexOf("REPLACE", StringComparison.OrdinalIgnoreCase) < 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnLoad()
    {
        Instance = null;
        sdkInitialized = false;
        sdkInitializing = false;
        adUnitModeLogged = false;
        OnRewardedAvailabilityChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null || FindObjectOfType<AdsManager>() != null) return;
        new GameObject("AdsManager (auto)").AddComponent<AdsManager>();
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
        if (IAPManager.RemoveAdsPurchaseEnabled)
        {
            IAPManager.OnAdsRemoved += HandleAdsRemoved;
        }
        LogAdUnitMode();
        InitializeAndPreload();
    }

    private static void LogAdUnitMode()
    {
        if (adUnitModeLogged) return;
        adUnitModeLogged = true;

#if UNITY_ANDROID
        bool productionIdsConfigured =
            IsConfiguredProductionAdUnitId(ProductionInterstitialIdAndroid)
            && IsConfiguredProductionAdUnitId(ProductionRewardedIdAndroid);
#elif UNITY_IOS
        bool productionIdsConfigured =
            IsConfiguredProductionAdUnitId(ProductionInterstitialIdIOS)
            && IsConfiguredProductionAdUnitId(ProductionRewardedIdIOS);
#else
        bool productionIdsConfigured = false;
#endif

        if (!Debug.isDebugBuild && !productionIdsConfigured)
        {
            Debug.LogWarning(
                "[AdsManager] Production ad unit IDs are not configured; "
                + "using Google's official test units. This build cannot earn ad revenue.");
        }
    }

    private void InitializeAndPreload()
    {
#if UNITY_EDITOR
        // The plugin's invisible Editor placeholder can leave Time.timeScale at
        // zero. Device builds exercise the real SDK; the Editor safely simulates
        // only the rewarded completion callback.
        Debug.Log("[AdsManager] Editor mode: AdMob skipped; rewarded completion is simulated.");
        NotifyRewardedAvailability();
        return;
#else
        if (sdkInitialized)
        {
            if (!IAPManager.AdsRemoved) LoadInterstitial();
            LoadRewarded();
            return;
        }

        if (sdkInitializing) return;
        sdkInitializing = true;
        MobileAds.RaiseAdEventsOnUnityMainThread = true;

        MobileAds.Initialize(_ =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                sdkInitializing = false;
                if (this == null) return;

                sdkInitialized = true;
                Debug.Log("[AdsManager] Google Mobile Ads initialized.");

                if (!IAPManager.AdsRemoved) LoadInterstitial();
                LoadRewarded();
            });
        });
#endif
    }

    private void LoadInterstitial()
    {
#if UNITY_EDITOR
        return;
#else
        if (!sdkInitialized || IAPManager.AdsRemoved || interstitialAd != null) return;

        InterstitialAd.Load(InterstitialAdUnitId, new AdRequest(), (ad, error) =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                if (this == null || IAPManager.AdsRemoved)
                {
                    ad?.Destroy();
                    return;
                }

                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[AdsManager] Interstitial failed to load: {error}");
                    return;
                }

                interstitialAd = ad;
                RegisterInterstitialEventHandlers(ad);
                Debug.Log("[AdsManager] Interstitial preloaded.");
            });
        });
#endif
    }

    private void RegisterInterstitialEventHandlers(InterstitialAd ad)
    {
        ad.OnAdFullScreenContentFailed += error =>
        {
            Debug.LogWarning($"[AdsManager] Interstitial failed to show: {error}");
            MobileAdsEventExecutor.ExecuteInUpdate(() => RetireAndPreloadNext(ad));
        };
        ad.OnAdFullScreenContentClosed += () =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() => RetireAndPreloadNext(ad));
        };
    }

    private void RetireAndPreloadNext(InterstitialAd ad)
    {
        ad.Destroy();
        if (interstitialAd == ad) interstitialAd = null;
        LoadInterstitial();
    }

    /// <summary>
    /// Shows a preloaded interstitial, then always invokes the completion callback.
    /// </summary>
    public void ShowInterstitial(Action onComplete)
    {
        if (IAPManager.AdsRemoved || interstitialAd == null || !interstitialAd.CanShowAd())
        {
            onComplete?.Invoke();
            if (!IAPManager.AdsRemoved && interstitialAd == null) LoadInterstitial();
            return;
        }

        InterstitialAd adToShow = interstitialAd;
        bool completed = false;
        void Complete()
        {
            if (completed) return;
            completed = true;
            RestoreGameAudioAfterAd();
            onComplete?.Invoke();
        }

        adToShow.OnAdFullScreenContentClosed += Complete;
        adToShow.OnAdFullScreenContentFailed += _ => Complete();
        SuspendGameAudioForAd();
        adToShow.Show();
    }

    /// <summary>
    /// Ensures an opt-in rewarded continue ad is being prepared.
    /// </summary>
    public void PrepareRewardedContinue()
    {
#if UNITY_EDITOR
        NotifyRewardedAvailability();
#else
        rewardedRetryAttempt = 0;
        if (!sdkInitialized)
        {
            InitializeAndPreload();
            return;
        }

        LoadRewarded();
#endif
    }

    /// <summary>
    /// Shows the rewarded ad and reports true only after the SDK grants the
    /// reward and the full-screen content closes. Returns false if no ad was ready.
    /// </summary>
    public bool TryShowRewardedContinue(Action<bool> onComplete)
    {
#if UNITY_EDITOR
        SuspendGameAudioForAd();
        StartCoroutine(SimulateRewardedContinue(result =>
        {
            RestoreGameAudioAfterAd();
            onComplete?.Invoke(result);
        }));
        return true;
#else
        if (!IsRewardedContinueReady)
        {
            PrepareRewardedContinue();
            return false;
        }

        RewardedAd adToShow = rewardedAd;
        rewardedAd = null;
        NotifyRewardedAvailability();

        int rewardEarned = 0;
        int completionSent = 0;

        Action<bool> finish = granted =>
        {
            if (Interlocked.Exchange(ref completionSent, 1) != 0) return;

            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                RestoreGameAudioAfterAd();
                adToShow.Destroy();
                onComplete?.Invoke(granted);
                LoadRewarded();
            });
        };

        adToShow.OnAdFullScreenContentClosed += () =>
        {
            finish(Volatile.Read(ref rewardEarned) == 1);
        };
        adToShow.OnAdFullScreenContentFailed += error =>
        {
            Debug.LogWarning($"[AdsManager] Rewarded ad failed to open: {error}");
            finish(false);
        };

        SuspendGameAudioForAd();
        adToShow.Show(_ => Interlocked.Exchange(ref rewardEarned, 1));
        return true;
#endif
    }

    private void LoadRewarded()
    {
#if UNITY_EDITOR
        return;
#else
        if (!sdkInitialized || rewardedAdLoading || IsRewardedContinueReady) return;

        CancelRewardedRetry();
        rewardedAdLoading = true;
        RewardedAd.Load(RewardedAdUnitId, new AdRequest(), (ad, error) =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                if (this == null)
                {
                    ad?.Destroy();
                    return;
                }

                rewardedAdLoading = false;
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[AdsManager] Rewarded ad failed to load: {error}");
                    NotifyRewardedAvailability();
                    ScheduleRewardedRetry();
                    return;
                }

                CancelRewardedRetry();
                rewardedRetryAttempt = 0;
                DestroyRewarded();
                rewardedAd = ad;
                NotifyRewardedAvailability();
                Debug.Log("[AdsManager] Rewarded continue ad preloaded.");
            });
        });
#endif
    }

    private void ScheduleRewardedRetry()
    {
        if (rewardedRetryCoroutine != null
            || rewardedRetryAttempt >= RewardedMaxAutomaticRetries)
        {
            return;
        }

        float delay = Mathf.Min(
            RewardedRetryDelaySeconds * Mathf.Pow(2f, rewardedRetryAttempt),
            120f);
        rewardedRetryAttempt++;
        rewardedRetryCoroutine = StartCoroutine(RetryRewardedAfterDelay(delay));
    }

    private IEnumerator RetryRewardedAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        rewardedRetryCoroutine = null;
        LoadRewarded();
    }

    private void CancelRewardedRetry()
    {
        if (rewardedRetryCoroutine == null) return;
        StopCoroutine(rewardedRetryCoroutine);
        rewardedRetryCoroutine = null;
    }

#if UNITY_EDITOR
    private static IEnumerator SimulateRewardedContinue(Action<bool> onComplete)
    {
        yield return new WaitForSecondsRealtime(0.25f);
        Debug.Log("[AdsManager] Simulated rewarded continue completed in the Editor.");
        onComplete?.Invoke(true);
    }
#endif

    private void NotifyRewardedAvailability()
    {
        OnRewardedAvailabilityChanged?.Invoke(IsRewardedContinueReady);
    }

    private void SuspendGameAudioForAd()
    {
        if (fullScreenAdAudioSuspended) return;

        fullScreenAdAudioSuspended = true;
        SoundManager.SuspendForFullScreenAd();
    }

    private void RestoreGameAudioAfterAd()
    {
        if (!fullScreenAdAudioSuspended) return;

        fullScreenAdAudioSuspended = false;
        SoundManager.RestoreAfterFullScreenAd();
    }

    private void DestroyRewarded()
    {
        if (rewardedAd == null) return;
        rewardedAd.Destroy();
        rewardedAd = null;
        NotifyRewardedAvailability();
    }

    private void HandleAdsRemoved()
    {
        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }

        Debug.Log("[AdsManager] Remove Ads applied; forced interstitials are disabled.");
    }

    void OnDestroy()
    {
        if (IAPManager.RemoveAdsPurchaseEnabled)
        {
            IAPManager.OnAdsRemoved -= HandleAdsRemoved;
        }
        if (Instance != this) return;

        RestoreGameAudioAfterAd();
        CancelRewardedRetry();
        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }
        DestroyRewarded();
        Instance = null;
    }
}
