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
    public sealed class PrototypeRemasterGeometry : MonoBehaviour, IXenaVisualModeTarget
    {
        // Terrain, rock, two pillars, hole fill, bridge and outer jungle net.
        private static readonly string[] Targets = { "level-01-object-1", "level-01-object-65", "level-01-object-66", "level-01-object-67", "level-01-object-68", "level-01-object-39", "level-01-object-73" };
        public const int Segments = 3;
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

        private void Awake()
        {
            foreach (MeshFilter filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null || System.Array.IndexOf(Targets, filter.sharedMesh.name) < 0) continue;
                bool terrain = filter.sharedMesh.name == Targets[0];
                // Falling-water faces (terrain page-9) round their box corners.
                var rounded = new HashSet<int>(); Material[] bound = filter.GetComponent<MeshRenderer>().sharedMaterials;
                for (int i = 0; i < bound.Length; i++) if (bound[i] != null && bound[i].name.StartsWith("page-9_")) rounded.Add(i);
                Mesh remaster = Subdivide(filter.sharedMesh, rounded, terrain ? TerrainBulge : PropBulge, filter.transform.InverseTransformDirection(Vector3.up).normalized, out float offset, out float walkOffset, out int seams, filter.transform.lossyScale.y);
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

        private static Mesh Subdivide(Mesh source, HashSet<int> rounded, float bulge, Vector3 localUp, out float maxOffset, out float walkableOffset, out int seamCount, float worldScaleY)
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
            float crease = Mathf.Cos(CreaseDegrees * Mathf.Deg2Rad), fallCrease = Mathf.Cos(FallCreaseDegrees * Mathf.Deg2Rad);
            // Symmetric per pair, so both sides of an edge agree.
            bool Smooth(int a, int b) => Vector3.Dot(unitNormal[a], unitNormal[b]) > (rounded.Contains(faceSubmesh[a]) && rounded.Contains(faceSubmesh[b]) ? fallCrease : crease);
            // Floors/ceilings stay exactly original so feet match the original
            // height query; only steep cliff and rock faces become curved.
            bool Walkable(int f) => Mathf.Abs(Vector3.Dot(unitNormal[f], localUp)) > 0.7f;
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
            maxOffset = 0; walkableOffset = 0; seamCount = 0;
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
                        Vector3 point = linear + (curved - linear) * bulge;
                        float offset = (point - linear).magnitude * worldScaleY; maxOffset = Mathf.Max(maxOffset, offset);
                        if (Walkable(f)) walkableOffset = Mathf.Max(walkableOffset, offset);
                        outPositions.Add(point);
                        outNormal.Add((t * n0 + u * n1 + w * n2).normalized);
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
                        if (hasColor) outColor.Add(t * c0 + u * c1 + w * c2);
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
