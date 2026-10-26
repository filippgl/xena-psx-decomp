using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    internal sealed class PrototypeCombatSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report
        { public string result = "FAIL", error, device; public List<string> checks = new List<string>(); public int swordComboHits, kickComboHits; public float swordRootTravel; }
        private Report report;
        private string output;
        private bool finished;
        private PrototypeMotor motor;
        private PrototypeCombat combat;
        private PrototypeInput input;
        private XenaAssetPreview visual;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad pad;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--combat-smoke");
            if (at < 0 || at + 1 >= args.Length) return;
            var runner = new GameObject("CombatSmoke").AddComponent<PrototypeCombatSmoke>(); runner.output = Path.GetFullPath(args[at + 1]);
        }
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type) { if (!finished && report != null && type == LogType.Exception) Finish(new Exception(message + "\n" + stack)); }
        private void Require(bool condition, string message)
        { if (!condition) { Finish(new Exception(message)); throw new Exception(message); } }
        private IEnumerator Ticks(int count) { int end = motor.Ticks + count; while (motor.Ticks < end) yield return new WaitForFixedUpdate(); }
        private IEnumerator Slot(int slot, int limit = 100)
        {
            for (int i = 0; i < limit && combat.Slot != slot; i++) yield return new WaitForFixedUpdate();
            Require(combat.Slot == slot, "Expected slot " + slot.ToString("X2") + ", got " + combat.Slot.ToString("X2"));
        }
        private IEnumerator Idle()
        {
            for (int i = 0; i < 140 && combat.Busy; i++) yield return new WaitForFixedUpdate();
            Require(!combat.Busy, "Attack failed to recover");
        }
        private IEnumerator Tap(int action)
        {
            if (action == 1) InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            else InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
            yield return null; yield return null; yield return Ticks(1);
            Release(); yield return null; yield return null;
        }
        private void Release()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.QueueStateEvent(pad, new GamepadState());
        }
        private void PlaceTarget(float distance, bool behind = false)
        {
            combat.Target.transform.position = motor.transform.position + motor.transform.forward * distance * (behind ? -1 : 1) + Vector3.up * 0.85f;
            combat.Target.ResetTarget(); Physics.SyncTransforms();
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            report = new Report { device = SystemInfo.graphicsDeviceName };
            motor = FindFirstObjectByType<PrototypeMotor>(); input = motor.GetComponent<PrototypeInput>();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); pad = InputSystem.AddDevice<Gamepad>(); input.UseDevices(keyboard, mouse, pad);
            for (int i = 0; i < 10; i++) yield return null;
            combat = motor.GetComponent<PrototypeCombat>(); visual = motor.GetComponentInChildren<XenaAssetPreview>();
            Require(combat != null && combat.Target != null, "Practice target unavailable"); report.checks.Add("practice target spawns with original player");
            Release(); motor.ResetToSpawn(); yield return Ticks(2); PlaceTarget(1.05f);
            Vector3 beforeSwing = motor.transform.position; float travelBefore = combat.RootTravel;
            yield return Tap(1); Require(combat.Slot == 0x51 && visual.CurrentSlot == 0x51, "Mouse sword did not sample original51");
            Vector3 start = motor.transform.position; float rootAtStart = combat.RootTravel;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return Ticks(8);
            // Input does not move Xena during an attack; only the clip's original root motion does.
            Vector3 slid = motor.transform.position - start; slid.y = 0;
            Require(slid.magnitude <= combat.RootTravel - rootAtStart + 0.005f, "Player slid during sword attack beyond the root motion: " + slid.magnitude.ToString("F3"));
            report.checks.Add("attacks ignore movement input; only the original root motion moves Xena");
            Release(); yield return new WaitForEndOfFrame(); Capture("combat-sword.png"); yield return Idle();
            // Native port (xena-console 2026-10-22, RAM 8804..8832, yaw ~35 deg): sword 0x51 carries Xena 481.6 units forward
            // (per-axis truncation makes the total depend on the yaw by up to ~1 unit per frame: 20 units tolerance).
            report.swordRootTravel = combat.RootTravel - travelBefore;
            Require(Mathf.Abs(report.swordRootTravel * 400 - 481.6f) < 20, "Sword 0x51 root motion differs from the native 481.6 units: " + (report.swordRootTravel * 400).ToString("F1"));
            report.checks.Add("sword 0x51 root motion matches the native port (481.6 units)");
            Require(combat.Target.Hits == 1 && combat.Target.Health == 80, "Sword contact was missing or duplicated"); report.checks.Add("mouse sword: one contact per swing");
            // The sword's root motion carried Xena 1.2 m on: put the dummy back in front for the kick (hits kept).
            { int hits = combat.Target.Hits, health = combat.Target.Health; combat.Target.transform.position = motor.transform.position + motor.transform.forward * 1.05f + Vector3.up * 0.85f; Physics.SyncTransforms(); Require(combat.Target.Hits == hits && combat.Target.Health == health, "Dummy state changed on move"); }
            yield return Tap(2); Require(combat.Slot == 0x61 && visual.CurrentSlot == 0x61, "F did not sample original kick61");
            yield return Ticks(10); yield return new WaitForEndOfFrame(); Capture("combat-kick.png"); yield return Idle();
            Require(combat.Target.Hits == 2 && combat.Target.Health == 65, "Kick contact differs"); report.checks.Add("keyboard kick: original61 and one contact");
            PlaceTarget(5); yield return Tap(1); yield return Idle(); Require(combat.Target.Hits == 0, "Sword hit a distant target"); report.checks.Add("practice reach rejects distant targets");
            PlaceTarget(1.05f, true); yield return Tap(1); yield return Idle(); Require(combat.Target.Hits == 0, "Sword hit behind player"); report.checks.Add("attack direction rejects targets behind player");
            PlaceTarget(1.05f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.GetComponent<Renderer>().enabled = false;
            wall.transform.position = motor.transform.position + motor.transform.forward * 0.55f + Vector3.up;
            wall.transform.rotation = motor.transform.rotation; wall.transform.localScale = new Vector3(2, 2, 0.1f); Physics.SyncTransforms();
            yield return Tap(1); yield return Idle(); Require(combat.Target.Hits == 0, "Sword crossed practice wall"); report.checks.Add("practice hit occlusion respects solid walls"); Destroy(wall); yield return null;

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            yield return Tap(1); yield return Ticks(3); yield return Tap(1); yield return Slot(0x52);
            yield return Tap(1); yield return Slot(0x3a);
            yield return Tap(2); // Mixed request expires before this long finisher ends.
            yield return Idle(); yield return Ticks(3);
            report.swordComboHits = combat.Target.Hits;
            Require(combat.Target.Hits == 3 && combat.Target.Health == 40, "Sword triple did not hit once per51/52/3A"); report.checks.Add("buffered original sword chain51-52-3A");
            Require(!combat.Busy, "Expired buffered kick played after finisher"); report.checks.Add("expired buffered action is discarded");
            motor.ResetToSpawn(); PlaceTarget(1.05f);
            yield return Tap(2); yield return Ticks(2); yield return Tap(2); yield return Slot(0x62);
            yield return Ticks(8); yield return Tap(2); yield return Slot(0x5a); yield return Idle();
            report.kickComboHits = combat.Target.Hits;
            Require(combat.Target.Hits == 3 && combat.Target.Health == 55, "Kick triple did not hit once per61/62/5A"); report.checks.Add("buffered original kick chain61-62-5A");

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            yield return Tap(1); yield return Tap(2); yield return Slot(0x57);
            Require(visual.CurrentSlot == 0x57, "Sword-kick bridge pose missing");
            int beforeBridgeHits = combat.Target.Hits;
            yield return Slot(0x62);
            Require(combat.Frame == 5 && visual.CurrentFrame == 5, "Sword-kick did not resume62 at native frame5");
            Require(combat.Target.Hits == beforeBridgeHits, "Bridge generated an extra practice hit");
            report.checks.Add("sword-kick bridge57 resumes62 at frame5 without extra contact");
            yield return Tap(1); yield return Ticks(40);
            Require(combat.Slot == 0x62, "Mixed finisher interrupted62 too early");
            yield return Slot(0x4f); yield return new WaitForEndOfFrame(); Capture("combat-mixed.png");
            yield return Idle();
            Require(combat.Target.Hits == 3 && combat.Target.Health == 45, "Sword-kick-sword finisher missing or duplicated");
            report.checks.Add("sword-kick-sword51-57-62-4F keeps accepted finisher beyond input expiry");

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            yield return Tap(2); yield return Tap(2); yield return Slot(0x62);
            yield return Tap(1); yield return Slot(0x55);
            Require(visual.CurrentSlot == 0x55, "Kick-kick-sword finisher pose missing"); yield return Idle();
            Require(combat.Target.Hits == 3 && combat.Target.Health == 50, "Kick-kick-sword contacts differ");
            report.checks.Add("kick-kick-sword61-62-55 uses original distinct finisher");

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            yield return Tap(2); yield return Tap(1); yield return Slot(0x56);
            Require(visual.CurrentSlot == 0x56, "Kick-sword bridge pose missing");
            yield return Slot(0x52);
            Require(combat.Frame == 2 && visual.CurrentFrame == 2, "Kick-sword did not resume52 at native frame2");
            report.checks.Add("kick-sword bridge56 resumes52 at frame2");
            yield return Tap(1); yield return Slot(0x53); yield return Idle();
            Require(combat.Target.Hits == 3 && combat.Target.Health == 45, "Kick-sword-sword contacts differ");
            report.checks.Add("kick-sword-sword61-56-52-53 uses original distinct finisher");

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            yield return Tap(1); yield return Tap(2); yield return Slot(0x57);
            yield return Idle(); yield return Ticks(4);
            Require(combat.Target.Hits == 2 && combat.Target.Health == 65 && !combat.Busy, "Two-action mixed series invented a third attack");
            report.checks.Add("sword-kick pair recovers without an unrequested finisher");

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            yield return Tap(1); yield return Tap(2); yield return Slot(0x57);
            motor.ResetToSpawn(); PlaceTarget(1.05f); yield return Ticks(90);
            Require(!combat.Busy && combat.Target.Hits == 0, "Reset during bridge retained a chain or contact");
            report.checks.Add("reset during mixed bridge cancels animation and reserved chain");
            motor.ResetToSpawn(); PlaceTarget(1.05f);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Ticks(80);
            Require(combat.Target.Hits == 1 && !combat.Busy, "Held mouse generated repeated attacks"); report.checks.Add("holding attack does not auto-repeat"); Release(); yield return Ticks(2);

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); yield return Slot(0x80);
            Require(visual.CurrentSlot == 0x80, "Guard did not render original80");
            combat.Target.Pulse(); Require(combat.Health == 100 && combat.BlockedHits == 1, "Front guard did not protect player"); report.checks.Add("held mouse guard blocks frontal practice hit");
            combat.Target.Retaliates = true; yield return Ticks(125); combat.Target.Retaliates = false;
            Require(combat.Health == 100 && combat.BlockedHits == 2, "Automatic practice attack did not respect held guard"); report.checks.Add("automatic practice attack uses fixed simulation and guard");
            yield return new WaitForEndOfFrame(); Capture("combat-block.png");
            // Let fixed-step catch-up after the slow capture finish before timing the next press.
            yield return null;
            PlaceTarget(1.05f, true); combat.Target.Pulse(); Require(combat.Health == 90, "Guard incorrectly blocked rear hit"); report.checks.Add("rear practice hit bypasses guard");
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 3 }); yield return Ticks(3);
            Require(combat.Slot == 0x46 && visual.CurrentSlot == 0x46, "Guard-sword did not select native46");
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); yield return Ticks(3);
            Release(); yield return Idle(); yield return Ticks(3);
            Require(!combat.Busy && combat.Target.Hits == 0, "Guard released a stale buffered attack"); report.checks.Add("guard-sword46 finishes without a stale attack on release");
            PlaceTarget(1.05f); combat.Target.Pulse(); Require(combat.Health == 80, "Unguarded hit did not damage player"); report.checks.Add("unguarded practice hit damages player");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); yield return Ticks(3); Release();
            Require(combat.Health == 100 && combat.Target.Health == 100 && !combat.Busy, "R did not reset combat state"); report.checks.Add("R restores player and target state");

            PlaceTarget(1.05f); yield return Tap(1); yield return Ticks(2);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
            Require(input.Paused, "Could not pause during sword attack"); int pausedFrame = combat.Frame;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); yield return new WaitForSecondsRealtime(0.2f);
            Require(combat.Frame == pausedFrame, "Combat advanced while paused"); report.checks.Add("pause freezes combat and its buffer clock");
            Release(); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null; Release();
            Require(!input.Paused, "Could not resume combat"); yield return Idle(); yield return Ticks(5);
            Require(combat.Target.Hits == 1 && !combat.Busy, "Paused input leaked after resume"); report.checks.Add("paused attack input does not leak after resume");

            motor.ResetToSpawn(); PlaceTarget(1.05f);
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 1u << (int)GamepadButton.West }); yield return Slot(0x51); Release(); yield return Idle();
            Require(combat.Target.Hits == 1, "Gamepad sword failed"); report.checks.Add("gamepad west button sword");
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 1u << (int)GamepadButton.North }); yield return Slot(0x61); Release(); yield return Idle();
            Require(combat.Target.Hits == 2, "Gamepad kick failed"); report.checks.Add("gamepad north button kick");
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 1u << (int)GamepadButton.LeftShoulder }); yield return Slot(0x80); Release(); yield return Ticks(3);
            Require(!combat.Busy, "Gamepad guard did not release"); report.checks.Add("gamepad shoulder guard and release");
            motor.ResetToSpawn(); PlaceTarget(1.05f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return Ticks(4); Release();
            Require(!motor.Grounded, "No jump for airborne action test"); yield return Tap(1); yield return Ticks(80);
            Require(combat.Target.Hits == 0 && !combat.Busy, "Airborne attack became a stale landing attack"); report.checks.Add("airborne unsupported attack is discarded");
            motor.ResetToSpawn(); PlaceTarget(1.05f); combat.Target.ReceiveHit(100); yield return Tap(1); yield return Idle();
            Require(combat.Target.Health == 0 && combat.Target.Hits == 1, "Defeated target kept taking hits"); report.checks.Add("defeated practice target ignores further hits");
            motor.ResetToSpawn(); PlaceTarget(20);
            motor.transform.rotation = Quaternion.Euler(0, FindFirstObjectByType<PrototypeCamera>().Yaw, 0);
            Vector3 evadeStart = motor.transform.position, left = -motor.transform.right;
            Quaternion evadeFacing = motor.transform.rotation;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A)); yield return Slot(0x28);
            Require(visual.CurrentSlot == 0x28, "Left cartwheel native pose missing");
            yield return Ticks(20); yield return new WaitForEndOfFrame(); Capture("combat-cartwheel.png");
            yield return Ticks(40);
            Require(combat.IsBlocking, "Held block-left repeated cartwheel instead of returning to guard");
            Require(Vector3.Dot(motor.transform.position - evadeStart, left) > 0.5f && Quaternion.Angle(evadeFacing, motor.transform.rotation) < 0.1f, "Cartwheel moved/faced wrong direction");
            Require(combat.Target.Hits == 0, "Defensive cartwheel caused a sword contact");
            report.checks.Add("block-left native cartwheel28 moves sideways without turning or damage");
            report.checks.Add("held dodge direction performs one cartwheel then guard"); Release(); yield return Ticks(3);
            motor.ResetToSpawn(); PlaceTarget(20);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); yield return Slot(0x29);
            Require(visual.CurrentSlot == 0x29, "Right cartwheel pose missing"); Release(); yield return Idle();
            report.checks.Add("block-right native cartwheel29 completes after releasing input");
            motor.ResetToSpawn(); PlaceTarget(20);
            motor.transform.rotation = Quaternion.Euler(0, FindFirstObjectByType<PrototypeCamera>().Yaw + 180, 0);
            evadeStart = motor.transform.position; Vector3 backward = -motor.transform.forward;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 });
            // Guard now aligns the camera before this same simulation tick;
            // backward is S even when the camera started opposite the body.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S)); yield return Slot(0x2a);
            Require(visual.CurrentSlot == 0x2a, "Back handspring did not use character-relative direction");
            yield return Ticks(30); Release(); yield return Idle();
            Require(Vector3.Dot(motor.transform.position - evadeStart, backward) > 0.5f, "Back handspring moved forward");
            report.checks.Add("handspring2A uses character backward even when camera faces opposite");
            motor.ResetToSpawn(); PlaceTarget(20);
            motor.transform.rotation = Quaternion.Euler(0, FindFirstObjectByType<PrototypeCamera>().Yaw, 0);
            evadeStart = motor.transform.position; left = -motor.transform.right;
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.GetComponent<Renderer>().enabled = false;
            wall.transform.position = evadeStart + left * 0.9f + Vector3.up;
            wall.transform.rotation = Quaternion.LookRotation(left); wall.transform.localScale = new Vector3(3, 3, 0.2f); Physics.SyncTransforms();
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
            yield return Slot(0x28); Release(); yield return Idle();
            Require(Vector3.Dot(motor.transform.position - evadeStart, left) < 0.75f, "Cartwheel crossed solid wall");
            report.checks.Add("cartwheel displacement respects capsule wall collision"); Destroy(wall); yield return null;
            motor.ResetToSpawn(); PlaceTarget(20);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 }); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
            yield return Slot(0x28); Release(); motor.ResetToSpawn(); yield return Ticks(3);
            Require(!combat.Busy && visual.CurrentSlot == 0, "Reset retained cartwheel pose"); report.checks.Add("reset clears defensive acrobatics");
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right, buttons = 1u << (int)GamepadButton.LeftShoulder });
            yield return Slot(0x29); Release(); yield return Idle(); report.checks.Add("gamepad shoulder plus stick performs cartwheel");
            motor.ResetToSpawn(); report.result = "PASS"; Finish(null);
        }
        private void Capture(string filename)
        {
            Directory.CreateDirectory(output); Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG()); Destroy(image);
        }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true;
            if (error != null) { report.result = "FAIL"; report.error = error.ToString(); }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "combat-player-verification.json"), JsonUtility.ToJson(report, true));
            Application.Quit(error == null ? 0 : 1);
        }
    }
}
