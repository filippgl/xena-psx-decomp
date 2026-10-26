using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xena.Assets
{
    // Remaster gate (@2doors / @2doorsr, level meshes 6 and 11): the original leaves are flat textured cards
    // whose planks, gaps and rings come from the alpha-cut texture. Remaster keeps their exact outline, UVs
    // and texture (Codex HD page) and gives the wood real volume: every triangle is subdivided, and a front
    // and a back layer are pushed apart along the leaf normal by a rounded profile of the distance to the
    // nearest transparent texel (0 at a gap edge, full thickness inside a plank: planks read as rounded
    // beams, rings as tori), plus a small wood-grain relief from the texel brightness. Both layers meet at the
    // cut-out edge, so the silhouette from the front and the ground footprint are the original ones (the
    // depth grows by at most 2 x Thickness); collision keeps the original meshes. Original mode restores them.
    public sealed class PrototypeRemasterGate : MonoBehaviour, IXenaVisualModeTarget
    {
        private static readonly string[] Leaves = { "level-01-object-6", "level-01-object-11" };
        public const float ThicknessMeters = 0.035f, GrainMeters = 0.006f;
        // EdgeTexels: profile ramp in original-page texels (256 wide; scaled for the 4x HD page).
        private const int Subdivisions = 24; private const float EdgeTexels = 4f;
        private sealed class Pair { public MeshFilter filter; public MeshRenderer renderer; public Mesh original, remaster; public Material shared, smooth; }
        // Smoothed cut-out: the HD page's alpha is stair-stepped at the original 256-texel grid; the leaves use a
        // copy whose alpha is blurred (two box passes, radius 1 HD texel) and re-thresholded with a soft ramp,
        // so the gap edges become curves. The copy is named "<page>-gate-smooth" (not a level page).
        public const string SmoothSuffix = "-gate-smooth";
        private readonly Dictionary<Texture, Texture2D> smoothed = new Dictionary<Texture, Texture2D>();
        public int SmoothedPages => smoothed.Count;
        private readonly List<Pair> pairs = new List<Pair>();
        public int Leaf => pairs.Count;
        public int RemasterTriangles { get; private set; }
        public float MaxDepthMeters { get; private set; }
        public bool RemasterShown { get; private set; }

        private void Start()
        {
            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null || System.Array.IndexOf(Leaves, filter.sharedMesh.name) < 0) continue;
                var renderer = filter.GetComponent<MeshRenderer>(); var page = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                if (page == null) continue;
                var remaster = Build(filter.sharedMesh, Read(page), page.width, page.height, filter.transform);
                if (remaster != null) pairs.Add(new Pair { filter = filter, renderer = renderer, original = filter.sharedMesh, remaster = remaster });
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
        // GPU readback works for pages that are not CPU readable.
        private static Color32[] Read(Texture page)
        {
            // sRGB target: the sampled colour is re-encoded, so the copy keeps the page's sRGB values (a linear
            // target stored linear values in an sRGB texture and darkened the copy).
            var rt = RenderTexture.GetTemporary(page.width, page.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(page, rt); var keep = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(page.width, page.height, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0, 0, page.width, page.height), 0, 0); t.Apply(false);
            RenderTexture.active = keep; RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32(); Destroy(t); return px;
        }
        // Chamfer distance (texels) from every opaque texel to the nearest transparent one.
        private static float[] Distance(Color32[] px, int w, int h)
        {
            var d = new float[w * h]; const float Far = 1e4f, Diag = 1.4142f;
            for (int i = 0; i < d.Length; i++) d[i] = px[i].a < 128 ? 0 : Far;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = y * w + x; if (d[i] == 0) continue; float v = d[i];
                if (x > 0) v = Mathf.Min(v, d[i - 1] + 1); if (y > 0) v = Mathf.Min(v, d[i - w] + 1);
                if (x > 0 && y > 0) v = Mathf.Min(v, d[i - w - 1] + Diag); if (x < w - 1 && y > 0) v = Mathf.Min(v, d[i - w + 1] + Diag);
                d[i] = v;
            }
            for (int y = h - 1; y >= 0; y--) for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x; if (d[i] == 0) continue; float v = d[i];
                if (x < w - 1) v = Mathf.Min(v, d[i + 1] + 1); if (y < h - 1) v = Mathf.Min(v, d[i + w] + 1);
                if (x < w - 1 && y < h - 1) v = Mathf.Min(v, d[i + w + 1] + Diag); if (x > 0 && y < h - 1) v = Mathf.Min(v, d[i + w - 1] + Diag);
                d[i] = v;
            }
            return d;
        }
        private static float Sample(float[] field, int w, int h, Vector2 uv)
        {
            float fx = Mathf.Clamp(uv.x * w - 0.5f, 0, w - 1.001f), fy = Mathf.Clamp(uv.y * h - 0.5f, 0, h - 1.001f);
            int x = (int)fx, y = (int)fy; float ax = fx - x, ay = fy - y;
            float a = Mathf.Lerp(field[y * w + x], field[y * w + x + 1], ax), b = Mathf.Lerp(field[(y + 1) * w + x], field[(y + 1) * w + x + 1], ax);
            return Mathf.Lerp(a, b, ay);
        }

        private Mesh Build(Mesh source, Color32[] px, int w, int h, Transform frame)
        {
            var dist = Distance(px, w, h); var lum = new float[px.Length];
            for (int i = 0; i < px.Length; i++) lum[i] = (px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f) / 255f;
            Vector3[] pos = source.vertices; Vector2[] uv = source.uv; Color32[] col = source.colors32; int[] tri = source.triangles;
            bool hasColor = col != null && col.Length == pos.Length;
            // Smooth leaf normal per welded position (the leaves bend along their arched top).
            var welded = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 1000);
            for (int t = 0; t < tri.Length; t += 3)
            {
                Vector3 n = Vector3.Cross(pos[tri[t + 1]] - pos[tri[t]], pos[tri[t + 2]] - pos[tri[t]]);
                for (int k = 0; k < 3; k++) { var key = Key(pos[tri[t + k]]); welded[key] = (welded.TryGetValue(key, out var s) ? s : Vector3.zero) + n; }
            }
            // Local scale: thickness in metres in the leaf's local space.
            float toLocal = 1f / Mathf.Max(1e-4f, frame.lossyScale.magnitude / Mathf.Sqrt(3));
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var colors = new List<Color32>(); var front = new List<int>(); var back = new List<int>();
            var index = new Dictionary<(Vector3Int, Vector2Int, int), int>();
            int Vertex(Vector3 p, Vector2 t, Color32 c, Vector3 n, int side)
            {
                var key = (Key(p), Vector2Int.RoundToInt(t * 65536), side);
                if (index.TryGetValue(key, out int found)) return found;
                float s = Mathf.Clamp01(Sample(dist, w, h, t) / (EdgeTexels * w / 256f)), profile = Mathf.Sqrt(1 - (1 - s) * (1 - s));
                float depth = (ThicknessMeters * profile + GrainMeters * (Sample(lum, w, h, t) - 0.5f) * s) * toLocal;
                MaxDepthMeters = Mathf.Max(MaxDepthMeters, depth / toLocal);
                verts.Add(p + n * depth * side); uvs.Add(t); colors.Add(c); index[key] = verts.Count - 1; return verts.Count - 1;
            }
            for (int t = 0; t < tri.Length; t += 3)
            {
                int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                for (int side = 1; side >= -1; side -= 2)
                {
                    var rows = new int[Subdivisions + 1][];
                    for (int a = 0; a <= Subdivisions; a++)
                    {
                        rows[a] = new int[Subdivisions + 1 - a];
                        for (int b = 0; b <= Subdivisions - a; b++)
                        {
                            float u = (float)a / Subdivisions, v = (float)b / Subdivisions, wgt = 1 - u - v;
                            Vector3 p = pos[i0] * wgt + pos[i1] * u + pos[i2] * v; Vector2 q = uv[i0] * wgt + uv[i1] * u + uv[i2] * v;
                            Vector3 n = (welded[Key(pos[i0])] * wgt + welded[Key(pos[i1])] * u + welded[Key(pos[i2])] * v).normalized;
                            Color32 c = hasColor ? (Color32)((Color)col[i0] * wgt + (Color)col[i1] * u + (Color)col[i2] * v) : new Color32(128, 128, 128, 255);
                            rows[a][b] = Vertex(p, q, c, n, side);
                        }
                    }
                    var list = side > 0 ? front : back;
                    for (int a = 0; a < Subdivisions; a++)
                        for (int b = 0; b < Subdivisions - a; b++)
                        {
                            int p0 = rows[a][b], p1 = rows[a + 1][b], p2 = rows[a][b + 1];
                            if (side > 0) list.AddRange(new[] { p0, p1, p2 }); else list.AddRange(new[] { p0, p2, p1 });
                            if (b < Subdivisions - a - 1) { int p3 = rows[a + 1][b + 1]; if (side > 0) list.AddRange(new[] { p1, p3, p2 }); else list.AddRange(new[] { p1, p2, p3 }); }
                        }
                }
            }
            var mesh = new Mesh { name = source.name + "-remaster-gate", indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetColors(colors);
            var all = new List<int>(front); all.AddRange(back); mesh.SetTriangles(all, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            RemasterTriangles += all.Count / 3;
            return mesh;
        }

        // Test only: show the flat original leaves under the Remaster material (lighting comparison).
        public void ShowFlatForTest(bool flat) { foreach (var p in pairs) if (p.filter != null) p.filter.sharedMesh = flat ? p.original : p.remaster; }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            foreach (var p in pairs)
            {
                if (p.filter != null) p.filter.sharedMesh = RemasterShown ? p.remaster : p.original;
                // Only undo our own binding: the level preview may already have bound its Original material.
                if (!RemasterShown && p.shared != null && p.renderer != null && p.renderer.sharedMaterial == p.smooth) p.renderer.sharedMaterial = p.shared;
            }
        }
        // The HD page is bound by the level preview after the mode switch: bind the smoothed copy once it is there.
        private void LateUpdate()
        {
            if (!RemasterShown) return;
            foreach (var p in pairs)
            {
                if (p.renderer == null) continue;
                var current = p.renderer.sharedMaterial;
                if (current == null || current == p.smooth) continue;
                var page = current.mainTexture;
                if (page == null || page.width < 1024) continue; // HD page not bound yet
                if (!smoothed.TryGetValue(page, out var copy)) smoothed[page] = copy = Smooth(page);
                p.shared = current;
                if (p.smooth == null || p.smooth.mainTexture != copy) { p.smooth = new Material(current) { name = current.name + SmoothSuffix, mainTexture = copy }; }
                p.renderer.sharedMaterial = p.smooth;
            }
        }
        private static Texture2D Smooth(Texture page)
        {
            int w = page.width, h = page.height; var px = Read(page); var a = new float[w * h]; var b = new float[w * h];
            for (int i = 0; i < a.Length; i++) a[i] = px[i].a / 255f;
            const int R = 1; // keeps 4-texel (one original texel) gaps fully open, rounds the stair corners
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < h; y++) { float sum = 0; int row = y * w; for (int x = -R; x <= R; x++) sum += a[row + Mathf.Clamp(x, 0, w - 1)];
                    for (int x = 0; x < w; x++) { b[row + x] = sum / (2 * R + 1); sum += a[row + Mathf.Min(w - 1, x + R + 1)] - a[row + Mathf.Max(0, x - R)]; } }
                for (int x = 0; x < w; x++) { float sum = 0; for (int y = -R; y <= R; y++) sum += b[Mathf.Clamp(y, 0, h - 1) * w + x];
                    for (int y = 0; y < h; y++) { a[y * w + x] = sum / (2 * R + 1); sum += b[Mathf.Min(h - 1, y + R + 1) * w + x] - b[Mathf.Max(0, y - R) * w + x]; } }
            }
            for (int i = 0; i < px.Length; i++) px[i].a = (byte)Mathf.RoundToInt(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.35f, 0.65f, a[i])) * 255);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = page.name + SmoothSuffix, filterMode = FilterMode.Trilinear, anisoLevel = 4, wrapMode = page.wrapMode };
            t.SetPixels32(px); t.Apply(true, false); return t; // kept readable (4 MB) for the colour check
        }
    }
}
