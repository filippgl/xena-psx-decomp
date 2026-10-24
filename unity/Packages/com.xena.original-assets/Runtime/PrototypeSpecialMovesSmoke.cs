using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    internal sealed class PrototypeSpecialMovesSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report
        { public string result = "FAIL", error; public List<string> checks = new List<string>(); public List<int> rollFrames = new List<int>(); }
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--special-moves-smoke");
            if (at >= 0 && at + 1 < args.Length)
                new GameObject("SpecialMovesSmoke").AddComponent<PrototypeSpecialMovesSmoke>().output = Path.GetFullPath(args[at + 1]);
        }
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type) { if (!finished && type == LogType.Exception) Finish(new Exception(message + stack)); }
        private void Require(bool condition, string message)
        { if (!condition) { Finish(new Exception(message)); throw new Exception(message); } }
        private IEnumerator Ticks(int count) { int end = motor.Ticks + count; while (motor.Ticks < end) yield return new WaitForFixedUpdate(); }
        private IEnumerator Slot(int slot)
        {
            for (int i = 0; i < 160 && combat.Slot != slot; i++) yield return new WaitForFixedUpdate();
            Require(combat.Slot == slot, "Expected " + slot.ToString("X2") + ", got " + combat.Slot.ToString("X2"));
        }
        private IEnumerator Idle()
        { for (int i = 0; i < 160 && combat.Busy; i++) yield return new WaitForFixedUpdate(); Require(!combat.Busy, "Special move failed to finish"); }
        private void Release()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.QueueStateEvent(pad, new GamepadState());
        }
        private void Reset(float distance = 30, bool behind = false)
        {
            Release(); motor.ResetToSpawn(); var capsule = motor.GetComponent<CharacterController>(); capsule.enabled = false;
            motor.transform.position = stage; motor.transform.rotation = Quaternion.Euler(0, orbit.Yaw, 0); capsule.enabled = true;
            combat.Target.transform.position = stage + motor.transform.forward * distance * (behind ? -1 : 1) + Vector3.up * 0.85f;
            combat.Target.ResetTarget(); Physics.SyncTransforms();
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            motor = FindFirstObjectByType<PrototypeMotor>(); combat = motor.GetComponent<PrototypeCombat>(); input = motor.GetComponent<PrototypeInput>();
            orbit = FindFirstObjectByType<PrototypeCamera>(); visual = motor.GetComponentInChildren<XenaAssetPreview>();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); pad = InputSystem.AddDevice<Gamepad>(); input.UseDevices(keyboard, mouse, pad);
            for (int i = 0; i < 10; i++) yield return null;
            // Deterministic temporary platform; original terrain is covered by movement smoke.
            stage = motor.transform.position + Vector3.up * 14;
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "SpecialMoveTestPlatform";
            platform.transform.position = stage - Vector3.up * 0.5f; platform.transform.localScale = new Vector3(80, 1, 80);
            var material = new Material(Shader.Find("Xena/OriginalUnlit")); material.color = new Color(0.32f, 0.36f, 0.28f); platform.GetComponent<Renderer>().sharedMaterial = material;
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift)); yield return Ticks(5);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift, Key.F)); yield return Slot(0x3c);
            Require(visual.CurrentSlot == 0x3c, "Running kick used a basic kick instead of roll");
            Vector3 begin = motor.transform.position, forward = motor.transform.forward; Quaternion facing = motor.transform.rotation;
            int starts = 1, previous = 0x3c;
            for (int i = 0; i < 100; i++)
            {
                if (visual.CurrentSlot == 0x3c && !report.rollFrames.Contains(visual.CurrentFrame)) report.rollFrames.Add(visual.CurrentFrame);
                if (combat.Slot == 0x3c && previous != 0x3c) starts++;
                previous = combat.Slot;
                if (combat.Slot == 0x3c && combat.Frame == 20) { yield return new WaitForEndOfFrame(); Capture("special-running-roll.png"); }
                yield return new WaitForFixedUpdate();
            }
            Require(report.rollFrames.Count == 41 && starts == 1 && combat.Target.Hits == 0, "Roll looped, lost poses or dealt attack damage");
            Require(Vector3.Dot(motor.transform.position - begin, forward) > 3 && Quaternion.Angle(facing, motor.transform.rotation) < 0.1f, "Roll direction/displacement differs");
            report.checks.Add("running kick selects original41-frame roll3C and forward movement");
            report.checks.Add("held running kick does not repeat roll or create kick damage");
            Reset(); yield return Ticks(3); begin = motor.transform.position; forward = motor.transform.forward;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.GetComponent<Renderer>().enabled = false;
            wall.transform.position = stage + forward * 0.9f + Vector3.up; wall.transform.rotation = Quaternion.LookRotation(forward); wall.transform.localScale = new Vector3(3, 3, 0.1f); Physics.SyncTransforms();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift, Key.F)); yield return Slot(0x3c); Release(); yield return Idle();
            Require(Vector3.Dot(motor.transform.position - begin, forward) < 0.8f, "Roll crossed a solid wall"); report.checks.Add("running roll uses capsule wall blocking"); Destroy(wall); yield return null;
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift, Key.F)); yield return Slot(0x3c);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
            Require(input.Paused, "Cannot pause running roll"); int frame = combat.Frame; begin = motor.transform.position;
            Release(); yield return new WaitForSecondsRealtime(0.2f);
            Require(combat.Frame == frame && motor.transform.position == begin, "Paused roll kept advancing"); report.checks.Add("pause freezes running roll animation and movement");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null; Release(); yield return Idle();
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift, Key.F)); yield return Slot(0x3c);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); yield return Ticks(3); Release();
            Require(!combat.Busy && visual.CurrentSlot == 0, "Reset retained roll animation"); report.checks.Add("R cancels running roll and pending motion");
            Reset(4); yield return Ticks(3); begin = motor.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift)); yield return Ticks(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Slot(0x3e); Release(); yield return Idle();
            Require(combat.Target.Hits == 1 && combat.Target.Health == 80 && Vector3.Distance(begin, motor.transform.position) > 3, "Running slash contact/motion missing or duplicated");
            report.checks.Add("running sword selects moving original3E and one practice hit");
            Reset(1.05f); yield return Ticks(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); yield return Slot(0x80);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 3 }); yield return Slot(0x46);
            Require(visual.CurrentSlot == 0x46, "Guard sword pose46 missing"); yield return Ticks(30); yield return new WaitForEndOfFrame(); Capture("special-guard-strike.png");
            yield return Slot(0x80); yield return Ticks(10);
            Require(combat.Target.Hits == 1 && combat.Target.Health == 85, "Guard sword should be one practice kick contact");
            report.checks.Add("guard-sword46 has priority and returns to held guard without repeat");
            Reset(1.05f, true); yield return Ticks(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); yield return Slot(0x80);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); yield return Slot(0x5c); yield return Slot(0x80); yield return Ticks(8);
            Require(combat.Target.Hits == 1 && combat.Target.Health == 85, "Circular guard kick did not hit behind exactly once");
            report.checks.Add("guard-kick5C hits a rear practice target once and does not repeat");
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift, Key.Space)); yield return Ticks(4);
            Require(!motor.Grounded && visual.CurrentSlot == 0x31, "Running jump did not use frontflip31"); Release();
            bool landing = false, airborne = false;
            for (int i = 0; i < 100; i++)
            {
                yield return new WaitForFixedUpdate();
                if (!motor.Grounded) { airborne = true; Require(visual.CurrentSlot == 0x31 && visual.CurrentFrame >= 6 && visual.CurrentFrame <= 32, "Flip airborne pose outside native range"); }
                if (motor.Grounded && visual.CurrentSlot == 0x31) { Require(visual.CurrentFrame >= 33, "Flip landing restarted early poses"); landing = true; }
                if (!motor.Grounded && visual.CurrentFrame == 19) { yield return new WaitForEndOfFrame(); Capture("special-frontflip.png"); }
            }
            Require(airborne && landing && motor.Grounded && visual.CurrentSlot == 0, "Frontflip failed landing/recovery"); report.checks.Add("running jump31 uses original airborne and non-looping landing phases");
            Reset(); yield return Ticks(3); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return Ticks(4); Release();
            Require(visual.CurrentSlot == 0x2f, "Regular jump incorrectly became a frontflip"); yield return Ticks(100); report.checks.Add("ordinary jump remains original2F");
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up, buttons = (1u << (int)GamepadButton.LeftStick) | (1u << (int)GamepadButton.North) });
            yield return Slot(0x3c); Release(); yield return Idle(); report.checks.Add("gamepad sprint-stick plus north selects running roll");
            Reset(); yield return Ticks(3);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up, buttons = (1u << (int)GamepadButton.LeftStick) | (1u << (int)GamepadButton.South) });
            yield return Ticks(4); Release(); Require(visual.CurrentSlot == 0x31, "Gamepad running jump did not flip"); yield return Ticks(100); report.checks.Add("gamepad sprint-stick plus south selects frontflip");
            Reset(1.05f, true); yield return Ticks(3);
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = (1u << (int)GamepadButton.LeftShoulder) | (1u << (int)GamepadButton.North) });
            yield return Slot(0x5c); Release(); yield return Idle(); Require(combat.Target.Hits == 1, "Gamepad guard kick missed"); report.checks.Add("gamepad shoulder plus north selects guard spin kick");
            Destroy(platform); Destroy(material); motor.ResetToSpawn(); report.result = "PASS"; Finish(null);
        }
        private void Capture(string filename)
        { Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, filename), shot.EncodeToPNG()); Destroy(shot); }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true; if (error != null) { report.result = "FAIL"; report.error = error.ToString(); }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "special-moves-player-verification.json"), JsonUtility.ToJson(report, true)); Application.Quit(error == null ? 0 : 1);
        }
    }
}
