using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    internal sealed class PrototypeAdvancedMovesSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report { public string result = "FAIL", error; public List<string> checks = new List<string>(); public List<int> spinCycles = new List<int>(); }
        private Report report = new Report();
        private string output;
        private bool finished;
        private PrototypeMotor motor;
        private PrototypeCombat combat;
        private PrototypeInput input;
        private PrototypeCamera orbit;
        private XenaAssetPreview visual;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad pad;
        private Vector3 stage;
        private int observedCycles;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--advanced-moves-smoke");
            if (at >= 0 && at + 1 < args.Length) new GameObject("AdvancedMovesSmoke").AddComponent<PrototypeAdvancedMovesSmoke>().output = Path.GetFullPath(args[at + 1]);
        }
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type) { if (!finished && type == LogType.Exception) Finish(new Exception(message + stack)); }
        private void Require(bool condition, string message) { if (!condition) { Finish(new Exception(message)); throw new Exception(message); } }
        private IEnumerator Ticks(int count) { int end = motor.Ticks + count; while (motor.Ticks < end) yield return new WaitForFixedUpdate(); }
        private IEnumerator Slot(int slot)
        { for (int i = 0; i < 160 && combat.Slot != slot; i++) yield return new WaitForFixedUpdate(); Require(combat.Slot == slot, "Expected " + slot.ToString("X2") + ", got " + combat.Slot.ToString("X2")); }
        private IEnumerator Idle()
        { for (int i = 0; i < 400 && combat.Busy; i++) yield return new WaitForFixedUpdate(); Require(!combat.Busy, "Advanced move failed recovery"); }
        private void Release()
        { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.QueueStateEvent(pad, new GamepadState()); }
        private void Reset(float distance = 30, bool behind = false)
        {
            Release(); motor.ResetToSpawn(); var capsule = motor.GetComponent<CharacterController>(); capsule.enabled = false;
            motor.transform.position = stage; motor.transform.rotation = Quaternion.Euler(0, orbit.Yaw, 0); capsule.enabled = true;
            combat.Target.transform.position = stage + motor.transform.forward * distance * (behind ? -1 : 1) + Vector3.up * 0.85f;
            combat.Target.ResetTarget(); Physics.SyncTransforms();
        }
        private IEnumerator BeginSpin(bool hold = false)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
            yield return Slot(0x4c); Require(visual.CurrentSlot == 0x4c, "Spin windup pose missing"); if (!hold) Release();
        }
        private IEnumerator ObserveSpin(int extensions = 0)
        {
            observedCycles = 0; int previousSlot = -1, previousFrame = -1, sentPair = -1, releaseAt = -1;
            bool ending = false;
            if (extensions > 0) InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            for (int i = 0; i < 420 && combat.IsSpinning; i++)
            {
                int slot = combat.Slot, frame = visual.CurrentFrame;
                if (slot == 0x4d && (previousSlot != slot || frame < previousFrame)) observedCycles++;
                if (slot == 0x4e) ending = true;
                int pair = (observedCycles - 1) / 2;
                if (extensions > 0 && slot == 0x4d && observedCycles % 2 == 1 && frame >= 1 && sentPair < pair && pair < extensions)
                { InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); sentPair = pair; releaseAt = motor.Ticks + 3; }
                if (releaseAt >= 0 && motor.Ticks >= releaseAt)
                { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); releaseAt = -1; }
                if (slot == 0x4d && frame == 3) { yield return new WaitForEndOfFrame(); Capture("advanced-sword-spin.png"); }
                previousSlot = slot; previousFrame = frame; yield return new WaitForFixedUpdate();
            }
            Require(!combat.IsSpinning && ending, "Spin failed native ending4E"); report.spinCycles.Add(observedCycles);
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            motor = FindFirstObjectByType<PrototypeMotor>(); combat = motor.GetComponent<PrototypeCombat>(); input = motor.GetComponent<PrototypeInput>();
            orbit = FindFirstObjectByType<PrototypeCamera>(); visual = motor.GetComponentInChildren<XenaAssetPreview>();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); pad = InputSystem.AddDevice<Gamepad>(); input.UseDevices(keyboard, mouse, pad);
            for (int i = 0; i < 10; i++) yield return null;
            stage = motor.transform.position + Vector3.up * 14;
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "AdvancedMoveTestPlatform";
            platform.transform.position = stage - Vector3.up * 0.5f; platform.transform.localScale = new Vector3(80, 1, 80);
            var material = new Material(Shader.Find("Xena/OriginalUnlit")); material.color = new Color(0.32f, 0.36f, 0.28f); platform.GetComponent<Renderer>().sharedMaterial = material;
            Reset(1.05f, true); yield return Ticks(3); yield return BeginSpin(); yield return ObserveSpin();
            Require(observedCycles == 2 && combat.Target.Hits == 2 && combat.Target.Health == 60, "Short spin did not make two single-contact radial cycles"); report.checks.Add("tap sword-kick uses4C-two4Dcycles-4E with radial practice contacts");
            Reset(); yield return Ticks(3); yield return BeginSpin(true); yield return ObserveSpin(); yield return Ticks(12);
            Require(observedCycles == 2 && !combat.Busy, "Held chord extended or restarted spin"); report.checks.Add("held chord neither extends nor restarts spin");
            Reset(1.05f, true); yield return Ticks(3); yield return BeginSpin(); yield return ObserveSpin(1); Release();
            Require(observedCycles == 4 && combat.Target.Hits == 4 && combat.Target.Health == 20, "Fresh kick did not add one pair or duplicated contact"); report.checks.Add("fresh kick while sword held adds one native pair");
            Reset(); yield return Ticks(3); yield return BeginSpin(); yield return ObserveSpin(99); Release();
            Require(observedCycles == 14, "Spin extension exceeded or missed native6-pair budget"); report.checks.Add("spin extension budget is bounded to six additional pairs");
            Reset(1.05f); yield return Ticks(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Slot(0x51); yield return Ticks(2);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); yield return Slot(0x4c); Release(); yield return ObserveSpin();
            Require(combat.Target.Hits == 2 && combat.Target.Health == 60, "Short chord stagger leaked a basic sword hit"); report.checks.Add("short chord stagger replaces pre-contact single attack without extra hit");
            Reset(1.05f); yield return Ticks(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Slot(0x51); yield return Ticks(12);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); yield return Idle(); Release();
            Require(!combat.IsSpinning && combat.Target.Hits == 1, "Late chord retroactively became spin or kick"); report.checks.Add("late chord does not replace an already-contacting attack");
            Reset(); yield return Ticks(3); yield return BeginSpin(); yield return Slot(0x4d);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null; Require(input.Paused, "Cannot pause spin");
            int frozen = combat.Frame; Release(); yield return new WaitForSecondsRealtime(0.2f); Require(combat.Frame == frozen, "Paused spin advanced");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null; Release(); yield return Idle(); report.checks.Add("pause freezes spin and resume recovers");
            Reset(); yield return Ticks(3); yield return BeginSpin(); yield return Slot(0x4d);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); yield return Ticks(3); Release(); Require(!combat.Busy && visual.CurrentSlot == 0, "Reset retained spin phase"); report.checks.Add("R clears spin phase and repeat latch");
            Reset(1.05f); yield return Ticks(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 3 }); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); yield return Slot(0x46); Release(); yield return Idle();
            Require(combat.Target.Hits == 1 && combat.Target.Health == 85, "Guard lost priority to sword-kick chord");
            Reset(); yield return Ticks(3); yield return BeginSpin(); yield return Slot(0x4d);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            for (int i = 0; i < 240 && combat.IsSpinning; i++) yield return new WaitForFixedUpdate();
            Require(combat.IsBlocking, "Spin exit left an unguarded simulation step despite held block"); Release(); yield return Ticks(3);
            report.checks.Add("guard takes chord priority and resumes in the same spin-exit tick");
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q)); yield return Ticks(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q, Key.F)); yield return Ticks(4);
            var chakram = motor.GetComponent<PrototypeChakram>(); Require(chakram.State == PrototypeChakram.Mode.Throwing && !combat.IsSpinning, "Chakram lost priority to spin chord"); Release();
            for (int i = 0; i < 180 && chakram.Active; i++) yield return new WaitForFixedUpdate(); Require(!chakram.Active, "Chakram failed recall after chord"); report.checks.Add("chakram aim keeps throw priority over chord");
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = (1u << (int)GamepadButton.West) | (1u << (int)GamepadButton.North) }); yield return Slot(0x4c);
            yield return ObserveSpin(); Release(); Require(observedCycles == 2, "Gamepad held chord extended"); report.checks.Add("gamepad west-north chord uses the same bounded spin");
            Reset(); yield return Ticks(3); Vector3 begin = motor.transform.position, back = -motor.transform.forward; Quaternion facing = motor.transform.rotation;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S, Key.Space)); yield return Ticks(4); Require(!motor.Grounded && visual.CurrentSlot == 0x35, "Backward jump did not use35"); Release();
            bool landed = false;
            for (int i = 0; i < 85; i++)
            {
                yield return new WaitForFixedUpdate();
                if (!motor.Grounded) { Require(visual.CurrentSlot == 0x35 && visual.CurrentFrame >= 4 && visual.CurrentFrame <= 20, "Backflip airborne phase differs"); Require(Quaternion.Angle(facing, motor.transform.rotation) < 0.1f, "Backflip turned character"); }
                if (motor.Grounded && visual.CurrentSlot == 0x35) { Require(visual.CurrentFrame >= 21, "Backflip landing restarted early poses"); landed = true; }
                if (!motor.Grounded && visual.CurrentFrame == 12) { yield return new WaitForEndOfFrame(); Capture("advanced-backflip.png"); }
            }
            Require(landed && visual.CurrentSlot == 0 && Vector3.Dot(motor.transform.position - begin, back) > 0.8f, "Backflip displacement/landing recovery failed"); report.checks.Add("backward jump35 preserves facing, moves back and lands once");
            Reset(); yield return Ticks(3); motor.transform.rotation = Quaternion.Euler(0, orbit.Yaw + 180, 0); facing = motor.transform.rotation;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Space)); yield return Ticks(4); Release();
            Require(visual.CurrentSlot == 0x35 && Quaternion.Angle(facing, motor.transform.rotation) < 0.1f, "Camera-opposed backward request did not backflip"); yield return Ticks(85); report.checks.Add("backflip is character-relative when camera faces opposite");
            Reset(); yield return Ticks(3); begin = motor.transform.position; back = -motor.transform.forward;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.GetComponent<Renderer>().enabled = false; wall.transform.position = stage + back * 0.8f + Vector3.up;
            wall.transform.rotation = Quaternion.LookRotation(back); wall.transform.localScale = new Vector3(3, 3, 0.1f); Physics.SyncTransforms();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S, Key.Space)); yield return Ticks(4); Release(); yield return Ticks(85);
            Require(Vector3.Dot(motor.transform.position - begin, back) < 0.8f, "Backflip crossed solid wall"); report.checks.Add("backflip respects capsule wall collision"); Destroy(wall); yield return null;
            Reset(1.05f, true); yield return Ticks(3); begin = motor.transform.position; facing = motor.transform.rotation;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S)); InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Slot(0x51); Release(); yield return Idle();
            Require(combat.Target.Hits == 1 && combat.Target.Health == 80 && Quaternion.Angle(facing, motor.transform.rotation) > 170 && Vector3.Distance(begin, motor.transform.position) < 0.08f, "Backward sword failed facing/hit/movement lock"); report.checks.Add("backward sword turns before original51 and hits rear target");
            Reset(1.05f, true); yield return Ticks(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S, Key.F)); yield return Slot(0x61); Release(); yield return Idle();
            Require(combat.Target.Hits == 1 && combat.Target.Health == 85, "Backward kick failed rear target"); report.checks.Add("backward kick turns before original61");
            Reset(1.05f); yield return Ticks(3); motor.transform.rotation = Quaternion.Euler(0, orbit.Yaw + 180, 0);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Slot(0x51); Release(); yield return Idle();
            Require(combat.Target.Hits == 1 && Mathf.Abs(Mathf.DeltaAngle(motor.transform.eulerAngles.y, orbit.Yaw)) < 0.1f, "Backward attack used keyboard S instead of body-relative direction"); report.checks.Add("directional attack uses actual facing rather than fixed S");
            Destroy(platform); Destroy(material); motor.ResetToSpawn(); report.result = "PASS"; Finish(null);
        }
        private void Capture(string name) { Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, name), shot.EncodeToPNG()); Destroy(shot); }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true; if (error != null) { report.result = "FAIL"; report.error = error.ToString(); }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "advanced-moves-player-verification.json"), JsonUtility.ToJson(report, true)); Application.Quit(error == null ? 0 : 1);
        }
    }
}
