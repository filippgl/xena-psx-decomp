using UnityEngine;

namespace Xena.Assets
{
    [RequireComponent(typeof(CharacterController), typeof(PrototypeInput))]
    public sealed class PrototypeMotor : MonoBehaviour
    {
        [SerializeField] private TextAsset terrainSource;
        [SerializeField] private XenaAssetPreview visual;
        [SerializeField] private PrototypeCamera orbit;
        [SerializeField] private Vector3 spawn;
        [SerializeField] private float walkSpeed = 2.5f, runSpeed = 5.5f, jumpSpeed = 6f, gravity = 20f;
        private CharacterController controller;
        private PrototypeInput input;
        private PrototypeCombat combat;
        private PrototypeChakram chakram;
        private XenaTerrainQuery terrain;
        private float vertical, groundTime, landingTime = -1;
        private int groundSlot;
        private bool backwardsJump;
        private Vector3 backwardsVelocity;
        // Zero-based SMA frames measured against native RAM by inspect_xena_jump.mjs.
        private const int JumpSlot = 0x2f, TakeoffFrame = 6, ApexFrame = 17, TouchdownFrame = 29, LastJumpFrame = 43;
        private int airSlot = JumpSlot, airTakeoff = TakeoffFrame, airApex = ApexFrame, airTouchdown = TouchdownFrame, airLast = LastJumpFrame;
        // Render-only interpolation between the last two 60Hz ticks; the
        // controller, combat and collision state stay on the discrete tick.
        private Vector3 previousPosition, currentPosition;
        private Quaternion previousRotation = Quaternion.identity, currentRotation = Quaternion.identity;
        private Vector3 restoreModelPosition; private Quaternion restoreModelRotation; private bool presentationApplied;
        public int Ticks { get; private set; }
        // A transform moved outside the tick (reset, disabled motor, tests) is shown as is.
        public Vector3 PresentationPosition => transform.position != currentPosition ? transform.position : Vector3.Lerp(previousPosition, currentPosition, PresentationBlend);
        private float PresentationBlend => input.Paused ? 1 : Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        public bool Grounded { get; private set; }
        public bool Backflipping => backwardsJump;
        public float VerticalSpeed => vertical;
        public void Configure(TextAsset data, XenaAssetPreview model, PrototypeCamera camera, Vector3 start)
        { terrainSource = data; visual = model; orbit = camera; spawn = start; }
        private void Awake()
        {
            // Dedicated prototype scene clock; Editor-only changes are not
            // serialized into the player's TimeManager settings automatically.
            Time.fixedDeltaTime = 1f / 60;
            controller = GetComponent<CharacterController>(); input = GetComponent<PrototypeInput>();
            terrain = new XenaTerrainQuery(JsonUtility.FromJson<XenaTerrainData>(terrainSource.text));
            visual.SetUpscale(true); visual.enabled = false;
            combat = GetComponent<PrototypeCombat>(); if (combat == null) combat = gameObject.AddComponent<PrototypeCombat>();
            chakram = GetComponent<PrototypeChakram>(); if (chakram == null) chakram = gameObject.AddComponent<PrototypeChakram>();
            if (GetComponent<PrototypeRemasterEnvironment>() == null) gameObject.AddComponent<PrototypeRemasterEnvironment>();
            if (GetComponent<PrototypeRemasterGeometry>() == null) gameObject.AddComponent<PrototypeRemasterGeometry>();
            if (GetComponent<PrototypeSky>() == null) gameObject.AddComponent<PrototypeSky>();
            if (GetComponent<PrototypeBandits>() == null) gameObject.AddComponent<PrototypeBandits>();
            if (GetComponent<PrototypeLevelScripts>() == null) gameObject.AddComponent<PrototypeLevelScripts>();
            if (GetComponent<PrototypeOriginalDamage>() == null) gameObject.AddComponent<PrototypeOriginalDamage>();
            if (GetComponent<PrototypeMusic>() == null) gameObject.AddComponent<PrototypeMusic>();
            if (GetComponent<PrototypeHud>() == null) gameObject.AddComponent<PrototypeHud>();
            if (GetComponent<PrototypeFrogs>() == null) gameObject.AddComponent<PrototypeFrogs>();
            if (GetComponent<PrototypeGameOver>() == null) gameObject.AddComponent<PrototypeGameOver>();
            if (GetComponent<PrototypeMainMenu>() == null) gameObject.AddComponent<PrototypeMainMenu>();
            if (GetComponent<PrototypeGraphics>() == null) gameObject.AddComponent<PrototypeGraphics>();
            AddHoleSolidColliders();
        }
        // @rck_cx/@plr_cx1/@plr_cx2 have no CLS: the original keeps Xena out
        // through terrain holes in their footprint. Their visible meshes become
        // colliders so projectiles and the camera sweep also stop at the stone.
        private static readonly string[] HoleSolids = { "level-01-object-65", "level-01-object-66", "level-01-object-67" };
        public static int HoleSolidColliders { get; private set; }
        private static void AddHoleSolidColliders()
        {
            HoleSolidColliders = 0;
            foreach (MeshFilter filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null || System.Array.IndexOf(HoleSolids, filter.sharedMesh.name) < 0) continue;
                if (filter.GetComponent<MeshCollider>() == null) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                HoleSolidColliders++;
            }
        }
        private void Start() { ResetToSpawn(); }
        public void ResetToSpawn()
        {
            controller.enabled = false; transform.position = spawn; controller.enabled = true;
            SnapPresentation();
            vertical = 0; Grounded = true; groundSlot = 0; groundTime = 0;
            landingTime = -1; visual.SampleSlotFrame(0, 0);
            backwardsJump = false; backwardsVelocity = Vector3.zero;
            ConfigureJump(false);
            if (combat != null) combat.ResetState();
            if (chakram != null) chakram.ResetState();
        }
        private float Floor(Vector3 position)
        {
            int height = terrain.GetHeight(Mathf.RoundToInt(position.x * 400), Mathf.RoundToInt(-position.y * 400), Mathf.RoundToInt(position.z * 400));
            return height == XenaTerrainQuery.NoHeight ? float.NegativeInfinity : -height / 400f;
        }
        public float GroundHeight(Vector3 position) => Floor(position);
        private void FixedUpdate()
        {
            RestorePresentation();
            if (input.Paused) return;
            if (transform.position != currentPosition || transform.rotation != currentRotation) SnapPresentation();
            previousPosition = currentPosition; previousRotation = currentRotation;
            float dt = Time.fixedDeltaTime;
            bool wasGrounded = Grounded;
            orbit.SynchronizeGuardCamera();
            chakram.Tick(dt, Grounded, orbit.Yaw, orbit.Pitch);
            var hurt = GetComponent<PrototypeOriginalDamage>(); bool reacting = hurt != null && hurt.Reacting;
            if (!chakram.Active && !reacting) combat.Tick(dt, Grounded, orbit.Yaw);
            Vector2 controls = reacting ? Vector2.zero : input.Move;
            Vector3 motion = Quaternion.Euler(0, orbit.Yaw, 0) * new Vector3(controls.x, 0, controls.y);
            if (combat.Busy || chakram.Active) motion = Vector3.zero;
            bool jump = input.ConsumeJump() && Grounded && !combat.Busy;
            bool backflip = jump && !input.Sprint && motion.sqrMagnitude >= 0.25f && Vector3.Dot(transform.forward, motion.normalized) < -0.5f;
            if (jump)
            {
                ConfigureJump(input.Sprint && motion.sqrMagnitude > 0.01f, backflip);
                backwardsJump = backflip; backwardsVelocity = -transform.forward * walkSpeed;
                vertical = jumpSpeed; Grounded = false; landingTime = -1;
            }
            if (!backwardsJump && motion.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(motion), 720 * dt);
            vertical -= gravity * dt;
            Vector3 horizontal = combat.DrivesMotion ? combat.ActionVelocity : motion * (input.Sprint ? runSpeed : walkSpeed);
            if (backwardsJump) horizontal = backwardsVelocity;
            if (Grounded) horizontal = OriginalTerrainLimit(horizontal, dt);
            CollisionFlags collision = controller.Move((horizontal + Vector3.up * vertical) * dt);
            if ((collision & CollisionFlags.Above) != 0 && vertical > 0) vertical = 0;
            float floor = Floor(transform.position);
            if (vertical <= 0 && !float.IsNegativeInfinity(floor) && transform.position.y <= floor + 0.08f && floor - transform.position.y < 0.4f)
            {
                controller.Move(Vector3.up * (floor - transform.position.y)); Grounded = true; vertical = -1;
            }
            else if ((collision & CollisionFlags.Below) != 0 && vertical <= 0) { Grounded = true; vertical = -1; }
            else Grounded = false;
            if (wasGrounded && !Grounded && vertical <= 0) { ConfigureJump(false); backwardsJump = false; }
            if (Grounded && !wasGrounded) { landingTime = 0; backwardsJump = false; }
            if (reacting) hurt.PresentReaction(controller);
            else if (!chakram.PresentPose() && !combat.PresentPose()) SampleLocomotion(motion.sqrMagnitude > 0.01f, dt);
            else { groundSlot = 0; groundTime = 0; landingTime = -1; }
            // These are externally driven sampled clips, not animation root motion.
            if (transform.position.y < spawn.y - 30) ResetToSpawn();
            currentPosition = transform.position; currentRotation = transform.rotation;
            Ticks++;
        }

