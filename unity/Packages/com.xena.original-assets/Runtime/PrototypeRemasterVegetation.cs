using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster vegetation: the original ground plants @pretty (35 crossed cards, page-22) are
    // replaced by CC0 Poly Haven plants (fern_02: 4 ferns, shrub_02: a shrub set;
    // tools/fetch_polyhaven.py). Each asset is a set of separate plants laid out side by side, so
    // every child mesh is one variant. One variant per original instance: footprint centre on the
    // original ground, footprint fitted to the original cards (height at most 1.2x), a stable
    // per-instance variant and turn. Plants have no gameplay role (no colliders either way).
    // Original mode shows the original cards.
    public sealed class PrototypeRemasterVegetation : MonoBehaviour, IXenaVisualModeTarget
    {
        // Ferns three times as often as shrubs: the original cards show fan palms / ferns.
        private static readonly (string asset, int weight)[] GroundPlants = { ("fern_02", 3), ("shrub_02", 1) };
        private readonly List<(Renderer original, GameObject plant)> plants = new List<(Renderer, GameObject)>();
        public int Replaced => plants.Count;
        public int Variants { get; private set; }
        public bool RemasterShown { get; private set; }
        public GameObject FirstPlant => plants.Count > 0 ? plants[0].plant : null;

        private void Start()
        {
            var motor = GetComponent<PrototypeMotor>();
            var variants = new List<(GameObject prefab, int child)>();
            foreach (var (asset, weight) in GroundPlants)
            {
                var prefab = Resources.Load<GameObject>("Remaster/Vegetation/" + asset); if (prefab == null) continue;
                int count = prefab.GetComponentsInChildren<Renderer>(true).Length;
                for (int w = 0; w < weight; w++) for (int c = 0; c < count; c++) variants.Add((prefab, c));
            }
            Variants = variants.Count;
            if (variants.Count == 0) return;
            var root = new GameObject("RemasterVegetation");
            int index = 0;
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.name.Contains("@pretty")) continue;
                Bounds b = renderer.bounds; int hash = Mathf.Abs(Mathf.RoundToInt(b.center.x * 73 + b.center.z * 151));
                var (prefab, child) = variants[hash % variants.Count];
                var plant = Instantiate(prefab, root.transform); plant.name = prefab.name + "_" + child + "_" + index++;
                var parts = plant.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < parts.Length; i++) parts[i].enabled = i == child;
                plant.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, hash * 47 % 360, 0)); plant.transform.localScale = Vector3.one;
                Bounds own = parts[child].bounds;
                float scale = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.9f / Mathf.Max(0.05f, Mathf.Max(own.size.x, own.size.z)), 0.05f, 50f);
                if (own.size.y * scale > b.size.y * 1.2f) scale = b.size.y * 1.2f / own.size.y;
                plant.transform.localScale = Vector3.one * scale;
                own = parts[child].bounds;
                float ground = motor != null ? motor.GroundHeight(new Vector3(b.center.x, b.max.y + 0.5f, b.center.z)) : b.min.y;
                if (float.IsNegativeInfinity(ground) || ground > b.max.y || ground < b.min.y - 1) ground = b.min.y;
                plant.transform.position = new Vector3(b.center.x - own.center.x, ground - own.min.y - 0.03f, b.center.z - own.center.z);
                plants.Add((renderer, plant));
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            foreach (var (original, plant) in plants) { if (original != null) original.enabled = !RemasterShown; if (plant != null) plant.SetActive(RemasterShown); }
        }
    }
}
