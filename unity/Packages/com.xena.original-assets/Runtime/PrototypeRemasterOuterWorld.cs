using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster "outer world" (presentation only; user request 2026-10-26): the level is a corridor closed by
    // vertical boundary walls painted with forest scenery (terrain pages 3, 5, 12 and the path page 16).
    // Remaster stops drawing those painted walls (their colliders, the camera collision and gameplay stay the
    // original ones: the walls become invisible) and builds the world they depicted behind them:
    // - a terrain band up to BandMeters beyond every painted wall: at the wall line it starts at the wall's
    //   base height (so the walkable ground continues), climbs to the wall's top within 4 m, then rises
    //   (0.1 m/m + 0.01 m/m^2, fBm hills); it fills everything outside the corridor's hull (no terrain face
    //   above or below) within BandMeters; CC0 Poly Haven forrest_ground_01 tinted to the backdrops' forest floor;
    // - Remaster trees (PrototypeRemasterTrees generator), CC0 ferns/shrubs and grass tufts on the band;
    // - where the wall painted a path (page 16) the path continues as a curving dirt strip that disappears
    //   between the trees.
    // Painted walls = steep faces of the terrain mesh on those pages whose back has no walkable ground within
    // 12 m (boundary of the corridor); detected from the terrain mesh itself.
    public sealed class PrototypeRemasterOuterWorld : MonoBehaviour, IXenaVisualModeTarget
    {
        public static readonly string[] BackdropPages = { "page-3_", "page-5_", "page-12_", "page-16_" };
        public const string PathPage = "page-16_";
        public const float BandMeters = 30, Cell = 0.5f;
        public struct Wall { public int submesh, triangle; public Vector3 a, b, c, outward; public bool path; public float baseY, topY; }
        // XZ cells (0.5 m) covered by any non-steep terrain face (|ny| >= 0.5): inside the corridor hull.
        private static HashSet<Vector2Int> covered;
        // Computed once from the terrain mesh (also used by PrototypeRemasterGeometry to drop the faces).
        public static List<Wall> Walls { get; private set; }
        public int BandTriangles { get; private set; }
        public int Trees { get; private set; }
        public int Plants { get; private set; }
        public int Tufts { get; private set; }
        public int PathSegments { get; private set; }
        public bool RemasterShown { get; private set; }
        private GameObject root;

        public static List<Wall> Detect(Mesh mesh, Material[] materials, Transform t)
        {
            if (Walls != null) return Walls;
            var walls = new List<Wall>(); var v = mesh.vertices;
            // Walkable footprint (0.5 m grid) of the terrain mesh.
            var walk = new HashSet<Vector2Int>(); covered = new HashSet<Vector2Int>(); var groundY = new Dictionary<Vector2Int, float>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tri = mesh.GetTriangles(s);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a).normalized; if (Mathf.Abs(n.y) < 0.5f) continue; // steep (wall) faces never mark the hull: the band may pass under slanted painted walls
                    bool walkable = Mathf.Abs(n.y) >= 0.6f;
                    float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    for (float x = Mathf.Floor(x0 * 2) / 2; x <= x1; x += 0.5f)
                        for (float z = Mathf.Floor(z0 * 2) / 2; z <= z1; z += 0.5f)
                            if (InTriangle(new Vector2(x + 0.25f, z + 0.25f), a, b, c))
                            {
                                var cellKey = new Vector2Int(Mathf.FloorToInt(x * 2), Mathf.FloorToInt(z * 2));
                                covered.Add(cellKey);
                                if (walkable)
                                {
                                    walk.Add(cellKey);
                                    // Highest walkable surface of the cell (plane of the face at the cell centre).
                                    float y = a.y - (n.x * (x + 0.25f - a.x) + n.z * (z + 0.25f - a.z)) / n.y;
                                    groundY[cellKey] = groundY.TryGetValue(cellKey, out float old) ? Mathf.Max(old, y) : y;
                                }
                            }
                }
            }
            bool Inside(Vector3 p)
            {
                int gx = Mathf.FloorToInt(p.x * 2), gz = Mathf.FloorToInt(p.z * 2);
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (walk.Contains(new Vector2Int(gx + dx, gz + dz))) return true;
                return false;
            }
            for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
            {
                var m = materials[s]; if (m == null) continue;
                bool backdrop = false; foreach (var page in BackdropPages) if (m.name.StartsWith(page)) backdrop = true;
                if (!backdrop) continue;
                int[] tri = mesh.GetTriangles(s);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a); if (n.sqrMagnitude < 1e-8f) continue; n.Normalize();
                    if (Mathf.Abs(n.y) > 0.5f) continue;
                    Vector3 h = new Vector3(n.x, 0, n.z).normalized, centre = (a + b + c) / 3;
                    bool front = Inside(centre + h * 2), back = Inside(centre - h * 2);
                    bool farFront = Inside(centre + h * 4) || Inside(centre + h * 8) || Inside(centre + h * 12);
                    bool farBack = Inside(centre - h * 4) || Inside(centre - h * 8) || Inside(centre - h * 12);
                    Vector3 outward;
                    if (front && !back && !farBack) outward = -h; else if (back && !front && !farFront) outward = h; else continue;
                    // Foot = the walkable ground just inside the wall (painted walls often continue far below it);
                    // the lowest wall vertex only when no ground is found within 3 m.
                    float foot = float.NegativeInfinity;
                    for (float k = 0.5f; k <= 3f && float.IsNegativeInfinity(foot); k += 0.5f)
                    {
                        Vector3 q = centre - outward * k; var key = new Vector2Int(Mathf.FloorToInt(q.x * 2), Mathf.FloorToInt(q.z * 2));
                        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (groundY.TryGetValue(key + new Vector2Int(dx, dz), out float gy) && gy < centre.y + 6) foot = Mathf.Max(foot, gy);
                    }
                    float low = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), high = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    walls.Add(new Wall { submesh = s, triangle = i / 3, a = a, b = b, c = c, outward = outward, path = m.name.StartsWith(PathPage), baseY = float.IsNegativeInfinity(foot) ? low : foot, topY = Mathf.Max(high, float.IsNegativeInfinity(foot) ? low : foot) });
                }
            }
            Walls = walls; return walls;
        }
        private static bool InTriangle(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Mathf.Abs(d) < 1e-9f) return false;
            float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.y - c.z)) / d, l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.y - c.z)) / d;
            return l1 >= -0.02f && l2 >= -0.02f && l1 + l2 <= 1.02f;
        }
        // Distance to a wall's base segment (lowest edge) in XZ; base height there.
        private static float BaseDistance(Wall w, Vector3 p, out float baseY, out Vector3 closest)
        {
            Vector3[] q = { w.a, w.b, w.c }; System.Array.Sort(q, (x, y) => x.y.CompareTo(y.y));
            Vector2 s0 = new Vector2(q[0].x, q[0].z), s1 = new Vector2(q[1].x, q[1].z), pp = new Vector2(p.x, p.z);
            Vector2 seg = s1 - s0; float t = seg.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(pp - s0, seg) / seg.sqrMagnitude) : 0;
            Vector2 c2 = s0 + seg * t; baseY = w.baseY; closest = new Vector3(c2.x, baseY, c2.y);
            return (pp - c2).magnitude;
        }

        private System.Collections.IEnumerator Start()
        {
            yield return null; // trees/vegetation/grass materials are created in their Start
            if (Walls == null)
            {
                var collider = System.Array.Find(FindObjectsByType<MeshCollider>(FindObjectsSortMode.None), c => c.sharedMesh != null && c.sharedMesh.name == "level-01-object-1");
                if (collider == null) yield break;
                Detect(collider.sharedMesh, collider.GetComponent<MeshRenderer>().sharedMaterials, collider.transform);
            }
            if (Walls.Count == 0) yield break;
            root = new GameObject("RemasterOuterWorld");
            BuildBand();
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }

        private float noise(float x, float z) => Mathf.PerlinNoise(x * 0.07f + 13.1f, z * 0.07f + 7.3f) + 0.5f * Mathf.PerlinNoise(x * 0.17f + 3.7f, z * 0.17f + 1.9f);
        private readonly List<(Vector3 p, Vector3 dir)> path = new List<(Vector3, Vector3)>();
        private bool OnBand(Vector3 p, out float height, out float distance)
        {
            height = 0; distance = float.MaxValue; float best = float.MaxValue; float baseY = 0, topY = 0;
            float wsum = 0, ysum = 0, tsum = 0;
            foreach (var w in Walls)
            {
                float d = BaseDistance(w, p, out float by, out _);
                if (d < best) { best = d; baseY = by; topY = w.topY; }
                if (d < 8) { float k = 1 / (0.5f + d * d); wsum += k; ysum += k * by; tsum += k * w.topY; }
            }
            if (best > BandMeters) return false;
            // Outside the corridor's hull only: no terrain face above or below (0.5 m cells, 1-cell margin).
            // Within 1.5 m of a painted wall's foot the band also runs under the corridor's ground (a skirt 8 cm
            // below it) so no gap opens between the walkable edge and the band.
            int gx = Mathf.FloorToInt(p.x * 2), gz = Mathf.FloorToInt(p.z * 2); bool inside = false;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (covered.Contains(new Vector2Int(gx + dx, gz + dz))) inside = true;
            if (inside && best > 1.5f) return false;
            float y0 = wsum > 0 ? ysum / wsum : baseY, y1 = wsum > 0 ? tsum / wsum : topY, d0 = best;
            if (inside) { height = y0 - 0.08f; distance = d0; return true; }
            // From the wall's foot the ground climbs to the wall's top within 4 m (a forested bank that closes
            // the old wall's outline and its top faces), then keeps rising with hills.
            height = Mathf.Lerp(y0, y1 - 0.4f, Mathf.SmoothStep(0, 1, d0 / 4)) + 0.1f * d0 + 0.01f * d0 * d0 + (noise(p.x, p.z) - 0.75f) * Mathf.Clamp01(d0 / 6) * 2.2f;
            // A path carves a gentle valley.
            foreach (var s in path) { float pd = new Vector2(p.x - s.p.x, p.z - s.p.z).magnitude; if (pd < 4) height = Mathf.Lerp(Mathf.Min(height, s.p.y + 0.15f), height, pd / 4); }
            distance = d0; return true;
        }

        private void BuildBand()
        {
            var random = new System.Random(2026);
            // Paths: from every painted-path wall, a 26 m curve leaving along its outward normal and bending aside.
            var pathWalls = Walls.FindAll(w => w.path);
            if (pathWalls.Count > 0)
            {
                Vector3 centre = Vector3.zero, outward = Vector3.zero; float baseY = float.MaxValue;
                foreach (var w in pathWalls) { centre += (w.a + w.b + w.c) / 3; outward += w.outward; baseY = Mathf.Min(baseY, w.baseY); }
                centre /= pathWalls.Count; outward.y = 0; outward.Normalize(); centre.y = baseY;
                Vector3 side = Vector3.Cross(Vector3.up, outward);
                for (int i = 0; i <= 26; i++)
                {
                    float s = i / 26f; Vector3 p = centre + outward * (s * 22) + side * (Mathf.Sin(s * 2.2f) * 7f);
                    p.y = baseY + 0.05f * i + 0.02f * i * i * 0.5f; path.Add((p, Vector3.zero));
                }
                for (int i = 0; i < path.Count; i++) { Vector3 d = path[Mathf.Min(i + 1, path.Count - 1)].p - path[Mathf.Max(i - 1, 0)].p; d.y = 0; path[i] = (path[i].p, d.normalized); }
            }
            // Heightfield over the band.
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var w in Walls) foreach (var q in new[] { w.a, w.b, w.c }) { minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minZ = Mathf.Min(minZ, q.z); maxZ = Mathf.Max(maxZ, q.z); }
            minX -= BandMeters + 2; minZ -= BandMeters + 2; maxX += BandMeters + 2; maxZ += BandMeters + 2;
            int nx = Mathf.CeilToInt((maxX - minX) / Cell) + 1, nz = Mathf.CeilToInt((maxZ - minZ) / Cell) + 1;
            var heights = new float[nx, nz]; var valid = new bool[nx, nz]; var dist = new float[nx, nz];
            for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
            {
                var p = new Vector3(minX + i * Cell, 0, minZ + k * Cell);
                valid[i, k] = OnBand(p, out heights[i, k], out dist[i, k]);
            }
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>(); var index = new Dictionary<int, int>();
            int V(int i, int k)
            {
                int key = i * nz + k; if (index.TryGetValue(key, out int at)) return at;
                var p = new Vector3(minX + i * Cell, heights[i, k], minZ + k * Cell); verts.Add(p); uvs.Add(new Vector2(p.x, p.z) / 3f); index[key] = verts.Count - 1; return verts.Count - 1;
            }
            for (int i = 0; i < nx - 1; i++) for (int k = 0; k < nz - 1; k++)
                if (valid[i, k] && valid[i + 1, k] && valid[i, k + 1] && valid[i + 1, k + 1])
                {
                    int a = V(i, k), b = V(i + 1, k), c = V(i + 1, k + 1), d = V(i, k + 1);
                    tris.AddRange(new[] { a, d, c, a, c, b });
                }
            var template = Resources.Load<Material>("Remaster/LevelLit"); var groundTex = Resources.Load<Texture2D>("Remaster/Vegetation/ForestGround");
            if (template == null || groundTex == null || tris.Count == 0) return;
            groundTex.wrapMode = TextureWrapMode.Repeat;
            var ground = new Material(template) { name = "OuterGround" }; ground.SetTexture("_MainTex", groundTex); ground.SetFloat("_Cutoff", 0); ground.SetFloat("_UseVertexLighting", 0); ground.SetFloat("_RockDetail", 0); ground.SetFloat("_Cull", 2);
            ground.SetColor("_Color", new Color(0.22f, 0.3f, 0.16f, 1)); // leaf floor tinted to the painted backdrops' dark forest floor
            var mesh = new Mesh { name = "OuterBand", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var band = new GameObject("OuterBand"); band.transform.SetParent(root.transform, false);
            band.AddComponent<MeshFilter>().sharedMesh = mesh; band.AddComponent<MeshRenderer>().sharedMaterial = ground;
            BandTriangles = tris.Count / 3;
            float Height(Vector3 p) { float fx = (p.x - minX) / Cell, fz = (p.z - minZ) / Cell; int i = Mathf.Clamp((int)fx, 0, nx - 2), k = Mathf.Clamp((int)fz, 0, nz - 2); float ax = fx - i, az = fz - k;
                return Mathf.Lerp(Mathf.Lerp(heights[i, k], heights[i + 1, k], ax), Mathf.Lerp(heights[i, k + 1], heights[i + 1, k + 1], ax), az); }
            bool Usable(Vector3 p) { float fx = (p.x - minX) / Cell, fz = (p.z - minZ) / Cell; int i = (int)fx, k = (int)fz; return i >= 0 && k >= 0 && i < nx - 1 && k < nz - 1 && valid[i, k] && valid[i + 1, k] && valid[i, k + 1] && valid[i + 1, k + 1]; }
            float PathDistance(Vector3 p) { float best = float.MaxValue; foreach (var s in path) best = Mathf.Min(best, new Vector2(p.x - s.p.x, p.z - s.p.z).magnitude); return best; }
            // Path strip: ForestGround untinted (brown leaf litter) reads as a dirt path.
            if (path.Count > 1)
            {
                var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pt = new List<int>(); float along = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, path[i].dir) * (0.9f + 0.25f * Mathf.Sin(i * 1.3f));
                    if (i > 0) along += (path[i].p - path[i - 1].p).magnitude;
                    foreach (float sgn in new[] { -1f, 1f }) { Vector3 q = path[i].p + side * sgn; q.y = (Usable(q) ? Height(q) : path[i].p.y) + 0.04f; pv.Add(q); puv.Add(new Vector2(sgn * 0.33f, along / 3)); }
                    if (i > 0) { int b0 = (i - 1) * 2; pt.AddRange(new[] { b0, b0 + 2, b0 + 3, b0, b0 + 3, b0 + 1 }); }
                }
                var pm = new Mesh { name = "OuterPath" }; pm.SetVertices(pv); pm.SetUVs(0, puv); pm.SetTriangles(pt, 0); pm.RecalculateNormals(); pm.RecalculateBounds();
                var dirt = new Material(ground) { name = "OuterPath" }; dirt.SetColor("_Color", new Color(0.4f, 0.34f, 0.26f, 1)); // the painted path's dull grey-brown
                var go = new GameObject("OuterPath"); go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = pm; go.AddComponent<MeshRenderer>().sharedMaterial = dirt;
                PathSegments = path.Count - 1;
            }
            // Trees on a jittered 5 m grid (not on the path, not right at the wall line).
            var trees = GetComponent<PrototypeRemasterTrees>();
            // The painted backdrops show a dark, dense forest: the outer trees use darker copies of the tree materials.
            Material outerBark = null, outerLeaves = null;
            if (trees != null && trees.Bark != null)
            {
                outerBark = new Material(trees.Bark) { name = "OuterBark" }; outerBark.SetColor("_Color", trees.Bark.GetColor("_Color") * 0.7f);
                outerLeaves = new Material(trees.Leaves) { name = "OuterLeaves" }; outerLeaves.SetColor("_Color", new Color(0.36f, 0.44f, 0.32f, 1));
            }
            if (outerBark != null)
                for (float x = minX; x < maxX; x += 3) for (float z = minZ; z < maxZ; z += 3)
                {
                    var p = new Vector3(x + (float)random.NextDouble() * 2.6f, 0, z + (float)random.NextDouble() * 2.6f);
                    if (!Usable(p) || random.NextDouble() < 0.15) continue;
                    OnBand(p, out _, out float d); if (d < 1.5f || PathDistance(p) < 2.2f) continue;
                    p.y = Height(p) - 0.1f;
                    float height = 9 + (float)random.NextDouble() * 9, width = height * (0.42f + 0.2f * (float)random.NextDouble());
                    var go = new GameObject("OuterTree"); go.transform.SetParent(root.transform, false); go.transform.position = p;
                    var m = PrototypeRemasterTrees.Build(height, width, random.Next(), 0);
                    go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterials = new[] { outerBark, outerLeaves }; Trees++;
                }
            // Ferns and shrubs (CC0), denser near the wall line and along the path.
            var plants = new List<GameObject>(); foreach (var asset in new[] { "fern_02", "fern_02", "shrub_02" }) { var pr = Resources.Load<GameObject>("Remaster/Vegetation/" + asset); if (pr != null) plants.Add(pr); }
            if (plants.Count > 0)
                for (float x = minX; x < maxX; x += 1.5f) for (float z = minZ; z < maxZ; z += 1.5f)
                {
                    var p = new Vector3(x + (float)random.NextDouble() * 1.4f, 0, z + (float)random.NextDouble() * 1.4f);
                    if (!Usable(p)) continue; OnBand(p, out _, out float d); float pd = PathDistance(p);
                    if (pd < 1.2f || random.NextDouble() > (d < 10 || pd < 4 ? 0.9 : 0.55)) continue;
                    var prefab = plants[random.Next(plants.Count)]; var plant = Instantiate(prefab, root.transform);
                    var parts = plant.GetComponentsInChildren<Renderer>(true); int child = random.Next(parts.Length);
                    for (int i = 0; i < parts.Length; i++) parts[i].enabled = i == child;
                    plant.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, random.Next(360), 0)); plant.transform.localScale = Vector3.one * (1.3f + 1.4f * (float)random.NextDouble());
                    Bounds own = parts[child].bounds; float y = Height(p);
                    plant.transform.position = new Vector3(p.x - own.center.x, y - own.min.y - 0.04f, p.z - own.center.z); Plants++;
                }
            // Grass tufts (the level's grass atlas and material), 1.6 per m2, off the path.
            var grassMat = Resources.Load<Material>("Remaster/Grass"); var atlas = Resources.Load<Texture2D>("Remaster/Vegetation/Grass");
            if (grassMat != null && atlas != null)
            {
                var gm = new Material(grassMat); gm.SetTexture("_MainTex", atlas); gm.SetColor("_Color", new Color(0.36f, 0.45f, 0.3f, 1));
                var chunks = new Dictionary<Vector2Int, (List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> tri, List<Vector2> h)>();
                for (float x = minX; x < maxX; x += 0.8f) for (float z = minZ; z < maxZ; z += 0.8f)
                {
                    var p = new Vector3(x + (float)random.NextDouble() * 0.8f, 0, z + (float)random.NextDouble() * 0.8f);
                    if (!Usable(p) || PathDistance(p) < 1.1f) continue; p.y = Height(p);
                    var key = new Vector2Int(Mathf.FloorToInt(p.x / 12), Mathf.FloorToInt(p.z / 12));
                    if (!chunks.TryGetValue(key, out var chunk)) chunks[key] = chunk = (new List<Vector3>(), new List<Vector2>(), new List<Color>(), new List<int>(), new List<Vector2>());
                    float h = 0.35f + 0.5f * (float)random.NextDouble();
                    PrototypeRemasterGrass.Tuft(chunk, p - new Vector3(key.x * 12, 0, key.y * 12), h, h * 1.1f, (float)random.NextDouble() * 180, new[] { 0, 2, 3 }[random.Next(3)], new Color(0.5f, 0.5f, 0.5f), 0.02f + 0.98f * (float)random.NextDouble());
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

        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            if (root != null) root.SetActive(RemasterShown);
        }
    }
}
