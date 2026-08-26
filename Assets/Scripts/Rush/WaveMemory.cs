using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks recent waves for the procedural memory system.
/// Used by <see cref="WaveInterestScore"/> to penalise repetition
/// and by the generator to ensure variety.
/// </summary>
public class WaveMemory
{
    /// <summary>Summary of a recently generated wave.</summary>
    public class WaveRecord
    {
        public DecisionPlan.DecisionType decisionType;
        public string archetypeName;

        /// <summary>Dominant safe-path direction: -1 left, 0 center, 1 right.</summary>
        public int dominantDirection;

        /// <summary>Number of direction changes in the safe path.</summary>
        public int directionChanges;

        /// <summary>Average corridor width in columns.</summary>
        public float avgCorridorWidthCols;

        /// <summary>Total gem count.</summary>
        public int gemCount;

        /// <summary>Interest score.</summary>
        public float interestScore;
    }

    readonly int capacity;
    readonly List<WaveRecord> records = new List<WaveRecord>();

    public WaveMemory(int capacity = 6)
    {
        this.capacity = capacity;
    }

    public List<WaveRecord> Records => records;

    public void Record(WaveRecord rec)
    {
        records.Add(rec);
        if (records.Count > capacity) records.RemoveAt(0);
    }

    /// <summary>Build a record from a completed wave + plan.</summary>
    public static WaveRecord BuildRecord(WaveDefinition wave, DecisionPlan plan, float interestScore)
    {
        var rec = new WaveRecord
        {
            decisionType = plan != null ? plan.type : DecisionPlan.DecisionType.Recovery,
            archetypeName = wave.archetypeName,
            interestScore = interestScore,
        };

        // Dominant direction.
        if (wave.rows.Count >= 2)
        {
            float first = wave.rows[0].SafeCenter;
            float last = wave.rows[wave.rows.Count - 1].SafeCenter;
            float delta = last - first;
            rec.dominantDirection = delta > 0.3f ? 1 : delta < -0.3f ? -1 : 0;
        }

        // Direction changes.
        int prevDir = 0;
        for (int i = 1; i < wave.rows.Count; i++)
        {
            float shift = wave.rows[i].SafeCenter - wave.rows[i - 1].SafeCenter;
            int dir = shift > 0.1f ? 1 : shift < -0.1f ? -1 : 0;
            if (dir != 0 && dir != prevDir && prevDir != 0) rec.directionChanges++;
            if (dir != 0) prevDir = dir;
        }

        // Average corridor width.
        float colWidth = Mathf.Max(RushColumns.ColumnWidth, 0.5f);
        float totalW = 0f;
        foreach (var row in wave.rows) totalW += row.SafeWidth;
        rec.avgCorridorWidthCols = wave.rows.Count > 0 ? (totalW / wave.rows.Count) / colWidth : 3f;

        // Gem count.
        foreach (var row in wave.rows)
            foreach (var slot in row.slots)
                if (slot.type == WaveDefinition.SlotType.Gem) rec.gemCount++;

        return rec;
    }

    /// <summary>
    /// Calculate a repetition penalty for a proposed plan based on recent history.
    /// Negative value (penalty). Zero or close to zero = good variety.
    /// </summary>
    public static float RepetitionPenalty(DecisionPlan plan, List<WaveRecord> recent)
    {
        if (recent == null || recent.Count == 0) return 0f;

        float penalty = 0f;

        // Same decision type as last wave: heavy penalty.
        if (recent.Count >= 1 && recent[recent.Count - 1].decisionType == plan.type)
            penalty -= 6f;

        // Same decision type as 2nd-to-last: moderate penalty.
        if (recent.Count >= 2 && recent[recent.Count - 2].decisionType == plan.type)
            penalty -= 3f;

        // Count how many of last N used same type.
        int sameCount = 0;
        for (int i = 0; i < recent.Count; i++)
            if (recent[i].decisionType == plan.type) sameCount++;

        if (sameCount >= 3) penalty -= 4f;

        // Same dominant direction as last 2 waves.
        if (recent.Count >= 2 && plan.routes != null && plan.routes.SafeRoute != null)
        {
            var safeRoute = plan.routes.SafeRoute;
            if (safeRoute.columns != null && safeRoute.columns.Length >= 2)
            {
                int first = safeRoute.columns[0];
                int last = safeRoute.columns[safeRoute.columns.Length - 1];
                int dir = last > first ? 1 : last < first ? -1 : 0;

                if (recent[recent.Count - 1].dominantDirection == dir &&
                    recent[recent.Count - 2].dominantDirection == dir)
                    penalty -= 3f;
            }
        }

        return penalty;
    }
}
