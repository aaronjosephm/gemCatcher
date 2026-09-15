#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_ANDROID
using UnityEditor.Android;
#endif

#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace QuickSlickLabs.EditorTools
{
    // These flags defer measurement until the runtime explicitly calls
    // MobileAds.Initialize after the selected privacy path permits ad requests.
    internal static class AdPrivacyPlatformSettings
    {
        internal const string AndroidDelayMeasurementKey =
            "com.google.android.gms.ads.DELAY_APP_MEASUREMENT_INIT";
        internal const string IosDelayMeasurementKey = "GADDelayAppMeasurementInit";
    }

#if UNITY_ANDROID
    internal sealed class AdPrivacyAndroidManifestProcessor
        : IPostGenerateGradleAndroidProject
    {
        private static readonly XNamespace AndroidNamespace =
            "http://schemas.android.com/apk/res/android";
        private static readonly XNamespace ToolsNamespace =
            "http://schemas.android.com/tools";

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string basePath)
        {
            string manifestPath = Path.Combine(basePath, "src/main/AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                throw new BuildFailedException(
                    $"Generated Android manifest was not found at {manifestPath}.");
            }

            XDocument document = XDocument.Load(manifestPath);
            XElement manifest = document.Root;
            XElement application = manifest?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "application");
            if (manifest == null || application == null)
            {
                throw new BuildFailedException(
                    "Generated Android manifest does not contain an application element.");
            }

            manifest.SetAttributeValue(XNamespace.Xmlns + "tools", ToolsNamespace);

            XElement metadata = application
                .Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "meta-data"
                    && (string)element.Attribute(AndroidNamespace + "name")
                        == AdPrivacyPlatformSettings.AndroidDelayMeasurementKey);
            if (metadata == null)
            {
                metadata = new XElement("meta-data");
                application.Add(metadata);
            }

            metadata.SetAttributeValue(
                AndroidNamespace + "name",
                AdPrivacyPlatformSettings.AndroidDelayMeasurementKey);
            metadata.SetAttributeValue(AndroidNamespace + "value", true);
            metadata.SetAttributeValue(ToolsNamespace + "replace", "android:value");
            document.Save(manifestPath);

            Debug.Log(
                "[AdPrivacyBuildProcessor] Android app measurement will remain delayed "
                + "until the consent-gated MobileAds.Initialize call.");
        }
    }
#endif

#if UNITY_IOS
    internal sealed class AdPrivacyIosPlistProcessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 1000;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;

            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                throw new BuildFailedException(
                    $"Generated iOS Info.plist was not found at {plistPath}.");
            }

            var document = new PlistDocument();
            document.ReadFromFile(plistPath);
            document.root.SetBoolean(
                AdPrivacyPlatformSettings.IosDelayMeasurementKey,
                true);
            document.WriteToFile(plistPath);

            Debug.Log(
                "[AdPrivacyBuildProcessor] iOS app measurement will remain delayed "
                + "until the consent-gated MobileAds.Initialize call.");
        }
    }
#endif
}
#endif
