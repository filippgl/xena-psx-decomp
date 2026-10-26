using UnityEngine;

namespace Xena.Assets
{
    public sealed class PrototypeCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private PrototypeInput input;
        [SerializeField] private float yaw, pitch = 18;
        public float Sensitivity => input.Preferences.Data.mouseSensitivity;
        public bool InvertY => input.Preferences.Data.invertY;
        public float DesiredDistance => input.Preferences.Data.cameraDistance;
        private Vector3 smoothFocus, focusVelocity;
        private bool initialized;
        private PrototypeCombat combat;
        private PrototypeChakram chakram;
        private PrototypeMotor motor;
        private PrototypeOriginalDamage vitality;
        private Vector3 deathDestination; private bool deathCamera; private float deathClock;
        public bool DeathCamera => deathCamera;
        private Camera view;
        private float playerFov, playerNearClip;
        public bool BehindCharacter { get; private set; }
        // Holding block (no evade/backflip running): the mouse turns the camera and Xena turns with it,
        // so cartwheels / handsprings follow the mouse (user request; the PS1 had no free camera).
        public bool GuardTurning { get; private set; }
        private bool wasTurning; private int guardEntryFrame = -1;
        public float Yaw => yaw;
        public float Pitch => pitch;
        public float DistanceToTarget { get; private set; }
        private void Awake() { view = GetComponent<Camera>(); playerFov = view.fieldOfView; playerNearClip = view.nearClipPlane; }
        public void Configure(Transform follow, PrototypeInput controls) { target = follow; input = controls; }
        // Also called before the simulation consumes movement: the first guard
        // tick must use the same frame of reference as the camera, not last yaw.
        public void SynchronizeGuardCamera()
        {
            if (combat == null) combat = target.GetComponent<PrototypeCombat>();
            if (motor == null) motor = target.GetComponent<PrototypeMotor>();
            if (chakram == null) chakram = target.GetComponent<PrototypeChakram>();
            BehindCharacter = (chakram == null || !chakram.Active) &&
                (input.Block || (combat != null && combat.IsEvading) || (motor != null && motor.Backflipping));
            bool turning = BehindCharacter && input.Block && (combat == null || !combat.IsEvading) && (motor == null || !motor.Backflipping);
            // Entering guard: the camera goes behind Xena first (as before) and mouse input of the entry
            // frame is ignored; only mouse deltas of later frames turn camera and Xena together.
            if (turning && !wasTurning) guardEntryFrame = Time.frameCount;
            wasTurning = turning;
            GuardTurning = turning && Time.frameCount > guardEntryFrame;
            if (BehindCharacter && !GuardTurning) yaw = target.eulerAngles.y;
        }
        private void LateUpdate()
        {
            SynchronizeGuardCamera();
            if (!input.Paused)
            {
                Vector2 look = input.MouseLook * Sensitivity + input.StickLook * (120 * Time.deltaTime);
                if (!BehindCharacter || GuardTurning) yaw += look.x;
                pitch = Mathf.Clamp(pitch + look.y * (InvertY ? 1 : -1), -30, 70);
                if (input.CenterCameraPressed) yaw = target.eulerAngles.y;
                input.Preferences.Data.cameraDistance = Mathf.Clamp(DesiredDistance - input.Zoom * 0.01f, 2, 7);
            }
            if (chakram == null) chakram = target.GetComponent<PrototypeChakram>();
            // Return is automatic: give the view back to Xena instead of
            // swinging the close lens180degrees into her body/a nearby wall.
            bool flightCamera = chakram != null && chakram.Flying && !chakram.Returning;
            if (flightCamera)
            {
                // Follow the actual projectile without positional damping: at
                // 14m/s a delayed focus leaves the small original disc far away.
                Vector3 forward = chakram.FlightDirection.normalized;
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward).normalized;
                Vector3 anchor = chakram.ProjectilePosition;
                Vector3 offset = -forward * 0.78f + up * 0.28f;
                float distance = offset.magnitude;
                if (Physics.SphereCast(anchor, 0.06f, offset.normalized, out RaycastHit obstacle, distance, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    distance = Mathf.Max(0.06f, obstacle.distance - 0.02f);
                transform.position = anchor + offset.normalized * distance;
                transform.LookAt(anchor + forward * 0.45f, up);
                // Keep horizontal framing across 16:9 and ultrawide windows.
                view.fieldOfView = 2 * Mathf.Atan(Mathf.Tan(17.5f * Mathf.Deg2Rad) * (16f / 9) / view.aspect) * Mathf.Rad2Deg;
                view.nearClipPlane = 0.02f; DistanceToTarget = distance;
                smoothFocus = anchor; focusVelocity = Vector3.zero; initialized = true;
                return;
            }
            view.fieldOfView = playerFov; view.nearClipPlane = playerNearClip;
            if (vitality == null) vitality = target.GetComponent<PrototypeOriginalDamage>();
            if (vitality != null && vitality.Dead) { DeathView(); return; }
            deathCamera = false;
            Vector3 desiredFocus = (motor != null ? motor.PresentationPosition : target.position) + Vector3.up * 1.1f;
            if (!initialized || Vector3.Distance(smoothFocus, desiredFocus) > 8)
            { smoothFocus = desiredFocus; focusVelocity = Vector3.zero; initialized = true; }
            else smoothFocus = Vector3.SmoothDamp(smoothFocus, desiredFocus, ref focusVelocity, 0.08f);
            Vector3 focus = smoothFocus;
            Vector3 back = Quaternion.Euler(pitch, yaw, 0) * Vector3.back;
            float radius = DesiredDistance;
            if (Physics.SphereCast(focus, 0.18f, back, out RaycastHit hit, radius, ~(1 << 2), QueryTriggerInteraction.Ignore)) radius = Mathf.Max(0.35f, hit.distance - 0.12f);
            // Stay inside the level: the original height query must find ground
            // under the lens and the lens must stay above it, else move closer.
            if (motor != null)
                // Never pull the lens into Xena for this: 1.6 m minimum.
                while (radius > 1.6f && OutsideLevel(focus + back * radius)) radius = Mathf.Max(1.6f, radius - 0.1f);
            transform.position = focus + back * radius; transform.LookAt(focus); DistanceToTarget = radius;
        }
        // Death camera func_80053380 (Xena: zero offset): destination = Xena + (Xena - camera)/100
        // and 3000 units (7.5 m) above her; func_8004F088(.., 0x71) moves the lens there while it
        // looks at Xena. Approach rate 0x71/4096 per 30/s step is INFERRED (native capture:
        // overhead view about 2 s after the hit, build/gameover).
        private void DeathView()
        {
            Vector3 xena = motor != null ? motor.PresentationPosition : target.position;
            if (!deathCamera)
            {
                Vector3 away = xena - transform.position; away.y = 0;
                deathDestination = new Vector3(away.x / 100, 7.5f, away.z / 100); deathCamera = true; deathClock = 0;
            }
            deathClock += Time.deltaTime;
            float steps = deathClock * 30; deathClock -= Mathf.Floor(steps) / 30;
            for (int i = 0; i < (int)steps; i++) transform.position += (xena + deathDestination - transform.position) * (0x71 / 4096f);
            Vector3 look = xena + Vector3.up * 0.3f - transform.position;
            if (look.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(look, Mathf.Abs(look.normalized.y) > 0.99f ? Quaternion.Euler(0, yaw, 0) * Vector3.forward : Vector3.up);
            DistanceToTarget = look.magnitude;
        }
        private bool OutsideLevel(Vector3 lens)
        {
            float ground = motor.GroundHeight(lens);
            return float.IsNegativeInfinity(ground) || lens.y < ground + 0.25f;
        }
        private void OnGUI()
        {
            if (combat == null) combat = target.GetComponent<PrototypeCombat>();
            if (chakram != null && chakram.Active && !input.Paused)
                GUI.Label(new Rect(Screen.width / 2 - 8, Screen.height / 2 - 12, 24, 30), "+");
            // The on-screen controls/moves panel was removed (user 2026-10-26): controls live in the pause menu
            // (Опции -> Управление).
        }
    }
}
