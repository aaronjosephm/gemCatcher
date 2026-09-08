using System;
using UnityEngine;
using UnityEngine.Purchasing;
#if UNITY_IOS
using UnityEngine.Purchasing.Extension;
#endif

// Unity IAP 5.x marks the classic ConfigurationBuilder / IStoreListener API as
// [Obsolete] (warning-only) in favour of a new async, services-based API
// (UnityIAPServices.StoreController(), Connect/FetchProducts/FetchPurchases,
// Order-based purchase confirmation). That new API is a large surface built
// for catalog-driven, multi-product storefronts. This game sells exactly one
// non-consumable ("remove_ads"), so the classic API is deliberately kept here:
// it is simpler, still fully functional, and by far the best documented path
// for this use case. The obsolete warnings are expected noise from that
// choice, not a bug — suppressed below so the Console stays readable.
#pragma warning disable CS0618

/// <summary>
/// Handles the one-time "Remove Ads" non-consumable purchase via Unity IAP,
/// unified across the App Store and Google Play. Auto-bootstraps like the
/// other manager singletons (<see cref="PowerUpManager"/>, <see cref="SoundManager"/>,
/// <see cref="HapticManager"/>) — no scene setup required.
///
/// Ownership is tracked two ways:
///   1. A PlayerPrefs flag (<see cref="AdsRemoved"/>) — a fast, synchronous
///      local cache that <see cref="AdsManager"/> reads before every
///      game-over without waiting on the store.
///   2. Unity IAP's own receipt/entitlement data — the source of truth,
///      reconciled into the PlayerPrefs flag on init and whenever
///      <see cref="RestorePurchases"/> runs (e.g. after a reinstall).
/// </summary>
public class IAPManager : MonoBehaviour, IStoreListener
{
    /// <summary>Non-consumable product id. Must match the id configured in
    /// App Store Connect and Google Play Console.</summary>
    public const string RemoveAdsProductId = "remove_ads";

    private const string AdsRemovedPrefKey = "AdsRemoved";

    public static IAPManager Instance { get; private set; }

    private static IStoreController storeController;
    private static IExtensionProvider extensionProvider;

    /// <summary>True once the store finishes connecting (success or failure).</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// Fast local cache of ownership. Safe to read from anywhere (including
    /// before the store finishes initializing) since it's just PlayerPrefs.
    /// </summary>
    public static bool AdsRemoved
    {
        get => PlayerPrefs.GetInt(AdsRemovedPrefKey, 0) == 1;
        private set
        {
            PlayerPrefs.SetInt(AdsRemovedPrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Raised once, right after ads are removed (purchase or restore), so UI can refresh (e.g. hide the "Remove Ads" button).</summary>
    public static event Action OnAdsRemoved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnLoad()
    {
        Instance = null;
        storeController = null;
        extensionProvider = null;
        IsInitialized = false;
        OnAdsRemoved = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (Instance != null) return;
        if (FindObjectOfType<IAPManager>() != null) return;

        GameObject go = new GameObject("IAPManager (auto)");
        go.AddComponent<IAPManager>();
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

        if (storeController == null)
        {
            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            builder.AddProduct(RemoveAdsProductId, ProductType.NonConsumable);
            UnityPurchasing.Initialize(this, builder);
        }
    }

    /// <summary>Called by the "Remove Ads - $2" button on the main menu.</summary>
    public void BuyRemoveAds()
    {
        if (AdsRemoved) return;

        if (storeController == null)
        {
            Debug.LogWarning("[IAPManager] Store not initialized yet — try again in a moment.");
            return;
        }

        Product product = storeController.products.WithID(RemoveAdsProductId);
        if (product == null || !product.availableToPurchase)
        {
            Debug.LogWarning($"[IAPManager] Product '{RemoveAdsProductId}' is not available to purchase right now.");
            return;
        }

        storeController.InitiatePurchase(product);
    }

    /// <summary>
    /// Re-derives ownership from the store's own purchase records and syncs
    /// the local PlayerPrefs flag. Apple requires a working "Restore
    /// Purchases" control for any non-consumable IAP (App Review checks
    /// this), since a fresh install / new device has no local PlayerPrefs
    /// history to go on.
    /// </summary>
    public void RestorePurchases()
    {
        if (storeController == null)
        {
            Debug.LogWarning("[IAPManager] Store not initialized yet — try again in a moment.");
            return;
        }

#if UNITY_IOS
        // iOS/App Store requires an explicit restore call. Android/Google Play
        // purchases are already reflected in storeController.products once the
        // store finishes fetching, so there's nothing extra to trigger there.
        extensionProvider.GetExtension<IAppleExtensions>().RestoreTransactions(OnAppleRestoreFinished);
#else
        SyncOwnershipFromController();
        Debug.Log("[IAPManager] Restore checked against existing purchase records.");
#endif
    }

#if UNITY_IOS
    private void OnAppleRestoreFinished(bool success, string error)
    {
        if (success)
        {
            SyncOwnershipFromController();
        }
        else
        {
            Debug.LogWarning($"[IAPManager] Restore purchases failed: {error}");
        }
    }
#endif

    private void SyncOwnershipFromController()
    {
        Product product = storeController?.products.WithID(RemoveAdsProductId);
        if (product != null && product.hasReceipt)
        {
            MarkAdsRemoved();
        }
    }

    private void MarkAdsRemoved()
    {
        if (AdsRemoved) return;
        AdsRemoved = true;
        OnAdsRemoved?.Invoke();
        Debug.Log("[IAPManager] Ads removed.");
    }

    // ---- IStoreListener --------------------------------------------------

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        storeController = controller;
        extensionProvider = extensions;
        IsInitialized = true;

        // Reconcile the local flag with the store's receipt cache in case the
        // player purchased on another device or reinstalled the app.
        SyncOwnershipFromController();

        Debug.Log("[IAPManager] Unity IAP initialized.");
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        OnInitializeFailed(error, null);
    }

    public void OnInitializeFailed(InitializationFailureReason error, string message)
    {
        IsInitialized = false;
        Debug.LogWarning($"[IAPManager] Initialization failed: {error} {message}");
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        if (args.purchasedProduct.definition.id == RemoveAdsProductId)
        {
            MarkAdsRemoved();
        }
        return PurchaseProcessingResult.Complete;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason reason)
    {
        Debug.LogWarning($"[IAPManager] Purchase failed for '{product.definition.id}': {reason}");
    }
}

#pragma warning restore CS0618
