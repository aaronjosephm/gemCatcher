using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ResetLocalPlayerData
{
    private const string MenuPath = "Tools/Gem Catch/Reset Local Player Data";
    private const string RequestPath = "Temp/GemCatchResetPlayerData.request";
    private const string CompletedPath = "Temp/GemCatchResetPlayerData.completed";

    static ResetLocalPlayerData()
    {
        EditorApplication.delayCall += ProcessPendingReset;
    }

    [MenuItem(MenuPath)]
    public static void RequestReset()
    {
        Directory.CreateDirectory("Temp");
        File.WriteAllText(RequestPath, DateTime.UtcNow.ToString("O"));
        ProcessPendingReset();
    }

    private static void ProcessPendingReset()
    {
        if (!File.Exists(RequestPath)) return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += ProcessPendingReset;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += ProcessPendingReset;
            return;
        }

        PlayerPrefs.DeleteKey("TutorialCompleted");
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        bool tutorialCleared = !PlayerPrefs.HasKey("TutorialCompleted");
        File.Delete(RequestPath);
        File.WriteAllText(
            CompletedPath,
            $"CompletedUtc={DateTime.UtcNow:O}\nTutorialCleared={tutorialCleared}");

        if (tutorialCleared)
        {
            Debug.Log(
                "[ResetLocalPlayerData] Cleared all Gem Catch local player data. "
                + "The next Play session will start as a fresh install.");
        }
        else
        {
            Debug.LogError(
                "[ResetLocalPlayerData] Unity still reports TutorialCompleted "
                + "after clearing PlayerPrefs.");
        }
    }
}
