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
        Fork,
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
            (Archetype.Fork,            Mathf.Max(0.5f, complex)),
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
            case Archetype.Fork:           wave = BuildFork(config, tier); break;
            case Archetype.Recovery:       wave = BuildRecovery(config, tier); break;
            default:                       wave = BuildCenterOpening(config, tier); break;
        }

        // Fork handles its own gem/rock placement — skip buffer rows.
        if (arch != Archetype.Fork)
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
                // 3 gems in the same column, rocks in adjacent columns.
                for (int r = 0; r < 3; r++)
                    rows.Add(BuildGemRowWithRocks(rows.Count, startCol, config));
                break;

            case ClusterShape.DiagonalRight:
                for (int r = 0; r < 3; r++)
                    rows.Add(BuildGemRowWithRocks(rows.Count, startCol + r, config));
                break;

            case ClusterShape.DiagonalLeft:
                for (int r = 0; r < 3; r++)
                    rows.Add(BuildGemRowWithRocks(rows.Count, startCol - r, config));
                break;

            case ClusterShape.Horizontal:
            {
                // 3 gems side by side, rocks in remaining columns.
                var row = new WaveDefinition.Row { yOffset = rows.Count * config.rowSpacing };
                bool[] gemCols = new bool[RushColumns.Count];
                for (int c = startCol; c < startCol + 3; c++) gemCols[c] = true;

                for (int c = 0; c < RushColumns.Count; c++)
                {
                    float x = RushColumns.GetColumnX(c);
                    if (gemCols[c])
                        row.slots.Add(WaveDefinition.Slot.GemAt(x));
                    else
                        row.slots.Add(WaveDefinition.Slot.Rock(x, config.rockSize.worldWidth, 0));
                }
                row.safeMinX = RushColumns.GetColumnX(startCol);
                row.safeMaxX = RushColumns.GetColumnX(startCol + 2);
                rows.Add(row);
                break;
            }
        }
    }

    /// <summary>
    /// Build a row with a gem in the specified column and rocks in all other columns.
    /// Creates risk/reward: the player must navigate through rocks to collect gems.
    /// </summary>
    static WaveDefinition.Row BuildGemRowWithRocks(int rowIndex, int gemColumn, RushConfig config)
    {
        var row = new WaveDefinition.Row
        {
            yOffset = rowIndex * config.rowSpacing,
            safeMinX = RushColumns.GetColumnX(gemColumn),
            safeMaxX = RushColumns.GetColumnX(gemColumn),
        };

        for (int c = 0; c < RushColumns.Count; c++)
        {
            float x = RushColumns.GetColumnX(c);
            if (c == gemColumn)
                row.slots.Add(WaveDefinition.Slot.GemAt(x));
            else
                row.slots.Add(WaveDefinition.Slot.Rock(x, config.rockSize.worldWidth, 0));
        }

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

    /// <summary>
    /// Fork: two paths separated by a center rock wall.
    /// Path A has 3 vertical gems but ends with a rock blocking the exit (trap).
    /// Path B is empty but safe. Player must decide: risk a life for gems, or play safe.
    /// </summary>
    static WaveDefinition BuildFork(RushConfig config, RushConfig.DifficultyTier tier)
    {
        var wave = new WaveDefinition { archetypeName = "Fork" };

        // Randomly pick which side is the gem trap.
        bool gemOnLeft = Random.value < 0.5f;

        // Gem path column (inner column of that side, closer to center).
        int gemCol = gemOnLeft ? 1 : 3;

        // Safe path columns (the other side).
        int safeCol1 = gemOnLeft ? 3 : 0;
        int safeCol2 = gemOnLeft ? 4 : 1;

        float rw = config.rockSize.worldWidth;

        // Rows 0-2: Gems on one side, rocks everywhere else except safe path.
        for (int r = 0; r < 3; r++)
        {
            var row = new WaveDefinition.Row { yOffset = r * config.rowSpacing };

            for (int c = 0; c < RushColumns.Count; c++)
            {
                float x = RushColumns.GetColumnX(c);

                if (c == gemCol)
                    row.slots.Add(WaveDefinition.Slot.GemAt(x));
                else if (c == safeCol1 || c == safeCol2)
                {
                    // Safe path — leave empty.
                }
                else
                    row.slots.Add(WaveDefinition.Slot.Rock(x, rw, 0));
            }

            row.safeMinX = Mathf.Min(RushColumns.GetColumnX(gemCol),
                                     RushColumns.GetColumnX(safeCol1));
            row.safeMaxX = Mathf.Max(RushColumns.GetColumnX(gemCol),
                                     RushColumns.GetColumnX(safeCol2));
            wave.rows.Add(row);
        }

        // Row 3: The trap — gem path column is now BLOCKED with a rock.
        // Safe path stays open.
        {
            var row = new WaveDefinition.Row { yOffset = 3 * config.rowSpacing };

            for (int c = 0; c < RushColumns.Count; c++)
            {
                float x = RushColumns.GetColumnX(c);

                if (c == safeCol1 || c == safeCol2)
                {
                    // Safe path still open.
                }
                else
                    row.slots.Add(WaveDefinition.Slot.Rock(x, rw, 0));
            }

            row.safeMinX = RushColumns.GetColumnX(Mathf.Min(safeCol1, safeCol2));
            row.safeMaxX = RushColumns.GetColumnX(Mathf.Max(safeCol1, safeCol2));
            wave.rows.Add(row);
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
