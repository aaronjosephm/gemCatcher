using UnityEngine;

/// <summary>
/// The power-ups currently in the game. Used as a key for active state,
/// HUD slots, and pickup spawning.
///
/// <para>Persistent power-ups (WiderCatcher / Shield / DoubleScore) latch on
/// catch and clear on the next unshielded miss; they show a HUD slot while
/// active.</para>
///
/// <para>Instant-effect power-ups (ExtraLife) apply their effect immediately on
/// catch and have no persistent state — they don't appear in the HUD slot
/// list, can't be revoked, and grant their reward through the existing
/// <see cref="GemCatcher"/> life path so the bonus-life banner / lives-counter
/// pop is reused.</para>
/// </summary>
public enum PowerUpType
{
  WiderCatcher,
  Shield,
  DoubleScore,
  /// <summary>
  /// Awards <see cref="PowerUpManager.ExtraLifeAwardCount"/> lives on catch,
  /// capped at <see cref="GemCatcher.MAX_LIVES"/>. Routes through
  /// <see cref="GemCatcher.AddLives"/> so the existing "EXTRA LIVES!"
  /// banner + lives-counter scale pop fire automatically.
  /// </summary>
  ExtraLife,
}

/// <summary>
/// Central tracker for active power-ups. Singleton MonoBehaviour, auto-
/// bootstrapped on scene load (no scene setup required). All gameplay code
/// reads multipliers / flags via the static accessors below; activation goes
/// through <see cref="Activate"/> from the pickup component.
///
/// Lifecycle: power-ups are <b>persistent</b> once collected — they stay active
/// indefinitely while the player keeps catching gems. The first uncaught gem
/// after pickup revokes <i>every</i> active power-up at once
/// (see <see cref="RevokeAllOnMiss"/>), unless the player is holding a Shield,
/// in which case the shield is consumed and everything else stays active.
///
/// Events:
///   <see cref="OnActivated"/> — fires on Activate(); UI shows banner + slot.
///   <see cref="OnExpired"/> — fires when a power-up is revoked; UI hides slot,
///     CatcherManager resets the wider-catcher scale.
///   <see cref="OnShieldConsumed"/> — fires when a miss is absorbed by the
///     shield; UI shows a "SHIELDED!" floating text instead of the usual miss FX.
/// </summary>
public class PowerUpManager : MonoBehaviour
{
  // ---- Tunable strengths --------------------------------------------------

  public const float WiderCatcherWidthFactor = 1.6f;
  public const int DoubleScoreMultiplierValue = 2;
  /// <summary>
  /// Lives awarded by a single <see cref="PowerUpType.ExtraLife"/> catch.
  /// Routes through <see cref="GemCatcher.AddLives"/>, which clamps the
  /// total at <see cref="GemCatcher.MAX_LIVES"/>; if the player is near
  /// the cap, the banner reports the actual count granted (1, 2, or 3)
  /// rather than the requested amount.
  /// </summary>
  public const int ExtraLifeAwardCount = 3;

  public static PowerUpManager Instance { get; private set; }

  // ---- Active state -------------------------------------------------------
  // Bools instead of timers — power-ups don't tick down; they latch on
  // activate and clear on the first unshielded miss.

  private static bool widerCatcherActive;
  private static bool doubleScoreActive;
  private static int shieldCharges;

  // ---- Events -------------------------------------------------------------

  public delegate void PowerUpActivatedDelegate(PowerUpType type, float duration);
  public static event PowerUpActivatedDelegate OnActivated;

  public delegate void PowerUpExpiredDelegate(PowerUpType type);
  public static event PowerUpExpiredDelegate OnExpired;

  public delegate void ShieldConsumedDelegate(Vector3 worldPosition);
  public static event ShieldConsumedDelegate OnShieldConsumed;

  // ---- Public accessors (read-only) ---------------------------------------

  public static float WiderCatcherFactor =>
      widerCatcherActive ? WiderCatcherWidthFactor : 1f;
  public static bool WiderCatcherActive => widerCatcherActive;

  public static int DoubleScoreMultiplier =>
      doubleScoreActive ? DoubleScoreMultiplierValue : 1;
  public static bool DoubleScoreActive => doubleScoreActive;

  public static bool HasShield => shieldCharges > 0;
  public static int ShieldCharges => shieldCharges;

  // ---- API ----------------------------------------------------------------

