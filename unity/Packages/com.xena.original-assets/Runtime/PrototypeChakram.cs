using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Original mesh and throw/catch poses. Flight tuning is provisional, not PS1 physics parity.
    public sealed class PrototypeChakram : MonoBehaviour
    {
        public enum Mode { Ready, Aiming, Throwing, Flight, Catching }
        public Mode State { get; private set; }
        public bool Active => State != Mode.Ready;
        public bool Flying { get; private set; }
        public bool Returning { get; private set; }
        public int Throws { get; private set; }
        public int Catches { get; private set; }
        public int WallContacts { get; private set; }
        public int Frame => Mathf.FloorToInt(time * 30);
        public Vector3 ProjectilePosition => projectile == null ? transform.position : projectile.transform.position;
        public Vector3 FlightDirection => direction;
        public bool Visible => projectile != null && projectile.activeSelf;
        private PrototypeInput input;
        private PrototypeCombat combat;
        private XenaAssetPreview visual;
        private GameObject projectile;
        private float time, flightAge, spin;
        private bool released;
        private Vector3 direction;
        private readonly List<Vector3> trail = new List<Vector3>();
        private readonly HashSet<PrototypePracticeTarget> struck = new HashSet<PrototypePracticeTarget>();
        private int returnIndex;
        private void Awake()
        {
            input = GetComponent<PrototypeInput>(); combat = GetComponent<PrototypeCombat>();
            visual = GetComponentInChildren<XenaAssetPreview>();
            GameObject original = Resources.Load<GameObject>("Chakram/Chakram");
            if (original == null) throw new InvalidOperationException("Standalone original Chakram resource unavailable; run prepare_movement_prototype.mjs");
            projectile = Instantiate(original); projectile.name = "OriginalChakramProjectile";
            foreach (Transform part in projectile.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 2;
            projectile.GetComponent<XenaAssetPreview>().enabled = false;
            projectile.SetActive(false);
        }
        public void ResetState()
        {
            State = Mode.Ready; Flying = Returning = released = false; time = flightAge = spin = 0;
            Throws = Catches = WallContacts = 0; trail.Clear(); struck.Clear();
            if (projectile != null) projectile.SetActive(false);
        }
        private void SetMode(Mode mode) { State = mode; time = 0; }
        public void Tick(float dt, bool grounded, float yaw, float pitch)
        {
            if (!Active && input.Chakram && grounded && !combat.Busy && !input.Block) SetMode(Mode.Aiming);
            if (!Active) return;
            int attack = input.ConsumeAttack(); input.ConsumeJump();
            if (State == Mode.Aiming)
            {
                if (!input.Chakram) { SetMode(Mode.Ready); return; }
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0, yaw, 0), 720 * dt);
                if (attack == 1 || attack == 3) { released = false; SetMode(Mode.Throwing); }
            }
            else if (State == Mode.Throwing)
            {
                // Original hand mesh becomes hidden at zero-based source frame16.
                if (!released && Frame >= 16)
                {
                    released = true; Flying = true; Returning = false; flightAge = 0; Throws++; struck.Clear(); trail.Clear();
                    projectile.transform.position = transform.position + Vector3.up * 1.1f;
                    direction = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
                    trail.Add(projectile.transform.position); projectile.SetActive(true);
                }
                if (Frame >= 21) SetMode(Mode.Flight);
            }
            else if (State == Mode.Catching && Frame >= 19) { SetMode(Mode.Ready); input.ClearPending(); }
            if (Flying)
            {
                flightAge += dt;
                if (!Returning && (!input.Chakram || flightAge >= 2.2f)) BeginReturn();
                if (Returning) Return(dt);
                else
                {
                    Vector3 desired = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
                    direction = Vector3.RotateTowards(direction, desired, 150 * Mathf.Deg2Rad * dt, 0);
                    Advance(direction * (14 * dt));
                }
                spin += 900 * dt; projectile.transform.rotation = Quaternion.Euler(65, spin, 0);
            }
            if (Active) time += dt;
        }
        private void Advance(Vector3 step)
        {
            Vector3 start = projectile.transform.position;
            RaycastHit[] hits = Physics.SphereCastAll(start, 0.12f, step.normalized, step.magnitude, ~(1 << 2), QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<PrototypeMotor>() == GetComponent<PrototypeMotor>()) continue;
                var target = hit.collider.GetComponentInParent<PrototypePracticeTarget>();
                if (target != null)
                { if (target.Health > 0 && struck.Add(target)) target.ReceiveHit(35); }
                else if (!hit.collider.isTrigger)
                {
                    projectile.transform.position = start + step.normalized * Mathf.Max(0, hit.distance - 0.03f);
                    trail.Add(projectile.transform.position); WallContacts++; BeginReturn(); return;
                }
            }
            projectile.transform.position += step; trail.Add(projectile.transform.position);
        }
        private void BeginReturn() { Returning = true; returnIndex = trail.Count - 1; }
        private void Return(float dt)
        {
            // Retrace the outgoing path so the prototype does not cut through walls on return.
            float distance = 20 * dt;
            while (distance > 0 && returnIndex >= 0)
            {
                Vector3 next = trail[returnIndex]; float gap = Vector3.Distance(projectile.transform.position, next);
                if (gap <= distance) { projectile.transform.position = next; distance -= gap; returnIndex--; }
                else { projectile.transform.position = Vector3.MoveTowards(projectile.transform.position, next, distance); distance = 0; }
            }
            if (returnIndex < 0)
            { Flying = Returning = false; projectile.SetActive(false); Catches++; SetMode(Mode.Catching); }
        }
        public bool PresentPose()
        {
            if (!Active) return false;
            if (State == Mode.Throwing) visual.SampleSlotFrame(0x64, Mathf.Min(Frame, 20));
            else if (State == Mode.Catching) visual.SampleSlotFrame(0x65, Mathf.Min(Frame, 18));
            else visual.SampleSlot(0, time);
            return true;
        }
        private void OnDestroy() { if (projectile != null) Destroy(projectile); }
    }
}
