using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Level-01 ambient creatures (docs/xena-ambient-audit.md section 2; models from tools/install_creatures.py,
    // placements from Level01Scripts actors). Logic runs on the 30/s game step in PSX units (y down), drawn
    // with render-only interpolation between the last two steps (ghosting rule).
    // Butterfly (0x80019FE0, VERIFIED unless noted): Xena >= 5001 away -> teleport to Xena +-1000 on one axis,
    // rand(-1000,1000) on the other, 20 above the ground. Else a timer: at 0 -> mode rand&1, timer
    // rand(3,30), speed +-1 clamped 5..10; mode 0 turns rand(-512,512) then flies straight. Blocked -> turn
    // 180 deg and rest (speed 0) rand(20,60) steps. Height target: < 1500 from Xena -> rand(100,200) above her
    // head; > 3000 -> 20 above the ground; between -> around Xena's mid height (wobble APPROXIMATED); kept
    // >= 200 above the ground when not landing; vertical speed 1..5 down, 1..10 up.
    // Frog (0x8001B12C): sits (mesh 0 hidden) until Xena < 1000 on a step with frame & 15 != 0, then jumps:
    // v = 6500, y -= v/100, v -= 500, 40 units/step along its yaw, meshes 0/1 swapped; below the ground ->
    // landing sound 0x3E + rand(0..4) (note 58, vol 63) and despawn. Its croak 0x10E6 is silent in game.
    // Larks (docs/xena-lark-generator.md, VERIFIED on native RAM): each g_lark generator runs once per 16
    // steps ((index & 15) == (step & 15)) and releases p1 larks, never respawning, when Xena is within its
    // radius (3000 + 100 p2) and no other actor is within 350 of it; the lark appears at the generator
    // position and yaw, perched (rest pose, wings frozen), lives 210 steps (also while perched), and when
    // Xena comes within 2000 flies off: velocity (trunc(-rsin/150), climb -9 (1/3) or -13 (2/3), trunc(-rcos/150))
    // per step, lifetime reset to 210, chirp 0x4B/0x4C 1/256 per step in flight.
    // Wings: SMA slot-0 vertex frames (Resources/Creatures/creature_vertex.json) loop forward one per step
    // (butterflies every step, every second step while resting; larks only in flight), drawn interpolated.
    // PSX rotY = Unity yaw (same matrix); models face local -z, so larks use the yaw without the 180 deg turn.
    public sealed class PrototypeCreatures : MonoBehaviour
    {
        [System.Serializable] private sealed class Actor { public int index; public string name, behaviour; public int[] position; public int yaw; }
        [System.Serializable] private sealed class ScriptFile { public Actor[] actors; }
        [System.Serializable] private sealed class GenActor { public int index; public string name; public Generator generator; }
        [System.Serializable] private sealed class Generator { public string spawnClass; public int remaining, spawnYaw; public int[] spawnPosition; public Trigger trigger; }
        [System.Serializable] private sealed class Trigger { public int[] centre; public int radius; }
        [System.Serializable] private sealed class GenFile { public GenActor[] actors; }
        [System.Serializable] public sealed class VMesh { public float[] v; }
        [System.Serializable] public sealed class VFrame { public VMesh[] meshes; }
        [System.Serializable] public sealed class VModel { public string name; public VFrame[] frames; }
        [System.Serializable] private sealed class VFile { public VModel[] models; }
        private sealed class LarkGenerator { public int index, remaining, radius, yaw; public Vector3 p; }
        private readonly List<LarkGenerator> generators = new List<LarkGenerator>();
        private readonly Dictionary<string, VModel> wings = new Dictionary<string, VModel>();
        public sealed class Creature
        {
            public string kind; public Transform root, body; public Vector3 p, previous; public int yaw, previousYaw, speed = 5, mode = 1, timer, vy, target;
            public bool gone; public Transform mesh0, mesh1;
            public int life, wing, previousWing, wingClock; public VModel frames; public Mesh[] meshes; public Vector3[][] rest; public bool flipped = true;
        }
        public int LarksSpawned { get; private set; }
        public int LarkChirps { get; private set; }
        public readonly List<Creature> Creatures = new List<Creature>();
        private PrototypeMotor motor; private System.Random random = new System.Random(3); private int steps, ticks;
        public int Teleports { get; private set; }
        public int FrogJumps { get; private set; }

        private void Start()
        {
            motor = GetComponent<PrototypeMotor>();
            var source = Resources.Load<TextAsset>("Level01Scripts"); if (source == null) return;
            var vertex = Resources.Load<TextAsset>("Creatures/creature_vertex");
            if (vertex != null) foreach (var m in JsonUtility.FromJson<VFile>(vertex.text).models) wings[m.name] = m;
            foreach (var a in JsonUtility.FromJson<ScriptFile>(source.text).actors)
            {
                string model = a.name == "bfly1" ? "Bfly1" : a.name == "bfly2" ? "Bfly2" : a.name == "frog" ? "Frog" : null;
                if (model == null || a.position == null) continue;
                var c = Spawn(model, a.name + "_" + a.index, new Vector3(a.position[0], a.position[1], a.position[2]), a.yaw & 4095, true);
                if (c == null) continue;
                c.kind = model == "Frog" ? "frog" : "bfly";
                if (c.kind == "frog") { c.mode = 0; if (c.mesh0 != null) c.mesh0.gameObject.SetActive(false); }
                if (c.kind == "bfly") c.wing = 1; // live butterflies animate from the start (RAM +0xE6 cycling)
            }
            foreach (var g in JsonUtility.FromJson<GenFile>(source.text).actors)
                if (g.generator != null && g.generator.spawnClass == "lark" && g.generator.spawnPosition != null)
                    generators.Add(new LarkGenerator { index = g.index, remaining = g.generator.remaining, radius = g.generator.trigger != null ? g.generator.trigger.radius : 3000,
                        yaw = g.generator.spawnYaw & 4095, p = new Vector3(g.generator.spawnPosition[0], g.generator.spawnPosition[1], g.generator.spawnPosition[2]) });
        }
        private Creature Spawn(string model, string name, Vector3 p, int yaw, bool flip)
        {
            var prefab = Resources.Load<GameObject>("Creatures/" + model + "/" + model); if (prefab == null) return null;
            var root = new GameObject(name).transform;
            var body = Instantiate(prefab, root, false).transform; if (flip) body.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var preview in body.GetComponentsInChildren<XenaAssetPreview>(true)) preview.enabled = false; // no preview GUI / clock
            var c = new Creature { root = root, body = body, p = p, yaw = yaw, flipped = flip };
            c.mesh0 = Find(body, "Geometry_0"); c.mesh1 = Find(body, "Geometry_1");
            if (wings.TryGetValue(model, out var frames))
            {
                c.frames = frames; int n = frames.frames[0].meshes.Length; c.meshes = new Mesh[n]; c.rest = new Vector3[n][];
                for (int i = 0; i < n; i++)
                {
                    var t = Find(body, "Geometry_" + i); var filter = t != null ? t.GetComponent<MeshFilter>() : null; if (filter == null) continue;
                    c.meshes[i] = Instantiate(filter.sharedMesh); c.meshes[i].MarkDynamic(); filter.sharedMesh = c.meshes[i]; c.rest[i] = c.meshes[i].vertices;
                }
            }
            c.previous = c.p; c.previousYaw = c.yaw; Creatures.Add(c); Draw(c, 1);
            return c;
        }
        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t) { var f = Find(child, name); if (f != null) return f; }
            return null;
        }
        private static int Dist(Vector3 a, Vector3 b)
        {
            int dx = Mathf.Abs(Mathf.RoundToInt(a.x - b.x)), dz = Mathf.Abs(Mathf.RoundToInt(a.z - b.z));
            return Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8;
        }
        private int Rand(int a, int b) => random.Next(a, b + 1);
        public static Vector3 ToUnity(Vector3 p) => new Vector3(p.x, -p.y, p.z) / 400f;
        // PSX ground y below p (y down); int.MaxValue when there is none.
        private float Ground(Vector3 p)
        {
            float g = motor != null ? motor.GroundHeight(ToUnity(p) + Vector3.up * 0.5f) : float.NegativeInfinity;
            return float.IsNegativeInfinity(g) ? float.MaxValue : -g * 400;
        }

        private void FixedUpdate()
        {
            if (++ticks % Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime)) != 0) return;
            steps++;
            Vector3 xena = motor != null ? new Vector3(motor.transform.position.x, -motor.transform.position.y, motor.transform.position.z) * 400 : Vector3.zero;
            foreach (var gen in generators) LarkGenerate(gen, xena);
            for (int i = 0; i < Creatures.Count; i++)
            {
                var c = Creatures[i];
                if (c.gone) continue;
                c.previous = c.p; c.previousYaw = c.yaw; c.previousWing = c.wing;
                if (c.kind == "frog") Frog(c, xena); else if (c.kind == "lark") Lark(c, xena); else Butterfly(c, xena);
                // Wings: +0xE0 = 2 (every step), 4 (every second step: butterfly rest), 0 frozen (perched lark).
                int rate = c.kind == "lark" ? (c.mode == 3 ? 1 : 0) : c.mode == 2 ? 2 : 1;
                if (c.frames != null && rate > 0 && ++c.wingClock >= rate) { c.wingClock = 0; c.wing = c.wing % c.frames.frames.Length + 1; }
            }
        }
        private void LarkGenerate(LarkGenerator g, Vector3 xena)
        {
            if (g.remaining <= 0 || (g.index & 15) != (steps & 15) || Dist(g.p, xena) >= g.radius) return;
            if (Dist(g.p, xena) < 350) return;
            foreach (var c in Creatures) if (!c.gone && Dist(g.p, c.p) < 350) return;
            var bandits = GetComponent<PrototypeBandits>();
            if (bandits != null) foreach (var b in bandits.Brains) if (b != null && !b.Removed && Dist(g.p, new Vector3(b.transform.position.x, -b.transform.position.y, b.transform.position.z) * 400) < 350) return;
            var lark = Spawn("Lark", "lark_" + g.index + "_" + g.remaining, g.p, g.yaw, false); if (lark == null) return;
            lark.kind = "lark"; lark.mode = 0; lark.life = 210; g.remaining--; LarksSpawned++;
        }
        private void Lark(Creature c, Vector3 xena)
        {
            if (--c.life <= 0) { c.gone = true; c.root.gameObject.SetActive(false); return; }
            if (c.mode == 0)
            {
                if (Dist(c.p, xena) < 2000) { c.mode = 3; c.life = 210; c.wingClock = -1; } // first flight frame on the next step
                return;
            }
            float a = c.yaw / 4096f * 2 * Mathf.PI;
            int rsin = Mathf.RoundToInt(Mathf.Sin(a) * 4096), rcos = Mathf.RoundToInt(Mathf.Cos(a) * 4096);
            c.p += new Vector3(-rsin / 150, Rand(0, 2) == 0 ? -9 : -13, -rcos / 150);
            if (random.Next(256) == 0) { PrototypeSfx.PlayAt(0x4B + (random.Next() & 1), ToUnity(c.p)); LarkChirps++; }
        }
        private void Frog(Creature c, Vector3 xena)
        {
            if (c.mode == 0)
            {
                if ((steps & 15) != 0 && Dist(c.p, xena) < 1000)
                {
                    c.mode = 1; c.vy = 6500; FrogJumps++;
                    if (c.mesh0 != null) c.mesh0.gameObject.SetActive(true); if (c.mesh1 != null) c.mesh1.gameObject.SetActive(false);
                }
                return;
            }
            float a = c.yaw / 4096f * 2 * Mathf.PI;
            c.p += new Vector3(40 * Mathf.Sin(a), -c.vy / 100f, 40 * Mathf.Cos(a)); c.vy -= 500;
            if (c.vy < 0 && Ground(c.p) < c.p.y)
            {
                PrototypeSfx.PlayAt(0x3E + Rand(0, 4), ToUnity(c.p), 58, 63);
                c.gone = true; c.root.gameObject.SetActive(false);
            }
        }
        private void Butterfly(Creature c, Vector3 xena)
        {
            int d = Dist(c.p, xena);
            if (d >= 5001)
            {
                bool onX = (random.Next() & 1) == 0; float near = (random.Next() & 1) == 0 ? -1000 : 1000, other = Rand(-1000, 1000);
                c.p = xena + (onX ? new Vector3(near, 0, other) : new Vector3(other, 0, near));
                float g = Ground(c.p); c.p.y = (g == float.MaxValue ? xena.y : g) - 20;
                c.previous = c.p; Teleports++; return; // a teleport snaps
            }
            if (--c.timer <= 0)
            {
                if (c.mode == 2) { c.mode = 1; c.speed = 5; }
                else
                {
                    c.mode = random.Next() & 1; c.timer = Rand(3, 30); c.speed = Mathf.Clamp(c.speed + ((random.Next() & 1) == 0 ? -1 : 1), 5, 10);
                    if (c.mode == 0) { c.yaw = (c.yaw + Rand(-512, 512)) & 4095; c.mode = 1; }
                }
                c.target = Rand(100, 200);
            }
            if (c.mode == 1)
            {
                float a = c.yaw / 4096f * 2 * Mathf.PI;
                var next = c.p + new Vector3(c.speed * Mathf.Sin(a), 0, c.speed * Mathf.Cos(a));
                float g = Ground(next);
                if (g == float.MaxValue || g < c.p.y) { c.yaw = (c.yaw + 2048) & 4095; c.mode = 2; c.speed = 0; c.timer = Rand(20, 60); }
                else c.p = next;
            }
            float ground = Ground(c.p); if (ground == float.MaxValue) ground = c.p.y + 20;
            float want = d < 1500 ? xena.y - 750 - c.target : d > 3000 ? ground - 20 : xena.y - 375 + 60 * Mathf.Sin(steps * 0.2f);
            if (d <= 3000) want = Mathf.Min(want, ground - 200);
            float dy = want - c.p.y;
            c.vy = dy > 0 ? Mathf.RoundToInt(Mathf.Clamp(dy, 1, 5)) : dy < 0 ? Mathf.RoundToInt(Mathf.Clamp(dy, -10, -1)) : 0;
            c.p.y += Mathf.Abs(dy) < Mathf.Abs(c.vy) ? dy : c.vy;
        }

        private void LateUpdate()
        {
            int perStep = Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime));
            float alpha = Mathf.Clamp01(((ticks % perStep) * Time.fixedDeltaTime + Mathf.Clamp(Time.time - Time.fixedTime, 0, Time.fixedDeltaTime)) * 30);
            foreach (var c in Creatures) if (!c.gone) { Draw(c, alpha); Wings(c, alpha); }
        }
        // Displayed frame = frames[wing - 1], wing 0 = rest mesh; render-only blend between the last two steps.
        private static void Wings(Creature c, float alpha)
        {
            if (c.frames == null || c.meshes == null) return;
            for (int m = 0; m < c.meshes.Length; m++)
            {
                if (c.meshes[m] == null) continue;
                var from = Pose(c, m, c.previousWing); var to = Pose(c, m, c.wing);
                var drawn = new Vector3[to.Length];
                for (int i = 0; i < drawn.Length; i++) drawn[i] = Vector3.LerpUnclamped(from[i], to[i], c.wing == c.previousWing % c.frames.frames.Length + 1 || c.wing == c.previousWing ? alpha : 1);
                c.meshes[m].vertices = drawn;
            }
        }
        private static Vector3[] Pose(Creature c, int m, int wing)
        {
            if (wing <= 0) return c.rest[m];
            var v = c.frames.frames[wing - 1].meshes[m].v; var r = new Vector3[v.Length / 3];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]);
            return r;
        }
        public bool WingsMoving(int i) => Creatures[i].wing != Creatures[i].previousWing;
        public Vector3[] DrawnWing(int i) => Creatures[i].meshes != null && Creatures[i].meshes[0] != null ? Creatures[i].meshes[0].vertices : null;
        private static void Draw(Creature c, float alpha)
        {
            c.root.position = Vector3.Lerp(ToUnity(c.previous), ToUnity(c.p), alpha);
            float from = c.previousYaw * 360f / 4096, to = c.yaw * 360f / 4096;
            c.root.rotation = Quaternion.Euler(0, Mathf.LerpAngle(from, to, Mathf.Abs(Mathf.DeltaAngle(from, to)) > 90 ? 1 : alpha), 0);
        }
        public Vector3 DrawnPosition(int i) => Creatures[i].root.position;
        public bool Moving(int i) => Creatures[i].p != Creatures[i].previous;
    }
}
