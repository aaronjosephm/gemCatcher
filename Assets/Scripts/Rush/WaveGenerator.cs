using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates <see cref="WaveDefinition"/> instances from a library of
/// archetypes. Each archetype creates a different navigation puzzle:
/// left opening, right opening, center gap, center blocker, moving
/// corridor, zig-zag, funnel, fork, risk/reward, and recovery.
///
/// The generator picks an archetype weighted by difficulty, fills in
/// hazard slots using <see cref="RushConfig.HazardSize"/> definitions,
/// places gems for guidance/reward, and validates the result via
/// <see cref="SafePathValidator"/>.
/// </summary>
public static class WaveGenerator
{
    // Archetype enum for weighting and logging.
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

    /// <summary>
    /// Generate a wave appropriate for the current difficulty tier.
    /// </summary>
    public static WaveDefinition Generate(
        RushConfig config,
        RushConfig.DifficultyTier tier,
        float areaLeft,
        float areaRight)
    {
        float areaWidth = areaRight - areaLeft;
        float corridorWidth = areaWidth * tier.safeCorridorFraction;
        corridorWidth = Mathf.Max(corridorWidth, config.minSafeCorridorWidth);

        // Pick an archetype.
        Archetype arch = PickArchetype(tier);

        WaveDefinition wave;
        int attempts = 0;
        do
        {
            wave = BuildArchetype(arch, config, tier, areaLeft, areaRight, corridorWidth);
            attempts++;
            if (attempts > 10)
            {
                // Fallback: simple recovery wave.
                wave = BuildRecovery(config, tier, areaLeft, areaRight, areaWidth);
                break;
            }
        }
        while (!SafePathValidator.Validate(wave, config, areaLeft, areaRight));

        if (config.logValidation && attempts > 1)
        {
            Debug.Log($"[WaveGen] {arch} accepted after {attempts} attempts");
        }

        return wave;
    }

