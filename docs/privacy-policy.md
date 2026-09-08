# Privacy Policy for Gem Catcher

**Effective date:** TODO (e.g. June 1, 2026)
**Publisher:** Quick Slick Labs
**Contact:** TODO@example.com

This Privacy Policy describes how Quick Slick Labs ("we", "us") handles
information in connection with the mobile game **Gem Catcher** (the "Game").

## Summary

Gem Catcher is a single-player arcade game that can be played fully offline,
with an optional ad-supported free tier and a one-time in-app purchase to
remove ads.

- We do **not** collect, store, or transmit any personally identifiable information ourselves.
- The Game shows ads via **Google AdMob**, unless you buy the one-time "Remove Ads" purchase. AdMob may collect device and advertising identifiers to serve and measure ads — see "Advertising" below.
- The Game offers a one-time, non-consumable **"Remove Ads"** purchase, processed entirely by the Apple App Store / Google Play. We never see or store your payment details.
- Beyond AdMob and the app stores' own purchase processing, we do **not** use third-party analytics or tracking SDKs.
- We do **not** sell your data. We do not share anything beyond what AdMob and the app stores require to serve ads and process purchases.
- The Game does **not** require an account or sign-in.

## Advertising

Gem Catcher uses **Google AdMob** to show a short interstitial ad after a
game over, unless you've purchased "Remove Ads." AdMob may collect and
process data such as your advertising ID, IP address, device information,
and ad interaction data to select and measure ads, and this data is handled
under Google's own privacy policy: https://policies.google.com/privacy

You can stop all ad-related data collection at any time by purchasing
"Remove Ads" from the main menu, which disables the ads SDK entirely going
forward.

## In-app purchases

The one-time "Remove Ads" purchase ($2, non-consumable) is billed and
processed entirely by the Apple App Store or Google Play using the payment
method already on file with your Apple ID / Google account. We do not
receive, see, or store your card details — only a purchase confirmation
used to unlock the ad-free experience on your device.

## Information stored on your device

The Game saves the following data **locally on your device only**, using Android's
standard `SharedPreferences` (Unity `PlayerPrefs`). This data never leaves your device:

- Your highest score (high score)
- Your daily-challenge progress and best score for the current calendar day
- Audio mute preference and other gameplay settings
- Whether you've purchased "Remove Ads"

You can clear this data at any time by uninstalling the Game or by clearing the
app's data in Android Settings. Note that uninstalling clears the local
"Remove Ads" flag too — use the in-game "Restore Purchases" button after
reinstalling to recover it from your App Store / Play Store purchase history.

## Permissions

Gem Catcher requests the minimum Android permissions required by the Unity engine,
Google AdMob, and Unity IAP (for example, internet access to request ads and
communicate with the app store). We do **not** request access to your contacts,
location, camera, microphone, photos, storage, or any other sensitive device data.

If a future update introduces a feature that requires a new permission, this policy
will be updated and you will be asked at runtime where applicable.

## Children's privacy (COPPA / GDPR-K)

Gem Catcher is not directed at children. We do not knowingly collect personal
data from children under 13 (United States) or under 16 (European Economic
Area). If the Game's audience or store listing changes to target children,
AdMob must be configured for child-directed treatment (non-personalized
ads only) before that change ships — see `docs/monetization-setup.md`.

## Crash and diagnostic data

Google Play and the Android operating system may automatically collect crash reports
and basic device information when an application crashes. This data is collected by
Google, not by Quick Slick Labs, and is governed by Google's Privacy Policy:
https://policies.google.com/privacy

We do not enable Unity Cloud Diagnostics or any other third-party crash-reporting
service in the Game, beyond AdMob and Unity IAP described above.

## Changes to this policy

If this policy changes, the updated version will be published at the same URL with
a new "Effective date" above. Continued use of the Game after the effective date of
a revised policy constitutes acceptance of that policy.

## Contact

Questions or concerns about this policy can be sent to: **TODO@example.com**

---

*This policy is published at: TODO (your GitHub Pages URL once you enable it)*
