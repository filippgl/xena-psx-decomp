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
        private int firstAction, secondAction, reservedSlot = -1, reservedFrame;
        private int heldEvade;
        private Vector3 evadeVelocity;
        private int spinLap, spinBudget;
        private bool spinExtend;
        public bool IsSpinning => slot >= 0x4c && slot <= 0x4e;
        public bool IsEvading => slot == 0x28 || slot == 0x29 || slot == 0x2a || slot == 0x3c;
        public bool DrivesMotion => IsEvading || slot == 0x3e;
        public Vector3 ActionVelocity => DrivesMotion ? evadeVelocity : Vector3.zero;
        public bool Busy => slot >= 0;
        public bool IsBlocking => slot == 0x80;
        public int Slot => slot;
        public int Frame => Mathf.FloorToInt(time * 30);
        public float PoseTime => time;
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
                case 0x61: return 18; case 0x62: return 31; case 0x5a: return 22;
                case 0x57: return 6; case 0x56: return 3; case 0x4f: return 40;
                case 0x55: return 23; case 0x53: return 25;
                case 0x28: return 24; case 0x29: return 25; case 0x2a: return 38;
                case 0x3c: return 41; case 0x3e: return 40; case 0x5c: return 39; case 0x46: return 28;
                case 0x4c: return 18; case 0x4d: return 7; case 0x4e: return 31; default: return 0; }
        }
        private int Follow(int action)
        {
            if (secondAction == 0)
            {
                if (slot == 0x51) return action == 1 ? 0x52 : 0x57;
                if (slot == 0x61) return action == 2 ? 0x62 : 0x56;
            }
            if (slot == 0x52 && action == 1) return firstAction == 1 ? 0x3a : 0x53;
            if (slot == 0x62)
            {
                if (firstAction == 2) return action == 2 ? 0x5a : 0x55;
                if (action == 1) return 0x4f;
            }
            return -1;
        }
        private int ChainFrame(int next)
        {
            switch (slot) { case 0x51: case 0x52: return 10;
                case 0x61: return next == 0x56 ? 11 : 9;
                case 0x62: return next == 0x4f ? 31 : next == 0x55 ? 19 : 22;
                default: return int.MaxValue; }
        }
        private void Begin(int value, int frame = 0) { slot = value; time = frame / 30f; struck.Clear(); }
        private void ClearChain() { firstAction = secondAction = 0; reservedSlot = -1; }
        public void ResetState()
        {
            slot = -1; time = 0; clock = 0; buffer.Clear(); struck.Clear(); input.ClearPending();
            ClearChain();
            heldEvade = 0; evadeVelocity = Vector3.zero;
            spinLap = spinBudget = 0; spinExtend = false;
            Health = 100; BlockedHits = 0; if (Target != null) Target.ResetTarget();
        }
        public void Tick(float dt, bool grounded, float cameraYaw)
        {
            clock += dt;
            int action = input.ConsumeAttack();
            Vector3 requested = Quaternion.Euler(0, cameraYaw, 0) * new Vector3(input.Move.x, 0, input.Move.y);
            Vector3 local = transform.InverseTransformDirection(requested);
            int evade = !input.Block || requested.sqrMagnitude < 0.25f ? 0
                : Mathf.Abs(local.x) > Mathf.Abs(local.z) ? (local.x < 0 ? 0x28 : 0x29) : local.z < -0.5f ? 0x2a : 0;
            bool evadePressed = evade != 0 && evade != heldEvade; heldEvade = evade;
            while (buffer.Count > 0 && buffer.Peek().expires < clock) buffer.Dequeue();
            if (!grounded) { slot = -1; buffer.Clear(); ClearChain(); return; }
            bool canStart = slot < 0 || IsBlocking;
            bool running = input.Sprint && requested.sqrMagnitude >= 0.25f;
            // A short stagger between the two chord buttons may replace the first
            // two pre-contact frames of a single attack. No individual hit escapes.
            bool earlySingle = (slot == 0x51 || slot == 0x61) && secondAction == 0 && Frame <= 2;
            if (action == 3 && !input.Block && !running && (canStart || earlySingle))
            { buffer.Clear(); ClearChain(); Begin(0x4c); spinLap = 0; spinBudget = 6; spinExtend = false; action = 0; }
            if (IsSpinning)
            {
                buffer.Clear();
                // Native4D runs in pairs. One fresh kick edge per pair may extend
                // another pair; holding buttons does not extend. Native budget=6.
                if (input.Block) spinExtend = false;
                else if (slot == 0x4d && (action == 2 || action == 3) && spinBudget > 0 && !spinExtend)
                { spinExtend = true; spinBudget--; }
                if (Frame >= Frames(slot))
                {
                    if (slot == 0x4c) Begin(0x4d);
                    else if (slot == 0x4d)
                    {
                        spinLap++;
                        if (spinLap < 2) Begin(0x4d);
                        else if (spinExtend) { spinLap = 0; spinExtend = false; Begin(0x4d); }
                        else Begin(0x4e);
                    }
                    else { slot = -1; time = 0; if (input.Block) Begin(0x80); }
                }
                if (Busy) time += dt;
                return;
            }
            if (canStart && input.Block && action != 0)
            {
                buffer.Clear(); ClearChain(); Begin(action == 2 ? 0x5c : 0x46);
                action = 0; // A guard combination consumes its single attack edge.
            }
            else if (slot < 0 && !input.Block && input.Sprint && requested.sqrMagnitude >= 0.25f && action != 0)
            {
                buffer.Clear(); ClearChain(); Begin(action == 2 ? 0x3c : 0x3e);
                transform.rotation = Quaternion.LookRotation(requested);
                evadeVelocity = requested.normalized * ((slot == 0x3c ? 8f : 5f) / (Frames(slot) / 30f));
                action = 0;
            }
            else if (evadePressed && canStart)
            {
                buffer.Clear(); ClearChain(); Begin(evade);
                // Original clips/durations; displacement is provisional motor tuning.
                evadeVelocity = evade == 0x2a ? -transform.forward * (4f / (38f / 30))
                    : transform.right * (evade == 0x28 ? -1 : 1) * (2.35f / (Frames(evade) / 30f));
            }
            if (IsEvading)
            {
                buffer.Clear();
                if (Frame < Frames(slot)) { time += dt; return; }
                slot = -1; time = 0;
            }
            if (input.Block)
            {
                buffer.Clear(); // Never release a queued attack after a held guard.
                reservedSlot = -1;
                if (slot < 0) Begin(0x80);
            }
            else
            {
                if (IsBlocking) { slot = -1; time = 0; }
                if ((action == 1 || action == 2) && buffer.Count < 2) buffer.Enqueue(new Intent { action = action, expires = clock + 0.65f });
            }
            if (!IsBlocking && slot >= 0)
            {
                // A recognized native combo request stays reserved through the current
                // animation; expiry only applies to input that has not been accepted.
                if (reservedSlot < 0 && buffer.Count > 0)
                {
                    int follow = Follow(buffer.Peek().action);
                    if (follow >= 0)
                    {
                        int accepted = buffer.Dequeue().action;
                        reservedSlot = follow; reservedFrame = ChainFrame(follow);
                        if (secondAction == 0) secondAction = accepted;
                    }
                }
                if (reservedSlot >= 0 && Frame >= reservedFrame)
                { int next = reservedSlot; reservedSlot = -1; Begin(next); }
                else if (Frame >= Frames(slot))
                {
                    // The original bridges replace the first poses of the next clip.
                    if (slot == 0x57) Begin(0x62, 5);
                    else if (slot == 0x56) Begin(0x52, 2);
                    else { slot = -1; time = 0; ClearChain(); }
                }
            }
            if (slot < 0)
            {
                if (input.Block) Begin(0x80);
                else if (buffer.Count > 0)
                {
                    ClearChain(); firstAction = buffer.Dequeue().action;
                    // A backward attack follows the requested world direction before
                    // locking movement; it uses the existing original sword/kick clip.
                    if (requested.sqrMagnitude >= 0.25f && Vector3.Dot(transform.forward, requested.normalized) < -0.5f)
                        transform.rotation = Quaternion.LookRotation(requested);
                    Begin(firstAction == 1 ? 0x51 : 0x61);
                }
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
                if (!IsEvading && slot != 0x56 && slot != 0x57 && slot != 0x4c && slot != 0x4e && Frame >= contact && Frame <= contact + 2) StrikePracticeTargets();
            }
            return true;
        }
        private void StrikePracticeTargets()
        {
            bool kick = slot == 0x61 || slot == 0x62 || slot == 0x5a || slot == 0x5c || slot == 0x46;
            float reach = kick ? 1.25f : 1.65f;
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            foreach (Collider collider in Physics.OverlapSphere(origin, reach, ~(1 << 2), QueryTriggerInteraction.Collide))
            {
                var target = collider.GetComponentInParent<PrototypePracticeTarget>();
                if (target == null || struck.Contains(target) || target.Health <= 0) continue;
                Vector3 offset = target.transform.position - origin; offset.y = 0;
                if (slot != 0x5c && slot != 0x4d && Vector3.Dot(transform.forward, offset.normalized) < 0.5f) continue;
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
