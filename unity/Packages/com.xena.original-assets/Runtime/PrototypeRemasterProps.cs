using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster props generated image-to-3D (Codex concept -> Hunyuan3D-2 shape + paint,
    // tools/hunyuan_shape.py / hunyuan_paint.py / install_generated_prop.py ->
    // Resources/Remaster/Generated/<name>.prefab). Two kinds of originals are replaced in Remaster:
    // - placed props (level-script models, e.g. Sign_pt): the model goes under the prop root with a
    //   facing correction, scaled to the original height, base on the original base;
    // - static level objects (e.g. @2doors, @rck_cx): the model is fitted to the original's bounds on
    //   its oriented frame (principal x/z axis of the original vertices) on all three axes.
    // Collision and triggers stay the original ones. Original mode restores the originals.
    public sealed class PrototypeRemasterProps : MonoBehaviour, IXenaVisualModeTarget
    {
        private enum Kind { Placed, Level }
        // original name, generated prefab, kind, extra yaw (deg).
        private static readonly (string original, string generated, Kind kind, float yaw)[] Mapping =
        {
            // Sign_pt: the Hunyuan model garbled the letters and painted the post white (user: "inside
            // out"); Remaster keeps the original sign model with a Codex HD texture (Sign_pt page-0-4x).
            // @2doors: v1 rejected (catalogue red doors with iron the original lacks); v2 concept passed
            // the silhouette check (IoU 0.86) and is used pending the 3D model check.
            ("@2doors", "gate", Kind.Level, 0),
            ("@rck_cx", "boulder", Kind.Level, 0),
        };
        private readonly List<(Renderer[] original, GameObject generated)> props = new List<(Renderer[], GameObject)>();
        public int Replaced => props.Count;
        public bool RemasterShown { get; private set; }
        public string Describe()
        {
            var names = new List<string>(); foreach (var p in props) names.Add(p.generated.name + " " + Measure(p.generated).size.ToString("F2"));
            return string.Join(", ", names) + " | " + string.Join("; ", Footprints) + (Rejected.Count > 0 ? " | rejected: " + string.Join("; ", Rejected) : "");
        }

        private System.Collections.IEnumerator Start()
        {
            yield return null; // level scripts place their props in Start
            GameObject holder = null;
            foreach (var (original, generated, kind, yaw) in Mapping)
            {
                var prefab = Resources.Load<GameObject>("Remaster/Generated/" + generated); if (prefab == null) continue;
                if (kind == Kind.Placed)
                {
                    foreach (var body in FindObjectsByType<XenaAssetPreview>(FindObjectsSortMode.None))
                    {
                        if (!body.name.StartsWith(original)) continue;
                        var renderers = body.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) continue;
                        Bounds b = Union(renderers);
                        var root = body.transform.parent != null ? body.transform.parent : body.transform;
                        var model = Instantiate(prefab, root, false); model.name = generated;
                        model.transform.localRotation = Quaternion.Euler(0, yaw, 0); model.transform.localScale = Vector3.one; model.transform.localPosition = Vector3.zero;
                        Bounds own = Measure(model);
                        model.transform.localScale = Vector3.one * (b.size.y / Mathf.Max(0.01f, own.size.y));
                        own = Measure(model);
                        model.transform.position += new Vector3(b.center.x - own.center.x, b.min.y - own.min.y, b.center.z - own.center.z);
                        props.Add((renderers, model));
                    }
                    continue;
                }
                if (holder == null) holder = new GameObject("RemasterGeneratedProps");
                // Meshes of one object (e.g. the two gate leaves @2doors/@2doorsr) within 5 m form one group.
                var groups = new List<List<MeshRenderer>>();
                foreach (var candidate in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!candidate.name.Contains(original)) continue;
                    var group = groups.Find(g => Vector3.Distance(g[0].bounds.center, candidate.bounds.center) < 5);
                    if (group != null) group.Add(candidate); else groups.Add(new List<MeshRenderer> { candidate });
                }
                foreach (var group in groups)
                {
                    var renderer = group[0];
                    // Oriented frame of the original from its vertices: principal horizontal axis (PCA in
                    // x/z), extents along it, across it and vertically.
                    var points = new List<Vector3>();
                    foreach (var member in group) { var f = member.GetComponent<MeshFilter>(); if (f != null && f.sharedMesh != null) foreach (var v in f.sharedMesh.vertices) points.Add(f.transform.TransformPoint(v)); }
                    if (points.Count == 0) continue;
                    var world = points.ToArray(); Vector3 mean = Vector3.zero; foreach (var w in world) mean += w; mean /= world.Length;
                    float cxx = 0, cxz = 0, czz = 0; foreach (var w in world) { float dx = w.x - mean.x, dz = w.z - mean.z; cxx += dx * dx; cxz += dx * dz; czz += dz * dz; }
                    float angle = 0.5f * Mathf.Atan2(2 * cxz, cxx - czz); // long axis direction in x/z
                    Vector3 along = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)), across = new Vector3(-along.z, 0, along.x);
                    float aMin = float.MaxValue, aMax = float.MinValue, cMin = float.MaxValue, cMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
                    foreach (var w in world) { float a = Vector3.Dot(w - mean, along), c = Vector3.Dot(w - mean, across); aMin = Mathf.Min(aMin, a); aMax = Mathf.Max(aMax, a); cMin = Mathf.Min(cMin, c); cMax = Mathf.Max(cMax, c); yMin = Mathf.Min(yMin, w.y); yMax = Mathf.Max(yMax, w.y); }
                    Vector3 centre = mean + along * ((aMin + aMax) / 2) + across * ((cMin + cMax) / 2);
                    float length = aMax - aMin, depth = cMax - cMin, height = yMax - yMin;
                    var model = Instantiate(prefab, holder.transform, false); model.name = generated + "_" + renderer.name;
                    model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); model.transform.localScale = Vector3.one;
                    Bounds own = Measure(model);
                    // The model's long horizontal axis (x or z) goes onto the original's long axis.
                    bool modelLongX = own.size.x >= own.size.z;
                    float yawLong = -angle * Mathf.Rad2Deg; // world yaw that turns +x onto `along`
                    model.transform.rotation = Quaternion.Euler(0, (modelLongX ? yawLong : yawLong - 90) + yaw, 0);
                    float sLong = length / Mathf.Max(0.01f, modelLongX ? own.size.x : own.size.z), sDeep = depth / Mathf.Max(0.01f, modelLongX ? own.size.z : own.size.x);
                    model.transform.localScale = modelLongX ? new Vector3(sLong, height / Mathf.Max(0.01f, own.size.y), sDeep) : new Vector3(sDeep, height / Mathf.Max(0.01f, own.size.y), sLong);
                    // Place: centre of the oriented box, base on the original's lowest point.
                    own = Measure(model);
                    model.transform.position += new Vector3(centre.x - own.center.x, yMin - own.min.y, centre.z - own.center.z);
                    // Ground footprint (top-down area) must match the original within FootprintTolerance:
                    // match the area by horizontal scale and the centroid by a shift, then the Remaster
                    // object gets a collider of its own shape (Original keeps the original collision).
                    var originalTris = new List<(Vector3, Vector3, Vector3)>();
                    foreach (var member in group) Triangles(member.GetComponent<MeshFilter>(), originalTris);
                    for (int pass = 0; pass < 3; pass++)
                    {
                        var modelTris = new List<(Vector3, Vector3, Vector3)>(); foreach (var f in model.GetComponentsInChildren<MeshFilter>()) Triangles(f, modelTris);
                        var fp = Compare(originalTris, modelTris);
                        // Thin objects (doors: depth < 30 % of length) keep the oriented-box fit; area scaling
                        // would only thicken them.
                        if (pass == 2 || fp.modelArea <= 0 || depth < length * 0.3f) { RecordFootprint(model.name, fp); break; }
                        float k = Mathf.Sqrt(fp.originalArea / fp.modelArea);
                        var ls = model.transform.localScale; model.transform.localScale = new Vector3(ls.x * k, ls.y, ls.z * k);
                        own = Measure(model); model.transform.position += new Vector3(centre.x - own.center.x, yMin - own.min.y, centre.z - own.center.z);
                        var after = new List<(Vector3, Vector3, Vector3)>(); foreach (var f in model.GetComponentsInChildren<MeshFilter>()) Triangles(f, after);
                        var c = Compare(originalTris, after); model.transform.position += new Vector3(c.originalCentroid.x - c.modelCentroid.x, 0, c.originalCentroid.y - c.modelCentroid.y);
                    }
                    if (Mathf.Abs(LastFootprint.modelArea / Mathf.Max(1e-4f, LastFootprint.originalArea) - 1) > FootprintTolerance)
                    { Destroy(model); Rejected.Add(generated + ": footprint off by " + (LastFootprint.modelArea / Mathf.Max(1e-4f, LastFootprint.originalArea) - 1).ToString("P0")); continue; }
                    foreach (var f in model.GetComponentsInChildren<MeshFilter>()) { var col = f.gameObject.AddComponent<MeshCollider>(); col.sharedMesh = f.sharedMesh; }
                    props.Add((group.ConvertAll(m => (Renderer)m).ToArray(), model));
                }
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
        // User rule (2026-10-25): a Remaster object may differ from the original's ground footprint by
        // at most 10-15 %; within that its own collider is used in Remaster.
        public const float FootprintTolerance = 0.15f, Cell = 0.05f;
        public readonly List<string> Footprints = new List<string>(), Rejected = new List<string>();
        public (float originalArea, float modelArea, float iou, Vector2 originalCentroid, Vector2 modelCentroid) LastFootprint { get; private set; }
        private void RecordFootprint(string name, (float originalArea, float modelArea, float iou, Vector2 originalCentroid, Vector2 modelCentroid) fp)
        { LastFootprint = fp; Footprints.Add(name + string.Format(System.Globalization.CultureInfo.InvariantCulture, " footprint original {0:F2} m2 model {1:F2} m2 ({2:+0;-0}%) IoU {3:F2}", fp.originalArea, fp.modelArea, (fp.modelArea / Mathf.Max(1e-4f, fp.originalArea) - 1) * 100, fp.iou)); }
        private static void Triangles(MeshFilter f, List<(Vector3, Vector3, Vector3)> into)
        {
            if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) return;
            var v = f.sharedMesh.vertices; var t = f.sharedMesh.triangles;
            for (int i = 0; i < t.Length; i += 3) into.Add((f.transform.TransformPoint(v[t[i]]), f.transform.TransformPoint(v[t[i + 1]]), f.transform.TransformPoint(v[t[i + 2]])));
        }
        // Top-down rasterisation of both triangle sets on one 5 cm grid.
        private static (float originalArea, float modelArea, float iou, Vector2 originalCentroid, Vector2 modelCentroid) Compare(List<(Vector3, Vector3, Vector3)> a, List<(Vector3, Vector3, Vector3)> b)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var list in new[] { a, b }) foreach (var (p, q, r) in list) foreach (var v in new[] { p, q, r }) { x0 = Mathf.Min(x0, v.x); z0 = Mathf.Min(z0, v.z); x1 = Mathf.Max(x1, v.x); z1 = Mathf.Max(z1, v.z); }
            if (x1 < x0) return (0, 0, 0, Vector2.zero, Vector2.zero);
            int w = Mathf.Min(1024, Mathf.CeilToInt((x1 - x0) / Cell) + 1), h = Mathf.Min(1024, Mathf.CeilToInt((z1 - z0) / Cell) + 1);
            bool[] A = Raster(a, x0, z0, w, h), B = Raster(b, x0, z0, w, h);
            int na = 0, nb = 0, both = 0, any = 0; Vector2 ca = Vector2.zero, cb = Vector2.zero;
            for (int i = 0; i < A.Length; i++)
            {
                var cell = new Vector2(x0 + (i % w + 0.5f) * Cell, z0 + (i / w + 0.5f) * Cell);
                if (A[i]) { na++; ca += cell; } if (B[i]) { nb++; cb += cell; } if (A[i] && B[i]) both++; if (A[i] || B[i]) any++;
            }
            return (na * Cell * Cell, nb * Cell * Cell, any > 0 ? (float)both / any : 0, na > 0 ? ca / na : Vector2.zero, nb > 0 ? cb / nb : Vector2.zero);
        }
        private static bool[] Raster(List<(Vector3, Vector3, Vector3)> tris, float x0, float z0, int w, int h)
        {
            var m = new bool[w * h];
            foreach (var (p, q, r) in tris)
            {
                Vector2 a = new Vector2(p.x, p.z), b = new Vector2(q.x, q.z), c = new Vector2(r.x, r.z);
                float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y); if (Mathf.Abs(area) < 1e-8f) continue;
                int ix0 = Mathf.Max(0, (int)((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - x0) / Cell)), ix1 = Mathf.Min(w - 1, (int)((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - x0) / Cell));
                int iz0 = Mathf.Max(0, (int)((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - z0) / Cell)), iz1 = Mathf.Min(h - 1, (int)((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - z0) / Cell));
                for (int iz = iz0; iz <= iz1; iz++) for (int ix = ix0; ix <= ix1; ix++)
                {
                    Vector2 pt = new Vector2(x0 + (ix + 0.5f) * Cell, z0 + (iz + 0.5f) * Cell);
                    float w0 = ((b.x - pt.x) * (c.y - pt.y) - (c.x - pt.x) * (b.y - pt.y)) / area, w1 = ((c.x - pt.x) * (a.y - pt.y) - (a.x - pt.x) * (c.y - pt.y)) / area;
                    if (w0 >= 0 && w1 >= 0 && w0 + w1 <= 1) m[iz * w + ix] = true;
                }
            }
            return m;
        }
        private static Bounds Union(Renderer[] renderers) { Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); return b; }
        private static Bounds Measure(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            foreach (var (original, generated) in props)
            {
                foreach (var r in original) if (r != null) r.enabled = !RemasterShown;
                // Static level objects: their runtime MeshCollider (chakram/camera solids) gives way to the
                // generated object's own collider in Remaster (footprint within FootprintTolerance).
                if (generated != null && generated.GetComponentInChildren<MeshCollider>() != null)
                    foreach (var r in original) if (r != null && r.GetComponent<MeshCollider>() != null) r.GetComponent<MeshCollider>().enabled = !RemasterShown;
                if (generated != null) generated.SetActive(RemasterShown);
            }
        }
    }
}
