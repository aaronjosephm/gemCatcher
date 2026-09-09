using TMPro;
using UnityEngine;

/// <summary>
/// Shared typography for every TextMesh Pro label in the game.
/// </summary>
public static class GameTextStyle
{
    public const string FontResourcePath = "Fonts/Fredoka One SDF";
    public const float OutlineWidth = 0.18f;
    public const float UnderlayOffsetX = 2f;
    public const float UnderlayOffsetY = -4f;
    public const float UnderlayDilate = 0.1f;
    public const float UnderlaySoftness = 0.1f;

    public static readonly Color32 FaceColor = new Color32(0xFF, 0xFD, 0xF4, 0xFF);
    public static readonly Color32 OutlineColor = new Color32(0x07, 0x5E, 0x12, 0xFF);
    public static readonly Color32 UnderlayColor = new Color32(0x03, 0x3F, 0x0B, 0xFF);

    private static TMP_FontAsset fontAsset;
    private static bool missingFontLogged;

    public static TMP_FontAsset FontAsset
    {
        get
        {
            if (fontAsset == null)
            {
                fontAsset = Resources.Load<TMP_FontAsset>(FontResourcePath);
                if (fontAsset != null)
                {
                    ConfigureMaterial(fontAsset.material);
                    if (fontAsset.fallbackFontAssetTable != null)
                    {
                        foreach (TMP_FontAsset fallback in fontAsset.fallbackFontAssetTable)
                        {
                            if (fallback != null)
                            {
                                ConfigureMaterial(fallback.material);
                            }
                        }
                    }
                }
                else if (!missingFontLogged)
                {
                    Debug.LogError(
                        $"[{nameof(GameTextStyle)}] Missing Resources/{FontResourcePath}.asset.");
                    missingFontLogged = true;
                }
            }

            return fontAsset;
        }
    }

    public static void Apply(TMP_Text text)
    {
        if (text == null)
        {
            return;
        }

        TMP_FontAsset fredoka = FontAsset;
        if (fredoka == null)
        {
            return;
        }

        if (text.font != fredoka)
        {
            text.font = fredoka;
        }

        if (text.fontSharedMaterial != fredoka.material)
        {
            text.fontSharedMaterial = fredoka.material;
        }
    }

    public static void ConfigureMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetColor("_FaceColor", FaceColor);
        material.SetColor("_OutlineColor", OutlineColor);
        material.SetFloat("_OutlineWidth", OutlineWidth);
        material.SetColor("_UnderlayColor", UnderlayColor);
        material.SetFloat("_UnderlayOffsetX", UnderlayOffsetX);
        material.SetFloat("_UnderlayOffsetY", UnderlayOffsetY);
        material.SetFloat("_UnderlayDilate", UnderlayDilate);
        material.SetFloat("_UnderlaySoftness", UnderlaySoftness);
        material.EnableKeyword("OUTLINE_ON");
        material.EnableKeyword("UNDERLAY_ON");
    }

    public static void ResetCache()
    {
        fontAsset = null;
        missingFontLogged = false;
    }
}
