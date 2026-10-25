using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster trees (SpeedTree-style, generated): every original tree (@tree card trees and the
    // @3dtree* trees) gets a procedural deciduous tree with the same trunk axis (centroid of the
    // original mesh's lowest quarter, i.e. where the trunk and its original CLS collider are),
    // the original height and crown width. Bark: CC0 Poly Haven bark_brown_02; leaves: Codex-painted
    // 2x2 leaf-cluster atlas (tools/codex-remaster/prompt-leaves.txt). Trunk + 5..7 branches with
    // twigs; ~70 leaf clusters on a crown ellipsoid with spherical normals; foliage sways
    // (Xena/RemasterLit _Wind). Deterministic per tree. Gameplay keeps the original colliders.
    // Original mode shows the original trees.
    public sealed class PrototypeRemasterTrees : MonoBehaviour, IXenaVisualModeTarget
    {
        private static readonly string[] Kinds = { "@tree", "@3dtree" };
        private readonly List<(Renderer original, GameObject tree)> trees = new List<(Renderer, GameObject)>();
        public int Replaced => trees.Count;
        public int Triangles { get; private set; }
        public bool RemasterShown { get; private set; }
        public GameObject FirstTree => trees.Count > 0 ? trees[0].tree : null;

        private void Start()
        {
            var template = Resources.Load<Material>("Remaster/LevelLit");
            var barkTexture = Resources.Load<Texture2D>("Remaster/Vegetation/TreeBark");
            var leafTexture = Resources.Load<Texture2D>("Remaster/Vegetation/TreeLeaves");
            if (template == null || barkTexture == null || leafTexture == null) return;
            var bark = new Material(template) { name = "RemasterBark" };
            bark.SetTexture("_MainTex", barkTexture); bark.SetFloat("_Cutoff", 0); bark.SetFloat("_UseVertexLighting", 0); bark.SetFloat("_RockDetail", 0);
            bark.SetFloat("_Cull", 2); bark.SetFloat("_Wind", 0.0015f); bark.SetColor("_Color", new Color(0.62f, 0.58f, 0.52f, 1));
            var leaves = new Material(template) { name = "RemasterLeaves" };
            leaves.SetTexture("_MainTex", leafTexture); leaves.SetFloat("_Cutoff", 0.45f); leaves.SetFloat("_UseVertexLighting", 0); leaves.SetFloat("_RockDetail", 0);
            leaves.SetFloat("_Cull", 0); leaves.SetFloat("_Wind", 0.004f); leaves.SetColor("_Color", new Color(0.62f, 0.68f, 0.56f, 1));
            var motor = GetComponent<PrototypeMotor>();
            var root = new GameObject("RemasterTrees");
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                bool match = false; foreach (string kind in Kinds) if (renderer.name.Contains(kind)) match = true;
                if (!match) continue;
                var filter = renderer.GetComponent<MeshFilter>(); if (filter == null || filter.sharedMesh == null) continue;
                Bounds b = renderer.bounds; float height = b.size.y, width = Mathf.Max(b.size.x, b.size.z);
                // Trunk axis: centroid of the lowest quarter of the original vertices.
                Vector3 trunk = Vector3.zero; int n = 0;
                foreach (var v in filter.sharedMesh.vertices) { Vector3 w = filter.transform.TransformPoint(v); if (w.y < b.min.y + height * 0.25f) { trunk += w; n++; } }
                trunk = n > 0 ? trunk / n : b.center;
                float ground = motor != null ? motor.GroundHeight(new Vector3(trunk.x, b.min.y + 1.5f, trunk.z)) : b.min.y;
                if (float.IsNegativeInfinity(ground) || Mathf.Abs(ground - b.min.y) > 1.5f) ground = b.min.y;
                int seed = Mathf.Abs(Mathf.RoundToInt(trunk.x * 131 + trunk.z * 197));
                var go = new GameObject("Tree_" + renderer.name); go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(trunk.x, ground - 0.05f, trunk.z);
                var mesh = Build(height + (b.min.y - ground), width, seed);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { bark, leaves };
                Triangles += mesh.triangles.Length / 3;
                trees.Add((renderer, go));
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }

        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            foreach (var (original, tree) in trees) { if (original != null) original.enabled = !RemasterShown; if (tree != null) tree.SetActive(RemasterShown); }
        }

        private sealed class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> bark = new List<int>(), leaves = new List<int>();
            // Tapered tube along a polyline.
            public void Tube(Vector3[] points, float r0, float r1, int sides)
            {
                int start = v.Count; float length = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    if (i > 0) length += Vector3.Distance(points[i], points[i - 1]);
                    Vector3 dir = (i < points.Length - 1 ? points[i + 1] - points[i] : points[i] - points[i - 1]).normalized;
                    Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized, other = Vector3.Cross(dir, side);
                    float r = Mathf.Lerp(r0, r1, (float)i / (points.Length - 1));
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s * 2 * Mathf.PI / sides; Vector3 normal = side * Mathf.Cos(a) + other * Mathf.Sin(a);
                        v.Add(points[i] + normal * r); n.Add(normal); uv.Add(new Vector2((float)s / sides * Mathf.Max(1, r0 * 6), length * 0.8f));
                    }
                }
                for (int i = 0; i < points.Length - 1; i++)
                    for (int s = 0; s < sides; s++)
                    {
                        int a = start + i * (sides + 1) + s, c = a + sides + 1;
                        bark.AddRange(new[] { a, c, a + 1, a + 1, c, c + 1 });
                    }
            }
            // Two crossed leaf quads; normals point away from the crown centre (soft volume light).
            public void Cluster(Vector3 at, float size, float turn, int cell, Vector3 crown)
            {
                Vector2 uv0 = new Vector2((cell % 2) * 0.5f, (cell / 2) * 0.5f);
                Vector3 normal = (at - crown).normalized;
                for (int q = 0; q < 2; q++)
                {
                    Quaternion r = Quaternion.Euler(Random.Range(-25f, 25f), turn + q * 90, Random.Range(-20f, 20f));
                    Vector3 right = r * Vector3.right * size * 0.5f, up = r * Vector3.up * size * 0.5f; int i = v.Count;
                    v.Add(at - right - up); v.Add(at + right - up); v.Add(at + right + up); v.Add(at - right + up);
                    for (int k = 0; k < 4; k++) n.Add(normal);
                    uv.Add(uv0); uv.Add(uv0 + new Vector2(0.5f, 0)); uv.Add(uv0 + new Vector2(0.5f, 0.5f)); uv.Add(uv0 + new Vector2(0, 0.5f));
                    leaves.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
            }
        }

        private static Mesh Build(float height, float width, int seed)
        {
            var state = Random.state; Random.InitState(seed);
            var b = new Builder();
            float trunkTop = height * Random.Range(0.42f, 0.52f), radius = 0.1f + 0.022f * height;
            Vector3 lean = new Vector3(Random.Range(-0.25f, 0.25f), 0, Random.Range(-0.25f, 0.25f));
            var trunk = new Vector3[7];
            for (int i = 0; i < trunk.Length; i++) { float t = (float)i / (trunk.Length - 1); trunk[i] = new Vector3(0, t * trunkTop, 0) + lean * t * t + new Vector3(Mathf.Sin(t * 5 + seed) * 0.05f, 0, Mathf.Cos(t * 4 + seed) * 0.05f); }
            // Root flare: a short wider ring at the base.
            b.Tube(new[] { new Vector3(0, -0.1f, 0), new Vector3(0, 0.35f, 0) }, radius * 1.7f, radius * 1.05f, 10);
            b.Tube(trunk, radius, radius * 0.55f, 10);
            Vector3 crown = new Vector3(lean.x, height * 0.68f, lean.z); float crownR = width * 0.5f, crownH = height * 0.34f;
            int branches = Random.Range(5, 8); var tips = new List<Vector3>();
            for (int k = 0; k < branches; k++)
            {
                float a = (k + Random.Range(-0.25f, 0.25f)) / branches * 2 * Mathf.PI, startT = Random.Range(0.55f, 1f);
                Vector3 from = Vector3.Lerp(trunk[3], trunk[trunk.Length - 1], startT);
                Vector3 dir = new Vector3(Mathf.Cos(a), Random.Range(0.55f, 1.1f), Mathf.Sin(a)).normalized;
                float len = Mathf.Min(crownR, height * 0.4f) * Random.Range(0.75f, 1.0f);
                var pts = new Vector3[5];
                for (int i = 0; i < pts.Length; i++) { float t = (float)i / (pts.Length - 1); pts[i] = from + dir * len * t + Vector3.up * (len * 0.25f * t * t); }
                b.Tube(pts, radius * 0.42f, radius * 0.08f, 6); tips.Add(pts[pts.Length - 1]);
                for (int s = 0; s < 2; s++)
                {
                    Vector3 at = Vector3.Lerp(pts[1], pts[3], Random.value), twig = (dir + Random.insideUnitSphere * 0.8f + Vector3.up * 0.4f).normalized * len * 0.45f;
                    b.Tube(new[] { at, at + twig * 0.5f, at + twig }, radius * 0.14f, radius * 0.04f, 5); tips.Add(at + twig);
                }
            }
            // Leaf clusters: at every tip plus a filled crown ellipsoid.
            foreach (var tip in tips) b.Cluster(tip, Random.Range(1.1f, 1.6f) * Mathf.Clamp(width / 5.5f, 0.6f, 1.4f), Random.Range(0f, 360f), Random.Range(0, 4), crown);
            int clusters = Mathf.RoundToInt(55 * Mathf.Clamp(width * height / 34f, 0.5f, 1.6f));
            for (int c = 0; c < clusters; c++)
            {
                Vector3 p = Random.insideUnitSphere; p = new Vector3(p.x * crownR * 0.9f, p.y * crownH, p.z * crownR * 0.9f);
                // Favour the outer shell so the crown reads full but not solid.
                p = crown + p.normalized * Mathf.Lerp(0.55f, 1f, Mathf.Sqrt(Random.value)) * p.magnitude;
                b.Cluster(p, Random.Range(1.0f, 1.5f) * Mathf.Clamp(width / 5.5f, 0.6f, 1.4f), Random.Range(0f, 360f), Random.Range(0, 4), crown);
            }
            Random.state = state;
            var mesh = new Mesh { name = "RemasterTree", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetUVs(0, b.uv);
            mesh.subMeshCount = 2; mesh.SetTriangles(b.bark, 0); mesh.SetTriangles(b.leaves, 1);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
