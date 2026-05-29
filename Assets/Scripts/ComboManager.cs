using UnityEngine;

/// <summary>
/// Tracks the player's current consecutive-catch streak and the score multiplier
/// that comes with it. Static, scene-agnostic, hooked into GemCatcher events
/// from the bootstrap callback below.
///
/// Multiplier ramp:
///     0 catches  →  ×1   (idle)
///     1-2        →  ×1   (warming up)
///     3-4        →  ×1.5
///     5-6        →  ×2
///     7-9        →  ×3
///     10+        →  ×5   (capped)
///
/// A normal miss (unshielded) breaks the combo. Catching a bomb also breaks it.
/// Catching a power-up pickup is neutral — combo is unchanged. Shield-absorbed
/// misses do NOT break the combo (the shield protected the chain).
/// </summary>
public static class ComboManager
{
  /// <summary>
  /// Multiplier tier definition. The first entry must be at threshold 0.
  /// </summary>
  public readonly struct Tier
  {
    public readonly int threshold;
    public readonly float multiplier;
    public Tier(int threshold, float multiplier)
    {
      this.threshold = threshold;
      this.multiplier = multiplier;
    }
  }

  private static readonly Tier[] tiers = new[]
  {
    new Tier(0, 1f),
    new Tier(3, 1.5f),
    new Tier(5, 2f),
    new Tier(7, 3f),
    new Tier(10, 5f),
  };

  // ---- State -------------------------------------------------------------

  private static int currentCombo = 0;

  // ---- Events ------------------------------------------------------------

  /// <summary>
  /// Fires every time the combo count changes (catch, break, reset). Subscribers
  /// (UIManager) read CurrentCombo / CurrentMultiplier off the manager.
  /// </summary>
  public static event System.Action<int, float> OnComboChanged;

  /// <summary>
  /// Fires when the combo multiplier tier increases (e.g. ×1 → ×1.5). UIManager
  /// uses this for a celebratory "×2 STREAK!" pulse — separate from OnComboChanged
  /// so the pulse doesn't fire on every single catch.
  /// </summary>
  public static event System.Action<int, float> OnComboTierUp;

  /// <summary>
  /// Fires when the combo is forcibly reset to zero. The two parameters are
  /// the (combo, multiplier) values JUST BEFORE the break, so the UI can show
  /// the player what they lost ("STREAK LOST: ×3").
  /// </summary>
  public static event System.Action<int, float> OnComboBroken;

  // ---- Accessors ---------------------------------------------------------

  public static int CurrentCombo => currentCombo;
  public static float CurrentMultiplier => MultiplierForCount(currentCombo);
  /// <summary>True once the multiplier is above 1× (i.e. combo ≥ 3).</summary>
  public static bool MultiplierActive => CurrentMultiplier > 1f;

  /// <summary>
  /// Returns the multiplier that WILL apply to the next catch. Useful if the
  /// caller wants the multiplier to apply to the catch that triggers the
  /// tier-up rather than waiting until the catch after.
  /// </summary>
  public static float MultiplierForNextCatch => MultiplierForCount(currentCombo + 1);

  private static float MultiplierForCount(int count)
  {
    float result = tiers[0].multiplier;
    for (int i = 0; i < tiers.Length; i++)
    {
      if (count >= tiers[i].threshold) result = tiers[i].multiplier;
      else break;
    }
    return result;
  }

  // ---- API ---------------------------------------------------------------

  /// <summary>
  /// Advance the combo by one. Fires OnComboChanged, plus OnComboTierUp when
  /// the multiplier tier increases. GemCatcher calls this from its catch path.
  /// </summary>
  public static void RegisterCatch()
  {
    float prevMult = CurrentMultiplier;
    currentCombo++;
    float newMult = CurrentMultiplier;

    OnComboChanged?.Invoke(currentCombo, newMult);
    if (newMult > prevMult)
    {
      OnComboTierUp?.Invoke(currentCombo, newMult);
    }
  }

  /// <summary>
  /// Reset the combo to zero. Fires OnComboBroken (only if there was a streak
  /// to break) and OnComboChanged.
  /// </summary>
  public static void Break()
  {
    if (currentCombo == 0) return;
    int lost = currentCombo;
    float lostMult = CurrentMultiplier;
    currentCombo = 0;
    OnComboBroken?.Invoke(lost, lostMult);
    OnComboChanged?.Invoke(0, 1f);
  }

  /// <summary>
  /// Wipe combo state silently. Used at round start so the new round always
  /// begins at zero combo without animating a "broken" flash.
  /// </summary>
  public static void ClearSilently()
  {
    currentCombo = 0;
  }

  // ---- Bootstrap ---------------------------------------------------------
  // Wipe static state on Play Mode entry so leftover events / counts from a
  // previous editor session don't carry over. Mirrors PowerUpManager.

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStaticState()
  {
    currentCombo = 0;
    OnComboChanged = null;
    OnComboTierUp = null;
    OnComboBroken = null;
  }
}
