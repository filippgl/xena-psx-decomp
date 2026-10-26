using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Xena.Assets
{
    // Automatic level inspector (user request 2026-10-26: find visual defects without manual screenshots).
    //   XenaMovementPrototype.exe --inspect DIR   (tools/run_inspector.py builds the sheet and cleans up)
    // Walks a grid of standing points over the level's ground (eye 1.7 m, 8 headings, 6 deg down) and renders
    // each view twice into small render targets:
    // - holes: the camera clears to magenta with no sky; magenta below the horizon row = a hole through the world.
    //   Original shows its background through such openings by design, so a Remaster defect is a view whose hole
    //   share exceeds the Original one at the same view (missing ground, band gaps, open wall edges);
    // - stills: the normal frame, saved only for flagged views.
    // inspect.json lists every view with its hole share; views above the threshold are saved as PNGs.
    public sealed class PrototypeInspector : MonoBehaviour
    {
        [Serializable] private sealed class View { public float x, y, z; public int heading; public string mode; public float holes, originalHoles; public string file; }
        [Serializable] private sealed class Report { public string result = "FAIL", error; public int points, views, flagged; public float threshold; public List<View> views_ = new List<View>(); }
        private string output; private readonly Report report = new Report();
        public const float Grid = 6, Threshold = 0.002f; public const int Width = 480, Height = 270;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--inspect");
            if (at >= 0 && at + 1 < args.Length) new GameObject("Inspector").AddComponent<PrototypeInspector>().output = Path.GetFullPath(args[at + 1]);
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            for (int i = 0; i < 30; i++) yield return null;
            try { Directory.CreateDirectory(output); } catch { }
            var motor = FindFirstObjectByType<PrototypeMotor>(); var cam = Camera.main;
            var orbit = cam.GetComponent<PrototypeCamera>(); if (orbit != null) orbit.enabled = false;
            if (motor != null) motor.enabled = false;
            var level = GameObject.Find("OriginalLevel01");
            Bounds lv = new Bounds(motor.transform.position, Vector3.one); foreach (var r in level.GetComponentsInChildren<MeshRenderer>()) lv.Encapsulate(r.bounds);
            var points = new List<Vector3>();
            for (float x = lv.min.x; x <= lv.max.x; x += Grid) for (float z = lv.min.z; z <= lv.max.z; z += Grid)
            {
                // Every walkable level: query from the top down, then below each found floor.
                float from = lv.max.y + 2;
                for (int layer = 0; layer < 3; layer++)
                {
                    float g = motor.GroundHeight(new Vector3(x, from, z)); if (float.IsNegativeInfinity(g) || g > from) break;
                    var q = new Vector3(x, g + 1.7f, z); if (!points.Exists(o => (o - q).sqrMagnitude < 0.25f)) points.Add(q); from = g - 2.5f;
                }
            }
            report.points = points.Count; report.threshold = Threshold;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var keepFlags = cam.clearFlags; var keepColor = cam.backgroundColor; var keepTarget = cam.targetTexture;
            Color magenta = new Color(1, 0, 1, 1);
            var originalHoles = new List<float>();
            foreach (var mode in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
            {
                int index = 0;
                PrototypeGraphics.Active.SetMode(mode); for (int i = 0; i < 3; i++) yield return null;
                var horizon = GameObject.Find("OriginalHorizon"); bool horizonWas = horizon != null && horizon.activeSelf;
                foreach (var p in points)
                    for (int h = 0; h < 8; h++)
                    {
                        cam.transform.SetPositionAndRotation(p, Quaternion.Euler(6, h * 45, 0));
                        // Hole pass: magenta clear, no sky / background strip.
                        if (horizon != null) horizon.SetActive(false);
                        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = magenta; cam.targetTexture = rt; cam.Render();
                        RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); read.Apply(false);
                        var px = read.GetPixels32(); int below = 0, holes = 0, horizonRow = (int)(Height * 0.55f);
                        for (int y = 0; y < horizonRow; y++) for (int x = 0; x < Width; x += 2)
                        { var c = px[y * Width + x]; below++; if (c.r > 240 && c.g < 16 && c.b > 240) holes++; }
                        float share = (float)holes / below;
                        if (mode == XenaVisualMode.Original) { originalHoles.Add(share); index++; RenderTexture.active = null; cam.targetTexture = keepTarget; cam.clearFlags = keepFlags; cam.backgroundColor = keepColor; if (horizon != null) horizon.SetActive(horizonWas); if (index % 16 == 0) yield return null; continue; }
                        float before = originalHoles[index++];
                        cam.clearFlags = keepFlags; cam.backgroundColor = keepColor; if (horizon != null) horizon.SetActive(horizonWas);
                        var view = new View { x = p.x, y = p.y, z = p.z, heading = h * 45, mode = mode.ToString(), holes = share, originalHoles = before };
                        if (share - before > Threshold)
                        {
                            // Still: the normal frame, with the holes outlined in magenta for the reader.
                            if (mode == XenaVisualMode.Original) cam.clearFlags = CameraClearFlags.SolidColor;
                            cam.Render(); RenderTexture.active = rt; var still = new Texture2D(Width, Height, TextureFormat.RGBA32, false); still.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                            var sp = still.GetPixels32();
                            for (int i = 0; i < sp.Length; i++) if (px[i].r > 240 && px[i].g < 16 && px[i].b > 240 && ((i % Width) + (i / Width)) % 6 < 2) sp[i] = new Color32(255, 0, 255, 255);
                            still.SetPixels32(sp); still.Apply(false);
                            view.file = string.Format("inspect-{0}-{1:000}-{2}.png", mode.ToString().ToLowerInvariant(), report.views_.Count, h * 45);
                            File.WriteAllBytes(Path.Combine(output, view.file), still.EncodeToPNG()); Destroy(still);
                            cam.clearFlags = keepFlags; report.flagged++;
                        }
                        report.views_.Add(view); report.views++;
                        RenderTexture.active = null; cam.targetTexture = keepTarget;
                        if (report.views % 16 == 0) yield return null;
                    }
            }
            // Wall tour: every steep terrain face (> 1 m2) grouped within 8 m; per group, from the standing ground 5 m in
            // front (corridor side) at eye height, straight and 35 deg to either side, Original and Remaster stills
            // (wall-<n>-<view>-<mode>.png, listed in walls.txt) for a careful review of every wall.
            {
                var collider = Array.Find(FindObjectsByType<MeshCollider>(FindObjectsSortMode.None), c => c.sharedMesh != null && c.sharedMesh.name == "level-01-object-1");
                var groups = new List<(Vector3 c, Vector3 n, float area)>();
                if (collider != null)
                {
                    var tm = collider.sharedMesh; var tv = tm.vertices; var tt = tm.triangles; var tr = collider.transform;
                    for (int i = 0; i < tt.Length; i += 3)
                    {
                        Vector3 a = tr.TransformPoint(tv[tt[i]]), b = tr.TransformPoint(tv[tt[i + 1]]), c = tr.TransformPoint(tv[tt[i + 2]]);
                        Vector3 n = Vector3.Cross(b - a, c - a); float area = n.magnitude / 2; if (area < 1) continue; n.Normalize(); if (Mathf.Abs(n.y) > 0.5f) continue;
                        Vector3 mid = (a + b + c) / 3; int g = groups.FindIndex(x => Vector3.Distance(x.c, mid) < 8);
                        if (g < 0) groups.Add((mid, n * area, area)); else { var x = groups[g]; groups[g] = ((x.c * x.area + mid * area) / (x.area + area), x.n + n * area, x.area + area); }
                    }
                }
                var lines = new List<string>(); var big = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32); var bigRead = new Texture2D(960, 540, TextureFormat.RGB24, false);
                for (int k = 0; k < groups.Count; k++)
                {
                    Vector3 c = groups[k].c, h = groups[k].n; h.y = 0; if (h.sqrMagnitude < 1e-6f) continue; h.Normalize();
                    // Corridor side: standing ground in front.
                    Vector3 eye = Vector3.zero; bool found = false;
                    foreach (float side in new[] { 1f, -1f })
                        foreach (float back in new[] { 5f, 3.5f, 7f })
                        {
                            if (found) break; Vector3 q = c + h * side * back; float gq = motor.GroundHeight(new Vector3(q.x, c.y + 3, q.z));
                            if (!float.IsNegativeInfinity(gq) && gq < c.y + 1.5f) { eye = new Vector3(q.x, gq + 1.7f, q.z); found = true; if (side < 0) h = -h; }
                        }
                    if (!found) { lines.Add(k + " no ground in front " + c.ToString("F1")); continue; }
                    lines.Add(k + " wall area " + groups[k].area.ToString("F0") + " centre " + c.ToString("F1") + " eye " + eye.ToString("F1"));
                    foreach (var (tag, turn) in new[] { ("front", 0f), ("left", -35f), ("right", 35f) })
                    {
                        Vector3 look = Quaternion.Euler(0, turn, 0) * (new Vector3(c.x, eye.y - 0.4f, c.z) - eye);
                        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look));
                        foreach (var mode in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                        {
                            if (PrototypeGraphics.Active.Mode != mode) { PrototypeGraphics.Active.SetMode(mode); yield return null; yield return null; }
                            cam.targetTexture = big; cam.Render(); RenderTexture.active = big; bigRead.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); bigRead.Apply(false);
                            File.WriteAllBytes(Path.Combine(output, string.Format("wall-{0:000}-{1}-{2}.png", k, tag, mode.ToString().ToLowerInvariant())), bigRead.EncodeToJPG(85));
                            RenderTexture.active = null; cam.targetTexture = keepTarget;
                        }
                    }
                    if (k % 4 == 0) yield return null;
                }
                File.WriteAllText(Path.Combine(output, "walls.txt"), string.Join(Environment.NewLine, lines));
            }
            report.views_.Sort((a, b) => (b.holes - b.originalHoles).CompareTo(a.holes - a.originalHoles));
            report.result = "PASS";
            File.WriteAllText(Path.Combine(output, "inspect.json"), JsonUtility.ToJson(report, true));
            Application.Quit(0);
        }
    }
}
