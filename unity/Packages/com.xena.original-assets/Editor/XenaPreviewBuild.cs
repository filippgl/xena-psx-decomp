using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Xena.Assets.Editor
{
    public static class XenaPreviewBuild
    {
        [Serializable] private sealed class Report
        {
            public string result = "FAIL";
            public string unityVersion;
            public string executable;
            public string error;
            public ulong bytes;
            public int errors;
            public string scope = "Windows x64 character asset viewer, original animations and texture toggle; no gameplay or level traversal.";
        }

        public static void RunBatch()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build"));
            var report = new Report { unityVersion = Application.unityVersion,
                executable = Path.Combine(root, "unity-preview", "XenaAssetPreview.exe") };
            try
            {
                XenaImportVerification.CreatePreviewScene();
                Directory.CreateDirectory(Path.GetDirectoryName(report.executable));
                BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { "Assets/Scenes/XenaAssetPreview.unity" },
                    locationPathName = report.executable,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });
                report.bytes = build.summary.totalSize;
                report.errors = (int)build.summary.totalErrors;
                if (build.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Windows build: " + build.summary.result);
                report.result = "PASS";
            }
            catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
            Directory.CreateDirectory(Path.Combine(root, "unity-import"));
            File.WriteAllText(Path.Combine(root, "unity-import", "build-verification.json"), JsonUtility.ToJson(report, true));
            EditorApplication.Exit(report.result == "PASS" ? 0 : 1);
        }
    }
}