        private void SampleLocomotion(bool moving, float dt)
        {
            if (!Grounded)
            {
                groundSlot = 0; groundTime = 0;
                // Keep modern jump physics; retime the verified original poses
                // to ascent/descent rather than looping a 44-frame jump in air.
                float phase = Mathf.Clamp01(Mathf.Abs(vertical) / jumpSpeed);
                int frame = vertical > 0
                    ? Mathf.RoundToInt(Mathf.Lerp(airApex, airTakeoff, phase))
                    : Mathf.RoundToInt(Mathf.Lerp(airApex, airTouchdown - 1, phase));
                visual.SampleSlotFrame(airSlot, frame);
                return;
            }
            if (landingTime >= 0 && !moving)
            {
                int frame = airTouchdown + Mathf.FloorToInt(landingTime * 30);
                if (frame <= airLast)
                {
                    visual.SampleSlotFrame(airSlot, frame); landingTime += dt; return;
                }
            }
            landingTime = -1;
            if (moving)
            {
                if (input.Sprint)
                {
                    if (groundSlot != 0x1a)
                    {
                        float phase = groundSlot == 0x15 ? (groundTime * 30 % 25) / 25 : 0;
                        SetGroundSlot(0x1a); groundTime = phase * 20 / 30;
                    }
                }
                else
                {
                    if (groundSlot == 0x1a)
                    { float phase = (groundTime * 30 % 20) / 20; SetGroundSlot(0x15); groundTime = phase * 25 / 30; }
                    if (groundSlot != 0x14 && groundSlot != 0x15) SetGroundSlot(0x14);
                    if (groundSlot == 0x14 && groundTime >= 5f / 30) SetGroundSlot(0x15);
                }
            }
            else
            {
                if (groundSlot == 0x1a) SetGroundSlot(0x1f);
                if (groundSlot == 0x14 || groundSlot == 0x15) SetGroundSlot(0x17);
                if (groundSlot == 0x17 && groundTime >= 7f / 30) SetGroundSlot(0);
                if (groundSlot == 0x1f && groundTime >= 5f / 30) SetGroundSlot(0);
            }
            // Walk15 and run1A are distinct native cycles, not a speed multiplier.
            if (groundSlot == 0x14 || groundSlot == 0x17 || groundSlot == 0x1f)
                visual.SampleSlotFrame(groundSlot, Mathf.FloorToInt(groundTime * 30));
            else visual.SampleSlot(groundSlot, groundTime);
            groundTime += dt;
        }
        private void SetGroundSlot(int slot) { groundSlot = slot; groundTime = 0; }
        // Original shared mover 8006E78C: ground probes at heading +-45deg, 150
        // units (Xena radius) beyond the step, and the end point reject terrain
        // with no surface or a step up above 300 units. Rocks/cliffs without CLS
        // are holes in the height grid. The original slides along the probed
        // surface normal; the probe direction stands in for it (approximation).
        private Vector3 OriginalTerrainLimit(Vector3 horizontal, float dt)
        {
            if (horizontal.sqrMagnitude < 0.000001f) return horizontal;
            Vector3 heading = horizontal.normalized;
            foreach (float angle in new[] { 45f, -45f })
            {
                Vector3 probe = Quaternion.Euler(0, angle, 0) * heading;
                if (TerrainBlocks(transform.position + horizontal * dt + probe * 0.375f))
                    horizontal -= probe * Mathf.Max(0, Vector3.Dot(horizontal, probe));
            }
            return TerrainBlocks(transform.position + horizontal * dt) ? Vector3.zero : horizontal;
        }
        private bool TerrainBlocks(Vector3 point)
        {
            float floor = Floor(new Vector3(point.x, transform.position.y, point.z));
            return float.IsNegativeInfinity(floor) || floor - transform.position.y > 0.75f;
        }
        public void SnapPresentationForTest() => SnapPresentation();
        private void SnapPresentation()
        {
            previousPosition = currentPosition = transform.position;
            previousRotation = currentRotation = transform.rotation;
        }
        private void RestorePresentation()
        {
            if (!presentationApplied) return;
            Transform model = visual.transform;
            model.localPosition = restoreModelPosition; model.localRotation = restoreModelRotation; presentationApplied = false;
        }
        private void ApplyPresentation()
        {
            RestorePresentation();
            // Teleports between ticks (reset/tests) are shown immediately.
            if (transform.position != currentPosition || transform.rotation != currentRotation) SnapPresentation();
            Transform model = visual.transform;
            restoreModelPosition = model.localPosition; restoreModelRotation = model.localRotation; presentationApplied = true;
            float blend = PresentationBlend;
            Quaternion delta = Quaternion.Slerp(previousRotation, currentRotation, blend) * Quaternion.Inverse(transform.rotation);
            model.SetPositionAndRotation(PresentationPosition + delta * (model.position - transform.position), delta * model.rotation);
        }
        private void LateUpdate()
        {
            ApplyPresentation();
            if (input.Paused || chakram.Active) return;
            float renderStep = Mathf.Clamp(Time.time - Time.fixedTime, 0, Time.fixedDeltaTime);
            if (combat.IsEvading)
                visual.SampleInterpolatedSlotFrame(combat.Slot, (combat.PoseTime + renderStep) * 30);
            else if (!Grounded && (backwardsJump || airSlot == 0x31))
            {
                float speed = vertical - gravity * renderStep;
                float phase = Mathf.Clamp01(Mathf.Abs(speed) / jumpSpeed);
                float frame = speed > 0 ? Mathf.Lerp(airApex, airTakeoff, phase) : Mathf.Lerp(airApex, airTouchdown - 1, phase);
                visual.SampleInterpolatedSlotFrame(airSlot, frame);
            }
        }
        private void ConfigureJump(bool running, bool backwards = false)
        {
            airSlot = backwards ? 0x35 : running ? 0x31 : JumpSlot; airTakeoff = backwards ? 4 : TakeoffFrame;
            airApex = backwards ? 12 : running ? 19 : ApexFrame; airTouchdown = backwards ? 21 : running ? 33 : TouchdownFrame;
            airLast = backwards ? 31 : running ? 46 : LastJumpFrame;
        }
    }
}
