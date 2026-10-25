using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster ground cover (presentation only, no colliders): grass tufts (Codex-painted 2x2
    // atlas, tools/codex-remaster/prompt-grass.txt) scattered on the walkable, green-textured
    // faces of the ORIGINAL terrain mesh (the collider mesh, so tufts stand on the exact ground),
    // TuftsPerSquareMetre, baked into 12 m chunk meshes (Xena/RemasterGrass: wind, distance
    // dissolve, ground light from the face's baked vertex colours), plus extra CC0 ferns
    // (fern_02 variants) at FernsPerSquareMetre. Paths (brown faces) stay clear. Deterministic.
    public sealed class PrototypeRemasterGrass : MonoBehaviour, IXenaVisualModeTarget
    {
        public const float TuftsPerSquareMetre = 3.2f, FernsPerSquareMetre = 0.012f, Chunk = 12;
        private GameObject root;
        public int Tufts { get; private set; }
        public int Ferns { get; private set; }
        public int Chunks { get; private set; }
        public float GrassArea { get; private set; }
        public bool RemasterShown { get; private set; }

        private void Start()
        {
            var material = Resources.Load<Material>("Remaster/Grass"); var atlas = Resources.Load<Texture2D>("Remaster/Vegetation/Grass");
            MeshCollider ground = null; foreach (var c in FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)) if (c.sharedMesh != null && c.sharedMesh.name == "level-01-object-1") ground = c;
            if (material == null || atlas == null || ground == null) return;
            material = new Material(material); material.SetTexture("_MainTex", atlas);
            // The Codex atlas is brighter and more yellow than the ground pages: tint to the forest greens.
            material.SetColor("_Color", new Color(0.52f, 0.6f, 0.46f, 1));
            var mesh = ground.sharedMesh; var renderer = ground.GetComponent<MeshRenderer>(); var t = ground.transform;
            Vector3[] vertices = mesh.vertices; Vector2[] uvs = mesh.uv; Color[] colors = mesh.colors; bool lit = colors.Length == vertices.Length;
            var pages = renderer != null ? renderer.sharedMaterials : new Material[0];
            var random = new System.Random(1999);
            var chunks = new Dictionary<Vector2Int, (List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> tri)>();
            var fernSpots = new List<Vector3>();
            root = new GameObject("RemasterGrass");
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var page = s < pages.Length && pages[s] != null ? pages[s].mainTexture as Texture2D : null;
                if (page == null || !page.isReadable) continue;
                int[] tris = mesh.GetTriangles(s);
                for (int f = 0; f < tris.Length; f += 3)
                {
                    int i0 = tris[f], i1 = tris[f + 1], i2 = tris[f + 2];
                    Vector3 a = t.TransformPoint(vertices[i0]), b = t.TransformPoint(vertices[i1]), c = t.TransformPoint(vertices[i2]);
                    Vector3 normal = Vector3.Cross(b - a, c - a); float area = normal.magnitude * 0.5f;
                    if (area < 1e-4f || Vector3.Dot(normal.normalized, Vector3.up) < 0.7f) continue;
                    // Green faces only: grass texture under the centroid and two inner points.
                    Color tone = Color.clear;
                    foreach (var w in new[] { new Vector3(1f / 3, 1f / 3, 1f / 3), new Vector3(0.6f, 0.2f, 0.2f), new Vector3(0.2f, 0.2f, 0.6f) })
                    { Vector2 uv = uvs[i0] * w.x + uvs[i1] * w.y + uvs[i2] * w.z; tone += page.GetPixelBilinear(uv.x, uv.y); }
                    tone /= 3;
                    if (tone.a < 0.5f || tone.g < tone.r * 1.08f || tone.g < tone.b * 1.2f || tone.g < 0.06f) continue;
                    GrassArea += area;
                    int count = (int)(area * TuftsPerSquareMetre + random.NextDouble());
                    for (int k = 0; k < count; k++)
                    {
                        float u = (float)random.NextDouble(), v = (float)random.NextDouble(); if (u + v > 1) { u = 1 - u; v = 1 - v; }
                        Vector3 p = a + (b - a) * u + (c - a) * v;
                        Color light = lit ? colors[i0] * (1 - u - v) + colors[i1] * u + colors[i2] * v : new Color(0.5f, 0.5f, 0.5f);
                        var key = new Vector2Int(Mathf.FloorToInt(p.x / Chunk), Mathf.FloorToInt(p.z / Chunk));
                        if (!chunks.TryGetValue(key, out var chunk)) chunks[key] = chunk = (new List<Vector3>(), new List<Vector2>(), new List<Color>(), new List<int>());
                        int cell = random.Next(10) < 9 ? new[] { 0, 2, 3 }[random.Next(3)] : 1; // atlas: v=0 row = clover (0), fern sprout (1, rarer); top row = meadow (2), seed heads (3)
                        float height = 0.3f + 0.45f * (float)random.NextDouble(), width = height * (0.9f + 0.4f * (float)random.NextDouble());
                        float turn = (float)random.NextDouble() * 180;
                        Tuft(chunk, p - new Vector3(key.x * Chunk, 0, key.y * Chunk), height, width, turn, cell, light);
                        Tufts++;
                    }
                    int ferns = (int)(area * FernsPerSquareMetre + random.NextDouble());
                    for (int k = 0; k < ferns; k++)
                    {
                        float u = (float)random.NextDouble(), v = (float)random.NextDouble(); if (u + v > 1) { u = 1 - u; v = 1 - v; }
                        fernSpots.Add(a + (b - a) * u + (c - a) * v);
                    }
                }
            }
            foreach (var pair in chunks)
            {
                var go = new GameObject("GrassChunk_" + pair.Key.x + "_" + pair.Key.y); go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(pair.Key.x * Chunk, 0, pair.Key.y * Chunk);
                var m = new Mesh { name = go.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(pair.Value.v); m.SetUVs(0, pair.Value.uv); m.SetColors(pair.Value.c); m.SetTriangles(pair.Value.tri, 0); m.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = material; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Chunks++;
            }
            var fern = Resources.Load<GameObject>("Remaster/Vegetation/fern_02");
            if (fern != null)
                foreach (var spot in fernSpots)
                {
                    var plant = Instantiate(fern, root.transform); var parts = plant.GetComponentsInChildren<Renderer>(true); int child = random.Next(parts.Length);
                    for (int i = 0; i < parts.Length; i++) parts[i].enabled = i == child;
                    plant.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, random.Next(360), 0)); plant.transform.localScale = Vector3.one * (1.1f + 0.8f * (float)random.NextDouble());
                    Bounds own = parts[child].bounds;
                    plant.transform.position = new Vector3(spot.x - own.center.x, spot.y - own.min.y - 0.03f, spot.z - own.center.z);
                    Ferns++;
                }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
        // Two crossed quads; base alpha 0, tip alpha 1 (wind weight).
        private static void Tuft((List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> tri) chunk, Vector3 at, float height, float width, float turn, int cell, Color light)
        {
            Vector2 uv0 = new Vector2((cell % 2) * 0.5f, (cell / 2) * 0.5f);
            for (int q = 0; q < 2; q++)
            {
                Vector3 right = Quaternion.Euler(0, turn + q * 90, 0) * Vector3.right * width * 0.5f; int i = chunk.v.Count;
                chunk.v.Add(at - right); chunk.v.Add(at + right); chunk.v.Add(at + right + Vector3.up * height); chunk.v.Add(at - right + Vector3.up * height);
                chunk.uv.Add(uv0); chunk.uv.Add(uv0 + new Vector2(0.5f, 0)); chunk.uv.Add(uv0 + new Vector2(0.5f, 0.5f)); chunk.uv.Add(uv0 + new Vector2(0, 0.5f));
                Color b = new Color(light.r, light.g, light.b, 0), top = new Color(light.r, light.g, light.b, 1);
                chunk.c.Add(b); chunk.c.Add(b); chunk.c.Add(top); chunk.c.Add(top);
                chunk.tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
        }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            if (root != null) root.SetActive(RemasterShown);
        }
    }
}
