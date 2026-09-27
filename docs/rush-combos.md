# Rush combo rules

Rush counts scoring gem catches since the last unprotected hazard hit. The catch
that reaches a tier immediately earns that tier's multiplier. The existing
Normal/Daily multiplier ladder remains unchanged.

| Catches | Multiplier | Base 20-point gem | Catch pitch |
| --- | --- | --- | --- |
| 1–4 | 1 | 20 | 1.00–1.09 |
| 5–9 | 1.5 | 30 | 1.12–1.24 |
| 10–19 | 2 | 40 | 1.26–1.44 |
| 20–29 | 3 | 60 | 1.47–1.65 |
| 30 (held during final charge) | 5 | 100 | 1.70 cap |

- Missed gems do not break the Rush streak.
- Shielded hits preserve it; unprotected rock hits reset it.
- Hearts and power-up pickups neither increment nor reset it.
- Magnet catches and gems created by the probability drive count normally.
- Rocks destroyed during invincibility retain their fixed 50-point reward and
  do not increment the gem combo.
- Ad revival and new runs start at zero streak / 1x.
- The HUD shows the multiplier and catches toward the next threshold; its bar
  fills within the current tier. At 30 it displays GEM RUSH and a full bar until the final charge expires.
- Score popups interpolate white/cyan, blue/purple, magenta/gold, then gold.
  Pitch follows the streak, not the level's base gem value.
- Tier increases pop the HUD and emit a banner/burst plus lightning around
  Catchy. The gold trail has been removed.
- Lightning tiers at 5/10/20/30 catches last 5/8/11/14 seconds. Each upgrade
  replaces the current timer rather than stacking durations. Collection ranges
  are 0.75/1/1.25/1.5 column widths; zap intervals are 0.48/0.40/0.32/0.24 seconds.
- Bolts reuse LightningSpawnEffect and Audio/LightningZap, with progressively
  larger arcs around Catchy. Zaps collect the nearest scoring gem in range;
  hazards, hearts and power-ups are excluded. Remote catches award normal
  score and count toward combos, but each gem is retired only once.
- Charge time freezes during pauses. Unprotected hits cancel it. Completion,
  game over and scene changes stop it. Final-tier count holds at 30 without
  extending the timer; expiry resets only the combo to zero / x1 (not score
  or lives), without showing a lost-streak message.
- Dice is enabled in Cave as well as later levels. It becomes pending after
  60 seconds of normal Rush play (after the first-run tutorial), then every
  90 seconds; it replaces an eligible gem slot after earlier pending drops.
  Its existing 10-second rock/gem swap behavior is unchanged.

## Play-mode/device regression checklist

Not yet executed in this environment:

1. In Cave, check catch numbers 4/5, 9/10, 19/20, 29/30: ordinary green gems
   must award 20/30, 30/40, 40/60, 60/100 respectively. The tier-up catch's
   popup, HUD, and pitch must all use the newly reached tier.
2. Catch gems beyond 30 during the 14-second final charge: multiplier stays
   5x and pitch stays 1.70. After expiry, the next catch starts at 1x. Check
   scoring with higher-value gems in Jungle, Space, and Bay Lookout.
3. Miss a gem; catch a heart/pickup; take a shielded hit: none should change
   streak count. Magnet and swapped gems should increment it once per catch.
4. Hit an unprotected rock: streak returns to zero, next gem awards base
   points at pitch 1.00, and the lightning charge disappears. Grace-period contacts
   must not award gem points or advance the streak.
5. Die, revive via the one-per-run ad, then restart: both must begin at 1x.
   Check the meter hides behind menus/game-over and returns after revival.
6. Verify text contrast and meter placement on small portrait screens, and
   listen to pitch transitions. Pause/resume must preserve the remaining charge time.
7. In Cave, wait past 60 seconds after the tutorial and catch the dice drop.
   Confirm the next rows swap for 10 seconds, then return to normal.
8. Playtest time to level unlocks and cosmetic purchases: combo points now
   count toward the existing thresholds, which have not been retuned.

## Automated progression checks

In Unity outside Play Mode, run Quick Slick Labs → Validation → Check Combo
Progression (or batchmode `-executeMethod ComboProgressionChecks.Run`). This
checks the actual combo tier events, final-tier cap, natural reset versus a
broken streak, retriggering a new cycle, charge durations/range growth, and
unchanged legacy tiers. These checks have been added but not executed here.

Also verify on device: simultaneous contact and zap cannot credit a gem twice;
rocks/shield/dice/heart/master-gem drops cannot be targeted; SFX volume zero
mutes zaps; charge strength visibly grows; and pausing during charge neither
collects gems nor consumes duration. Range and zap pacing need playtest tuning.
