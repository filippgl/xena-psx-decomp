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
    public sealed class PrototypeCreatures : MonoBehaviour
    {
        [System.Serializable] private sealed class Actor { public int index; public string name, behaviour; public int[] position; public int yaw; }
        [System.Serializable] private sealed class ScriptFile { public Actor[] actors; }
        public sealed class Creature
        {
            public string kind; public Transform root, body; public Vector3 p, previous; public int yaw, previousYaw, speed = 5, mode = 1, timer, vy, target;
            public bool gone; public Transform mesh0, mesh1;
        }
        public readonly List<Creature> Creatures = new List<Creature>();
        private PrototypeMotor motor; private System.Random random = new System.Random(3); private int steps, ticks;
        public int Teleports { get; private set; }
        public int FrogJumps { get; private set; }

        private void Start()
        {
            motor = GetComponent<PrototypeMotor>();
            var source = Resources.Load<TextAsset>("Level01Scripts"); if (source == null) return;
            foreach (var a in JsonUtility.FromJson<ScriptFile>(source.text).actors)
            {
                string model = a.name == "bfly1" ? "Bfly1" : a.name == "bfly2" ? "Bfly2" : a.name == "frog" ? "Frog" : null;
                if (model == null || a.position == null) continue;
                var prefab = Resources.Load<GameObject>("Creatures/" + model + "/" + model); if (prefab == null) continue;
                var root = new GameObject(a.name + "_" + a.index).transform;
                var body = Instantiate(prefab, root, false).transform; body.localRotation = Quaternion.Euler(0, 180, 0);
                foreach (var preview in body.GetComponentsInChildren<XenaAssetPreview>(true)) preview.enabled = false; // no preview GUI / clock
                var c = new Creature { kind = model == "Frog" ? "frog" : "bfly", root = root, body = body, p = new Vector3(a.position[0], a.position[1], a.position[2]), yaw = a.yaw & 4095 };
                c.mesh0 = Find(body, "Geometry_0"); c.mesh1 = Find(body, "Geometry_1");
                if (c.kind == "frog") { c.mode = 0; if (c.mesh0 != null) c.mesh0.gameObject.SetActive(false); }
                c.previous = c.p; c.previousYaw = c.yaw; Creatures.Add(c); Draw(c, 1);
            }
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
            foreach (var c in Creatures)
            {
                if (c.gone) continue;
                c.previous = c.p; c.previousYaw = c.yaw;
                if (c.kind == "frog") Frog(c, xena); else Butterfly(c, xena);
            }
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
            foreach (var c in Creatures) if (!c.gone) Draw(c, alpha);
        }
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
