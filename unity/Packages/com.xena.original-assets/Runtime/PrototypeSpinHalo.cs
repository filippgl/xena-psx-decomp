using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Short rotational afterimages expand from the rim in the disc's LOCAL
    // plane. No world-space ribbon or copies of the projectile along its path.
    public sealed class PrototypeSpinHalo : MonoBehaviour, IXenaVisualModeTarget
    {
        private Mesh mesh;
        private MeshRenderer display;
        private float age;
        private bool remaster = true;
        public int VertexCount => mesh == null ? 0 : mesh.vertexCount;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> indices = new List<int>();
        public void Configure(Material material)
        {
            mesh = new Mesh { name = "ChakramRotationalAfterimages" }; mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            display = gameObject.AddComponent<MeshRenderer>(); display.sharedMaterial = material;
            display.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; display.receiveShadows = false;
        }
        public void ApplyVisualMode(XenaVisualMode mode)
        { remaster = mode == XenaVisualMode.Remaster; if (display != null) display.enabled = remaster && VertexCount > 0; }
        public void Clear() { age = 0; if (mesh != null) mesh.Clear(); if (display != null) display.enabled = false; }
        public void Tick(float dt)
        {
            age += dt; vertices.Clear(); colors.Clear(); indices.Clear();
            for (int ring = 0; ring < 4; ring++)
            {
                float phase = Mathf.Repeat(age + ring * 0.04f, 0.16f) / 0.16f;
                float radius = 0.148f + phase * 0.055f, width = 0.008f * (1 - phase);
                for (int arc = 0; arc < 6; arc++)
                for (int segment = 0; segment < 8; segment++)
                {
                    int start = vertices.Count;
                    for (int end = 0; end < 2; end++)
                    {
                        float angle = (arc * 60 + (segment + end) * 4 - ring * 12) * Mathf.Deg2Rad;
                        Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                        vertices.Add(radial * (radius - width) + Vector3.up * 0.006f);
                        vertices.Add(radial * (radius + width) + Vector3.up * 0.006f);
                        float alpha = (1 - phase) * 0.3f * (segment + end) / 8;
                        colors.Add(new Color(1, 0.78f, 0.36f, alpha)); colors.Add(new Color(1, 0.9f, 0.65f, 0));
                    }
                    indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
                    indices.Add(start + 1); indices.Add(start + 2); indices.Add(start + 3);
                }
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
            display.enabled = remaster;
        }
        private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