    static Archetype PickArchetype(RushConfig.DifficultyTier tier)
    {
        // Simple weighted random. Complex patterns get more weight at higher difficulty.
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

    static WaveDefinition BuildArchetype(
        Archetype arch,
        RushConfig config,
        RushConfig.DifficultyTier tier,
        float left, float right, float corridorWidth)
    {
        switch (arch)
        {
            case Archetype.LeftOpening:    return BuildSideOpening(config, tier, left, right, corridorWidth, true);
            case Archetype.RightOpening:   return BuildSideOpening(config, tier, left, right, corridorWidth, false);
            case Archetype.CenterOpening:  return BuildCenterOpening(config, tier, left, right, corridorWidth);
            case Archetype.CenterBlocker:  return BuildCenterBlocker(config, tier, left, right, corridorWidth);
            case Archetype.MovingCorridor: return BuildMovingCorridor(config, tier, left, right, corridorWidth);
            case Archetype.ZigZag:         return BuildZigZag(config, tier, left, right, corridorWidth);
            case Archetype.Recovery:       return BuildRecovery(config, tier, left, right, right - left);
            default:                       return BuildCenterOpening(config, tier, left, right, corridorWidth);
        }
    }

    // ------------------------------------------------------------------
    // Archetype builders
    // ------------------------------------------------------------------

    /// <summary>Safe corridor on one side, hazards cover the rest.</summary>
    static WaveDefinition BuildSideOpening(
        RushConfig config, RushConfig.DifficultyTier tier,
        float left, float right, float corridorWidth, bool openLeft)
    {
        var wave = new WaveDefinition { archetypeName = openLeft ? "LeftOpening" : "RightOpening" };
        int rowCount = Mathf.Min(tier.maxRows, Random.Range(2, tier.maxRows + 1));

        for (int r = 0; r < rowCount; r++)
        {
            var row = new WaveDefinition.Row { yOffset = r * config.rowSpacing };

            if (openLeft)
            {
                row.safeMinX = left;
                row.safeMaxX = left + corridorWidth;
                FillHazards(row, row.safeMaxX, right, config, tier);
            }
            else
            {
                row.safeMinX = right - corridorWidth;
                row.safeMaxX = right;
                FillHazards(row, left, row.safeMinX, config, tier);
            }

            // Guide gem in the safe corridor.
            if (Random.value < config.gemGuideChance)
            {
                row.slots.Add(WaveDefinition.Slot.GemAt(row.SafeCenter));
            }

            wave.rows.Add(row);
        }

        return wave;
    }

    /// <summary>Safe corridor in the center, hazards on both sides.</summary>
    static WaveDefinition BuildCenterOpening(
        RushConfig config, RushConfig.DifficultyTier tier,
        float left, float right, float corridorWidth)
    {
        var wave = new WaveDefinition { archetypeName = "CenterOpening" };
        int rowCount = Mathf.Min(tier.maxRows, Random.Range(2, tier.maxRows + 1));
        float center = (left + right) * 0.5f;

        for (int r = 0; r < rowCount; r++)
        {
            var row = new WaveDefinition.Row { yOffset = r * config.rowSpacing };
            row.safeMinX = center - corridorWidth * 0.5f;
            row.safeMaxX = center + corridorWidth * 0.5f;

            FillHazards(row, left, row.safeMinX, config, tier);
            FillHazards(row, row.safeMaxX, right, config, tier);

            if (Random.value < config.gemGuideChance)
            {
                row.slots.Add(WaveDefinition.Slot.GemAt(center));
            }

            wave.rows.Add(row);
        }

        return wave;
    }

    /// <summary>Large rock in center, player forced left or right.</summary>
    static WaveDefinition BuildCenterBlocker(
        RushConfig config, RushConfig.DifficultyTier tier,
        float left, float right, float corridorWidth)
    {
        var wave = new WaveDefinition { archetypeName = "CenterBlocker" };
        float center = (left + right) * 0.5f;

        // Row 1: big rock in center.
        int largeIdx = GetLargestSizeIndex(config);
        float largeWidth = config.hazardSizes[largeIdx].worldWidth;

        var row1 = new WaveDefinition.Row { yOffset = 0f };
        row1.slots.Add(WaveDefinition.Slot.Rock(center, largeWidth, largeIdx));
        row1.safeMinX = left;
        row1.safeMaxX = center - largeWidth * 0.5f;
        // Also safe on the right:
        float rightSafeMin = center + largeWidth * 0.5f;
        // For validation we pick the wider safe side.
        if ((right - rightSafeMin) > row1.SafeWidth)
        {
            row1.safeMinX = rightSafeMin;
            row1.safeMaxX = right;
        }
        wave.rows.Add(row1);

        // Row 2: one side has hazards, making the other the correct choice.
        bool blockRight = Random.value < 0.5f;
        var row2 = new WaveDefinition.Row { yOffset = config.rowSpacing };
        if (blockRight)
        {
            FillHazards(row2, center, right, config, tier);
            row2.safeMinX = left;
            row2.safeMaxX = center;
        }
        else
        {
            FillHazards(row2, left, center, config, tier);
            row2.safeMinX = center;
            row2.safeMaxX = right;
        }

        if (Random.value < config.gemGuideChance)
        {
            row2.slots.Add(WaveDefinition.Slot.GemAt(row2.SafeCenter));
        }
        wave.rows.Add(row2);

        return wave;
    }

    /// <summary>Safe opening shifts across rows.</summary>
    static WaveDefinition BuildMovingCorridor(
        RushConfig config, RushConfig.DifficultyTier tier,
        float left, float right, float corridorWidth)
    {
        var wave = new WaveDefinition { archetypeName = "MovingCorridor" };
        int rowCount = Mathf.Clamp(tier.maxRows, 3, 5);
        float areaWidth = right - left;

        // Start position and direction.
        float openingCenter = Random.Range(left + corridorWidth * 0.5f, right - corridorWidth * 0.5f);
        float drift = Random.Range(0.3f, 0.6f) * (Random.value < 0.5f ? 1f : -1f);

        for (int r = 0; r < rowCount; r++)
        {
            var row = new WaveDefinition.Row { yOffset = r * config.rowSpacing };
            row.safeMinX = openingCenter - corridorWidth * 0.5f;
            row.safeMaxX = openingCenter + corridorWidth * 0.5f;

            FillHazards(row, left, row.safeMinX, config, tier);
            FillHazards(row, row.safeMaxX, right, config, tier);

            if (Random.value < config.gemGuideChance)
            {
                row.slots.Add(WaveDefinition.Slot.GemAt(openingCenter));
            }

            wave.rows.Add(row);

            // Drift the opening.
            openingCenter += drift * areaWidth * 0.3f;
            openingCenter = Mathf.Clamp(openingCenter,
                left + corridorWidth * 0.5f,
                right - corridorWidth * 0.5f);
        }

        return wave;
    }

    /// <summary>Opening alternates left ↔ right.</summary>
    static WaveDefinition BuildZigZag(
        RushConfig config, RushConfig.DifficultyTier tier,
        float left, float right, float corridorWidth)
    {
        var wave = new WaveDefinition { archetypeName = "ZigZag" };
        int rowCount = Mathf.Clamp(tier.maxRows, 3, 5);
        bool goLeft = Random.value < 0.5f;

        for (int r = 0; r < rowCount; r++)
        {
            var row = new WaveDefinition.Row { yOffset = r * config.rowSpacing };

            if (goLeft)
            {
                row.safeMinX = left;
                row.safeMaxX = left + corridorWidth;
                FillHazards(row, row.safeMaxX, right, config, tier);
            }
            else
            {
                row.safeMinX = right - corridorWidth;
                row.safeMaxX = right;
                FillHazards(row, left, row.safeMinX, config, tier);
            }

            if (Random.value < config.gemGuideChance)
            {
                row.slots.Add(WaveDefinition.Slot.GemAt(row.SafeCenter));
            }

            wave.rows.Add(row);
            goLeft = !goLeft;
        }

        return wave;
    }

    /// <summary>Easy wave — few or no hazards. Gives the player a breather.</summary>
    static WaveDefinition BuildRecovery(
        RushConfig config, RushConfig.DifficultyTier tier,
        float left, float right, float areaWidth)
    {
        var wave = new WaveDefinition { archetypeName = "Recovery" };
        var row = new WaveDefinition.Row
        {
            yOffset = 0f,
            safeMinX = left,
            safeMaxX = right,
        };

        // Just a gem or two, no rocks.
        float center = (left + right) * 0.5f;
        row.slots.Add(WaveDefinition.Slot.GemAt(center));
        if (Random.value < 0.5f)
        {
            row.slots.Add(WaveDefinition.Slot.GemAt(center + Random.Range(-0.3f, 0.3f)));
        }

        wave.rows.Add(row);
        return wave;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Fill a horizontal band [fromX, toX] with hazards.</summary>
    static void FillHazards(
        WaveDefinition.Row row,
        float fromX, float toX,
        RushConfig config,
        RushConfig.DifficultyTier tier)
    {
        float bandWidth = toX - fromX;
        if (bandWidth < 0.3f) return; // Too narrow for any rock.

        float cursor = fromX;
        int safety = 0;
        while (cursor < toX - 0.1f && safety < 20)
        {
            safety++;
            RushConfig.HazardSize size = config.PickRandomSize();
            if (size == null) break;

            // Skip large rocks if difficulty doesn't allow them yet.
            int sizeIdx = System.Array.IndexOf(config.hazardSizes, size);
            if (sizeIdx == GetLargestSizeIndex(config) && Random.value > tier.largeRockChance)
            {
                // Downgrade to medium.
                sizeIdx = Mathf.Min(1, config.hazardSizes.Length - 1);
                size = config.hazardSizes[sizeIdx];
            }

            float halfW = size.worldWidth * 0.5f;
            float rockX = cursor + halfW;

            if (rockX + halfW > toX + 0.05f) break; // Doesn't fit.

            row.slots.Add(WaveDefinition.Slot.Rock(rockX, size.worldWidth, sizeIdx));
            cursor = rockX + halfW + Random.Range(0.05f, 0.2f); // Small gap between rocks.
        }
    }

    static int GetLargestSizeIndex(RushConfig config)
    {
        if (config.hazardSizes == null || config.hazardSizes.Length == 0) return 0;
        int idx = 0;
        float maxW = 0f;
        for (int i = 0; i < config.hazardSizes.Length; i++)
        {
            if (config.hazardSizes[i].worldWidth > maxW)
            {
                maxW = config.hazardSizes[i].worldWidth;
                idx = i;
            }
        }
        return idx;
    }
}
