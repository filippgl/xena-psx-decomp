using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Xena.Assets.Editor
{
    public static class XenaImportVerification
    {
        public const string ModelPath = "Assets/Imported/Xena/Xena.xenamodel";
        private const string ScenePath = "Assets/Scenes/XenaAssetPreview.unity";
        private const string LevelModelPath = "Assets/Imported/Level01/Level01.xenamodel";
        private const string LevelScenePath = "Assets/Scenes/Level01AssetPreview.unity";

        [Serializable] private sealed class Report
        {
            public string result;
            public string unityVersion;
            public int meshes;
            public int clips;
            public int triangles;
            public int sampledFrames;
            public int comparedTransforms;
            public string scene;
            public string note;
            public string error;
            public int levelMeshes;
            public int levelTriangles;
            public int levelLightingValues;
            public string levelScene;
        }

        [MenuItem("Tools/Xena/Create Asset Preview Scene")]
        public static void CreatePreviewScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigurePipeline();
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new InvalidOperationException("Generate/import Xena.xenamodel first");
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            instance.name = "Xena";
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.075f);
            camera.fieldOfView = 35;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50;
            cameraObject.transform.position = new Vector3(0, 0.9f, -3.5f);
            cameraObject.transform.LookAt(new Vector3(0, 0.9f, 0));
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Cannot save preview scene");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = instance;
        }

        [MenuItem("Tools/Xena/Create Level 01 Resource Preview Scene")]
        public static void CreateLevelPreviewScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigurePipeline();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelModelPath);
            if (prefab == null) throw new InvalidOperationException("Generate/import Level01.xenamodel first");
            if (File.Exists(LevelScenePath)) { EditorSceneManager.OpenScene(LevelScenePath); return; }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "Level01";
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidDataException("Level preview has no renderers");
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            float extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z, 1);
            cameraObject.transform.position = bounds.center + new Vector3(0.35f, 1.0f, -1.0f).normalized * extent * 3;
            cameraObject.transform.LookAt(bounds.center);
            camera.orthographic = true; camera.orthographicSize = extent * 1.05f;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = extent * 12;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.04f, 0.06f, 0.08f);
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, LevelScenePath);
            AssetDatabase.SaveAssets(); Selection.activeGameObject = instance;
        }

        private static void ConfigurePipeline()
        {
            RenderPipelineAsset current = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            if (current != null)
            {
                if (!(current is UniversalRenderPipelineAsset)) throw new InvalidOperationException("Xena preview needs URP; the existing pipeline was not changed");
                return;
            }
            Directory.CreateDirectory("Assets/Generated");
            AssetDatabase.Refresh();
            const string rendererPath = "Assets/Generated/XenaRenderer.asset";
            const string pipelinePath = "Assets/Generated/XenaURP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "XenaRenderer";
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "XenaURP";
                pipeline.msaaSampleCount = 1;
                pipeline.supportsHDR = false;
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
        }

        // -batchmode -nographics -projectPath ... -executeMethod Xena.Assets.Editor.XenaImportVerification.RunBatch
        public static void RunBatch()
        {
            var report = new Report { result = "FAIL", unityVersion = Application.unityVersion,
                note = "Asset import and native AnimationClip sampling only; no GPU rendering or gameplay validation." };
            GameObject instance = null;
            try
            {
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                XenaDocument document = JsonUtility.FromJson<XenaDocument>(File.ReadAllText(ModelPath));
                XenaValidationSummary summary = XenaDocumentValidator.Validate(document);
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (prefab == null) throw new InvalidDataException("Importer did not create the model prefab");
                report.meshes = assets.OfType<Mesh>().Count(); report.clips = assets.OfType<AnimationClip>().Count();
                report.triangles = assets.OfType<Mesh>().Sum(mesh => mesh.triangles.Length / 3);
                if (report.meshes != summary.Meshes || report.clips != summary.Clips || report.triangles != summary.Triangles)
                    throw new InvalidDataException("Native Unity subasset counts differ from the source");
                instance = UnityEngine.Object.Instantiate(prefab);
                var transforms = new Transform[document.nodes.Length];
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i] = instance.transform.Find(XenaModelImporter.NodePath(document, i));
                    if (transforms[i] == null) throw new InvalidDataException("Missing imported transform " + i);
                }
                var clips = assets.OfType<AnimationClip>().ToDictionary(clip => clip.name);
                foreach (XenaClip source in document.clips)
                {
                    AnimationClip clip = clips[source.name];
                    if (Mathf.Abs(clip.length - source.times[source.times.Length - 1]) > 0.0001f)
                        throw new InvalidDataException("Animation duration differs: " + source.name);
                    for (int frame = 0; frame < source.sourceFrameCount; frame++)
                    {
                        // Reset defaults, so an earlier clip cannot conceal missing bindings.
                        for (int node = 0; node < transforms.Length; node++) ApplyDefault(transforms[node], document.nodes[node]);
                        clip.SampleAnimation(instance, source.times[frame]);
                        foreach (XenaChannel channel in source.channels)
                        {
                            int at = frame * channel.components;
                            Transform target = transforms[channel.node];
                            if (channel.property == "rotation")
                            {
                                var expected = new Quaternion(channel.values[at], channel.values[at + 1], channel.values[at + 2], channel.values[at + 3]);
                                if (Mathf.Abs(Quaternion.Dot(target.localRotation, expected)) < 0.99999f)
                                    throw new InvalidDataException("Rotation mismatch " + source.name + " frame " + frame);
                            }
                            else
                            {
                                var expected = new Vector3(channel.values[at], channel.values[at + 1], channel.values[at + 2]);
                                Vector3 actual = channel.property == "translation" ? target.localPosition : target.localScale;
                                if (Vector3.Distance(actual, expected) > 0.0001f)
                                    throw new InvalidDataException("Transform mismatch " + source.name + " frame " + frame);
                            }
                            report.comparedTransforms++;
                        }
                        report.sampledFrames++;
                    }
                }
                UnityEngine.Object.DestroyImmediate(instance); instance = null;
                CreatePreviewScene(); report.scene = ScenePath;
                if (File.Exists(LevelModelPath))
                {
                    AssetDatabase.ImportAsset(LevelModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    XenaDocument level = JsonUtility.FromJson<XenaDocument>(File.ReadAllText(LevelModelPath));
                    XenaValidationSummary levelSummary = XenaDocumentValidator.Validate(level);
                    UnityEngine.Object[] levelAssets = AssetDatabase.LoadAllAssetsAtPath(LevelModelPath);
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(LevelModelPath) == null) throw new InvalidDataException("Level prefab missing");
                    report.levelMeshes = levelAssets.OfType<Mesh>().Count();
                    report.levelTriangles = levelAssets.OfType<Mesh>().Sum(mesh => mesh.triangles.Length / 3);
                    if (report.levelMeshes != levelSummary.Meshes || report.levelTriangles != levelSummary.Triangles || levelAssets.OfType<AnimationClip>().Any())
                        throw new InvalidDataException("Level import subasset totals differ");
                    foreach (XenaMesh mesh in level.meshes)
                    {
                        Mesh imported = levelAssets.OfType<Mesh>().Single(item => item.name == mesh.name);
                        Color32[] colors = imported.colors32;
                        int vertex = 0;
                        foreach (XenaPrimitive primitive in mesh.primitives)
                            for (int i = 0; i < primitive.lightingRgb.Length; i += 3)
                            {
                                Color32 color = colors[vertex++];
                                if (color.r != primitive.lightingRgb[i] || color.g != primitive.lightingRgb[i + 1] || color.b != primitive.lightingRgb[i + 2] || color.a != 255)
                                    throw new InvalidDataException("Native Unity lighting byte mismatch: " + mesh.name);
                                report.levelLightingValues += 3;
                            }
                        if (colors.Length != vertex) throw new InvalidDataException("Native Unity lighting vertex count mismatch");
                    }
                    CreateLevelPreviewScene(); report.levelScene = LevelScenePath;
                }
                report.result = "PASS";
            }
            catch (Exception error)
            {
                report.error = error.ToString(); Debug.LogException(error);
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/unity-import"));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "editor-verification.json"), JsonUtility.ToJson(report, true));
            }
            EditorApplication.Exit(report.result == "PASS" ? 0 : 1);
        }

        private static void ApplyDefault(Transform target, XenaNode source)
        {
            target.localPosition = new Vector3(source.translation[0], source.translation[1], source.translation[2]);
            target.localRotation = new Quaternion(source.rotation[0], source.rotation[1], source.rotation[2], source.rotation[3]);
            target.localScale = new Vector3(source.scale[0], source.scale[1], source.scale[2]);
        }
    }
}
