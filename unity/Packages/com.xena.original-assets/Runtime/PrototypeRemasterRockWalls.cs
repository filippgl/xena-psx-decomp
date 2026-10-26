using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xena.Assets
{
    // Remaster rock walls (user request 2026-10-26: the flat moss-rock walls of the corridor -> real 3D rocks, for
    // every level). Shapes: three cliff-rock pieces made by the local Hunyuan3D-2 from Codex concepts painted from the
    // level's own rock texture (tools/codex-remaster/prompt-cliffrock*.txt, tools/install_cliff_rocks.py ->
    // Resources/Remaster/Rocks/cliffrocks.json). Universal placement, no per-level lists:
    // - rock walls = steep (|ny| < 0.5) faces of the level terrain whose texture region is opaque and reads as rock
    //   (median colour G < 1.5 R, R < 1.6 G: not painted forest, not a red sky backdrop; not falling water; not a
    //   painted backdrop the outer world replaces);
    // - each face is clad with rock pieces on a jittered 2.2 m grid in its plane, 2.2..3.2 m large, random turn about
    //   the face normal, the back sunk into the wall so the front stands at most 0.5 m proud of it (collision, the
    //   walkable edges and the camera keep the original wall); pieces never rise above the face's top edge;
    // - every piece wears the wall's own texture: its vertices are projected onto the face plane and take the face's
    //   UV / baked light by the face's affine (barycentric) map, so the rocks continue the Codex HD page around them.
    // Pieces of one terrain submesh are merged and drawn with that submesh's current Remaster material.
    public sealed class PrototypeRemasterRockWalls : MonoBehaviour, IXenaVisualModeTarget
    {
        [System.Serializable] private sealed class Shape { public string name; public float[] vertices; public int[] triangles; public float[] extents; }
        [System.Serializable] private sealed class ShapeFile { public Shape[] shapes; }
        // Proud 0.5 m: the Remaster terrain bulges rock walls by up to 0.45 m (PN cap) + 0.18 m relief, which hid
        // pieces standing 0.15 m proud; Xena (radius 0.375 m) stops at the original wall, so she grazes them at most.
        public const float Spacing = 2.2f, MinSize = 2.2f, MaxSize = 3.2f, Proud = 0.5f;
        public int Faces { get; private set; }
        public int Pieces { get; private set; }
        public int Flipped { get; private set; }
        public readonly List<(Vector3 centre, Vector3 normal)> CladFaces = new List<(Vector3, Vector3)>();
        public int Triangles { get; private set; }
        public bool RemasterShown { get; private set; }
        private GameObject root; private MeshRenderer terrain;
        private readonly List<(MeshRenderer renderer, int sub)> parts = new List<(MeshRenderer, int)>();

        private System.Collections.IEnumerator Start()
        {
            yield return null; yield return null; // outer world detects its backdrops first
            var json = Resources.Load<TextAsset>("Remaster/Rocks/cliffrocks"); if (json == null) yield break;
            var shapes = JsonUtility.FromJson<ShapeFile>(json.text).shapes; if (shapes == null || shapes.Length == 0) yield break;
            var collider = System.Array.Find(FindObjectsByType<MeshCollider>(FindObjectsSortMode.None), c => c.sharedMesh != null && c.sharedMesh.name == "level-01-object-1");
            if (collider == null) yield break;
            terrain = collider.GetComponent<MeshRenderer>();
            var mesh = collider.sharedMesh; var t = collider.transform; var v = mesh.vertices; var uv = mesh.uv; var col = mesh.colors; bool lit = col.Length == v.Length;
            var mats = terrain.sharedMaterials;
            var dropped = new HashSet<(int, int)>(); if (PrototypeRemasterOuterWorld.Walls != null) foreach (var w in PrototypeRemasterOuterWorld.Walls) dropped.Add((w.submesh, w.triangle));
            var random = new System.Random(77);
            root = new GameObject("RemasterRockWalls");
            for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
            {
                var m = mats[s]; if (m == null || m.mainTexture == null || PrototypeRemasterEnvironment.IsFallMaterial(m)) continue;
                var px = PrototypeRemasterGate.Read(m.mainTexture); int tw = m.mainTexture.width, th = m.mainTexture.height;
                int[] tri = mesh.GetTriangles(s);
                var faces = new List<int>(); var colours = new List<Color>();
                for (int i = 0; i < tri.Length; i += 3)
                {
                    if (dropped.Contains((s, i / 3))) continue;
                    Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a); float area = n.magnitude / 2; if (area < 0.6f) continue; n.Normalize();
                    if (Mathf.Abs(n.y) > 0.5f) continue;
                    int cut = 0, count = 0; Color sum = Color.clear;
                    for (int p0 = 1; p0 < 6; p0++) for (int p1 = 1; p0 + p1 < 6; p1++)
                    {
                        float wa = p0 / 6f, wb = p1 / 6f; Vector2 q = uv[tri[i]] * wa + uv[tri[i + 1]] * wb + uv[tri[i + 2]] * (1 - wa - wb);
                        var cc = px[Mathf.Clamp((int)(q.y * th), 0, th - 1) * tw + Mathf.Clamp((int)(q.x * tw), 0, tw - 1)];
                        count++; if (cc.a < 128) cut++; else sum += (Color)cc;
                    }
                    if (cut * 4 > count) continue;
                    faces.Add(i); colours.Add(sum / Mathf.Max(1, count - cut));
                }
                if (faces.Count == 0) continue;
                // Rock page = median face colour not forest-green and not red.
                var gr = colours.ConvertAll(c => c.g / Mathf.Max(1e-3f, c.r)); var rg = colours.ConvertAll(c => c.r / Mathf.Max(1e-3f, c.g)); gr.Sort(); rg.Sort();
                if (gr[gr.Count / 2] >= PrototypeRemasterOuterWorld.ForestGreenOverRed || rg[rg.Count / 2] >= 1.6f) continue;
                var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pc = new List<Color>(); var pt = new List<int>();
                foreach (int i in faces)
                {
                    Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a).normalized; Faces++;
                    // Face the corridor: the side with standing ground near the wall (winding is not reliable on
                    // double-sided level faces).
                    Vector3 mid = (a + b + c) / 3; var motor = GetComponent<PrototypeMotor>();
                    if (motor != null)
                    {
                        Vector3 h = new Vector3(n.x, 0, n.z).normalized;
                        float front = motor.GroundHeight(mid + h * 1.5f + Vector3.up * 2), back = motor.GroundHeight(mid - h * 1.5f + Vector3.up * 2);
                        bool fOk = !float.IsNegativeInfinity(front) && front < mid.y + 1, bOk = !float.IsNegativeInfinity(back) && back < mid.y + 1;
                        if (bOk && !fOk) { n = -n; Flipped++; }
                        else if (!bOk && !fOk) { Faces--; continue; } // no standing ground on either side: not seen from the corridor
                    }
                    // In-plane frame: e1 horizontal along the face, e2 up the face.
                    Vector3 e1 = Vector3.Cross(Vector3.up, n).normalized; if (e1.sqrMagnitude < 1e-4f) e1 = (b - a).normalized; Vector3 e2 = Vector3.Cross(n, e1);
                    float topY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    if ((a - b).magnitude + (b - c).magnitude + (c - a).magnitude > 12) CladFaces.Add((mid, n));
                    Vector2 P(Vector3 w) => new Vector2(Vector3.Dot(w - a, e1), Vector3.Dot(w - a, e2));
                    Vector2 A = P(a), B = P(b), C = P(c);
                    float minU = Mathf.Min(A.x, Mathf.Min(B.x, C.x)), maxU = Mathf.Max(A.x, Mathf.Max(B.x, C.x)), minV = Mathf.Min(A.y, Mathf.Min(B.y, C.y)), maxV = Mathf.Max(A.y, Mathf.Max(B.y, C.y));
                    for (float gu = minU; gu < maxU; gu += Spacing) for (float gv = minV; gv < maxV; gv += Spacing)
                    {
                        var q = new Vector2(gu + (float)random.NextDouble() * Spacing, gv + (float)random.NextDouble() * Spacing);
                        if (!Bary(q, A, B, C, out _, out _, out _)) continue;
                        var shape = shapes[random.Next(shapes.Length)];
                        float size = Mathf.Lerp(MinSize, MaxSize, (float)random.NextDouble());
                        Vector3 centre = a + e1 * q.x + e2 * q.y;
                        // Never above the face's top edge (walkable rims stay clear).
                        size = Mathf.Min(size, Mathf.Max(0.6f, (topY - centre.y) * 2 / Mathf.Max(0.3f, shape.extents[1])));
                        float depth = shape.extents[2] * size;
                        var rot = Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(0, 0, (float)random.NextDouble() * 360);
                        Vector3 origin = centre + n * (Proud - depth / 2);
                        int baseIndex = pv.Count; var sv = shape.vertices;
                        for (int k = 0; k + 2 < sv.Length; k += 3)
                        {
                            Vector3 w = origin + rot * (new Vector3(sv[k], sv[k + 1], sv[k + 2]) * size);
                            pv.Add(w);
                            Bary(P(w), A, B, C, out float la, out float lb, out float lc); // extrapolated affine map
                            puv.Add(uv[tri[i]] * la + uv[tri[i + 1]] * lb + uv[tri[i + 2]] * lc);
                            pc.Add(lit ? col[tri[i]] * la + col[tri[i + 1]] * lb + col[tri[i + 2]] * lc : new Color(0.5f, 0.5f, 0.5f, 1));
                        }
                        foreach (int k in shape.triangles) pt.Add(baseIndex + k);
                        Pieces++;
                    }
                }
                if (pt.Count == 0) continue;
                var part = new Mesh { name = "RockWalls_" + s, indexFormat = IndexFormat.UInt32 };
                part.SetVertices(pv); part.SetUVs(0, puv); part.SetColors(pc); part.SetTriangles(pt, 0); part.RecalculateNormals(); part.RecalculateBounds();
                var go = new GameObject("RockWalls_" + m.name); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
                parts.Add((mr, s)); Triangles += pt.Count / 3;
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
        private static bool Bary(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float la, out float lb, out float lc)
        {
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-9f) { la = 1; lb = lc = 0; return false; }
            la = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d; lb = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d; lc = 1 - la - lb;
            return la >= 0 && lb >= 0 && lc >= 0;
        }
        // The terrain's Remaster material of each submesh is bound after the mode switch: follow it.
        private void LateUpdate()
        {
            if (!RemasterShown || terrain == null) return;
            var mats = terrain.sharedMaterials;
            foreach (var (r, s) in parts) if (r != null && s < mats.Length && r.sharedMaterial != mats[s]) r.sharedMaterial = mats[s];
        }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            if (root != null) root.SetActive(RemasterShown);
        }
    }
}
