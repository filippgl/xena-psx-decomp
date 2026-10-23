using UnityEngine;

namespace Xena.Assets
{
    public sealed class PrototypeCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private PrototypeInput input;
        [SerializeField] private float yaw, pitch = 18, distance = 4, sensitivity = 0.12f;
        [SerializeField] private bool invertY;
        private Vector3 smoothFocus, focusVelocity;
        private bool initialized;
        private PrototypeCombat combat;
        public float Yaw => yaw;
        public float DistanceToTarget { get; private set; }
        public void Configure(Transform follow, PrototypeInput controls) { target = follow; input = controls; }
        private void LateUpdate()
        {
            if (!input.Paused)
            {
                Vector2 look = input.MouseLook * sensitivity + input.StickLook * (120 * Time.deltaTime);
                yaw += look.x; pitch = Mathf.Clamp(pitch + look.y * (invertY ? 1 : -1), -30, 70);
            }
            Vector3 desiredFocus = target.position + Vector3.up * 1.1f;
            if (!initialized || Vector3.Distance(smoothFocus, desiredFocus) > 8)
            { smoothFocus = desiredFocus; focusVelocity = Vector3.zero; initialized = true; }
            else smoothFocus = Vector3.SmoothDamp(smoothFocus, desiredFocus, ref focusVelocity, 0.08f);
            Vector3 focus = smoothFocus;
            Vector3 back = Quaternion.Euler(pitch, yaw, 0) * Vector3.back;
            float radius = distance;
            if (Physics.SphereCast(focus, 0.18f, back, out RaycastHit hit, distance, ~(1 << 2), QueryTriggerInteraction.Ignore)) radius = Mathf.Max(0.35f, hit.distance - 0.12f);
            transform.position = focus + back * radius; transform.LookAt(focus); DistanceToTarget = radius;
        }
        private void OnGUI()
        {
            if (combat == null) combat = target.GetComponent<PrototypeCombat>();
            GUILayout.BeginArea(new Rect(16, 16, 430, input.Paused ? 310 : 150), GUI.skin.box);
            GUILayout.Label("Xena — movement & combat prototype");
            GUILayout.Label("WASD move · Shift run · Space jump · R reset · Esc pause");
            GUILayout.Label("Gamepad: sticks · L3 run · A jump · Start pause");
            GUILayout.Label("LMB sword · F kick · hold RMB block");
            GUILayout.Label("Gamepad: X sword · Y kick · LB block");
            if (combat != null) GUILayout.Label("Health: " + combat.Health + " | R resets you and the dummy");
            if (input.Paused)
            {
                GUILayout.Label("Mouse sensitivity: " + sensitivity.ToString("0.00"));
                sensitivity = GUILayout.HorizontalSlider(sensitivity, 0.03f, 0.3f);
                invertY = GUILayout.Toggle(invertY, "Invert camera Y");
                if (combat != null && combat.Target != null)
                    combat.Target.Retaliates = GUILayout.Toggle(combat.Target.Retaliates, "Practice dummy attacks nearby (temporary rules)");
                GUILayout.Label("Practice reach/damage only; original hitboxes pending");
                GUILayout.Label("Press Esc to resume");
            }
            GUILayout.EndArea();
        }
    }
}
