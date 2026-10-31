using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster presentation only: the closed shell baked by tools/build_remaster_shell.py replaces the terrain's
    // faces in its region (start pocket). Colliders and the original height query stay; Original restores the
    // original mesh through PrototypeRemasterGeometry, which drops the replaced faces from its Remaster mesh.
    public sealed class PrototypeRemasterShell : MonoBehaviour, IXenaVisualModeTarget
    {
        private static HashSet<Vector3Int> replaced; private static float zCut, xMin, xMax; private static Vector4 mossTint; public static float TrailMeters { get; private set; }
        public static bool Available { get { Load(); return replaced.Count > 0; } }
        private static void Load()
        {
            if (replaced != null) return;
            replaced = new HashSet<Vector3Int>();
            var asset = Resources.Load<TextAsset>("Remaster/Shell/pocket-faces"); if (asset == null) return;
            byte[] b = asset.bytes; zCut = BitConverter.ToSingle(b, 0); xMin = BitConverter.ToSingle(b, 4); xMax = BitConverter.ToSingle(b, 8);
            mossTint = new Vector4(BitConverter.ToSingle(b, 12), BitConverter.ToSingle(b, 16), BitConverter.ToSingle(b, 20), BitConverter.ToSingle(b, 24));
            TrailMeters = BitConverter.ToSingle(b, 28);
            for (int at = 32; at + 12 <= b.Length; at += 12) replaced.Add(new Vector3Int(BitConverter.ToInt32(b, at), BitConverter.ToInt32(b, at + 4), BitConverter.ToInt32(b, at + 8)));
        }
        // Face centroid in world metres; baked in centimetres, so allow one unit of rounding.
        public static bool Replaces(Vector3 centroid)
        {
            Load(); Vector3Int k = Vector3Int.RoundToInt(centroid * 100);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) if (replaced.Contains(k + new Vector3Int(x, y, z))) return true;
            return false;
        }
        public static bool Covers(Vector3 p) { Load(); return replaced.Count > 0 && p.z < zCut && p.x > xMin && p.x < xMax; }
        // Baked solid grid: the camera's collision only knows the original level, so in Remaster it also keeps out of
        // the rock the shell adds. Presentation only: gameplay colliders never see this.
        private static byte[] solid; private static Vector3 solidOrigin; private static float solidCell; private static int sx, sy, sz;
        public static bool LensBlocked(Vector3 p)
        {
            if (!ActiveShown) return false;
            if (solid == null)
            {
                var asset = Resources.Load<TextAsset>("Remaster/Shell/pocket-solid"); byte[] b = asset != null ? asset.bytes : new byte[0]; solid = b;
                if (b.Length < 28) return false;
                solidOrigin = new Vector3(BitConverter.ToSingle(b, 0), BitConverter.ToSingle(b, 4), BitConverter.ToSingle(b, 8)); solidCell = BitConverter.ToSingle(b, 12);
                sx = BitConverter.ToInt32(b, 16); sy = BitConverter.ToInt32(b, 20); sz = BitConverter.ToInt32(b, 24);
            }
            if (solid.Length < 28) return false;
            Vector3 g = (p - solidOrigin) / solidCell; int i = Mathf.RoundToInt(g.x), j = Mathf.RoundToInt(g.y), k = Mathf.RoundToInt(g.z);
            return i >= 0 && j >= 0 && k >= 0 && i < sx && j < sy && k < sz && solid[28 + i + sx * (j + sy * k)] != 0;
        }
        private static bool ActiveShown;
        private GameObject root;
        public int Triangles { get; private set; }
        public bool Shown { get; private set; }
        private void Awake()
        {
            if (!Available) return;
            var mesh = Resources.Load<Mesh>("Remaster/Shell/pocket"); var template = Resources.Load<Material>("Remaster/ShellMaterial");
            if (mesh == null || template == null) throw new InvalidOperationException("Remaster shell assets unavailable");
            var material = new Material(template); material.SetVector("_MossTint", mossTint);
            foreach (string layer in new[] { "Rock", "Moss", "Ground", "Path" })
            {
                material.SetTexture("_" + layer + "Color", Resources.Load<Texture2D>("Remaster/Shell/" + layer + "Color"));
                material.SetTexture("_" + layer + "Normal", Resources.Load<Texture2D>("Remaster/Shell/" + layer + "Normal"));
            }
            root = new GameObject("RemasterShell"); root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>().sharedMaterial = material;
            Triangles = (int)mesh.GetIndexCount(0) / 3; root.SetActive(false);
        }
        public void ApplyVisualMode(XenaVisualMode mode) { Shown = ActiveShown = mode == XenaVisualMode.Remaster && root != null; if (root != null) root.SetActive(Shown); if (dress != null) dress.SetActive(Shown); }

        // Real plants baked as kind, position, normal, size: 0 thicket crowns, 1 ferns on mossy rock, 2 hill trees.
        private GameObject dress;
        public int Plants { get; private set; }
        private IEnumerator Start()
        {
            if (root == null) yield break;
            yield return null; yield return null; // PrototypeRemasterTrees creates its materials in its Start
            var asset = Resources.Load<TextAsset>("Remaster/Shell/pocket-dress"); var trees = GetComponent<PrototypeRemasterTrees>();
            if (asset == null || trees == null || trees.Leaves == null) yield break;
            dress = new GameObject("RemasterShellPlants");
            var bark = new Material(trees.Bark) { name = "ShellBark" }; bark.SetColor("_Color", trees.Bark.GetColor("_Color") * 0.75f);
            var leaves = new Material(trees.Leaves) { name = "ShellLeaves" }; leaves.SetColor("_Color", new Color(0.46f, 0.54f, 0.4f, 1));
            var bushLeaves = new Material[4]; Color[] bushTints = { new Color(0.52f, 0.62f, 0.42f, 1), new Color(0.44f, 0.58f, 0.38f, 1), new Color(0.6f, 0.65f, 0.4f, 1), new Color(0.4f, 0.52f, 0.38f, 1) };
            for (int i = 0; i < bushLeaves.Length; i++) { bushLeaves[i] = new Material(trees.Leaves) { name = "ShellBushLeaves" }; bushLeaves[i].SetColor("_Color", bushTints[i]); }
            // Leaf clusters have a fixed real size, so the meshes are built at their real size and scaled only a little.
            var random = new System.Random(2026);
            var bushes = new Mesh[12]; var tall = new Mesh[9]; float[] bushSizes = { 1.8f, 3, 4.4f }, tallSizes = { 8, 10, 12.5f };
            // Thicket crowns: leaves only, no trunk stumps.
            for (int i = 0; i < bushes.Length; i++)
            {
                var full = PrototypeRemasterTrees.Build(bushSizes[i % 3], bushSizes[i % 3] * 1.15f, 900 + i, 0);
                bushes[i] = new Mesh { name = "ShellBush", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = full.vertices, normals = full.normals, uv = full.uv };
                bushes[i].SetTriangles(full.GetTriangles(1), 0); bushes[i].RecalculateBounds(); Destroy(full);
            }
            for (int i = 0; i < tall.Length; i++) tall[i] = PrototypeRemasterTrees.Build(tallSizes[i % 3], tallSizes[i % 3] * 0.55f, 700 + i, 0);
            Mesh Pick(Mesh[] set, float[] sizes, float size, out float scale)
            {
                int best = 0; for (int i = 1; i < sizes.Length; i++) if (Mathf.Abs(sizes[i] - size) < Mathf.Abs(sizes[best] - size)) best = i;
                scale = size / sizes[best]; return set[best + sizes.Length * random.Next(set.Length / sizes.Length)];
            }
            var ferns = new List<GameObject>(); foreach (string name in new[] { "fern_02" }) { var prefab = Resources.Load<GameObject>("Remaster/Vegetation/" + name); if (prefab != null) ferns.Add(prefab); }
            float[] data = new float[asset.bytes.Length / 4]; Buffer.BlockCopy(asset.bytes, 0, data, 0, asset.bytes.Length);
            for (int at = 0; at + 8 <= data.Length; at += 8)
            {
                int kind = (int)data[at]; var p = new Vector3(data[at + 1], data[at + 2], data[at + 3]); var n = new Vector3(data[at + 4], data[at + 5], data[at + 6]); float size = data[at + 7];
                var tilt = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, n, kind == 1 ? 0.75f : kind == 0 ? 0.35f : 0).normalized) * Quaternion.Euler(0, random.Next(360), 0);
                if (kind == 1 && ferns.Count > 0)
                {
                    var plant = Instantiate(ferns[random.Next(ferns.Count)], dress.transform); plant.transform.SetPositionAndRotation(Vector3.zero, tilt); plant.transform.localScale = Vector3.one;
                    var parts = plant.GetComponentsInChildren<Renderer>(true); int child = random.Next(parts.Length);
                    for (int i = 0; i < parts.Length; i++) if (i != child) Destroy(parts[i].gameObject);
                    Bounds own = parts[child].bounds; float scale = size / Mathf.Max(0.05f, Mathf.Max(own.size.x, Mathf.Max(own.size.y, own.size.z))); plant.transform.localScale = Vector3.one * scale;
                    own = parts[child].bounds; plant.transform.position = p - own.center + n * own.extents.y * 0.25f;
                    parts[child].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                else
                {
                    var go = new GameObject(kind == 2 ? "ShellTree" : "ShellBush"); go.transform.SetParent(dress.transform, false);
                    go.transform.SetPositionAndRotation(p + Vector3.down * (kind == 2 ? 0.2f : size * 0.42f), tilt); 
                    go.AddComponent<MeshFilter>().sharedMesh = kind == 2 ? Pick(tall, tallSizes, size, out float scale) : Pick(bushes, bushSizes, size, out scale); go.transform.localScale = Vector3.one * scale;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = kind == 2 ? new[] { bark, leaves } : new[] { bushLeaves[random.Next(bushLeaves.Length)] };
                    mr.shadowCastingMode = kind == 2 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                Plants++;
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }

        // Review stills: XenaMovementPrototype.exe --shell-review DIR (same views in both modes).
        private static readonly (string name, float x, float z, float yaw, float pitch, float eye)[] Views =
        {
            ("spawn-north", -20, -46, 0, 4, 1.7f), ("spawn-south", -18, -44, 185, 2, 1.7f), ("west-lobe", -27, -33, 275, 3, 1.7f), ("east-lobe", -1, -34, 90, 3, 1.7f),
            ("south-east", -6, -40, 145, 0, 1.7f), ("north-wall", -13, -29, 345, -4, 1.7f), ("neck", -8, -29, 0, 4, 1.7f), ("wall-close", -21, -47.5f, 200, -12, 1.5f),
            ("chase-south", -14, -41, 170, 14, 3.4f), ("chase-west", -22, -36, 250, 14, 3.4f), ("high-wide", -12, -36, 200, 22, 7f), ("back-of-rim", -10, -49, 330, 8, 8f), ("arch-sw", -20, -40, 40, -10, 1.7f), ("arch-w", -24, -30, 95, -14, 1.7f), ("arch-e", 0, -29, 265, -14, 1.7f), ("arch-s", -8, -44, 355, -14, 1.7f), ("south-rim-up", -14, -44, 200, -18, 1.6f), ("trail", -21.5f, -45, 170, 2, 1.7f), ("west-wall-near", -33.5f, -31, 280, -8, 1.6f),
            ("cards-west-a", -27, -15, 275, -10, 1.7f), ("cards-west-b", -26, 5, 275, -10, 1.7f), ("cards-north", 16, 29, 5, -10, 1.7f),
            ("top-1", -14, -44, 200, -24, 1.7f), ("top-2", -6, -42, 120, -24, 1.7f), ("top-3", -2, -36, 80, -24, 1.7f), ("top-4", -2, -30, 40, -24, 1.7f), ("top-5", -20, -30, 320, -24, 1.7f), ("top-6", -28, -33, 280, -24, 1.7f),
            ("top-7", -34, -31, 220, -24, 1.7f), ("top-8", -24, -42, 250, -24, 1.7f), ("top-9", -20, -48, 180, -24, 1.7f), ("top-10", -8, -42, 355, -22, 1.7f), ("top-11", -18, -26, 100, -30, 1.7f), ("top-12", -4, -24, 250, -28, 1.7f),
        };
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeReview()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--shell-review");
            if (at >= 0 && at + 1 < args.Length) new GameObject("ShellReview").AddComponent<ShellReview>().output = Path.GetFullPath(args[at + 1]);
        }
        private sealed class ShellReview : MonoBehaviour
        {
            public string output;
            private IEnumerator Start()
            {
                Application.runInBackground = true;
                for (int i = 0; i < 60; i++) yield return null;
                Directory.CreateDirectory(output);
                var motor = FindFirstObjectByType<PrototypeMotor>(); var cam = Camera.main; var orbit = cam.GetComponent<PrototypeCamera>();
                if (orbit != null) orbit.enabled = false; motor.enabled = false;
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); var read = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                foreach (var mode in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                {
                    PrototypeGraphics.Active.SetMode(mode); for (int i = 0; i < 5; i++) yield return null;
                    foreach (var v in Views)
                    {
                        float ground = motor.GroundHeight(new Vector3(v.x, 30, v.z)); if (float.IsNegativeInfinity(ground)) ground = 5;
                        cam.transform.SetPositionAndRotation(new Vector3(v.x, ground + v.eye, v.z), Quaternion.Euler(v.pitch, v.yaw, 0));
                        yield return null;
                        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); read.Apply(false);
                        File.WriteAllBytes(Path.Combine(output, v.name + "-" + mode.ToString().ToLowerInvariant() + ".jpg"), read.EncodeToJPG(90));
                        RenderTexture.active = null; cam.targetTexture = null;
                    }
                }
                // Real frame times at the first view's camera, uncapped, in the player's own window.
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; var timing = new List<string>();
                foreach (var mode in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                {
                    PrototypeGraphics.Active.SetMode(mode); for (int i = 0; i < 30; i++) yield return null;
                    foreach (var v in new[] { Views[1], Views[8] })
                    {
                        cam.transform.SetPositionAndRotation(new Vector3(v.x, motor.GroundHeight(new Vector3(v.x, 30, v.z)) + v.eye, v.z), Quaternion.Euler(v.pitch, v.yaw, 0));
                        for (int i = 0; i < 30; i++) yield return null;
                        float start = Time.realtimeSinceStartup; for (int i = 0; i < 200; i++) yield return null;
                        timing.Add(mode + " " + v.name + " " + Screen.width + "x" + Screen.height + " " + (200 / (Time.realtimeSinceStartup - start)).ToString("F0") + " fps");
                    }
                }
                File.WriteAllText(Path.Combine(output, "timing.txt"), string.Join(Environment.NewLine, timing));
                File.WriteAllText(Path.Combine(output, "done.txt"), "PASS");
                Application.Quit(0);
            }
        }
    }
}