  /// <summary>
  /// Apply the given power-up. Subsequent pickups of the same type are no-ops
  /// (the effect is already on). Shields are single-charge and refresh.
  /// </summary>
  public static void Activate(PowerUpType type)
  {
    switch (type)
    {
      case PowerUpType.WiderCatcher:
        widerCatcherActive = true;
        OnActivated?.Invoke(type, 0f);
        break;
      case PowerUpType.DoubleScore:
        doubleScoreActive = true;
        OnActivated?.Invoke(type, 0f);
        break;
      case PowerUpType.Shield:
        shieldCharges = 1;
        OnActivated?.Invoke(type, 0f);
        break;
      case PowerUpType.ExtraLife:
        // Instant-effect: route through AddLives so the cap, the
        // OnLivesChanged event, and the "EXTRA LIVES!" banner / lives-counter
        // scale pop all fire through the same path the per-100-points bonus
        // life uses. No HUD slot — this power-up has no persistent state.
        // We deliberately don't fire OnActivated so UIManager doesn't try to
        // open a slot for it; the BonusLife banner is the player-facing
        // confirmation instead. AddLives reports the count actually granted
        // (1, 2, or 3 depending on how close the player is to MAX_LIVES) so
        // a player at 9/10 lives sees "EXTRA LIFE +1 ♥" rather than
        // "EXTRA LIVES +3" — the banner stays accurate to what was awarded.
        GemCatcher.AddLives(ExtraLifeAwardCount);
        break;
    }
  }

  /// <summary>
  /// Try to absorb a miss with the shield. Returns true if a charge was
  /// consumed (caller should skip the normal miss penalty AND skip the
  /// power-up revocation — shield protects every other active effect).
  /// </summary>
  public static bool TryConsumeShield(Vector3 worldPosition)
  {
    if (shieldCharges <= 0) return false;
    shieldCharges--;
    OnShieldConsumed?.Invoke(worldPosition);
    if (shieldCharges <= 0)
    {
      OnExpired?.Invoke(PowerUpType.Shield);
    }
    return true;
  }

  /// <summary>
  /// Revoke every active power-up. Called from GemCatcher when an unshielded
  /// miss happens — one missed gem clears the entire stack.
  /// </summary>
  public static void RevokeAllOnMiss()
  {
    if (widerCatcherActive)
    {
      widerCatcherActive = false;
      OnExpired?.Invoke(PowerUpType.WiderCatcher);
    }
    if (doubleScoreActive)
    {
      doubleScoreActive = false;
      OnExpired?.Invoke(PowerUpType.DoubleScore);
    }
    if (shieldCharges > 0)
    {
      shieldCharges = 0;
      OnExpired?.Invoke(PowerUpType.Shield);
    }
  }

  /// <summary>
  /// Wipe all state silently (no events). Used at round start so a fresh round
  /// never inherits power-ups from a previous one — PowerUpManager survives
  /// scene reloads via DontDestroyOnLoad.
  /// </summary>
  public static void ClearAll()
  {
    widerCatcherActive = false;
    doubleScoreActive = false;
    shieldCharges = 0;
  }

  // ---- Bootstrap ----------------------------------------------------------

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void EnsureInstance()
  {
    if (Instance != null) return;
    if (FindObjectOfType<PowerUpManager>() != null) return;

    GameObject go = new GameObject("PowerUpManager (auto)");
    go.AddComponent<PowerUpManager>();
  }

  // Wipe static state on Play Mode entry so a leftover flag from a previous
  // editor session doesn't carry over into the first run.
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStaticState()
  {
    widerCatcherActive = false;
    doubleScoreActive = false;
    shieldCharges = 0;
    OnActivated = null;
    OnExpired = null;
    OnShieldConsumed = null;
    Instance = null;
  }

  void Awake()
  {
    if (Instance != null && Instance != this)
    {
      Destroy(gameObject);
      return;
    }
    Instance = this;
    DontDestroyOnLoad(gameObject);

    GemCatcher.OnGameOver += HandleGameOver;
  }

  void HandleGameOver()
  {
    // On game over, expire everything and notify the UI / catcher so the HUD
    // and catcher width return to their default state on the post-game panel.
    RevokeAllOnMiss();
  }

  void OnDestroy()
  {
    if (Instance == this)
    {
      GemCatcher.OnGameOver -= HandleGameOver;
      Instance = null;
    }
  }
}
