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
| 30+ | 5 | 100 | 1.70 cap |

- Missed gems do not break the Rush streak.
- Shielded hits preserve it; unprotected rock hits reset it.
- Hearts and power-up pickups neither increment nor reset it.
- Magnet catches and gems created by the probability drive count normally.
- Rocks destroyed during invincibility retain their fixed 50-point reward and
  do not increment the gem combo.
- Ad revival and new runs start at zero streak / 1x.
- The HUD shows the multiplier and catches toward the next threshold; its bar
  fills within the current tier. At 30+ it displays GEM RUSH and a full bar.
- Score popups interpolate white/cyan, blue/purple, magenta/gold, then gold.
  Pitch follows the streak, not the level's base gem value.
- Tier increases pop the HUD and emit a brief banner/burst. Gem Rush adds a
  short gold catcher trail and an additional small gold catch burst.
- Dice is enabled in Cave as well as later levels. It becomes pending after
  60 seconds of normal Rush play (after the first-run tutorial), then every
  90 seconds; it replaces an eligible gem slot after earlier pending drops.
  Its existing 10-second rock/gem swap behavior is unchanged.

## Play-mode/device regression checklist

Not yet executed in this environment:

1. In Cave, check catch numbers 4/5, 9/10, 19/20, 29/30: ordinary green gems
   must award 20/30, 30/40, 40/60, 60/100 respectively. The tier-up catch's
   popup, HUD, and pitch must all use the newly reached tier.
2. Catch gems beyond 30: multiplier stays 5x and pitch stays 1.70. Check
   scoring with higher-value gems in Jungle, Space, and Bay Lookout.
3. Miss a gem; catch a heart/pickup; take a shielded hit: none should change
   streak count. Magnet and swapped gems should increment it once per catch.
4. Hit an unprotected rock: streak returns to zero, next gem awards base
   points at pitch 1.00, and the gold trail disappears. Grace-period contacts
   must not award gem points or advance the streak.
5. Die, revive via the one-per-run ad, then restart: both must begin at 1x.
   Check the meter hides behind menus/game-over and returns after revival.
6. Verify text contrast and meter placement on small portrait screens, and
   listen to pitch transitions. Pause/resume must not leave a trail artifact.
7. In Cave, wait past 60 seconds after the tutorial and catch the dice drop.
   Confirm the next rows swap for 10 seconds, then return to normal.
8. Playtest time to level unlocks and cosmetic purchases: combo points now
   count toward the existing thresholds, which have not been retuned.
