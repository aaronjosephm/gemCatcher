using UnityEngine;

/// <summary>
/// Shared utility defining the 5-column grid for Rush Mode.
/// All spawning and catcher movement snaps to these positions.
/// </summary>
public static class RushColumns
{
    public const int Count = 4;

    /// <summary>
    /// Get the world-X positions for all columns, evenly spaced across
    /// the play area with padding on each side.
    /// </summary>
    public static float[] GetColumnPositions()
    {
        float left = ScreenPadding.WorldLeft + 0.5f;
        float right = ScreenPadding.WorldRight - 0.5f;
        float[] positions = new float[Count];
        float step = (right - left) / (Count - 1);
        for (int i = 0; i < Count; i++)
        {
            positions[i] = left + step * i;
        }
        return positions;
    }

    /// <summary>Get the X position for a specific column index (0-based).</summary>
    public static float GetColumnX(int column)
    {
        float left = ScreenPadding.WorldLeft + 0.5f;
        float right = ScreenPadding.WorldRight - 0.5f;
        float step = (right - left) / (Count - 1);
        return left + step * Mathf.Clamp(column, 0, Count - 1);
    }

    /// <summary>Width of one column (distance between adjacent columns).</summary>
    public static float ColumnWidth
    {
        get
        {
            float left = ScreenPadding.WorldLeft + 0.5f;
            float right = ScreenPadding.WorldRight - 0.5f;
            return (right - left) / (Count - 1);
        }
    }
}
