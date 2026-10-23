using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Xena.Assets
{
    // Optional player-only CLI verification; inactive during ordinary preview use.
    internal sealed class XenaPreviewSmoke : MonoBehaviour
    {
        private string output;
        [Serializable] private sealed class Report
        {
            public string result;
            public string error;
            public string graphicsDevice;
            public int clips;
            public int renderers;
            public string originalHash;
            public string upscaleHash;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--xena-smoke");
            if (index < 0 || index + 1 >= args.Length) return;
            var instance = new GameObject("PreviewSmoke").AddComponent<XenaPreviewSmoke>();
            instance.output = Path.GetFullPath(args[index + 1]);
        }

        private IEnumerator Start()
        {
            // Let the camera and render pipeline finish initialization.
            for (int i = 0; i < 10; i++) yield return null;
            var report = new Report { result = "FAIL", graphicsDevice = SystemInfo.graphicsDeviceName };
            XenaAssetPreview preview = FindFirstObjectByType<XenaAssetPreview>();
            for (int variant = 0; variant < 2; variant++)
            {
                if (preview == null) break;
                preview.enabled = false;
                preview.SampleFrame(0, 0);
                preview.SetUpscale(variant == 1);
                yield return new WaitForEndOfFrame();
                try
                {
                    Directory.CreateDirectory(output);
                    Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
                    byte[] png = image.EncodeToPNG(); Destroy(image);
                    string hash = Hash128.Compute(png).ToString();
                    if (variant == 0) report.originalHash = hash; else report.upscaleHash = hash;
                    File.WriteAllBytes(Path.Combine(output, variant == 0 ? "player-original.png" : "player-upscale.png"), png);
                }
                catch (Exception error) { report.error = error.ToString(); break; }
            }
            if (preview != null)
            {
                report.clips = preview.ClipCount;
                report.renderers = preview.GetComponentsInChildren<Renderer>().Length;
            }
            if (report.error == null && report.clips == 81 && report.renderers == 33 &&
                report.originalHash != null && report.upscaleHash != null && report.originalHash != report.upscaleHash)
                report.result = "PASS";
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "player-verification.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.result == "PASS" ? 0 : 1);
        }
    }
}
