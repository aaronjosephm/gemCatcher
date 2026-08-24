using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates <see cref="WaveDefinition"/> instances using a 5-column grid.
/// Each row assigns every column to Rock, Gem, PoisonGem, or Empty.
/// At least one column per row is always safe (empty or gem).
/// </summary>
public static class WaveGenerator
{
    enum Archetype
    {
        LeftOpening,
        RightOpening,
        CenterOpening,
        CenterBlocker,
        MovingCorridor,
        ZigZag,
        Recovery,
    }

    /// <summary>Generate a column-based wave for the current difficulty.</summary>
    public static WaveDefinition Generate(
        RushConfig config,
        RushConfig.DifficultyTier tier,
        float areaLeft,
        float areaRight,
        out int attempts,
        WaveDefinition.Row previousLastRow = null)
    {
        const int MaxAttempts = 20;
        attempts = 0;

        for (int i = 0; i < MaxAttempts; i++)
        {
            attempts++;
            Archetype arch = PickArchetype(tier);
            WaveDefinition wave = BuildArchetype(arch, config, tier);

            // Validate internal reachability.
            if (!SafePathValidator.Validate(wave, config, areaLeft, areaRight))
                continue;

            // Cross-wave reachability: can the player get from the previous
            // wave's last row safe corridor to this wave's first row?
            if (previousLastRow != null && wave.rows.Count > 0)
            {
                var firstRow = wave.rows[0];
                float dist = ClosestDistance(
                    previousLastRow.safeMinX, previousLastRow.safeMaxX,
                    firstRow.safeMinX, firstRow.safeMaxX);

                // Allow generous travel time for the gap between waves.
                float travelBudget = config.playerMoveSpeed *
                    (config.wavePause / Mathf.Max(tier.fallSpeed, 0.1f) + config.reactionTimeBuffer);

                if (dist > travelBudget)
                    continue;
            }

            if (config.logValidation)
                Debug.Log($"[WaveGen] {arch}: {wave.rows.Count} rows (attempt {attempts})");

            return wave;
        }

        // Fallback: generate a Recovery wave (all columns safe).
        attempts++;
        if (config.logValidation)
            Debug.LogWarning($"[WaveGen] All {MaxAttempts} attempts failed, using Recovery fallback");
        return BuildArchetype(Archetype.Recovery, config, tier);
    }

    /// <summary>Overload without out parameter.</summary>
    public static WaveDefinition Generate(
        RushConfig config,
        RushConfig.DifficultyTier tier,
        float areaLeft,
        float areaRight)
    {
        return Generate(config, tier, areaLeft, areaRight, out _, null);
    }

    /// <summary>
    /// Minimum horizontal distance between two intervals. Zero if overlapping.
    /// </summary>
    static float ClosestDistance(float aMin, float aMax, float bMin, float bMax)
    {
        if (aMax < bMin) return bMin - aMax;
        if (bMax < aMin) return aMin - bMax;
        return 0f;
    }

    static Archetype PickArchetype(RushConfig.DifficultyTier tier)
    {
        float simple = 1f;
        float complex = tier.complexPatternWeight;

        var options = new List<(Archetype a, float w)>
        {
            (Archetype.LeftOpening,     simple),
            (Archetype.RightOpening,    simple),
            (Archetype.CenterOpening,   simple),
            (Archetype.CenterBlocker,   simple * 0.7f),
            (Archetype.MovingCorridor,  complex),
            (Archetype.ZigZag,          complex),
            (Archetype.Recovery,        0.3f),
        };

        float total = 0f;
        foreach (var o in options) total += o.w;

        float roll = Random.Range(0f, total);
        float running = 0f;
        foreach (var o in options)
        {
            running += o.w;
            if (roll <= running) return o.a;
        }
        return Archetype.CenterOpening;
    }

