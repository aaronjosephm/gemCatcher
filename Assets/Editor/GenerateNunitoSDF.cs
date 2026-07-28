using UnityEngine;
using UnityEditor;
using TMPro;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// One-shot editor script that generates a TMP SDF font asset from the Nunito TTF
/// on first load. Run via menu: Tools → Generate Nunito SDF Font.
/// After generating, the asset is saved to Assets/Resources/Fonts/Nunito SDF.asset
/// so it can be loaded at runtime with Resources.Load.
/// </summary>
public static class GenerateNunitoSDF
{
    [MenuItem("Tools/Generate Nunito SDF Font")]
    public static void Generate()
    {
        string ttfPath = "Assets/Fonts/Nunito.ttf";
        string outputPath = "Assets/Resources/Fonts/Nunito SDF.asset";

        // Check if already generated
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outputPath) != null)
        {
            Debug.Log("Nunito SDF already exists at " + outputPath);
            return;
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (sourceFont == null)
        {
            Debug.LogError("Could not find Nunito.ttf at " + ttfPath);
            return;
        }

        // Generate the font asset using TMP's API
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            64,     // sampling point size
            5,      // padding
            GlyphRenderMode.SDFAA,
            2048,   // atlas width
            2048    // atlas height
        );

        if (fontAsset == null)
        {
            Debug.LogError("Failed to create Nunito SDF font asset.");
            return;
        }

        // Ensure output directory exists
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts"))
            AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

        AssetDatabase.CreateAsset(fontAsset, outputPath);

        // Also save the atlas texture as a sub-asset
        if (fontAsset.atlasTexture != null)
        {
            fontAsset.atlasTexture.name = "Nunito SDF Atlas";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
        }

        // Save the material as a sub-asset
        if (fontAsset.material != null)
        {
            fontAsset.material.name = "Nunito SDF Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("✓ Nunito SDF font asset generated at " + outputPath);
    }
}
