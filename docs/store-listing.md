# Gem Catcher — Play Store listing copy

Paste these strings directly into the Google Play Console. All character
counts are checked against Google's published limits.

---

## App name (30 chars max)

```
Gem Catcher
```
*(11 chars)*

---

## Short description (80 chars max)

Pick one:

**Option A — punchy:**
```
Catch falling gems, build streaks, dodge bombs. Free arcade game.
```
*(64 chars)*

**Option B — challenge-focused:**
```
Position. Predict. Catch. Daily challenge + endless mode arcade fun.
```
*(67 chars)*

**Option C — feature-led:**
```
Combo streaks, power-ups, daily challenges. Free offline arcade game.
```
*(68 chars)*

---

## Full description (4000 chars max)

```
Gem Catcher is a fast, satisfying arcade game about positioning a single
glass cube and predicting where falling gems will land. Sounds simple. It's not.

Each round you place your catcher, then a gem rains down — bouncing off
walls, accelerating with the score, and occasionally being a bomb you do
NOT want to catch. Build a streak of consecutive catches and your score
multiplier climbs from x1 all the way to x5. Miss a single gem and the
streak dies.

KEY FEATURES

• Pick-up-and-play controls — tap to position your catcher, then watch
  the gem fall. No timing puzzles, no twitch reactions, just pure
  prediction.

• Combo system — chain catches to push your multiplier through five tiers.
  Every miss resets it. Every catch raises the stakes.

• Special gems
   - Golden gems are worth 5x a normal catch
   - Hearts grant a bonus life
   - Bombs subtract a life if you catch them — let them fall

• Power-ups — Wider Catcher, Shield, and 2x Score drop occasionally.
  They activate on catch and persist until your next miss.

• Milestone celebrations — every 500, 1000, 2500, 5000, and 10000 points
  triggers a banner and rewards a power-up or bonus life.

• Daily challenge — every day a new deterministic seeded run with the
  same starting conditions for every player. Compare your daily best
  against yourself.

• Difficulty ramp — gems accelerate as your score climbs, shrink to half
  size at 1000 points, and your catcher shrinks at 2000 points. The game
  scales as fast as you do.

• Up to 10 lives, with bonus lives every 100 points and from heart gems.

• Built for mobile — adaptive UI fits any phone, supports notches and
  cutouts, and runs offline.

• Free to play, with an optional one-time "Remove Ads" purchase. No
  account, no data collection beyond standard ad serving.

HOW TO PLAY

1. Tap anywhere on the play field during the placement countdown to
   position your catcher.
2. The gem falls — bouncing off walls before reaching you.
3. Catch normal and golden gems. Avoid bombs. Grab hearts and power-ups.
4. Build a streak. Beat your high score.

Gem Catcher is built by Quick Slick Labs. Single developer, single
focus, no telemetry, no nonsense.
```

*(~1900 chars — well under 4000-char limit, leaves headroom for edits)*

---

## What's new (500 chars max) — first release

```
Initial release of Gem Catcher!

* Combo / streak multiplier system (x1 - x5)
* Three special gem types: Golden, Heart, Bomb
* Three power-ups: Wider Catcher, Shield, 2x Score
* Milestone rewards every 500-10000 points
* Daily challenge with deterministic seed
* Adaptive UI for notched / cutout displays
* Free to play with an optional one-time "Remove Ads" purchase
```
*(~370 chars)*

---

## Categorization (Play Console field)

- **App category:** Game
- **Game category:** Arcade
- **Tags (up to 5):** Arcade, Casual, Single player, Offline, Free

---

## Contact details

- **Website:** TODO (e.g. your GitHub repo public URL or a landing page)
- **Email:** TODO@example.com (must be a real address Google can reach)
- **Phone:** Optional, can leave blank

---

## Privacy policy URL

```
TODO https://<your-github-username>.github.io/<repo-name>/privacy-policy
```
*(See `docs/privacy-policy.md` and the Phase 2 walkthrough for hosting steps.)*

---

## Ads and Data safety declarations (required once AdMob ships)

Adding ads/IAP means two Play Console forms must be filled out accurately
before submission, or the release can be rejected or suspended later:

- **App content > Ads**: answer "Yes, my app contains ads."
- **App content > Data safety**: declare that Advertising ID and other
  identifiers are collected/shared via Google AdMob for advertising
  purposes, and that purchase data is handled by Google Play Billing.
  Use `docs/privacy-policy.md` as the source of truth for what to declare.
- App Store Connect has an equivalent **App Privacy** questionnaire —
  declare "Identifiers" and "Usage Data" linked to advertising for the
  AdMob SDK, per Apple's privacy nutrition label requirements.

See `docs/monetization-setup.md` for the full AdMob/IAP account setup steps.

---

## Asset checklist (uploaded separately in Play Console)

| Asset                  | Required size           | Status |
|------------------------|-------------------------|--------|
| App icon (high-res)    | 512 x 512 PNG, 32-bit   | Have it (export from Assets/Icons/) |
| Feature graphic        | 1024 x 500 PNG/JPG      | TODO — design needed |
| Phone screenshots      | Min 2, max 8 (16:9 or 9:16) | TODO — capture from device |
| Tablet screenshots     | Optional but recommended | TODO |
| Promo video (YouTube)  | Optional                | Skip for v1 |

### Screenshot capture command

After installing the AAB on a connected Android device:

```bash
adb shell screencap -p /sdcard/screen.png && adb pull /sdcard/screen.png ./screenshots/
```

Capture at minimum: main menu, gameplay (mid-combo), power-up active,
game over screen, daily challenge screen.
