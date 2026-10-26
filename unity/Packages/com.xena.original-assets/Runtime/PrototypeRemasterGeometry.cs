using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xena.Assets
{
    // Remaster presentation only: curved PN-triangle subdivision of the
    // original level terrain and large solid props. Hard edges (dihedral above
    // the crease angle) and open borders stay straight, and soft shared edges
    // use one symmetric edge normal, so neighbouring faces remain watertight.
    // MeshColliders, the original height query, CLS and placements keep the
    // original meshes; Original mode restores the original MeshFilter mesh.
    // Rock relief: non-walkable faces are displaced up to the relief amplitude along a smooth vertex
    // normal by 3D fBm noise of the position, so straight hard edges and flat faces read as
    // rough stone. The weight is built only from vertex/edge data both neighbours share
    // (vertex weights, a bump on free edges, the face interior), so the mesh stays watertight;
    // it is 0 on walkable faces and on edges/vertices touching them or open borders, so the
    // ground under Xena stays exact. Shading normals soften the hard corners.
    // Tone harmonisation: each face's mean texture colour is measured on its page; at every
    // corner the faces of one colour family (similar chroma, e.g. dark and light grass) share a
    // target tone, and the vertex colour pulls the face toward it near its edges (1 on the edge,
    // 0 at the centre), so similar textures on neighbouring faces no longer meet in a hard step
    // while distinct materials (path vs grass) keep their border.
    public sealed class PrototypeRemasterGeometry : MonoBehaviour, IXenaVisualModeTarget
    {
        // Terrain, rock, two pillars, hole fill, bridge and outer jungle net.
        private static readonly string[] Targets = { "level-01-object-1", "level-01-object-65", "level-01-object-66", "level-01-object-67", "level-01-object-68", "level-01-object-39", "level-01-object-73" };
        public const int Segments = 6;
        // Relief amplitude: terrain cliffs / free-standing rocks and pillars (meshes 65-67, which
        // also round every edge like natural boulders: crease 180).
        public const float TerrainReliefMeters = 0.18f, RockReliefMeters = 0.25f;
        // Terrain cliffs also round their right-angle corners (crease 120), with the PN bulge
        // capped so long walls do not balloon.
        private const float TerrainCreaseDegrees = 120, TerrainMaxBulgeMeters = 0.45f;
        private static readonly string[] Boulders = { "level-01-object-65", "level-01-object-66", "level-01-object-67" };
        private const float CreaseDegrees = 70, FallCreaseDegrees = 100, TerrainBulge = 0.6f, PropBulge = 1;
        private sealed class Pair { public MeshFilter filter; public MeshRenderer renderer; public Mesh original, remaster; }
        private readonly Dictionary<(Material, bool), Material> litMaterials = new Dictionary<(Material, bool), Material>();
        private Material litTemplate;
        public int LitSlots { get; private set; }
        private readonly List<Pair> pairs = new List<Pair>();
        public int OriginalTriangles { get; private set; }
        public int RemasterTriangles { get; private set; }
        public float MaxTerrainOffset { get; private set; }
        public float MaxWalkableOffset { get; private set; }
        public bool RemasterActive { get; private set; }
        public int SeamEdges { get; private set; }
        public float MaxReliefMeters { get; private set; }
        public int DroppedBackdropTriangles { get; private set; }

        private void Awake()
        {
            foreach (MeshFilter filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null || System.Array.IndexOf(Targets, filter.sharedMesh.name) < 0) continue;
                bool terrain = filter.sharedMesh.name == Targets[0];
                // Falling-water faces (terrain page-9) round their box corners.
                var rounded = new HashSet<int>(); Material[] bound = filter.GetComponent<MeshRenderer>().sharedMaterials;
                for (int i = 0; i < bound.Length; i++) if (bound[i] != null && bound[i].name.StartsWith("page-9_")) rounded.Add(i);
                bool boulder = System.Array.IndexOf(Boulders, filter.sharedMesh.name) >= 0;
                var pages = new Texture2D[bound.Length]; for (int i = 0; i < bound.Length; i++) pages[i] = bound[i] != null ? bound[i].mainTexture as Texture2D : null;
                // Painted boundary walls (PrototypeRemasterOuterWorld) are not drawn in Remaster: their
                // collider stays, the world they depicted is built behind them.
                Mesh source = filter.sharedMesh;
                if (terrain)
                {
                    var drop = new HashSet<(int, int)>(); foreach (var w in PrototypeRemasterOuterWorld.Detect(source, bound, filter.transform)) drop.Add((w.submesh, w.triangle));
                    foreach (var w in PrototypeRemasterOuterWorld.InteriorWalls) drop.Add((w.submesh, w.triangle)); // drawn as hedges
                    if (drop.Count > 0)
                    {
                        source = Instantiate(filter.sharedMesh); source.name = filter.sharedMesh.name;
                        for (int s = 0; s < source.subMeshCount; s++)
                        {
                            int[] all = source.GetTriangles(s); var keep = new List<int>(all.Length);
                            for (int i = 0; i < all.Length; i += 3) if (!drop.Contains((s, i / 3))) { keep.Add(all[i]); keep.Add(all[i + 1]); keep.Add(all[i + 2]); }
                            source.SetTriangles(keep, s);
                        }
                        DroppedBackdropTriangles = drop.Count;
                    }
                }
                Mesh remaster = Subdivide(source, rounded, pages, terrain ? TerrainBulge : PropBulge, filter.transform.InverseTransformDirection(Vector3.up).normalized, out float offset, out float walkOffset, out int seams, filter.transform.lossyScale.y, out float relief,
                    terrain ? TerrainReliefMeters : boulder ? RockReliefMeters : 0, boulder ? 180 : terrain ? TerrainCreaseDegrees : CreaseDegrees, terrain ? TerrainMaxBulgeMeters : float.PositiveInfinity);
                MaxReliefMeters = Mathf.Max(MaxReliefMeters, relief);
                SeamEdges += seams;
                if (terrain) { MaxTerrainOffset = offset; MaxWalkableOffset = walkOffset; }
                pairs.Add(new Pair { filter = filter, renderer = filter.GetComponent<MeshRenderer>(), original = filter.sharedMesh, remaster = remaster });
                OriginalTriangles += filter.sharedMesh.triangles.Length / 3; RemasterTriangles += remaster.triangles.Length / 3;
            }
        }

        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterActive = mode == XenaVisualMode.Remaster;
            foreach (Pair pair in pairs) if (pair.filter != null) pair.filter.sharedMesh = RemasterActive ? pair.remaster : pair.original;
            // Original materials are restored by the importer bindings first;
            // Remaster replaces unlit level slots with the normal-shaped variant.
            LitSlots = 0;
            if (!RemasterActive) return;
            if (litTemplate == null) litTemplate = Resources.Load<Material>("Remaster/LevelLit");
            if (litTemplate == null) throw new System.InvalidOperationException("Remaster level material unavailable");
            // Every opaque level surface (incl. trees/bushes) gets the Remaster
            // shader so it shares the sky aerial perspective.
            var renderers = new List<MeshRenderer>();
            foreach (Pair pair in pairs) if (pair.renderer != null) renderers.Add(pair.renderer);
            GameObject level = GameObject.Find("OriginalLevel01");
            if (level != null) foreach (MeshRenderer item in level.GetComponentsInChildren<MeshRenderer>()) if (!renderers.Contains(item)) renderers.Add(item);
            foreach (MeshRenderer renderer in renderers)
            {
                bool rocky = pairs.Exists(pair => pair.renderer == renderer);
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material bound = slots[i];
                    if (bound == null || bound.shader.name != "Xena/OriginalUnlit") continue;
                    if (!litMaterials.TryGetValue((bound, rocky), out Material lit))
                    {
                        lit = new Material(litTemplate) { name = bound.name + "_lit", renderQueue = bound.renderQueue };
                        lit.SetTexture("_MainTex", bound.mainTexture);
                        foreach (string property in new[] { "_Cutoff", "_UseVertexLighting", "_LightBias", "_Cull" }) lit.SetFloat(property, bound.GetFloat(property));
                        lit.SetColor("_Color", bound.GetColor("_Color")); lit.SetFloat("_RockDetail", rocky ? 1 : 0); litMaterials[(bound, rocky)] = lit;
                    }
                    slots[i] = lit; LitSlots++;
                }
                renderer.sharedMaterials = slots;
            }
        }

        private static Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 4096), Mathf.RoundToInt(p.y * 4096), Mathf.RoundToInt(p.z * 4096));

        // 3D gradient noise (Perlin) and 3-octave fBm, deterministic.
        private static readonly int[] Perm = BuildPermutation();
        private static int[] BuildPermutation()
        {
            var r = new System.Random(1999); var p = new int[512]; var b = new int[256];
            for (int i = 0; i < 256; i++) b[i] = i;
            for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); int tmp = b[i]; b[i] = b[j]; b[j] = tmp; }
            for (int i = 0; i < 512; i++) p[i] = b[i & 255];
            return p;
        }
        private static float Grad(int h, float x, float y, float z) { h &= 15; float u = h < 8 ? x : y, v = h < 4 ? y : h == 12 || h == 14 ? x : z; return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v); }
        private static float Ease(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static float Perlin(Vector3 p)
        {
            int X = Mathf.FloorToInt(p.x), Y = Mathf.FloorToInt(p.y), Z = Mathf.FloorToInt(p.z);
            float x = p.x - X, y = p.y - Y, z = p.z - Z; X &= 255; Y &= 255; Z &= 255;
            float u = Ease(x), v = Ease(y), w = Ease(z);
            int A = Perm[X] + Y, AA = Perm[A] + Z, AB = Perm[A + 1] + Z, B = Perm[X + 1] + Y, BA = Perm[B] + Z, BB = Perm[B + 1] + Z;
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(Grad(Perm[AA], x, y, z), Grad(Perm[BA], x - 1, y, z), u), Mathf.Lerp(Grad(Perm[AB], x, y - 1, z), Grad(Perm[BB], x - 1, y - 1, z), u), v),
                Mathf.Lerp(Mathf.Lerp(Grad(Perm[AA + 1], x, y, z - 1), Grad(Perm[BA + 1], x - 1, y, z - 1), u), Mathf.Lerp(Grad(Perm[AB + 1], x, y - 1, z - 1), Grad(Perm[BB + 1], x - 1, y - 1, z - 1), u), v), w);
        }
        // Metres in, roughly -1..1 out: large forms plus finer cracks.
        private static float Relief(Vector3 m) => 0.62f * Perlin(m * 0.7f) + 0.28f * Perlin(m * 1.9f + new Vector3(17, 3, 9)) + 0.1f * Perlin(m * 4.6f + new Vector3(5, 29, 11));

        private static Mesh Subdivide(Mesh source, HashSet<int> rounded, Texture2D[] pages, float bulge, Vector3 localUp, out float maxOffset, out float walkableOffset, out int seamCount, float worldScaleY, out float maxRelief, float reliefMeters, float creaseDegrees, float maxBulgeMeters)
        {
            Vector3[] positions = source.vertices; Vector2[] uvs = source.uv; Color[] colors = source.colors;
            bool hasUv = uvs.Length == positions.Length, hasColor = colors.Length == positions.Length;
            var faces = new List<int>(); var faceSubmesh = new List<int>();
            for (int s = 0; s < source.subMeshCount; s++) { int[] t = source.GetTriangles(s); faces.AddRange(t); for (int i = 0; i < t.Length / 3; i++) faceSubmesh.Add(s); }
            int faceCount = faces.Count / 3;
            var faceNormal = new Vector3[faceCount]; var unitNormal = new Vector3[faceCount];
            var keys = new Vector3Int[faces.Count];
            var incident = new Dictionary<Vector3Int, List<int>>();
            var edges = new Dictionary<(Vector3Int, Vector3Int), List<int>>();
            for (int f = 0; f < faceCount; f++)
            {
                Vector3 a = positions[faces[f * 3]], b = positions[faces[f * 3 + 1]], c = positions[faces[f * 3 + 2]];
                faceNormal[f] = Vector3.Cross(b - a, c - a); unitNormal[f] = faceNormal[f].normalized;
                for (int k = 0; k < 3; k++)
                {
                    keys[f * 3 + k] = Key(positions[faces[f * 3 + k]]);
                    if (!incident.TryGetValue(keys[f * 3 + k], out var list)) incident[keys[f * 3 + k]] = list = new List<int>();
                    list.Add(f);
                }
                for (int k = 0; k < 3; k++)
                {
                    Vector3Int p = keys[f * 3 + k], q = keys[f * 3 + (k + 1) % 3];
                    var edge = Compare(p, q) < 0 ? (p, q) : (q, p);
                    if (!edges.TryGetValue(edge, out var list)) edges[edge] = list = new List<int>();
                    list.Add(f);
                }
            }
            // Relief anchors: vertices of walkable faces or open-border edges, and edges touching them.
            var anchoredVertex = new HashSet<Vector3Int>(); var anchoredEdge = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var pair in edges)
                if (pair.Value.Count != 2 || pair.Value.Exists(face => Vector3.Dot(unitNormal[face], localUp) > 0.7f))
                { anchoredEdge.Add(pair.Key); anchoredVertex.Add(pair.Key.Item1); anchoredVertex.Add(pair.Key.Item2); }
            // Smooth (crease-free, area-weighted) normal per position: shared by all faces there.
            var smoothNormal = new Dictionary<Vector3Int, Vector3>();
            foreach (var pair in incident) { Vector3 sum = Vector3.zero; foreach (int face in pair.Value) sum += faceNormal[face]; smoothNormal[pair.Key] = sum.sqrMagnitude > 0 ? sum.normalized : Vector3.up; }
            // Mean texture colour per face (7 samples inside its UV triangle).
            var tone = new Color[faceCount]; var toned = new bool[faceCount];
            for (int f = 0; f < faceCount && hasUv; f++)
            {
                int sub = faceSubmesh[f]; Texture2D page = sub < pages.Length ? pages[sub] : null;
                if (page == null || !page.isReadable) continue;
                Vector2 a = uvs[faces[f * 3]], b = uvs[faces[f * 3 + 1]], c = uvs[faces[f * 3 + 2]]; Color sum = Color.clear;
                foreach (var w in new[] { new Vector3(1f / 3, 1f / 3, 1f / 3), new Vector3(0.6f, 0.2f, 0.2f), new Vector3(0.2f, 0.6f, 0.2f), new Vector3(0.2f, 0.2f, 0.6f), new Vector3(0.4f, 0.4f, 0.2f), new Vector3(0.2f, 0.4f, 0.4f), new Vector3(0.4f, 0.2f, 0.4f) })
                { Vector2 uv = a * w.x + b * w.y + c * w.z; sum += page.GetPixelBilinear(uv.x, uv.y); }
                tone[f] = sum / 7; toned[f] = tone[f].a > 0.5f && tone[f].r + tone[f].g + tone[f].b > 0.03f;
            }
            bool Family(int a, int b)
            {
                if (!toned[a] || !toned[b]) return false;
                float sa = tone[a].r + tone[a].g + tone[a].b, sb = tone[b].r + tone[b].g + tone[b].b;
                return Mathf.Abs(tone[a].r / sa - tone[b].r / sb) + Mathf.Abs(tone[a].g / sa - tone[b].g / sb) + Mathf.Abs(tone[a].b / sa - tone[b].b / sb) < 0.09f;
            }
            // Target tone of face f at corner key: area-weighted mean over the incident faces of its family.
            Color Target(int f, Vector3Int at)
            {
                Color sum = Color.clear; float weight = 0;
                foreach (int g in incident[at]) if (g == f || Family(f, g)) { float area = faceNormal[g].magnitude; sum += tone[g] * area; weight += area; }
                return weight > 0 ? sum / weight : tone[f];
            }
            float crease = Mathf.Cos(creaseDegrees * Mathf.Deg2Rad), fallCrease = Mathf.Cos(FallCreaseDegrees * Mathf.Deg2Rad);
            // Symmetric per pair, so both sides of an edge agree.
            bool Smooth(int a, int b) => Vector3.Dot(unitNormal[a], unitNormal[b]) > (rounded.Contains(faceSubmesh[a]) && rounded.Contains(faceSubmesh[b]) ? fallCrease : crease);
            // Floors stay exactly original so feet match the original height query;
            // cliffs, rock faces and ceilings/overhangs (never stood on; the height query
            // uses the original data) become curved and rough.
            bool Walkable(int f) => Vector3.Dot(unitNormal[f], localUp) > 0.7f;
            Vector3 CornerNormal(int f, Vector3Int at)
            {
                Vector3 sum = Vector3.zero;
                foreach (int other in incident[at]) if (Smooth(f, other)) sum += faceNormal[other];
                return sum.sqrMagnitude > 0 ? sum.normalized : unitNormal[f];
            }
            // Baked part lighting differs at duplicated part borders; average
            // coincident corners inside one smoothing group to hide the steps.
            var cornerAt = new Dictionary<Vector3Int, List<int>>();
            for (int c = 0; c < faces.Count; c++) { if (!cornerAt.TryGetValue(keys[c], out var list)) cornerAt[keys[c]] = list = new List<int>(); list.Add(c); }
            Color CornerColor(int f, int corner)
            {
                Color sum = Color.clear; int count = 0;
                foreach (int c in cornerAt[keys[f * 3 + corner]])
                    if (Smooth(f, c / 3)) { sum += colors[faces[c]]; count++; }
                return count > 0 ? sum / count : colors[faces[f * 3 + corner]];
            }
            // Returns the shared-edge neighbour when the edge is soft, else -1.
            int SoftNeighbour(int f, Vector3Int p, Vector3Int q)
            {
                var list = edges[Compare(p, q) < 0 ? (p, q) : (q, p)];
                if (list.Count != 2) return -1;
                int other = list[0] == f ? list[1] : list[0];
                return Smooth(f, other) ? other : -1;
            }

            // Texture seams: a shared edge on the same page whose corners use
            // different UVs. Each side fades towards the neighbour's affine UV
            // continuation (clamped to that face's UV box): 50/50 on the edge.
            int SeamNeighbour(int f, int corner)
            {
                Vector3Int p = keys[f * 3 + corner], q = keys[f * 3 + (corner + 1) % 3];
                var list = edges[Compare(p, q) < 0 ? (p, q) : (q, p)];
                if (list.Count != 2 || !hasUv) return -1;
                int other = list[0] == f ? list[1] : list[0];
                // Ground UV breaks are intentional path/grass borders; keep them crisp.
                if (faceSubmesh[other] != faceSubmesh[f] || Walkable(f) || Walkable(other)) return -1;
                Vector2 Uv(int face, Vector3Int at) { for (int k = 0; k < 3; k++) if (keys[face * 3 + k] == at) return uvs[faces[face * 3 + k]]; return Vector2.zero; }
                bool split = (Uv(f, p) - Uv(other, p)).sqrMagnitude > 1e-8f || (Uv(f, q) - Uv(other, q)).sqrMagnitude > 1e-8f;
                return split ? other : -1;
            }
            Vector2 Continue(int face, Vector3 point, out float overshoot)
            {
                Vector3 q0 = positions[faces[face * 3]], q1 = positions[faces[face * 3 + 1]], q2 = positions[faces[face * 3 + 2]];
                Vector3 e1 = q1 - q0, e2 = q2 - q0, d = point - q0;
                float d11 = Vector3.Dot(e1, e1), d12 = Vector3.Dot(e1, e2), d22 = Vector3.Dot(e2, e2), denom = d11 * d22 - d12 * d12;
                overshoot = 0;
                if (Mathf.Abs(denom) < 1e-12f) return uvs[faces[face * 3]];
                float l1 = (d22 * Vector3.Dot(d, e1) - d12 * Vector3.Dot(d, e2)) / denom, l2 = (d11 * Vector3.Dot(d, e2) - d12 * Vector3.Dot(d, e1)) / denom;
                Vector2 u0 = uvs[faces[face * 3]], u1 = uvs[faces[face * 3 + 1]], u2 = uvs[faces[face * 3 + 2]];
                Vector2 uv = u0 + l1 * (u1 - u0) + l2 * (u2 - u0);
                Vector2 min = Vector2.Min(u0, Vector2.Min(u1, u2)), max = Vector2.Max(u0, Vector2.Max(u1, u2));
                Vector2 clamped = Vector2.Max(min, Vector2.Min(max, uv)); overshoot = (uv - clamped).magnitude;
                return clamped;
            }
            var seamUv = new[] { new List<Vector2>(), new List<Vector2>(), new List<Vector2>() }; var seamWeight = new List<Vector3>();
            int seamEdges = 0;
            var outPositions = new List<Vector3>(); var outUv = new List<Vector2>(); var outColor = new List<Color>(); var outNormal = new List<Vector3>();
            var outTriangles = new List<int>[source.subMeshCount]; for (int s = 0; s < source.subMeshCount; s++) outTriangles[s] = new List<int>();
            maxOffset = 0; walkableOffset = 0; seamCount = 0; maxRelief = 0;
            float metresPerUnit = Mathf.Max(1e-6f, worldScaleY), reliefUnits = reliefMeters / metresPerUnit;
            var cp = new Vector3[10];
            for (int f = 0; f < faceCount; f++)
            {
                int i0 = faces[f * 3], i1 = faces[f * 3 + 1], i2 = faces[f * 3 + 2];
                Vector3 p0 = positions[i0], p1 = positions[i1], p2 = positions[i2];
                Vector3Int k0 = keys[f * 3], k1 = keys[f * 3 + 1], k2 = keys[f * 3 + 2];
                Vector3 n0 = CornerNormal(f, k0), n1 = CornerNormal(f, k1), n2 = CornerNormal(f, k2);
                Color c0 = hasColor ? CornerColor(f, 0) : Color.white, c1 = hasColor ? CornerColor(f, 1) : Color.white, c2 = hasColor ? CornerColor(f, 2) : Color.white;
                // Edge control point near `from` on edge from->to.
                Vector3 Control(Vector3 from, Vector3 to, Vector3Int kf, Vector3Int kt, Vector3 nf)
                {
                    int other = SoftNeighbour(f, kf, kt);
                    if (other < 0 || Walkable(f) || Walkable(other)) return (2 * from + to) / 3;
                    Vector3 n = (nf + CornerNormal(other, kf)).normalized;
                    return (2 * from + to - Vector3.Dot(to - from, n) * n) / 3;
                }
                // Cubic Bezier triangle: b300 b030 b003, edges, centre b111.
                cp[0] = p0; cp[1] = p1; cp[2] = p2;
                cp[3] = Control(p0, p1, k0, k1, n0); cp[4] = Control(p1, p0, k1, k0, n1);
                cp[5] = Control(p1, p2, k1, k2, n1); cp[6] = Control(p2, p1, k2, k1, n2);
                cp[7] = Control(p2, p0, k2, k0, n2); cp[8] = Control(p0, p2, k0, k2, n0);
                Vector3 e = (cp[3] + cp[4] + cp[5] + cp[6] + cp[7] + cp[8]) / 6, v = (p0 + p1 + p2) / 3;
                cp[9] = Walkable(f) ? v : e + (e - v) / 2;
                int s0 = SeamNeighbour(f, 0), s1 = SeamNeighbour(f, 1), s2 = SeamNeighbour(f, 2);
                bool rough = reliefMeters > 0 && !Walkable(f) && !rounded.Contains(faceSubmesh[f]);
                float vw0 = anchoredVertex.Contains(k0) ? 0 : 1, vw1 = anchoredVertex.Contains(k1) ? 0 : 1, vw2 = anchoredVertex.Contains(k2) ? 0 : 1;
                bool Free(Vector3Int a, Vector3Int b) => !anchoredEdge.Contains(Compare(a, b) < 0 ? (a, b) : (b, a));
                bool free01 = Free(k0, k1), free12 = Free(k1, k2), free20 = Free(k2, k0);
                Vector3 m0 = smoothNormal[k0], m1 = smoothNormal[k1], m2 = smoothNormal[k2];
                Color g0 = toned[f] ? Target(f, k0) : Color.white, g1 = toned[f] ? Target(f, k1) : Color.white, g2 = toned[f] ? Target(f, k2) : Color.white;
                seamEdges += (s0 >= 0 ? 1 : 0) + (s1 >= 0 ? 1 : 0) + (s2 >= 0 ? 1 : 0);
                int start = outPositions.Count;
                for (int row = 0; row <= Segments; row++)
                    for (int col = 0; col <= Segments - row; col++)
                    {
                        float w = (float)row / Segments, u = (float)col / Segments, t = 1 - u - w;
                        Vector3 linear = t * p0 + u * p1 + w * p2;
                        Vector3 curved = t * t * t * cp[0] + u * u * u * cp[1] + w * w * w * cp[2]
                            + 3 * t * t * u * cp[3] + 3 * t * u * u * cp[4] + 3 * u * u * w * cp[5]
                            + 3 * u * w * w * cp[6] + 3 * w * w * t * cp[7] + 3 * w * t * t * cp[8] + 6 * t * u * w * cp[9];
                        Vector3 bulgeOffset = (curved - linear) * bulge;
                        float cap = maxBulgeMeters / Mathf.Max(1e-6f, worldScaleY);
                        if (bulgeOffset.magnitude > cap) bulgeOffset = bulgeOffset.normalized * cap;
                        Vector3 point = linear + bulgeOffset;
                        Vector3 shadeNormal = (t * n0 + u * n1 + w * n2).normalized;
                        if (rough)
                        {
                            // Weight from shared data only (see class comment), then fBm along the smooth normal.
                            float weight = Mathf.Max(t * vw0 + u * vw1 + w * vw2, 27 * t * u * w);
                            if (free01) weight = Mathf.Max(weight, 4 * t * u);
                            if (free12) weight = Mathf.Max(weight, 4 * u * w);
                            if (free20) weight = Mathf.Max(weight, 4 * w * t);
                            weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(weight));
                            Vector3 dir = (t * m0 + u * m1 + w * m2).normalized, metres = linear * metresPerUnit;
                            float h = Relief(metres);
                            point += dir * (h * weight * reliefUnits);
                            maxRelief = Mathf.Max(maxRelief, Mathf.Abs(h * weight) * reliefMeters);
                            // Shading: soften hard corners toward the smooth normal, tilt by the relief slope.
                            const float step = 0.05f;
                            Vector3 grad = new Vector3(Relief(metres + Vector3.right * step) - h, Relief(metres + Vector3.up * step) - h, Relief(metres + Vector3.forward * step) - h) / step;
                            Vector3 soft = Vector3.Slerp(shadeNormal, dir, 0.55f);
                            shadeNormal = (soft - Vector3.ProjectOnPlane(grad, soft) * (reliefMeters * weight)).normalized;
                        }
                        float offset = (point - linear).magnitude * worldScaleY; maxOffset = Mathf.Max(maxOffset, offset);
                        if (Walkable(f)) walkableOffset = Mathf.Max(walkableOffset, offset);
                        outPositions.Add(point);
                        outNormal.Add(shadeNormal);
                        Vector2 own = hasUv ? t * uvs[i0] + u * uvs[i1] + w * uvs[i2] : Vector2.zero;
                        if (hasUv) outUv.Add(own);
                        // Edge k joins corners k,k+1; its opposite barycentric is w,t,u.
                        float Fade(float opposite) => 0.5f * Mathf.Clamp01(1 - opposite * Segments);
                        // Clamped (stretched) continuation fades out; exact on the edge itself.
                        float Trust(float overshoot) => Mathf.Clamp01(1 - overshoot / 0.012f);
                        float o0 = 0, o1 = 0, o2 = 0;
                        Vector2 e0 = s0 >= 0 ? Continue(s0, linear, out o0) : own, e1 = s1 >= 0 ? Continue(s1, linear, out o1) : own, e2 = s2 >= 0 ? Continue(s2, linear, out o2) : own;
                        seamUv[0].Add(e0); seamUv[1].Add(e1); seamUv[2].Add(e2);
                        seamWeight.Add(new Vector3(s0 >= 0 ? Fade(w) * Trust(o0) : 0, s1 >= 0 ? Fade(t) * Trust(o1) : 0, s2 >= 0 ? Fade(u) * Trust(o2) : 0));
                        Color lighting = hasColor ? t * c0 + u * c1 + w * c2 : Color.white;
                        if (toned[f])
                        {
                            // Pull toward the shared family tone near the edges (proximity 1 on an edge).
                            float near = Mathf.Clamp01(1 - 3 * Mathf.Min(t, Mathf.Min(u, w)));
                            Color shared = t * g0 + u * g1 + w * g2, mine = tone[f];
                            Vector3 factor = new Vector3(Mathf.Clamp(shared.r / Mathf.Max(mine.r, 0.01f), 0.6f, 1.6f), Mathf.Clamp(shared.g / Mathf.Max(mine.g, 0.01f), 0.6f, 1.6f), Mathf.Clamp(shared.b / Mathf.Max(mine.b, 0.01f), 0.6f, 1.6f));
                            factor = Vector3.Lerp(Vector3.one, factor, near * near * (3 - 2 * near));
                            lighting = new Color(Mathf.Min(1, lighting.r * factor.x), Mathf.Min(1, lighting.g * factor.y), Mathf.Min(1, lighting.b * factor.z), lighting.a);
                        }
                        if (hasColor) outColor.Add(lighting);
                    }
                int Index(int row, int col) { int offset = 0; for (int r = 0; r < row; r++) offset += Segments + 1 - r; return start + offset + col; }
                List<int> target = outTriangles[faceSubmesh[f]];
                for (int row = 0; row < Segments; row++)
                    for (int col = 0; col < Segments - row; col++)
                    {
                        target.Add(Index(row, col)); target.Add(Index(row, col + 1)); target.Add(Index(row + 1, col));
                        if (col < Segments - row - 1) { target.Add(Index(row, col + 1)); target.Add(Index(row + 1, col + 1)); target.Add(Index(row + 1, col)); }
                    }
            }
            var mesh = new Mesh { name = source.name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(outPositions); mesh.SetNormals(outNormal);
            if (hasUv) { mesh.SetUVs(0, outUv); mesh.SetUVs(1, seamUv[0]); mesh.SetUVs(2, seamUv[1]); mesh.SetUVs(3, seamUv[2]); mesh.SetUVs(4, seamWeight); }
            seamCount = seamEdges;
            if (hasColor) mesh.SetColors(outColor);
            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++) mesh.SetTriangles(outTriangles[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }
        private static int Compare(Vector3Int a, Vector3Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z);
    }
}
