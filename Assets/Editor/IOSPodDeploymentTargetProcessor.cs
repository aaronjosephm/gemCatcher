using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class IOSPodDeploymentTargetProcessor
{
    private const string Marker = "# Gem Catch: synchronize CocoaPods deployment targets";

    // External Dependency Manager generates the Podfile at order 40 and runs
    // pod install at order 50. Inject the hook between those two steps.
    [PostProcessBuild(45)]
    public static void SynchronizePodDeploymentTargets(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;

        string podfilePath = Path.Combine(buildPath, "Podfile");
        if (!File.Exists(podfilePath))
        {
            Debug.LogWarning(
                $"[iOS Build] Podfile not found at '{podfilePath}'; pod deployment targets were not updated.");
            return;
        }

        string contents = File.ReadAllText(podfilePath);
        if (contents.Contains(Marker)) return;

        string deploymentTarget = PlayerSettings.iOS.targetOSVersionString;
        string newline = contents.Contains("\r\n") ? "\r\n" : "\n";
        Match existingHook = Regex.Match(
            contents,
            @"(?m)^(?<indent>[ \t]*)post_install\s+do\s+\|(?<installer>[A-Za-z_][A-Za-z0-9_]*)\|[ \t]*$");

        if (existingHook.Success)
        {
            int insertionIndex = contents.IndexOf('\n', existingHook.Index);
            insertionIndex = insertionIndex < 0 ? contents.Length : insertionIndex + 1;
            string indent = existingHook.Groups["indent"].Value + "  ";
            string installer = existingHook.Groups["installer"].Value;
            string body = BuildHookBody(deploymentTarget, installer, indent, newline);
            contents = contents.Insert(insertionIndex, body);
        }
        else
        {
            if (!contents.EndsWith(newline)) contents += newline;
            contents += newline
                + "post_install do |installer|" + newline
                + BuildHookBody(deploymentTarget, "installer", "  ", newline)
                + "end" + newline;
        }

        File.WriteAllText(podfilePath, contents, new UTF8Encoding(false));
        Debug.Log(
            $"[iOS Build] Set all CocoaPods deployment targets to iOS {deploymentTarget}.");
    }

    private static string BuildHookBody(
        string deploymentTarget,
        string installer,
        string indent,
        string newline)
    {
        string nested = indent + "  ";
        string deepest = nested + "  ";
        return indent + Marker + newline
            + indent + installer + ".pods_project.targets.each do |pod_target|" + newline
            + nested + "pod_target.build_configurations.each do |config|" + newline
            + deepest + "config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '"
            + deploymentTarget + "'" + newline
            + nested + "end" + newline
            + indent + "end" + newline;
    }
}
