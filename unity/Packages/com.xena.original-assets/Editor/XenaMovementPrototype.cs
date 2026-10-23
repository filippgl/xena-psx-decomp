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
            public int terrainTriangles, originalCollisionTriangles, inputActions;
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

        public static void RunBatch()
        {
            var report = new Report { unityVersion = Application.unityVersion, scene = ScenePath };
            try
            {
                AssetDatabase.ImportAsset(XenaImportVerification.ModelPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset("Assets/Imported/Level01/Level01.xenamodel", ImportAssetOptions.ForceSynchronousImport);
                CreateScene();
                report.terrainTriangles = GameObject.Find("OriginalLevel01").GetComponentsInChildren<MeshCollider>().Single().sharedMesh.triangles.Length / 3;
                report.originalCollisionTriangles = GameObject.Find("OriginalCLSWalls").GetComponent<MeshCollider>().sharedMesh.triangles.Length / 6;
                report.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Packages/com.xena.original-assets/Input/XenaPrototype.inputactions").actionMaps[0].actions.Count;
                if (report.terrainTriangles != 2290 || report.originalCollisionTriangles != 119 || report.inputActions != 7) throw new InvalidDataException("Prototype scene totals differ");
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
