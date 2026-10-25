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
    //   all three axes, its thinnest horizontal axis turned onto the original's thinnest one.
    // Collision and triggers stay the original ones. Original mode restores the originals.
    public sealed class PrototypeRemasterProps : MonoBehaviour, IXenaVisualModeTarget
    {
        private enum Kind { Placed, Level }
        // original name, generated prefab, kind, extra yaw (deg).
        private static readonly (string original, string generated, Kind kind, float yaw)[] Mapping =
        {
            ("Sign_pt", "signpost", Kind.Placed, -90),
            // @2doors: v1 concept rejected by the in-game style check (catalogue red doors with iron the
            // original lacks, build/pano/props3.jpg); v2 pending.
            ("@rck_cx", "boulder", Kind.Level, 0),
        };
        private readonly List<(Renderer[] original, GameObject generated)> props = new List<(Renderer[], GameObject)>();
        public int Replaced => props.Count;
        public bool RemasterShown { get; private set; }
        public string Describe()
        {
            var names = new List<string>(); foreach (var p in props) names.Add(p.generated.name + " " + Measure(p.generated).size.ToString("F2"));
            return string.Join(", ", names);
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
                foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!renderer.name.Contains(original)) continue;
                    Bounds b = renderer.bounds;
                    var model = Instantiate(prefab, holder.transform, false); model.name = generated + "_" + renderer.name;
                    model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); model.transform.localScale = Vector3.one;
                    Bounds own = Measure(model);
                    // Thin horizontal axis of each (x or z); turn the model's onto the original's.
                    bool modelThinX = own.size.x < own.size.z, originalThinX = b.size.x < b.size.z;
                    bool quarter = modelThinX != originalThinX;
                    model.transform.rotation = Quaternion.Euler(0, (quarter ? 90 : 0) + yaw, 0);
                    // Local scale per axis; after a quarter turn the model's local x lies along world z.
                    float sx = (quarter ? b.size.z : b.size.x) / Mathf.Max(0.01f, own.size.x), sz = (quarter ? b.size.x : b.size.z) / Mathf.Max(0.01f, own.size.z);
                    model.transform.localScale = new Vector3(sx, b.size.y / Mathf.Max(0.01f, own.size.y), sz);
                    own = Measure(model);
                    model.transform.position += new Vector3(b.center.x - own.center.x, b.min.y - own.min.y, b.center.z - own.center.z);
                    props.Add((new Renderer[] { renderer }, model));
                }
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
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
                if (generated != null) generated.SetActive(RemasterShown);
            }
        }
    }
}
