# Monetization setup — AdMob ads + Remove Ads IAP

This documents the **manual, account-level steps** that can't be done from
code. The code side (packages, `IAPManager.cs`, `AdsManager.cs`, the main
menu button, the game-over ad hook) is already implemented — see "How it
works" at the bottom. Everything below must be done by a human with access
to the AdMob, App Store Connect, and Google Play Console accounts.

---

## 1. Create an AdMob account and app

1. Go to https://admob.google.com and sign in with the Google account that
   owns the app.
2. **Apps > Add app.** Create two AdMob "apps" — one for Android, one for
   iOS (same game, AdMob treats platforms separately). If the app isn't
   published yet, choose "No" when asked if it's listed on a store.
3. For each platform app, note the **App ID**. Format:
   `ca-app-pub-XXXXXXXXXXXXXXXX~YYYYYYYYYY`
4. Under each app, **Ad units > Add ad unit > Interstitial**. Create one
   interstitial ad unit per platform. Note the **Ad unit ID**. Format:
   `ca-app-pub-XXXXXXXXXXXXXXXX/ZZZZZZZZZZ`

You'll end up with 4 IDs total: Android App ID, Android ad unit ID, iOS App
ID, iOS ad unit ID.

## 2. Enter the App ID in Unity

The Google Mobile Ads Unity Plugin needs the **App ID** (not the ad unit ID)
configured in `Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset`
before it will let you build — this is a separate, per-app identifier from
the ad unit IDs in step 3, and the plugin's build hook fails the build with
`[GoogleMobileAds] Android Google Mobile Ads app ID is empty` if it's blank.

This repo ships with **Google's sample App IDs** pre-filled so the project
builds out of the box during development:

```yaml
adMobAndroidAppId: ca-app-pub-3940256099942544~3347511713
adMobIOSAppId: ca-app-pub-3940256099942544~1458002511
```

These are the same well-known test publisher ID (`3940256099942544`) used
for the sample ad units in step 3 — safe to build and test with, never
associated with a real account. **Before submitting to either store**,
replace them with your real App IDs:

1. Open the project in Unity Editor.
2. Menu: **Assets > Google Mobile Ads > Settings**.
3. Paste the real Android App ID and iOS App ID (from step 1) into the
   matching fields, replacing the sample ones.
4. This overwrites `Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset`
   — commit that change.

## 3. Put the real ad unit IDs in code

Open `Assets/Scripts/AdsManager.cs` and replace the placeholder constants:

```csharp
private const string ProductionInterstitialIdAndroid = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_UNIT";
private const string ProductionInterstitialIdIOS = "ca-app-pub-REPLACE_WITH_YOUR_ID/REPLACE_WITH_YOUR_UNIT";
```

with the real ad unit IDs from step 1. Leave the test ad unit IDs above them
alone — those are Google's official sample IDs, used automatically whenever
`Debug.isDebugBuild` is true (Editor and Development Builds), so you never
need to touch them and never risk accidental invalid-traffic flags on your
own account while developing.

## 4. Create the "Remove Ads" product in each store

The product ID **must be exactly** `remove_ads` in both stores — it must
match the `RemoveAdsProductId` constant in `Assets/Scripts/IAPManager.cs`.

**Google Play Console:**
1. **Monetize > Products > In-app products > Create product.**
2. Product ID: `remove_ads`. Type: this is a one-time purchase, so use a
   *managed product* (not a subscription).
3. Set price to ~$1.99 (nearest tier to $2 in most currencies).
4. Activate the product.
5. Play Console requires a merchant account with banking/tax details set up
   under **Setup > Payments profile** before real purchases can go through —
   do this once, it's easy to miss and blocks purchases silently otherwise.

**App Store Connect:**
1. **App > In-App Purchases > Manage > Create.**
2. Type: **Non-Consumable**.
3. Product ID: `remove_ads`. Reference name: anything internal (e.g. "Remove Ads").
4. Price tier: Tier 2 ($1.99 in the US, localized automatically elsewhere).
5. Fill in the required localized display name/description, then submit for
   review (IAPs are reviewed alongside your next app binary submission).
6. Agreements, Tax, and Banking must be completed in App Store Connect
   before sandbox/production purchases will work.

## 5. Let the packages resolve

Reopen/focus the Unity Editor after `Packages/manifest.json` was updated
with `com.unity.purchasing` and `com.google.ads.mobile`. Watch for:

- Package Manager resolving the new packages plus their dependencies
  (`com.google.external-dependency-manager`, `com.unity.services.core`,
  etc.) — this can take a few minutes and may prompt Android resolver
  popups from EDM4U on first import.
- **Possible Unity Gaming Services project-linking prompt.** `com.unity.services.core`
  is a dependency of Unity IAP. If the Editor prompts you to link a Unity
  Cloud project, it's safe to do (free) — the classic IAP API used here
  doesn't depend on any cloud catalog/Remote Config feature, but the
  package may still want a project link for its own initialization.
- Check the **Console** window for compile errors. This code was written
  and manually reviewed carefully, but could not be compiled this session —
  report back anything you see here.

## 6. Testing before you ship

- **Ads:** Development Builds and the Editor automatically use Google's
  test ad unit IDs, so you'll see a "Test Ad" banner — this confirms the
  wiring works without touching your real AdMob account. Never tap your
  own production ads to "test" them; Google can permanently ban an AdMob
  account for self-clicks/invalid traffic.
- **Purchases (Android):** add your Google account as a **License Tester**
  under Play Console > **Setup > License testing**, then install via an
  Internal Testing track release — test purchases won't charge real money.
- **Purchases (iOS):** use a **Sandbox Tester** Apple ID (App Store Connect
  > Users and Access > Sandbox) signed into the device's App Store sandbox
  account, then run a Development build.
- Verify: the "Remove Ads" button appears on the main menu, completing a
  test purchase hides it immediately, "Restore Purchases" in Settings
  recovers the flag on a fresh install, and no interstitial appears once
  ads are removed.

---

## How it works (code reference)

- `Assets/Scripts/IAPManager.cs` — Unity IAP wrapper. Non-consumable
  product `remove_ads`. Persists the purchase via `PlayerPrefs` so it
  survives without a network call once bought; `RestorePurchases()`
  reconciles it from the store on demand (App Store restore flow on iOS,
  automatic receipt re-validation elsewhere).
- `Assets/Scripts/AdsManager.cs` — AdMob interstitial wrapper. No-ops
  entirely if `IAPManager.AdsRemoved` is true. Preloads one interstitial at
  startup and after every show/failure so one is ready when needed.
- `Assets/Scripts/UIManager.cs` — adds the "Remove Ads - $2" main menu
  button (auto-hidden once purchased) and a "Restore Purchases" row in
  Settings. The interstitial is shown when the player taps **Try Again** or
  **Main Menu** from the game-over screen (not at the instant of death, so
  the score reveal isn't interrupted), then the scene loads once the ad
  closes or fails to show.
