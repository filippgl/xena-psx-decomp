using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    internal sealed class PrototypeSmoke : MonoBehaviour
    {
        private string output;
        private PrototypeMotor motor;
        private PrototypeInput input;
        private PrototypeCamera orbit;
        private XenaAssetPreview visual;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad gamepad;
        private bool finished;
        [Serializable] private sealed class Report
        {
            public string result = "FAIL", error, device;
            public List<string> checks = new List<string>();
            public List<int> jumpFrames = new List<int>();
            public List<int> locomotionFrames = new List<int>();
            public List<int> sprintFrames = new List<int>(); public int runFootsteps;
            public string rockResult = "";
            public float walkDistance, runDistance, diagonalDistance, gamepadDistance, jumpHeight, yawChange, cameraObstructedDistance, wallDistance, treeAxisDistance, treeProgress, treeMinAxis, treeContactTicks, strafeStalledFrames, strafeScreenJitter, strafeFrames, rockClosest, rockHoleTicks, lowFpsDistance, highFpsDistance, fixedStep;
        }
        private Report report;
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type)
        { if (!finished && report != null && type == LogType.Exception) Finish(new Exception(message + "\n" + stack)); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--movement-smoke");
            if (index < 0 || index + 1 >= args.Length) return;
            var smoke = new GameObject("MovementSmoke").AddComponent<PrototypeSmoke>(); smoke.output = Path.GetFullPath(args[index + 1]);
        }
        private void Require(bool valid, string message)
        {
            if (valid) return;
            Finish(new InvalidOperationException(message)); throw new InvalidOperationException(message);
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            report = new Report { device = SystemInfo.graphicsDeviceName };
            motor = FindFirstObjectByType<PrototypeMotor>(); input = motor.GetComponent<PrototypeInput>(); orbit = FindFirstObjectByType<PrototypeCamera>();
            visual = motor.GetComponentInChildren<XenaAssetPreview>();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); gamepad = InputSystem.AddDevice<Gamepad>();
            input.UseDevices(keyboard, mouse, gamepad);
            for (int i = 0; i < 10; i++) yield return null;
            report.fixedStep = Time.fixedDeltaTime;
            Require(Mathf.Abs(report.fixedStep - 1f / 60) < 0.00001f, "Prototype must use 60Hz fixed simulation"); report.checks.Add("60Hz simulation clock");
            Require(motor.Grounded, "Player did not reach original ground"); report.checks.Add("original terrain grounding");

            yield return Segment(new[] { Key.W }, 16, value => report.walkDistance = value);
            Require(report.walkDistance > 0.2f, "WASD forward did not move"); report.checks.Add("WASD movement");
            yield return Segment(new[] { Key.W, Key.LeftShift }, 16, value => report.runDistance = value);
            Require(report.runDistance > report.walkDistance * 1.5f && report.runDistance < report.walkDistance * 2.6f, "Sprint speed ratio differs"); report.checks.Add("Shift sprint");
            yield return Segment(new[] { Key.W, Key.D }, 16, value => report.diagonalDistance = value);
            Require(report.diagonalDistance <= report.walkDistance * 1.08f, "Diagonal movement is faster"); report.checks.Add("normalized diagonal input");
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30;
            yield return Segment(new[] { Key.W }, 20, value => report.lowFpsDistance = value);
            Application.targetFrameRate = 120;
            yield return Segment(new[] { Key.W }, 20, value => report.highFpsDistance = value);
            Require(Mathf.Abs(report.lowFpsDistance - report.highFpsDistance) < 0.09f, "Movement differs between 30 and 120 FPS"); report.checks.Add("30/120 FPS movement consistency");

            motor.ResetToSpawn();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return null; yield return null;
            bool started = false, looping = false, wrapped = false, capturedRun = false;
            int previousRunFrame = -1;
            Key[] square = { Key.W, Key.D, Key.S, Key.A };
            for (int i = 0; i < 125; i++)
            {
                // Stay near the verified spawn while observing a full slow cycle.
                if (i % 31 == 0) InputSystem.QueueStateEvent(keyboard, new KeyboardState(square[(i / 31) % 4]));
                yield return new WaitForFixedUpdate();
                Require(motor.Grounded, "Locomotion sample left the original ground");
                if (visual.CurrentSlot == 0x14)
                { Require(!looping, "Start clip repeated during continuous movement"); started = true; }
                else
                {
                    Require(visual.CurrentSlot == 0x15, "Continuous movement did not use native run loop"); looping = true;
                    int frame = visual.CurrentFrame;
                    if (previousRunFrame > frame) wrapped = true;
                    previousRunFrame = frame;
                    if (!report.locomotionFrames.Contains(frame)) report.locomotionFrames.Add(frame);
                    if (frame == 10 && !capturedRun) { capturedRun = true; yield return new WaitForEndOfFrame(); Capture("movement-run-cycle.png"); }
                }
            }
            Require(started && looping && wrapped && report.locomotionFrames.Count == 25, "Incomplete start/run cycle");
            report.checks.Add("native walk start once then complete 25-frame walk loop");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift)); yield return null; yield return null; yield return new WaitForFixedUpdate();
            Require(visual.CurrentSlot == 0x1a, "Shift still uses walking animation15 instead of run1A");
            previousRunFrame = -1; wrapped = false; bool capturedSprint = false;
            var steps = motor.GetComponent<PrototypeSfxEvents>(); int stepsBefore = steps.FootstepsPlayed; int stepLog = PrototypeSfx.Log.Count;
            for (int i = 0; i < 62; i++)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(square[(i / 16) % 4], Key.LeftShift));
                yield return new WaitForFixedUpdate();
                Require(visual.CurrentSlot == 0x1a, "Sprint left original run1A");
                int frame = visual.CurrentFrame; if (frame < previousRunFrame) wrapped = true;
                previousRunFrame = frame; if (!report.sprintFrames.Contains(frame)) report.sprintFrames.Add(frame);
                if (frame == 10 && !capturedSprint) { capturedSprint = true; yield return new WaitForEndOfFrame(); Capture("movement-run-cycle.png"); }
            }
            Require(wrapped && report.sprintFrames.Count == 20, "Incomplete distinct 20-frame sprint cycle");
            report.checks.Add("Shift uses distinct complete20-frame native run1A loop");
            // Footsteps (func_800416E8): run 0x1A frames 6 and 17 -> rand(0x2E,0x32) at vol 31; 62 fixed ticks = 31 game steps = 1.55 cycles -> 3 (+-1 at the window edges).
            report.runFootsteps = steps.FootstepsPlayed - stepsBefore;
            Require(report.runFootsteps >= 2 && report.runFootsteps <= 4 && PrototypeSfx.Log.Exists(x => x.id >= 0x2E && x.id <= 0x32 && x.vol == 31),
                "Run footsteps differ from 2 per 20-frame cycle: " + report.runFootsteps);
            report.checks.Add("run footsteps rand(0x2E,0x32) vol 31 on run 1A frames 6/17");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            bool stopping = false;
            for (int i = 0; i < 20; i++) { yield return new WaitForFixedUpdate(); stopping |= visual.CurrentSlot == 0x1f; }
            Require(stopping && visual.CurrentSlot == 0, "Stop clip did not play once then recover to idle"); report.checks.Add("native stop once then idle");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return null; yield return null; yield return new WaitForFixedUpdate();
            Require(visual.CurrentSlot == 0x14, "Movement did not restart with initial pose");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null; yield return new WaitForFixedUpdate();
            Require(visual.CurrentSlot == 0x17, "Interrupted start did not stop");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return null; yield return null; yield return new WaitForFixedUpdate();
            Require(visual.CurrentSlot == 0x14 && visual.CurrentFrame < 4, "Movement could not interrupt stop recovery"); report.checks.Add("restart interrupts stop recovery");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); motor.ResetToSpawn();
            yield return null; yield return null;
            float beforeYaw = orbit.Yaw;
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(150, 0) });
            yield return null; yield return null;
            report.yawChange = orbit.Yaw - beforeYaw;
            Require(report.yawChange > 10, "Mouse did not rotate camera"); report.checks.Add("mouse camera look");
            float zoomBefore = orbit.DesiredDistance;
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 120) }); yield return null; yield return null;
            Require(orbit.DesiredDistance < zoomBefore - 0.5f, "Mouse wheel did not zoom camera"); report.checks.Add("mouse wheel camera zoom");
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 100000) }); yield return null; yield return null;
            Require(Mathf.Abs(orbit.DesiredDistance - 2) < 0.001f, "Camera zoom crossed minimum");
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, -100000) }); yield return null; yield return null;
            Require(Mathf.Abs(orbit.DesiredDistance - 7) < 0.001f, "Camera zoom crossed maximum"); report.checks.Add("camera zoom clamps to allowed range");
            input.Preferences.Data.cameraDistance = 4;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.C)); yield return null; yield return null;
            Require(Mathf.Abs(Mathf.DeltaAngle(orbit.Yaw, motor.transform.eulerAngles.y)) < 0.1f, "C did not center camera behind player"); report.checks.Add("keyboard camera centering");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());

            motor.ResetToSpawn(); float startY = motor.transform.position.y, peak = startY;
            Transform[] joints = visual.GetComponentsInChildren<Transform>();
            Quaternion[] idle = Array.ConvertAll(joints, joint => joint.localRotation);
            bool rising = false, falling = false, landed = false, changedPose = false;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for (int i = 0; i < 76; i++)
            {
                yield return new WaitForFixedUpdate(); peak = Mathf.Max(peak, motor.transform.position.y);
                if (!motor.Grounded)
                {
                    Require(visual.CurrentSlot == 0x2f, "Airborne player used a different clip");
                    Require(visual.CurrentFrame >= 6 && visual.CurrentFrame < 29, "Airborne jump wrapped into preparation/landing frames");
                    if (!report.jumpFrames.Contains(visual.CurrentFrame)) report.jumpFrames.Add(visual.CurrentFrame);
                    for (int joint = 0; joint < joints.Length; joint++)
                        if (Quaternion.Angle(joints[joint].localRotation, idle[joint]) > 5) changedPose = true;
                    if (motor.VerticalSpeed > 0) rising = true;
                    else if (!falling)
                    {
                        falling = true; yield return new WaitForEndOfFrame(); Capture("movement-jump-air.png");
                    }
                }
                else if (visual.CurrentSlot == 0x2f)
                {
                    Require(visual.CurrentFrame >= 29 && visual.CurrentFrame <= 43, "Landing clip wrapped");
                    if (!landed) { landed = true; yield return new WaitForEndOfFrame(); Capture("movement-jump-land.png"); }
                }
            }
            report.jumpHeight = peak - startY;
            Require(report.jumpHeight > 0.35f && motor.Grounded, "Jump/landing failed"); report.checks.Add("Space jump and landing");
            Require(rising && falling && landed && changedPose, "Original jump poses/phases were not rendered"); report.checks.Add("original jump ascent/descent/landing poses");
            Require(visual.CurrentSlot == 0, "Standing landing did not recover to idle"); report.checks.Add("non-looping landing recovers to idle");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for (int i = 0; i < 60 && !motor.Grounded; i++) yield return new WaitForFixedUpdate();
            Require(motor.Grounded && visual.CurrentSlot == 0x2f, "Second jump did not reach landing phase");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return null; yield return null; yield return new WaitForFixedUpdate();
            Require(visual.CurrentSlot == 0x14, "Movement did not interrupt landing"); report.checks.Add("movement interrupts landing recovery");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); motor.ResetToSpawn();
            Require(visual.CurrentSlot == 0 && visual.CurrentFrame == 0, "Reset left a stale jump pose"); report.checks.Add("reset clears sampled jump pose");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
            Require(input.Paused, "Escape did not pause");
            int ticks = motor.Ticks;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return new WaitForSecondsRealtime(0.2f);
            Require(motor.Ticks == ticks, "Paused motor advanced");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
            Require(!input.Paused, "Escape did not resume"); report.checks.Add("pause/resume stops simulation");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); motor.ResetToSpawn();
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.up });
            yield return null; yield return null;
            Vector3 start = motor.transform.position;
            ticks = motor.Ticks; while (motor.Ticks < ticks + 16) yield return new WaitForFixedUpdate();
            report.gamepadDistance = FlatDistance(start, motor.transform.position);
            Require(report.gamepadDistance > 0.2f, "Gamepad stick did not move"); report.checks.Add("gamepad left-stick movement");
            beforeYaw = orbit.Yaw;
            InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = Vector2.right });
            yield return new WaitForSecondsRealtime(0.2f);
            Require(orbit.Yaw > beforeYaw + 10, "Gamepad right stick did not rotate camera"); report.checks.Add("gamepad right-stick camera");
            InputSystem.QueueStateEvent(gamepad, new GamepadState { buttons = 1u << (int)GamepadButton.RightStick }); yield return null; yield return null;
            Require(Mathf.Abs(Mathf.DeltaAngle(orbit.Yaw, motor.transform.eulerAngles.y)) < 0.1f, "R3 did not center camera"); report.checks.Add("gamepad camera centering");
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); yield return null; yield return null;
            Require(FlatDistance(motor.transform.position, start) < 0.3f, "R reset failed"); report.checks.Add("R reset to original spawn");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());

            Vector3 forward = Quaternion.Euler(0, orbit.Yaw, 0) * Vector3.forward;
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.GetComponent<Renderer>().enabled = false;
            wall.transform.position = motor.transform.position + forward + Vector3.up * 1.5f;
            wall.transform.rotation = Quaternion.LookRotation(forward); wall.transform.localScale = new Vector3(4, 3, 0.4f);
            Physics.SyncTransforms();
            yield return Segment(new[] { Key.W, Key.LeftShift }, 20, value => report.wallDistance = value);
            Require(report.wallDistance < 0.8f, "Capsule crossed a known wall"); report.checks.Add("capsule wall blocking"); Destroy(wall);

            // Original CLS group3 is the six-triangle trunk prism of tree node5.
            Mesh cls = GameObject.Find("OriginalCLSWalls").GetComponent<MeshCollider>().sharedMesh;
            Vector3 trunk = Vector3.zero; float trunkBase = float.PositiveInfinity;
            for (int i = 54; i < 72; i++) { trunk += cls.vertices[i] / 18; trunkBase = Mathf.Min(trunkBase, cls.vertices[i].y); }
            yield return TreeApproach(trunk, trunkBase, forward);
            yield return RockApproach();
            Require(report.rockHoleTicks == 0, "Capsule entered original terrain hole under @rck_cx"); report.checks.Add("no-surface terrain hole under original rock blocks movement");
            yield return StrafeContinuity();
            Require(report.strafeStalledFrames == 0 && report.strafeScreenJitter < 6, "Drawn model steps with the 60Hz tick under a faster render clock");
            report.checks.Add("render-interpolated model and camera focus stay continuous at 120 FPS");
            // Sliding around the prism is allowed (the original also slides along the
            // hit normal); entering it is not. Inradius .18m + capsule .28m.
            Require(report.treeMinAxis > 0.4f && report.treeContactTicks > 0, "Capsule entered original CLS tree trunk"); report.checks.Add("original CLS tree trunk prism stops capsule penetration");

            // A temporary known obstacle tests camera sweep and capsule blocking;
            // it is never saved into the user's scene or the ordinary build state.
            motor.enabled = false; motor.transform.position += Vector3.up * 20;
            yield return null; yield return null;
            Vector3 focus = motor.transform.position + Vector3.up * 1.1f;
            Vector3 back = (orbit.transform.position - focus).normalized;
            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.GetComponent<Renderer>().enabled = false; obstacle.transform.position = focus + back * 2;
            obstacle.transform.localScale = Vector3.one * 0.6f; Physics.SyncTransforms();
            yield return null; yield return new WaitForEndOfFrame();
            report.cameraObstructedDistance = orbit.DistanceToTarget;
            Require(orbit.DistanceToTarget < 2, "Camera crossed a known obstacle"); report.checks.Add("camera obstruction sweep");
            Destroy(obstacle); motor.enabled = true; motor.ResetToSpawn();
            yield return null; yield return null;
            report.result = "PASS";
            yield return new WaitForEndOfFrame();
            Capture("movement-player.png");
            Finish(null);
        }
        // @rck_cx has no CLS: the original height query returns no surface
        // inside its footprint, and the shared mover rejects such moves.
        private IEnumerator RockApproach()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            Bounds rock = default; foreach (MeshFilter filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (filter.sharedMesh != null && filter.sharedMesh.name == "level-01-object-65") rock = filter.GetComponent<Renderer>().bounds;
            Require(rock.size.x > 1, "Original rock mesh unavailable");
            Vector3 edge = Vector3.zero; bool found = false; Vector3 direction = Vector3.zero; Key travel = Key.W;
            Vector3 cameraForward = Quaternion.Euler(0, orbit.Yaw, 0) * Vector3.forward, cameraRight = Quaternion.Euler(0, orbit.Yaw, 0) * Vector3.right;
            // Approach along a camera-relative key direction: axis points from the rock to Xena.
            var approaches = new[] { (-cameraForward, Key.W), (cameraForward, Key.S), (-cameraRight, Key.D), (cameraRight, Key.A) };
            foreach (var (axis, key) in approaches)
            {
                for (float d = 0.2f; d < 8 && !found; d += 0.1f)
                {
                    Vector3 p = rock.center + axis * d; p.y = rock.max.y + 2; float floor = motor.GroundHeight(p);
                    if (float.IsNegativeInfinity(floor)) continue;
                    bool open = true; for (float k = 0.1f; k <= 2.5f; k += 0.1f) { Vector3 q = p + axis * k; q.y = floor + 0.5f; if (float.IsNegativeInfinity(motor.GroundHeight(q))) open = false; }
                    if (open) { edge = new Vector3(p.x, floor, p.z); direction = -axis; travel = key; found = true; }
                }
                if (found) break;
            }
            Require(found, "No open terrain beside original rock");
            var controller = motor.GetComponent<CharacterController>(); controller.enabled = false;
            motor.transform.position = edge - direction * 2f + Vector3.up * 0.3f; controller.enabled = true; Physics.SyncTransforms();
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(travel, Key.LeftShift)); yield return null; yield return null;
            int ticks = motor.Ticks; report.rockClosest = float.PositiveInfinity;
            while (motor.Ticks < ticks + 90)
            {
                yield return new WaitForFixedUpdate();
                Vector3 position = motor.transform.position;
                if (float.IsNegativeInfinity(motor.GroundHeight(position + Vector3.up * 0.5f))) report.rockHoleTicks++;
                report.rockClosest = Mathf.Min(report.rockClosest, Vector3.Dot(position - edge, direction) * -1);
            }
            report.rockResult = "edge " + edge + " dir " + direction + " end " + motor.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        }
        // Presentation continuity while the 60Hz simulation runs under a faster
        // render clock: stalled frames and mean frame-to-frame change of screen motion (px).
        private IEnumerator StrafeContinuity()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); motor.ResetToSpawn();
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.LeftShift));
            for (int i = 0; i < 20; i++) yield return new WaitForEndOfFrame();
            Camera view = Camera.main; Transform drawn = visual.transform;
            var world = new List<float>(); var screen = new List<float>();
            Vector3 lastWorld = drawn.position; Vector3 lastScreen = view.WorldToScreenPoint(drawn.position + Vector3.up);
            for (int i = 0; i < 90; i++)
            {
                yield return new WaitForEndOfFrame();
                Vector3 s = view.WorldToScreenPoint(drawn.position + Vector3.up);
                world.Add(Vector3.Distance(drawn.position, lastWorld)); screen.Add(s.x - lastScreen.x);
                lastWorld = drawn.position; lastScreen = s;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            var sorted = new List<float>(world); sorted.Sort(); float median = sorted[sorted.Count / 2];
            int stalled = 0; foreach (float d in world) if (d < median * 0.25f) stalled++;
            float jerk = 0; for (int i = 1; i < screen.Count; i++) jerk += Mathf.Abs(screen[i] - screen[i - 1]) / (screen.Count - 1);
            report.strafeFrames = world.Count; report.strafeStalledFrames = stalled; report.strafeScreenJitter = jerk;
        }
        private IEnumerator TreeApproach(Vector3 trunk, float trunkBase, Vector3 forward)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            var controller = motor.GetComponent<CharacterController>(); controller.enabled = false;
            motor.transform.position = new Vector3(trunk.x, trunkBase + 0.6f, trunk.z) - forward * 1.6f;
            controller.enabled = true; Physics.SyncTransforms();
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift)); yield return null; yield return null;
            int ticks = motor.Ticks; report.treeMinAxis = float.PositiveInfinity;
            while (motor.Ticks < ticks + 60)
            {
                yield return new WaitForFixedUpdate();
                Vector3 o = motor.transform.position - trunk; o.y = 0;
                report.treeMinAxis = Mathf.Min(report.treeMinAxis, o.magnitude);
                if (o.magnitude < 0.6f) report.treeContactTicks++;
            }
            Vector3 offset = motor.transform.position - trunk; offset.y = 0;
            report.treeAxisDistance = offset.magnitude; report.treeProgress = Vector3.Dot(offset, forward);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        }
        private IEnumerator Segment(Key[] keys, int count, Action<float> record)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); motor.ResetToSpawn();
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); yield return null; yield return null;
            Vector3 start = motor.transform.position; int ticks = motor.Ticks;
            while (motor.Ticks < ticks + count) yield return new WaitForFixedUpdate();
            record(FlatDistance(start, motor.transform.position));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        }
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private void Capture(string filename)
        {
            Directory.CreateDirectory(output);
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG()); Destroy(image);
        }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true;
            if (error != null) { report.result = "FAIL"; report.error = error.ToString(); }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "movement-player-verification.json"), JsonUtility.ToJson(report, true));
            Application.Quit(error == null ? 0 : 1);
        }
    }
}
