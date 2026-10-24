using UnityEngine;

namespace Xena.Assets
{
    public sealed class PrototypeCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private PrototypeInput input;
        [SerializeField] private float yaw, pitch = 18;
        private Vector2 settingsScroll;
        public float Sensitivity => input.Preferences.Data.mouseSensitivity;
        public bool InvertY => input.Preferences.Data.invertY;
        public float DesiredDistance => input.Preferences.Data.cameraDistance;
        private Vector3 smoothFocus, focusVelocity;
        private bool initialized;
        private PrototypeCombat combat;
        private PrototypeChakram chakram;
        private PrototypeMotor motor;
        public bool BehindCharacter { get; private set; }
        public float Yaw => yaw;
        public float Pitch => pitch;
        public float DistanceToTarget { get; private set; }
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
            if (BehindCharacter) yaw = target.eulerAngles.y;
        }
        private void LateUpdate()
        {
            SynchronizeGuardCamera();
            if (!input.Paused)
            {
                Vector2 look = input.MouseLook * Sensitivity + input.StickLook * (120 * Time.deltaTime);
                if (!BehindCharacter) yaw += look.x;
                pitch = Mathf.Clamp(pitch + look.y * (InvertY ? 1 : -1), -30, 70);
                if (input.CenterCameraPressed) yaw = target.eulerAngles.y;
                input.Preferences.Data.cameraDistance = Mathf.Clamp(DesiredDistance - input.Zoom * 0.01f, 2, 7);
            }
            if (chakram == null) chakram = target.GetComponent<PrototypeChakram>();
            bool flightCamera = chakram != null && chakram.Flying;
            Vector3 desiredFocus = flightCamera ? chakram.ProjectilePosition : target.position + Vector3.up * 1.1f;
            if (!initialized || Vector3.Distance(smoothFocus, desiredFocus) > 8)
            { smoothFocus = desiredFocus; focusVelocity = Vector3.zero; initialized = true; }
            else smoothFocus = Vector3.SmoothDamp(smoothFocus, desiredFocus, ref focusVelocity, 0.08f);
            Vector3 focus = smoothFocus;
            Vector3 back = Quaternion.Euler(pitch, yaw, 0) * Vector3.back;
            float radius = flightCamera ? 1.8f : DesiredDistance;
            if (Physics.SphereCast(focus, 0.18f, back, out RaycastHit hit, radius, ~(1 << 2), QueryTriggerInteraction.Ignore)) radius = Mathf.Max(0.35f, hit.distance - 0.12f);
            transform.position = focus + back * radius; transform.LookAt(focus); DistanceToTarget = radius;
        }
        private void OnGUI()
        {
            if (combat == null) combat = target.GetComponent<PrototypeCombat>();
            if (chakram != null && chakram.Active && !input.Paused)
                GUI.Label(new Rect(Screen.width / 2 - 8, Screen.height / 2 - 12, 24, 30), "+");
            GUILayout.BeginArea(new Rect(16, 16, 560, input.Paused ? Mathf.Min(Screen.height - 32, 690) : 270), GUI.skin.box);
            GUILayout.Label("Xena — movement & combat prototype");
            GUILayout.Label(input.BindingLabel("Forward") + "/" + input.BindingLabel("Left") + "/" + input.BindingLabel("Back") + "/" + input.BindingLabel("Right") + " move · " + input.BindingLabel("Sprint") + " run · " + input.BindingLabel("Jump") + " jump · Esc pause");
            GUILayout.Label("Gamepad: sticks · L3 run · A jump · X sword · Y kick · LB block · Start pause");
            GUILayout.Label(input.BindingLabel("Sword") + " sword · " + input.BindingLabel("Kick") + " kick · hold " + input.BindingLabel("Block") + " block");
            GUILayout.Label("Hold block + direction: cartwheel / backward handspring");
            GUILayout.Label("Run + kick: roll · run + sword: slash · run + jump: frontflip");
            GUILayout.Label("Block + kick: spin kick · block + sword: strike");
            GUILayout.Label("Sword + kick: spin (tap kick to extend) · backward + jump: backflip");
            GUILayout.Label("Hold " + input.BindingLabel("Chakram") + " / RB aim · sword button throws · release aim recalls");
            GUILayout.Label("Wheel zoom · " + input.BindingLabel("CenterCamera") + " / R3 center camera");
            if (combat != null) GUILayout.Label("Health: " + combat.Health + " | " + input.BindingLabel("Reset") + " resets you and the dummy");
            if (input.Paused)
            {
                settingsScroll = GUILayout.BeginScrollView(settingsScroll);
                GUILayout.Label("Mouse sensitivity: " + Sensitivity.ToString("0.00"));
                GUILayout.Label("Camera distance: " + DesiredDistance.ToString("0.0"));
                input.Preferences.Data.mouseSensitivity = GUILayout.HorizontalSlider(Sensitivity, 0.03f, 0.3f);
                input.Preferences.Data.invertY = GUILayout.Toggle(InvertY, "Invert camera Y");
                foreach (string key in PrototypeInput.BindingKeys)
                {
                    GUILayout.BeginHorizontal(); GUILayout.Label(key, GUILayout.Width(140));
                    bool enabled = GUI.enabled; GUI.enabled = enabled && !input.IsRebinding;
                    if (GUILayout.Button(input.BindingLabel(key), GUILayout.Width(260))) input.BeginRebind(key);
                    GUI.enabled = enabled; GUILayout.EndHorizontal();
                }
                if (input.IsRebinding) { if (GUILayout.Button("Cancel binding")) input.CancelRebind(); }
                else
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Save settings")) input.SaveSettings();
                    if (GUILayout.Button("Restore defaults")) input.ResetPreferences();
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label(input.BindingStatus); GUILayout.Label(input.Preferences.Status);
                if (combat != null && combat.Target != null)
                    combat.Target.Retaliates = GUILayout.Toggle(combat.Target.Retaliates, "Practice dummy attacks nearby (temporary rules)");
                GUILayout.Label("Practice reach/damage only; original hitboxes pending");
                GUILayout.Label("Press Esc to resume");
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }
    }
}
