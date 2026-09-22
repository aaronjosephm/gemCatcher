using System;
using System.Collections;
using System.Threading;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

/// <summary>
/// Owns AdMob interstitial and rewarded-ad lifecycles. Forced interstitials
/// are disabled by Remove Ads; optional rewarded ads remain available because
/// they are shown only after an explicit player action and grant a direct reward.
/// </summary>
public class AdsManager : MonoBehaviour
{
    public enum AgeBand
    {
        Unknown = 0,
        Under16 = 1,
        Adult16Plus = 2,
    }

    public enum PrivacyFlowState
    {
        AwaitingAgeSelection = 0,
        Resolving = 1,
        Ready = 2,
    }

    public static AdsManager Instance { get; private set; }

    private const string AgeBandPrefsKey = "AdPrivacy.AgeBand";

    // Google's official test ad unit IDs. Development builds always use these.
    private const string TestInterstitialIdAndroid = "ca-app-pub-3940256099942544/1033173712";
    private const string TestInterstitialIdIOS = "ca-app-pub-3940256099942544/4411468910";
    private const string TestRewardedIdAndroid = "ca-app-pub-3940256099942544/5224354917";
    private const string TestRewardedIdIOS = "ca-app-pub-3940256099942544/1712485313";

    private const string ProductionInterstitialIdAndroid = "ca-app-pub-5414130987848915/3174600810";
    private const string ProductionInterstitialIdIOS = "ca-app-pub-5414130987848915/4898746027";
    private const string ProductionRewardedIdAndroid = "ca-app-pub-5414130987848915/7716481058";
    private const string ProductionRewardedIdIOS = "ca-app-pub-5414130987848915/2773689298";

    private const float RewardedRetryDelaySeconds = 10f;
    private const int RewardedMaxAutomaticRetries = 5;

    private static bool sdkInitialized;
    private static bool sdkInitializing;
    private static bool adUnitModeLogged;
    private static bool adsRequestPermitted;
    private static AgeBand selectedAgeBand = AgeBand.Unknown;
    private static PrivacyFlowState privacyFlowState = PrivacyFlowState.AwaitingAgeSelection;
    private static bool privacyOptionsRequired;
    private static bool privacyOptionsFormInProgress;
    private static string privacyOptionsStatusMessage = "";

    private InterstitialAd interstitialAd;
    private RewardedAd rewardedAd;
    private bool rewardedAdLoading;
    private int rewardedRetryAttempt;
    private Coroutine rewardedRetryCoroutine;
    private bool fullScreenAdAudioSuspended;
    private bool privacyBootstrapStarted;
    private bool adultConsentFlowRunning;
    private bool interstitialShowInProgress;
    private readonly InterstitialCadence interstitialCadence =
        new InterstitialCadence();

    public static event Action<bool> OnRewardedAvailabilityChanged;
    public static event Action OnPrivacyStateChanged;
    public static event Action OnPrivacyOptionsStateChanged;

    public static AgeBand SelectedAgeBand => selectedAgeBand;
    public static PrivacyFlowState CurrentPrivacyFlowState => privacyFlowState;
    public static bool IsPrivacyFlowBlocking => privacyFlowState != PrivacyFlowState.Ready;
    public static bool AdsRequestPermitted => adsRequestPermitted;
    public static bool PrivacyOptionsRequired =>
        selectedAgeBand == AgeBand.Adult16Plus && privacyOptionsRequired;
    public static bool PrivacyOptionsFormInProgress => privacyOptionsFormInProgress;
    public static string PrivacyOptionsStatusMessage => privacyOptionsStatusMessage;