    static WaveDefinition BuildArchetype(Archetype arch, RushConfig config, RushConfig.DifficultyTier tier)
    {
        switch (arch)
        {
            case Archetype.LeftOpening:    return BuildSideOpening(config, tier, true);
            case Archetype.RightOpening:   return BuildSideOpening(config, tier, false);
            case Archetype.CenterOpening:  return BuildCenterOpening(config, tier);
            case Archetype.CenterBlocker:  return BuildCenterBlocker(config, tier);
            case Archetype.MovingCorridor: return BuildMovingCorridor(config, tier);
            case Archetype.ZigZag:         return BuildZigZag(config, tier);
            case Archetype.Recovery:       return BuildRecovery(config, tier);
            default:                       return BuildCenterOpening(config, tier);
        }
    }

    // ------------------------------------------------------------------
    // Archetype builders — all work with column indices 0..4
    // ------------------------------------------------------------------

    // How many columns to keep safe based on safeCorridorFraction.
    static int SafeColumnCount(RushConfig.DifficultyTier tier)
    {
        // 0.7 → 4 safe, 0.5 → 3 safe, 0.4 → 2 safe, <0.3 → 1 safe
        int safe = Mathf.RoundToInt(tier.safeCorridorFraction * RushColumns.Count);
        return Mathf.Clamp(safe, 1, RushColumns.Count - 1);
    }

    /// <summary>Safe columns on one side, rocks on the other.</summary>
    static WaveDefinition BuildSideOpening(RushConfig config, RushConfig.DifficultyTier tier, bool openLeft)
    {
        var wave = new WaveDefinition { archetypeName = openLeft ? "LeftOpening" : "RightOpening" };
        int rowCount = Mathf.Clamp(Random.Range(2, tier.maxRows + 1), 1, tier.maxRows);
        int safeCols = SafeColumnCount(tier);

        for (int r = 0; r < rowCount; r++)
        {
            bool[] safe = new bool[RushColumns.Count];
            if (openLeft)
                for (int c = 0; c < safeCols; c++) safe[c] = true;
            else
                for (int c = RushColumns.Count - safeCols; c < RushColumns.Count; c++) safe[c] = true;

            wave.rows.Add(BuildRow(r, safe, config, tier));
        }
        return wave;
    }

    /// <summary>Safe columns in the center, rocks on both sides.</summary>
    static WaveDefinition BuildCenterOpening(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "CenterOpening" };
        int rowCount = Mathf.Clamp(Random.Range(2, tier.maxRows + 1), 1, tier.maxRows);
        int safeCols = SafeColumnCount(tier);

