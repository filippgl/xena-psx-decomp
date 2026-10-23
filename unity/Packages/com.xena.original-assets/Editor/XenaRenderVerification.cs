using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Xena.Assets.Editor
{
    // Real GPU smoke check, deliberately separate from -nographics import checks.
    public static class XenaRenderVerification
    {
        [Serializable] private sealed class Frame
        {
            public string file;
            public int foregroundPixels;
            public int magentaPixels;
            public string hash;
        }
        [Serializable] private sealed class Report
        {
            public string result = "FAIL";
            public string unityVersion;
            public string graphicsDevice;
            public string graphicsApi;
            public string error;
            public List<Frame> frames = new List<Frame>();
            public string scope = "GPU rendering of imported resources and shader; no gameplay validation.";
        }
        private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/unity-import"));
        private static Report report;
        private static int job;
        private static int retries;

        public static void RunBatch()
        {
            report = new Report { unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString() };
            Directory.CreateDirectory(Output);
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("GPU verification requires graphics; remove -nographics");
                EditorSceneManager.OpenScene("Assets/Scenes/XenaAssetPreview.unity");
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Tick()
        {
            try
            {
                Camera camera = Camera.main;
                if (camera == null) throw new InvalidDataException("Preview camera missing");
                var target = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    UnityEngine.Object.DestroyImmediate(target);
                    if (++retries > 300) throw new InvalidOperationException("URP render requests unavailable");
                    EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); return;
                }
                retries = 0;
                string filename;
                if (job < 3)
                {
                    XenaAssetPreview preview = UnityEngine.Object.FindFirstObjectByType<XenaAssetPreview>();
                    if (preview == null) throw new InvalidDataException("Character preview missing");
                    preview.SetUpscale(job != 1);
                    preview.SampleFrame(job == 2 ? 1 : 0, 0);
                    filename = job == 0 ? "unity-xena-upscale.png" : job == 1 ? "unity-xena-original.png" : "unity-xena-pose.png";
                }
                else if (job < 6)
                {
                    XenaLevelLighting lighting = UnityEngine.Object.FindFirstObjectByType<XenaLevelLighting>();
                    if (lighting == null) throw new InvalidDataException("Level lighting controls missing");
                    lighting.SetLighting(job != 3); lighting.SetBrightness(job == 5 ? 35 : 0);
                    filename = job == 3 ? "unity-level01-unlit.png" : job == 4 ? "unity-level01.png" : "unity-level01-brightness35.png";
                }
                else
                {
                    GameObject.Find("ColorProbe").GetComponent<Renderer>().sharedMaterial.SetFloat("_LightBias", job == 6 ? 0 : 35);
                    filename = job == 6 ? "unity-lighting-neutral-probe.png" : "unity-lighting-bias-probe.png";
                }
                RenderTexture previous = RenderTexture.active;
                Texture2D pixels = null;
                try
                {
                    target.Create(); camera.aspect = 960f / 720;
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    pixels = new Texture2D(960, 720, TextureFormat.RGBA32, false);
                    pixels.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); pixels.Apply();
                    Color32[] colors = pixels.GetPixels32(); Color32 background = colors[0];
                    if (job >= 6)
                    {
                        Color32 center = colors[(pixels.height / 2) * pixels.width + pixels.width / 2];
                        int[] expected = job == 6 ? new[] { 64, 96, 128 } : new[] { 82, 122, 163 };
                        if (Math.Abs(center.r - expected[0]) > 2 || Math.Abs(center.g - expected[1]) > 2 || Math.Abs(center.b - expected[2]) > 2)
                            throw new InvalidDataException("Encoded RGB lighting probe differs: " + center + ", expected " + string.Join(",", expected));
                    }
                    var frame = new Frame { file = filename };
                    foreach (Color32 color in colors)
                    {
                        if (Math.Abs(color.r - background.r) + Math.Abs(color.g - background.g) + Math.Abs(color.b - background.b) > 20) frame.foregroundPixels++;
                        if (color.r > 230 && color.g < 25 && color.b > 230) frame.magentaPixels++;
                    }
                    if (frame.foregroundPixels < 1000) throw new InvalidDataException("Empty render: " + filename);
                    if (frame.magentaPixels > 100) throw new InvalidDataException("Possible shader error: " + filename);
                    byte[] png = pixels.EncodeToPNG();
                    frame.hash = Hash128.Compute(png).ToString();
                    File.WriteAllBytes(Path.Combine(Output, filename), png); report.frames.Add(frame);
                }
                finally
                {
                    RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target);
                    if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                }
                job++;
                if (job == 3) EditorSceneManager.OpenScene("Assets/Scenes/Level01AssetPreview.unity");
                if (job == 6) CreateColorProbe();
                if (job == 8)
                {
                    Shader shader = Shader.Find("Xena/OriginalUnlit");
                    if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidDataException("OriginalUnlit shader has compilation errors");
                    if (report.frames[0].hash == report.frames[1].hash) throw new InvalidDataException("Texture variants render identically");
                    if (report.frames[3].hash == report.frames[4].hash || report.frames[4].hash == report.frames[5].hash)
                        throw new InvalidDataException("Level lighting or brightness has no effect");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CreateColorProbe()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -3);
            camera.orthographic = true; camera.orthographicSize = 1;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.name = "ColorProbe";
            Mesh mesh = UnityEngine.Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
            mesh.colors32 = EnumerableNeutralColors(mesh.vertexCount);
            quad.GetComponent<MeshFilter>().sharedMesh = mesh;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            texture.SetPixels32(new[] { new Color32(64, 96, 128, 255), new Color32(64, 96, 128, 255), new Color32(64, 96, 128, 255), new Color32(64, 96, 128, 255) }); texture.Apply();
            var material = new Material(Shader.Find("Xena/OriginalUnlit"));
            material.SetTexture("_MainTex", texture); material.SetFloat("_UseVertexLighting", 1);
            material.SetFloat("_Cull", 0); quad.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Color32[] EnumerableNeutralColors(int count)
        {
            var colors = new Color32[count];
            for (int i = 0; i < count; i++) colors[i] = new Color32(128, 128, 128, 255);
            return colors;
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            report.result = error == null ? "PASS" : "FAIL";
            if (error != null) { report.error = error.ToString(); Debug.LogException(error); }
            File.WriteAllText(Path.Combine(Output, "render-verification.json"), JsonUtility.ToJson(report, true));
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
