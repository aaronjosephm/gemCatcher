using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedurally generates crystal-themed button visuals at runtime:
/// rounded rectangle with a gradient fill, a bright inner border that
/// simulates a gem-facet glow, and a subtle outer shadow. No sprite
/// assets required.
/// </summary>
public static class CrystalButtonStyle
{
    /// <summary>
    /// Applies crystal styling to an existing button GameObject.
    /// Expects the button to have an Image component (background)
    /// and a child TextMeshProUGUI (label).
    /// </summary>
    public static void Apply(GameObject btnGo, Color baseColor)
    {
        Image bg = btnGo.GetComponent<Image>();
        if (bg == null) return;

        // Generate a rounded-rect sprite with gradient + glow border.
        int w = 512, h = 128;
        Texture2D tex = GenerateCrystalTexture(w, h, baseColor);
        bg.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
        bg.type = Image.Type.Sliced;
        bg.color = Color.white; // Texture carries the color now.

        // Set up button color transitions for a polished feel.
        Button btn = btnGo.GetComponent<Button>();
        if (btn != null)
        {
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.1f;
            btn.colors = cb;
        }

        // Add an Outline component for a subtle outer glow.
        Outline outline = btnGo.GetComponent<Outline>();
        if (outline == null) outline = btnGo.AddComponent<Outline>();
        Color glowColor = baseColor * 1.4f;
        glowColor.a = 0.4f;
        outline.effectColor = glowColor;
        outline.effectDistance = new Vector2(2f, -2f);

        // Style the label text.
        var tmp = btnGo.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.color = Color.white;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            // Add a subtle shadow to the text for readability.
            tmp.outlineWidth = 0.15f;
            tmp.outlineColor = new Color32(0, 0, 0, 100);
        }
    }

    static Texture2D GenerateCrystalTexture(int w, int h, Color baseColor)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float cornerRadius = 24f;
        float borderWidth = 3f;

        Color topColor = Color.Lerp(baseColor, Color.white, 0.3f);
        Color bottomColor = Color.Lerp(baseColor, Color.black, 0.2f);
        Color borderColor = Color.Lerp(baseColor, Color.white, 0.6f);
        borderColor.a = 0.9f;

        Color[] pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float fx = x, fy = y;

                // Rounded rect SDF.
                float dx = Mathf.Max(cornerRadius - fx, fx - (w - 1 - cornerRadius), 0);
                float dy = Mathf.Max(cornerRadius - fy, fy - (h - 1 - cornerRadius), 0);
                float dist = Mathf.Sqrt(dx * dx + dy * dy) - cornerRadius;

                if (dist > 1f)
                {
                    // Outside the rounded rect.
                    pixels[y * w + x] = Color.clear;
                    continue;
                }

                // Vertical gradient fill.
                float t = (float)y / h;
                Color fill = Color.Lerp(bottomColor, topColor, t);

                // Inner highlight at top edge for gem-facet shine.
                float topHighlight = Mathf.Clamp01((float)(y - (h - 12)) / 8f);
                fill = Color.Lerp(fill, Color.white, topHighlight * 0.35f);

                // Bright border glow.
                float borderDist = -dist; // positive inside
                if (borderDist < borderWidth)
                {
                    float borderT = Mathf.Clamp01(borderDist / borderWidth);
                    fill = Color.Lerp(borderColor, fill, borderT);
                }

                // Anti-alias the edge.
                float alpha = Mathf.Clamp01(1f - dist);
                fill.a = alpha;

                pixels[y * w + x] = fill;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
