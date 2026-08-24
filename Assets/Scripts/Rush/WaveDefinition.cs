using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Describes a single wave (formation) of falling objects. A wave consists
/// of one or more <see cref="Row"/>s, each containing <see cref="Slot"/>s
/// that can be hazards, gems, or empty space. The <see cref="SpawnDirector"/>
/// materializes these into pooled GameObjects at runtime.
/// </summary>
public class WaveDefinition
{
    /// <summary>What occupies a slot in a row.</summary>
    public enum SlotType
    {
        Empty,
        Hazard,
        Gem,
    }

    /// <summary>One slot in a row — a hazard, gem, or empty space.</summary>
    public struct Slot
    {
        public SlotType type;

        /// <summary>World-X center of this slot.</summary>
        public float x;

        /// <summary>World width of this slot's object (used for gap calculations).</summary>
        public float width;

        /// <summary>Index into RushConfig.hazardSizes (only meaningful when type == Hazard).</summary>
        public int hazardSizeIndex;

        public static Slot Empty(float x) => new Slot { type = SlotType.Empty, x = x, width = 0f };
        public static Slot Rock(float x, float width, int sizeIdx) =>
            new Slot { type = SlotType.Hazard, x = x, width = width, hazardSizeIndex = sizeIdx };
        public static Slot GemAt(float x) =>
            new Slot { type = SlotType.Gem, x = x, width = 0.4f };
    }

    /// <summary>One horizontal row of slots, spawned at a specific Y offset from the wave origin.</summary>
    public class Row
    {
        /// <summary>Vertical offset from the wave's spawn origin (0 = first row).</summary>
        public float yOffset;

        /// <summary>All slots in this row.</summary>
        public List<Slot> slots = new List<Slot>();

        /// <summary>
        /// The safe corridor for this row: the X range the player can
        /// safely occupy. Set by the generator, validated by SafePathValidator.
        /// </summary>
        public float safeMinX;
        public float safeMaxX;

        public float SafeCenter => (safeMinX + safeMaxX) * 0.5f;
        public float SafeWidth => safeMaxX - safeMinX;
    }

    /// <summary>Which archetype generated this wave (for logging/debug).</summary>
    public string archetypeName = "Unknown";

    /// <summary>Ordered rows, top-to-bottom (row 0 spawns first / highest).</summary>
    public List<Row> rows = new List<Row>();

    /// <summary>Fall speed for all objects in this wave.</summary>
    public float fallSpeed = 3f;

    /// <summary>Total vertical span of the wave (yOffset of last row).</summary>
    public float TotalHeight
    {
        get
        {
            if (rows.Count == 0) return 0f;
            return rows[rows.Count - 1].yOffset;
        }
    }
}
