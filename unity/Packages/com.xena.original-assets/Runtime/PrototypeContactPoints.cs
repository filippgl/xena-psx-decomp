using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Original weapon contact points (docs/xena-contact-points.md, VERIFIED on native RAM): fixed offsets on
    // skeleton parts posed per clip frame (Resources/Combat/contact_points.json from
    // tools/install_contact_points.py). Points are attacker-local PSX units (y down, facing -z); with the
    // bodies' 180 deg turn the Unity offset is (-x, -y, -z) / 400 rotated by the root yaw. Step counter n
    // uses frame n - 1 and the root from before the step's movement. Contact test func_80070DFC: coarse
    // dist < 2 (r_t + r_a); candidate = projection on the prev -> cur segment only when strictly
    // cur < p < prev on all axes, else cur then prev; hit when feet - height < y < feet and
    // dist < r_t + 50, dist = max(|dx|,|dz|) + 3/8 min (integer). No facing cone.
    public static class PrototypeContactPoints
    {
        [Serializable] private sealed class FrameData { public int damage, active; public int[] points; public float[] pivot; }
        [Serializable] private sealed class ClipData { public int slot; public FrameData[] frames; }
        [Serializable] private sealed class ActorData { public string name; public int radius, height, twistChain; public ClipData[] clips; }
        [Serializable] private sealed class FileData { public ActorData[] actors; }
        private static Dictionary<string, ActorData> actors;
        private static Dictionary<string, Dictionary<int, FrameData[]>> clips;

        private static bool Load()
        {
            if (actors != null) return actors.Count > 0;
            actors = new Dictionary<string, ActorData>(); clips = new Dictionary<string, Dictionary<int, FrameData[]>>();
            var json = Resources.Load<TextAsset>("Combat/contact_points");
            if (json == null) return false;
            foreach (var a in JsonUtility.FromJson<FileData>(json.text).actors)
            {
                actors[a.name] = a; var map = clips[a.name] = new Dictionary<int, FrameData[]>();
                foreach (var c in a.clips) map[c.slot] = c.frames;
            }
            return actors.Count > 0;
        }
        public static bool Available => Load();
        public static bool HasClip(string actor, int slot) => Load() && clips.TryGetValue(actor, out var m) && m.ContainsKey(slot);

        // World points of clip frame k (only the active ones are written; mask returned). twist: bandit
        // part-3 rotY (+0x11E, original angle units) applied to the points on part 3's chain.
        public static int Pose(string actor, int slot, int k, Vector3 root, float yawDegrees, int twist, Vector3[] world, out int damage)
        {
            damage = 0;
            if (!Load() || !clips[actor].TryGetValue(slot, out var frames) || frames.Length == 0) return 0;
            var f = frames[Mathf.Clamp(k, 0, frames.Length - 1)]; damage = f.damage;
            var rotation = Quaternion.Euler(0, yawDegrees, 0); int chain = actors[actor].twistChain;
            for (int i = 0; i < world.Length && i * 3 + 2 < f.points.Length; i++)
            {
                if ((f.active >> i & 1) == 0) continue;
                var p = new Vector3(f.points[i * 3], f.points[i * 3 + 1], f.points[i * 3 + 2]);
                if (twist != 0 && (chain >> i & 1) != 0 && f.pivot != null && f.pivot.Length == 6)
                {
                    var origin = new Vector3(f.pivot[0], f.pivot[1], f.pivot[2]); var axis = new Vector3(f.pivot[3], f.pivot[4], f.pivot[5]);
                    p = origin + Rodrigues(p - origin, axis.normalized, twist * 2 * Mathf.PI / 4096);
                }
                world[i] = root + rotation * (new Vector3(-p.x, -p.y, -p.z) / 400f);
            }
            return f.active;
        }
        // Right-handed rotation about axis in the PSX local space (same sign as the part rotation).
        private static Vector3 Rodrigues(Vector3 v, Vector3 axis, float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            var cross = new Vector3(axis.y * v.z - axis.z * v.y, axis.z * v.x - axis.x * v.z, axis.x * v.y - axis.y * v.x);
            return v * c + cross * s + axis * Vector3.Dot(axis, v) * (1 - c);
        }

        private static int Dist(Vector3 a, Vector3 b)
        {
            int dx = Mathf.Abs(Mathf.RoundToInt((a.x - b.x) * 400)), dz = Mathf.Abs(Mathf.RoundToInt((a.z - b.z) * 400));
            return Mathf.Max(dx, dz) + (3 * Mathf.Min(dx, dz) >> 3);
        }
        private static bool Fine(Vector3 p, Vector3 feet, int rTarget, int height)
        {
            float up = (p.y - feet.y) * 400; // PSX feet - height < y < feet  <=>  0 < up < height
            return up > 0 && up < height && Dist(p, feet) < rTarget + 50;
        }
        public static bool Test(Vector3 cur, Vector3 prev, bool hasPrev, Vector3 feet, int rTarget, int rAttacker, int height, out Vector3 hit)
        {
            hit = cur;
            if (Dist(cur, feet) >= 2 * (rTarget + rAttacker)) return false;
            if (hasPrev)
            {
                // PSX space (x, -y, z) units: the original compares cur < p < prev componentwise only.
                Vector3 c = Psx(cur), q = Psx(prev), t = Psx(feet), n = (q - c).normalized;
                var p = c + n * Vector3.Dot(n, t - c);
                if (c.x < p.x && p.x < q.x && c.y < p.y && p.y < q.y && c.z < p.z && p.z < q.z)
                {
                    var u = new Vector3(p.x, -p.y, p.z) / 400f;
                    if (Fine(u, feet, rTarget, height)) { hit = u; return true; }
                    return false;
                }
            }
            if (Fine(cur, feet, rTarget, height)) { hit = cur; return true; }
            if (hasPrev && Fine(prev, feet, rTarget, height)) { hit = prev; return true; }
            return false;
        }
        private static Vector3 Psx(Vector3 u) => new Vector3(u.x, -u.y, u.z) * 400;
    }
}
