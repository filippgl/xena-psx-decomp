using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    internal sealed class PrototypeSettingsSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report { public string result = "FAIL", error, phase; public List<string> checks = new List<string>(); }
        private Report report;
        private string phase, output;
        private PrototypeMotor motor;
        private PrototypeInput input;
        private PrototypeCamera orbit;
        private Keyboard keyboard;
        private Mouse mouse;
        private bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--settings-smoke");
            if (at < 0 || at + 2 >= args.Length) return;
            var runner = new GameObject("SettingsSmoke").AddComponent<PrototypeSettingsSmoke>(); runner.phase = args[at + 1]; runner.output = args[at + 2];
        }
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type) { if (!finished && report != null && type == LogType.Exception) Finish(new Exception(message + "\n" + stack)); }
        private void Require(bool condition, string message) { if (!condition) { Finish(new Exception(message)); throw new Exception(message); } }
        private IEnumerator Frames(int count = 4) { for (int i = 0; i < count; i++) yield return null; }
        private IEnumerator Pause(bool expected)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return Frames();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames();
            Require(input.Paused == expected, "Unexpected pause state");
        }
        private IEnumerator Bind(string key, Key control)
        {
            Require(input.BeginRebind(key), "Could not begin rebind " + key);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(control)); yield return Frames();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames();
            Require(!input.IsRebinding, "Rebind did not complete/cancel");
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            report = new Report { phase = phase };
            motor = FindFirstObjectByType<PrototypeMotor>(); input = motor.GetComponent<PrototypeInput>(); orbit = FindFirstObjectByType<PrototypeCamera>();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); var pad = InputSystem.AddDevice<Gamepad>(); input.UseDevices(keyboard, mouse, pad);
            yield return Frames(10);
            if (phase == "write")
            {
                Require(input.BindingPath("Jump") == "<Keyboard>/space", "Initial jump is not default");
                yield return Pause(true); yield return Bind("Jump", Key.K);
                Require(input.BindingPath("Jump") == "<Keyboard>/k", "Jump binding was not replaced"); report.checks.Add("interactive jump rebind through virtual keyboard");
                yield return Bind("Kick", Key.Escape);
                Require(input.Paused && input.BindingPath("Kick") == "<Keyboard>/f", "Escape cancellation changed binding or resumed game"); report.checks.Add("Escape cancels rebind and keeps pause");
                yield return Bind("Sword", Key.F);
                Require(input.BindingPath("Sword") == "<Mouse>/leftButton" && input.BindingStatus.StartsWith("Already assigned"), "Conflicting key was accepted"); report.checks.Add("duplicate binding rejected without changing action");
                Require(input.BeginRebind("Sword"), "Could not begin mouse rebind");
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 4 }); yield return Frames();
                InputSystem.QueueStateEvent(mouse, new MouseState()); yield return Frames();
                Require(input.BindingPath("Sword") == "<Mouse>/middleButton", "Mouse button rebind failed"); report.checks.Add("interactive mouse button rebind");
                yield return Bind("Forward", Key.I);
                Require(input.BindingPath("Forward") == "<Keyboard>/i", "WASD composite part did not rebind"); report.checks.Add("movement composite part rebind");
                input.Preferences.Data.mouseSensitivity = 0.23f; input.Preferences.Data.invertY = true; input.Preferences.Data.cameraDistance = 5;
                motor.GetComponent<PrototypeGraphics>().SetMode(XenaVisualMode.Original);
                Require(input.SaveSettings() && File.Exists(input.Preferences.FilePath), "Settings were not written"); report.checks.Add("camera and bindings saved to isolated file");
                Require(input.Preferences.Data.originalGraphics, "Original graphics preference missing"); report.checks.Add("graphics mode saved to isolated preferences");
                yield return new WaitForEndOfFrame();
                Texture2D picture = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, "settings-menu.png"), picture.EncodeToPNG()); Destroy(picture);
                yield return Pause(false);
                motor.ResetToSpawn(); Vector3 before = motor.transform.position;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return new WaitForSecondsRealtime(0.2f);
                Require(Vector3.Distance(before, motor.transform.position) < 0.06f, "Old W binding still moves");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.I)); yield return new WaitForSecondsRealtime(0.2f);
                Require(Vector3.Distance(before, motor.transform.position) > 0.2f, "New I binding does not move"); report.checks.Add("old movement key inactive and new key moves player");
            }
            else if (phase == "read")
            {
                Require(input.BindingPath("Jump") == "<Keyboard>/k" && input.BindingPath("Forward") == "<Keyboard>/i" && input.BindingPath("Sword") == "<Mouse>/middleButton", "Binding overrides did not survive restart");
                Require(Mathf.Abs(orbit.Sensitivity - 0.23f) < 0.001f && orbit.InvertY, "Camera settings did not survive restart"); report.checks.Add("new process loads camera settings and binding overrides");
                Require(Mathf.Abs(orbit.DesiredDistance - 5) < 0.001f, "Camera distance did not survive restart"); report.checks.Add("camera zoom distance survives restart");
                Require(motor.GetComponent<PrototypeGraphics>().Mode == XenaVisualMode.Original && !motor.GetComponentInChildren<XenaAssetPreview>().UsingUpscale, "Original graphics mode did not survive restart"); report.checks.Add("new process restores actual Original graphics materials");
                float pitch = orbit.Pitch;
                InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(0, 50) }); yield return Frames();
                Require(orbit.Pitch > pitch + 5, "Loaded inversion did not change actual camera direction"); report.checks.Add("loaded inversion affects mouse camera");
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return Frames(6);
                Require(!motor.GetComponent<PrototypeCombat>().Busy, "Old mouse attack binding remained active");
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 4 }); yield return Frames(6);
                Require(motor.GetComponent<PrototypeCombat>().Slot == 0x51, "Loaded mouse binding did not attack");
                InputSystem.QueueStateEvent(mouse, new MouseState()); yield return new WaitForSecondsRealtime(0.6f); report.checks.Add("loaded mouse binding attacks and old button is inactive");
                motor.ResetToSpawn(); float y = motor.transform.position.y;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return new WaitForSecondsRealtime(0.1f);
                Require(motor.Grounded, "Old Space binding still jumps");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.K)); yield return new WaitForSecondsRealtime(0.12f);
                Require(!motor.Grounded && motor.transform.position.y > y + 0.2f, "Loaded K binding did not jump"); report.checks.Add("loaded jump binding works and original key is inactive");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Pause(true); input.ResetPreferences();
                Require(input.BindingPath("Jump") == "<Keyboard>/space" && input.BindingPath("Forward") == "<Keyboard>/w" && Mathf.Abs(orbit.Sensitivity - 0.12f) < 0.001f && !orbit.InvertY && Mathf.Abs(orbit.DesiredDistance - 4) < 0.001f, "Defaults did not restore"); report.checks.Add("restore defaults resets bindings and camera");
                yield return Frames(); Require(motor.GetComponent<PrototypeGraphics>().Mode == XenaVisualMode.Remaster && motor.GetComponentInChildren<XenaAssetPreview>().UsingUpscale, "Defaults did not restore Remaster presentation"); report.checks.Add("restoring defaults restores Remaster presentation");
            }
            else if (phase == "malformed")
            {
                Require(input.Preferences.Status.StartsWith("Settings load failed") && input.BindingPath("Jump") == "<Keyboard>/space" && Mathf.Abs(orbit.Sensitivity - 0.12f) < 0.001f, "Malformed settings did not recover safely");
                Require(File.ReadAllText(input.Preferences.FilePath) == "{invalid-json", "Malformed user file was overwritten"); report.checks.Add("malformed settings use defaults and preserve original file");
            }
            else Require(false, "Unknown settings verification phase");
            report.result = "PASS"; Finish(null);
        }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true;
            if (error != null) { report.result = "FAIL"; report.error = error.ToString(); }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "settings-" + phase + "-verification.json"), JsonUtility.ToJson(report, true));
            Application.Quit(error == null ? 0 : 1);
        }
    }
}
