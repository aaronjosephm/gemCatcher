using UnityEngine;

/// <summary>
/// Static metadata helpers for the power-up pickup system. Used to be a
/// MonoBehaviour that owned a procedural sphere pickup; pickups now ride on
/// the regular gem prefabs (each power-up has a designated prefab + tint +
/// fiery particle effect — see <see cref="ObjectPooler.TrySpawnPowerUp"/>),
/// so the only thing left here is the type→visual lookup tables that the HUD
/// + the spawner share.
///
/// Keeping the class name <c>PowerUpPickup</c> for backwards compatibility
/// with <see cref="UIManager"/> call sites (PowerUpPickup.ColorForType /
/// LabelForType) — renaming would churn references for no functional gain.
/// </summary>
public static class PowerUpPickup
{
  /// <summary>
  /// Theme color for a power-up type. Used by:
  ///   • the HUD slot tint (active power-up indicators);
  ///   • the gem albedo / emission override applied at spawn time;
  ///   • the catch-burst / fire particle tint;
  ///   • banner notifications fired on activation.
  /// </summary>
  public static Color ColorForType(PowerUpType type)
  {
    switch (type)
    {
      case PowerUpType.WiderCatcher: return new Color(0.40f, 0.85f, 1.00f); // sky blue
      case PowerUpType.Shield:       return new Color(1.00f, 0.85f, 0.35f); // warm yellow
      case PowerUpType.DoubleScore:  return new Color(0.45f, 1.00f, 0.55f); // bright green
      case PowerUpType.ExtraLife:    return new Color(1.00f, 0.30f, 0.90f); // hot magenta
      case PowerUpType.Swap:         return new Color(0.20f, 0.50f, 1.00f); // blue
      case PowerUpType.Invincibility:return new Color(1.00f, 0.85f, 0.20f); // gold
      default:                       return Color.white;
    }
  }

  /// <summary>Short label shown in the HUD slot for this power-up.</summary>
  public static string LabelForType(PowerUpType type)
  {
    switch (type)
    {
      case PowerUpType.WiderCatcher: return "WIDE";
      case PowerUpType.Shield:       return "SHIELD";
      case PowerUpType.DoubleScore:  return "2\u00d7 SCORE";
      // Mirrors PowerUpManager.ExtraLifeAwardCount so the test-mode
      // "NEXT: …" preview overlay matches what the player will actually
      // receive. Not shown in the in-game HUD slot list (ExtraLife has no
      // slot — it's instant-effect) so this is dev-facing only.
      case PowerUpType.ExtraLife:    return "+" + PowerUpManager.ExtraLifeAwardCount + " \u2665";
      case PowerUpType.Swap:         return "SWAP";
      case PowerUpType.Invincibility:return "INVINCIBLE";
      default:                       return "";
    }
  }

  /// <summary>
  /// Prefab-name prefix that this power-up should ride on. Matched against
  /// <see cref="ObjectPooler.objectPrefabs"/> by name prefix (pool clones are
  /// "&lt;Prefab&gt;(Clone)") so the spawner can pull the correct gem mesh
  /// out of the existing pool — no separate power-up pool needed.
  ///
  /// <para>The mapping is intentionally locked here in code (not Inspector-
  /// wired) because the design is: <i>a heart shape always means a life</i>,
  /// <i>a star shape always means wider catch</i>, etc. — moving this to the
  /// Inspector would let a designer accidentally break that contract.</para>
  /// </summary>
  public static string GemPrefabNameForType(PowerUpType type)
  {
    switch (type)
    {
      case PowerUpType.DoubleScore:  return "GreenVolcom";
      case PowerUpType.WiderCatcher: return "StarGem";
      case PowerUpType.Shield:       return "TopazGem";
      case PowerUpType.ExtraLife:    return "HeartGem";
      default:                       return null;
    }
  }
}
