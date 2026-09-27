# Finite level completion and HUD

Rush ends immediately at its score goal: Crystal Cave 10,000; Jungle Falls
25,000; Deep Space 50,000; Bay Lookout 100,000. The final goal is a provisional
balance choice now that endless play is deferred. Scores cap at the goal.
Replays have the same goal, including already-unlocked levels.

The score counts up at the top center without a Score label, above a slim
green progress bar. Hearts appear without a Lives label. Combo progress stays
below and to the right, with a remaining charge-time label during lightning.

At completion the game freezes, banks points once, unlocks the next level and
shows confetti with a rounded gold card and blue/cyan border. Typography uses
warm brown outlines to echo UI/ContinueOfferPanel.png. No gameplay or menu
music plays on this panel. There is no Keep playing action.

Next level requests an interstitial and starts a fresh run after dismissal.
Main menu returns to the menu, where the completed level can be replayed.
The final level offers Main menu only. The transition curtain becomes opaque
before the ad opens; destination state changes and loading occur behind that
same cover, which remains until the destination renders. Missing/failed ads
continue immediately. Consent, Remove Ads and test-ID selection are unchanged.

## Validation

C# syntax parsing and git diff whitespace checks run in the editing environment.
Unity compilation, runtime and Android visual/ad tests are not available here.

## Device / Unity checks (not yet run)

- On narrow/tall portrait phones, verify the centered score and green meter
  clear the hearts and combo HUD, including the combo's scale pop animation.
- Reach the goal by contact, lightning, a heart at full lives, and invincible
  rock collection. Confirm the score caps, one popup opens, no subsequent
  hazard can hurt Catchy, and there is no Keep playing option.
- Confirm gold/blue panel contrast, button hit areas, full-screen confetti,
  and silence from BOTH menu and gameplay music. Background and return to the
  app: the completion panel must remain frozen without a Resume overlay.
- Next level with a loaded test interstitial: no old-background frame on
  dismissal; new level starts at zero score/combo with three lives. Also test
  no fill, offline, show failure, and repeated Next level taps.
- Main menu: points credited once; replay the same level from zero. Complete
  every level, including the last: it must end rather than become endless.
- After a prior rewarded revival, reaching the goal credits only points not
  already banked. A new level gets its own one-revival allowance.
