using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Original level-01 water motion (docs/xena-water-animation.md; all VERIFIED against the native port):
    // - CLUT cycling of the falling-water textures NiagraL (terrain page-9) and rivlette (@rivlet2):
    //   palette entries 128..178 rotate by one every game step (51-step period); the texels are rewritten
    //   from tools/export_water_cycle.py data (indices + CLUT, colours c*255/31; rivlette texel 0 stays
    //   transparent; the STP mask is not rotated: APPROXIMATED);
    // - @_stream vertex wave: phase += 0x200 per step, y = trunc(sin(phase + ((x & 7) << 13)) / 256)
    //   units (+-64, 128-step period), x = vertex x in object units; drawn interpolated between steps;
    // - splash emitters (LEVEL.BIN effect table "splash"): 15 streaks every 2nd step, life 10..24 steps,
    //   speed 5..84 units/step mostly upwards, gravity +7 units/step^2, spawn box +-250, colours
    //   (80,190,220) -> (30,150,200).
    // Original mode only; Remaster has its own water/falls (the stream mesh is restored there).
    public sealed class PrototypeWaterOriginal : MonoBehaviour, IXenaVisualModeTarget
    {
        [System.Serializable] private sealed class Cycled { public string name; public int[] uvOrigin; public int width, height; public int[] indices, clut; }
        [System.Serializable] private sealed class CycleFile { public int first, last; public Cycled[] textures; }
        [System.Serializable] private sealed class Placement { public string name; public int[] position; }
        [System.Serializable] private sealed class Effects { public Placement[] placements; }
        private sealed class Target { public Cycled data; public Texture2D page; public Color32[] pixels; }
        private readonly List<Target> targets = new List<Target>();
        private CycleFile cycle; private int offset, steps, phase = 16384;
        private MeshFilter stream; private Mesh streamOriginal, streamWave; private Vector3[] baseVertices, previous, current, drawn; private float baseY;
        private readonly List<GameObject> splashes = new List<GameObject>();
        private static readonly short[] Sine = BuildSine();
        public bool OriginalShown { get; private set; }
        public int CycleOffset => offset;
        public int CycledTextures => targets.Count;
        public int WaveVertices => baseVertices != null ? baseVertices.Length : 0;
        public int Splashes => splashes.Count;

        private static short[] BuildSine()
        {
            // func_800AD94C table (258 entries, 16384 = 1.0) is a quarter-wave linear table; a float sine
            // with the same scale matches it to within 1 unit after /256.
            var t = new short[4097]; for (int i = 0; i <= 4096; i++) t[i] = (short)Mathf.RoundToInt(Mathf.Sin(i / 4096f * 2 * Mathf.PI) * 16384); return t;
        }
        private static int Sin(int p) { p &= 0xFFFF; return Sine[p >> 4]; }

        private void Start()
        {
            var json = Resources.Load<TextAsset>("Effects/water_cycle");
            if (json != null) cycle = JsonUtility.FromJson<CycleFile>(json.text);
            foreach (var f in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)) if (f.sharedMesh != null && f.sharedMesh.name == "level-01-object-14") stream = f;
            if (stream != null)
            {
                streamOriginal = stream.sharedMesh; streamWave = Instantiate(streamOriginal); streamWave.name = "StreamWave"; streamWave.MarkDynamic();
                baseVertices = streamOriginal.vertices; previous = (Vector3[])baseVertices.Clone(); current = (Vector3[])baseVertices.Clone(); drawn = (Vector3[])baseVertices.Clone();
                baseY = baseVertices.Length > 0 ? baseVertices[0].y : 0; Wave();
            }
            var fx = Resources.Load<TextAsset>("Effects/gate_fire"); var mist = Resources.Load<Material>("Remaster/Mist");
            if (fx != null && mist != null)
                foreach (var p in JsonUtility.FromJson<Effects>(fx.text).placements) if (p.name == "splash") splashes.Add(Splash(PrototypeFire.Unity(p.position), mist));
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
        private void FindTargets()
        {
            if (cycle == null || targets.Count > 0) return;
            foreach (var data in cycle.textures)
                foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || !(m.mainTexture is Texture2D page) || !page.isReadable || targets.Exists(t => t.page == page)) continue;
                        bool match = data.name == "NiagraL" ? m.name.StartsWith("page-9_") && f.sharedMesh.name == "level-01-object-1"
                            : f.sharedMesh.name == "level-01-object-40";
                        if (match) targets.Add(new Target { data = data, page = page, pixels = page.GetPixels32() });
                    }
                }
        }
        private void FixedUpdate()
        {
            if (++steps % Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime)) != 0) return;
            offset = (offset + 1) % (cycle != null ? cycle.last - cycle.first + 1 : 51);
            phase += 0x200;
            if (!OriginalShown) return;
            Recolour(); if (stream != null) Wave();
        }
        private void Recolour()
        {
            int first = cycle.first, ring = cycle.last - cycle.first + 1;
            foreach (var t in targets)
            {
                var d = t.data; int w = t.page.width, h = t.page.height;
                for (int y = 0; y < d.height; y++)
                    for (int x = 0; x < d.width; x++)
                    {
                        int index = d.indices[y * d.width + x];
                        // After k rotations entry i shows the colour that was in entry i - k.
                        int src = index >= first && index < first + ring ? first + ((index - first - offset) % ring + ring) % ring : index;
                        int c = d.clut[src]; if (index < first || index >= first + ring) continue;
                        byte a = (byte)(c == 0 ? 0 : 255);
                        int px = d.uvOrigin[0] + x, py = h - 1 - (d.uvOrigin[1] + y);
                        if (px < 0 || px >= w || py < 0) continue;
                        t.pixels[py * w + px] = new Color32((byte)((c & 31) * 255 / 31), (byte)((c >> 5 & 31) * 255 / 31), (byte)((c >> 10 & 31) * 255 / 31), a);
                    }
                t.page.SetPixels32(t.pixels); t.page.Apply(false);
            }
        }
        private void Wave()
        {
            System.Array.Copy(current, previous, current.Length);
            for (int i = 0; i < baseVertices.Length; i++)
            {
                int x = Mathf.RoundToInt(baseVertices[i].x * 400);
                int w = Sin(phase + ((x & 7) << 13)) / 256; // units, PS1 y down
                current[i] = new Vector3(baseVertices[i].x, baseY - w / 400f, baseVertices[i].z);
            }
        }
        private void LateUpdate()
        {
            if (!OriginalShown || stream == null) return;
            // Render-only interpolation between the last two steps (ghosting rule).
            int perStep = Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime));
            float a = Mathf.Clamp01(((steps % perStep) * Time.fixedDeltaTime + Mathf.Clamp(Time.time - Time.fixedTime, 0, Time.fixedDeltaTime)) * 30);
            for (int i = 0; i < drawn.Length; i++) drawn[i] = Vector3.LerpUnclamped(previous[i], current[i], a);
            streamWave.vertices = drawn; streamWave.RecalculateBounds();
        }
        public float SampleWaveY(int i) => drawn != null && i < drawn.Length ? drawn[i].y : 0;
        // The integer wave can hold a value for a step near its peaks: only moving steps count for continuity.
        public bool WaveMoving(int i) => current != null && i < current.Length && Mathf.Abs(current[i].y - previous[i].y) > 1e-6f;

        private static GameObject Splash(Vector3 at, Material mist)
        {
            var go = new GameObject("OriginalSplash"); go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10 / 30f, 24 / 30f); main.startSpeed = new ParticleSystem.MinMaxCurve(5 * 30 / 400f, 84 * 30 / 400f);
            main.gravityModifier = 7 * 900 / 400f / 9.81f; main.maxParticles = 30; main.startSize = 0.04f; main.startColor = new Color(80 / 255f, 190 / 255f, 220 / 255f);
            var em = ps.emission; em.rateOverTime = 15 * 15;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(1.25f, 0.05f, 1.25f);
            sh.randomDirectionAmount = 0.2f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(80 / 255f, 190 / 255f, 220 / 255f), 0), new GradientColorKey(new Color(30 / 255f, 150 / 255f, 200 / 255f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }); col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mist; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 1f / 30; r.lengthScale = 1;
            ps.Play(); return go;
        }

        public void ApplyVisualMode(XenaVisualMode mode)
        {
            OriginalShown = mode == XenaVisualMode.Original;
            if (OriginalShown) FindTargets();
            if (stream != null) stream.sharedMesh = OriginalShown ? streamWave : streamOriginal;
            foreach (var s in splashes) if (s != null) s.SetActive(OriginalShown);
        }
    }
}
