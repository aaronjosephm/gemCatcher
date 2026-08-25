using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scores a <see cref="WaveDefinition"/> for "interestingness" — how
/// engaging the wave is likely to feel to the player. Used to pick the
/// best candidate from several valid waves.
///
/// Higher score = more interesting.
/// </summary>
public static class WaveInterestScore
{
    /// <summary>Detailed breakdown of an interest score for debugging.</summary>
    public class Breakdown
    {
        public float routeDecisions;
        public float horizontalMovement;
        public float directionChanges;
        public float riskReward;
        public float gemInfluence;
        public float stationaryPenalty;
        public float repetitionPenalty;
        public float widthPenalty;
        public float total;

        public override string ToString()
        {
            return $"Interest={total:F1} [decisions={routeDecisions:F1} hMove={horizontalMovement:F1} " +
                   $"dirChg={directionChanges:F1} risk={riskReward:F1} gems={gemInfluence:F1} " +
                   $"statPen={stationaryPenalty:F1} repPen={repetitionPenalty:F1} widthPen={widthPenalty:F1}]";
        }
    }

    /// <summary>
    /// Score a wave. Higher = more interesting.
    /// </summary>
    public static float Score(
        WaveDefinition wave,
        DecisionPlan plan,
        RushConfig config,
        RushConfig.DifficultyTier tier,
        List<WaveMemory.WaveRecord> recentWaves,
        out Breakdown breakdown)
    {
        breakdown = new Breakdown();
        if (wave == null || wave.rows.Count == 0) return 0f;

        // --- Route decisions ---
        // Reward waves where routes split (fork/merge points).
        if (plan != null && plan.routes != null)
        {
            int routeCount = plan.routes.routes.Count;
            breakdown.routeDecisions = Mathf.Min(routeCount - 1, 3) * 3f;
            if (plan.routes.splitRow >= 0) breakdown.routeDecisions += 2f;
        }

        // --- Horizontal movement required ---
        float totalColumnShift = 0f;
        int dirChanges = 0;
        int prevDir = 0;
        for (int i = 1; i < wave.rows.Count; i++)
        {
            float shift = wave.rows[i].SafeCenter - wave.rows[i - 1].SafeCenter;
            totalColumnShift += Mathf.Abs(shift);

            int dir = shift > 0.1f ? 1 : shift < -0.1f ? -1 : 0;
            if (dir != 0 && dir != prevDir && prevDir != 0) dirChanges++;
            if (dir != 0) prevDir = dir;
        }
        float colWidth = Mathf.Max(RushColumns.ColumnWidth, 0.5f);
        breakdown.horizontalMovement = Mathf.Min(totalColumnShift / colWidth, 8f) * 1.5f;
        breakdown.directionChanges = dirChanges * 2f;

        // --- Risk/reward (gems near risky columns) ---
        if (plan != null && plan.type != DecisionPlan.DecisionType.Recovery)
        {
            int riskyGems = 0;
            foreach (var row in wave.rows)
            {
                foreach (var slot in row.slots)
                {
                    if (slot.type == WaveDefinition.SlotType.Gem)
                    {
                        float distFromSafe = Mathf.Min(
                            Mathf.Abs(slot.x - row.safeMinX),
                            Mathf.Abs(slot.x - row.safeMaxX));
                        if (distFromSafe > colWidth * 0.5f) riskyGems++;
                    }
                }
            }
            breakdown.riskReward = Mathf.Min(riskyGems, 5) * 2f;
        }

        // --- Gem influence on movement ---
        int gemsOnSafePath = 0;
        int gemsOffSafePath = 0;
        foreach (var row in wave.rows)
        {
            foreach (var slot in row.slots)
            {
                if (slot.type == WaveDefinition.SlotType.Gem)
                {
                    if (slot.x >= row.safeMinX - 0.1f && slot.x <= row.safeMaxX + 0.1f)
                        gemsOnSafePath++;
                    else
                        gemsOffSafePath++;
                }
            }
        }
        // Best when gems are split between safe and risky areas.
        int totalGems = gemsOnSafePath + gemsOffSafePath;
        if (totalGems > 0)
        {
            float ratio = gemsOffSafePath / (float)totalGems;
            // Peak at 40% off-path.
            breakdown.gemInfluence = (1f - Mathf.Abs(ratio - 0.4f) * 2f) * 4f;
            breakdown.gemInfluence = Mathf.Max(0f, breakdown.gemInfluence);
        }

        // --- Stationary penalty ---
        // Penalize if the player can stay in one column the whole wave.
        bool canStayPut = true;
        if (wave.rows.Count > 1)
        {
            float firstSafe = wave.rows[0].SafeCenter;
            for (int i = 1; i < wave.rows.Count; i++)
            {
                if (firstSafe < wave.rows[i].safeMinX - 0.1f ||
                    firstSafe > wave.rows[i].safeMaxX + 0.1f)
                {
                    canStayPut = false;
                    break;
                }
            }
        }
        if (canStayPut && plan != null && plan.type != DecisionPlan.DecisionType.Recovery)
            breakdown.stationaryPenalty = -5f;

        // --- Width penalty (at high difficulty, wide corridors are boring) ---
        float avgWidth = 0f;
        foreach (var row in wave.rows) avgWidth += row.SafeWidth;
        avgWidth /= Mathf.Max(wave.rows.Count, 1);
        float maxAllowedWidth = Mathf.Lerp(4f, 1.5f, tier.complexPatternWeight);
        if (avgWidth > maxAllowedWidth)
            breakdown.widthPenalty = -(avgWidth - maxAllowedWidth) * 3f;

        // --- Repetition penalty ---
        if (recentWaves != null && plan != null)
        {
            breakdown.repetitionPenalty = WaveMemory.RepetitionPenalty(plan, recentWaves);
        }

        breakdown.total =
            breakdown.routeDecisions +
            breakdown.horizontalMovement +
            breakdown.directionChanges +
            breakdown.riskReward +
            breakdown.gemInfluence +
            breakdown.stationaryPenalty +
            breakdown.repetitionPenalty +
            breakdown.widthPenalty;

        return breakdown.total;
    }
}
