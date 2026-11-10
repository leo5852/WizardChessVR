#if UNITY_ANDROID
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace WizardChess.EditorTools
{
    /// <summary>
    /// Stockfish runs as a separate process from the app's nativeLibraryDir.
    /// With minSdk >= 23, AGP stores .so files uncompressed inside the APK and does NOT extract them,
    /// which leaves nativeLibraryDir empty. Forcing legacy packaging makes the installer extract
    /// libstockfish.so to disk with execute permission.
    /// </summary>
    public class StockfishAndroidBuildFix : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        // 'path' points to the unityLibrary module; the launcher module is its sibling.
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string gradlePath = Path.Combine(path, "..", "launcher", "build.gradle");
            if (!File.Exists(gradlePath))
            {
                Debug.LogWarning("[StockfishAndroidBuildFix] launcher/build.gradle not found: " + gradlePath);
                return;
            }

            string text = File.ReadAllText(gradlePath);
            if (text.Contains("useLegacyPackaging")) return;   // already configured (by Unity or a template)

            text += "\n// Added by StockfishAndroidBuildFix: extract native libs so libstockfish.so can be executed\n"
                  + "android {\n    packaging {\n        jniLibs {\n            useLegacyPackaging = true\n        }\n    }\n}\n";
            File.WriteAllText(gradlePath, text);
        }
    }
}
#endif
