using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Small regression suite runnable from the menu or -executeMethod.</summary>
public static class ComboProgressionChecks
{
    [MenuItem("Quick Slick Labs/Validation/Check Combo Progression")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Run these checks outside Play Mode.");
        GameState.GameMode savedMode = GameState.Mode;
        int savedCombo = ComboManager.CurrentCombo;
        var tiers = new List<int>();
        int broken = 0;
        int resets = 0;
        Action<int, float> onTier = (count, multiplier) => tiers.Add(count);
        Action<int, float> onBroken = (count, multiplier) => broken++;
        Action<int, float> onChange = (count, multiplier) => { if (count == 0) resets++; };
        ComboManager.OnComboTierUp += onTier;
        ComboManager.OnComboBroken += onBroken;
        ComboManager.OnComboChanged += onChange;
        try
        {
            GameState.Mode = GameState.GameMode.Rush;
            ComboManager.ClearSilently();
            for (int i = 0; i < 100; i++) ComboManager.RegisterCatch();
            Require(string.Join(",", tiers) == "5,10,20,30", "Each Rush tier must fire once.");
            Require(ComboManager.CurrentCombo == 30 && ComboManager.CurrentMultiplier == 5f,
                "Final charge must hold at 30 / x5 without retriggering.");
            ComboManager.CompleteChargeCycle();
            Require(ComboManager.CurrentCombo == 0 && ComboManager.CurrentMultiplier == 1f,
                "Charge expiry must return to x1.");
            Require(broken == 0 && resets == 1, "Successful expiry must refresh HUD without a lost-streak event.");
            tiers.Clear();
            for (int i = 0; i < 5; i++) ComboManager.RegisterCatch();
            Require(tiers.Count == 1 && tiers[0] == 5, "A new cycle must earn lightning again.");
            ComboManager.Break();
            Require(broken == 1 && ComboManager.CurrentCombo == 0, "A rock hit must break the streak.");
            ComboManager.Break();
            Require(broken == 1, "Repeated break must not duplicate cancellation.");
            for (int tier = 1; tier <= 4; tier++)
            {
                Require(ComboLightning.DurationForTier(tier) == new[] { 5f, 8f, 11f, 14f }[tier - 1],
                    "Charge durations must be 5/8/11/14 seconds.");
                if (tier > 1) Require(ComboLightning.RangeInColumns(tier) > ComboLightning.RangeInColumns(tier - 1),
                    "Each charge must increase collection range.");
            }
            GameState.Mode = GameState.GameMode.Normal;
            ComboManager.ClearSilently();
            tiers.Clear();
            for (int i = 0; i < 35; i++) ComboManager.RegisterCatch();
            Require(string.Join(",", tiers) == "3,5,7,10" && ComboManager.CurrentCombo == 35,
                "Legacy modes must retain their old tiers and uncapped count.");
            Debug.Log("Combo progression regression checks passed.");
        }
        finally
        {
            ComboManager.OnComboTierUp -= onTier;
            ComboManager.OnComboBroken -= onBroken;
            ComboManager.OnComboChanged -= onChange;
            GameState.Mode = savedMode;
            ComboManager.ClearSilently();
            for (int i = 0; i < savedCombo; i++) ComboManager.RegisterCatch();
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
