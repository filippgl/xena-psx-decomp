using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Animation-frame and ambient sound triggers of level 01 (docs/xena-sfx.md, tools/sfx_triggers.json;
    // VERIFIED calls unless noted). Script opcodes fire when a clip reaches the listed frame (30/s clock):
    // Xena sword swings rand(0,2), kicks rand(3,7), whoosh 8; footsteps rand(0x2E,0x32) vol 31 (level 01
    // default surface 3) on walk 0x15 f11/f22 and run 0x1A f6/f17; death voice XA rand(0x1013,0x1014) at
    // 0x91 f2. Bandits: swings rand(0,2), footsteps on 0x15 f4/f24 and 0x1A f2/f14, death thud 0x2E, XA
    // 0x1000 at 0x7B f10. Level load: water loop 0x13 note 70 vol 63 at (-3750,1515,12475) and the two
    // splash actors (vol 31). Power-up loop 0x3D note 71 vol 31 at Xena while the power-up runs.
    public sealed class PrototypeSfxEvents : MonoBehaviour
    {
        public static readonly Dictionary<int, int[]> XenaSword = new Dictionary<int, int[]>
        {
            { 0x51, new[] { 7 } }, { 0x3A, new[] { 11 } }, { 0x4F, new[] { 13 } }, { 0x53, new[] { 15 } }, { 0x55, new[] { 10 } }, { 0x3B, new[] { 5 } },
            { 0x3C, new[] { 1 } }, { 0x3D, new[] { 2 } }, { 0x3E, new[] { 36 } }, { 0x40, new[] { 15 } }, { 0x44, new[] { 15 } }, { 0x45, new[] { 6 } },
            { 0x46, new[] { 14 } }, { 0x47, new[] { 20 } },
        };
        public static readonly Dictionary<int, int[]> XenaKick = new Dictionary<int, int[]>
        {
            { 0x61, new[] { 8 } }, { 0x5A, new[] { 16 } }, { 0x59, new[] { 9 } }, { 0x5B, new[] { 8 } }, { 0x63, new[] { 8 } }, { 0x60, new[] { 10 } },
            { 0x49, new[] { 14 } }, { 0x48, new[] { 3 } }, { 0x3F, new[] { 7 } },
        };
        private static readonly Dictionary<int, int[]> XenaWhoosh = new Dictionary<int, int[]> { { 0x4A, new[] { 23 } }, { 0x5C, new[] { 14 } } };
        public static readonly Dictionary<int, int[]> Steps = new Dictionary<int, int[]> { { 0x15, new[] { 11, 22 } }, { 0x1A, new[] { 6, 17 } } };
        public static readonly Dictionary<int, int[]> BanditSword = new Dictionary<int, int[]>
        {
            { 0x48, new[] { 11 } }, { 0x58, new[] { 11 } }, { 0x59, new[] { 12 } }, { 0x3A, new[] { 12 } }, { 0x3B, new[] { 6 } }, { 0x3C, new[] { 12 } },
            { 0x3D, new[] { 11 } }, { 0x3E, new[] { 7 } }, { 0x3F, new[] { 8 } }, { 0x40, new[] { 13 } }, { 0x41, new[] { 11 } }, { 0x45, new[] { 13, 26 } },
            { 0x46, new[] { 14, 29 } }, { 0x4C, new[] { 10, 26 } }, { 0x4F, new[] { 8 } }, { 0x50, new[] { 9 } },
        };
        public static readonly Dictionary<int, int[]> BanditSteps = new Dictionary<int, int[]> { { 0x15, new[] { 4, 24 } }, { 0x1A, new[] { 2, 14 } } };
        public static readonly Dictionary<int, int[]> BanditThud = new Dictionary<int, int[]>
        { { 0x91, new[] { 21 } }, { 0x8A, new[] { 29 } }, { 0x8B, new[] { 19 } }, { 0x8C, new[] { 17 } }, { 0x8D, new[] { 14 } }, { 0x8E, new[] { 33 } }, { 0x90, new[] { 12 } } };

        // Fires when (slot, frame) reaches a listed frame since the last observation (a new clip counts from 0).
        public static bool Crossed(Dictionary<int, int[]> table, int lastSlot, int lastFrame, int slot, int frame)
        {
            if (!table.TryGetValue(slot, out var frames)) return false;
            int from = slot == lastSlot && frame >= lastFrame ? lastFrame : -1;
            foreach (var f in frames) if (f > from && f <= frame) return true;
            return false;
        }

        private PrototypeCombat combat; private PrototypeOriginalDamage damage; private XenaAssetPreview visual; private PrototypeChakram chakram;
        private int lastSlot = -1, lastFrame = -1, steps, whooshTimer; private bool powerLoop, flying;
        public int FootstepsPlayed { get; private set; }
        private void Awake()
        {
            combat = GetComponent<PrototypeCombat>(); damage = GetComponent<PrototypeOriginalDamage>(); chakram = GetComponent<PrototypeChakram>();
            visual = GetComponentInChildren<XenaAssetPreview>();
        }
        private void Start()
        {
            PrototypeSfx.StopAll();
            PrototypeSfx.PlayAt(0x13, PrototypeFire.Unity(new[] { -3750, 1515, 12475 }), 70, 63);
            PrototypeSfx.PlayAt(0x13, PrototypeFire.Unity(new[] { 1249, -2883, -12923 }), 70, 31);
            PrototypeSfx.PlayAt(0x13, PrototypeFire.Unity(new[] { 7273, 4212, 4793 }), 70, 31);
        }
        private void FixedUpdate()
        {
            if (++steps % Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime)) != 0) return;
            int slot = combat != null && combat.Busy ? combat.Slot : visual != null ? visual.CurrentSlot : -1;
            int frame = combat != null && combat.Busy ? combat.Frame : visual != null ? visual.CurrentFrame : -1;
            if (damage != null && damage.Dead) { slot = 0x91; frame = damage.DeathFrame; }
            var sfx = PrototypeSfx.Instance;
            if (Crossed(XenaSword, lastSlot, lastFrame, slot, frame)) PrototypeSfx.PlayOn(sfx.Rand(0, 2), transform);
            if (Crossed(XenaKick, lastSlot, lastFrame, slot, frame)) PrototypeSfx.PlayOn(sfx.Rand(3, 7), transform);
            if (Crossed(XenaWhoosh, lastSlot, lastFrame, slot, frame)) PrototypeSfx.PlayOn(8, transform);
            if (Crossed(Steps, lastSlot, lastFrame, slot, frame)) { PrototypeSfx.PlayOn(sfx.Rand(0x2E, 0x32), transform, 60, 31); FootstepsPlayed++; }
            if (damage != null && damage.Dead && !damage.FellIntoVoid && !damage.FallDeath && lastFrame < 2 && frame >= 2 && lastSlot == 0x91) PrototypeSfx.PlayOn(sfx.Rand(0x1013, 0x1014), transform);
            lastSlot = slot; lastFrame = frame;
            // Power-up loop while D_800F1136 runs (func_8003DCF8 / func_8003DD78).
            bool power = damage != null && damage.PowerUp > 0;
            if (power && !powerLoop) PrototypeSfx.PlayOn(0x3D, transform, 71, 31);
            if (!power && powerLoop) PrototypeSfx.Stop(0x3D);
            powerLoop = power;
            // Chakram whoosh every rand(8,16) frames in flight (func_8003DDEC 0x8003F034).
            if (chakram != null && chakram.Flying)
            {
                if (!flying) whooshTimer = 0;
                if (--whooshTimer <= 0) { PrototypeSfx.PlayAt(8, chakram.ProjectilePosition); whooshTimer = sfx.Rand(8, 16); }
            }
            flying = chakram != null && chakram.Flying;
        }
    }
}
