using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster replacement for the original additive sun-beam polygon @sunbm (level object 49,
    // page-24, PS1 blend 1) by the waterfall: the beam's axis and end radii are fitted to the
    // original vertices (principal axis), then an open 24-sided frustum with the
    // Xena/RemasterLightShaft shader and slow dust motes fill the same volume. Original mode
    // shows the original polygon and hides the replacement. Presentation only.
    public sealed class PrototypeLightShafts : MonoBehaviour, IXenaVisualModeTarget
    {
        private readonly List<(Renderer original, GameObject shaft)> beams = new List<(Renderer, GameObject)>();
        public int Shafts => beams.Count;
        public bool RemasterShown { get; private set; }

        private void Start()
        {
            var material = Resources.Load<Material>("Remaster/LightShaft");
            var mist = Resources.Load<Material>("Remaster/Mist");
            if (material == null) return;
            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null || !filter.name.Contains("@sunbm")) continue;
                if (!Fit(filter, out Vector3 top, out Vector3 bottom, out float rTop, out float rBottom)) continue;
                var shaft = new GameObject("RemasterLightShaft");
                shaft.AddComponent<MeshFilter>().sharedMesh = Frustum(top, bottom, rTop, rBottom);
                shaft.AddComponent<MeshRenderer>().sharedMaterial = material;
                if (mist != null) Dust(shaft.transform, top, bottom, Mathf.Max(rTop, rBottom), mist);
                beams.Add((filter.GetComponent<Renderer>(), shaft));
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }

        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            foreach (var (original, shaft) in beams)
            {
                if (original != null) original.enabled = !RemasterShown;
                if (shaft != null) shaft.SetActive(RemasterShown);
            }
        }

        // Principal axis of the world-space vertices (power iteration), end radii from the
        // vertices nearest to each end; "top" is the higher end (sun side).
        private static bool Fit(MeshFilter filter, out Vector3 top, out Vector3 bottom, out float rTop, out float rBottom)
        {
            var local = filter.sharedMesh.vertices; top = bottom = Vector3.zero; rTop = rBottom = 0;
            if (local.Length < 4) return false;
            var p = new Vector3[local.Length]; Vector3 mean = Vector3.zero;
            for (int i = 0; i < p.Length; i++) { p[i] = filter.transform.TransformPoint(local[i]); mean += p[i]; }
            mean /= p.Length;
            Vector3 axis = Vector3.down;
            for (int it = 0; it < 32; it++)
            {
                Vector3 next = Vector3.zero;
                foreach (var v in p) { Vector3 d = v - mean; next += d * Vector3.Dot(d, axis); }
                if (next.sqrMagnitude < 1e-12f) break; axis = next.normalized;
            }
            float tMin = float.MaxValue, tMax = float.MinValue;
            foreach (var v in p) { float t = Vector3.Dot(v - mean, axis); tMin = Mathf.Min(tMin, t); tMax = Mathf.Max(tMax, t); }
            float span = tMax - tMin; if (span < 0.1f) return false;
            float r0 = 0, r1 = 0;
            foreach (var v in p)
            {
                float t = Vector3.Dot(v - mean, axis), r = Vector3.ProjectOnPlane(v - mean, axis).magnitude;
                if (t < tMin + span * 0.25f) r0 = Mathf.Max(r0, r); if (t > tMax - span * 0.25f) r1 = Mathf.Max(r1, r);
            }
            Vector3 a = mean + axis * tMin, b = mean + axis * tMax;
            if (a.y >= b.y) { top = a; bottom = b; rTop = Mathf.Max(0.15f, r0); rBottom = Mathf.Max(0.15f, r1); }
            else { top = b; bottom = a; rTop = Mathf.Max(0.15f, r1); rBottom = Mathf.Max(0.15f, r0); }
            return true;
        }

        private static Mesh Frustum(Vector3 top, Vector3 bottom, float rTop, float rBottom)
        {
            const int sides = 24, rings = 8;
            Vector3 axis = (bottom - top).normalized, side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, other = Vector3.Cross(axis, side);
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings, radius = Mathf.Lerp(rTop, rBottom, t); Vector3 centre = Vector3.Lerp(top, bottom, t);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * 2 * Mathf.PI / sides; Vector3 n = side * Mathf.Cos(a) + other * Mathf.Sin(a);
                    vertices.Add(centre + n * radius); normals.Add(n); uvs.Add(new Vector2((float)s / sides, t));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    int i = r * (sides + 1) + s, j = i + sides + 1;
                    triangles.AddRange(new[] { i, j, i + 1, i + 1, j, j + 1 });
                }
            var mesh = new Mesh { name = "RemasterLightShaft" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        // Slow, faint dust motes inside the shaft volume.
        private static void Dust(Transform parent, Vector3 top, Vector3 bottom, float radius, Material mist)
        {
            var go = new GameObject("ShaftDust"); go.transform.SetParent(parent, false);
            go.transform.position = (top + bottom) / 2; go.transform.rotation = Quaternion.FromToRotation(Vector3.up, top - bottom);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(6, 10);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f); main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new Color(1, 0.93f, 0.75f, 0.55f); main.maxParticles = 160; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission; emission.rateOverTime = 18;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.ConeVolume; shape.angle = 0; shape.radius = radius * 0.8f; shape.length = Vector3.Distance(top, bottom);
            shape.position = new Vector3(0, -Vector3.Distance(top, bottom) / 2, 0); shape.rotation = new Vector3(-90, 0, 0);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.05f; noise.frequency = 0.3f;
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mist;
            ps.Play();
        }
    }
}
