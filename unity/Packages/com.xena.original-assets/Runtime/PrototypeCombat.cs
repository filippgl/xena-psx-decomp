using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Original clips/observed chains; practice damage, reach and hit windows are provisional.
    public sealed class PrototypeCombat : MonoBehaviour
    {
        private struct Intent { public int action; public float expires; }
        private readonly Queue<Intent> buffer = new Queue<Intent>();
        private readonly HashSet<PrototypePracticeTarget> struck = new HashSet<PrototypePracticeTarget>();
        private PrototypeInput input;
        private XenaAssetPreview visual;
        private PrototypeMotor motor;
        private float clock, time;
        private int slot = -1;
        public bool Busy => slot >= 0;
        public bool IsBlocking => slot == 0x80;
        public int Slot => slot;
        public int Frame => Mathf.FloorToInt(time * 30);
        public int Health { get; private set; } = 100;
        public int BlockedHits { get; private set; }
        public PrototypePracticeTarget Target { get; private set; }
        private void Awake()
        {
            input = GetComponent<PrototypeInput>(); motor = GetComponent<PrototypeMotor>();
            visual = GetComponentInChildren<XenaAssetPreview>();
        }
        private void Start()
        {
            Vector3 position = transform.position + Vector3.right * 3;
            float floor = motor.GroundHeight(position);
            if (!float.IsNegativeInfinity(floor)) position.y = floor;
            Target = PrototypePracticeTarget.Create(position, this);
        }
        private static int Frames(int value)
        {
            switch (value) { case 0x51: return 15; case 0x52: return 20; case 0x3a: return 23;
                case 0x61: return 18; case 0x62: return 31; case 0x5a: return 22; default: return 0; }
        }
        private static int Follow(int value, int action)
        {
            if (action == 1) { if (value == 0x51) return 0x52; if (value == 0x52) return 0x3a; }
            if (action == 2) { if (value == 0x61) return 0x62; if (value == 0x62) return 0x5a; }
            return -1;
        }
        private static int ChainFrame(int value)
        {
            switch (value) { case 0x51: case 0x52: return 10; case 0x61: return 9; case 0x62: return 22; default: return int.MaxValue; }
        }
        private void Begin(int value) { slot = value; time = 0; struck.Clear(); }
        public void ResetState()
        {
            slot = -1; time = 0; clock = 0; buffer.Clear(); struck.Clear(); input.ClearPending();
            Health = 100; BlockedHits = 0; if (Target != null) Target.ResetTarget();
        }
        public void Tick(float dt, bool grounded)
        {
            clock += dt;
            int action = input.ConsumeAttack();
            while (buffer.Count > 0 && buffer.Peek().expires < clock) buffer.Dequeue();
            if (!grounded) { slot = -1; buffer.Clear(); return; }
            if (input.Block)
            {
                buffer.Clear(); // Never release a queued attack after a held guard.
                if (slot < 0) Begin(0x80);
            }
            else
            {
                if (IsBlocking) { slot = -1; time = 0; }
                if (action != 0 && buffer.Count < 2) buffer.Enqueue(new Intent { action = action, expires = clock + 0.65f });
            }
            if (!IsBlocking && slot >= 0)
            {
                int follow = buffer.Count == 0 ? -1 : Follow(slot, buffer.Peek().action);
                if (follow >= 0 && Frame >= ChainFrame(slot)) { buffer.Dequeue(); Begin(follow); }
                else if (Frame >= Frames(slot)) { slot = -1; time = 0; }
            }
            if (slot < 0)
            {
                if (input.Block) Begin(0x80);
                else if (buffer.Count > 0) Begin(buffer.Dequeue().action == 1 ? 0x51 : 0x61);
            }
            if (Busy) time += dt;
        }
        public bool PresentPose()
        {
            if (!Busy) return false;
            if (IsBlocking) visual.SampleSlot(slot, time);
            else
            {
                visual.SampleSlotFrame(slot, Mathf.Min(Frame, Frames(slot) - 1));
                // These contact frames/radii affect only the practice target.
                int contact = slot == 0x51 ? 5 : slot == 0x61 ? 6 : Frames(slot) / 2;
                if (Frame >= contact && Frame <= contact + 2) StrikePracticeTargets();
            }
            return true;
        }
        private void StrikePracticeTargets()
        {
            bool kick = slot == 0x61 || slot == 0x62 || slot == 0x5a;
            float reach = kick ? 1.25f : 1.65f;
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            foreach (Collider collider in Physics.OverlapSphere(origin, reach, ~(1 << 2), QueryTriggerInteraction.Collide))
            {
                var target = collider.GetComponentInParent<PrototypePracticeTarget>();
                if (target == null || struck.Contains(target) || target.Health <= 0) continue;
                Vector3 offset = target.transform.position - origin; offset.y = 0;
                if (Vector3.Dot(transform.forward, offset.normalized) < 0.5f) continue;
                if (Physics.Linecast(origin, target.transform.position, out RaycastHit hit, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PrototypePracticeTarget>() != target) continue;
                struck.Add(target); target.ReceiveHit(kick ? 15 : 20);
            }
        }
        public void ReceivePracticeHit(int damage, Vector3 attacker)
        {
            Vector3 direction = attacker - transform.position; direction.y = 0;
            if (IsBlocking && Vector3.Dot(transform.forward, direction.normalized) > 0.2f) BlockedHits++;
            else Health = Mathf.Max(0, Health - damage);
        }
    }
}