        for (int r = 0; r < rowCount; r++)
        {
            bool[] safe = new bool[RushColumns.Count];
            int startSafe = (RushColumns.Count - safeCols) / 2;
            for (int c = startSafe; c < startSafe + safeCols; c++) safe[c] = true;

            wave.rows.Add(BuildRow(r, safe, config, tier));
        }
        return wave;
    }

    /// <summary>Rock in center column, safe on both sides.</summary>
    static WaveDefinition BuildCenterBlocker(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "CenterBlocker" };

        // Row 1: rock in center, safe on sides.
        bool[] safe1 = new bool[RushColumns.Count];
        for (int c = 0; c < RushColumns.Count; c++) safe1[c] = c != 2;
        wave.rows.Add(BuildRow(0, safe1, config, tier));

        // Row 2: block one side to force direction.
        bool blockRight = Random.value < 0.5f;
        bool[] safe2 = new bool[RushColumns.Count];
        if (blockRight)
            for (int c = 0; c < 3; c++) safe2[c] = true;
        else
            for (int c = 2; c < RushColumns.Count; c++) safe2[c] = true;
        wave.rows.Add(BuildRow(1, safe2, config, tier));

        return wave;
    }

    /// <summary>Safe column(s) shift across rows.</summary>
    static WaveDefinition BuildMovingCorridor(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "MovingCorridor" };
        int rowCount = Mathf.Clamp(tier.maxRows, 3, 5);
        int safeCol = Random.Range(0, RushColumns.Count);
        int dir = Random.value < 0.5f ? 1 : -1;

        for (int r = 0; r < rowCount; r++)
        {
            bool[] safe = new bool[RushColumns.Count];
            safe[safeCol] = true;
            // Also make adjacent column safe for playability.
            if (safeCol > 0) safe[safeCol - 1] = true;
            if (safeCol < RushColumns.Count - 1) safe[safeCol + 1] = true;

            wave.rows.Add(BuildRow(r, safe, config, tier));

            safeCol += dir;
            if (safeCol <= 0 || safeCol >= RushColumns.Count - 1) dir = -dir;
            safeCol = Mathf.Clamp(safeCol, 0, RushColumns.Count - 1);
        }
        return wave;
    }

    /// <summary>Safe columns alternate left ↔ right.</summary>
    static WaveDefinition BuildZigZag(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "ZigZag" };
        int rowCount = Mathf.Clamp(tier.maxRows, 3, 5);
        bool goLeft = Random.value < 0.5f;
        int safeCols = Mathf.Max(2, SafeColumnCount(tier));

        for (int r = 0; r < rowCount; r++)
        {
            bool[] safe = new bool[RushColumns.Count];
            if (goLeft)
                for (int c = 0; c < safeCols; c++) safe[c] = true;
            else
                for (int c = RushColumns.Count - safeCols; c < RushColumns.Count; c++) safe[c] = true;

            wave.rows.Add(BuildRow(r, safe, config, tier));
            goLeft = !goLeft;
        }
        return wave;
    }

    /// <summary>Easy wave — all columns safe, gem in center.</summary>
    static WaveDefinition BuildRecovery(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "Recovery" };
        bool[] safe = new bool[RushColumns.Count];
        for (int c = 0; c < RushColumns.Count; c++) safe[c] = true;

        var row = BuildRow(0, safe, config, tier);
        // Override: place a gem in center column.
        row.slots.Clear();
        row.slots.Add(WaveDefinition.Slot.GemAt(RushColumns.GetColumnX(2)));
        wave.rows.Add(row);
        return wave;
    }

    // ------------------------------------------------------------------
    // Row builder — one item per column
    // ------------------------------------------------------------------

    /// <summary>
    /// Build a row from a safe-column mask. Unsafe columns get rocks.
    /// One safe column may get a guide gem. One unsafe column may get a poison gem.
    /// </summary>
    static WaveDefinition.Row BuildRow(int rowIndex, bool[] safe, RushConfig config, RushConfig.DifficultyTier tier)
    {
        var row = new WaveDefinition.Row { yOffset = rowIndex * config.rowSpacing };

        // Determine safe corridor bounds for validator.
        float minSafe = float.MaxValue;
        float maxSafe = float.MinValue;

        for (int c = 0; c < RushColumns.Count; c++)
        {
            float x = RushColumns.GetColumnX(c);

            if (safe[c])
            {
                if (x < minSafe) minSafe = x;
                if (x > maxSafe) maxSafe = x;
                // Leave empty (safe)
            }
            else
            {
                // Poison gem chance in rock columns.
                if (tier.poisonGemChance > 0f && Random.value < tier.poisonGemChance)
                {
                    row.slots.Add(WaveDefinition.Slot.PoisonGemAt(x));
                }
                else
                {
                    row.slots.Add(WaveDefinition.Slot.Rock(x, config.rockSize.worldWidth, 0));
                }
            }
        }

        row.safeMinX = minSafe;
        row.safeMaxX = maxSafe;

        // Guide gem in one safe column.
        if (Random.value < config.gemGuideChance)
        {
            // Pick a random safe column.
            List<int> safeCols = new List<int>();
            for (int c = 0; c < RushColumns.Count; c++)
                if (safe[c]) safeCols.Add(c);
            if (safeCols.Count > 0)
            {
                int pick = safeCols[Random.Range(0, safeCols.Count)];
                row.slots.Add(WaveDefinition.Slot.GemAt(RushColumns.GetColumnX(pick)));
            }
        }

        return row;
    }
}
