using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster props generated image-to-3D (Codex concept -> Hunyuan3D-2 shape + paint,
    // tools/hunyuan_shape.py / hunyuan_paint.py -> Resources/Remaster/Generated/<name>.prefab).
    // Each original placed prop of a mapped model is hidden in Remaster and the generated model is
    // put in its place: same root position and turn, scaled to the original's height, base on the
    // original base. Collision and triggers stay the original ones. Original mode restores them.
    public sealed class PrototypeRemasterProps : MonoBehaviour, IXenaVisualModeTarget
    {
        // Original prop model -> generated prefab and the generated model's facing correction (deg).
        private static readonly (string original, string generated, float yaw)[] Mapping = { ("Sign_pt", "signpost", -90) };
        private readonly List<(Renderer[] original, GameObject generated)> props = new List<(Renderer[], GameObject)>();
        public int Replaced => props.Count;
        public bool RemasterShown { get; private set; }
        public string Describe()
        {
            if (props.Count == 0) return "none";
            var f = props[0].generated.GetComponentInChildren<MeshFilter>(); var r = props[0].generated.GetComponentInChildren<Renderer>();
            var tex = r.sharedMaterial.GetTexture("_MainTex");
            return "uv " + f.sharedMesh.uv.Length + "/" + f.sharedMesh.vertexCount + " tex " + (tex != null ? tex.name + " " + tex.width : "null") + " shader " + r.sharedMaterial.shader.name + " size " + r.bounds.size.ToString("F2");
        }

        private System.Collections.IEnumerator Start()
        {
            yield return null; // level scripts place their props in Start
            foreach (var (original, generated, yaw) in Mapping)
            {
                var prefab = Resources.Load<GameObject>("Remaster/Generated/" + generated); if (prefab == null) continue;
                foreach (var body in FindObjectsByType<XenaAssetPreview>(FindObjectsSortMode.None))
                {
                    if (!body.name.StartsWith(original)) continue;
                    var renderers = body.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) continue;
                    Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                    var root = body.transform.parent != null ? body.transform.parent : body.transform;
                    var model = Instantiate(prefab, root, false); model.name = generated;
                    model.transform.localRotation = Quaternion.Euler(0, yaw, 0); model.transform.localScale = Vector3.one; model.transform.localPosition = Vector3.zero;
                    Bounds own = Measure(model);
                    float scale = b.size.y / Mathf.Max(0.01f, own.size.y); model.transform.localScale = Vector3.one * scale;
                    own = Measure(model);
                    model.transform.position += new Vector3(b.center.x - own.center.x, b.min.y - own.min.y, b.center.z - own.center.z);
                    props.Add((renderers, model));
                }
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
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
