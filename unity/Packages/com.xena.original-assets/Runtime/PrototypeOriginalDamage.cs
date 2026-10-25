using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Original damage between Xena and enemies (docs/xena-damage-audit.md).
    // VERIFIED values: per-clip damage windows from the action scripts (Xena
    // 0x800C90D8, bandits 0x800C9CF8; +0xFE live on frames start+1..end); Xena
    // damage = |d| + sword level (up_sword, 0..4); Xena health 500, potion
    // auto-use below 350 (+30% of max); blocked bandit hits deal d>40 ? d-40 : 1;
    // Xena death clip 0x91. APPROXIMATED here (contact points not ported yet):
    // contact = horizontal reach (sword 1.65 m / kick 1.25 m for Xena, 1.85 m for
    // bandits: their 700-unit attack range) in a +-60 deg front cone and |dy| < 750 units. Each attack hits a
    // target once (the original invulnerability timer D_800F0CC2 is UNKNOWN).
    // NOT implemented: power-up doubling (kill meter), armour subtraction
    // (up_armor absent on level 01), Xena hit-reaction clips, game-over flow.
    // The practice dummy keeps its separate provisional damage.
    public sealed class PrototypeOriginalDamage : MonoBehaviour
    {
        public struct Window { public int damage, start, end; public Window(int d, int s, int e) { damage = d; start = s; end = e; } }
        // Xena clip -> windows (negative = kick contact points).
        public static readonly Dictionary<int, Window[]> XenaWindows = new Dictionary<int, Window[]>
        {
            { 0x51, new[] { new Window(30, 4, 10) } }, { 0x52, new[] { new Window(40, 2, 10) } }, { 0x3A, new[] { new Window(45, 11, 18) } },
            { 0x53, new[] { new Window(75, 11, 18) } }, { 0x4F, new[] { new Window(80, 13, 26) } }, { 0x55, new[] { new Window(50, 7, 16) } },
            { 0x61, new[] { new Window(-35, 3, 12) } }, { 0x62, new[] { new Window(-35, 0, 10), new Window(-40, 10, 18) } },
            { 0x5A, new[] { new Window(-70, 0, 16) } }, { 0x56, new[] { new Window(-35, 0, 3) } }, { 0x46, new[] { new Window(-70, 14, 20) } },
            { 0x5C, new[] { new Window(-60, 14, 38) } }, { 0x3E, new[] { new Window(30, 0, 36) } }, { 0x3C, new[] { new Window(15, 1, 40) } },
        };
        // Bandit clip -> window (all 7 contact points, no level scaling).
        public static readonly Dictionary<int, Window> BanditWindows = new Dictionary<int, Window>
        {
            { 0x3D, new Window(25, 11, 20) }, { 0x3E, new Window(15, 7, 13) }, { 0x3F, new Window(15, 8, 15) },
            { 0x50, new Window(15, 9, 16) }, { 0x3C, new Window(30, 12, 20) }, { 0x40, new Window(15, 13, 23) },
        };
        public const int MaxHealth = 500, PotionThreshold = 350;
        public int Health { get; private set; } = MaxHealth;
        public int HitsTaken { get; private set; }
        public int LastIntake { get; private set; }
        public bool LastIntakeBlocked { get; private set; }
        public int BlockedHits { get; private set; }
        public int HitsDealt { get; private set; }
        public int LastDamageDealt { get; private set; }
        public bool Dead { get; private set; }
        private PrototypeMotor motor; private PrototypeCombat combat; private PrototypeLevelScripts scripts; private PrototypeBandits bandits;
        private XenaAssetPreview visual;
        private float clock; private int deathFrame;
        private int attackSlot = -1; private float attackStart;
        private readonly HashSet<PrototypeBanditBrain> struck = new HashSet<PrototypeBanditBrain>();

        private void Awake()
        {
            motor = GetComponent<PrototypeMotor>(); combat = GetComponent<PrototypeCombat>(); scripts = GetComponent<PrototypeLevelScripts>();
            bandits = GetComponent<PrototypeBandits>(); visual = GetComponentInChildren<XenaAssetPreview>();
        }
        // health2 pickup (jtbl_80017594 / func_800410C0): potion +1, used at once
        // when health + 150 < 500 (+30% of max, capped); otherwise kept.
        public void OnPotionPicked()
        {
            if (Health + MaxHealth * 30 / 100 < MaxHealth && scripts != null && scripts.UsePotion()) Health = Mathf.Min(MaxHealth, Health + MaxHealth * 30 / 100);
        }
        public void SetHealthForTest(int value) { Health = Mathf.Clamp(value, 0, MaxHealth); }
        public void ResetState() { Health = MaxHealth; Dead = false; deathFrame = 0; HitsTaken = 0; BlockedHits = 0; HitsDealt = 0; struck.Clear(); attackSlot = -1; if (motor != null) motor.enabled = true; }

        private void FixedUpdate()
        {
            clock += Time.fixedDeltaTime;
            while (clock >= 1f / 30) { clock -= 1f / 30; Step(); }
        }
        private void Step()
        {
            if (Dead)
            {
                // Original death clip 0x91 on the 30/s clock; the game-over flow is unknown.
                visual.SampleSlotFrame(0x91, deathFrame); deathFrame++;
                return;
            }
            if (combat == null || bandits == null) return;
            // One hit per target per attack instance (a new clip start clears it).
            if (!combat.Busy || combat.Slot != attackSlot) { struck.Clear(); attackSlot = combat.Busy ? combat.Slot : -1; }
            if (!combat.Busy || !XenaWindows.TryGetValue(combat.Slot, out var windows)) return;
            int frame = combat.Frame;
            foreach (var window in windows)
            {
                if (frame <= window.start || frame > window.end) continue;
                bool kick = window.damage < 0;
                float reach = kick ? 1.25f : 1.65f;
                bool spin = combat.Slot == 0x5C || combat.Slot == 0x46 || combat.Slot == 0x4D;
                int damage = Math.Abs(window.damage) + (scripts != null ? scripts.SwordLevel : 0);
                foreach (var bandit in bandits.Brains)
                {
                    if (bandit == null || bandit.Removed || bandit.Health <= 0 || struck.Contains(bandit)) continue;
                    Vector3 offset = bandit.transform.position - transform.position; float dy = offset.y; offset.y = 0;
                    if (offset.magnitude > reach + PrototypeBanditBrain.Radius / 400f || Mathf.Abs(dy) * 400 > 750) continue;
                    if (!spin && offset.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, offset) > 60) continue;
                    if (bandit.TakeHit(damage, transform.position, kick)) { struck.Add(bandit); HitsDealt++; LastDamageDealt = damage; }
                }
            }
        }

        // Bandit -> Xena (called by the bandit inside its attack window).
        public bool TakeHit(int damage, Vector3 attacker)
        {
            if (Dead || (scripts != null && (scripts.Modal || scripts.CutsceneActive))) return false;
            Vector3 toward = attacker - transform.position; toward.y = 0;
            bool facing = toward.sqrMagnitude < 0.0001f || Vector3.Angle(transform.forward, toward) <= 90;
            if (combat != null && combat.IsBlocking && facing) { damage = damage > 40 ? damage - 40 : 1; BlockedHits++; }
            Health = Mathf.Max(0, Health - damage); HitsTaken++; LastIntake = damage; LastIntakeBlocked = combat != null && combat.IsBlocking && facing;
            if (Health > 0 && Health < PotionThreshold && scripts != null && scripts.UsePotion()) Health = Mathf.Min(MaxHealth, Health + MaxHealth * 30 / 100);
            if (Health == 0) { Dead = true; deathFrame = 0; motor.enabled = false; }
            return true;
        }

    }
}
