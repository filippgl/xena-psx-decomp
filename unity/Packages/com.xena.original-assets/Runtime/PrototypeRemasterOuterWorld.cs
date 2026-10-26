using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster "outer world" (presentation only; user request 2026-10-26): the level is a corridor closed by
    // vertical boundary walls painted as forest: a stack of painted bands (page 3 forest, page 5 dark top band,
    // page 6 grass strip, page 16 the path into the forest). Remaster stops drawing those
    // boundary faces (their colliders, the camera collision and gameplay stay: the walls become invisible) and
    // builds the world they depicted behind them:
    // - a terrain band up to BandMeters beyond every painted wall, outside the corridor's hull (no non-steep
    //   terrain face above or below), starting at the walkable ground at the wall's foot and rising gently into
    //   hills; a 1.5 m skirt under the corridor's edge closes the seam;
    // - its ground is the corridor's own ground: each band triangle tiles the texture of the walkable face at the
    //   nearest wall's foot (same page region, same world scale, same baked vertex light), so the ground runs on
    //   without a colour step; the painted path continues with the corridor path's own texture;
    // - Remaster trees (darker, like the painted forest), CC0 ferns/shrubs and grass tufts; nothing is planted
    //   where a level collider (rocks, boulders, props) stands above the band.
    // Painted walls = steep faces of the terrain mesh on those pages whose back has no walkable ground within
    // 12 m (the corridor's boundary) and open space behind them; page 12 (red backdrop), page 10 (fern-covered
    // rock) and the rock/fence pages stay as they are.
    public sealed class PrototypeRemasterOuterWorld : MonoBehaviour, IXenaVisualModeTarget
    {
        // Page 10 (fern-covered rock) is a cliff, not a painted backdrop: kept (it bounds raised plateaus).
        // Universal (no per-level page lists, user 2026-10-26): a boundary face is a painted backdrop when its page's
        // boundary faces are, by median texture colour, painted forest (G >= 1.5 R and G >= 1.5 B; level 01:
        // painted forest 1.7..3+, fern-covered rock 1.27..1.3, rock ~1.0, cliff 0.7, red sky 0.45) and its own region is opaque (atlas
        // pages mix panels with cut-out fences/cards). A painted path continues where the ground at a wall's foot
        // is brown path earth (R >= 0.85 G, R > 1.6 B, saturated; level 01 path HD R/B 2.9, grey rock 1.26).
        public const float ForestGreenOverRed = 1.5f, ForestGreenOverBlue = 1.5f;
        // Path ground = brown earth (red over blue with some saturation); grey rock and green grass are not.
        private static bool PathColour(Color c) => c.r >= 0.85f * c.g && c.r > 1.6f * c.b && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > 0.05f;
        public const float BandMeters = 30, Cell = 0.5f;
        public struct Wall
        {
            public int submesh, triangle; public Vector3 a, b, c, outward; public bool path; public float baseY, topY;
            // Walkable face at the foot: its submesh, UV rectangle, mean vertex light and metres per UV unit.
            public int footSub; public Rect footUv; public Color footLight; public float metresPerUv;
        }
        private static HashSet<Vector2Int> covered;
        public static List<Wall> Walls { get; private set; }
        public static List<string> ForestPages { get; private set; }
        public static int PartialPanels { get; private set; }
        public static int WaterCells { get; private set; }
        public int BandTriangles { get; private set; }
        public int Trees { get; private set; }
        public int Plants { get; private set; }
        public int Tufts { get; private set; }
        public int PathSegments { get; private set; }
        public int GroundCrops => crops.Count;
        public int Blocked { get; private set; }
        public int Resettled { get; private set; }
        public readonly List<Vector3> PathStarts = new List<Vector3>();
        public bool RemasterShown { get; private set; }
        private GameObject root;
        private MeshRenderer terrain;
        private sealed class Crop { public int sub; public Rect uv; public Material material; public Texture source; public float tileMetres; public Color light; }
        private readonly List<Crop> crops = new List<Crop>();

        public static List<Wall> Detect(Mesh mesh, Material[] materials, Transform t)
        {
            if (Walls != null) return Walls;
            var walls = new List<Wall>(); var v = mesh.vertices; var uv = mesh.uv; var colors = mesh.colors; bool lit = colors.Length == v.Length;
            var walk = new Dictionary<Vector2Int, (float y, int sub, int tri)>(); covered = new HashSet<Vector2Int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tri = mesh.GetTriangles(s);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
                    // Steep (wall) faces never mark the hull: the band may pass under slanted painted walls.
                    Vector3 n = Vector3.Cross(b - a, c - a).normalized; if (Mathf.Abs(n.y) < 0.5f) continue;
                    bool walkable = Mathf.Abs(n.y) >= 0.6f;
                    float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    for (float x = Mathf.Floor(x0 * 2) / 2; x <= x1; x += 0.5f)
                        for (float z = Mathf.Floor(z0 * 2) / 2; z <= z1; z += 0.5f)
                        {
                            if (!InTriangle(new Vector2(x + 0.25f, z + 0.25f), a, b, c)) continue;
                            var key = new Vector2Int(Mathf.FloorToInt(x * 2), Mathf.FloorToInt(z * 2)); covered.Add(key);
                            if (!walkable) continue;
                            float y = a.y - (n.x * (x + 0.25f - a.x) + n.z * (z + 0.25f - a.z)) / n.y;
                            if (!walk.TryGetValue(key, out var old) || y > old.y) walk[key] = (y, s, i);
                        }
                }
            }
            // Water surfaces (PrototypeRemasterEnvironment) belong to the level like its ground: no band on a lake and
            // no wall dropped in front of one.
            foreach (var f in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!PrototypeRemasterEnvironment.IsWaterSurface(f.sharedMesh)) continue;
                var wv = f.sharedMesh.vertices; var wt = f.sharedMesh.triangles;
                for (int i = 0; i < wt.Length; i += 3)
                {
                    Vector3 a = f.transform.TransformPoint(wv[wt[i]]), b = f.transform.TransformPoint(wv[wt[i + 1]]), c = f.transform.TransformPoint(wv[wt[i + 2]]);
                    float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    for (float x = Mathf.Floor(x0 * 2) / 2 - 0.5f; x <= x1 + 0.5f; x += 0.5f)
                        for (float z = Mathf.Floor(z0 * 2) / 2 - 0.5f; z <= z1 + 0.5f; z += 0.5f)
                            if (InTriangle(new Vector2(x + 0.25f, z + 0.25f), a, b, c)) { covered.Add(new Vector2Int(Mathf.FloorToInt(x * 2), Mathf.FloorToInt(z * 2))); WaterCells++; }
                }
            }
            bool Inside(Vector3 p)
            {
                int gx = Mathf.FloorToInt(p.x * 2), gz = Mathf.FloorToInt(p.z * 2);
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (walk.ContainsKey(new Vector2Int(gx + dx, gz + dz))) return true;
                return false;
            }
            var pages = new Dictionary<int, (Color32[] px, int w, int h)>();
            (Color32[] px, int w, int h) Page(int sub)
            {
                if (pages.TryGetValue(sub, out var pg)) return pg;
                var tex = sub < materials.Length && materials[sub] != null ? materials[sub].mainTexture : null;
                pg = tex != null ? (PrototypeRemasterGate.Read(tex), tex.width, tex.height) : (null, 0, 0); pages[sub] = pg; return pg;
            }
            // Mean colour of a triangle's texture over interior barycentric samples; holes = cut-out share.
            Color Sample(int sub, Vector2 ua, Vector2 ub, Vector2 uc, out float holes)
            {
                var pg = Page(sub); holes = 0; if (pg.px == null) return Color.gray;
                int n = 0, cut = 0; Color sum = Color.clear;
                for (int p0 = 1; p0 < 8; p0++) for (int p1 = 1; p0 + p1 < 8; p1++)
                {
                    float wa = p0 / 8f, wb = p1 / 8f; Vector2 q = ua * wa + ub * wb + uc * (1 - wa - wb);
                    var c = pg.px[Mathf.Clamp((int)(q.y * pg.h), 0, pg.h - 1) * pg.w + Mathf.Clamp((int)(q.x * pg.w), 0, pg.w - 1)];
                    n++; if (c.a < 128) cut++; else sum += (Color)c;
                }
                holes = (float)cut / n; return n > cut ? sum / (n - cut) : Color.clear;
            }
            var candidates = new List<(Wall wall, Color colour, int steep)>();
            // Panels: steep faces of one submesh that share an edge and a plane are decided together (a quad's two
            // triangles are dropped both or neither, so no half walls remain).
            var steepNormals = new List<Vector3>(); var steepSub = new List<int>(); var parent = new List<int>();
            var edges = new Dictionary<(int, Vector3Int, Vector3Int), int>();
            Vector3Int Q(Vector3 p) => Vector3Int.RoundToInt(p * 100);
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
            {
                var m = materials[s]; if (m == null) continue;
                int[] tri = mesh.GetTriangles(s);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a); if (n.sqrMagnitude < 1e-8f) continue; n.Normalize();
                    if (Mathf.Abs(n.y) > 0.5f) continue;
                    int steepId = parent.Count; parent.Add(steepId); steepNormals.Add(n); steepSub.Add(s);
                    foreach (var (e0, e1) in new[] { (a, b), (b, c), (c, a) })
                    {
                        Vector3Int k0 = Q(e0), k1 = Q(e1);
                        bool swap = k0.x != k1.x ? k0.x > k1.x : k0.y != k1.y ? k0.y > k1.y : k0.z > k1.z; // undirected edge key
                        var key = swap ? (s, k1, k0) : (s, k0, k1);
                        if (edges.TryGetValue(key, out int other)) { if (Vector3.Dot(steepNormals[other], n) > 0.98f) parent[Find(other)] = Find(steepId); }
                        else edges[key] = steepId;
                    }
                    Vector3 h = new Vector3(n.x, 0, n.z).normalized, centre = (a + b + c) / 3;
                    bool front = Inside(centre + h * 2), back = Inside(centre - h * 2);
                    bool farFront = Inside(centre + h * 4) || Inside(centre + h * 8) || Inside(centre + h * 12);
                    bool farBack = Inside(centre - h * 4) || Inside(centre - h * 8) || Inside(centre - h * 12);
                    Vector3 outward;
                    if (front && !back && !farBack) outward = -h; else if (back && !front && !farFront) outward = h; else continue;
                    // More than a quarter cut-out -> a fence/card, not a painted panel.
                    Color colour = Sample(s, uv[tri[i]], uv[tri[i + 1]], uv[tri[i + 2]], out float holeShare);
                    if (holeShare > 0.25f) continue;
                    // Only walls with open space behind them (no terrain face above or below at 1.5 m and 3 m):
                    // the band can stand there. A wall in front of a plateau or a cliff pocket stays.
                    // Checked from the centre and both ends of the lowest edge out to 8 m, so a thin gap in front of
                    // a plateau is not mistaken for open land.
                    bool open = true; Vector3[] ends = { a, b, c }; System.Array.Sort(ends, (x, y) => x.y.CompareTo(y.y));
                    foreach (var from in new[] { centre, Vector3.Lerp(ends[0], centre, 0.2f), Vector3.Lerp(ends[1], centre, 0.2f) })
                        foreach (float k in new[] { 1.5f, 3f, 5f, 8f })
                        {
                            Vector3 q = from + outward * k; var key = new Vector2Int(Mathf.FloorToInt(q.x * 2), Mathf.FloorToInt(q.z * 2));
                            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (covered.Contains(key + new Vector2Int(dx, dz))) open = false;
                        }
                    if (!open) continue;
                    // Foot = the walkable face just inside the wall (painted walls often continue far below it).
                    bool found = false; (float y, int sub, int tri) foot = default;
                    for (float k = 0.5f; k <= 3f && !found; k += 0.5f)
                    {
                        Vector3 q = centre - outward * k; var key = new Vector2Int(Mathf.FloorToInt(q.x * 2), Mathf.FloorToInt(q.z * 2));
                        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                            if (walk.TryGetValue(key + new Vector2Int(dx, dz), out var g) && g.y < centre.y + 6 && (!found || g.y > foot.y)) { foot = g; found = true; }
                    }
                    float low = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), high = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    var wall = new Wall { submesh = s, triangle = i / 3, a = a, b = b, c = c, outward = outward, baseY = found ? foot.y : low, topY = high, footSub = -1 };
                    if (found)
                    {
                        int[] ft = mesh.GetTriangles(foot.sub); int f0 = ft[foot.tri], f1 = ft[foot.tri + 1], f2 = ft[foot.tri + 2];
                        Vector2 u0 = uv[f0], u1 = uv[f1], u2 = uv[f2];
                        wall.footSub = foot.sub;
                        wall.footUv = Rect.MinMaxRect(Mathf.Min(u0.x, Mathf.Min(u1.x, u2.x)), Mathf.Min(u0.y, Mathf.Min(u1.y, u2.y)), Mathf.Max(u0.x, Mathf.Max(u1.x, u2.x)), Mathf.Max(u0.y, Mathf.Max(u1.y, u2.y)));
                        wall.footLight = lit ? (colors[f0] + colors[f1] + colors[f2]) / 3 : new Color(0.5f, 0.5f, 0.5f, 1);
                        Vector3 w0 = t.TransformPoint(v[f0]), w1 = t.TransformPoint(v[f1]), w2 = t.TransformPoint(v[f2]);
                        float world = (w1 - w0).magnitude + (w2 - w1).magnitude + (w0 - w2).magnitude, tex = (u1 - u0).magnitude + (u2 - u1).magnitude + (u0 - u2).magnitude;
                        wall.metresPerUv = tex > 1e-5f ? world / tex : 16;
                        wall.path = PathColour(Sample(foot.sub, u0, u1, u2, out _));
                    }
                    candidates.Add((wall, colour, steepId));
                }
            }
            // Page classification by the median colour of its boundary faces.
            var bySub = new Dictionary<int, List<Color>>();
            foreach (var (w, c, _) in candidates) { if (!bySub.TryGetValue(w.submesh, out var l)) bySub[w.submesh] = l = new List<Color>(); l.Add(c); }
            var forest = new HashSet<int>();
            foreach (var pair in bySub)
            {
                var gr = pair.Value.ConvertAll(c => c.g / Mathf.Max(1e-3f, c.r)); var gb = pair.Value.ConvertAll(c => c.g / Mathf.Max(1e-3f, c.b));
                gr.Sort(); gb.Sort();
                if (gr[gr.Count / 2] >= ForestGreenOverRed && gb[gb.Count / 2] >= ForestGreenOverBlue) forest.Add(pair.Key);
            }
            // A panel is dropped only when every one of its triangles passed all tests.
            var panelSize = new Dictionary<int, int>(); var panelPassed = new Dictionary<int, int>();
            for (int k = 0; k < parent.Count; k++) { int r = Find(k); panelSize[r] = (panelSize.TryGetValue(r, out int ps) ? ps : 0) + 1; }
            foreach (var cand in candidates) { int r = Find(cand.steep); panelPassed[r] = (panelPassed.TryGetValue(r, out int pp) ? pp : 0) + 1; }
            foreach (var (w, c, id) in candidates)
            {
                int r = Find(id);
                if (forest.Contains(w.submesh) && panelPassed[r] == panelSize[r]) walls.Add(w); else PartialPanels++;
            }
            ForestPages = new List<string>(); foreach (int sub in forest) ForestPages.Add(materials[sub].name);
            Walls = walls; return walls;
        }
        private static bool InTriangle(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Mathf.Abs(d) < 1e-9f) return false;
            float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.y - c.z)) / d, l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.y - c.z)) / d;
            return l1 >= -0.02f && l2 >= -0.02f && l1 + l2 <= 1.02f;
        }
        private static float BaseDistance(Wall w, Vector3 p)
        {
            Vector3[] q = { w.a, w.b, w.c }; System.Array.Sort(q, (x, y) => x.y.CompareTo(y.y));
            Vector2 s0 = new Vector2(q[0].x, q[0].z), s1 = new Vector2(q[1].x, q[1].z), pp = new Vector2(p.x, p.z);
            Vector2 seg = s1 - s0; float t = seg.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(pp - s0, seg) / seg.sqrMagnitude) : 0;
            return (pp - (s0 + seg * t)).magnitude;
        }

        private System.Collections.IEnumerator Start()
        {
            yield return null; // trees/vegetation/grass materials are created in their Start
            var collider = System.Array.Find(FindObjectsByType<MeshCollider>(FindObjectsSortMode.None), c => c.sharedMesh != null && c.sharedMesh.name == "level-01-object-1");
            if (collider == null) yield break;
            terrain = collider.GetComponent<MeshRenderer>();
            if (Walls == null) Detect(collider.sharedMesh, terrain.sharedMaterials, collider.transform);
            if (Walls.Count == 0) yield break;
            root = new GameObject("RemasterOuterWorld");
            waterLevel = float.MaxValue;
            foreach (var f in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (PrototypeRemasterEnvironment.IsWaterSurface(f.sharedMesh)) foreach (var v in f.sharedMesh.vertices) waterLevel = Mathf.Min(waterLevel, f.transform.TransformPoint(v).y);
            if (waterLevel == float.MaxValue) { waterLevel = float.MaxValue; foreach (var w in Walls) waterLevel = Mathf.Min(waterLevel, w.baseY); waterLevel -= 5; }
            BuildBand();
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }

        private static float Noise(float x, float z) => Mathf.PerlinNoise(x * 0.07f + 13.1f, z * 0.07f + 7.3f) + 0.5f * Mathf.PerlinNoise(x * 0.17f + 3.7f, z * 0.17f + 1.9f);
        private readonly List<Vector3> path = new List<Vector3>();
        // Level water level (lowest water-surface vertex; else 5 m below the lowest wall foot): band shores end there.
        private float waterLevel;
        public int SkirtQuads { get; private set; }
        private float PathDistance(Vector3 p) { float best = float.MaxValue; foreach (var s in path) best = Mathf.Min(best, new Vector2(p.x - s.x, p.z - s.z).magnitude); return best; }
        // Band height at p; false outside the band. nearest = index of the nearest wall (its foot ground is used).
        private bool OnBand(Vector3 p, out float height, out float distance, out int nearest)
        {
            height = 0; distance = float.MaxValue; nearest = -1; float wsum = 0, ysum = 0;
            for (int i = 0; i < Walls.Count; i++)
            {
                float d = BaseDistance(Walls[i], p);
                if (d < distance) { distance = d; nearest = i; }
                if (d < 8) { float k = 1 / (0.5f + d * d); wsum += k; ysum += k * Walls[i].baseY; }
            }
            if (distance > BandMeters) return false;
            int gx = Mathf.FloorToInt(p.x * 2), gz = Mathf.FloorToInt(p.z * 2); bool inside = false;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (covered.Contains(new Vector2Int(gx + dx, gz + dz))) inside = true;
            if (inside && distance > 1.5f) return false;
            float y0 = wsum > 0 ? ysum / wsum : Walls[nearest].baseY, d0 = distance;
            if (inside) { height = y0 - 0.08f; return true; } // skirt under the corridor's edge
            // Gentle rise into low hills (no bank: a steep rise reads as a new wall).
            height = y0 + 0.05f * d0 + 0.006f * d0 * d0 + (Noise(p.x, p.z) - 0.75f) * Mathf.Clamp01(d0 / 8) * 1.6f;
            foreach (var s in path) { float pd = new Vector2(p.x - s.x, p.z - s.z).magnitude; if (pd < 4) height = Mathf.Lerp(Mathf.Min(height, s.y + 0.1f), height, pd / 4); }
            // The far 14 m run down to the water level like a shore, so the band never ends in mid-air.
            height = Mathf.Lerp(height, waterLevel - 1.5f, Mathf.SmoothStep(0, 1, (d0 - (BandMeters - 14)) / 14));
            return true;
        }
        // A level collider (rock, boulder, prop, tree collision) above the band at p.
        private bool Occupied(Vector3 p, float y)
        {
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, y + 40, p.z), Vector3.down, 40 - 0.3f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<PrototypeMotor>() == null) return true;
            return false;
        }
        private Crop CropFor(Wall w)
        {
            if (w.footSub < 0) return null;
            foreach (var c in crops) if (c.sub == w.footSub && Vector2.Distance(c.uv.min, w.footUv.min) < 1e-3f && Vector2.Distance(c.uv.max, w.footUv.max) < 1e-3f) return c;
            var template = Resources.Load<Material>("Remaster/LevelLit");
            var m = new Material(template) { name = "OuterGround_" + w.footSub }; m.SetFloat("_Cutoff", 0); m.SetFloat("_UseVertexLighting", 1); m.SetFloat("_RockDetail", 0); m.SetFloat("_Cull", 2);
            var crop = new Crop { sub = w.footSub, uv = w.footUv, material = m, tileMetres = Mathf.Max(0.5f, Mathf.Max(w.footUv.width, w.footUv.height) * w.metresPerUv), light = w.footLight };
            crops.Add(crop); Refresh(crop); return crop;
        }
        // The crop follows the page the terrain currently shows (original 256 or the HD page).
        private void Refresh(Crop crop)
        {
            var mats = terrain != null ? terrain.sharedMaterials : null; if (mats == null || crop.sub >= mats.Length || mats[crop.sub] == null) return;
            var page = mats[crop.sub].mainTexture; if (page == null || page == crop.source) return;
            var px = PrototypeRemasterGate.Read(page); int w = page.width, h = page.height;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(crop.uv.xMin * w), 0, w - 1), y0 = Mathf.Clamp(Mathf.RoundToInt(crop.uv.yMin * h), 0, h - 1);
            int cw = Mathf.Clamp(Mathf.RoundToInt(crop.uv.width * w), 4, w - x0), ch = Mathf.Clamp(Mathf.RoundToInt(crop.uv.height * h), 4, h - y0);
            var tile = new Color32[cw * ch];
            for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) { var c = px[(y0 + y) * w + x0 + x]; c.a = 255; tile[y * cw + x] = c; }
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, true) { name = page.name + "-outer-crop", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(tile); tex.Apply(true, false);
            crop.material.SetTexture("_MainTex", tex); crop.source = page;
        }
        private void LateUpdate()
        {
            if (!RemasterShown) return;
            foreach (var c in crops) { var mats = terrain.sharedMaterials; if (c.sub < mats.Length && mats[c.sub] != null && mats[c.sub].mainTexture != c.source) Refresh(c); }
        }

        private void BuildBand()
        {
            var random = new System.Random(2026);
            // Painted paths continue: per group of walls whose foot is path ground, a 26 m curve leaving along the
            // group's outward normal and bending aside, textured with that foot's path texture.
            var pathGroups = new List<List<Wall>>();
            foreach (var w in Walls)
            {
                if (!w.path) continue; Vector3 c = (w.a + w.b + w.c) / 3;
                var g = pathGroups.Find(x => Vector3.Distance((x[0].a + x[0].b + x[0].c) / 3, c) < 6); if (g != null) g.Add(w); else pathGroups.Add(new List<Wall> { w });
            }
            var strips = new List<(List<Vector3> points, Wall foot)>();
            foreach (var group in pathGroups)
            {
                Vector3 centre = Vector3.zero, outward = Vector3.zero; float baseY = float.MaxValue;
                foreach (var w in group) { centre += (w.a + w.b + w.c) / 3; outward += w.outward; baseY = Mathf.Min(baseY, w.baseY); }
                centre /= group.Count; outward.y = 0; outward.Normalize(); centre.y = baseY;
                Vector3 side = Vector3.Cross(Vector3.up, outward); var points = new List<Vector3>();
                for (int i = 0; i <= 26; i++) { float s = i / 26f; Vector3 p = centre + outward * (s * 22) + side * (Mathf.Sin(s * 2.2f) * 7f); p.y = baseY + 0.04f * i; points.Add(p); path.Add(p); }
                strips.Add((points, group[0])); PathStarts.Add(centre);
            }
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var w in Walls) foreach (var q in new[] { w.a, w.b, w.c }) { minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minZ = Mathf.Min(minZ, q.z); maxZ = Mathf.Max(maxZ, q.z); }
            minX -= BandMeters + 2; minZ -= BandMeters + 2; maxX += BandMeters + 2; maxZ += BandMeters + 2;
            int nx = Mathf.CeilToInt((maxX - minX) / Cell) + 1, nz = Mathf.CeilToInt((maxZ - minZ) / Cell) + 1;
            var heights = new float[nx, nz]; var valid = new bool[nx, nz]; var nearest = new int[nx, nz]; var dist = new float[nx, nz];
            bandHeights = heights; bandValid = valid; bandMinX = minX; bandMinZ = minZ; bandNx = nx; bandNz = nz;
            for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
                valid[i, k] = OnBand(new Vector3(minX + i * Cell, 0, minZ + k * Cell), out heights[i, k], out dist[i, k], out nearest[i, k]);
            float Height(Vector3 p) { float fx = (p.x - minX) / Cell, fz = (p.z - minZ) / Cell; int i = Mathf.Clamp((int)fx, 0, nx - 2), k = Mathf.Clamp((int)fz, 0, nz - 2); float ax = fx - i, az = fz - k;
                return Mathf.Lerp(Mathf.Lerp(heights[i, k], heights[i + 1, k], ax), Mathf.Lerp(heights[i, k + 1], heights[i + 1, k + 1], ax), az); }
            bool Usable(Vector3 p) { int i = (int)((p.x - minX) / Cell), k = (int)((p.z - minZ) / Cell); return i >= 0 && k >= 0 && i < nx - 1 && k < nz - 1 && valid[i, k] && valid[i + 1, k] && valid[i, k + 1] && valid[i + 1, k + 1]; }
            // Ground: one submesh per crop of the corridor ground (nearest wall's foot face).
            var groups = new Dictionary<Crop, (List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> t)>();
            var fallback = Walls.Find(w => w.footSub >= 0);
            for (int i = 0; i < nx - 1; i++) for (int k = 0; k < nz - 1; k++)
            {
                if (!(valid[i, k] && valid[i + 1, k] && valid[i, k + 1] && valid[i + 1, k + 1])) continue;
                var wall = Walls[nearest[i, k]]; var crop = CropFor(wall.footSub >= 0 ? wall : fallback); if (crop == null) continue;
                if (!groups.TryGetValue(crop, out var g)) groups[crop] = g = (new List<Vector3>(), new List<Vector2>(), new List<Color>(), new List<int>());
                int b0 = g.v.Count;
                foreach (var (di, dk) in new[] { (0, 0), (1, 0), (1, 1), (0, 1) })
                {
                    var p = new Vector3(minX + (i + di) * Cell, heights[i + di, k + dk], minZ + (k + dk) * Cell);
                    g.v.Add(p); g.uv.Add(new Vector2(p.x, p.z) / crop.tileMetres); // the crop repeats at the foot face's world scale
                    // The foot face's baked light, fading slightly into the forest.
                    float fade = Mathf.Lerp(1, 0.82f, Mathf.Clamp01(dist[i + di, k + dk] / BandMeters)); var light = crop.light * fade; light.a = 1; g.c.Add(light);
                }
                g.t.AddRange(new[] { b0, b0 + 3, b0 + 2, b0, b0 + 2, b0 + 1 });
            }
            // Skirts: every open edge of the band (no band quad beyond it) gets an earth bank down to below the water
            // level, so no band edge can hang in the air whatever the corridor's relief does there.
            bool Quad(int i, int k) => i >= 0 && k >= 0 && i < nx - 1 && k < nz - 1 && valid[i, k] && valid[i + 1, k] && valid[i, k + 1] && valid[i + 1, k + 1];
            for (int i = 0; i < nx - 1; i++) for (int k = 0; k < nz - 1; k++)
            {
                if (!Quad(i, k)) continue;
                var wall = Walls[nearest[i, k]]; var crop = CropFor(wall.footSub >= 0 ? wall : fallback); if (crop == null) continue;
                var g = groups[crop];
                foreach (var (di, dk, a0i, a0k, a1i, a1k) in new[] { (-1, 0, 0, 1, 0, 0), (1, 0, 1, 0, 1, 1), (0, -1, 0, 0, 1, 0), (0, 1, 1, 1, 0, 1) })
                {
                    if (Quad(i + di, k + dk)) continue;
                    Vector3 top0 = new Vector3(minX + (i + a0i) * Cell, heights[i + a0i, k + a0k], minZ + (k + a0k) * Cell), top1 = new Vector3(minX + (i + a1i) * Cell, heights[i + a1i, k + a1k], minZ + (k + a1k) * Cell);
                    float bottom = waterLevel - 2; if (Mathf.Min(top0.y, top1.y) <= bottom) continue;
                    Vector3 bot0 = new Vector3(top0.x, bottom, top0.z), bot1 = new Vector3(top1.x, bottom, top1.z);
                    int b0 = g.v.Count; var light = crop.light * 0.8f; light.a = 1;
                    foreach (var q in new[] { top0, top1, bot1, bot0 }) { g.v.Add(q); g.uv.Add(new Vector2(q.x + q.z, q.y) / crop.tileMetres); g.c.Add(light); }
                    g.t.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 }); SkirtQuads++;
                }
            }
            foreach (var pair in groups)
            {
                var mesh = new Mesh { name = "OuterBand", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(pair.Value.v); mesh.SetUVs(0, pair.Value.uv); mesh.SetColors(pair.Value.c); mesh.SetTriangles(pair.Value.t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject("OuterBand"); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key.material;
                BandTriangles += pair.Value.t.Count / 3;
            }
            // Path strips: the corridor path's own texture (the path walls' foot face), 1.8 m wide.
            foreach (var (points, foot) in strips)
            {
                var pathCrop = CropFor(foot); if (pathCrop == null || points.Count < 2) continue;
                var path = points;
                var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pc = new List<Color>(); var pt = new List<int>(); float along = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 dir = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]; dir.y = 0; dir.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * (0.9f + 0.2f * Mathf.Sin(i * 1.3f));
                    if (i > 0) along += (path[i] - path[i - 1]).magnitude;
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        Vector3 q = path[i] + side * sgn; q.y = (Usable(q) ? Height(q) : path[i].y) + 0.03f; pv.Add(q);
                        puv.Add(new Vector2((sgn * 0.9f) / pathCrop.tileMetres, along / pathCrop.tileMetres)); var l = pathCrop.light; l.a = 1; pc.Add(l);
                    }
                    if (i > 0) { int b0 = (i - 1) * 2; pt.AddRange(new[] { b0, b0 + 2, b0 + 3, b0, b0 + 3, b0 + 1 }); }
                }
                var pm = new Mesh { name = "OuterPath" }; pm.SetVertices(pv); pm.SetUVs(0, puv); pm.SetColors(pc); pm.SetTriangles(pt, 0); pm.RecalculateNormals(); pm.RecalculateBounds();
                var go = new GameObject("OuterPath"); go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = pm; go.AddComponent<MeshRenderer>().sharedMaterial = pathCrop.material;
                PathSegments += path.Count - 1;
            }
            // Original backdrop trees that stood beyond the walls over nothing (hidden by the painted walls) now
            // stand on the band where it reaches them.
            var motor = GetComponent<PrototypeMotor>();
            foreach (var f in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!f.name.StartsWith("Tree_")) continue; Vector3 b = f.transform.position;
                bool supported = motor != null && !float.IsNegativeInfinity(motor.GroundHeight(b + Vector3.up * 0.6f));
                if (!supported && Usable(b)) { f.transform.position = new Vector3(b.x, Height(b) - 0.1f, b.z); Resettled++; }
            }
            // Trees: a dense dark forest like the painted one, on a jittered 3 m grid.
            var trees = GetComponent<PrototypeRemasterTrees>(); Material bark = null, leaves = null;
            if (trees != null && trees.Bark != null)
            {
                bark = new Material(trees.Bark) { name = "OuterBark" }; bark.SetColor("_Color", trees.Bark.GetColor("_Color") * 0.7f);
                leaves = new Material(trees.Leaves) { name = "OuterLeaves" }; leaves.SetColor("_Color", new Color(0.36f, 0.44f, 0.32f, 1));
                for (float x = minX; x < maxX; x += 3) for (float z = minZ; z < maxZ; z += 3)
                {
                    var p = new Vector3(x + (float)random.NextDouble() * 2.6f, 0, z + (float)random.NextDouble() * 2.6f);
                    if (!Usable(p) || random.NextDouble() < 0.15) continue;
                    OnBand(p, out _, out float d, out _); if (d < 1.5f || PathDistance(p) < 2.2f) continue;
                    p.y = Height(p) - 0.1f; if (p.y < waterLevel + 0.4f) continue; if (Occupied(p, p.y)) { Blocked++; continue; }
                    float height = 9 + (float)random.NextDouble() * 9, width = height * (0.42f + 0.2f * (float)random.NextDouble());
                    var go = new GameObject("OuterTree"); go.transform.SetParent(root.transform, false); go.transform.position = p;
                    go.AddComponent<MeshFilter>().sharedMesh = PrototypeRemasterTrees.Build(height, width, random.Next(), 0);
                    go.AddComponent<MeshRenderer>().sharedMaterials = new[] { bark, leaves }; Trees++;
                }
            }
            // Ferns and shrubs (CC0), dense undergrowth.
            var plants = new List<GameObject>(); foreach (var asset in new[] { "fern_02", "fern_02", "shrub_02" }) { var pr = Resources.Load<GameObject>("Remaster/Vegetation/" + asset); if (pr != null) plants.Add(pr); }
            for (float x = minX; x < maxX && plants.Count > 0; x += 1.5f) for (float z = minZ; z < maxZ; z += 1.5f)
            {
                var p = new Vector3(x + (float)random.NextDouble() * 1.4f, 0, z + (float)random.NextDouble() * 1.4f);
                if (!Usable(p)) continue; OnBand(p, out _, out float d, out _); float pd = PathDistance(p);
                if (pd < 1.2f || random.NextDouble() > (d < 10 || pd < 4 ? 0.9 : 0.55)) continue;
                float y = Height(p); if (y < waterLevel + 0.3f) continue; if (Occupied(p, y)) { Blocked++; continue; }
                var prefab = plants[random.Next(plants.Count)]; var plant = Instantiate(prefab, root.transform);
                var parts = plant.GetComponentsInChildren<Renderer>(true); int child = random.Next(parts.Length);
                for (int i = 0; i < parts.Length; i++) parts[i].enabled = i == child;
                plant.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, random.Next(360), 0)); plant.transform.localScale = Vector3.one * (1.3f + 1.4f * (float)random.NextDouble());
                Bounds own = parts[child].bounds;
                plant.transform.position = new Vector3(p.x - own.center.x, y - own.min.y - 0.04f, p.z - own.center.z); Plants++;
            }
            // Grass tufts (the level's grass atlas), lit like the ground they stand on.
            var grassMat = Resources.Load<Material>("Remaster/Grass"); var atlas = Resources.Load<Texture2D>("Remaster/Vegetation/Grass");
            if (grassMat != null && atlas != null)
            {
                var gm = new Material(grassMat); gm.SetTexture("_MainTex", atlas); gm.SetColor("_Color", new Color(0.52f, 0.6f, 0.46f, 1));
                var chunks = new Dictionary<Vector2Int, (List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> tri, List<Vector2> h)>();
                for (float x = minX; x < maxX; x += 0.8f) for (float z = minZ; z < maxZ; z += 0.8f)
                {
                    var p = new Vector3(x + (float)random.NextDouble() * 0.8f, 0, z + (float)random.NextDouble() * 0.8f);
                    if (!Usable(p) || PathDistance(p) < 1.1f) continue; p.y = Height(p); if (p.y < waterLevel + 0.2f) continue;
                    OnBand(p, out _, out float d, out int w); if (Occupied(p, p.y)) continue;
                    var key = new Vector2Int(Mathf.FloorToInt(p.x / 12), Mathf.FloorToInt(p.z / 12));
                    if (!chunks.TryGetValue(key, out var chunk)) chunks[key] = chunk = (new List<Vector3>(), new List<Vector2>(), new List<Color>(), new List<int>(), new List<Vector2>());
                    float h = 0.35f + 0.5f * (float)random.NextDouble(); var light = Walls[w].footSub >= 0 ? Walls[w].footLight : new Color(0.5f, 0.5f, 0.5f);
                    var slope = new Vector3(Height(p - Vector3.right * 0.25f) - Height(p + Vector3.right * 0.25f), 0.5f, Height(p - Vector3.forward * 0.25f) - Height(p + Vector3.forward * 0.25f)).normalized;
                    PrototypeRemasterGrass.Tuft(chunk, p - new Vector3(key.x * 12, 0, key.y * 12), h, h * 1.1f, (float)random.NextDouble() * 180, new[] { 0, 2, 3 }[random.Next(3)], light, 0.02f + 0.98f * (float)random.NextDouble(), slope);
                    Tufts++;
                }
                foreach (var pair in chunks)
                {
                    var go = new GameObject("OuterGrass"); go.transform.SetParent(root.transform, false); go.transform.position = new Vector3(pair.Key.x * 12, 0, pair.Key.y * 12);
                    var m = new Mesh { name = "OuterGrass", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    m.SetVertices(pair.Value.v); m.SetUVs(0, pair.Value.uv); m.SetUVs(1, pair.Value.h); m.SetColors(pair.Value.c); m.SetTriangles(pair.Value.tri, 0); m.RecalculateBounds();
                    go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = gm; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        // Band surface height at p (test hook); false outside the band.
        private float[,] bandHeights; private bool[,] bandValid; private float bandMinX, bandMinZ; private int bandNx, bandNz;
        public bool BandHeightAt(Vector3 p, out float y)
        {
            y = 0; if (bandHeights == null) return false;
            float fx = (p.x - bandMinX) / Cell, fz = (p.z - bandMinZ) / Cell; int i = (int)fx, k = (int)fz;
            if (i < 0 || k < 0 || i >= bandNx - 1 || k >= bandNz - 1 || !(bandValid[i, k] && bandValid[i + 1, k] && bandValid[i, k + 1] && bandValid[i + 1, k + 1])) return false;
            float ax = fx - i, az = fz - k;
            y = Mathf.Lerp(Mathf.Lerp(bandHeights[i, k], bandHeights[i + 1, k], ax), Mathf.Lerp(bandHeights[i, k + 1], bandHeights[i + 1, k + 1], ax), az); return true;
        }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            if (root != null) root.SetActive(RemasterShown);
        }
    }
}
