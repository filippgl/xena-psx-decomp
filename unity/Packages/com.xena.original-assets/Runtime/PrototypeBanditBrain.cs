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
    // per frame). NOT implemented (unknown in the original): damage, hit
    // reactions/death, block (mode 4), patrol waypoints, avoid (mode 6), flee.
    public sealed class PrototypeBanditBrain : MonoBehaviour
    {
        [Serializable] public sealed class MotionFile { public MotionClip[] clips; }
        [Serializable] public sealed class MotionClip { public int slot, frames; public Move[] moves; }
        [Serializable] public sealed class Move { public int frame, speed, direction; }
        public enum Mode { Idle = 0, Hold = 1, Approach = 2, Wait = 3, Attack = 5 }
        public const int Notice = 9500, AttackRange = 700, Engage = 1700, LeaveWait = 2000, TooClose = 550, Turn = 64, Radius = 150;
        public const int Walk = 0x15, Run = 0x1A, Backstep = 0x23, SideLeft = 0x26, SideRight = 0x27;
        public static readonly int[] Attacks = { 0x3D, 0x3E, 0x3F, 0x50 };
        private static readonly int[] AttackWeights = { 3, 2, 3, 2, 1 };

        public Mode State { get; private set; }
        public int Slot { get; private set; } = PrototypeBandits.IdleSlot;
        public int Frame { get; private set; }
        public int Health { get; private set; }
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
            return true;
        }
        private int knockbackUnits;

        private XenaAssetPreview pose;
        private PrototypeMotor target;
        private Dictionary<int, MotionClip> motion;
        private int aggression, heading; // heading in original units (0..4095), Unity yaw = heading * 360/4096
        private float clock;
        // Render-only interpolation between the last two AI steps (30/s): the
        // drawn body glides, the root/actor state stays on the discrete step.
        private Vector3 previousPosition, currentPosition; private Quaternion previousRotation, currentRotation; private bool hasStep;
        private System.Random random;
        private static readonly List<PrototypeBanditBrain> all = new List<PrototypeBanditBrain>();

        public void Configure(XenaAssetPreview model, PrototypeMotor player, Dictionary<int, MotionClip> clips, int maxHealth, int aggressionPercent, int seed)
        {
            pose = model; target = player; motion = clips; Health = maxHealth; aggression = Mathf.Clamp(aggressionPercent, 0, 99);
            random = new System.Random(seed); heading = Mathf.RoundToInt(transform.eulerAngles.y / 360f * 4096) & 4095;
            pose.enabled = false; Play(PrototypeBandits.IdleSlot);
        }
        private void OnEnable() { all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        public bool Configured => target != null && pose != null;
        private void FixedUpdate()
        {
            if (!Active || target == null || pose == null) return;
            var input = target.GetComponent<PrototypeInput>(); if (input != null && input.Paused) return;
            clock += Time.fixedDeltaTime;
            while (clock >= 1f / 30)
            {
                clock -= 1f / 30;
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
            float alpha = Mathf.Clamp01((clock + Mathf.Clamp(Time.time - Time.fixedTime, 0, Time.fixedDeltaTime)) * 30);
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
            bool busy = Array.IndexOf(Attacks, Slot) >= 0 || Slot == Backstep || Slot == SideLeft || Slot == SideRight;
            switch (State)
            {
                case Mode.Idle:
                    if (distance < Notice && (Mathf.Abs(relative) < 1536)) State = random.Next(2) == 0 ? Mode.Hold : Mode.Approach;
                    break;
                case Mode.Hold:
                case Mode.Approach:
                    if (distance < AttackRange) State = Mode.Attack;
                    else if (distance < LeaveWait && Attackers() >= 2) State = Mode.Wait;
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
        private bool ClipFinished() => !Looping && Frame >= Frames(Slot) - 1;
        private int Frames(int slot) => motion.TryGetValue(slot, out var clip) ? clip.frames : 1;
        private void Play(int slot) { if (slot == Slot && (Looping || !ClipFinished())) return; Slot = slot; Frame = 0; }

        private void Advance()
        {
            // Attack windows: damage Xena once per attack inside start+1..end.
            if (Frame == 0) attackLanded = false;
            if (!Dying && !attackLanded && PrototypeOriginalDamage.BanditWindows.TryGetValue(Slot, out var window) && Frame > window.start && Frame <= window.end)
            {
                Vector3 offset = target.transform.position - transform.position; float dy = offset.y; offset.y = 0;
                var damage = target.GetComponent<PrototypeOriginalDamage>();
                if (damage != null && offset.magnitude < (AttackRange + 40) / 400f && Mathf.Abs(dy) * 400 < 750
                    && Vector3.Angle(Quaternion.Euler(0, heading * 360f / 4096, 0) * Vector3.forward, offset) <= 60 && damage.TakeHit(window.damage, transform.position)) attackLanded = true;
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
                if (!blocked) transform.position = new Vector3(next.x, ground, next.z);
            }
            transform.rotation = Quaternion.Euler(0, heading * 360f / 4096, 0);
            pose.SampleSlotFrame(Slot, Frame);
            Frame = Looping ? (Frame + 1) % Mathf.Max(1, Frames(Slot)) : Mathf.Min(Frame + 1, Frames(Slot) - 1);
        }
    }
}
