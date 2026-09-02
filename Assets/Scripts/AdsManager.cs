using System;
using GoogleMobileAds.Api;
using UnityEngine;

/// <summary>
/// Wraps the Google Mobile Ads (AdMob) Unity plugin to show a single
/// interstitial ad at natural transition points (leaving the game-over
/// screen). Auto-bootstraps like the other manager singletons
/// (<see cref="PowerUpManager"/>, <see cref="SoundManager"/>,
/// <see cref="HapticManager"/>, <see cref="IAPManager"/>) — no scene setup
/// required.
///
/// Design:
///   - If the player has purchased "Remove Ads" (<see cref="IAPManager.AdsRemoved"/>),
///     the Ads SDK is never even initialized — zero ad-related activity at all.
///   - One interstitial is always kept preloaded so <see cref="ShowInterstitial"/>
///     can show it instantly; the next one starts preloading as soon as the
///     current one closes or fails.
///   - <see cref="ShowInterstitial"/> never blocks gameplay: if no ad is ready
///     (still loading, failed to load, no fill, or ads removed) it invokes the
///     completion callback immediately so the caller's scene transition still
///     happens.
/// </summary>
public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance { get; private set; }

    // Google's official test ad unit IDs. Safe to use during development —
    // they always fill and never generate real (or accidentally invalid)
    // ad traffic. https://developers.google.com/admob/unity/test-ads
    private const string TestInterstitialIdAndroid = "ca-app-pub-3940256099942544/1033173712";
    private const string TestInterstitialIdIOS = "ca-app-pub-3940256099942544/4411468910";

    // TODO: Replace with your real AdMob interstitial ad unit IDs before
    // shipping a release build. Create these in the AdMob dashboard under
    // Apps > (your app) > Ad units, after the app has been added to AdMob
    // (Assets > Google Mobile Ads > Settings sets the App ID). Test IDs
    // above are used automatically for every Editor/dev-build run, so it's
    // safe to fill these in ahead of time without affecting local testing.
    private const string ProductionInterstitialIdAndroid = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_UNIT";
    private const string ProductionInterstitialIdIOS = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_UNIT";

    private static string InterstitialAdUnitId
    {
        get
        {
            // Debug.isDebugBuild is always true in the Editor and true for any
            // Development Build — this guarantees test ads are used for every
            // local/dev run, and only a Release build ever requests real ads.
            // Requesting real ads from a developer's own device/editor risks
            // AdMob flagging the account for invalid traffic.
#if UNITY_ANDROID
            return Debug.isDebugBuild ? TestInterstitialIdAndroid : ProductionInterstitialIdAndroid;
#elif UNITY_IOS
            return Debug.isDebugBuild ? TestInterstitialIdIOS : ProductionInterstitialIdIOS;
#else
            return TestInterstitialIdAndroid;
#endif
        }
    }

    private static bool sdkInitialized;
    private InterstitialAd interstitialAd;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnLoad()
    {
        Instance = null;
        sdkInitialized = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null) return;
        if (FindObjectOfType<AdsManager>() != null) return;

        GameObject go = new GameObject("AdsManager (auto)");
        go.AddComponent<AdsManager>();
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

        IAPManager.OnAdsRemoved += HandleAdsRemoved;
        InitializeAndPreload();
    }

    void OnDestroy()
    {
        IAPManager.OnAdsRemoved -= HandleAdsRemoved;
    }

    private void InitializeAndPreload()
    {
#if UNITY_EDITOR
        // The Google Mobile Ads Unity plugin has no real ad network in the
        // Editor - InterstitialAd.Show() there instantiates a placeholder
        // prefab and sets Time.timeScale = 0 to simulate a pause, but the
        // prefab isn't parented under our own UI canvases, so it renders
        // behind them and is invisible. The player then has no visible way
        // to dismiss it and the timeScale = 0 never gets undone, which looks
        // exactly like the game freezing with no ad on screen. Skip the ads
        // SDK entirely in the Editor; ShowInterstitial() below always falls
        // through to onComplete when no ad is loaded. Ads (including the
        // real Google test ad units) only need to be verified on an actual
        // Android/iOS Development Build - see docs/monetization-setup.md.
        return;
#else
        // Ads already removed (restored from a previous purchase) - never
        // touch the ads SDK at all.
        if (IAPManager.AdsRemoved) return;

        if (sdkInitialized)
        {
            LoadInterstitial();
            return;
        }

        MobileAds.Initialize(status =>
        {
            sdkInitialized = true;
            Debug.Log("[AdsManager] Google Mobile Ads initialized.");

            // A purchase could have completed while the SDK was initializing.
            if (!IAPManager.AdsRemoved)
            {
                LoadInterstitial();
            }
        });
#endif
    }

    private void LoadInterstitial()
    {
#if UNITY_EDITOR
        return;
#else
        if (IAPManager.AdsRemoved) return;

        var adRequest = new AdRequest();
        InterstitialAd.Load(InterstitialAdUnitId, adRequest, (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning($"[AdsManager] Interstitial failed to load: {error}");
                return;
            }

            interstitialAd = ad;
            RegisterEventHandlers(interstitialAd);
            Debug.Log("[AdsManager] Interstitial preloaded.");
        });
#endif
    }

    private void RegisterEventHandlers(InterstitialAd ad)
    {
        ad.OnAdFullScreenContentFailed += error =>
        {
            Debug.LogWarning($"[AdsManager] Interstitial failed to show: {error}");
            RetireAndPreloadNext(ad);
        };
        ad.OnAdFullScreenContentClosed += () => RetireAndPreloadNext(ad);
    }

    private void RetireAndPreloadNext(InterstitialAd ad)
    {
        ad.Destroy();
        if (interstitialAd == ad)
        {
            interstitialAd = null;
        }
        LoadInterstitial();
    }

    /// <summary>
    /// Shows a preloaded interstitial if one is ready, then invokes
    /// <paramref name="onComplete"/>. If ads are removed or no ad is
    /// currently available, <paramref name="onComplete"/> fires immediately
    /// so the caller's scene transition is never blocked waiting on an ad.
    /// </summary>
    public void ShowInterstitial(Action onComplete)
    {
        if (IAPManager.AdsRemoved || interstitialAd == null || !interstitialAd.CanShowAd())
        {
            onComplete?.Invoke();

            // Make sure one is in flight for next time (e.g. the previous
            // load failed, or this is the very first call and preloading is
            // still in progress).
            if (!IAPManager.AdsRemoved && interstitialAd == null)
            {
                LoadInterstitial();
            }
            return;
        }

        InterstitialAd adToShow = interstitialAd;
        bool completed = false;
        void Complete()
        {
            if (completed) return;
            completed = true;
            onComplete?.Invoke();
        }

        adToShow.OnAdFullScreenContentClosed += Complete;
        adToShow.OnAdFullScreenContentFailed += _ => Complete();

        adToShow.Show();
    }

    private void HandleAdsRemoved()
    {
        if (interstitialAd != null)
        {
            interstitialAd.Destroy();
            interstitialAd = null;
        }
        Debug.Log("[AdsManager] Ads removed — interstitials disabled.");
    }
}
