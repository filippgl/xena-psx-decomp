using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster presentation only: replaces the boxy falling faces of a
    // waterfall surface with smooth water curtains. The lip (top edges of the
    // falling faces) is chained, its corners rounded (Chaikin), and the sheet
    // follows a ballistic arc from the lip down to the original fall bottom,
    // so the overall shape and footprint stay those of the original falls.
    internal static class PrototypeRemasterFalls
    {
        public sealed class Curtain { public Mesh mesh, foam, lipEmitter; public Bounds plunge; public float length, fallTime; public Vector3 outward; }
        private const float FallingNormalY = 0.75f, Spacing = 0.12f, LipSpeed = 1.1f, Gravity = 9.8f;
        private const int Rows = 28;

        private static Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 50), Mathf.RoundToInt(p.y * 50), Mathf.RoundToInt(p.z * 50));

        public static List<Curtain> Build(Mesh source, int[] triangles, Transform space, Vector4 uvRect, float flowSign, System.Func<Vector3, float> waterHeight)
        {
            Vector3[] local = source.vertices;
            int count = triangles.Length / 3;
            var world = new Vector3[triangles.Length];
            for (int i = 0; i < triangles.Length; i++) world[i] = space.TransformPoint(local[triangles[i]]);
            var falling = new List<int>(); var normal = new Dictionary<int, Vector3>();
            for (int f = 0; f < count; f++)
            {
                Vector3 n = Vector3.Cross(world[f * 3 + 1] - world[f * 3], world[f * 3 + 2] - world[f * 3]).normalized;
                if (Mathf.Abs(n.y) < FallingNormalY) { falling.Add(f); normal[f] = n; }
            }
            // Lip edges: roughly horizontal top edges of falling faces that no
            // other falling face continues upward.
            var edgeFaces = new Dictionary<(Vector3Int, Vector3Int), List<int>>();
            (Vector3Int, Vector3Int) EdgeKey(Vector3 a, Vector3 b) { Vector3Int ka = Key(a), kb = Key(b); return Less(ka, kb) ? (ka, kb) : (kb, ka); }
            foreach (int f in falling)
                for (int k = 0; k < 3; k++)
                {
                    var key = EdgeKey(world[f * 3 + k], world[f * 3 + (k + 1) % 3]);
                    if (!edgeFaces.TryGetValue(key, out var list)) edgeFaces[key] = list = new List<int>();
                    list.Add(f);
                }
            var lips = new List<(Vector3 a, Vector3 b, Vector3 outward)>();
            foreach (int f in falling)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 a = world[f * 3 + k], b = world[f * 3 + (k + 1) % 3], c = world[f * 3 + (k + 2) % 3];
                    if (c.y > Mathf.Min(a.y, b.y) - 0.1f || Mathf.Abs(a.y - b.y) > 0.35f * Vector3.Distance(a, b)) continue;
                    bool continued = false;
                    foreach (int other in edgeFaces[EdgeKey(a, b)])
                    {
                        if (other == f) continue;
                        for (int j = 0; j < 3; j++) if (world[other * 3 + j].y > Mathf.Max(a.y, b.y) + 0.1f) continued = true;
                    }
                    if (continued) continue;
                    Vector3 outward = Vector3.ProjectOnPlane(normal[f], Vector3.up).normalized;
                    lips.Add((a, b, outward));
                }
            // Chain lip edges through shared endpoints.
            var used = new bool[lips.Count]; var chains = new List<List<(Vector3 p, Vector3 d)>>();
            for (int s = 0; s < lips.Count; s++)
            {
                if (used[s]) continue; used[s] = true;
                var chain = new LinkedList<(Vector3 p, Vector3 d)>();
                chain.AddLast((lips[s].a, lips[s].outward)); chain.AddLast((lips[s].b, lips[s].outward));
                bool grown = true;
                while (grown)
                {
                    grown = false;
                    for (int i = 0; i < lips.Count; i++)
                    {
                        if (used[i]) continue;
                        Vector3Int head = Key(chain.First.Value.p), tail = Key(chain.Last.Value.p);
                        Vector3Int ka = Key(lips[i].a), kb = Key(lips[i].b);
                        if (ka == tail) { chain.AddLast((lips[i].b, lips[i].outward)); }
                        else if (kb == tail) { chain.AddLast((lips[i].a, lips[i].outward)); }
                        else if (kb == head) { chain.AddFirst((lips[i].a, lips[i].outward)); }
                        else if (ka == head) { chain.AddFirst((lips[i].b, lips[i].outward)); }
                        else continue;
                        used[i] = true; grown = true;
                    }
                }
                chains.Add(new List<(Vector3 p, Vector3 d)>(chain));
            }
            // The plunge pools of the big falls are the near-horizontal faces of
            // this same surface; fall back to the caller's water (e.g. @_stream).
            var pool = new List<Vector3>();
            for (int f = 0; f < count; f++) if (!normal.ContainsKey(f)) for (int k = 0; k < 3; k++) pool.Add(world[f * 3 + k]);
            float Surface(Vector3 at)
            {
                float best = 3f * 3f, height = float.NaN;
                foreach (Vector3 p in pool)
                {
                    if (p.y > at.y - 0.3f) continue;
                    float d = (p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z);
                    if (d < best) { best = d; height = p.y; }
                }
                return float.IsNaN(height) ? waterHeight(at) : height;
            }
            var curtains = new List<Curtain>();
            foreach (var chain in chains)
            {
                var points = Resample(Chaikin(Chaikin(chain)));
                if (points.Count < 2) continue;
                float span = 0; for (int i = 1; i < points.Count; i++) span += Vector3.Distance(points[i - 1].p, points[i].p);
                if (span < 0.9f) continue; // stray single edges, not a fall lip
                // Bottom: lowest falling-face point below this lip.
                Bounds lipBounds = new Bounds(points[0].p, Vector3.zero); foreach (var item in points) lipBounds.Encapsulate(item.p);
                lipBounds.Expand(new Vector3(1.2f, 100, 1.2f));
                float bottom = lipBounds.center.y;
                foreach (int f in falling) for (int k = 0; k < 3; k++) if (lipBounds.Contains(world[f * 3 + k])) bottom = Mathf.Min(bottom, world[f * 3 + k].y);
                curtains.Add(Sheet(points, bottom, uvRect, flowSign, Surface));
            }
            return curtains;
        }

        private static bool Less(Vector3Int a, Vector3Int b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        private static List<(Vector3 p, Vector3 d)> Chaikin(List<(Vector3 p, Vector3 d)> input)
        {
            if (input.Count < 3) return input;
            var output = new List<(Vector3 p, Vector3 d)> { input[0] };
            for (int i = 0; i < input.Count - 1; i++)
            {
                var a = input[i]; var b = input[i + 1];
                output.Add((Vector3.Lerp(a.p, b.p, 0.25f), Vector3.Slerp(a.d, b.d, 0.25f)));
                output.Add((Vector3.Lerp(a.p, b.p, 0.75f), Vector3.Slerp(a.d, b.d, 0.75f)));
            }
            output.Add(input[input.Count - 1]);
            return output;
        }

        private static List<(Vector3 p, Vector3 d)> Resample(List<(Vector3 p, Vector3 d)> input)
        {
            var output = new List<(Vector3 p, Vector3 d)> { input[0] }; float carry = 0;
            for (int i = 0; i < input.Count - 1; i++)
            {
                var a = input[i]; var b = input[i + 1]; float length = Vector3.Distance(a.p, b.p);
                for (float s = Spacing - carry; s < length; s += Spacing) output.Add((Vector3.Lerp(a.p, b.p, s / length), Vector3.Slerp(a.d, b.d, s / length).normalized));
                carry = (carry + length) % Spacing;
            }
            output.Add(input[input.Count - 1]);
            return output;
        }

        private static Curtain Sheet(List<(Vector3 p, Vector3 d)> lip, float bottom, Vector4 uvRect, float flowSign, System.Func<Vector3, float> waterHeight)
        {
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var indices = new List<int>();
            float total = 0; var along = new float[lip.Count];
            for (int i = 1; i < lip.Count; i++) { total += Vector3.Distance(lip[i - 1].p, lip[i].p); along[i] = total; }
            Bounds plunge = default; bool first = true;
            var foamVertices = new List<Vector3>(); var foamUvs = new List<Vector2>(); var foamIndices = new List<int>(); int foamRun = -1;
            for (int i = 0; i < lip.Count; i++)
            {
                // Meet the pool surface where the original fall plunges into water.
                float floor = bottom, water = waterHeight(lip[i].p + lip[i].d * 0.8f);
                bool wet = !float.IsNaN(water) && water < lip[i].p.y - 0.3f && water > bottom - 1.5f;
                if (wet) floor = water;
                float drop = Mathf.Max(0.2f, lip[i].p.y - floor), time = Mathf.Sqrt(2 * drop / Gravity);
                Vector3 plungePoint = lip[i].p + lip[i].d * (LipSpeed * time + 0.02f) + Vector3.down * drop;
                if (wet)
                {
                    int at = foamVertices.Count;
                    foreach (float side in new[] { -1f, 0f, 1f })
                    {
                        foamVertices.Add(new Vector3(plungePoint.x, water + 0.03f, plungePoint.z) + lip[i].d * side * (side < 0 ? 0.9f : 1.8f));
                        foamUvs.Add(new Vector2(along[i], side));
                    }
                    if (foamRun >= 0 && foamRun == at - 3)
                        for (int k = 0; k < 2; k++) { int a = foamRun + k, b = at + k; foamIndices.Add(a); foamIndices.Add(b); foamIndices.Add(a + 1); foamIndices.Add(a + 1); foamIndices.Add(b); foamIndices.Add(b + 1); }
                    foamRun = at;
                }
                else foamRun = -1;
                for (int r = 0; r <= Rows; r++)
                {
                    float s = (float)r / Rows, tau = s * time;
                    Vector3 point = lip[i].p + lip[i].d * (LipSpeed * tau + 0.02f) + Vector3.down * (0.5f * Gravity * tau * tau);
                    vertices.Add(point);
                    float fx = total > 0 ? along[i] / total : 0, fy = s;
                    uvs.Add(new Vector2(uvRect.x + fx * uvRect.z, uvRect.y + (flowSign > 0 ? fy : 1 - fy) * uvRect.w));
                    if (r == Rows) { if (first) { plunge = new Bounds(point, Vector3.zero); first = false; } else plunge.Encapsulate(point); }
                }
            }
            for (int i = 0; i < lip.Count - 1; i++)
                for (int r = 0; r < Rows; r++)
                {
                    int a = i * (Rows + 1) + r, b = a + 1, c = a + Rows + 1, d = c + 1;
                    indices.Add(a); indices.Add(c); indices.Add(b); indices.Add(b); indices.Add(c); indices.Add(d);
                }
            var mesh = new Mesh { name = "RemasterWaterCurtain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            plunge.Expand(new Vector3(0.6f, 0.4f, 0.6f));
            Mesh foam = null;
            if (foamIndices.Count > 0)
            {
                foam = new Mesh { name = "RemasterPlungeFoam" }; foam.SetVertices(foamVertices); foam.SetUVs(0, foamUvs); foam.SetTriangles(foamIndices, 0);
                foam.RecalculateNormals(); foam.RecalculateBounds();
            }
            Vector3 facing = Vector3.zero; foreach (var item in lip) facing += item.d;
            // Thin ribbon along the lip whose normals point outward: particle
            // emitter for falling streams (mesh-edge emission follows normals).
            var lipVertices = new List<Vector3>(); var lipNormals = new List<Vector3>(); var lipIndices = new List<int>(); float meanTime = 0;
            for (int i = 0; i < lip.Count; i++)
            {
                Vector3 at = lip[i].p + lip[i].d * 0.04f;
                lipVertices.Add(at + Vector3.up * 0.03f); lipVertices.Add(at - Vector3.up * 0.03f);
                Vector3 launch = (lip[i].d + Vector3.down * 0.15f).normalized; lipNormals.Add(launch); lipNormals.Add(launch);
                if (i > 0) { int a = (i - 1) * 2; lipIndices.Add(a); lipIndices.Add(a + 2); lipIndices.Add(a + 1); lipIndices.Add(a + 1); lipIndices.Add(a + 2); lipIndices.Add(a + 3); }
                meanTime += Mathf.Sqrt(2 * Mathf.Max(0.2f, lip[i].p.y - bottom) / Gravity) / lip.Count;
            }
            var emitter = new Mesh { name = "RemasterFallLip" }; emitter.SetVertices(lipVertices); emitter.SetNormals(lipNormals); emitter.SetTriangles(lipIndices, 0); emitter.RecalculateBounds();
            return new Curtain { mesh = mesh, foam = foam, lipEmitter = emitter, plunge = plunge, length = total, fallTime = meanTime, outward = facing.normalized };
        }
    }
}
