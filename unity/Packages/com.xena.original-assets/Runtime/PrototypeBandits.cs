using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Level01 bandit generators (docs/xena-level-scripts.md, generator update
    // 0x80032C18; data tools/export_level_scripts.mjs, 42/42 RAM checks).
    // VERIFIED rules: each generator releases its remaining count one at a
    // time; it runs once per 16 frames when (actor index & 15) == (frame & 15);
    // trigger is a radius (desc+0x2C + 100*p2) or, with flag 0x400, Xena within
    // 800 of waypoint1->waypoint2; flag 0x2000 also needs |dy| <= 1000; at most
    // levelConfig.maxLiveEnemies (3) enemies alive; nothing within 350 of the
    // spawn point. Frames are counted at 30/s (unverified), distances use the
    // original max+3/8min metric for radii and XZ point-segment distance for
    // segments (INFERRED). Not implemented: camera line test flag 0x100 (its
    // direction is unknown), the post-3-spawn rate limit D_800F0A58 (unknown,
    // treated as 0), larks.
    public sealed class PrototypeBandits : MonoBehaviour, IXenaVisualModeTarget
    {
        [Serializable] private sealed class ScriptFile { public LevelConfig levelConfig; public Actor[] actors; }
        [Serializable] private sealed class LevelConfig { public Value maxLiveEnemies; }
        [Serializable] private sealed class Value { public int value; }
        [Serializable] private sealed class Actor { public int index; public string name, behaviour; public Generator generator; }
        [Serializable] private sealed class Generator
        {
            public string spawnClass, flags; public int remaining, aggression, healthDelta, blockPercent, spawnYaw; public bool infinite, countsAsEnemy;
            public int[] spawnPosition, patrolTarget; public Trigger trigger;
        }
        [Serializable] private sealed class Trigger { public string type; public int[] centre, from, to; public int radius, lateral; }
        private sealed class Slot { public Actor actor; public int remaining; public int flags; public int spawned; }

        // Class max health from descriptors 0x800C33E4+idx*0x44 (+0x08).
        private static readonly Dictionary<string, int> MaxHealth = new Dictionary<string, int> { { "bandit", 75 }, { "bandit1", 100 }, { "bandit2", 75 }, { "bandit4", 75 } };
        public const int IdleSlot = 0x07, Period = 16, Occupancy = 350, VerticalLimit = 1000;
        public readonly List<PrototypeBanditBrain> Brains = new List<PrototypeBanditBrain>();
        private readonly List<XenaAssetPreview> bandits = new List<XenaAssetPreview>();
        public List<XenaAssetPreview> Bandits => bandits;
        public int MaxLiveEnemies { get; private set; } = 3;
        public bool GeneratorsEnabled = true;
        public int Frame { get; private set; }
        public int GeneratorCount => slots.Count;
        public int Spawned { get; private set; }
        public int TotalRemaining { get { int n = 0; foreach (var s in slots) n += s.remaining; return n; } }
        private readonly List<Slot> slots = new List<Slot>();
        private readonly Dictionary<string, Dictionary<int, PrototypeBanditBrain.MotionClip>> motionCache = new Dictionary<string, Dictionary<int, PrototypeBanditBrain.MotionClip>>();
        private PrototypeMotor motor;
        private Transform group;
        private float clock;
        // Player-mechanics smoke runs stay isolated from enemies.
        public static bool AiAllowed { get { foreach (string arg in Environment.GetCommandLineArgs()) if (arg.EndsWith("-smoke") && arg != "--presentation-smoke" && arg != "--bandit-smoke") return false; return true; } }

        private void Awake()
        {
            motor = GetComponent<PrototypeMotor>();
            var source = Resources.Load<TextAsset>("Level01Scripts");
            if (source == null) throw new InvalidOperationException("Level01 script data unavailable");
            var file = JsonUtility.FromJson<ScriptFile>(source.text);
            if (file.levelConfig?.maxLiveEnemies != null) MaxLiveEnemies = file.levelConfig.maxLiveEnemies.value;
            foreach (Actor actor in file.actors)
                if (actor.behaviour == "generator" && actor.generator != null && actor.generator.spawnClass.StartsWith("bandit"))
                    slots.Add(new Slot { actor = actor, remaining = actor.generator.remaining, flags = Convert.ToInt32(actor.generator.flags, 16) });
        }

        private void FixedUpdate()
        {
            if (!AiAllowed || !GeneratorsEnabled) return;
            var input = GetComponent<PrototypeInput>(); if (input != null && input.Paused) return;
            clock += Time.fixedDeltaTime;
            while (clock >= 1f / 30) { clock -= 1f / 30; Frame++; Tick(); }
        }

        // Original live counter D_800F2774 decrements on removal (after the death wait).
        public int Live() { int count = 0; foreach (var brain in Brains) if (brain != null && !brain.Removed) count++; return count; }
        public int Kills { get { int n = 0; foreach (var brain in Brains) if (brain != null && brain.Removed && brain.Health == 0) n++; return n; } }
        private static Vector3 Unity(int[] p) => new Vector3(p[0], -p[1], p[2]) / 400;
        private static int Metric(Vector3 a, Vector3 b)
        {
            int dx = Mathf.Abs(Mathf.RoundToInt((a.x - b.x) * 400)), dz = Mathf.Abs(Mathf.RoundToInt((a.z - b.z) * 400));
            return Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8;
        }
        private bool Triggered(Slot slot, Vector3 xena)
        {
            var g = slot.actor.generator;
            if ((slot.flags & 0x2000) != 0 && Mathf.Abs((xena.y - Unity(g.spawnPosition).y) * 400) > VerticalLimit) return false;
            if (g.trigger.type == "segment")
            {
                Vector2 a = new Vector2(g.trigger.from[0], g.trigger.from[2]), b = new Vector2(g.trigger.to[0], g.trigger.to[2]), p = new Vector2(xena.x, xena.z) * 400;
                Vector2 ab = b - a; float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
                return Vector2.Distance(p, a + ab * t) <= g.trigger.lateral;
            }
            return Metric(xena, Unity(g.trigger.centre)) < g.trigger.radius;
        }

        private void Tick()
        {
            foreach (Slot slot in slots)
            {
                if ((slot.actor.index & (Period - 1)) != (Frame & (Period - 1))) continue;
                if (!slot.actor.generator.infinite && slot.remaining <= 0) continue;
                if (slot.actor.generator.countsAsEnemy && Live() >= MaxLiveEnemies) continue;
                if (!Triggered(slot, motor.transform.position)) continue;
                Vector3 at = Unity(slot.actor.generator.spawnPosition);
                if (Metric(motor.transform.position, at) < Occupancy) continue;
                bool occupied = false; foreach (var brain in Brains) if (brain != null && Metric(brain.transform.position, at) < Occupancy) occupied = true;
                if (occupied) continue;
                Spawn(slot, at); slot.remaining--; slot.spawned++;
            }
        }

        private void Spawn(Slot slot, Vector3 position)
        {
            var g = slot.actor.generator;
            string model = "Bandit" + g.spawnClass.Substring("bandit".Length);
            var prefab = Resources.Load<GameObject>("Bandits/" + model + "/" + model);
            if (prefab == null) throw new InvalidOperationException("Bandit model unavailable: " + model);
            float ground = motor.GroundHeight(position + Vector3.up * 0.5f);
            if (!float.IsNegativeInfinity(ground)) position.y = ground;
            if (group == null) group = new GameObject("Level01Bandits").transform;
            var root = new GameObject(slot.actor.name + " #" + slot.actor.index + " " + model); root.transform.SetParent(group, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, (g.spawnYaw & 4095) * 360f / 4096, 0));
            var body = Instantiate(prefab, root.transform, false); body.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var preview = body.GetComponent<XenaAssetPreview>(); preview.ShowControls = false;
            // New actors take the current Original/Remaster bindings and shadows.
            bool remaster = PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster;
            preview.SetUpscale(remaster);
            foreach (Renderer item in root.GetComponentsInChildren<Renderer>(true))
            { item.shadowCastingMode = remaster ? UnityEngine.Rendering.ShadowCastingMode.TwoSided : UnityEngine.Rendering.ShadowCastingMode.Off; item.receiveShadows = remaster; }
            bandits.Add(preview);
            var brain = root.AddComponent<PrototypeBanditBrain>();
            brain.Configure(preview, motor, Motion(model), MaxHealth[g.spawnClass] + g.healthDelta, g.aggression, slot.actor.index * 7919 + slot.spawned * 131 + 17);
            brain.ConfigureDefence(g.spawnClass != "bandit", g.blockPercent); // classes 0x96..0x98 block; plain bandit never
            brain.ConfigurePatrol(g.patrolTarget != null && g.patrolTarget.Length == 3, g.patrolTarget != null && g.patrolTarget.Length == 3 ? PrototypeFire.Unity(g.patrolTarget) : Vector3.zero); // flag 0x200, waypoint 0
            Brains.Add(brain); Spawned++;
        }

        private Dictionary<int, PrototypeBanditBrain.MotionClip> Motion(string model)
        {
            if (motionCache.TryGetValue(model, out var cached)) return cached;
            var source = Resources.Load<TextAsset>("Bandits/" + model + "/" + model + "Motion");
            if (source == null) throw new InvalidOperationException("Bandit root motion unavailable: " + model);
            var clips = new Dictionary<int, PrototypeBanditBrain.MotionClip>();
            foreach (var clip in JsonUtility.FromJson<PrototypeBanditBrain.MotionFile>(source.text).clips) clips[clip.slot] = clip;
            return motionCache[model] = clips;
        }

        // Remaster keeps corpses of removed bandits; Original hides them.
        public void ApplyVisualMode(XenaVisualMode mode)
        { foreach (var brain in Brains) if (brain != null && brain.Removed) brain.ShowCorpse(mode == XenaVisualMode.Remaster); }
        // Tests: release the next pending generator immediately at a position.
        public PrototypeBanditBrain SpawnForTest(Vector3 position)
        {
            foreach (Slot slot in slots) if (slot.remaining > 0) { Spawn(slot, position); slot.remaining--; slot.spawned++; return Brains[Brains.Count - 1]; }
            return null;
        }
    }
}
