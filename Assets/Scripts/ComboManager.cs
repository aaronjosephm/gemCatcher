using UnityEngine;

/// <summary>
/// Tracks gem catches without an unprotected hit. Rush ignores missed gems;
/// pickups are neutral. Presentation and scoring share the same tier state.
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

  private static readonly Tier[] legacyTiers = new[]
  {
    new Tier(0, 1f),
    new Tier(3, 1.5f),
    new Tier(5, 2f),
    new Tier(7, 3f),
    new Tier(10, 5f),
  };

  private static readonly Tier[] rushTiers = new[]
  {
    new Tier(0, 1f), new Tier(5, 1.5f), new Tier(10, 2f),
    new Tier(20, 3f), new Tier(30, 5f),
  };

  private static Tier[] Tiers => GameState.Mode == GameState.GameMode.Rush
      ? rushTiers : legacyTiers;

  public static bool IsGemRush => GameState.Mode == GameState.GameMode.Rush
      && CurrentCombo >= 30;

  public static int NextThreshold
  {
    get
    {
      foreach (Tier tier in Tiers)
        if (tier.threshold > currentCombo) return tier.threshold;
      return 0; // Capped.
    }
  }

  public static float TierProgress
  {
    get
    {
      int previous = 0;
      foreach (Tier tier in Tiers)
      {
        if (tier.threshold > currentCombo)
          return (currentCombo - previous) / (float)(tier.threshold - previous);
        previous = tier.threshold;
      }
      return 1f;
    }
  }

  // Small per-catch rises within each tier, with a distinct step at tier-up.
  public static float CatchPitch
  {
    get
    {
      int n = currentCombo;
      if (n < 5) return Mathf.Lerp(1f, 1.09f, Mathf.Clamp01((n - 1) / 3f));
      if (n < 10) return Mathf.Lerp(1.12f, 1.24f, (n - 5) / 4f);
      if (n < 20) return Mathf.Lerp(1.26f, 1.44f, (n - 10) / 9f);
      if (n < 30) return Mathf.Lerp(1.47f, 1.65f, (n - 20) / 9f);
      return 1.70f;
    }
  }

  public static Color CatchColor
  {
    get
    {
      int n = currentCombo;
      if (n < 5) return Color.Lerp(Color.white, new Color(0.65f, 1f, 1f),
          Mathf.Clamp01((n - 1) / 3f));
      if (n < 10) return Color.Lerp(new Color(0.45f, 1f, 1f),
          new Color(0.2f, 0.8f, 1f), (n - 5) / 4f);
      if (n < 20) return Color.Lerp(new Color(0.35f, 0.65f, 1f),
          new Color(0.8f, 0.45f, 1f), (n - 10) / 9f);
      if (n < 30) return Color.Lerp(new Color(1f, 0.4f, 0.85f),
          new Color(1f, 0.8f, 0.25f), (n - 20) / 9f);
      return new Color(1f, 0.85f, 0.25f);
    }
  }

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
  /// <summary>True once the multiplier is above 1× (threshold depends on mode).</summary>
  public static bool MultiplierActive => CurrentMultiplier > 1f;

  /// <summary>
  /// Returns the multiplier that WILL apply to the next catch. Useful if the
  /// caller wants the multiplier to apply to the catch that triggers the
  /// tier-up rather than waiting until the catch after.
  /// </summary>
  public static float MultiplierForNextCatch => MultiplierForCount(currentCombo + 1);

  private static float MultiplierForCount(int count)
  {
    float result = Tiers[0].multiplier;
    for (int i = 0; i < Tiers.Length; i++)
    {
      if (count >= Tiers[i].threshold) result = Tiers[i].multiplier;
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
