using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Bandit AI after func_8001B460 (docs/xena-enemy-audit.md, original units:
    // 400 = 1 m, 4096 = 360 deg). One AI step per 1/30 s (30 steps/s is the
    // audit's unverified assumption). Implemented: modes 0 idle, 1 hold, 2
    // approach (walk 15 / run 1A at the 1700 boundary), 3 wait (max two
    // attackers per target), 5 attack (weighted 3D/3E/3F/50/none, cooldown
    // rand(4q,6q), q=(100-aggression)/5; backstep 23 under 550; ally spacing
    // sidesteps). Movement is the clip root motion (BANDIT.SMA speed/direction
    // per frame). Block (mode 4, bandit1/2/4 only): dist < 1000, rand(1,100) <= block %, Xena in an
    // attack state 0x3A..0x65 within +-70 deg ahead; clip 80 then 82; guard while 80 frame < 15 or
    // 82 frame < 14 discards all damage (blue spark); leaves to attack (< 700) or hold. Flee: after a
    // hit with health < max/3, 1/4 chance: turn away and run, despawn at dist >= 14001
    // (docs/xena-enemy-audit.md, docs/xena-damage-audit.md section 4). NOT implemented: patrol
    // waypoints, avoid (mode 6).
    public sealed class PrototypeBanditBrain : MonoBehaviour
    {
        [Serializable] public sealed class MotionFile { public MotionClip[] clips; }
        [Serializable] public sealed class MotionClip { public int slot, frames; public Move[] moves; }
        [Serializable] public sealed class Move { public int frame, speed, direction; }
        public enum Mode { Idle = 0, Hold = 1, Approach = 2, Wait = 3, Block = 4, Attack = 5, Avoid = 6 }
        // Mode 6 avoid (docs/xena-bandit-avoid.md, VERIFIED on native RAM unless noted). The mover drops a step
        // that would end within r + r = 300 units of another live bandit (unless already overlapping it) and keeps
        // that bandit as the sticky last contact. Entry (0x80020048, outside hit/attack/death clips): the mover
        // was blocked by a non-target actor and the goal (Xena, or the patrol waypoint) is >= 3001 away -> mode 6
        // (nothing when the blocker itself avoids); that step does nothing else. Action (0x8002103C), R = 850:
        // blocker behind (dot < 0) and d > R -> turn to the goal (+-64); else |lat| < R -> turn 64 away from the
        // blocker's side; else turn to the goal and back to mode 1. Walk below 1700 from the blocker, else run.
        // A blocker idling or patrolling is woken (mode 1). The push on the blocker is INFERRED: not ported.
        public PrototypeBanditBrain LastContact { get; private set; }
        public int AvoidEntries { get; private set; }
        private bool moverBlocked;
        public const int BlockStart = 0x80, BlockHold = 0x82;
        public bool CanBlock { get; private set; }
        public int BlockPercent { get; private set; }
        public int Blocks { get; private set; }
        public bool Fleeing { get; private set; }
        public bool Blocking => !Dying && ((Slot == BlockStart && Frame < 15) || (Slot == BlockHold && Frame < 14));
        private int blockRolledFor = -1;
        public void ConfigureDefence(bool canBlock, int blockPercent) { CanBlock = canBlock; BlockPercent = blockPercent; }
        public void ForceFleeForTest() { Fleeing = true; }
        // Patrol (flag 0x200, waypoint 0; docs/xena-enemy-audit.md mode 1, 0x80020648): while patrolling, mode 1
        // walks (15) to the waypoint and only looks for the player: dist < 6000 within +-35 deg or dist < 1000
        // within +-90 deg, plus a line test 400 units above the feet (func_800A2E98, here a Physics line cast
        // against level colliders: APPROXIMATED) -> mode 2. Stopping at the waypoint is INFERRED (unknown).
        public bool Patrolling { get; private set; }
        public Vector3 PatrolTarget { get; private set; }
        public int Sightings { get; private set; }
        public string SightBlocker { get; private set; } // last collider that failed the line test (diagnostics)
        public void ConfigurePatrol(bool patrol, Vector3 target) { Patrolling = patrol; PatrolTarget = target; }
        public void ForcePatrolHoldForTest() { State = Mode.Hold; Patrolling = true; }
        public void ForceStateForTest(Mode mode) { State = mode; }
        private bool SeesTarget(int distance, int relative)
        {
            if (!((distance < 6000 && Mathf.Abs(relative) <= 398) || (distance < 1000 && Mathf.Abs(relative) <= 1024))) return false;
            Vector3 from = transform.position + Vector3.up, to = target.transform.position + Vector3.up;
            foreach (var hit in Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(target.transform) && !hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<PrototypeBanditBrain>() == null) { SightBlocker = hit.collider.name; return false; }
            return true;
        }
        public const int Notice = 9500, AttackRange = 700, Engage = 1700, LeaveWait = 2000, TooClose = 550, Turn = 64, Radius = 150;
        public const int Walk = 0x15, Run = 0x1A, Backstep = 0x23, SideLeft = 0x26, SideRight = 0x27;
        public static readonly int[] Attacks = { 0x3D, 0x3E, 0x3F, 0x50 };
        private static readonly int[] AttackWeights = { 3, 2, 3, 2, 1 };

        public Mode State { get; private set; }
        public int Slot { get; private set; } = PrototypeBandits.IdleSlot;
        public int Frame { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }
        public int Cooldown { get; private set; }
        public int AttacksStarted { get; private set; }
        public int Steps { get; private set; }
        public int MaxTurnObserved { get; private set; }
        public bool Active = true;
        // Damage (docs/xena-damage-audit.md): reactions 66/67/6D front, 6A rear;
        // deaths 8B low/8D mid, 8A rear; 8 invulnerable steps after a hit;
        // knockback 2*clamp(d,10,50) units/step for the first 8 frames (away from
        // the attacker: sign INFERRED); retaliation timer rand(90,220) with the
        // attack cooldown reset; dead bandits stay 300 frames after the clip, then
        // are removed (live counter). Hit height is INFERRED from the move
        // (kick = low, sword = mid): contact points are not ported yet.
        public bool Removed { get; private set; }
        public bool CorpseVisible { get; private set; }
        public void ShowCorpse(bool visible) { CorpseVisible = visible; foreach (Renderer item in GetComponentsInChildren<Renderer>(true)) item.enabled = visible; }
        public bool Dying { get; private set; }
        public int Invulnerable { get; private set; }
        public int Retaliation { get; private set; }
        public int Reactions { get; private set; }
        private int knockback, knockbackHeading, deathWait;
        private bool attackLanded;
        // Weapon contact points (PrototypeContactPoints) and the upper-body aim twist +0x11E (part 3 rotY,
        // docs/xena-contact-points.md 5): bearing a to Xena (0 outside +-0x384 or when not engaged),
        // target trunc(a/3), stepped 60 toward it only when farther than the step.
        private readonly Vector3[] points = new Vector3[7], previousPoints = new Vector3[7];
        private int previousMask, previousKey = -1;
        public int Twist { get; private set; }
        public int ContactHits { get; private set; }
        public const int ReactFront = 0x67, ReactLow = 0x6D, ReactRear = 0x6A, DieMid = 0x8D, DieLow = 0x8B, DieRear = 0x8A;
        private static bool IsReaction(int slot) => slot >= 0x66 && slot <= 0x7F;
        private static bool IsDeath(int slot) => slot >= 0x8A && slot <= 0x91;
        public bool TakeHit(int damage, Vector3 attacker, bool kick)
        {
            if (Removed || Health <= 0 || Invulnerable > 0) return false;
            Vector3 d = (attacker - transform.position) * 400;
            int toward = Mathf.RoundToInt(Mathf.Atan2(d.x, d.z) / (2 * Mathf.PI) * 4096) & 4095;
            int relative = (toward - heading) & 4095; bool rear = relative >= 0x400 && relative <= 0xC00;
            Health = Mathf.Max(0, Health - damage);
            knockback = 8; knockbackHeading = (toward + 2048) & 4095; knockbackUnits = 2 * Mathf.Clamp(damage, 10, 50);
            if (Health == 0) { Dying = true; Slot = rear ? DieRear : kick ? DieLow : DieMid; Frame = 0; deathWait = 0; return true; }
            Slot = rear ? ReactRear : kick ? ReactLow : ReactFront; Frame = 0; Invulnerable = 8; Reactions++;
            if (Retaliation == 0) { Retaliation = random.Next(90, 221); Cooldown = 0; }
            Patrolling = false; // a hit ends the patrol (INFERRED)
            // Hurt voice (0x8001F5F0, family 0): D_800C0BF4 list, 50 %.
            if (Health > 0) PrototypeSfx.PlayListAt(HurtVoices, transform.position, 2048);
            if (!Fleeing && Health < MaxHealth / 3 && random.Next(4) == 0) Fleeing = true;
            return true;
        }
        private int knockbackUnits;

        private XenaAssetPreview pose;
        private PrototypeMotor target;
        private Dictionary<int, MotionClip> motion;
        private int aggression, heading; // heading in original units (0..4095), Unity yaw = heading * 360/4096
        // Integer tick count: one 30/s AI step every 2nd 60 Hz tick (float accumulation
        // occasionally slipped a step to the 3rd tick and broke the drawn rhythm).
        private int ticks, ticksSinceStep;
        public float LastAlpha { get; private set; }
        public float LastPhase { get; private set; }
        public int LastTicksSinceStep { get; private set; }
        private float clock => ticksSinceStep * Time.fixedDeltaTime;
        // Render-only interpolation between the last two AI steps (30/s): the
        // drawn body glides, the root/actor state stays on the discrete step.
        private Vector3 previousPosition, currentPosition;
        // Distance the drawn body travels across the current AI step (tests).
        public float StepTravel => Vector3.Distance(previousPosition, currentPosition); private Quaternion previousRotation, currentRotation; private bool hasStep;
        private System.Random random;
        private static readonly List<PrototypeBanditBrain> all = new List<PrototypeBanditBrain>();

        public void Configure(XenaAssetPreview model, PrototypeMotor player, Dictionary<int, MotionClip> clips, int maxHealth, int aggressionPercent, int seed)
        {
            pose = model; target = player; motion = clips; Health = maxHealth; MaxHealth = maxHealth; aggression = Mathf.Clamp(aggressionPercent, 0, 99);
            random = new System.Random(seed); heading = Mathf.RoundToInt(transform.eulerAngles.y / 360f * 4096) & 4095;
            Fleeing = false; Patrolling = false; State = Mode.Idle; blockRolledFor = -1; Twist = 0; previousKey = -1;
            pose.enabled = false; Play(PrototypeBandits.IdleSlot);
        }
        private void OnEnable() { all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        public bool Configured => target != null && pose != null;
        private void FixedUpdate()
        {
            if (!Active || target == null || pose == null) return;
            var input = target.GetComponent<PrototypeInput>(); if (input != null && input.Paused) return;
            int perStep = Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime)); ticksSinceStep++;
            if (++ticks % perStep == 0)
            {
                ticksSinceStep = 0;
                if (transform.position != currentPosition || !hasStep) { previousPosition = currentPosition = transform.position; previousRotation = currentRotation = transform.rotation; hasStep = true; }
                previousPosition = currentPosition; previousRotation = currentRotation;
                Step();
                currentPosition = transform.position; currentRotation = transform.rotation;
            }
        }
        private void LateUpdate()
        {
            if (pose == null) return;
            Transform body = pose.transform;
            if (!hasStep || !Active || transform.position != currentPosition) { body.localPosition = Vector3.zero; body.localRotation = Quaternion.Euler(0, 180, 0); return; }
            // The AI step runs inside the 60 Hz tick, so up to 1/60 s can pass beyond the
            // 1/30 s step before it fires: extrapolate (<= 1.5) instead of holding a frame.
            float alpha = Mathf.Clamp((clock + Mathf.Clamp(Time.time - Time.fixedTime, 0, Time.fixedDeltaTime)) * 30, 0, 1.5f);
            LastAlpha = alpha; LastPhase = Time.time - Time.fixedTime; LastTicksSinceStep = ticksSinceStep;
            if (alpha > 1) { body.SetPositionAndRotation(Vector3.LerpUnclamped(previousPosition, currentPosition, alpha), Quaternion.SlerpUnclamped(previousRotation, currentRotation, alpha) * Quaternion.Euler(0, 180, 0)); return; }
            Quaternion rotation = Quaternion.Slerp(previousRotation, currentRotation, alpha);
            body.SetPositionAndRotation(Vector3.Lerp(previousPosition, currentPosition, alpha), rotation * Quaternion.Euler(0, 180, 0));
        }

        // Original horizontal distance: max(|dx|,|dz|) + 3/8 min, in units.
        private int Distance(out int relative)
        {
            Vector3 d = (target.transform.position - transform.position) * 400;
            int dx = Mathf.Abs(Mathf.RoundToInt(d.x)), dz = Mathf.Abs(Mathf.RoundToInt(d.z));
            int toward = Mathf.RoundToInt(Mathf.Atan2(d.x, d.z) / (2 * Mathf.PI) * 4096) & 4095;
            relative = ((toward - heading + 2048) & 4095) - 2048; // 0 = dead ahead
            return Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8;
        }
        private int Attackers()
        {
            int count = 0;
            foreach (var other in all) if (other != this && other.State == Mode.Attack && other.target == target) count++;
            return count;
        }

        private void Step()
        {
            Steps++;
            Sounds();
            if (Invulnerable > 0) Invulnerable--;
            if (Retaliation > 0) Retaliation--;
            if (Dying)
            {
                // Death clip, then 300 frames, then removal.
                if (Frame >= Frames(Slot) - 1 && ++deathWait >= 300)
                {
                    // Removed from the game (live counter); Remaster keeps the corpse
                    // in its last death pose, Original hides it as the original does.
                    Removed = true; enabled = false; ShowCorpse(PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster);
                    return;
                }
                Advance(); return;
            }
            if (IsReaction(Slot) && !ClipFinished()) { Advance(); return; }
            if (Cooldown > 0) Cooldown--;
            int distance = Distance(out int relative);
            if (AvoidEntry(distance)) { Advance(); return; }
            if (State == Mode.Avoid) { Avoid(distance, relative); Advance(); return; }
            bool busy = Array.IndexOf(Attacks, Slot) >= 0 || Slot == Backstep || Slot == SideLeft || Slot == SideRight;
            if (Fleeing)
            {
                // Turn away and run; far enough -> gone (despawn INFERRED).
                if (distance >= 14001) { Removed = true; enabled = false; gameObject.SetActive(false); return; }
                if (!busy || ClipFinished()) { TurnToward(((relative + 2048 + 2048) & 4095) - 2048); Play(Run); }
                Advance(); return;
            }
            if (State == Mode.Block)
            {
                if (Slot == BlockStart && ClipFinished()) Play(BlockHold);
                else if (Slot == BlockHold && Frame >= 9) State = distance < AttackRange ? Mode.Attack : Mode.Hold;
                Advance(); return;
            }
            if (CanBlock && distance < 1000 && (!busy || ClipFinished()))
            {
                // One roll per Xena attack (AI +0x3E guard, INFERRED); Xena attacking and in front.
                var combat = target.GetComponent<PrototypeCombat>(); int xs = combat != null && combat.Busy ? combat.Slot : -1;
                if (xs >= 0x3A && xs <= 0x65 && Mathf.Abs(relative) <= 0x31C && blockRolledFor != xs + 1000 * combat.Starts)
                {
                    blockRolledFor = xs + 1000 * combat.Starts;
                    if (random.Next(1, 101) <= BlockPercent) { State = Mode.Block; Play(BlockStart); Blocks++; TurnToward(relative); Advance(); return; }
                }
            }
            switch (State)
            {
                case Mode.Idle:
                    if (distance < Notice && (Mathf.Abs(relative) < 1536)) State = random.Next(2) == 0 ? Mode.Hold : Mode.Approach;
                    break;
                case Mode.Hold when Patrolling:
                    if (SeesTarget(distance, relative)) { Patrolling = false; State = Mode.Approach; Sightings++; }
                    break;
                case Mode.Hold:
                case Mode.Approach:
                    // The crowd test (0x8002012C) runs before the < 700 attack test: with two
                    // allies already attacking the target, a third bandit waits even when close.
                    if (distance < LeaveWait && Attackers() >= 2) State = Mode.Wait;
                    else if (distance < AttackRange) State = Mode.Attack;
                    else if (State == Mode.Hold && distance > Engage) State = Mode.Approach;
                    break;
                case Mode.Wait:
                    if (distance > LeaveWait) State = Mode.Hold;
                    else if (distance < AttackRange && Attackers() < 2) State = Mode.Attack;
                    break;
                case Mode.Attack:
                    if (distance > AttackRange && !busy) State = Mode.Hold;
                    break;
            }
            // Actions.
            if (!busy || ClipFinished())
            {
                if (State == Mode.Idle) Play(PrototypeBandits.IdleSlot);
                else if (State == Mode.Hold && Patrolling)
                {
                    Vector3 w = (PatrolTarget - transform.position) * 400; int wd = Mathf.Max(Mathf.Abs((int)w.x), Mathf.Abs((int)w.z)) + 3 * Mathf.Min(Mathf.Abs((int)w.x), Mathf.Abs((int)w.z)) / 8;
                    int toward = Mathf.RoundToInt(Mathf.Atan2(w.x, w.z) / (2 * Mathf.PI) * 4096) & 4095;
                    if (wd > 200) { TurnToward(((toward - heading + 2048) & 4095) - 2048); Play(Walk); } else Play(PrototypeBandits.IdleSlot);
                }
                else if (State == Mode.Hold || State == Mode.Approach) { TurnToward(relative); Play(State == Mode.Hold && distance < Engage ? Walk : Run); }
                else if (State == Mode.Wait) { TurnToward(relative); if (distance < Engage && distance > TooClose) { int r = random.Next(51); Play(r == 0 ? SideLeft : r == 1 ? SideRight : PrototypeBandits.IdleSlot); } else Play(distance <= TooClose ? Backstep : Walk); }
                else if (State == Mode.Attack)
                {
                    TurnToward(relative);
                    if (distance < TooClose) Play(Backstep);
                    else if (Cooldown == 0)
                    {
                        int pick = Weighted();
                        int q = (100 - aggression) / 5; Cooldown = random.Next(4 * q, 6 * q + 1);
                        if (pick < Attacks.Length) { Play(Attacks[pick]); AttacksStarted++; } else Play(PrototypeBandits.IdleSlot);
                    }
                    else Play(PrototypeBandits.IdleSlot);
                }
            }
            else if (State != Mode.Idle && (Slot == Walk || Slot == Run)) TurnToward(relative);
            Advance();
        }
        private int Weighted()
        {
            int roll = random.Next(11);
            for (int i = 0; i < AttackWeights.Length; i++) { if (roll < AttackWeights[i]) return i; roll -= AttackWeights[i]; }
            return AttackWeights.Length - 1;
        }
        private void TurnToward(int relative)
        {
            int turn = Mathf.Clamp(relative, -Turn, Turn); heading = (heading + turn) & 4095;
            MaxTurnObserved = Mathf.Max(MaxTurnObserved, Mathf.Abs(turn));
        }
        private bool Looping => !Dying && (Slot == PrototypeBandits.IdleSlot || Slot == Walk || Slot == Run);
        // Bandit sounds (docs/xena-sfx.md): script swings rand(0,2), footsteps rand(0x2E,0x32) vol 31, death
        // thud 0x2E, XA 0x1000 at 0x7B f10; barks: alert 0x102D when leaving mode 0, approach list at
        // 0xCC/4096 per step (mode 2), attack taunt lists at 0x14/4096 (mode 5). Barks start only while no
        // XA clip plays (one CD stream; INFERRED pacing).
        public static readonly int[] HurtVoices = { 0x1036, 0x1037, 0x1038, 0x1048, 0x104C, 0x104D, 0x105A, 0x105B, 0x1061 };
        private static readonly int[] ApproachBarks = { 0x1039, 0x103A, 0x1049, 0x104A, 0x104B, 0x10AC, 0x10B2 };
        private static readonly int[] Taunts = { 0x104E, 0x104F, 0x1050, 0x1051, 0x1052 }, Taunts2 = { 0x1078, 0x1079, 0x107A, 0x1080, 0x1081, 0x1082, 0x1083 };
        private static readonly System.Collections.Generic.Dictionary<int, int[]> HurtVoice = new System.Collections.Generic.Dictionary<int, int[]> { { 0x7B, new[] { 10 } } };
        private int soundSlot = -1, soundFrame = -1; private Mode soundState;
        private void Sounds()
        {
            var sfx = PrototypeSfx.Instance; Vector3 at = transform.position;
            if (PrototypeSfxEvents.Crossed(PrototypeSfxEvents.BanditSword, soundSlot, soundFrame, Slot, Frame)) PrototypeSfx.PlayAt(sfx.Rand(0, 2), at);
            if (PrototypeSfxEvents.Crossed(PrototypeSfxEvents.BanditSteps, soundSlot, soundFrame, Slot, Frame)) PrototypeSfx.PlayAt(sfx.Rand(0x2E, 0x32), at, 60, 31);
            if (Dying && PrototypeSfxEvents.Crossed(PrototypeSfxEvents.BanditThud, soundSlot, soundFrame, Slot, Frame)) PrototypeSfx.PlayAt(0x2E, at);
            if (PrototypeSfxEvents.Crossed(HurtVoice, soundSlot, soundFrame, Slot, Frame)) PrototypeSfx.PlayAt(0x1000, at);
            if (!Dying && !PrototypeSfx.XaPlaying)
            {
                if (soundState == Mode.Idle && State != Mode.Idle) PrototypeSfx.PlayAt(0x102D, at);
                else if (State == Mode.Approach && sfx.Rand(0, 4095) < 0xCC) PrototypeSfx.PlayAt(ApproachBarks[sfx.Rand(0, ApproachBarks.Length - 1)], at);
                else if (State == Mode.Attack && sfx.Rand(0, 4095) < 0x14) PrototypeSfx.PlayAt(Taunts[sfx.Rand(0, Taunts.Length - 1)], at);
                else if (State == Mode.Attack && sfx.Rand(0, 4095) < 0x14) PrototypeSfx.PlayAt(Taunts2[sfx.Rand(0, Taunts2.Length - 1)], at);
            }
            soundSlot = Slot; soundFrame = Frame; soundState = State;
        }
        private static int Metric(Vector3 a, Vector3 b)
        {
            int dx = Mathf.Abs(Mathf.RoundToInt((a.x - b.x) * 400)), dz = Mathf.Abs(Mathf.RoundToInt((a.z - b.z) * 400));
            return Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8;
        }
        // func_80070BD4: first live bandit whose span overlaps and that the step would touch (< 300), unless it
        // already overlaps at the current position (escape rule).
        private PrototypeBanditBrain BanditAt(Vector3 next)
        {
            foreach (var other in all)
            {
                if (other == this || other == null || other.Removed || other.Dying || other.Health <= 0 || !other.gameObject.activeInHierarchy) continue;
                if (Mathf.Abs(other.transform.position.y - next.y) * 400 >= 750) continue;
                if (Metric(next, other.transform.position) < 2 * Radius && Metric(transform.position, other.transform.position) >= 2 * Radius) return other;
            }
            return null;
        }
        private bool SkipAvoidCheck => IsReaction(Slot) || Dying || (Slot >= 0x3A && Slot <= 0x65);
        private bool AvoidEntry(int targetDistance)
        {
            bool blocked = moverBlocked; moverBlocked = false;
            if (SkipAvoidCheck || !blocked || LastContact == null || LastContact.Removed) return false;
            int goal = Patrolling ? Metric(transform.position, PatrolTarget) : targetDistance;
            if (goal < 3001) return false;
            if (LastContact.State == Mode.Avoid) return true;
            // Already avoiding: the original stays frozen until the walk family switches clip (0x15 -> 0x16, trigger
            // UNKNOWN, docs/xena-bandit-avoid.md 3); the port lets the avoid action steer instead (APPROXIMATED).
            if (State == Mode.Avoid) return false;
            if (LastContact.State == Mode.Idle || LastContact.Patrolling) { LastContact.State = Mode.Hold; LastContact.Patrolling = false; } // woken (CODE)
            if (State != Mode.Avoid) AvoidEntries++;
            State = Mode.Avoid; return true;
        }
        private void Avoid(int targetDistance, int relative)
        {
            var b = LastContact; if (b == null || b.Removed) { State = Mode.Hold; return; }
            Vector3 toB = (b.transform.position - transform.position) * 400; toB.y = 0;
            float h = heading / 4096f * 2 * Mathf.PI; var f = new Vector3(Mathf.Sin(h), 0, Mathf.Cos(h)); var l = new Vector3(Mathf.Cos(h), 0, -Mathf.Sin(h));
            float dot = Vector3.Dot(toB, f), lat = Vector3.Dot(toB, l); int d = Metric(transform.position, b.transform.position);
            int goal = relative;
            if (Patrolling) { Vector3 w = (PatrolTarget - transform.position) * 400; goal = ((Mathf.RoundToInt(Mathf.Atan2(w.x, w.z) / (2 * Mathf.PI) * 4096) - heading + 2048) & 4095) - 2048; }
            const int R = Radius + 700;
            if (dot < 0 && d > R) TurnToward(goal);
            else if (Mathf.Abs(lat) < R) heading = (heading + (lat < 0 ? Turn : -Turn)) & 4095;
            else { TurnToward(goal); State = Mode.Hold; }
            Play(d < Engage ? Walk : Run);
        }
        private void Contacts()
        {
            // Clip frame n (step counter) uses SMA frame n - 1 at the root before this step's movement.
            int mask = PrototypeContactPoints.Pose("bandit", Slot, Frame - 1, transform.position, heading * 360f / 4096, Twist, points, out int raw);
            int key = Slot * 1000 + Frame; bool hasPrevious = previousKey == key - 1;
            if (raw == 0 || mask == 0) { previousKey = -1; return; }
            var damage = target.GetComponent<PrototypeOriginalDamage>();
            if (!attackLanded && damage != null)
            {
                bool found = false; Vector3 point = default;
                for (int i = 0; i < points.Length; i++)
                    if ((mask >> i & 1) != 0 && PrototypeContactPoints.Test(points[i], previousPoints[i], hasPrevious && (previousMask >> i & 1) != 0,
                        target.transform.position, Radius, Radius, 750, out var hit)) { found = true; point = hit; }
                if (found) { ContactHits++; if (damage.TakeHit(Mathf.Abs(raw), transform.position, point, Slot)) attackLanded = true; }
            }
            Array.Copy(points, previousPoints, points.Length); previousMask = mask; previousKey = key;
        }
        private bool ClipFinished() => !Looping && Frame >= Frames(Slot) - 1;
        private int Frames(int slot) => motion.TryGetValue(slot, out var clip) ? clip.frames : 1;
        private void Play(int slot) { if (slot == Slot && (Looping || !ClipFinished())) return; Slot = slot; Frame = 0; }

        private void Advance()
        {
            // Attack windows: damage Xena once per attack inside start+1..end.
            if (Frame == 0) attackLanded = false;
            if (!Dying && !IsReaction(Slot))
            {
                Distance(out int bearing);
                int aim = State != Mode.Idle && !Fleeing && Mathf.Abs(bearing) <= 0x384 ? bearing / 3 : 0;
                if (Mathf.Abs(aim - Twist) > 60) Twist += aim > Twist ? 60 : -60;
            }
            if (!Dying && PrototypeContactPoints.HasClip("bandit", Slot)) Contacts();
            else if (!Dying && !attackLanded && PrototypeOriginalDamage.BanditWindows.TryGetValue(Slot, out var window) && Frame > window.start && Frame <= window.end)
            {
                Vector3 offset = target.transform.position - transform.position; float dy = offset.y; offset.y = 0;
                var damage = target.GetComponent<PrototypeOriginalDamage>();
                if (damage != null && offset.magnitude < (AttackRange + 40) / 400f && Mathf.Abs(dy) * 400 < 750
                    && Vector3.Angle(Quaternion.Euler(0, heading * 360f / 4096, 0) * Vector3.forward, offset) <= 60 && damage.TakeHit(window.damage, transform.position, null, Slot)) attackLanded = true;
            }
            // Root motion of the current frame (last change at or before it).
            int speed = 0, direction = 2048;
            if (motion.TryGetValue(Slot, out var clip) && clip.moves != null)
                foreach (var move in clip.moves) if (move.frame <= Frame) { speed = move.speed; direction = move.direction; }
            if (knockback > 0 && Frame < 8) { speed = knockbackUnits; direction = 2048 + ((knockbackHeading - heading) & 4095); knockback--; }
            if (speed != 0)
            {
                float angle = (heading + direction - 2048) / 4096f * 2 * Mathf.PI;
                Vector3 step = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * speed / 400f;
                Vector3 next = transform.position + step;
                float ground = target.GroundHeight(next + Vector3.up * 0.5f);
                Vector3 offset = next - target.transform.position; offset.y = 0;
                bool blocked = float.IsNegativeInfinity(ground) || ground - transform.position.y > 0.75f || offset.magnitude < 2 * Radius / 400f;
                var contact = BanditAt(next);
                if (contact != null) { LastContact = contact; blocked = true; }
                moverBlocked = blocked;
                if (!blocked) transform.position = new Vector3(next.x, ground, next.z);
            }
            transform.rotation = Quaternion.Euler(0, heading * 360f / 4096, 0);
            pose.SampleSlotFrame(Slot, Frame);
            Frame = Looping ? (Frame + 1) % Mathf.Max(1, Frames(Slot)) : Mathf.Min(Frame + 1, Frames(Slot) - 1);
        }
    }
}
