using UnityEngine;

/// <summary>
/// Calculates movement budgets in terms of TIME and player capability
/// rather than raw Unity distances. Used by the wave generator and
/// interest scorer to ensure fairness and tune difficulty.
/// </summary>
public static class MovementBudget
{
    /// <summary>
    /// How long (seconds) the player has between two consecutive rows.
    /// </summary>
    public static float TimeBetweenRows(float rowSpacing, float fallSpeed)
    {
        return rowSpacing / Mathf.Max(fallSpeed, 0.01f);
    }

    /// <summary>
    /// How many columns the player can traverse in the time between two rows.
    /// </summary>
    public static float ColumnsReachable(float rowSpacing, float fallSpeed, float playerMoveSpeed, float reactionBuffer)
    {
        float time = TimeBetweenRows(rowSpacing, fallSpeed) + reactionBuffer;
        float worldDistance = playerMoveSpeed * time;
        float colWidth = RushColumns.ColumnWidth;
        return colWidth > 0.001f ? worldDistance / colWidth : RushColumns.Count;
    }

    /// <summary>
    /// What fraction (0–1) of the player's movement budget is consumed by
    /// moving <paramref name="columnDelta"/> columns between rows.
    /// </summary>
    public static float MovementDemand(int columnDelta, float rowSpacing, float fallSpeed, float playerMoveSpeed, float reactionBuffer)
    {
        float reachable = ColumnsReachable(rowSpacing, fallSpeed, playerMoveSpeed, reactionBuffer);
        return reachable > 0.001f ? Mathf.Clamp01(columnDelta / reachable) : 1f;
    }

    /// <summary>
    /// Maximum column-delta the player can cover between rows at a given
    /// difficulty demand fraction (e.g. 0.7 = 70% of budget).
    /// </summary>
    public static int MaxColumnDelta(float demandFraction, float rowSpacing, float fallSpeed, float playerMoveSpeed, float reactionBuffer)
    {
        float reachable = ColumnsReachable(rowSpacing, fallSpeed, playerMoveSpeed, reactionBuffer);
        return Mathf.FloorToInt(reachable * Mathf.Clamp01(demandFraction));
    }

    /// <summary>
    /// Target movement demand for a difficulty tier (0–1).
    /// Easy ≈ 0.35, Medium ≈ 0.55, Hard ≈ 0.75, VeryHard ≈ 0.85.
    /// </summary>
    public static float TargetDemand(float complexPatternWeight)
    {
        // complexPatternWeight goes from 0.2 (easy) to 1.0 (hardest).
        // Map to demand: 0.3 → 0.85.
        return Mathf.Lerp(0.30f, 0.85f, Mathf.Clamp01(complexPatternWeight));
    }
}
