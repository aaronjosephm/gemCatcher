using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Generates the shared Fredoka One TMP font asset and applies it to shipping scenes.
/// </summary>
public static class GenerateGameFontSDF
{
    private const string SourceFontPath = "Assets/Fonts/FredokaOne-Regular.ttf";
    private const string OutputPath = "Assets/Resources/Fonts/Fredoka One SDF.asset";
    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    [MenuItem("Tools/Generate Game Font (Fredoka One)")]
    public static void Generate()
    {
        if (!Application.isBatchMode &&
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("Fredoka One generation canceled; no scenes were changed.");
            return;
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            throw new System.IO.FileNotFoundException(
                $"Could not find Fredoka One at {SourceFontPath}.");
        }

        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath);
        if (fontAsset == null)
        {
            fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
            if (fontAsset == null)
            {
                throw new System.InvalidOperationException(
                    "Failed to create the Fredoka One SDF asset.");
            }

            fontAsset.name = "Fredoka One SDF";
            fontAsset.atlasTexture.name = "Fredoka One SDF Atlas";
            fontAsset.material.name = "Fredoka One SDF Material";
            AssetDatabase.CreateAsset(fontAsset, OutputPath);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts"))
            AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fontAsset.isMultiAtlasTexturesEnabled = true;

        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset");
        fontAsset.fallbackFontAssetTable = fallback == null
            ? new List<TMP_FontAsset>()
            : new List<TMP_FontAsset> { fallback };

        GameTextStyle.ConfigureMaterial(fontAsset.material);
        EditorUtility.SetDirty(fontAsset);
        EditorUtility.SetDirty(fontAsset.material);

        SetTmpDefaultFont(fontAsset);
        ApplyToShippingScenes(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Fredoka One SDF font asset generated at {OutputPath}.");
    }

    private static void SetTmpDefaultFont(TMP_FontAsset fontAsset)
    {
        TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (settings == null)
        {
            throw new System.IO.FileNotFoundException($"Could not find {TmpSettingsPath}.");
        }

        var serializedSettings = new SerializedObject(settings);
        SerializedProperty defaultFont = serializedSettings.FindProperty("m_defaultFontAsset");
        defaultFont.objectReferenceValue = fontAsset;
        serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    private static void ApplyToShippingScenes(TMP_FontAsset fontAsset)
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled)
                {
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        text.font = fontAsset;
                        text.fontSharedMaterial = fontAsset.material;
                        EditorUtility.SetDirty(text);
                    }
                }

                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (previousSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }
    }
}
