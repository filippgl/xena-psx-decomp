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
    // NOT implemented: armour subtraction (up_armor absent on level 01).
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
        // Karma meter D_800F1135 (func_80032904): +10 for kills while Xena is in
        // 0x61/0x62/0x5A/0x60/0x49/0x55/0x3F/0x5C/0x5F, -10 for killing a fleeing
        // enemy (flee not ported); at >= 96 the power-up D_800F1136 = 825 steps
        // doubles Xena's damage; the meter resets when it ends.
        public int Karma { get; private set; }
        public int PowerUp { get; private set; }
        public int PowerUps { get; private set; }
        private static readonly int[] KarmaStates = { 0x61, 0x62, 0x5A, 0x60, 0x49, 0x55, 0x3F, 0x5C, 0x5F };
        // Hit effect type 2 (yellow) states, D_800C0F4C (docs/xena-hit-effects.md).
        private static readonly int[] SpecialEffectStates = { 0x61, 0x62, 0x5A, 0x55, 0x60, 0x49, 0x3F, 0x5C, 0x5F };
        private PrototypeHitEffects effects;
        // Added after this component by the motor: resolve on first use.
        private PrototypeHitEffects Effects => effects != null ? effects : (effects = GetComponent<PrototypeHitEffects>());
        public void SetKarmaForTest(int value) { Karma = Mathf.Clamp(value, 0, 96); }
        public bool Dead { get; private set; }
        public int DeathFrame => deathFrame;
        // Fall damage (0x8003B834): landing drop > 2000 units deals drop - 2000 (1000 with D_800F0EF4,
        // not ported), unblockable and without a reaction; a fall kill plays clip 0x8F (10 frames) and the
        // menu follows after 180 vblanks; falling into the void ends the game at once (no clip).
        public bool FallDeath { get; private set; }
        public bool FellIntoVoid { get; private set; }
        public int LastFallDamage { get; private set; }
        public int MenuAfterSteps => FellIntoVoid ? 0 : FallDeath ? 10 + 90 : 65 + 45;
        public void OnLanded(float dropUnits)
        {
            if (Dead || dropUnits <= 2000) return;
            int d = Mathf.RoundToInt(dropUnits) - 2000; LastFallDamage = d;
            Health = Mathf.Max(0, Health - d); HitsTaken++; LastIntake = d;
            if (Health == 0) { Dead = true; FallDeath = true; deathFrame = 0; motor.enabled = false; Reacting = false; }
        }
        public void FellOut() { if (Dead) return; Health = 0; Dead = true; FellIntoVoid = true; deathFrame = 0; motor.enabled = false; Reacting = false; }
        // Xena hit reactions (docs/xena-hit-reactions.md, func_800367BC 0x80037E68..0x800383F8):
        // only when final damage >= 2; sector s = (atan2(Xena - attacker) - rotY + 0x200) & 0xFFF:
        // [0,0x400) front -> 0x66 (2/3) / 0x67 (1/3), or knockdown 0x7C when damage >= 30;
        // [0x400,0x800) side A -> 0x74; [0x800,0xC00) rear -> 0x72; else side B -> 0x75 (mid
        // height assumed: bandit contact heights are not ported; side mirroring UNKNOWN).
        // Frame 1 is held 4 steps pushing min(d,30) units/step away from the attacker (not
        // for knockdowns); input is locked; invulnerable only while the reaction clip plays;
        // knockdowns get up with 0x88. No reaction in attack states 0x48, 0x4A..0x4D, 0x5C,
        // airborne or pickup states (damage still applies).
        public bool Reacting { get; private set; }
        public int ReactionSlot { get; private set; } = -1;
        public int ReactionFrame { get; private set; }
        public int Reactions { get; private set; }
        private int freeze, pushUnits; private Vector3 pushDirection; private bool getUp;
        private static readonly Dictionary<int, int> ReactionFrames = new Dictionary<int, int> { { 0x66, 13 }, { 0x67, 22 }, { 0x70, 41 }, { 0x72, 14 }, { 0x74, 14 }, { 0x75, 14 }, { 0x7C, 26 }, { 0x88, 37 } };
        private readonly System.Random random = new System.Random(5);
        private void StartReaction(int damage, Vector3 attacker)
        {
            if (combat != null && (combat.Slot == 0x48 || (combat.Slot >= 0x4A && combat.Slot <= 0x4D) || combat.Slot == 0x5C)) return;
            if (!motor.Grounded) return;
            Vector3 away = transform.position - attacker; away.y = 0;
            int angle = Mathf.RoundToInt(Mathf.Atan2(away.x, away.z) / (2 * Mathf.PI) * 4096) & 4095;
            int yaw = Mathf.RoundToInt(transform.eulerAngles.y / 360f * 4096) & 4095;
            int sector = (angle - yaw + 0x200) & 0xFFF;
            int slot = sector < 0x400 ? (damage >= 30 ? 0x7C : (random.Next(3) < 2 ? 0x66 : 0x67)) : sector < 0x800 ? 0x74 : sector < 0xC00 ? 0x72 : 0x75;
            if (combat != null) combat.Interrupt();
            Reacting = true; ReactionSlot = slot; ReactionFrame = 1; getUp = slot == 0x7C; Reactions++;
            freeze = slot == 0x7C ? 0 : 4; pushUnits = Mathf.Min(damage, 30); pushDirection = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
        }
        // Called by the motor each 60 Hz tick while reacting; steps the 30/s reaction clock.
        private float reactionClock;
        public void PresentReaction(CharacterController controller)
        {
            reactionClock += Time.fixedDeltaTime;
            while (reactionClock >= 1f / 30 && Reacting)
            {
                reactionClock -= 1f / 30;
                if (freeze > 0) { freeze--; controller.Move(pushDirection * pushUnits / 400f); }
                else if (++ReactionFrame >= ReactionFrames[ReactionSlot])
                {
                    if (getUp && ReactionSlot != 0x88) { ReactionSlot = 0x88; ReactionFrame = 0; }
                    else { Reacting = false; ReactionSlot = -1; }
                }
            }
            if (Reacting) visual.SampleSlotFrame(ReactionSlot, ReactionFrame);
        }
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
        public void ResetState() { FallDeath = false; FellIntoVoid = false; Karma = 0; PowerUp = 0; Reacting = false; ReactionSlot = -1; Reactions = 0; Health = MaxHealth; Dead = false; deathFrame = 0; HitsTaken = 0; BlockedHits = 0; HitsDealt = 0; struck.Clear(); attackSlot = -1; if (motor != null) motor.enabled = true; }

        private void FixedUpdate()
        {
            clock += Time.fixedDeltaTime;
            while (clock >= 1f / 30) { clock -= 1f / 30; Step(); }
        }
        private void Step()
        {
            if (Dead)
            {
                // Original death clip 0x91 (65 frames) on the 30/s clock, then held on its last
                // frame; PrototypeGameOver opens the menu at DeathFrame >= MenuStep.
                if (!FellIntoVoid) visual.SampleSlotFrame(FallDeath ? 0x8F : 0x91, Mathf.Min(deathFrame, FallDeath ? 9 : 64));
                deathFrame++;
                return;
            }
            if (PowerUp > 0 && --PowerUp == 0) Karma = 0;
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
                int damage = Math.Abs(window.damage) * (PowerUp > 0 ? 2 : 1) + (scripts != null ? scripts.SwordLevel : 0);
                foreach (var bandit in bandits.Brains)
                {
                    if (bandit == null || bandit.Removed || bandit.Health <= 0 || struck.Contains(bandit)) continue;
                    Vector3 offset = bandit.transform.position - transform.position; float dy = offset.y; offset.y = 0;
                    if (offset.magnitude > reach + PrototypeBanditBrain.Radius / 400f || Mathf.Abs(dy) * 400 > 750) continue;
                    if (!spin && offset.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, offset) > 60) continue;
                    if (bandit.TakeHit(damage, transform.position, kick))
                    {
                        struck.Add(bandit); HitsDealt++; LastDamageDealt = damage;
                        // Hit effects at the contact point (docs/xena-hit-effects.md): yellow for the
                        // kick/special states, red otherwise; the bandit bleeds for d >= 4 (20 steps on a kill).
                        if (Effects != null)
                        {
                            Vector3 toward = transform.position - bandit.transform.position; toward.y = 0; toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : bandit.transform.forward;
                            Vector3 point = bandit.transform.position + toward * (PrototypeBanditBrain.Radius / 400f) + Vector3.up * ((kick ? 733 : 418) / 400f);
                            effects.Spark(point, damage, Array.IndexOf(SpecialEffectStates, combat.Slot) >= 0 ? PrototypeHitEffects.Type.Special : PrototypeHitEffects.Type.Hit);
                            if (damage >= 4) effects.Bleed(bandit.transform, point, bandit.Health == 0 ? 20 : 10, bandit.Health == 0 ? 170 : 80);
                        }
                        if (bandit.Health == 0 && PowerUp == 0 && Array.IndexOf(KarmaStates, combat.Slot) >= 0 && (Karma = Mathf.Min(96, Karma + 10)) >= 96) { PowerUp = 825; PowerUps++; }
                    }
                }
            }
        }

        // Bandit -> Xena (called by the bandit inside its attack window).
        public bool TakeHit(int damage, Vector3 attacker)
        {
            if (Dead || (scripts != null && (scripts.Modal || scripts.CutsceneActive))) return false;
            // Invulnerable while a reaction clip plays (not during the get-up 0x88).
            if (Reacting && ReactionSlot != 0x88) return false;
            Vector3 toward = attacker - transform.position; toward.y = 0;
            bool facing = toward.sqrMagnitude < 0.0001f || Vector3.Angle(transform.forward, toward) <= 90;
            int raw = damage;
            if (combat != null && combat.IsBlocking && facing) { damage = damage > 40 ? damage - 40 : 1; BlockedHits++; }
            if (Effects != null)
            {
                // Contact 447 units above Xena's feet; blue flash with the raw value when blocked;
                // Xena bleeds for a final d >= 2 (blocked hits deal 1 and never bleed).
                Vector3 away = attacker - transform.position; away.y = 0; away = away.sqrMagnitude > 1e-4f ? away.normalized : transform.forward;
                Vector3 point = transform.position + away * 0.3f + Vector3.up * (447 / 400f);
                bool blocked = combat != null && combat.IsBlocking && facing;
                effects.Spark(point, blocked ? raw : damage, blocked ? PrototypeHitEffects.Type.Block : PrototypeHitEffects.Type.Hit);
                if (!blocked && damage >= 2) effects.Bleed(transform, point, 8, 200);
            }
            Health = Mathf.Max(0, Health - damage); HitsTaken++; LastIntake = damage; LastIntakeBlocked = combat != null && combat.IsBlocking && facing;
            if (Health > 0 && Health < PotionThreshold && scripts != null && scripts.UsePotion()) Health = Mathf.Min(MaxHealth, Health + MaxHealth * 30 / 100);
            if (Health == 0) { Dead = true; deathFrame = 0; motor.enabled = false; Reacting = false; }
            else if (damage >= 2) StartReaction(damage, attacker);
            return true;
        }

    }
}
