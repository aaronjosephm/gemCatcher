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
            Require(tiers.Count == 1 && tiers[0] == 5, "A new cycle must earn its first multiplier again.");
            ComboManager.Break();
            Require(broken == 1 && ComboManager.CurrentCombo == 0, "A rock hit must break the streak.");
            ComboManager.Break();
            Require(broken == 1, "Repeated break must not duplicate cancellation.");
            for (int tier = 1; tier <= 4; tier++)
            {
                Require(ComboLightning.DurationForTier(tier) == new[] { 0f, 0f, 0f, 10f }[tier - 1],
                    "Only Gem Rush grants lightning, lasting exactly 10 seconds.");
                Require(ComboLightning.RangeInColumns(tier) == new[] { 2f, 4f, 6f, 8f }[tier - 1],
                    "Charge reach must be 2/4/6/8 boulder slots.");
                Require(ComboLightning.ZapIntervalForTier(tier) == new[] { 0.5f, 0.2f, 0.125f, 0.1f }[tier - 1],
                    "Zap intervals must be 500/200/125/100 ms.");
            }
            RushConfig speedConfig = ScriptableObject.CreateInstance<RushConfig>();
            try
            {
                for (int level = 0; level < 4; level++)
                {
                    var id = (LevelManager.LevelId)level;
                    float start = speedConfig.GetLevelFallSpeed(0f, id);
                    float end = speedConfig.GetLevelFallSpeed(float.MaxValue, id);
                    Require(end > start, "Every level must accelerate.");
                    if (level < 3)
                        Require(Mathf.Approximately(end, speedConfig.GetLevelFallSpeed(0f,
                            (LevelManager.LevelId)(level + 1))),
                            "Each level must start at the previous level's ending speed.");
                }
                Require(Mathf.Approximately(speedConfig.GetLevelFallSpeed(55f, LevelManager.LevelId.Cave),
                    speedConfig.GetFallSpeed(55f)), "Cave must retain its original speed curve.");
            }
            finally { UnityEngine.Object.DestroyImmediate(speedConfig); }
            Require(LevelManager.JungleUnlockScore == 25000 && LevelManager.SpaceUnlockScore == 50000
                && LevelManager.LavaUnlockScore == 100000 && LevelManager.FinalLevelGoal == 200000,
                "Level goals must be 25k/50k/100k/200k.");
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
