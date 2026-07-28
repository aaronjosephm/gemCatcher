using UnityEngine;

/// <summary>
/// Attached to each decorative rock parent. Changes the gem children's
/// material tint and glow color based on the currently selected level.
/// Configure which rock this is (left or right) in the Inspector.
/// </summary>
[ExecuteAlways]
public class DecorativeRockGems : MonoBehaviour
{
    public enum RockSide { Left, Right }

    [Tooltip("Which side of the screen this rock is on.")]
    public RockSide side = RockSide.Left;

    void Start()
    {
        ApplyLevelTheme();
    }

    void OnEnable()
    {
        ApplyLevelTheme();
    }

    void ApplyLevelTheme()
    {
        var level = LevelManager.SelectedLevel;

        Color gemTint;
        Color glowColor;

        if (level == LevelManager.LevelId.Jungle)
        {
            if (side == RockSide.Left)
            {
                gemTint = new Color(0.3f, 0.5f, 1f);   // Blue tint
                glowColor = new Color(0.3f, 0.6f, 1f); // Blue glow
            }
            else
            {
                gemTint = Color.white;                   // Rainbow = white base (natural colors)
                glowColor = Color.white;                 // White glow
            }
        }
        else // Cave
        {
            if (side == RockSide.Left)
            {
                gemTint = new Color(0.2f, 0.9f, 0.3f);  // Green
                glowColor = new Color(0.2f, 0.9f, 0.3f);
            }
            else
            {
                gemTint = new Color(0.95f, 0.2f, 0.2f);  // Red
                glowColor = new Color(0.95f, 0.2f, 0.2f);
            }
        }

        // Apply to all child gem renderers and their StaticGemGlow components.
        foreach (Transform child in transform)
        {
            // Tint the gem material
            var mr = child.GetComponent<MeshRenderer>();
            if (mr != null && mr.material != null)
            {
                if (mr.material.HasProperty("_Color"))
                    mr.material.SetColor("_Color", gemTint);
                else if (mr.material.HasProperty("_BaseColor"))
                    mr.material.SetColor("_BaseColor", gemTint);
            }

            // Update glow color
            var glow = child.GetComponent<StaticGemGlow>();
            if (glow != null)
            {
                glow.glowColor = glowColor;
                // Enable rainbow cycling for the right rock in jungle level
                glow.rainbowCycle = (level == LevelManager.LevelId.Jungle && side == RockSide.Right);
            }
        }
    }
}
