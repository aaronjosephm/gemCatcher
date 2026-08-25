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
        WaveDefinition wave;
        switch (arch)
        {
            case Archetype.LeftOpening:    wave = BuildSideOpening(config, tier, true); break;
            case Archetype.RightOpening:   wave = BuildSideOpening(config, tier, false); break;
            case Archetype.CenterOpening:  wave = BuildCenterOpening(config, tier); break;
            case Archetype.CenterBlocker:  wave = BuildCenterBlocker(config, tier); break;
            case Archetype.MovingCorridor: wave = BuildMovingCorridor(config, tier); break;
            case Archetype.ZigZag:         wave = BuildZigZag(config, tier); break;
            case Archetype.Recovery:       wave = BuildRecovery(config, tier); break;
            default:                       wave = BuildCenterOpening(config, tier); break;
        }
        // Insert gem-only buffer rows so gems always form clusters of 3+.
        InsertGemBufferRows(wave, config);
        return wave;
    }

    /// <summary>
    /// Inserts gem clusters (vertical, diagonal, horizontal) between rock rows.
    /// Each cluster is exactly 3 gems in a line shape.
    /// </summary>
    static void InsertGemBufferRows(WaveDefinition wave, RushConfig config)
    {
        var expanded = new List<WaveDefinition.Row>();

        for (int i = 0; i < wave.rows.Count; i++)
        {
            // Insert a gem cluster before each rock row.
            InsertGemCluster(expanded, config);

            var row = wave.rows[i];
            row.yOffset = expanded.Count * config.rowSpacing;
            expanded.Add(row);
        }

        // One more cluster after the last row.
        InsertGemCluster(expanded, config);

        wave.rows = expanded;
    }

    enum ClusterShape { Vertical, DiagonalRight, DiagonalLeft, Horizontal }

    static void InsertGemCluster(List<WaveDefinition.Row> rows, RushConfig config)
    {
        // Pick a random shape.
        ClusterShape shape = (ClusterShape)Random.Range(0, 4);
        int startCol = Random.Range(0, RushColumns.Count);

        // Adjust start column so the cluster fits within 0..4.
        switch (shape)
        {
            case ClusterShape.DiagonalRight:
                startCol = Mathf.Clamp(startCol, 0, RushColumns.Count - 3);
                break;
            case ClusterShape.DiagonalLeft:
                startCol = Mathf.Clamp(startCol, 2, RushColumns.Count - 1);
                break;
            case ClusterShape.Horizontal:
                startCol = Mathf.Clamp(startCol, 0, RushColumns.Count - 3);
                break;
        }

        switch (shape)
        {
            case ClusterShape.Vertical:
                // 3 gems in the same column, 3 consecutive rows.
                for (int r = 0; r < 3; r++)
                    rows.Add(BuildSingleGemRow(rows.Count, startCol, config));
                break;

            case ClusterShape.DiagonalRight:
                // 3 gems stepping right: col, col+1, col+2.
                for (int r = 0; r < 3; r++)
                    rows.Add(BuildSingleGemRow(rows.Count, startCol + r, config));
                break;

            case ClusterShape.DiagonalLeft:
                // 3 gems stepping left: col, col-1, col-2.
                for (int r = 0; r < 3; r++)
                    rows.Add(BuildSingleGemRow(rows.Count, startCol - r, config));
                break;

            case ClusterShape.Horizontal:
                // 3 gems in a horizontal line, same row.
                var row = new WaveDefinition.Row { yOffset = rows.Count * config.rowSpacing };
                float minX = RushColumns.GetColumnX(startCol);
                float maxX = RushColumns.GetColumnX(startCol + 2);
                for (int c = 0; c < 3; c++)
                    row.slots.Add(WaveDefinition.Slot.GemAt(RushColumns.GetColumnX(startCol + c)));
                row.safeMinX = minX;
                row.safeMaxX = maxX;
                rows.Add(row);
                break;
        }
    }

    /// <summary>Build a row with a single gem in the specified column.</summary>
    static WaveDefinition.Row BuildSingleGemRow(int rowIndex, int column, RushConfig config)
    {
        float x = RushColumns.GetColumnX(column);
        var row = new WaveDefinition.Row
        {
            yOffset = rowIndex * config.rowSpacing,
            safeMinX = RushColumns.GetColumnX(0),
            safeMaxX = RushColumns.GetColumnX(RushColumns.Count - 1),
        };
        row.slots.Add(WaveDefinition.Slot.GemAt(x));
        return row;
    }

    // ------------------------------------------------------------------
    // Archetype builders — all work with column indices 0..4
    // ------------------------------------------------------------------

    // How many columns to keep safe based on safeCorridorFraction.
    static int SafeColumnCount(RushConfig.DifficultyTier tier)
    {
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

    /// <summary>Easy wave — no rocks, just a vertical gem cluster in center.</summary>
    static WaveDefinition BuildRecovery(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "Recovery" };
        // No rock rows — InsertGemBufferRows will still add a cluster.
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

        return row;
    }
}