    public bool IsRewardedContinueReady
    {
        get
        {
#if UNITY_EDITOR
            return adsRequestPermitted;
#else
            return adsRequestPermitted && rewardedAd != null && rewardedAd.CanShowAd();
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
        adsRequestPermitted = false;
        selectedAgeBand = AgeBand.Unknown;
        privacyFlowState = PrivacyFlowState.AwaitingAgeSelection;
        privacyOptionsRequired = false;
        privacyOptionsFormInProgress = false;
        privacyOptionsStatusMessage = "";
        OnRewardedAvailabilityChanged = null;
        OnPrivacyStateChanged = null;
        OnPrivacyOptionsStateChanged = null;
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
        GemCatcher.OnGameOverFinalized += HandleRoundFinalized;

        MobileAdsEventExecutor.Initialize();
#pragma warning disable 0618
        MobileAds.RaiseAdEventsOnUnityMainThread = true;
#pragma warning restore 0618

        LogAdUnitMode();
        LoadAgeBandAndBeginPrivacyFlow();
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

    private void LoadAgeBandAndBeginPrivacyFlow()
    {
        int storedValue = PlayerPrefs.GetInt(AgeBandPrefsKey, (int)AgeBand.Unknown);
        if (storedValue == (int)AgeBand.Under16 || storedValue == (int)AgeBand.Adult16Plus)
        {
            selectedAgeBand = (AgeBand)storedValue;
        }
        else
        {
            if (storedValue != (int)AgeBand.Unknown)
            {
                Debug.LogWarning(
                    $"[AdsManager] Ignoring invalid saved age band value {storedValue}.");
            }
            selectedAgeBand = AgeBand.Unknown;
        }

        BeginPrivacyBootstrap();
    }

    private void BeginPrivacyBootstrap()
    {
        if (privacyBootstrapStarted) return;
        privacyBootstrapStarted = true;

        switch (selectedAgeBand)
        {
            case AgeBand.Under16:
                BeginUnder16Flow();
                break;
            case AgeBand.Adult16Plus:
                BeginAdultConsentFlow();
                break;
            default:
                SetPrivacyFlowState(PrivacyFlowState.AwaitingAgeSelection);
                break;
        }
    }

    /// <summary>
    /// Persists a neutral age band and begins the matching privacy path.
    /// The selection is accepted only while no age band has been saved.
    /// </summary>
    public bool SelectAgeBand(AgeBand ageBand)
    {
        if (selectedAgeBand != AgeBand.Unknown
            || privacyFlowState != PrivacyFlowState.AwaitingAgeSelection)
        {
            return false;
        }

        if (ageBand != AgeBand.Under16 && ageBand != AgeBand.Adult16Plus)
        {
            Debug.LogWarning($"[AdsManager] Rejected invalid age band selection: {ageBand}.");
            return false;
        }

        selectedAgeBand = ageBand;
        PlayerPrefs.SetInt(AgeBandPrefsKey, (int)ageBand);
        PlayerPrefs.Save();

        if (ageBand == AgeBand.Under16)
        {
            BeginUnder16Flow();
        }
        else
        {
            BeginAdultConsentFlow();
        }

        return true;
    }

    private void BeginUnder16Flow()
    {
        adultConsentFlowRunning = false;
        SetPrivacyOptionsRequired(false);
        SetPrivacyFlowState(PrivacyFlowState.Resolving);

        PermitAdsAndInitialize(
            CreateUnder16RequestConfiguration(),
            "[AdsManager] Under-16 privacy configuration applied; UMP and ATT are skipped.");
    }

    private void BeginAdultConsentFlow()
    {
        if (adultConsentFlowRunning) return;

        adultConsentFlowRunning = true;
        SetPrivacyOptionsRequired(false);
        SetPrivacyFlowState(PrivacyFlowState.Resolving);

#if UNITY_EDITOR
        adultConsentFlowRunning = false;
        Debug.Log(
            "[AdsManager] Editor mode: adult UMP is skipped and rewarded completion is simulated.");
        PermitAdsAndInitialize(
            CreateAdultRequestConfiguration(),
            "[AdsManager] Editor adult privacy flow simulated.");
#else
        try
        {
            var requestParameters = new ConsentRequestParameters
            {
                TagForUnderAgeOfConsent = false,
            };

            ConsentInformation.Update(
                requestParameters,
                error => RunOnUnityThread(() => HandleConsentInformationUpdated(error)));
        }
        catch (Exception exception)
        {
            adultConsentFlowRunning = false;
            Debug.LogWarning(
                $"[AdsManager] UMP consent update could not start; continuing without ads. "
                + exception.Message);
            CompletePrivacyFlowWithoutAds();
        }
#endif
    }

    private void HandleConsentInformationUpdated(FormError error)
    {
        if (this == null || !adultConsentFlowRunning
            || selectedAgeBand != AgeBand.Adult16Plus)
        {
            return;
        }

        RefreshPrivacyOptionsRequirement();

        if (error != null)
        {
            adultConsentFlowRunning = false;
            Debug.LogWarning(
                $"[AdsManager] UMP consent update failed ({DescribeFormError(error)}).");
            CompleteAdultConsentFlowAfterUmp("consent update failure");
            return;
        }

        try
        {
            ConsentForm.LoadAndShowConsentFormIfRequired(
                formError => RunOnUnityThread(() => HandleConsentFormDismissed(formError)));
        }
        catch (Exception exception)
        {
            adultConsentFlowRunning = false;
            Debug.LogWarning(
                $"[AdsManager] UMP consent form could not be loaded; continuing based on "
                + $"cached consent. {exception.Message}");
            CompleteAdultConsentFlowAfterUmp("consent form startup failure");
        }
    }

    private void HandleConsentFormDismissed(FormError error)
    {
        if (this == null || !adultConsentFlowRunning
            || selectedAgeBand != AgeBand.Adult16Plus)
        {
            return;
        }

        adultConsentFlowRunning = false;
        RefreshPrivacyOptionsRequirement();

        if (error != null)
        {
            Debug.LogWarning(
                $"[AdsManager] UMP consent form failed ({DescribeFormError(error)}).");
        }

        CompleteAdultConsentFlowAfterUmp(
            error == null ? "consent flow completion" : "consent form failure");
    }

    private void CompleteAdultConsentFlowAfterUmp(string outcome)
    {
        if (CanRequestAdsFromUmp())
        {
            PermitAdsAndInitialize(
                CreateAdultRequestConfiguration(),
                $"[AdsManager] Adult UMP {outcome}; ad requests are permitted.");
            return;
        }

        Debug.LogWarning(
            $"[AdsManager] Adult UMP {outcome} did not provide permission to request ads; "
            + "gameplay will continue without ads.");
        CompletePrivacyFlowWithoutAds();
    }

    private static bool CanRequestAdsFromUmp()
    {
        try
        {
            return ConsentInformation.CanRequestAds();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[AdsManager] Unable to read UMP ad-request permission: {exception.Message}");
            return false;
        }
    }

    private void RefreshPrivacyOptionsRequirement()
    {
        if (selectedAgeBand != AgeBand.Adult16Plus)
        {
            SetPrivacyOptionsRequired(false);
            return;
        }

        try
        {
            SetPrivacyOptionsRequired(
                ConsentInformation.PrivacyOptionsRequirementStatus
                == PrivacyOptionsRequirementStatus.Required);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[AdsManager] Unable to read UMP privacy-options status: {exception.Message}");
            SetPrivacyOptionsRequired(false);
        }
    }

    /// <summary>
    /// Shows UMP's adult privacy-options form when the SDK requires an entry point.
    /// This flow never re-opens the blocking age gate.
    /// </summary>
    public bool ShowPrivacyOptionsForm()
    {
        if (selectedAgeBand != AgeBand.Adult16Plus || !privacyOptionsRequired)
        {
            Debug.LogWarning(
                "[AdsManager] Privacy options are not available for the current privacy state.");
            return false;
        }

        if (privacyOptionsFormInProgress) return false;

        privacyOptionsFormInProgress = true;
        privacyOptionsStatusMessage = "Opening privacy choices...";
        NotifyPrivacyOptionsStateChanged();

#if UNITY_EDITOR
        privacyOptionsFormInProgress = false;
        privacyOptionsStatusMessage = "Privacy choices are unavailable in the Unity Editor.";
        NotifyPrivacyOptionsStateChanged();
        return false;
#else
        try
        {
            ConsentForm.ShowPrivacyOptionsForm(
                error => RunOnUnityThread(() => HandlePrivacyOptionsFormDismissed(error)));
            return true;
        }
        catch (Exception exception)
        {
            privacyOptionsFormInProgress = false;
            privacyOptionsStatusMessage =
                "Couldn't open privacy choices. Please try again.";
            Debug.LogWarning(
                $"[AdsManager] UMP privacy-options form could not start: {exception.Message}");
            NotifyPrivacyOptionsStateChanged();
            return false;
        }
#endif
    }

    private void HandlePrivacyOptionsFormDismissed(FormError error)
    {
        if (this == null) return;

        privacyOptionsFormInProgress = false;
        RefreshPrivacyOptionsRequirement();

        if (error != null)
        {
            privacyOptionsStatusMessage =
                "Couldn't open privacy choices. Please try again.";
            Debug.LogWarning(
                $"[AdsManager] UMP privacy-options form failed ({DescribeFormError(error)}).");
            NotifyPrivacyOptionsStateChanged();
            return;
        }

        privacyOptionsStatusMessage = "Privacy choices updated.";

        if (CanRequestAdsFromUmp())
        {
            if (!adsRequestPermitted)
            {
                PermitAdsAndInitialize(
                    CreateAdultRequestConfiguration(),
                    "[AdsManager] Updated adult privacy choices permit ad requests.");
            }
        }
        else
        {
            DisableAdRequestsAndDestroyLoadedAds();
            Debug.LogWarning(
                "[AdsManager] Updated adult privacy choices no longer permit ad requests.");
        }

        NotifyPrivacyOptionsStateChanged();
    }

    private static RequestConfiguration CreateAdultRequestConfiguration()
    {
        return new RequestConfiguration
        {
            AgeRestrictedTreatment = AgeRestrictedTreatment.Unspecified,
            PublisherPrivacyPersonalizationState =
                PublisherPrivacyPersonalizationState.Default,
        };
    }

    private static RequestConfiguration CreateUnder16RequestConfiguration()
    {
        return new RequestConfiguration
        {
            AgeRestrictedTreatment = AgeRestrictedTreatment.Child,
            MaxAdContentRating = MaxAdContentRating.G,
            PublisherPrivacyPersonalizationState =
                PublisherPrivacyPersonalizationState.Disabled,
        };
    }

    private bool ApplyPostInitializationPrivacyConfiguration()
    {
        if (selectedAgeBand != AgeBand.Under16) return true;

        RequestConfiguration requestConfiguration =
            CreateUnder16RequestConfiguration();
        requestConfiguration.PublisherFirstPartyIdEnabled = false;

        try
        {
            // Google requires this setting after MobileAds.Initialize. Apply it
            // before any ad load while retaining all child-request safeguards.
            MobileAds.SetRequestConfiguration(requestConfiguration);
            Debug.Log(
                "[AdsManager] Under-16 publisher first-party ID disabled "
                + "before loading ads.");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[AdsManager] Failed to disable the publisher first-party ID; "
                + $"continuing without ads. {exception.Message}");
            return false;
        }
    }

    private void PermitAdsAndInitialize(
        RequestConfiguration requestConfiguration,
        string logMessage)
    {
        try
        {
            MobileAds.SetRequestConfiguration(requestConfiguration);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[AdsManager] Failed to apply ad request configuration; "
                + $"continuing without ads. {exception.Message}");
            CompletePrivacyFlowWithoutAds();
            return;
        }

        adsRequestPermitted = true;
        Debug.Log(logMessage);
        SetPrivacyFlowState(PrivacyFlowState.Ready);
        InitializeAndPreload();
    }

