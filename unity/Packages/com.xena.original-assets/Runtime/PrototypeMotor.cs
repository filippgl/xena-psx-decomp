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
        private XenaTerrainQuery terrain;
        private float vertical, groundTime, landingTime = -1;
        private int groundSlot;
        // Zero-based SMA frames measured against native RAM by inspect_xena_jump.mjs.
        private const int JumpSlot = 0x2f, TakeoffFrame = 6, ApexFrame = 17, TouchdownFrame = 29, LastJumpFrame = 43;
        public int Ticks { get; private set; }
        public bool Grounded { get; private set; }
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
        }
        private void Start() { ResetToSpawn(); }
        public void ResetToSpawn()
        {
            controller.enabled = false; transform.position = spawn; controller.enabled = true;
            vertical = 0; Grounded = true; groundSlot = 0; groundTime = 0;
            landingTime = -1; visual.SampleSlotFrame(0, 0);
            if (combat != null) combat.ResetState();
        }
        private float Floor(Vector3 position)
        {
            int height = terrain.GetHeight(Mathf.RoundToInt(position.x * 400), Mathf.RoundToInt(-position.y * 400), Mathf.RoundToInt(position.z * 400));
            return height == XenaTerrainQuery.NoHeight ? float.NegativeInfinity : -height / 400f;
        }
        public float GroundHeight(Vector3 position) => Floor(position);
        private void FixedUpdate()
        {
            if (input.Paused) return;
            float dt = Time.fixedDeltaTime;
            bool wasGrounded = Grounded;
            combat.Tick(dt, Grounded);
            Vector2 controls = input.Move;
            Vector3 motion = Quaternion.Euler(0, orbit.Yaw, 0) * new Vector3(controls.x, 0, controls.y);
            if (combat.Busy) motion = Vector3.zero;
            if (motion.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(motion), 720 * dt);
            if (input.ConsumeJump() && Grounded && !combat.Busy) { vertical = jumpSpeed; Grounded = false; landingTime = -1; }
            vertical -= gravity * dt;
            CollisionFlags collision = controller.Move((motion * (input.Sprint ? runSpeed : walkSpeed) + Vector3.up * vertical) * dt);
            if ((collision & CollisionFlags.Above) != 0 && vertical > 0) vertical = 0;
            float floor = Floor(transform.position);
            if (vertical <= 0 && !float.IsNegativeInfinity(floor) && transform.position.y <= floor + 0.08f && floor - transform.position.y < 0.4f)
            {
                controller.Move(Vector3.up * (floor - transform.position.y)); Grounded = true; vertical = -1;
            }
            else if ((collision & CollisionFlags.Below) != 0 && vertical <= 0) { Grounded = true; vertical = -1; }
            else Grounded = false;
            if (Grounded && !wasGrounded) landingTime = 0;
            if (!combat.PresentPose()) SampleLocomotion(motion.sqrMagnitude > 0.01f, dt);
            else { groundSlot = 0; groundTime = 0; landingTime = -1; }
            // These are externally driven sampled clips, not animation root motion.
            if (transform.position.y < spawn.y - 30) ResetToSpawn();
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
                    ? Mathf.RoundToInt(Mathf.Lerp(ApexFrame, TakeoffFrame, phase))
                    : Mathf.RoundToInt(Mathf.Lerp(ApexFrame, TouchdownFrame - 1, phase));
                visual.SampleSlotFrame(JumpSlot, frame);
                return;
            }
            if (landingTime >= 0 && !moving)
            {
                int frame = TouchdownFrame + Mathf.FloorToInt(landingTime * 30);
                if (frame <= LastJumpFrame)
                {
                    visual.SampleSlotFrame(JumpSlot, frame); landingTime += dt; return;
                }
            }
            landingTime = -1;
            if (moving)
            {
                if (groundSlot != 0x14 && groundSlot != 0x15) SetGroundSlot(0x14);
                if (groundSlot == 0x14 && groundTime >= 5f / 30) SetGroundSlot(0x15);
            }
            else
            {
                if (groundSlot == 0x14 || groundSlot == 0x15) SetGroundSlot(0x17);
                if (groundSlot == 0x17 && groundTime >= 7f / 30) SetGroundSlot(0);
            }
            // Native start/stop are one-shot clips. Only the 25-frame run loops.
            if (groundSlot == 0x14 || groundSlot == 0x17)
                visual.SampleSlotFrame(groundSlot, Mathf.FloorToInt(groundTime * 30));
            else visual.SampleSlot(groundSlot, groundTime);
            groundTime += dt * (moving && !input.Sprint ? 0.6f : 1);
        }
        private void SetGroundSlot(int slot) { groundSlot = slot; groundTime = 0; }
    }
}
