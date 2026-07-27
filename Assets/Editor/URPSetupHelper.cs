#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-shot editor helper that creates a URP Pipeline Asset and Renderer,
/// assigns them to Graphics + Quality settings, and triggers the built-in
/// material converter. Run via the menu: Tools > Setup URP Pipeline.
///
/// After running, you can delete this script -- it's not needed at runtime.
/// </summary>
public static class URPSetupHelper
{
    private const string PipelinePath = "Assets/Settings/URPPipelineAsset.asset";
    private const string RendererPath = "Assets/Settings/URPRendererData.asset";

    [MenuItem("Tools/Setup URP Pipeline")]
    public static void SetupURP()
    {
        // Ensure the Settings folder exists.
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        // Create the Universal Renderer Data asset if it doesn't exist.
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, RendererPath);
            Debug.Log($"[URPSetup] Created renderer data at {RendererPath}");
        }

        // Create the URP Pipeline Asset if it doesn't exist.
        var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipelineAsset == null)
        {
            // Unity 6 API: create pipeline asset referencing the renderer.
            pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipelineAsset, PipelinePath);
            Debug.Log($"[URPSetup] Created pipeline asset at {PipelinePath}");
        }

        // Configure the pipeline for a mobile-friendly 2.5D game.
        pipelineAsset.renderScale = 1f;
        pipelineAsset.supportsHDR = true;
        // Shadow settings appropriate for a simple gem catcher game.
        pipelineAsset.shadowDistance = 50f;
        EditorUtility.SetDirty(pipelineAsset);

        // Assign to Graphics Settings (default render pipeline).
        GraphicsSettings.defaultRenderPipeline = pipelineAsset;
        Debug.Log("[URPSetup] Assigned pipeline to GraphicsSettings.defaultRenderPipeline");

        // Assign to all Quality levels.
        var qualityNames = QualitySettings.names;
        for (int i = 0; i < qualityNames.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
            QualitySettings.renderPipeline = pipelineAsset;
        }
        // Restore to the highest quality level.
        QualitySettings.SetQualityLevel(qualityNames.Length - 1, applyExpensiveChanges: true);
        Debug.Log($"[URPSetup] Assigned pipeline to all {qualityNames.Length} quality levels");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[URPSetup] URP setup complete! Next steps:\n" +
                  "  1) Open Edit > Rendering > Materials > Convert Built-in Materials to URP\n" +
                  "  2) Enter Play mode to verify visuals\n" +
                  "  3) Delete this script (Assets/Editor/URPSetupHelper.cs) when done");
    }
}
#endif
