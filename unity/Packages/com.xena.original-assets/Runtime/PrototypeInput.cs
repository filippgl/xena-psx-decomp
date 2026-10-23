using UnityEngine;
using UnityEngine.InputSystem;

namespace Xena.Assets
{
    public sealed class PrototypeInput : MonoBehaviour
    {
        [SerializeField] private InputActionAsset source;
        private InputActionAsset actions;
        private InputAction move, mouse, stick, sprint, jump, pause, reset, sword, kick, block;
        private bool jumpPending, swordPending, kickPending;
        public bool Block => !Paused && block.IsPressed();
        public bool Paused { get; private set; }
        public bool Automated { get; set; }
        public Vector2 Move => Paused ? Vector2.zero : Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1);
        public bool Sprint => !Paused && sprint.IsPressed();
        public Vector2 MouseLook => Paused ? Vector2.zero : mouse.ReadValue<Vector2>();
        public Vector2 StickLook => Paused ? Vector2.zero : stick.ReadValue<Vector2>();
        public void Configure(InputActionAsset profile) { source = profile; }
        private void Awake()
        {
            actions = Instantiate(source);
            move = actions.FindAction("Player/Move", true); mouse = actions.FindAction("Player/LookMouse", true);
            stick = actions.FindAction("Player/LookStick", true); sprint = actions.FindAction("Player/Sprint", true);
            jump = actions.FindAction("Player/Jump", true); pause = actions.FindAction("Player/Pause", true); reset = actions.FindAction("Player/Reset", true);
            sword = actions.FindAction("Player/Sword", true); kick = actions.FindAction("Player/Kick", true); block = actions.FindAction("Player/Block", true);
        }
        private void OnEnable() { actions.Enable(); }
        private void OnDisable() { if (actions != null) actions.Disable(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        private void OnDestroy() { if (actions != null) Destroy(actions); }
        private void Start() { CaptureCursor(); }
        public void UseDevices(params InputDevice[] devices) { actions.FindActionMap("Player").devices = devices; Automated = true; CaptureCursor(); }
        private void CaptureCursor() { Cursor.lockState = Paused || Automated ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = Paused || Automated; }
        private void Update()
        {
            if (pause.WasPressedThisFrame()) { Paused = !Paused; ClearPending(); CaptureCursor(); }
            if (!Paused && jump.WasPressedThisFrame()) jumpPending = true;
            if (!Paused && sword.WasPressedThisFrame()) swordPending = true;
            if (!Paused && kick.WasPressedThisFrame()) kickPending = true;
            if (!Paused && reset.WasPressedThisFrame()) GetComponent<PrototypeMotor>().ResetToSpawn();
        }
        public bool ConsumeJump() { bool value = jumpPending; jumpPending = false; return value; }
        public int ConsumeAttack() { int value = swordPending ? 1 : kickPending ? 2 : 0; swordPending = kickPending = false; return value; }
        public void ClearPending() { jumpPending = swordPending = kickPending = false; }
    }
}
