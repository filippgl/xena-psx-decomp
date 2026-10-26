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
        public Vector3 DiscNormal => projectile.transform.up;
        public int TrailPoints => spinHalo == null ? 0 : spinHalo.VertexCount;
        public int SparkBursts { get; private set; }
        public int SparkCount => sparks == null ? 0 : sparks.particleCount;
        public bool Rebounding => Flying && reboundTime > 0;
        public Vector3 ImpactPosition { get; private set; }
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
        private PrototypeSpinHalo spinHalo;
        private Material trailMaterial;
        private ParticleSystem sparks;
        private float reboundTime;
        private void Awake()
        {
            input = GetComponent<PrototypeInput>(); combat = GetComponent<PrototypeCombat>();
            visual = GetComponentInChildren<XenaAssetPreview>();
            GameObject original = Resources.Load<GameObject>("Chakram/Chakram");
            if (original == null) throw new InvalidOperationException("Standalone original Chakram resource unavailable; run prepare_movement_prototype.mjs");
            projectile = Instantiate(original); projectile.name = "OriginalChakramProjectile";
            foreach (Transform part in projectile.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 2;
            projectile.GetComponent<XenaAssetPreview>().enabled = false;
            // CHAKRAM.TMD object 0 is a single flat grey 6-unit triangle at the disc centre (hierarchy root marker). At the
            // PS1 camera distance and 320 px it is below a pixel; in the close flight camera it showed as a white triangle
            // inside the hole (user 2026-10-26). The mesh stays; only untextured geometry inside the hole is not drawn.
            foreach (MeshFilter part in projectile.GetComponentsInChildren<MeshFilter>(true))
            {
                bool inHole = true; foreach (Vector3 v in part.sharedMesh.vertices) inHole &= new Vector2(v.x, v.z).magnitude < 0.05f;
                if (inHole) part.GetComponent<MeshRenderer>().enabled = false;
            }
            var trailObject = new GameObject("ChakramSpinHalo"); trailObject.layer = 2;
            trailObject.transform.SetParent(projectile.transform, false);
            Material effectMaterial = Resources.Load<Material>("Chakram/FlightTrail");
            if (effectMaterial == null)
            {
                trailMaterial = new Material(Shader.Find("Xena/ChakramTrail"));
                effectMaterial = trailMaterial;
            }
            spinHalo = trailObject.AddComponent<PrototypeSpinHalo>(); spinHalo.Configure(effectMaterial);
            var sparkObject = new GameObject("ChakramImpactSparks"); sparkObject.layer = 2;
            sparks = sparkObject.AddComponent<ParticleSystem>();
            var main = sparks.main; main.playOnAwake = false; main.loop = false; main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0.7f;
            var emission = sparks.emission; emission.enabled = false;
            var shape = sparks.shape; shape.enabled = false;
            var color = sparks.colorOverLifetime; color.enabled = true;
            color.color = new Gradient {
                colorKeys = new[] { new GradientColorKey(new Color(1, 0.95f, 0.65f), 0), new GradientColorKey(new Color(1, 0.3f, 0.03f), 1) },
                alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }
            };
            sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = effectMaterial;
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            projectile.SetActive(false);
        }
        public void ResetState()
        {
            State = Mode.Ready; Flying = Returning = released = false; time = flightAge = spin = 0;
            Throws = Catches = WallContacts = 0; trail.Clear(); struck.Clear();
            ClearSpinTrail();
            reboundTime = 0; SparkBursts = 0;
            if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (projectile != null) projectile.SetActive(false);
        }
        private void SetMode(Mode mode) { State = mode; time = 0; }
        public void Tick(float dt, bool grounded, float yaw, float pitch)
        {
            // Manual simulation shares the flight clock, including pause/reset.
            if (sparks.particleCount > 0) { sparks.Simulate(dt, true, false, false); sparks.Pause(true); }
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
                    ClearSpinTrail(); spin = 0;
                    trail.Add(projectile.transform.position); projectile.SetActive(true);
                }
                if (Frame >= 21) SetMode(Mode.Flight);
            }
            else if (State == Mode.Catching && Frame >= 19) { SetMode(Mode.Ready); input.ClearPending(); }
            if (Flying)
            {
                Vector3 previousPosition = projectile.transform.position;
                flightAge += dt;
                if (!Returning && (!input.Chakram || flightAge >= 2.2f)) BeginReturn();
                if (Returning) Return(dt);
                else
                {
                    Vector3 desired = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
                    direction = Vector3.RotateTowards(direction, desired, 150 * Mathf.Deg2Rad * dt, 0);
                    Advance(direction * (14 * dt));
                }
                Vector3 travel = projectile.transform.position - previousPosition;
                if (!Rebounding && travel.sqrMagnitude > 0.000001f) direction = travel.normalized;
                // The original mesh lies in local XZ. Spin around its own normal
                // AFTER orienting its plane along travel; world-Y Euler rotation
                // of a tilted disc made the normal precess like a wobbling coin.
                spin = (spin + 900 * dt) % 360;
                projectile.transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.AngleAxis(spin, Vector3.up);
                if (Flying) UpdateSpinTrail(dt); else ClearSpinTrail();
            }
            if (Active) time += dt;
        }
        private void ClearSpinTrail()
        { if (spinHalo != null) spinHalo.Clear(); }
        private void UpdateSpinTrail(float dt)
        { spinHalo.Tick(dt); }
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
                    trail.Add(projectile.transform.position); WallContacts++; LastImpactCollider = hit.collider.name;
                    PrototypeSfx.PlayRandAt(0x19, 0x1A, projectile.transform.position); // ricochet 0x8003EAB4
                    EmitImpactSparks(hit.point, hit.normal);
                    BeginReturn(); reboundTime = 0.12f; direction = -step.normalized;
                    return;
                }
            }
            projectile.transform.position += step; trail.Add(projectile.transform.position);
            HitBandits(projectile.transform.position);
        }
        // Original chakram vs enemies (docs/xena-chakram-damage.md, func_8003DDEC / func_80070BD4):
        // body test max+3/8min < 100 + target radius (horizontal) and [y-20, y] overlapping the target's
        // [y-h, y] (h = 750); flat 300 damage per contact on the way out and back (no sword level, no
        // power-up, no karma); repeats are absorbed by the target's own hit timer; red spark + blood.
        // After a hit the disc returns at once while aiming (R1 held), otherwise it bounces off.
        public const int Damage = 300;
        public int EnemyHits { get; private set; }
        public void HitBanditsForTest(Vector3 at) => HitBandits(at);
        private void HitBandits(Vector3 at)
        {
            var bandits = GetComponent<PrototypeBandits>(); if (bandits == null) return;
            foreach (var bandit in bandits.Brains)
            {
                if (bandit == null || bandit.Removed || bandit.Health <= 0 || bandit.Dying) continue;
                Vector3 d = (bandit.transform.position - at) * 400; int dx = Mathf.Abs(Mathf.RoundToInt(d.x)), dz = Mathf.Abs(Mathf.RoundToInt(d.z));
                if (Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8 >= 100 + PrototypeBanditBrain.Radius) continue;
                float y = at.y * 400, top = bandit.transform.position.y * 400 + 750, bottom = bandit.transform.position.y * 400;
                if (y < bottom || y - 20 > top) continue;
                bool killed = false;
                if (bandit.TakeHit(Damage, at, false))
                {
                    EnemyHits++; killed = bandit.Health == 0;
                    PrototypeSfx.PlayAt(0x1B + PrototypeSfx.Instance.Rand(0, 1), bandit.transform.position); // 0x8003ED8C
                    var effects = GetComponent<PrototypeHitEffects>();
                    if (effects != null) { effects.Spark(at, Damage, PrototypeHitEffects.Type.Hit); effects.Bleed(bandit.transform, at, killed ? 20 : 10, killed ? 170 : 80); }
                }
                Vector3 away = at - bandit.transform.position; away.y = 0;
                if (input.Chakram || away.sqrMagnitude < 1e-6f) BeginReturn();
                else direction = Vector3.Reflect(direction, away.normalized);
                return;
            }
        }
        public string LastImpactCollider { get; private set; } = "";
        private void BeginReturn() { Returning = true; returnIndex = trail.Count - 1; }
        private void EmitImpactSparks(Vector3 point, Vector3 normal)
        {
            ImpactPosition = point; SparkBursts++;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 side = Vector3.Cross(normal, tangent);
            for (int i = 0; i < 18; i++)
            {
                float angle = i * 2.399963f;
                Vector3 velocity = (normal * (0.7f + i % 3 * 0.15f) + tangent * Mathf.Cos(angle) + side * Mathf.Sin(angle)).normalized * (2 + i % 5 * 0.5f);
                var particle = new ParticleSystem.EmitParams { position = point + normal * 0.015f, velocity = velocity,
                    startLifetime = 0.22f + i % 4 * 0.05f, startSize = 0.02f + i % 3 * 0.008f, startColor = Color.white };
                sparks.Emit(particle, 1);
            }
            // Emit can resume an otherwise stopped system. Keep automatic
            // particle updates off; only Tick is allowed to advance the burst.
            sparks.Pause(true);
        }
        private void Return(float dt)
        {
            // Retrace the outgoing path so the prototype does not cut through walls on return.
            float distance = (Rebounding ? 24 : 20) * dt;
            reboundTime = Mathf.Max(0, reboundTime - dt);
            while (distance > 0 && returnIndex >= 0)
            {
                Vector3 next = trail[returnIndex]; float gap = Vector3.Distance(projectile.transform.position, next);
                if (gap <= distance) { projectile.transform.position = next; distance -= gap; returnIndex--; }
                else { projectile.transform.position = Vector3.MoveTowards(projectile.transform.position, next, distance); distance = 0; }
            }
            if (returnIndex < 0)
            { Flying = Returning = false; projectile.SetActive(false); Catches++; SetMode(Mode.Catching); PrototypeSfx.Stop(8); PrototypeSfx.PlayOn(0x1D, transform); }
        }
        public bool PresentPose()
        {
            if (!Active) return false;
            if (State == Mode.Throwing) visual.SampleSlotFrame(0x64, Mathf.Min(Frame, 20));
            else if (State == Mode.Catching) visual.SampleSlotFrame(0x65, Mathf.Min(Frame, 18));
            else visual.SampleSlot(0, time);
            return true;
        }
        private void OnDestroy() { if (projectile != null) Destroy(projectile); if (sparks != null) Destroy(sparks.gameObject); if (trailMaterial != null) Destroy(trailMaterial); }
    }
}
