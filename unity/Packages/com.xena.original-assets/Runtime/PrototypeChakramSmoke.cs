using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    internal sealed class PrototypeChakramSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report { public string result = "FAIL", error; public List<string> checks = new List<string>(); }
        private Report report = new Report();
        private string output;
        private bool finished;
        private PrototypeMotor motor;
        private PrototypeInput input;
        private PrototypeChakram chakram;
        private PrototypeCombat combat;
        private PrototypeCamera orbit;
        private XenaAssetPreview visual;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad pad;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--chakram-smoke");
            if (at < 0 || at + 1 >= args.Length) return;
            new GameObject("ChakramSmoke").AddComponent<PrototypeChakramSmoke>().output = Path.GetFullPath(args[at + 1]);
        }
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type) { if (!finished && type == LogType.Exception) Finish(new Exception(message + stack)); }
        private void Require(bool condition, string message)
        { if (!condition) { Finish(new Exception(message)); throw new Exception(message); } }
        private IEnumerator Ticks(int count) { int end = motor.Ticks + count; while (motor.Ticks < end) yield return new WaitForFixedUpdate(); }
        private IEnumerator Mode(PrototypeChakram.Mode mode, int limit = 240)
        {
            for (int i = 0; i < limit && chakram.State != mode; i++) yield return new WaitForFixedUpdate();
            Require(chakram.State == mode, "Expected chakram " + mode + ", got " + chakram.State);
        }
        private void Release()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.QueueStateEvent(pad, new GamepadState());
        }
        private void PlaceTarget(float distance)
        {
            combat.Target.transform.position = motor.transform.position + Quaternion.Euler(0, orbit.Yaw, 0) * Vector3.forward * distance + Vector3.up * 1.1f;
            combat.Target.ResetTarget(); Physics.SyncTransforms();
        }
        private IEnumerator Throw()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q)); yield return Mode(PrototypeChakram.Mode.Aiming);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Mode(PrototypeChakram.Mode.Throwing);
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            motor = FindFirstObjectByType<PrototypeMotor>(); input = motor.GetComponent<PrototypeInput>();
            chakram = motor.GetComponent<PrototypeChakram>(); combat = motor.GetComponent<PrototypeCombat>();
            orbit = FindFirstObjectByType<PrototypeCamera>(); visual = motor.GetComponentInChildren<XenaAssetPreview>();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); pad = InputSystem.AddDevice<Gamepad>();
            input.UseDevices(keyboard, mouse, pad); for (int i = 0; i < 10; i++) yield return null;
            Require(chakram != null, "Chakram component missing"); motor.ResetToSpawn();
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(0, 150) }); yield return null; yield return null;
            Require(Mathf.Abs(orbit.Pitch) < 1, "Could not aim horizontally through actual mouse input");
            PlaceTarget(2); Vector3 start = motor.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q, Key.W, Key.F)); yield return Mode(PrototypeChakram.Mode.Aiming); yield return Ticks(5);
            Require(Vector3.Distance(start, motor.transform.position) < 0.08f && !combat.Busy, "Aim leaked movement or kick");
            report.checks.Add("Q aims with character stationary and suppresses melee");
            yield return Throw();
            Require(visual.CurrentSlot == 0x64 && combat.Slot != 0x51, "Aim-click used sword instead of original throw64");
            report.checks.Add("aim plus mouse sword button plays native throw64");
            for (int i = 0; i < 80 && !chakram.Flying; i++) yield return new WaitForFixedUpdate();
            Require(chakram.Flying && chakram.Visible && chakram.Throws == 1, "Original chakram did not launch");
            var projectile = GameObject.Find("OriginalChakramProjectile");
            Require(projectile != null, "Projectile original resource absent");
            MeshFilter[] meshes = projectile.GetComponentsInChildren<MeshFilter>(); int triangles = 0;
            foreach (MeshFilter mesh in meshes) triangles += mesh.sharedMesh.triangles.Length / 3;
            Require(meshes.Length == 2 && triangles == 46, "Projectile is not standalone original46-triangle Chakram");
            report.checks.Add("standalone original2-mesh46-triangle textured chakram launches once");
            yield return Ticks(18);
            Vector3 normal = chakram.DiscNormal, rim = projectile.transform.right;
            for (int i = 0; i < 6; i++)
            {
                yield return Ticks(1);
                Require(Mathf.Abs(Vector3.Dot(chakram.DiscNormal, chakram.FlightDirection)) < 0.001f && Vector3.Angle(normal, chakram.DiscNormal) < 0.1f, "Spinning disc normal wobbles or is not in flight plane");
            }
            Require(Vector3.Angle(rim, projectile.transform.right) > 30, "Disc does not rotate around its own normal");
            report.checks.Add("disc spins around its own stable normal with flight direction in its plane");
            var spinTrail = projectile.GetComponentInChildren<LineRenderer>();
            Require(chakram.TrailPoints >= 3 && chakram.TrailPoints <= 12 && spinTrail.sharedMaterial.shader.isSupported && spinTrail.sharedMaterial.shader.name == "Xena/ChakramTrail", "Spin trail missing, unbounded or shader stripped");
            Require(spinTrail.colorGradient.Evaluate(0).a == 0 && spinTrail.colorGradient.Evaluate(1).a > 0.5f, "Spin trail does not fade into air");
            report.checks.Add("rim rotation leaves a bounded widening fading trail with supported build shader");
            Require(combat.Target.Hits == 1 && combat.Target.Health == 65, "Chakram target contact missing or repeated");
            report.checks.Add("swept chakram hits practice target once per throw");
            Vector3 beforeSteer = chakram.FlightDirection;
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(150, 0) }); yield return null; yield return null; yield return Ticks(8);
            Require(chakram.Flying && !chakram.Returning && Vector3.Angle(beforeSteer, chakram.FlightDirection) > 5, "Mouse did not steer flying chakram");
            Require(Mathf.Abs(Vector3.Dot(chakram.DiscNormal, chakram.FlightDirection)) < 0.001f, "Steering tipped disc out of flight plane");
            report.checks.Add("actual mouse motion steers outgoing flight");
            Require(Vector3.Distance(orbit.transform.position, chakram.ProjectilePosition) < 5, "Camera did not follow flight");
            report.checks.Add("camera follows chakram flight");
            yield return new WaitForEndOfFrame(); Capture("chakram-flight.png");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q, Key.Escape)); yield return null; yield return null;
            Require(input.Paused, "Cannot pause flight"); Vector3 paused = chakram.ProjectilePosition;
            Vector3[] pausedTrail = new Vector3[spinTrail.positionCount]; spinTrail.GetPositions(pausedTrail);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q)); yield return new WaitForSecondsRealtime(0.2f);
            Require(chakram.ProjectilePosition == paused, "Flight advanced while paused"); report.checks.Add("pause freezes chakram flight");
            Require(spinTrail.positionCount == pausedTrail.Length, "Paused trail kept aging");
            for (int i = 0; i < pausedTrail.Length; i++) Require(spinTrail.GetPosition(i) == pausedTrail[i], "Paused spin trail moved");
            report.checks.Add("pause freezes spin trail geometry and lifetime");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q, Key.Escape)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q)); yield return Ticks(2);
            Release(); yield return Mode(PrototypeChakram.Mode.Catching);
            Require(chakram.Catches == 1 && !chakram.Visible && visual.CurrentSlot == 0x65, "Recall did not play original catch65");
            Require(chakram.TrailPoints == 0, "Catch retained old flight trail");
            report.checks.Add("release Q recalls along flight path and plays catch65"); yield return Mode(PrototypeChakram.Mode.Ready);
            Require(combat.Target.Hits == 1, "Returning chakram duplicated damage"); report.checks.Add("return does not duplicate practice damage");
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Ticks(3); Release();
            Require(combat.Slot == 0x51, "Melee did not resume after catch"); report.checks.Add("normal sword resumes after catch"); yield return Ticks(35);

            motor.ResetToSpawn(); PlaceTarget(2);
            Vector3 forward = Quaternion.Euler(0, orbit.Yaw, 0) * Vector3.forward;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.GetComponent<Renderer>().enabled = false;
            wall.transform.position = motor.transform.position + forward * 0.8f + Vector3.up;
            wall.transform.rotation = Quaternion.LookRotation(forward); wall.transform.localScale = new Vector3(3, 3, 0.1f); Physics.SyncTransforms();
            yield return Throw();
            for (int i = 0; i < 180 && chakram.Catches == 0; i++) yield return new WaitForFixedUpdate();
            Release(); Require(chakram.WallContacts == 1 && chakram.Catches == 1 && combat.Target.Hits == 0, "Chakram crossed wall or failed to return");
            report.checks.Add("solid wall stops outgoing flight and protects target behind it");
            yield return Mode(PrototypeChakram.Mode.Ready); Destroy(wall); yield return null;
            motor.ResetToSpawn(); PlaceTarget(20);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q)); yield return Mode(PrototypeChakram.Mode.Aiming);
            Release(); yield return Mode(PrototypeChakram.Mode.Ready);
            Require(chakram.Throws == 0, "Aim cancellation threw without click"); report.checks.Add("releasing aim without clicking cancels safely");
            yield return Throw();
            for (int i = 0; i < 80 && !chakram.Flying; i++) yield return new WaitForFixedUpdate();
            Require(chakram.Flying, "No flight for reset check");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); yield return Ticks(3); Release();
            Require(!chakram.Active && !chakram.Visible && chakram.Throws == 0, "R retained projectile state"); report.checks.Add("R clears projectile and ability state in flight");
            yield return Ticks(3);
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 1u << (int)GamepadButton.RightShoulder }); yield return Mode(PrototypeChakram.Mode.Aiming);
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = (1u << (int)GamepadButton.RightShoulder) | (1u << (int)GamepadButton.West) });
            yield return Mode(PrototypeChakram.Mode.Throwing); Release();
            for (int i = 0; i < 180 && chakram.Catches == 0; i++) yield return new WaitForFixedUpdate();
            Require(chakram.Throws == 1 && chakram.Catches == 1, "Gamepad throw/recall failed"); yield return Mode(PrototypeChakram.Mode.Ready);
            report.checks.Add("gamepad RB aim and west button throw then release recall");
            motor.ResetToSpawn(); PlaceTarget(20);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q)); yield return Mode(PrototypeChakram.Mode.Aiming);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Mode(PrototypeChakram.Mode.Throwing);
            for (int i = 0; i < 360 && chakram.Catches == 0; i++) yield return new WaitForFixedUpdate();
            Require(chakram.Catches == 1, "Held aim never returned chakram"); yield return Ticks(80);
            Require(chakram.Throws == 1 && chakram.State == PrototypeChakram.Mode.Aiming, "Held attack repeated after catch");
            report.checks.Add("held aim has bounded flight and held attack does not repeat after catch"); Release(); yield return Mode(PrototypeChakram.Mode.Ready);
            motor.ResetToSpawn(); report.result = "PASS"; Finish(null);
        }
        private void Capture(string filename)
        { Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, filename), shot.EncodeToPNG()); Destroy(shot); }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true;
            if (error != null) { report.result = "FAIL"; report.error = error.ToString(); }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "chakram-player-verification.json"), JsonUtility.ToJson(report, true));
            Application.Quit(error == null ? 0 : 1);
        }
    }
}
