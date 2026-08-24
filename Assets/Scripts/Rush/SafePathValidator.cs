using UnityEngine;

/// <summary>
/// Validates that a <see cref="WaveDefinition"/> is fair: every
/// consecutive pair of rows has a safe corridor the player can
/// physically reach given horizontal movement speed, fall speed,
/// and row spacing.
/// </summary>
public static class SafePathValidator
{
    /// <summary>
    /// Returns true if the wave has at least one reachable safe path
    /// from row to row. Returns false if any transition is impossible.
    /// </summary>
    public static bool Validate(
        WaveDefinition wave,
        RushConfig config,
        float areaLeft,
        float areaRight)
    {
        if (wave == null || wave.rows.Count == 0) return false;

        for (int i = 0; i < wave.rows.Count; i++)
        {
            var row = wave.rows[i];

            // Each row must have a corridor at least as wide as the minimum.
            if (row.SafeWidth < config.minSafeCorridorWidth - 0.01f)
            {
                if (config.logValidation)
                    Debug.LogWarning($"[Validator] Row {i}: corridor too narrow ({row.SafeWidth:F2} < {config.minSafeCorridorWidth:F2})");
                return false;
            }

            // Corridor must be within play area.
            if (row.safeMinX < areaLeft - 0.1f || row.safeMaxX > areaRight + 0.1f)
            {
                if (config.logValidation)
                    Debug.LogWarning($"[Validator] Row {i}: corridor outside play area");
                return false;
            }

            // Check reachability from previous row.
            if (i > 0)
            {
                var prev = wave.rows[i - 1];
                float dy = Mathf.Abs(row.yOffset - prev.yOffset);
                float travelTime = dy / Mathf.Max(wave.fallSpeed, 0.1f);
                float reachableDistance = config.playerMoveSpeed * (travelTime + config.reactionTimeBuffer);

                // Can the player reach the new corridor from anywhere in the previous corridor?
                float closestPrevToNew = ClosestDistance(prev.safeMinX, prev.safeMaxX, row.safeMinX, row.safeMaxX);

                if (closestPrevToNew > reachableDistance)
                {
                    if (config.logValidation)
                        Debug.LogWarning($"[Validator] Row {i}: unreachable (need {closestPrevToNew:F2}, can travel {reachableDistance:F2})");
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Minimum horizontal distance a player in interval [aMin,aMax]
    /// needs to travel to enter interval [bMin,bMax]. Zero if they overlap.
    /// </summary>
    static float ClosestDistance(float aMin, float aMax, float bMin, float bMax)
    {
        if (aMax < bMin) return bMin - aMax;
        if (bMax < aMin) return aMin - bMax;
        return 0f; // Overlapping.
    }
}
