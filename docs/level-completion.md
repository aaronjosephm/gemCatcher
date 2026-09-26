# Level completion and feedback

Crossing the finish line pauses Rush and celebrates with screen-wide confetti.
The modal offers Next level or Keep playing this level. The latter resumes the
same score, lives, combo, power-ups and rewarded-revival allowance, without an
ad or another completion prompt in that run. Next level banks earned points,
requests an interstitial and starts a fresh run in the next environment.

Level-transition ads are a separate placement from retry/menu ads: they bypass
the completed-run count and cooldown gates, but respect consent, Remove Ads,
and ad readiness. An unavailable or failed ad immediately continues the
transition. Existing development-build test-ID selection is unchanged.

The meter below the combo HUD shows the current run score against its target:
Crystal Cave 10,000; Jungle Falls 25,000; Deep Space 50,000. At the target it
asks the player to cross the falling finish line. After crossing it turns gold
and says COMPLETE / BONUS PLAY. Unlocked levels retain their completion targets
on replay. Bay Lookout is currently the final, endless level and displays
FINAL LEVEL / ENDLESS rather than an invented completion threshold.

Gem Rush's trail uses an explicitly bundled URP vertex-color shader, starts
in front of the catcher's collider, and is wider/brighter with a 0.38-second
fade. It still requires 30 consecutive catches and sideways movement to see.

## Validation

C# syntax parsing and git diff whitespace checks were run in the editing
environment. Unity and Android runtime tests were not available.

## Device / Unity regression checks (not yet run)

- On a development build, get 30 consecutive catches and move in both
  directions. Confirm a gold trail on every background and with cosmetics,
  shield and invincibility. Hit an unprotected rock: the trail must disappear.
- Watch score and combo HUD plus all active power-up indicators on a narrow
  phone. Check the new meter's legibility, margins and lack of overlap.
- Reach each score target; the meter fills and the finish line falls. Crossing
  it opens the modal once, freezes hazards/input/catch checks and rains
  confetti across the screen. Confetti must not block either button.
- Keep playing: verify unchanged lives, combo, score and power-up duration;
  the combo HUD returns immediately and no ad or second prompt appears.
  A later death still uses the existing one-revival-per-run rule.
- Next level: with a loaded test ad, verify it closes before the new level
  starts, with zero score/combo and three lives. Rapid repeated taps must not
  cause extra ads or scene loads. Test no fill, offline and show failure.
- Verify points are credited only once at completion, with only additional
  points credited at later deaths or when advancing after a rewarded revival.
- Background/resume on the modal and during the test ad; no pause overlay
  should cover the completion choice or automatically resume the old run.
- Replay an unlocked level: the finish line and both choices still appear.
- Start Bay Lookout: the meter says FINAL LEVEL / ENDLESS and does not offer
  a nonexistent next level. Normal/Daily/Tutorial retain their existing UI.
