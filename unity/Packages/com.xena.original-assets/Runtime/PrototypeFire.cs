using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Level fires (docs/xena-gate-fire.md, tools/export_gate_fire.mjs -> Resources/Effects):
    // LEVEL.BIN effect table "fire" (22-frame FIRE.STM, 4 vblanks/frame, random start 0..8) and
    // "fire2" (10 frames, 4 vblanks) sprites: 300x300 units (0.75 m) camera-facing quads, bottom
    // centre at the record position, additive (abr 1); "smok_blk" dark smoke emitters (6 slots,
    // 2 particles every 5..9 steps, rising, 50..109 steps). Original draws these; Remaster draws
    // Unity particle fire (flames, embers, smoke, flickering light) at the same places instead.
    public sealed class PrototypeFire : MonoBehaviour, IXenaVisualModeTarget
    {
        [System.Serializable] private sealed class Placement { public int index; public string name; public int[] position; }
        [System.Serializable] private sealed class Data { public Placement[] placements; }
        private sealed class Sprite { public Transform quad; public MeshRenderer renderer; public Texture2D[] frames; public int frame; }
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly List<GameObject> original = new List<GameObject>(), remaster = new List<GameObject>();
        private Mesh quadMesh; private float clock;
        public int Fires => sprites.Count;
        public int RemasterFires { get; private set; }
        public bool RemasterShown { get; private set; }
        public static Vector3 Unity(int[] p) => new Vector3(p[0], -p[1], p[2]) / 400f;

        private void Start()
        {
            var json = Resources.Load<TextAsset>("Effects/gate_fire"); var mist = Resources.Load<Material>("Remaster/Mist");
            if (json == null || mist == null) return;
            var data = JsonUtility.FromJson<Data>(json.text);
            var fire = Load("Effects/Fire/fire_", 22); var fire2 = Load("Effects/Fire2/fire2_", 10); var smokeTile = Resources.Load<Texture2D>("Effects/Smoke/smoke3_00");
            quadMesh = Quad();
            var random = new System.Random(77);
            var root = new GameObject("LevelFires");
            foreach (var p in data.placements)
            {
                Vector3 at = Unity(p.position);
                if (p.name == "fire" || p.name == "fire2")
                {
                    var frames = p.name == "fire" ? fire : fire2; if (frames == null) continue;
                    var go = new GameObject("OriginalFire_" + p.index); go.transform.SetParent(root.transform, false); go.transform.position = at;
                    go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                    var material = Additive(mist, frames[0]);
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = material; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    sprites.Add(new Sprite { quad = go.transform, renderer = mr, frames = frames, frame = random.Next(9) }); original.Add(go);
                    remaster.Add(RemasterFire(root.transform, at, mist, p.name == "fire2" ? 0.7f : 1)); RemasterFires++;
                }
                else if (p.name == "smok_blk" || p.name == "smok_sml")
                {
                    bool dark = p.name == "smok_blk";
                    original.Add(Smoke(root.transform, at + Vector3.up * 1, mist, smokeTile, dark, false));
                    remaster.Add(Smoke(root.transform, at + Vector3.up * 1, mist, null, dark, true));
                }
            }
            ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode);
        }
        private static Texture2D[] Load(string prefix, int count)
        {
            var frames = new Texture2D[count];
            for (int i = 0; i < count; i++) { frames[i] = Resources.Load<Texture2D>(prefix + i.ToString("00")); if (frames[i] == null) return null; frames[i].filterMode = FilterMode.Point; }
            return frames;
        }
        // 0.75 m quad, origin at the bottom centre, facing -Z (turned to the camera every frame).
        private static Mesh Quad()
        {
            var m = new Mesh { name = "FireQuad" };
            m.vertices = new[] { new Vector3(-0.375f, 0, 0), new Vector3(0.375f, 0, 0), new Vector3(0.375f, 0.75f, 0), new Vector3(-0.375f, 0.75f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 }; m.RecalculateBounds(); return m;
        }
        private static Material Additive(Material template, Texture texture)
        {
            var m = new Material(template) { name = "FireAdditive" };
            m.SetTexture("_BaseMap", texture); m.SetColor("_BaseColor", Color.white);
            // Additive weighted by the texture alpha (soft particle puffs; sprite texel 0 is transparent).
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_Blend", 2); m.renderQueue = 3100;
            return m;
        }

        private void LateUpdate()
        {
            var cam = Camera.main; if (cam == null) return;
            // Frames advance on a 15 Hz clock (4 vblanks); quads are screen-aligned like the PS1 billboard.
            clock += Time.deltaTime; int steps = (int)(clock * 15); clock -= steps / 15f;
            foreach (var s in sprites)
            {
                if (!s.quad.gameObject.activeInHierarchy) continue;
                if (steps > 0) { s.frame = (s.frame + steps) % s.frames.Length; s.renderer.sharedMaterial.SetTexture("_BaseMap", s.frames[s.frame]); }
                s.quad.rotation = cam.transform.rotation;
            }
        }

        // Remaster fire: soft flame particles (additive, yellow -> orange -> red), embers and a flickering light.
        private GameObject RemasterFire(Transform parent, Vector3 at, Material mist, float scale)
        {
            var root = new GameObject("RemasterFire"); root.transform.SetParent(parent, false); root.transform.position = at;
            var flames = Emitter(root.transform, "Flames", mist, true);
            var main = flames.main; main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.5f * scale); main.maxParticles = 120; main.gravityModifier = -0.15f;
            var em = flames.emission; em.rateOverTime = 55;
            var sh = flames.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.22f * scale; sh.rotation = new Vector3(-90, 0, 0);
            var col = flames.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1, 0.95f, 0.6f), 0), new GradientColorKey(new Color(1, 0.55f, 0.12f), 0.35f), new GradientColorKey(new Color(0.75f, 0.12f, 0.02f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.55f, 0.12f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = flames.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1), new Keyframe(1, 0.2f)));
            var noise = flames.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.6f; noise.scrollSpeed = 0.8f;
            var embers = Emitter(root.transform, "Embers", mist, true);
            var em2 = embers.main; em2.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.2f); em2.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            em2.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f); em2.startColor = new Color(1, 0.6f, 0.2f, 1); em2.maxParticles = 60; em2.gravityModifier = -0.05f;
            var e2 = embers.emission; e2.rateOverTime = 9; var s2 = embers.shape; s2.shapeType = ParticleSystemShapeType.Circle; s2.radius = 0.2f * scale; s2.rotation = new Vector3(-90, 0, 0);
            var n2 = embers.noise; n2.enabled = true; n2.strength = 0.6f; n2.frequency = 1.2f;
            var light = new GameObject("FireLight").AddComponent<Light>(); light.transform.SetParent(root.transform, false); light.transform.localPosition = Vector3.up * 0.5f;
            light.type = LightType.Point; light.color = new Color(1, 0.55f, 0.2f); light.range = 5 * scale; light.intensity = 2.2f; light.shadows = LightShadows.None;
            root.AddComponent<FireFlicker>().Bind(light);
            return root;
        }
        private static ParticleSystem Emitter(Transform parent, string name, Material mist, bool additive)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = additive ? Additive(mist, mist.GetTexture("_BaseMap")) : mist;
            ps.Play();
            return ps;
        }
        // Dark (smok_blk) or light (smok_sml) smoke column.
        private static GameObject Smoke(Transform parent, Vector3 at, Material mist, Texture2D tile, bool dark, bool remaster)
        {
            var go = new GameObject(remaster ? "RemasterSmoke" : "OriginalSmoke"); go.transform.SetParent(parent, false); go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            // Original: 2 particles every 5..9 steps (~5/s at 30 steps/s), 50..109 steps, rising; Remaster denser and softer.
            main.startLifetime = remaster ? new ParticleSystem.MinMaxCurve(3.5f, 6f) : new ParticleSystem.MinMaxCurve(50 / 30f, 109 / 30f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f); main.gravityModifier = -0.04f; main.maxParticles = remaster ? 80 : 6;
            main.startSize = remaster ? new ParticleSystem.MinMaxCurve(0.8f, 1.6f) : new ParticleSystem.MinMaxCurve(0.8f, 1.0f);
            main.startColor = dark ? new Color(0.08f, 0.07f, 0.06f, remaster ? 0.45f : 0.7f) : new Color(0.8f, 0.8f, 0.8f, 0.4f);
            var em = ps.emission; em.rateOverTime = remaster ? 9 : 5;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
            if (remaster)
            {
                var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) }); col.color = g;
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.4f;
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (!remaster && tile != null) { var m = new Material(mist) { name = "OriginalSmoke" }; m.SetTexture("_BaseMap", tile); renderer.sharedMaterial = m; }
            else renderer.sharedMaterial = mist;
            ps.Play();
            return go;
        }

        public void ApplyVisualMode(XenaVisualMode mode)
        {
            RemasterShown = mode == XenaVisualMode.Remaster;
            foreach (var go in original) if (go != null) go.SetActive(!RemasterShown);
            foreach (var go in remaster) if (go != null) go.SetActive(RemasterShown);
        }
    }

    // Light flicker for Remaster fires (presentation only).
    public sealed class FireFlicker : MonoBehaviour
    {
        private Light target; private float seed;
        public void Bind(Light light) { target = light; seed = Random.value * 100; }
        private void Update()
        {
            if (target == null) return;
            float t = Time.time * 9 + seed;
            target.intensity = 1.8f + 0.5f * Mathf.PerlinNoise(t, seed) + 0.25f * Mathf.Sin(t * 2.3f);
        }
    }
}
