using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Xena.Assets.Editor
{
    public static class XenaMovementPrototype
    {
        public const string ScenePath = "Assets/Scenes/Level01MovementPrototype.unity";
        [Serializable] private sealed class SpawnFile { public Spawn[] records; }
        [Serializable] private sealed class Spawn { public string name; public int[] position; }
        [Serializable] private sealed class Report
        {
            public string result = "FAIL", error, unityVersion, scene, executable;
            public int terrainTriangles, originalCollisionTriangles, inputActions, chakramMeshes, chakramTriangles;
            public ulong bytes;
            public string scope = "Movement prototype using original assets/terrain, approximate capsule collision and tuning; no combat/AI parity.";
        }
        private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/unity-import"));
        private static Vector3 UnityPoint(int[] p) => new Vector3(p[0], -p[1], p[2]) / 400;

        [MenuItem("Tools/Xena/Create Movement Prototype")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            // Configure URP through the existing scene tool, then create a separate scene.
            XenaImportVerification.CreateLevelPreviewScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var levelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/Level01/Level01.xenamodel");
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XenaImportVerification.ModelPath);
            if (levelPrefab == null || playerPrefab == null) throw new InvalidDataException("Import original level and Xena first");
            var level = (GameObject)PrefabUtility.InstantiatePrefab(levelPrefab, scene); level.name = "OriginalLevel01";
            var levelControls = level.GetComponent<XenaLevelLighting>(); if (levelControls != null) levelControls.enabled = false;
            MeshFilter floor = level.GetComponentsInChildren<MeshFilter>().Single(filter => filter.sharedMesh.name == "level-01-object-1");
            floor.gameObject.AddComponent<MeshCollider>().sharedMesh = floor.sharedMesh;
            CreateOriginalWallColliders();
            var records = JsonUtility.FromJson<SpawnFile>(File.ReadAllText("Assets/Imported/Level01/Spawns.json"));
            Vector3 spawn = UnityPoint(records.records.Single(record => record.name == "xena").position) + Vector3.up * 0.03f;
            var root = new GameObject("ControlledXena"); root.layer = 2; root.transform.position = spawn;
            var capsule = root.AddComponent<CharacterController>();
            capsule.height = 1.8f; capsule.radius = 0.28f; capsule.center = new Vector3(0, 0.9f, 0);
            capsule.stepOffset = 0.3f; capsule.slopeLimit = 50; capsule.skinWidth = 0.025f;
            var input = root.AddComponent<PrototypeInput>();
            var profile = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Packages/com.xena.original-assets/Input/XenaPrototype.inputactions");
            if (profile == null) throw new InvalidDataException("Input action profile unavailable");
            input.Configure(profile);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            model.transform.SetParent(root.transform, false); model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (Transform item in model.GetComponentsInChildren<Transform>()) item.gameObject.layer = 2;
            var preview = model.GetComponent<XenaAssetPreview>(); preview.enabled = false;
            var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>(); camera.fieldOfView = 60; camera.nearClipPlane = 0.08f; camera.farClipPlane = 250;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.04f, 0.06f, 0.08f);
            var orbit = cameraObject.AddComponent<PrototypeCamera>(); orbit.Configure(root.transform, input);
            var motor = root.AddComponent<PrototypeMotor>();
            motor.Configure(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Imported/Level01/Terrain.json"), preview, orbit, spawn);
            cameraObject.transform.position = spawn + new Vector3(0, 2, -4); cameraObject.transform.LookAt(spawn + Vector3.up);
            Time.fixedDeltaTime = 1f / 60;
            Directory.CreateDirectory("Assets/Scenes"); AssetDatabase.Refresh();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Cannot save movement scene");
        }

        private static void CreateOriginalWallColliders()
        {
            // JsonUtility does not support jagged arrays; use the verified source
            // snapshot's flat Unity collision export generated by the node tool.
            var data = JsonUtility.FromJson<CollisionMesh>(File.ReadAllText("Assets/Imported/Level01/CollisionMesh.json"));
            var vertices = new Vector3[data.vertices.Length / 3];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(data.vertices[i * 3], -data.vertices[i * 3 + 1], data.vertices[i * 3 + 2]) / 400;
            var indices = new int[vertices.Length * 2];
            for (int i = 0; i < vertices.Length; i += 3)
            { indices[i * 2] = i; indices[i * 2 + 1] = i + 1; indices[i * 2 + 2] = i + 2; indices[i * 2 + 3] = i + 2; indices[i * 2 + 4] = i + 1; indices[i * 2 + 5] = i; }
            const string path = "Assets/Generated/PrototypeCollision.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "OriginalCLS" }; mesh.vertices = vertices; mesh.triangles = indices; mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
            }
            new GameObject("OriginalCLSWalls").AddComponent<MeshCollider>().sharedMesh = mesh;
        }
        [Serializable] private sealed class CollisionMesh { public int[] vertices; }

        // Resources materials keep the Remaster shaders in standalone builds.
        private static void CreateRemasterResources()
        {
            Directory.CreateDirectory("Assets/Imported/Resources/Remaster");
            foreach (var (file, shaderName) in new[] { ("Water", "Xena/RemasterWater"), ("Waterfall", "Xena/RemasterWaterfall"), ("Rainbow", "Xena/RemasterRainbow"), ("LevelLit", "Xena/RemasterLit"), ("Foam", "Xena/RemasterFoam") })
            {
                string path = "Assets/Imported/Resources/Remaster/" + file + ".mat";
                Shader shader = Shader.Find(shaderName); if (shader == null) throw new InvalidDataException(shaderName + " unavailable");
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                // Recreate so tuned shader defaults reach the standalone material.
                if (existing != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(new Material(shader), path);
            }
            const string puffPath = "Assets/Imported/Resources/Remaster/MistPuff.asset";
            var puff = AssetDatabase.LoadAssetAtPath<Texture2D>(puffPath);
            if (puff == null)
            {
                puff = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "MistPuff" };
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) / 32;
                    float a = Mathf.Pow(Mathf.Clamp01(1 - d), 2.2f);
                    puff.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                puff.Apply(true); AssetDatabase.CreateAsset(puff, puffPath);
            }
            // CC0 ambientCG Rock051 (staged from build/remaster-sources/rock051).
            foreach (var (file, normal) in new[] { ("RockColor", false), ("RockNormal", true), ("RockAO", false) })
            {
                string texturePath = "Assets/Imported/Resources/Remaster/" + file + ".jpg";
                if (!(AssetImporter.GetAtPath(texturePath) is TextureImporter rockImporter)) throw new InvalidDataException("Remaster rock texture missing: " + texturePath);
                rockImporter.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                rockImporter.sRGBTexture = file == "RockColor"; rockImporter.wrapMode = TextureWrapMode.Repeat; rockImporter.mipmapEnabled = true;
                rockImporter.maxTextureSize = 2048; rockImporter.SaveAndReimport();
            }
            // Original HUD pixels (tools/export_hud.mjs): uncompressed, point, no mips.
            foreach (string hudPath in Directory.GetFiles("Assets/Imported/Resources/HUD", "*.png", SearchOption.AllDirectories))
                if (AssetImporter.GetAtPath(hudPath.Replace(Path.DirectorySeparatorChar, '/')) is TextureImporter hudImporter)
                {
                    hudImporter.textureCompression = TextureImporterCompression.Uncompressed; hudImporter.mipmapEnabled = false;
                    hudImporter.filterMode = FilterMode.Point; hudImporter.npotScale = TextureImporterNPOTScale.None; hudImporter.alphaIsTransparency = true; hudImporter.SaveAndReimport();
                }
            const string streakPath = "Assets/Imported/Resources/Remaster/FallStreak.asset";
            var streak = AssetDatabase.LoadAssetAtPath<Texture2D>(streakPath);
            if (streak == null)
            {
                // Soft water streak: bright core, frayed sides, tapered ends.
                streak = new Texture2D(32, 128, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FallStreak" };
                for (int y = 0; y < 128; y++) for (int x = 0; x < 32; x++)
                {
                    float u = (x + 0.5f) / 32 * 2 - 1, v = (y + 0.5f) / 128 * 2 - 1;
                    float fray = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.4f, y * 0.08f);
                    float a = Mathf.Exp(-u * u * 5f / fray) * Mathf.Pow(Mathf.Clamp01(1 - v * v), 0.6f);
                    streak.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
                }
                streak.Apply(true); AssetDatabase.CreateAsset(streak, streakPath);
            }
            const string fallMaterialPath = "Assets/Imported/Resources/Remaster/FallStreak.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(fallMaterialPath) == null)
            {
                Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                var fallMaterial = new Material(particles); fallMaterial.SetTexture("_BaseMap", streak);
                fallMaterial.SetFloat("_Surface", 1); fallMaterial.SetFloat("_Blend", 0); fallMaterial.SetOverrideTag("RenderType", "Transparent");
                fallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); fallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fallMaterial.SetInt("_ZWrite", 0); fallMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); fallMaterial.renderQueue = 3150;
                AssetDatabase.CreateAsset(fallMaterial, fallMaterialPath);
            }
            const string mistPath = "Assets/Imported/Resources/Remaster/Mist.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(mistPath) == null)
            {
                Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (particles == null) throw new InvalidDataException("URP particle shader unavailable");
                var mist = new Material(particles); mist.SetTexture("_BaseMap", puff);
                mist.SetFloat("_Surface", 1); mist.SetFloat("_Blend", 0); mist.SetOverrideTag("RenderType", "Transparent");
                mist.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mist.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mist.SetInt("_ZWrite", 0); mist.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mist.renderQueue = 3000;
                AssetDatabase.CreateAsset(mist, mistPath);
            }
            // CC0 Poly Haven HDRI (kloofendal_48d_partly_cloudy_puresky, 4k),
            // staged from build/remaster-sources/sky; Remaster sky only.
            const string skyTexturePath = "Assets/Imported/Remaster/Sky/Sky.hdr";
            if (AssetImporter.GetAtPath(skyTexturePath) is TextureImporter skyImporter)
            {
                skyImporter.mipmapEnabled = false; skyImporter.wrapModeU = TextureWrapMode.Repeat; skyImporter.wrapModeV = TextureWrapMode.Clamp;
                skyImporter.maxTextureSize = 4096; skyImporter.textureCompression = TextureImporterCompression.CompressedHQ; skyImporter.SaveAndReimport();
            }
            var skyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(skyTexturePath);
            if (skyTexture == null) throw new InvalidDataException("Remaster sky HDRI missing: " + skyTexturePath);
            const string skyPath = "Assets/Imported/Resources/Remaster/Sky.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(skyPath) != null) AssetDatabase.DeleteAsset(skyPath);
            Shader panoramic = Shader.Find("Xena/RemasterSky"); if (panoramic == null) throw new InvalidDataException("Remaster sky shader unavailable");
            var sky = new Material(panoramic); sky.SetTexture("_MainTex", skyTexture); sky.SetFloat("_Exposure", 1.05f); sky.SetFloat("_Rotation", 40);
            AssetDatabase.CreateAsset(sky, skyPath);
            // Remaster post: HDR buffer, bloom, ACES, grading, light vignette.
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Generated/XenaURP.asset");
            if (pipeline != null && !pipeline.supportsHDR) { pipeline.supportsHDR = true; EditorUtility.SetDirty(pipeline); }
            if (pipeline != null)
            {
                // Remaster sun: 4 cascades over 70m, 4096 map, soft filtering.
                var settings = new SerializedObject(pipeline);
                settings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
                settings.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
                settings.FindProperty("m_ShadowDistance").floatValue = 70;
                settings.FindProperty("m_ShadowCascadeCount").intValue = 4;
                settings.FindProperty("m_SoftShadowsSupported").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline);
            }
            // The generated renderer had no PostProcessData, which silently disables post.
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Generated/XenaRenderer.asset");
            if (rendererData != null && rendererData.postProcessData == null)
            {
                rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if (rendererData.postProcessData == null) throw new InvalidDataException("URP PostProcessData unavailable");
                EditorUtility.SetDirty(rendererData);
            }
            const string postPath = "Assets/Imported/Resources/Remaster/Post.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(postPath) != null) AssetDatabase.DeleteAsset(postPath);
            var post = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(post, postPath);
            var bloom = post.Add<Bloom>(true); bloom.threshold.value = 0.92f; bloom.intensity.value = 0.55f; bloom.scatter.value = 0.65f;
            var tone = post.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.Neutral; // keeps the original palette
            var grade = post.Add<ColorAdjustments>(true); grade.postExposure.value = 0.5f; grade.contrast.value = 9; grade.saturation.value = 8;
            var vignette = post.Add<Vignette>(true); vignette.intensity.value = 0.12f; vignette.smoothness.value = 0.45f;
            foreach (var component in post.components) { component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(component, post); }
            EditorUtility.SetDirty(post);
            AssetDatabase.SaveAssets();
        }
        public static void RunBatch()
        {
            var report = new Report { unityVersion = Application.unityVersion, scene = ScenePath };
            try
            {
                // A Resources material keeps the trail shader in standalone builds.
                const string trailPath = "Assets/Imported/Resources/Chakram/FlightTrail.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(trailPath) == null)
                {
                    Shader trailShader = Shader.Find("Xena/ChakramTrail");
                    if (trailShader == null) throw new InvalidDataException("Chakram trail shader unavailable");
                    AssetDatabase.CreateAsset(new Material(trailShader), trailPath);
                    AssetDatabase.SaveAssets();
                }
                CreateRemasterResources();
                AssetDatabase.ImportAsset(XenaImportVerification.ModelPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset("Assets/Imported/Level01/Level01.xenamodel", ImportAssetOptions.ForceSynchronousImport);
                const string chakramPath = "Assets/Imported/Resources/Chakram/Chakram.xenamodel";
                AssetDatabase.ImportAsset(chakramPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var chakramPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(chakramPath);
                if (chakramPrefab == null) throw new InvalidDataException("Standalone original chakram failed import");
                var chakramGeometry = chakramPrefab.GetComponentsInChildren<MeshFilter>(true);
                report.chakramMeshes = chakramGeometry.Length;
                report.chakramTriangles = chakramGeometry.Sum(filter => filter.sharedMesh.triangles.Length / 3);
                if (report.chakramMeshes != 2 || report.chakramTriangles != 46) throw new InvalidDataException("Standalone chakram geometry differs");
                CreateScene();
                report.terrainTriangles = GameObject.Find("OriginalLevel01").GetComponentsInChildren<MeshCollider>().Single().sharedMesh.triangles.Length / 3;
                report.originalCollisionTriangles = GameObject.Find("OriginalCLSWalls").GetComponent<MeshCollider>().sharedMesh.triangles.Length / 6;
                report.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Packages/com.xena.original-assets/Input/XenaPrototype.inputactions").actionMaps[0].actions.Count;
                if (report.terrainTriangles != 2290 || report.originalCollisionTriangles != 119 || report.inputActions != 13) throw new InvalidDataException("Prototype scene totals differ");
                if (Environment.GetCommandLineArgs().Contains("--build-movement"))
                {
                    report.executable = Path.GetFullPath(Path.Combine(Output, "../unity-movement/XenaMovementPrototype.exe"));
                    Directory.CreateDirectory(Path.GetDirectoryName(report.executable));
                    var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = report.executable, target = BuildTarget.StandaloneWindows64 });
                    report.bytes = build.summary.totalSize;
                    if (build.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Movement build: " + build.summary.result);
                }
                report.result = "PASS";
            }
            catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, Environment.GetCommandLineArgs().Contains("--build-movement") ? "movement-build-verification.json" : "movement-scene-verification.json"), JsonUtility.ToJson(report, true));
            EditorApplication.Exit(report.result == "PASS" ? 0 : 1);
        }
    }
}