    private void CompletePrivacyFlowWithoutAds()
    {
        DisableAdRequestsAndDestroyLoadedAds();
        SetPrivacyFlowState(PrivacyFlowState.Ready);
    }

    private void DisableAdRequestsAndDestroyLoadedAds()
    {
        adsRequestPermitted = false;
        CancelRewardedRetry();

        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }

        DestroyRewarded();
        NotifyRewardedAvailability();
    }

    private static void SetPrivacyFlowState(PrivacyFlowState state)
    {
        if (privacyFlowState == state) return;
        privacyFlowState = state;
        OnPrivacyStateChanged?.Invoke();
    }

    private static void SetPrivacyOptionsRequired(bool required)
    {
        if (privacyOptionsRequired == required) return;
        privacyOptionsRequired = required;
        OnPrivacyOptionsStateChanged?.Invoke();
    }

    private static void NotifyPrivacyOptionsStateChanged()
    {
        OnPrivacyOptionsStateChanged?.Invoke();
    }

    private static void RunOnUnityThread(Action action)
    {
        if (action == null) return;
        if (MobileAdsEventExecutor.IsOnMainThread())
        {
            action();
        }
        else
        {
            MobileAdsEventExecutor.ExecuteInUpdate(action);
        }
    }

    private static string DescribeFormError(FormError error)
    {
        return $"{error.ErrorCode}: {error.Message}";
    }

    private void InitializeAndPreload()
    {
        if (!adsRequestPermitted)
        {
            NotifyRewardedAvailability();
            return;
        }

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

        try
        {
            MobileAds.Initialize(_ =>
            {
                MobileAdsEventExecutor.ExecuteInUpdate(() =>
                {
                    sdkInitializing = false;
                    if (this == null) return;

                    if (!ApplyPostInitializationPrivacyConfiguration())
                    {
                        CompletePrivacyFlowWithoutAds();
                        return;
                    }

                    sdkInitialized = true;
                    Debug.Log("[AdsManager] Google Mobile Ads initialized.");

                    if (!IAPManager.AdsRemoved) LoadInterstitial();
                    LoadRewarded();
                });
            });
        }
        catch (Exception exception)
        {
            sdkInitializing = false;
            Debug.LogWarning(
                $"[AdsManager] Google Mobile Ads initialization failed; "
                + $"gameplay will continue without loaded ads. {exception.Message}");
            NotifyRewardedAvailability();
        }
#endif
    }

    private void LoadInterstitial()
    {
#if UNITY_EDITOR
        return;
#else
        if (!adsRequestPermitted || !sdkInitialized
            || IAPManager.AdsRemoved || interstitialAd != null)
        {
            return;
        }

        InterstitialAd.Load(InterstitialAdUnitId, new AdRequest(), (ad, error) =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                if (this == null || !adsRequestPermitted || IAPManager.AdsRemoved)
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
    /// Marks the beginning of a playable round for interstitial cadence tracking.
    /// </summary>
    public void NotifyRoundStarted()
    {
        interstitialCadence.BeginRound();
    }

    private void HandleRoundFinalized()
    {
        interstitialCadence.CompleteRound();
    }

    /// <summary>
    /// Shows an eligible preloaded interstitial, then always invokes the callback.
    /// Ineligible or unavailable ads never delay the scene transition.
    /// </summary>
    public void ShowInterstitial(Action onComplete)
    {
        if (interstitialShowInProgress
            || !interstitialCadence.CanShowInterstitial(DateTime.UtcNow)
            || !adsRequestPermitted || IAPManager.AdsRemoved
            || interstitialAd == null || !interstitialAd.CanShowAd())
        {
            onComplete?.Invoke();
            if (adsRequestPermitted && !IAPManager.AdsRemoved && interstitialAd == null)
            {
                LoadInterstitial();
            }
            return;
        }

        InterstitialAd adToShow = interstitialAd;
        bool completed = false;
        interstitialShowInProgress = true;
        void Complete()
        {
            if (completed) return;
            completed = true;
            interstitialShowInProgress = false;
            RestoreGameAudioAfterAd();
            onComplete?.Invoke();
        }

        adToShow.OnAdFullScreenContentClosed += Complete;
        adToShow.OnAdFullScreenContentFailed += _ => Complete();
        interstitialCadence.MarkInterstitialShown(DateTime.UtcNow);
        SuspendGameAudioForAd();
        try
        {
            adToShow.Show();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[AdsManager] Interstitial could not be shown: {exception.Message}");
            RetireAndPreloadNext(adToShow);
            Complete();
        }
    }

    /// <summary>
    /// Ensures an opt-in rewarded continue ad is being prepared.
    /// </summary>
    public void PrepareRewardedContinue()
    {
        if (!adsRequestPermitted)
        {
            NotifyRewardedAvailability();
            return;
        }

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
        if (!IsRewardedContinueReady)
        {
            PrepareRewardedContinue();
            return false;
        }

#if UNITY_EDITOR
        interstitialCadence.MarkRewardedAdShown(DateTime.UtcNow);
        SuspendGameAudioForAd();
        StartCoroutine(SimulateRewardedContinue(result =>
        {
            RestoreGameAudioAfterAd();
            onComplete?.Invoke(result);
        }));
        return true;
#else
        RewardedAd adToShow = rewardedAd;
        rewardedAd = null;
        NotifyRewardedAvailability();
        interstitialCadence.MarkRewardedAdShown(DateTime.UtcNow);

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
        try
        {
            adToShow.Show(_ => Interlocked.Exchange(ref rewardEarned, 1));
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[AdsManager] Rewarded ad could not be shown: {exception.Message}");
            finish(false);
        }
        return true;
#endif
    }

    private void LoadRewarded()
    {
#if UNITY_EDITOR
        return;
#else
        if (!adsRequestPermitted || !sdkInitialized
            || rewardedAdLoading || IsRewardedContinueReady)
        {
            return;
        }

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
                if (!adsRequestPermitted)
                {
                    ad?.Destroy();
                    NotifyRewardedAvailability();
                    return;
                }

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

        GemCatcher.OnGameOverFinalized -= HandleRoundFinalized;
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
