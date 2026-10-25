using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xena.Assets
{
    // Original combat hit effects (docs/xena-hit-effects.md, tools/export_hit_effects.mjs;
    // formulas VERIFIED against native-port RAM dumps). Burst pool func_80028650 (12 slots):
    // flash star (kind 0), shock ring (kind 1, d >= 26), grey spark discs (kind 2); screen-space
    // additive gouraud polygons in 320x240 pixels scaled by w = 256 - SZ/32. Blood emitter
    // func_800B2F84: 40 slots, 2..9 particles every 2nd step for 8 (Xena) / 10 (bandit) /
    // 20 (bandit kill) steps, gravity 3 units/step^2; Original draws one-step red->black lines.
    // Remaster keeps the flash/ring/discs (vector shapes) and draws the blood as soft droplets.
    // Game step = 30/s; movers are drawn interpolated between steps (ghosting rule).
    public sealed class PrototypeHitEffects : MonoBehaviour
    {
        public enum Type { Hit = 0, Block = 1, Special = 2 }
        private static readonly Color32[,] Colors = { { new Color32(32, 0, 0, 255), new Color32(255, 32, 0, 255), new Color32(0, 0, 0, 255) },
            { new Color32(0, 0, 32, 255), new Color32(0, 32, 255, 255), new Color32(0, 0, 0, 255) },
            { new Color32(32, 32, 0, 255), new Color32(150, 150, 32, 255), new Color32(0, 0, 0, 255) } };
        private sealed class Burst { public int kind, size, life, tilt, type; public Vector3 pos, prev, vel; }
        private sealed class Drop { public Vector3 pos, prev, vel; public int life; }
        private sealed class Emitter { public Transform follow; public Vector3 offset; public int steps, delay; public Vector3 v; public Color32 head; public readonly List<Drop> drops = new List<Drop>(); }
        private readonly Burst[] pool = new Burst[12];
        private readonly Dictionary<Transform, Emitter> emitters = new Dictionary<Transform, Emitter>();
        private readonly System.Random random = new System.Random(7);
        private Material additive, opaque; private ParticleSystem droplets;
        // Screen-space shapes become a mesh on a plane just past the near clip (overlay material,
        // ZTest Always), submitted per camera before culling.
        private Mesh burstMesh, bloodMesh; private Camera view; private float planeDepth;
        private readonly List<Vector3> vertices = new List<Vector3>(); private readonly List<Color> colors = new List<Color>(); private readonly List<int> indices = new List<int>();
        public int LastDrawnTriangles { get; private set; }
        // Drawn (interpolated) position of the first moving spark disc, for the continuity check.
        // Only discs that already took a step: before its first step a new disc is rightly drawn
        // at its birth point (rendering trails the simulation by one step).
        public bool MovingDisc(out Vector3 drawn)
        {
            foreach (var b in pool) if (b != null && b.life > 0 && b.kind == 2 && b.vel.sqrMagnitude > 1e-8f && b.prev != b.pos) { drawn = Vector3.Lerp(b.prev, b.pos, Alpha()); return true; }
            drawn = default; return false;
        }
        public string DebugClock => string.Format(System.Globalization.CultureInfo.InvariantCulture, "a={0:F3} tick={1} ph={2:F4} dt={3:F4}", Alpha(), ticks, Time.time - Time.fixedTime, Time.deltaTime);
        private int frame, ticks;
        public int ActiveBursts { get { int n = 0; foreach (var b in pool) if (b != null && b.life > 0) n++; return n; } }
        public int ActiveDrops { get { int n = 0; foreach (var e in emitters.Values) n += e.drops.Count; return n; } }
        public int Spawned { get; private set; }
        public int BloodEmitters { get; private set; }
        public Type LastType { get; private set; }
        public int LastFlashLife { get; private set; }
        public int LastDiscs { get; private set; }
        public bool LastRing { get; private set; }
        public int RemasterDroplets => droplets != null ? droplets.particleCount : 0;
        // Test log: (type, d) of every spark call and the victims that started bleeding.
        public readonly List<(Type type, int d, int life, bool ring, int discs)> SparkLog = new List<(Type, int, int, bool, int)>();
        public readonly List<Transform> BleedLog = new List<Transform>();

        private int Rand(int lo, int hi) => random.Next(lo, hi + 1);
        private float Rand01() => (float)random.NextDouble();

        private void Awake()
        {
            var a = Resources.Load<Material>("Remaster/HitAdditive"); var o = Resources.Load<Material>("Remaster/HitOpaque");
            additive = a; opaque = o;
            RenderPipelineManager.beginCameraRendering += Draw;
            burstMesh = new Mesh { name = "HitBursts" }; burstMesh.MarkDynamic(); bloodMesh = new Mesh { name = "HitBlood" }; bloodMesh.MarkDynamic();
        }
        private void OnDestroy() { RenderPipelineManager.beginCameraRendering -= Draw; }

        // func_800291A0(point, d, type): one flash, a ring when d >= 26, clamp(d/4-1, 0, 6) discs.
        public void Spark(Vector3 point, int d, Type type)
        {
            int life0 = Mathf.Min(d / 4 + 4, 12);
            Add(0, life0, point, Vector3.zero, type);
            LastRing = d >= 26; if (LastRing) Add(1, life0 * 5 / 8, point, Vector3.zero, type);
            int discs = Mathf.Clamp(d / 4 - 1, 0, 6);
            for (int i = 0; i < discs; i++) Add(2, Rand(10, 20), point, new Vector3(Rand(-18, 18), -Rand(-18, 18), Rand(-18, 18)) / 400f, type);
            Spawned++; LastType = type; LastFlashLife = life0; LastDiscs = discs; SparkLog.Add((type, d, life0, LastRing, discs));
        }
        private void Add(int kind, int life, Vector3 pos, Vector3 vel, Type type)
        {
            int slot = System.Array.FindIndex(pool, b => b == null || b.life <= 0);
            if (slot < 0) slot = System.Array.FindIndex(pool, b => b.kind >= 2 && b.life < 5);
            if (slot < 0) return; // all 12 busy: the effect is dropped
            int tilt = Rand(0x100, 0x2AA) * (random.Next(2) == 0 ? 1 : -1);
            pool[slot] = new Burst { kind = kind, size = life, life = life, tilt = tilt, type = (int)type, pos = pos, prev = pos, vel = vel };
        }
        // Blood emitter, one per victim; v = victim facing * 500 + up * (200 Xena / 80 hit / 170 kill).
        public void Bleed(Transform victim, Vector3 point, int steps, float up)
        {
            if (emitters.TryGetValue(victim, out var old) && old.steps > 0) return; // victim +0x104 busy
            var e = new Emitter { follow = victim, offset = point - victim.position, steps = steps, delay = 0, head = new Color32(255, 0, 0, 255) };
            Vector3 f = victim.forward; f.y = 0; f.Normalize();
            e.v = new Vector3(f.x * 500, -up, f.z * 500); // PS1 axes: y down
            if (old != null) e.drops.AddRange(old.drops);
            emitters[victim] = e; BloodEmitters++; BleedLog.Add(victim);
        }

        private void FixedUpdate()
        {
            if (++ticks % Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime)) == 0) Step();
        }
        private void Step()
        {
            frame++;
            foreach (var b in pool)
            {
                if (b == null || b.life <= 0) continue;
                b.prev = b.pos; b.life--;
                if (b.kind == 2)
                {
                    b.pos += b.vel;
                    if ((frame & 1) == 0) b.vel = new Vector3(Toward(b.vel.x), Toward(b.vel.y), Toward(b.vel.z));
                }
            }
            var finished = new List<Transform>();
            foreach (var pair in emitters)
            {
                var e = pair.Value; Vector3 origin = (e.follow != null ? e.follow.position : Vector3.zero) + e.offset;
                foreach (var p in e.drops)
                {
                    p.prev = p.pos; p.pos += p.vel; p.life--;
                    // Gravity +3 units/step^2 down while |vy| < 200 units/step.
                    if (Mathf.Abs(p.vel.y * 400) < 200) p.vel.y -= 3f / 400;
                }
                e.drops.RemoveAll(p => p.life <= 0);
                if (e.steps > 0)
                {
                    e.steps--;
                    if (e.delay-- <= 0)
                    {
                        e.delay = 1;
                        int count = 2 + (int)(Rand01() * 8);
                        for (int i = 0; i < count && e.drops.Count < 40; i++)
                        {
                            // Direction per axis = (v - 100) + rand * (v + 200), normalised (PS1 axes).
                            var d = new Vector3(e.v.x - 100 + Rand01() * (e.v.x + 200), e.v.y - 100 + Rand01() * (e.v.y + 200), e.v.z - 100 + Rand01() * (e.v.z + 200));
                            if (d.sqrMagnitude < 1) d = new Vector3(0, -1, 0);
                            d.Normalize(); float speed = 5 + (int)(Rand01() * 15);
                            Vector3 at = origin + new Vector3(Rand01() * 30 - 15, Rand01() * 30 - 15, Rand01() * 30 - 15) / 400f;
                            var drop = new Drop { pos = at, prev = at, vel = new Vector3(d.x, -d.y, d.z) * speed / 400f, life = 5 + (int)(Rand01() * 6) };
                            e.drops.Add(drop); EmitDroplet(drop);
                        }
                    }
                }
                if (e.steps <= 0 && e.drops.Count == 0) finished.Add(pair.Key);
            }
            foreach (var key in finished) emitters.Remove(key);
        }
        private static float Toward(float v) { float u = v * 400; u = u > 1 ? u - 1 : u < -1 ? u + 1 : u; return u / 400; }

        private static bool Remaster => PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster;
        // Remaster blood: soft dark-red droplets with real gravity, seeded by each original particle.
        private void EmitDroplet(Drop drop)
        {
            if (!Remaster) return;
            if (droplets == null)
            {
                var mist = Resources.Load<Material>("Remaster/Mist"); if (mist == null) return;
                var go = new GameObject("RemasterBlood"); droplets = go.AddComponent<ParticleSystem>();
                droplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = droplets.main; main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 1.0f; main.maxParticles = 600; main.startSpeed = 0;
                var emission = droplets.emission; emission.enabled = false; var shape = droplets.shape; shape.enabled = false;
                var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = mist;
                renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = 0.04f; renderer.lengthScale = 1.2f;
                var col = droplets.colorOverLifetime; col.enabled = true;
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.02f, 0.02f), 0), new GradientColorKey(new Color(0.25f, 0.0f, 0.0f), 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0, 1) });
                col.color = g;
                var size = droplets.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.4f));
                droplets.Play();
            }
            // Three droplets per original particle: a modern, denser spray with the original's cone.
            for (int i = 0; i < 3; i++)
            {
                var p = new ParticleSystem.EmitParams { position = drop.pos, velocity = drop.vel * 30 * (0.8f + 0.4f * Rand01()) + Random.insideUnitSphere * 0.25f,
                    startSize = 0.025f + 0.03f * Rand01(), startLifetime = 0.45f + 0.35f * Rand01() };
                droplets.Emit(p, 1);
            }
        }

        private float Alpha()
        {
            int perStep = Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime));
            float since = (ticks % perStep) * Time.fixedDeltaTime + Mathf.Clamp(Time.time - Time.fixedTime, 0, Time.fixedDeltaTime);
            return Mathf.Clamp01(since * 30);
        }

        private void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (camera != Camera.main || additive == null || opaque == null) return;
            float a = Alpha(); float scale = camera.pixelHeight / 240f;
            view = camera; planeDepth = camera.nearClipPlane * 1.5f; Begin();
            foreach (var b in pool)
            {
                if (b == null || b.life <= 0) continue;
                Vector3 s = camera.WorldToScreenPoint(Vector3.Lerp(b.prev, b.pos, a)); if (s.z <= 0.05f) continue;
                float w = 256 - s.z * 400 / 32; if (w <= 0 && b.kind != 1) continue;
                if (b.kind == 0) Flash(b, s, w, scale); else if (b.kind == 1) Ring(b, s, scale); else Disc(b, s, w, scale);
            }
            LastDrawnTriangles = indices.Count / 3; Submit(burstMesh, additive);
            Begin();
            if (!Remaster)
            {
                // Blood: one-step lines, red head -> black tail, opaque (drawn as 1-px-of-240 quads).
                foreach (var e in emitters.Values)
                    foreach (var p in e.drops)
                    {
                        Vector3 head = camera.WorldToScreenPoint(Vector3.Lerp(p.prev, p.pos, a)), tail = camera.WorldToScreenPoint(Vector3.Lerp(p.prev - p.vel, p.prev, a));
                        if (head.z <= 0.05f || tail.z <= 0.05f || head.z * 400 / 32 < 9) continue;
                        Vector2 dir = (Vector2)(head - tail); Vector2 n = dir.sqrMagnitude > 0.01f ? new Vector2(-dir.y, dir.x).normalized * scale * 0.5f : new Vector2(scale * 0.5f, 0);
                        Color red = ((Color)e.head).linear;
                        Vertex(head.x + n.x, head.y + n.y, red); Vertex(head.x - n.x, head.y - n.y, red); Vertex(tail.x - n.x, tail.y - n.y, Color.black);
                        Vertex(head.x + n.x, head.y + n.y, red); Vertex(tail.x - n.x, tail.y - n.y, Color.black); Vertex(tail.x + n.x, tail.y + n.y, Color.black);
                    }
            }
            Submit(bloodMesh, opaque);
        }
        private void Begin() { vertices.Clear(); colors.Clear(); indices.Clear(); }
        private void Vertex(float x, float y, Color c) { indices.Add(vertices.Count); vertices.Add(view.ScreenToWorldPoint(new Vector3(x, y, planeDepth))); colors.Add(c); }
        private void Submit(Mesh mesh, Material material)
        {
            mesh.Clear(); if (indices.Count == 0) return;
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0, false); mesh.bounds = new Bounds(view.transform.position, Vector3.one * 1000);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, view, 0, null, false, false, false);
        }
        // PS1 colours are gamma-encoded framebuffer values: convert so additive sums match in linear space.
        private static Color Scaled(Color32 c, float f) => new Color(c.r / 255f * f, c.g / 255f * f, c.b / 255f * f, 1).linear;
        private static Vector2 Polar(float angle4096, float r) { float t = angle4096 / 4096f * 2 * Mathf.PI; return new Vector2(Mathf.Sin(t) * r, Mathf.Cos(t) * r); }
        private void Flash(Burst b, Vector3 s, float w, float scale)
        {
            float f = Mathf.Min((b.life + 2) * 32, 255) / 255f;
            int r = ((int)w * ((b.life * 192 / b.size) * 7 / 32)) >> 8;
            Color tip = Scaled(Colors[b.type, 0], f), side = Scaled(Colors[b.type, 1], f), centre = Scaled(Colors[b.type, 2], f);
            for (int star = 0; star < 2; star++)
            {
                float R = (star == 0 ? r : 3 * r / 4) * scale, half = star == 0 ? 0x1D0 : 0x320, baseAngle = b.tilt + 64 * b.life + (star == 0 ? 0 : 0x199);
                for (int k = 0; k < 5; k++)
                {
                    float ang = baseAngle + k * 4096f / 5;
                    Vector2 t = Polar(ang, R), l = Polar(ang - half, R / 8), rr = Polar(ang + half, R / 8);
                    Tri(s, Vector2.zero, centre, l, side, t, tip); Tri(s, Vector2.zero, centre, t, tip, rr, side);
                }
            }
        }
        private static int Segments(float extent) => extent < 6 ? 4 : extent < 12 ? 6 : extent < 20 ? 8 : extent < 40 ? 12 : 16;
        private void Ring(Burst b, Vector3 s, float scale)
        {
            float inner = ((b.size - (b.life - 1)) * 6) & ~1, thick = (b.life * 4) | 2, f = Mathf.Min(b.life * 14, 255) / 255f;
            Ellipse(s, inner * scale, (inner + thick) * scale, b.tilt, (frame & 0xFFF) << 4, Segments(inner + thick), Scaled(Colors[b.type, 1], f), Scaled(Colors[b.type, 2], f));
        }
        private void Disc(Burst b, Vector3 s, float w, float scale)
        {
            int r = ((int)w * ((((b.life >> 1) + 4) * 256 / b.size) * 5 / 32)) >> 8;
            float g = (int)(Mathf.Min(b.life * 8 + 16, 255) * 99 / 256f) / 255f;
            Ellipse(s, 0, r * scale, b.tilt, (frame & 0x3FF) << 6, Segments(r), new Color(g, g, g, 1).linear, Color.black);
        }
        // Sheared circle: x = sin(a) r, y = cos(a + tilt) r, gouraud from inner to outer edge.
        private void Ellipse(Vector3 s, float inner, float outer, float tilt, float spin, int segments, Color ci, Color co)
        {
            Vector2 P(float a, float r) { float t = (a + spin) / 4096f * 2 * Mathf.PI, u = (a + spin + tilt) / 4096f * 2 * Mathf.PI; return new Vector2(Mathf.Sin(t) * r, Mathf.Cos(u) * r); }
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 4096f / segments, a1 = (i + 1) * 4096f / segments;
                Tri(s, P(a0, inner), ci, P(a0, outer), co, P(a1, outer), co);
                if (inner > 0) Tri(s, P(a0, inner), ci, P(a1, outer), co, P(a1, inner), ci);
            }
        }
        private void Tri(Vector3 s, Vector2 a, Color ca, Vector2 b, Color cb, Vector2 c, Color cc)
        {
            // PS1 screen y grows down; Unity screen space grows up.
            Vertex(s.x + a.x, s.y - a.y, ca); Vertex(s.x + b.x, s.y - b.y, cb); Vertex(s.x + c.x, s.y - c.y, cc);
        }
    }
}
