using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QuickSlickLabs.EditorTools
{
    /// <summary>
    /// One-click Android build helpers for Gem Catcher.
    ///
    /// Menu items live under: Quick Slick Labs / Build
    ///
    /// Before using:
    ///   1. Player Settings -> Publishing Settings: configure the Custom Keystore
    ///      (Keystore path, Keystore password, Alias, Alias password). The keystore
    ///      itself is gitignored; passwords live in UserSettings (also gitignored).
    ///   2. Active build target must be Android (File -> Build Settings -> Android -> Switch Platform).
    ///
    /// Outputs:
    ///   Builds/Android/GemCatcher_v{version}_b{versionCode}.aab  (for Play Store upload)
    ///   Builds/Android/GemCatcher_v{version}_b{versionCode}.apk  (for sideload testing)
    /// </summary>
    public static class BuildScript
    {
        private const string OUTPUT_DIR = "Builds/Android";
        private const string MENU_ROOT = "Quick Slick Labs/Build/";

        [MenuItem(MENU_ROOT + "Android AAB (Release)", priority = 100)]
        public static void BuildAndroidAab()
        {
            BuildAndroid(asAppBundle: true, development: false);
        }

        [MenuItem(MENU_ROOT + "Android APK (Release, sideload)", priority = 101)]
        public static void BuildAndroidApkRelease()
        {
            BuildAndroid(asAppBundle: false, development: false);
        }

        [MenuItem(MENU_ROOT + "Android APK (Development)", priority = 200)]
        public static void BuildAndroidApkDev()
        {
            BuildAndroid(asAppBundle: false, development: true);
        }

        [MenuItem(MENU_ROOT + "Bump Version Code", priority = 300)]
        public static void BumpVersionCode()
        {
            int next = PlayerSettings.Android.bundleVersionCode + 1;
            PlayerSettings.Android.bundleVersionCode = next;
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildScript] Bumped Android bundleVersionCode to {next}");
        }

        [MenuItem(MENU_ROOT + "Reveal Build Folder", priority = 301)]
        public static void RevealBuildFolder()
        {
            Directory.CreateDirectory(OUTPUT_DIR);
            EditorUtility.RevealInFinder(Path.GetFullPath(OUTPUT_DIR));
        }

        [MenuItem(MENU_ROOT + "Diagnose Build Config", priority = 302)]
        public static void DiagnoseSigningConfig()
        {
            string keystorePass = PlayerSettings.Android.keystorePass ?? "";
            string aliasPass = PlayerSettings.Android.keyaliasPass ?? "";
            string keystoreName = PlayerSettings.Android.keystoreName ?? "";
            string aliasName = PlayerSettings.Android.keyaliasName ?? "";

            int targetSdkInt = (int)PlayerSettings.Android.targetSdkVersion;
            int minSdkInt = (int)PlayerSettings.Android.minSdkVersion;
            string targetSdkLabel = targetSdkInt == 0 ? "Auto (highest installed)" : $"API {targetSdkInt}";

            string report =
                "Android Build Diagnostic\n" +
                "========================\n" +
                "[ Identification ]\n" +
                $"  Application ID           : {PlayerSettings.applicationIdentifier}\n" +
                $"  Bundle Version           : {PlayerSettings.bundleVersion}\n" +
                $"  Bundle Version Code      : {PlayerSettings.Android.bundleVersionCode}\n" +
                $"  Company / Product        : {PlayerSettings.companyName} / {PlayerSettings.productName}\n" +
                "\n" +
                "[ SDK / Architecture ]\n" +
                $"  Min SDK                  : API {minSdkInt}\n" +
                $"  Target SDK               : {targetSdkLabel}\n" +
                $"  Scripting Backend        : {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}\n" +
                $"  Target Architectures     : {PlayerSettings.Android.targetArchitectures}\n" +
                "\n" +
                "[ Signing ]\n" +
                $"  Use Custom Keystore      : {PlayerSettings.Android.useCustomKeystore}\n" +
                $"  Keystore path            : {(string.IsNullOrEmpty(keystoreName) ? "(empty)" : keystoreName)}\n" +
                $"  Keystore password set    : {!string.IsNullOrEmpty(keystorePass)} (length {keystorePass.Length})\n" +
                $"  Alias name               : {(string.IsNullOrEmpty(aliasName) ? "(empty)" : aliasName)}\n" +
                $"  Alias password set       : {!string.IsNullOrEmpty(aliasPass)} (length {aliasPass.Length})\n" +
                "\n" +
                "[ Build Output ]\n" +
                $"  Build App Bundle (AAB)   : {EditorUserBuildSettings.buildAppBundle}\n" +
                $"  Active Build Target      : {EditorUserBuildSettings.activeBuildTarget}\n" +
                "\n" +
                "Reminder: Unity caches in-memory; if a value here surprises you, the file on disk\n" +
                "may differ until you do File -> Save Project (Cmd+S).";

            Debug.Log(report);
            EditorUtility.DisplayDialog("Build Diagnostic", report, "OK");
        }

        private static void BuildAndroid(bool asAppBundle, bool development)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUtility.DisplayDialog(
                    "Switch to Android?",
                    $"Active build target is {EditorUserBuildSettings.activeBuildTarget}. Switch to Android now?",
                    "Switch", "Cancel"))
                {
                    return;
                }
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            if (asAppBundle && !development && !ValidateReleaseSigning())
            {
                return;
            }

            Directory.CreateDirectory(OUTPUT_DIR);

            string ext = asAppBundle ? "aab" : "apk";
            string version = string.IsNullOrEmpty(PlayerSettings.bundleVersion) ? "0.0.0" : PlayerSettings.bundleVersion;
            int versionCode = PlayerSettings.Android.bundleVersionCode;
            string filename = $"GemCatcher_v{version}_b{versionCode}{(development ? "_dev" : "")}.{ext}";
            string outputPath = Path.Combine(OUTPUT_DIR, filename);

            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "No scenes",
                    "There are no scenes enabled in Build Settings. Add at least one scene before building.",
                    "OK");
                return;
            }

            EditorUserBuildSettings.buildAppBundle = asAppBundle;
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;

            BuildOptions opts = BuildOptions.None;
            if (development)
            {
                opts |= BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler;
            }

            BuildPlayerOptions buildOpts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = opts,
            };

            Debug.Log($"[BuildScript] Building {ext.ToUpper()} -> {outputPath}");
            Debug.Log($"[BuildScript] Version: {version} (code {versionCode})");
            Debug.Log($"[BuildScript] Scenes: {string.Join(", ", scenes)}");

            BuildReport report = BuildPipeline.BuildPlayer(buildOpts);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                long sizeBytes = (long)summary.totalSize;
                double sizeMb = sizeBytes / (1024.0 * 1024.0);
                Debug.Log($"[BuildScript] SUCCESS in {summary.totalTime} | {sizeMb:F1} MB | {outputPath}");

                if (asAppBundle && !development)
                {
                    VerifyAabSignature(outputPath);
                }

                EditorUtility.RevealInFinder(Path.GetFullPath(outputPath));
            }
            else
            {
                Debug.LogError($"[BuildScript] BUILD FAILED: {summary.result} ({summary.totalErrors} errors)");
            }
        }

        private static bool ValidateReleaseSigning()
        {
            if (PlayerSettings.Android.useCustomKeystore == false)
            {
                EditorUtility.DisplayDialog(
                    "Release signing not configured",
                    "Custom Keystore is OFF.\n\n" +
                    "Open Player Settings -> Publishing Settings, enable 'Custom Keystore', " +
                    "and select your quickslicklabs.keystore file before building a release AAB.",
                    "OK");
                return false;
            }

            if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName) ||
                string.IsNullOrEmpty(PlayerSettings.Android.keyaliasName))
            {
                EditorUtility.DisplayDialog(
                    "Release signing not configured",
                    "Keystore path or alias is missing.\n\n" +
                    "Open Player Settings -> Publishing Settings and fill in:\n" +
                    "  - Keystore (path to .keystore)\n" +
                    "  - Keystore password\n" +
                    "  - Alias name (e.g. 'gemcatcher')\n" +
                    "  - Alias password",
                    "OK");
                return false;
            }

            if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) ||
                string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
            {
                EditorUtility.DisplayDialog(
                    "Release passwords are blank",
                    "Keystore or alias password is empty in Player Settings.\n\n" +
                    "Without these, Unity silently falls back to DEBUG SIGNING and the resulting " +
                    "AAB will be rejected by Google Play with 'app bundle is not signed'.\n\n" +
                    "Fix:\n" +
                    "  1. Player Settings -> Publishing Settings\n" +
                    "  2. Type both Keystore Password and Alias Password\n" +
                    "  3. Tab out of each field (don't press Enter)\n" +
                    "  4. File -> Save Project (Cmd+S)\n" +
                    "  5. Re-run the build",
                    "OK");
                return false;
            }

            return true;
        }

        private static void VerifyAabSignature(string aabPath)
        {
            string jarsigner = FindJarsigner();
            if (string.IsNullOrEmpty(jarsigner))
            {
                Debug.LogWarning("[BuildScript] Could not locate jarsigner; skipping post-build signature verification.");
                return;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = jarsigner,
                    Arguments = $"-verify \"{aabPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(15000);

                bool verified = stdout.Contains("jar verified") || stderr.Contains("jar verified");
                if (verified)
                {
                    Debug.Log($"[BuildScript] Signature verified: {Path.GetFileName(aabPath)}");
                }
                else
                {
                    Debug.LogError(
                        "[BuildScript] SIGNATURE VERIFICATION FAILED. Play Console will reject this AAB.\n" +
                        $"jarsigner stdout:\n{stdout}\njarsigner stderr:\n{stderr}");
                    EditorUtility.DisplayDialog(
                        "Unsigned AAB",
                        "Build completed but jarsigner could not verify the signature. " +
                        "Check the Console for jarsigner output. Do not upload this AAB to Play Console.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildScript] jarsigner verification threw: {ex.Message}");
            }
        }

        private static string FindJarsigner()
        {
            string editorPath = EditorApplication.applicationPath;
            int hubIdx = editorPath.IndexOf("/Editor/", StringComparison.Ordinal);
            if (hubIdx < 0) return null;
            int versionEnd = editorPath.IndexOf('/', hubIdx + 8);
            if (versionEnd < 0) return null;
            string editorRoot = editorPath.Substring(0, versionEnd);
            string candidate = Path.Combine(editorRoot, "PlaybackEngines/AndroidPlayer/OpenJDK/bin/jarsigner");
            return File.Exists(candidate) ? candidate : null;
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.enabled && !string.IsNullOrEmpty(s.path))
                {
                    scenes.Add(s.path);
                }
            }
            return scenes.ToArray();
        }
    }
}
