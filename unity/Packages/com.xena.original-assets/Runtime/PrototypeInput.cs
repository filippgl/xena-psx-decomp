using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Xena.Assets
{
    public sealed class PrototypeInput : MonoBehaviour
    {
        [SerializeField] private InputActionAsset source;
        private InputActionAsset actions;
        private InputAction move, mouse, stick, sprint, jump, pause, reset, sword, kick, block, centerCamera, zoom, chakram;
        private bool jumpPending, swordPending, kickPending;
        private InputActionRebindingExtensions.RebindingOperation rebinding;
        private HashSet<InputDevice> allowedDevices;
        private bool suppressPause;
        public PrototypePreferences Preferences { get; private set; }
        public bool IsRebinding => rebinding != null;
        public bool GraphicsTogglePressed => !IsRebinding && Keyboard.current != null &&
            (allowedDevices == null || allowedDevices.Contains(Keyboard.current)) && Keyboard.current.f10Key.wasPressedThisFrame;
        public string BindingStatus { get; private set; } = "";
        public static readonly string[] BindingKeys = { "Forward", "Back", "Left", "Right", "Sprint", "Jump", "Sword", "Kick", "Block", "Reset", "CenterCamera", "Chakram" };
        public bool Chakram => !Paused && chakram.IsPressed();
        public bool CenterCameraPressed => !Paused && centerCamera.WasPressedThisFrame();
        public float Zoom => Paused ? 0 : zoom.ReadValue<float>();
        public bool Block => !Paused && block.IsPressed();
        public bool Paused { get; private set; }
        // Set while a front-end menu (main menu) owns the keys: no pause toggle, no queued actions;
        // after it is released, actions stay blocked until jump/sword/kick are all let go, so the
        // confirming press (Space = jump) never reaches gameplay.
        private bool menuOwnsInput, waitRelease;
        public bool MenuOwnsInput { get => menuOwnsInput; set { if (menuOwnsInput && !value) waitRelease = true; menuOwnsInput = value; } }
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
            centerCamera = actions.FindAction("Player/CenterCamera", true); zoom = actions.FindAction("Player/Zoom", true);
            chakram = actions.FindAction("Player/Chakram", true);
            Preferences = new PrototypePreferences(); Preferences.Load(actions);
        }
        private void OnEnable() { actions.Enable(); }
        private void OnDisable() { CancelRebind(); if (actions != null) actions.Disable(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        private void OnDestroy() { if (actions != null) Destroy(actions); }
        private void Start() { CaptureCursor(); }
        public void UseDevices(params InputDevice[] devices) { actions.FindActionMap("Player").devices = devices; allowedDevices = new HashSet<InputDevice>(devices); Automated = true; CaptureCursor(); }
        private void CaptureCursor() { Cursor.lockState = Paused || Automated ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = Paused || Automated; }
        private void Update()
        {
            if (suppressPause) { if (!IsRebinding && !pause.IsPressed()) suppressPause = false; }
            else if (!IsRebinding && !MenuOwnsInput && pause.WasPressedThisFrame()) { Paused = !Paused; ClearPending(); CaptureCursor(); }
            if (waitRelease && !jump.IsPressed() && !sword.IsPressed() && !kick.IsPressed()) waitRelease = false;
            if (menuOwnsInput || waitRelease) { ClearPending(); return; }
            if (!Paused && jump.WasPressedThisFrame()) jumpPending = true;
            if (!Paused && sword.WasPressedThisFrame()) swordPending = true;
            if (!Paused && kick.WasPressedThisFrame()) kickPending = true;
            if (!Paused && reset.WasPressedThisFrame()) GetComponent<PrototypeMotor>().ResetToSpawn();
        }
        public bool ConsumeJump() { bool value = jumpPending; jumpPending = false; return value; }
        public int ConsumeAttack()
        {
            bool chord = swordPending && kickPending || (swordPending || kickPending) && sword.IsPressed() && kick.IsPressed();
            int value = chord ? 3 : swordPending ? 1 : kickPending ? 2 : 0;
            swordPending = kickPending = false; return value;
        }
        public void ClearPending() { jumpPending = swordPending = kickPending = false; }
        private int Binding(string key, out InputAction action)
        {
            string part = key == "Forward" ? "up" : key == "Back" ? "down" : key == "Left" ? "left" : key == "Right" ? "right" : null;
            action = part == null ? actions.FindAction("Player/" + key, true) : move;
            for (int i = 0; i < action.bindings.Count; i++)
                if (!action.bindings[i].isComposite && (action.bindings[i].groups ?? "").Contains("KeyboardMouse") && (part == null || string.Equals(action.bindings[i].name, part, StringComparison.OrdinalIgnoreCase))) return i;
            throw new ArgumentException("Keyboard/mouse binding unavailable: " + key);
        }
        public string BindingPath(string key) { int at = Binding(key, out InputAction action); return action.bindings[at].effectivePath; }
        public string BindingLabel(string key) { int at = Binding(key, out InputAction action); return action.GetBindingDisplayString(at); }
        public bool BeginRebind(string key)
        {
            if (!Paused || IsRebinding) return false;
            int at = Binding(key, out InputAction action); string previous = action.bindings[at].overridePath;
            action.Disable(); ClearPending(); suppressPause = true; BindingStatus = "Press a key or mouse button; Esc cancels";
            rebinding = action.PerformInteractiveRebinding(at).WithExpectedControlType("Button")
                .WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Pointer>/position").WithControlsExcluding("<Pointer>/delta").WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape").WithTimeout(10)
                .OnPotentialMatch(operation =>
                {
                    if (Automated && allowedDevices != null)
                        for (int i = operation.candidates.Count - 1; i >= 0; i--)
                            if (!allowedDevices.Contains(operation.candidates[i].device)) operation.RemoveCandidate(operation.candidates[i]);
                    if (operation.candidates.Count > 0) operation.Complete();
                })
                .OnCancel(operation => FinishRebind(action, "Binding cancelled"))
                .OnComplete(operation =>
                {
                    string candidate = action.bindings[at].effectivePath; bool conflict = false;
                    foreach (InputAction other in actions) for (int i = 0; i < other.bindings.Count; i++)
                        if (!(other == action && i == at) && !other.bindings[i].isComposite && (other.bindings[i].groups ?? "").Contains("KeyboardMouse")
                            && string.Equals(other.bindings[i].effectivePath, candidate, StringComparison.OrdinalIgnoreCase)) conflict = true;
                    if (conflict)
                    {
                        if (previous == null) action.RemoveBindingOverride(at); else action.ApplyBindingOverride(at, previous);
                        FinishRebind(action, "Already assigned; previous binding kept");
                    }
                    else { FinishRebind(action, "Binding applied"); Preferences.Save(actions); }
                });
            rebinding.Start(); return true;
        }
        private void FinishRebind(InputAction action, string message)
        { var operation = rebinding; rebinding = null; operation?.Dispose(); action.Enable(); ClearPending(); suppressPause = true; BindingStatus = message; }
        public void CancelRebind() { rebinding?.Cancel(); }
        public bool SaveSettings() => Preferences.Save(actions);
        public void ResetPreferences() { CancelRebind(); Preferences.Reset(actions); BindingStatus = "Default bindings restored"; ClearPending(); }
    }
}
