using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    internal sealed class PrototypePresentationSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report { public string result = "FAIL", error; public int highResolutionPages, stpTexels, originalTriangles, remasterTriangles; public float maxTerrainOffset, maxWalkableOffset; public int seamEdges, curtains, foams, bandits; public List<string> checks = new List<string>(), wallSpots = new List<string>(); public int wallSpotsTested, wallLensBehind; public float gateCopyMean, outerFrameMs, noOuterFrameMs; public int floatingTrees, treeFlippedFaces, treeFaces; }
        private readonly Report report = new Report();
        private string output; private bool finished; private Gamepad pad;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--presentation-smoke");
            if (at >= 0 && at + 1 < args.Length) new GameObject("PresentationSmoke").AddComponent<PrototypePresentationSmoke>().output = Path.GetFullPath(args[at + 1]);
        }
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type) { if (!finished && type == LogType.Exception) Finish(new Exception(message + stack)); }
        private void Require(bool value, string message) { if (!value) { Finish(new Exception(message)); throw new Exception(message); } }
        private IEnumerator Start()
        {
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var motor = FindFirstObjectByType<PrototypeMotor>(); var input = motor.GetComponent<PrototypeInput>();
            var keyboard = InputSystem.AddDevice<Keyboard>(); pad = InputSystem.AddDevice<Gamepad>(); input.UseDevices(keyboard, InputSystem.AddDevice<Mouse>(), pad);
            for (int i = 0; i < 10; i++) yield return null;
            GameObject level = GameObject.Find("OriginalLevel01"); var preview = level.GetComponent<XenaAssetPreview>();
            MeshRenderer beam = null;
            foreach (var item in level.GetComponentsInChildren<MeshRenderer>()) if (item.name.Contains("@sunbm")) beam = item;
            Require(beam != null && beam.sharedMaterial.shader.name == "Xena/OriginalSemiTransparent" && beam.sharedMaterial.GetFloat("_Ps1BlendMode") == 1, "Sunbeam is still imported as an opaque column");
            var mask = beam.sharedMaterial.GetTexture("_StpMask") as Texture2D;
            foreach (Color32 pixel in mask.GetPixels32()) if (pixel.r > 127) report.stpTexels++;
            Require(report.stpTexels > 0, "Original sunbeam STP bits missing"); report.checks.Add("sunbeam uses original additive mode and nonempty per-texel STP mask");
            var graphics = motor.GetComponent<PrototypeGraphics>(); var orbit = Camera.main.GetComponent<PrototypeCamera>();
            orbit.enabled = false; var camera = Camera.main;
            camera.transform.position = beam.bounds.center + new Vector3(0, 0.2f, -1).normalized * Mathf.Max(3, beam.bounds.extents.magnitude * 2);
            camera.transform.LookAt(beam.bounds.center);
            Vector3 position = motor.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F10)); yield return null; yield return null;
            Require(graphics.Mode == XenaVisualMode.Original && !preview.UsingUpscale, "F10 did not change environment to Original");
            foreach (var item in level.GetComponentsInChildren<MeshRenderer>()) foreach (var material in item.sharedMaterials)
                if (material.mainTexture != null) Require(material.mainTexture.width == 256, "Original environment has replacement texture");
            yield return new WaitForEndOfFrame(); Capture("presentation-original.png");
            // Original background + depth cue (docs/xena-level-background.md oracle): fog 0 at 24 m, 0.57 at 28.6 m,
            // 1 beyond 32.77 m; the horizon strip is drawn.
            {
                var skyOriginal = FindFirstObjectByType<PrototypeSky>();
                Require(skyOriginal != null && skyOriginal.HorizonActive, "Original horizon strip is not drawn");
                Require(PrototypeSky.OriginalFogFactor(24) == 0 && Mathf.Abs(PrototypeSky.OriginalFogFactor(28.6f) - 0.57f) < 0.02f && PrototypeSky.OriginalFogFactor(33) == 1,
                    "Original fog curve differs: " + PrototypeSky.OriginalFogFactor(28.6f));
                // Orientation oracle: looking 20 deg down the strip leaves the screen top (s5 < 0, top row = strip/fog),
                // looking 20 deg up the top rows are the sky fill (21,97,214).
                var cam0 = Camera.main; var orb = cam0.GetComponent<PrototypeCamera>(); orb.enabled = false; var keepP = cam0.transform.position; var keepR = cam0.transform.rotation;
                cam0.transform.SetPositionAndRotation(new Vector3(0, 300, 0), Quaternion.Euler(-20, 0, 0)); yield return null; yield return new WaitForEndOfFrame();
                var shotUp = ScreenCapture.CaptureScreenshotAsTexture(); Color32 upTop = shotUp.GetPixel(shotUp.width / 2, shotUp.height - 3); Destroy(shotUp);
                Require(Mathf.Abs(upTop.r - 21) <= 3 && Mathf.Abs(upTop.g - 97) <= 3 && Mathf.Abs(upTop.b - 214) <= 3, "Original horizon orientation: looking up the top row is not the sky fill (21,97,214): " + upTop);
                cam0.transform.SetPositionAndRotation(keepP, keepR); orb.enabled = true;
                report.checks.Add("Original: HORIZ.STM horizon strip + hyperbolic depth cue 24.45..32.77 m to RGB(0,74,97)");
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F10)); yield return null; yield return null;
            // Pages by name; the gate's smoothed copy counts as its source page.
            var pages = new HashSet<string>();
            foreach (var item in level.GetComponentsInChildren<MeshRenderer>()) foreach (var material in item.sharedMaterials)
                if (material.mainTexture != null && material.mainTexture.width == 1024)
                { string n = material.mainTexture.name; pages.Add(n.EndsWith(PrototypeRemasterGate.SmoothSuffix) ? n.Substring(0, n.Length - PrototypeRemasterGate.SmoothSuffix.Length) : n); }
            report.highResolutionPages = pages.Count;
            var levelPreview = level.GetComponentInChildren<XenaAssetPreview>(); int expectedPages = levelPreview != null ? levelPreview.UpscaledTextureCount : 16;
            Require(graphics.Mode == XenaVisualMode.Remaster && preview.UsingUpscale && pages.Count == expectedPages && pages.Count >= 16 && motor.transform.position == position, "F10 did not bind all real high-resolution environment pages: " + pages.Count + "/" + expectedPages);
            report.checks.Add("actual F10 input changes environment 256px to all " + pages.Count + " real 1024px pages without moving actor");
            yield return new WaitForEndOfFrame(); Capture("presentation-remaster.png");
            var environment = motor.GetComponent<PrototypeRemasterEnvironment>();
            MeshRenderer water = null, fall = null, bow = null;
            foreach (var item in level.GetComponentsInChildren<MeshRenderer>())
            {
                string mesh = item.GetComponent<MeshFilter>().sharedMesh.name;
                if (mesh == "level-01-object-14") water = item; else if (mesh == "level-01-object-40") fall = item; else if (mesh == "level-01-object-0") bow = item;
            }
            Require(environment != null && water != null && fall != null && bow != null, "Remaster water targets missing");
            Require(environment.RemasterSurfaces == 4 && environment.MistEmitters >= 2 && water.sharedMaterial.shader.name == "Xena/RemasterWater" && fall.sharedMaterial.shader.name == "Xena/RemasterWaterfall"
                && bow.sharedMaterial.shader.name == "Xena/RemasterRainbow" && environment.MistActive, "Remaster did not bind water, waterfalls, rainbow and mist: " + environment.RemasterSurfaces + "/" + environment.MistEmitters);
            report.checks.Add("Remaster binds procedural water, terrain/rivlet waterfalls, rainbow and waterfall mist");
            report.curtains = environment.Curtains; report.foams = environment.Foams;
            Require(environment.Curtains > 0 && environment.CurtainsVisible && environment.FallParticleSystems == environment.Curtains, "Remaster did not replace boxy falls with smooth curtains and particle streams");
            report.checks.Add("Remaster replaces boxy falling faces with smooth ballistic water curtains");
            yield return Front(camera, environment.MainFall, environment.MainFallFacing, "presentation-bigfall-remaster.png");
            yield return Front(camera, environment.MainFall, environment.MainFallFacing, "presentation-bigfall-far-remaster.png", 30);
            Require(camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null && RenderSettings.skybox.shader.name == "Xena/RemasterSky"
                && RenderSettings.skybox.GetTexture("_MainTex") != null && RenderSettings.skybox.GetTexture("_MainTex").width >= 4096, "Remaster sky dome missing");
            report.checks.Add("Remaster draws the HDRI sky dome");
            var sky = motor.GetComponent<PrototypeSky>();
            Require(sky.PostActive && sky.SkyFogActive && sky.SunActive && sky.RockActive && UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera).renderPostProcessing, "Remaster post-processing/fog inactive");
            report.checks.Add("Remaster enables post volume, SMAA and sky-coloured aerial perspective");
            Require(sky.LandscapeActive && Shader.GetGlobalVector("_XenaLandscapeParams").w > 0.5f, "Remaster painted 360-degree landscape inactive");
            report.checks.Add("Remaster sky and far fog show the painted 360-degree landscape");
            yield return SkyViews(motor, orbit, camera, "presentation-sky-remaster.png");
            yield return PerimeterTour(motor, orbit, camera);
            // Outer world cost: mean frame time looking back from the spawn (180 deg), with and without it.
            {
                var outer = FindFirstObjectByType<PrototypeRemasterOuterWorld>(); orbit.enabled = false; int keepRate = Application.targetFrameRate; Application.targetFrameRate = -1;
                camera.transform.SetPositionAndRotation(motor.transform.position + Vector3.up * 1.7f, Quaternion.Euler(-12, 180, 0));
                foreach (bool on in new[] { true, false, true })
                {
                    outer.ApplyVisualMode(on ? XenaVisualMode.Remaster : XenaVisualMode.Original);
                    for (int i = 0; i < 20; i++) yield return null;
                    float t0 = Time.realtimeSinceStartup; for (int i = 0; i < 120; i++) yield return null;
                    float ms = (Time.realtimeSinceStartup - t0) / 120 * 1000;
                    if (on) report.outerFrameMs = ms; else report.noOuterFrameMs = ms;
                }
                Application.targetFrameRate = keepRate; orbit.enabled = true;
                report.checks.Add("outer world frame time " + report.outerFrameMs.ToString("F2") + " ms vs " + report.noOuterFrameMs.ToString("F2") + " ms without (spawn, looking back)");
            }
            yield return WallCamera(motor, orbit, camera, level);
            // Original water motion (docs/xena-water-animation.md): CLUT cycling, stream wave, splashes.
            {
                var ow = motor.GetComponent<PrototypeWaterOriginal>();
                graphics.SetMode(XenaVisualMode.Original); yield return null;
                int o0 = ow.CycleOffset; float y0 = ow.SampleWaveY(5);
                for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
                Require(ow.CycledTextures == 2 && ow.CycleOffset != o0 && ow.WaveVertices > 1000 && Mathf.Abs(ow.SampleWaveY(5) - y0) > 1e-4f && ow.Splashes == 2,
                    "Original water motion missing: textures " + ow.CycledTextures + " wave " + ow.WaveVertices + " splashes " + ow.Splashes);
                // Ghosting rule: the drawn stream vertices change every render frame while waving.
                // The first frame after a held (non-moving) step rightly starts from rest: not counted.
                int frames = 0, stalls = 0; float last = ow.SampleWaveY(5); bool wasMoving = ow.WaveMoving(5);
                for (int i = 0; i < 30; i++) { yield return null; float now = ow.SampleWaveY(5); bool moving = ow.WaveMoving(5); frames++; if (moving && wasMoving && Mathf.Abs(now - last) < 1e-7f) stalls++; last = now; wasMoving = moving; }
                report.checks.Add("Original water: CLUT cycling on 2 falling-water textures, stream vertex wave (" + stalls + "/" + frames + " stalled frames), 2 splash emitters");
                Require(stalls == 0, "Stream wave stalls between steps: " + stalls + "/" + frames);
                graphics.SetMode(XenaVisualMode.Remaster); yield return null;
            }
            var fires = motor.GetComponent<PrototypeFire>();
            Require(fires.Fires == 6 && fires.RemasterFires == 6 && fires.RemasterShown, "Level fires missing (5 gate fires + fire2): " + fires.Fires);
            report.checks.Add("level fires: 6 original animated additive sprites (5 at the gate), Remaster particle fires in their place");
            var generatedProps = motor.GetComponent<PrototypeRemasterProps>();
            Require(generatedProps.Replaced >= 1 && generatedProps.RemasterShown, "Remaster did not place the generated props: " + generatedProps.Replaced);
            report.checks.Add("Remaster places " + generatedProps.Replaced + " image-to-3D props: " + generatedProps.Describe());
            var vegetation = motor.GetComponent<PrototypeRemasterVegetation>();
            Require(vegetation.Replaced == 35 && vegetation.RemasterShown, "Remaster did not replace the 35 @pretty plants: " + vegetation.Replaced);
            report.checks.Add("Remaster replaces the 35 @pretty plant cards with CC0 Poly Haven plants");
            var trees = motor.GetComponent<PrototypeRemasterTrees>();
            Require(trees.Replaced >= 20 && trees.RemasterShown, "Remaster did not replace the original trees: " + trees.Replaced);
            report.checks.Add("Remaster replaces " + trees.Replaced + " original trees with generated trees (" + trees.Triangles + " triangles), " + trees.MatchedTrunks + " trunks sized to their original collision");
            var grass = motor.GetComponent<PrototypeRemasterGrass>();
            Require(grass.Tufts > 1000 && grass.Chunks > 0 && grass.RemasterShown, "Remaster grass missing: " + grass.Tufts);
            report.checks.Add("Remaster grass: " + grass.Tufts + " tufts in " + grass.Chunks + " chunks on " + grass.GrassArea.ToString("F0") + " m2, " + grass.Ferns + " extra ferns");
            Require(grass.SlopeTufts > 0 && grass.MaxBaseGap < 0.01f, "Grass on slopes does not sit on the surface: " + grass.SlopeTufts + " slope tufts, base gap " + grass.MaxBaseGap + " m");
            report.checks.Add("Remaster grass on slopes follows the surface: " + grass.SlopeTufts + " slope tufts, max base gap " + grass.MaxBaseGap.ToString("F4") + " m");
            yield return VegetationViews(orbit, camera, level);
            yield return OrthoReferences(orbit, camera, level);
            // Pause menu (docs/xena-pause-menu.md): opens on the main page, no wrap, confirm starts on Нет,
            // Options holds the moved settings; both graphics modes captured.
            {
                var pauseInput = motor.GetComponent<PrototypeInput>(); var pause = motor.GetComponent<PrototypePauseMenu>();
                orbit.enabled = true; motor.ResetToSpawn(); pauseInput.SetPaused(true); yield return null; yield return null;
                Require(pause.Current == PrototypePauseMenu.Page.Main && pause.Cursor == 0, "Pause did not open on the main page");
                pause.Move(-1); Require(pause.Cursor == 0, "Pause cursor wrapped");
                foreach (var mode in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                { graphics.SetMode(mode); yield return null; yield return new WaitForEndOfFrame(); Capture("pause-" + mode.ToString().ToLowerInvariant() + ".png"); }
                pause.Move(3); pause.Select(); Require(pause.Current == PrototypePauseMenu.Page.ConfirmExit && pause.Cursor == 1, "Exit confirm did not start on Нет");
                yield return null; yield return new WaitForEndOfFrame(); Capture("pause-confirm.png");
                pause.Select(); Require(pause.Current == PrototypePauseMenu.Page.Main, "Нет did not return to the pause menu");
                pause.Move(2); pause.Select(); Require(pause.Current == PrototypePauseMenu.Page.Options, "Options did not open");
                yield return null; yield return new WaitForEndOfFrame(); Capture("pause-options.png");
                // Original items Аудио (Моно/Стерео) and Движения (Ходьба/Бег): LEFT/RIGHT set them.
                pause.Move(2); bool stereo = PrototypePauseMenu.Stereo, run = PrototypePauseMenu.AlwaysRun;
                pause.Select(); Require(PrototypePauseMenu.Stereo != stereo, "Аудио did not toggle Моно/Стерео"); pause.Select();
                pause.Move(1); pause.Select(); Require(PrototypePauseMenu.AlwaysRun != run, "Движения did not toggle Ходьба/Бег"); pause.Select();
                Require(PrototypePauseMenu.Stereo == stereo && PrototypePauseMenu.AlwaysRun == run, "Аудио/Движения did not toggle back");
                report.checks.Add("options: original Аудио Моно/Стерео and Движения Ходьба/Бег toggle");
                pause.Move(6); pause.Select(); Require(pause.Current == PrototypePauseMenu.Page.Controls, "Controls did not open");
                yield return null; yield return new WaitForEndOfFrame(); Capture("pause-controls.png");
                pause.Back(); pause.Back(); Require(pause.Current == PrototypePauseMenu.Page.Main, "Back did not return to the main page");
                pause.Select(); Require(!pauseInput.Paused, "Продолжить did not resume"); yield return null;
                report.checks.Add("pause menu: original layout/items, no wrap, Нет default, options + controls pages, resume");
            }
            // Lake views (user: the water ends at the level edge; stream and lake looked different).
            {
                orbit.enabled = false; MeshRenderer lakeRenderer = null;
                foreach (var r in level.GetComponentsInChildren<MeshRenderer>()) if (r.GetComponent<MeshFilter>().sharedMesh.name == "level-01-object-14") lakeRenderer = r;
                var env = motor.GetComponent<PrototypeRemasterEnvironment>();
                report.checks.Add("extra lake-look water triangles " + env.ExtraWaterTriangles + (lakeRenderer != null ? " lake bounds " + lakeRenderer.bounds.ToString("F1") : ""));
                if (lakeRenderer != null)
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 dir = Quaternion.Euler(0, k * 90, 0) * Vector3.forward;
                        camera.transform.position = lakeRenderer.bounds.center + Vector3.up * 6 - dir * 6; camera.transform.rotation = Quaternion.LookRotation(dir + Vector3.down * 0.25f);
                        yield return null; yield return new WaitForEndOfFrame(); Capture("lake-" + k * 90 + ".png");
                    }
                orbit.enabled = true;
            }
            // Close-ups: grass at knee height and a signpost at 1.5 m (user reports).
            {
                orbit.enabled = false; motor.ResetToSpawn(); Vector3 at = motor.transform.position + motor.transform.forward * 2;
                camera.transform.position = at + Vector3.up * 0.45f - motor.transform.forward * 1.2f; camera.transform.LookAt(at + Vector3.up * 0.25f);
                yield return null; yield return new WaitForEndOfFrame(); Capture("closeup-grass.png");
                foreach (string layer in new[] { "RemasterGrass", "RemasterVegetation", "RemasterTrees" })
                {
                    var go = GameObject.Find(layer); if (go == null) continue; go.SetActive(false);
                    yield return null; yield return new WaitForEndOfFrame(); Capture("closeup-grass-no-" + layer + ".png"); go.SetActive(true);
                }
                Vector3 sign = new Vector3(-5283, 1900, -9432) / 400f; float g = motor.GroundHeight(sign + Vector3.up * 2); if (!float.IsNegativeInfinity(g)) sign.y = g;
                foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    camera.transform.position = sign + Vector3.up * 1.2f + dir * 1.6f; camera.transform.LookAt(sign + Vector3.up * 1.1f);
                    yield return null; yield return new WaitForEndOfFrame(); Capture("closeup-sign-" + (dir == Vector3.forward ? "n" : dir == Vector3.back ? "s" : dir == Vector3.left ? "w" : "e") + ".png");
                }
                orbit.enabled = true;
            }
            // Native-port reference view of sign #0 (build/signdir/native_sign.png): Xena at PS1
            // (-5283,-1900,-10400), camera behind her looking along +Z.
            orbit.enabled = false;
            foreach (var mode in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
            {
                PrototypeGraphics.Active.SetMode(mode);
                camera.transform.position = new Vector3(-5283, 1900, -10400) / 400f + new Vector3(0, 1.9f, -3.2f); camera.transform.LookAt(new Vector3(-5283, 1900, -10400) / 400f + new Vector3(0, 1.0f, 2.5f));
                yield return null; yield return new WaitForEndOfFrame(); Capture("signview-" + mode.ToString().ToLowerInvariant() + ".png");
            }
            orbit.enabled = true;
            var shafts = motor.GetComponent<PrototypeLightShafts>(); Renderer sunbeam = null;
            foreach (var r in level.GetComponentsInChildren<MeshRenderer>()) if (r.name.Contains("@sunbm")) sunbeam = r;
            Require(shafts.Shafts == 1 && shafts.RemasterShown && sunbeam != null && !sunbeam.enabled, "Remaster did not replace the @sunbm beam with a light shaft");
            report.checks.Add("Remaster replaces the original @sunbm beam with a volumetric-style light shaft");
            orbit.enabled = false; Vector3 beamAt = sunbeam.bounds.center;
            Vector3 viewFrom = beamAt + new Vector3(6, -2.5f, 6); float floor = motor.GroundHeight(viewFrom + Vector3.up * 3); if (!float.IsNegativeInfinity(floor)) viewFrom.y = Mathf.Max(viewFrom.y, floor + 1.7f);
            camera.transform.position = viewFrom; camera.transform.LookAt(beamAt);
            yield return null; yield return new WaitForEndOfFrame(); Capture("presentation-sunbeam-remaster.png");
            graphics.SetMode(XenaVisualMode.Original); yield return null; yield return new WaitForEndOfFrame(); Capture("presentation-sunbeam-original.png");
            Require(sunbeam.enabled && !shafts.RemasterShown, "Original mode did not restore the @sunbm beam");
            graphics.SetMode(XenaVisualMode.Remaster); yield return null; orbit.enabled = true;
            foreach (var r in level.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].shader.name == "Xena/OriginalSemiTransparent")
                        report.checks.Add("stp " + r.name + " slot " + i + " " + (mats[i].mainTexture != null ? mats[i].mainTexture.name : "-") + " blend " + mats[i].GetFloat("_Ps1BlendMode") + " centre " + r.bounds.center.ToString("F1") + " size " + r.bounds.size.ToString("F1"));
            }
            var bandits = motor.GetComponent<PrototypeBandits>(); report.bandits = bandits.Bandits.Count;
            Require(bandits.GeneratorCount == 11 && bandits.Spawned + bandits.TotalRemaining == 12 && bandits.Live() <= bandits.MaxLiveEnemies, "Level01 bandit generators differ from the original records");
            report.checks.Add("11 original bandit generators hold 12 bandits, at most 3 alive");
            if (bandits.Bandits.Count == 0) bandits.SpawnForTest(motor.transform.position + motor.transform.forward * 3);
            var nearest = bandits.Bandits[0]; foreach (var item in bandits.Bandits) if (Vector3.Distance(item.transform.position, motor.transform.position) < Vector3.Distance(nearest.transform.position, motor.transform.position)) nearest = item;
            yield return Portrait(camera, nearest.transform, "presentation-bandit-remaster.png");
            yield return Gameplay(motor, orbit, "presentation-gameplay-remaster.png");
            var scripts = motor.GetComponent<PrototypeLevelScripts>(); var firstSign = new List<Vector3>(scripts.TriggerPositions("sign"))[0];
            yield return Gameplay(motor, orbit, "presentation-sign0-remaster.png", firstSign + new Vector3(2.5f, 0, 2.5f));
            // The user's view: level end near Siras, looking back towards the start.
            var sirasAt = new List<Vector3>(scripts.TriggerPositions("levelEndNpc"))[0];
            Vector3 back = motor.transform.position - sirasAt; back.y = 0;
            yield return Gameplay(motor, orbit, "presentation-levelend-remaster.png", sirasAt + back.normalized * 8);
            var geometry = motor.GetComponent<PrototypeRemasterGeometry>(); MeshFilter terrain = null; MeshRenderer rock = null;
            foreach (var item in level.GetComponentsInChildren<MeshFilter>())
            { if (item.sharedMesh.name == "level-01-object-1") terrain = item; else if (item.sharedMesh.name == "level-01-object-66") rock = item.GetComponent<MeshRenderer>(); }
            report.originalTriangles = geometry.OriginalTriangles; report.remasterTriangles = geometry.RemasterTriangles; report.maxTerrainOffset = geometry.MaxTerrainOffset; report.maxWalkableOffset = geometry.MaxWalkableOffset; report.seamEdges = geometry.SeamEdges;
            int perFace = PrototypeRemasterGeometry.Segments * PrototypeRemasterGeometry.Segments;
            // Painted boundary walls (PrototypeRemasterOuterWorld) are left out of the drawn terrain only.
            int dropped = geometry.DroppedBackdropTriangles;
            Require(geometry.RemasterActive && geometry.RemasterTriangles == (geometry.OriginalTriangles - dropped) * perFace && terrain.sharedMesh.triangles.Length / 3 == (2290 - dropped) * perFace
                && terrain.GetComponent<MeshCollider>().sharedMesh.triangles.Length / 3 == 2290 && geometry.MaxWalkableOffset < 0.001f && geometry.LitSlots > 10 && terrain.GetComponent<MeshRenderer>().sharedMaterials[0].shader.name == "Xena/RemasterLit", "Remaster did not subdivide terrain while keeping the original collider");
            report.checks.Add("Remaster draws 9x curved terrain/prop triangles; walkable surfaces and colliders keep original geometry");
            yield return View(camera, rock.bounds, new Vector3(-0.6f, 0.25f, -1), 1.3f, "presentation-geometry-remaster.png");
            yield return View(camera, water.bounds, new Vector3(0.2f, 0.45f, -1), 0.55f, "presentation-water-remaster.png");
            yield return View(camera, fall.bounds, new Vector3(0.1f, 0.1f, -1), 1.6f, "presentation-waterfall-remaster.png");
            yield return View(camera, bow.bounds, new Vector3(1, 0.15f, 0.2f), 1.2f, "presentation-rainbow-remaster.png");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F10)); yield return null; yield return null;
            Require(graphics.Mode == XenaVisualMode.Original && environment.MistInactive && !environment.CurtainsVisible && !water.sharedMaterial.shader.name.StartsWith("Xena/Remaster")
                && !fall.sharedMaterial.shader.name.StartsWith("Xena/Remaster") && !bow.sharedMaterial.shader.name.StartsWith("Xena/Remaster"), "Original retained Remaster water presentation");
            report.checks.Add("Original restores original water, waterfall and rainbow materials without mist");
            Require(!geometry.RemasterActive && geometry.LitSlots == 0 && terrain.sharedMesh.triangles.Length / 3 == 2290 && terrain.GetComponent<MeshRenderer>().sharedMaterials[0].shader.name == "Xena/OriginalUnlit", "Original did not restore original terrain mesh");
            report.checks.Add("Original restores original terrain and prop meshes");
            Color32 background = camera.backgroundColor;
            Require(camera.clearFlags == CameraClearFlags.SolidColor && background.r == 0 && background.g == 74 && background.b == 97, "Original background is not the level fog colour RGB(0,74,97) (ENVIRO.BIN +0x4C, docs/xena-level-background.md)");
            report.checks.Add("Original clears to the level fog colour RGB(0,74,97)");
            Require(!sky.PostActive && !sky.SkyFogActive && !sky.SunActive && !UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera).renderPostProcessing, "Original kept Remaster post/fog");
            report.checks.Add("Original disables post-processing and fog");
            yield return Gameplay(motor, orbit, "presentation-gameplay-original.png");
            yield return SkyViews(motor, orbit, Camera.main, "presentation-sky-original.png");
            yield return View(camera, rock.bounds, new Vector3(-0.6f, 0.25f, -1), 1.3f, "presentation-geometry-original.png");
            yield return View(camera, water.bounds, new Vector3(0.2f, 0.45f, -1), 0.55f, "presentation-water-original.png");
            yield return Front(camera, environment.MainFall, environment.MainFallFacing, "presentation-bigfall-original.png");
            yield return View(camera, fall.bounds, new Vector3(0.1f, 0.1f, -1), 1.6f, "presentation-waterfall-original.png");
            yield return View(camera, bow.bounds, new Vector3(1, 0.15f, 0.2f), 1.2f, "presentation-rainbow-original.png");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F10)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            VerifyBlendPixels(); orbit.enabled = true; report.result = "PASS"; Finish(null);
        }
        private void VerifyBlendPixels()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.layer = 31; quad.transform.position = new Vector3(10000, 100, 10000); quad.transform.localScale = Vector3.one * 2;
            var cameraObject = new GameObject("BlendPixelVerification"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = quad.transform.position + Vector3.back * 2; camera.orthographic = true; camera.orthographicSize = 0.8f;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.2f, 0.3f, 1);
            var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            var pixels = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
            var diffuse = new Texture2D(1, 1, TextureFormat.RGBA32, false, true); diffuse.SetPixel(0, 0, new Color(0.2f, 0.1f, 0.05f, 1)); diffuse.Apply();
            var stp = new Texture2D(1, 1, TextureFormat.RGBA32, false, true); stp.SetPixel(0, 0, Color.black); stp.Apply();
            var material = new Material(Shader.Find("Xena/OriginalSemiTransparent")); material.SetTexture("_MainTex", diffuse); material.SetTexture("_StpMask", stp); material.SetFloat("_UseVertexLighting", 0); material.SetFloat("_Cull", 0);
            var renderer = quad.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            Color Read()
            {
                camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); pixels.Apply(); RenderTexture.active = previous; return pixels.GetPixel(16, 16);
            }
            bool Close(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.015f && Mathf.Abs(a.g - b.g) < 0.015f && Mathf.Abs(a.b - b.b) < 0.015f;
            renderer.enabled = false; Color background = Read(); renderer.enabled = true;
            material.SetFloat("_Ps1BlendMode", 1); material.SetFloat("_SrcBlend", 1); material.SetFloat("_DstBlend", 5);
            Color opaque = Read(); Require(Close(opaque, diffuse.GetPixel(0, 0)) && opaque.a > 0.99f, "Non-STP texel did not replace background opaquely");
            stp.SetPixel(0, 0, Color.white); stp.Apply(); Color additive = Read();
            Require(Close(additive, background + opaque) && additive.a > 0.99f, "Additive STP texel did not preserve and brighten background"); report.checks.Add("GPU additive STP brightens background while non-STP texels remain opaque");
            material.SetFloat("_Ps1BlendMode", 0); material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10);
            Require(Close(Read(), (background + opaque) * 0.5f), "Half-blend STP equation differs"); report.checks.Add("GPU half-blend STP preserves half of foreground and background");
            material.SetFloat("_Ps1BlendMode", 3); material.SetFloat("_SrcBlend", 1); material.SetFloat("_DstBlend", 5);
            Require(Close(Read(), background + opaque * 0.25f), "Quarter additive STP equation differs"); report.checks.Add("GPU quarter-additive STP equation verified");
            diffuse.SetPixel(0, 0, Color.clear); diffuse.Apply(); Require(Close(Read(), background), "Transparent zero texel obscured background"); report.checks.Add("GPU transparent zero texel leaves background intact");
            camera.targetTexture = null; target.Release(); Destroy(target); Destroy(pixels); Destroy(diffuse); Destroy(stp); Destroy(material); Destroy(quad); Destroy(cameraObject);
        }
        // The ordinary follow camera behind Xena at the original spawn.
        // Camera at walls (user report: walls turn see-through up close). Xena stands with her
        // back to a terrain wall, camera centred behind her; the lens must stay in front of the
        // VISIBLE (Remaster-smoothed) surface: a line focus -> lens must not cross it.
        private IEnumerator WallCamera(PrototypeMotor motor, PrototypeCamera orbit, Camera camera, GameObject level)
        {
            MeshFilter terrain = null; foreach (var c in level.GetComponentsInChildren<MeshCollider>()) if (c.sharedMesh.name == "level-01-object-1") terrain = c.GetComponent<MeshFilter>();
            Require(terrain != null && terrain.sharedMesh != null, "Terrain mesh not found");
            var probe = new GameObject("VisualTerrainProbe"); probe.layer = 31; probe.transform.SetParent(terrain.transform, false);
            var visual = probe.AddComponent<MeshCollider>(); visual.sharedMesh = terrain.sharedMesh; visual.enabled = false;
            orbit.enabled = true; int spots = 0, behind = 0; var capsule = motor.GetComponent<CharacterController>();
            motor.ResetToSpawn(); Vector3 origin = motor.transform.position;
            for (float r = 2; r < 40 && spots < 8; r += 3)
                for (int a = 0; a < 12 && spots < 8; a++)
                {
                    Vector3 at = origin + Quaternion.Euler(0, a * 30, 0) * Vector3.forward * r;
                    float ground = motor.GroundHeight(at + Vector3.up * 1.5f); if (float.IsNegativeInfinity(ground) || Mathf.Abs(ground - origin.y) > 4) continue;
                    at.y = ground;
                    for (int d = 0; d < 8; d++)
                    {
                        Vector3 dir = Quaternion.Euler(0, d * 45, 0) * Vector3.forward;
                        if (!Physics.Raycast(at + Vector3.up * 1.2f, dir, out RaycastHit wall, 1.4f, 1 << terrain.gameObject.layer) || wall.distance < 0.7f || Mathf.Abs(wall.normal.y) > 0.5f) continue;
                        capsule.enabled = false; motor.transform.SetPositionAndRotation(at + Vector3.up * 0.02f, Quaternion.LookRotation(-dir)); capsule.enabled = true; motor.SnapPresentationForTest();
                        InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 1u << (int)GamepadButton.RightStick }); yield return null; yield return null;
                        InputSystem.QueueStateEvent(pad, new GamepadState());
                        for (int i = 0; i < 30; i++) yield return null;
                        Vector3 focus = motor.PresentationPosition + Vector3.up * 1.1f, lens = camera.transform.position;
                        visual.enabled = true; Physics.SyncTransforms();
                        bool hidden = Physics.Linecast(focus, lens, out RaycastHit cut, 1 << 31, QueryTriggerInteraction.Ignore);
                        visual.enabled = false;
                        spots++; if (hidden) behind++;
                        report.wallSpots.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1},{1:F1},{2:F1} lens {3:F2} m{4}", at.x, at.y, at.z, Vector3.Distance(focus, lens), hidden ? " BEHIND visible wall by " + (Vector3.Distance(focus, lens) - cut.distance).ToString("F2") : ""));
                        if (spots == 1) { yield return new WaitForEndOfFrame(); Capture("presentation-wall-camera-remaster.png"); }
                        break;
                    }
                }
            Destroy(probe); motor.ResetToSpawn(); orbit.enabled = false;
            report.wallSpotsTested = spots; report.wallLensBehind = behind;
            Require(spots >= 4, "Too few wall spots found: " + spots);
            Require(behind == 0, "Camera lens ends up behind the visible wall at " + behind + "/" + spots + " spots");
            report.checks.Add("camera stays in front of the visible (Remaster) wall surface at " + spots + " wall spots");
        }
        // Shape-locked references for image-to-3D props (tools/silhouette_check.py): each object alone,
        // orthographic along its thinnest horizontal axis (corner view for blocky objects), white
        // background, no post. ortho-<name>-original.png = the original model (Original mode),
        // ortho-<name>-remaster.png = what Remaster draws there (the generated model when mapped).
        private IEnumerator OrthoReferences(PrototypeCamera orbit, Camera camera, GameObject level)
        {
            orbit.enabled = false;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera);
            bool post = data.renderPostProcessing; var flags = camera.clearFlags; var background = camera.backgroundColor; int mask = camera.cullingMask;
            var hud = FindFirstObjectByType<PrototypeHud>(); if (hud != null) hud.enabled = false; // HUD must not enter the masks
            foreach (string name in new[] { "Sign_pt", "@2doors", "@rck_cx" })
            {
                Bounds frame = default; bool framed = false; // the ORIGINAL object's bounds frame both shots
                foreach (var mode in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                {
                    PrototypeGraphics.Active.SetMode(mode); yield return null;
                    // The visible object: original renderers in Original, the generated stand-in (or original) in Remaster.
                    var shown = new List<Renderer>();
                    foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        Transform t = r.transform; bool match = false;
                        // The original object or its generated stand-in (PrototypeRemasterProps names).
                        string standIn = name == "Sign_pt" ? "signpost" : name == "@2doors" ? "gate_" : "boulder_";
                        for (int depth = 0; t != null && depth < 4; depth++, t = t.parent) if (t.name.Contains(name) || t.name.StartsWith(standIn)) match = true;
                        if (match) shown.Add(r);
                    }
                    if (shown.Count == 0) continue;
                    // One object only: the renderers nearest to the first match.
                    // The same instance in both modes: the Remaster pass keeps what is near the original's frame.
                    Vector3 anchor = framed ? frame.center : shown[0].bounds.center; shown.RemoveAll(r => Vector3.Distance(r.bounds.center, anchor) > 3);
                    if (shown.Count == 0) continue;
                    Bounds b = shown[0].bounds; foreach (var r in shown) b.Encapsulate(r.bounds);
                    if (!framed) { frame = b; framed = true; } else b = frame;
                    var layers = new List<int>(); foreach (var r in shown) { layers.Add(r.gameObject.layer); r.gameObject.layer = 30; }
                    Vector3 thin = b.size.x < b.size.z * 0.6f ? Vector3.right : b.size.z < b.size.x * 0.6f ? Vector3.forward : new Vector3(1, 0, 1).normalized;
                    float width = thin == Vector3.right ? b.size.z : thin == Vector3.forward ? b.size.x : (b.size.x + b.size.z) * 0.72f;
                    camera.transform.position = b.center - thin * 20; camera.transform.rotation = Quaternion.LookRotation(thin, Vector3.up);
                    camera.orthographic = true; camera.orthographicSize = Mathf.Max(b.size.y * 0.55f, width * 0.55f / camera.aspect);
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white; camera.cullingMask = 1 << 30; data.renderPostProcessing = false;
                    yield return null; yield return new WaitForEndOfFrame();
                    Capture("ortho-" + name.Trim('@') + "-" + mode.ToString().ToLowerInvariant() + ".png");
                    for (int i = 0; i < shown.Count; i++) shown[i].gameObject.layer = layers[i];
                    camera.orthographic = false; camera.clearFlags = flags; camera.backgroundColor = background; camera.cullingMask = mask; data.renderPostProcessing = post;
                }
            }
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster); yield return null;
            if (hud != null) hud.enabled = true;
            orbit.enabled = true;
        }
        // Reference shots of each vegetation kind (first instance), with bounds in the report.
        private IEnumerator VegetationViews(PrototypeCamera orbit, Camera camera, GameObject level)
        {
            orbit.enabled = false;
            // Each plant model alone on open ground in front of the spawn.
            var spawnMotor = FindFirstObjectByType<PrototypeMotor>(); spawnMotor.ResetToSpawn();
            Vector3 stage = spawnMotor.transform.position + spawnMotor.transform.forward * 6;
            var shown = new List<GameObject>(); int slot = 0;
            foreach (string name in new[] { "fern_02", "shrub_02" })
            {
                var prefab = Resources.Load<GameObject>("Remaster/Vegetation/" + name); if (prefab == null) continue;
                var go = Instantiate(prefab); go.transform.position = stage + spawnMotor.transform.right * (slot++ * 4f - 3f); shown.Add(go);
                Bounds gb = go.GetComponentInChildren<Renderer>().bounds; foreach (var r in go.GetComponentsInChildren<Renderer>()) gb.Encapsulate(r.bounds);
                report.checks.Add("prefab " + name + " unit size " + gb.size.ToString("F2") + " renderers " + go.GetComponentsInChildren<Renderer>().Length);
            }
            camera.transform.position = stage - spawnMotor.transform.forward * 5f + Vector3.up * 2.2f; camera.transform.LookAt(stage + Vector3.up * 0.5f);
            yield return null; yield return new WaitForEndOfFrame(); Capture("veg-prefabs.png");
            foreach (var go in shown) Destroy(go);
            var firstPlant = FindFirstObjectByType<PrototypeRemasterVegetation>().FirstPlant;
            if (firstPlant != null)
            {
                Bounds pb = firstPlant.GetComponentInChildren<Renderer>().bounds; foreach (var r in firstPlant.GetComponentsInChildren<Renderer>()) pb.Encapsulate(r.bounds);
                report.checks.Add("plant " + firstPlant.name + " size " + pb.size.ToString("F2"));
                camera.transform.position = pb.center + new Vector3(1, 0.8f, 1).normalized * (pb.size.magnitude + 0.8f); camera.transform.LookAt(pb.center);
                yield return null; yield return new WaitForEndOfFrame(); Capture("veg-plant-closeup.png");
            }
            foreach (string kind in new[] { "@tree", "@tritree", "@pretty", "@3dtrees", "@3dtree1", "@3dtree2" })
            {
                MeshRenderer first = null; int count = 0;
                foreach (var r in level.GetComponentsInChildren<MeshRenderer>()) if (r.name.EndsWith(kind) || r.name.Contains(kind + " ") || r.name == kind || r.name.Contains("-" + kind) || r.name.Contains(kind + "_")) { count++; if (first == null) first = r; }
                if (first == null) { report.checks.Add("veg " + kind + ": none"); continue; }
                var b = first.bounds; var mesh = first.GetComponent<MeshFilter>().sharedMesh;
                report.checks.Add("veg " + kind + " x" + count + " size " + b.size.ToString("F1") + " centre " + b.center.ToString("F1") + " tris " + mesh.triangles.Length / 3 + " tex " + (first.sharedMaterial.mainTexture != null ? first.sharedMaterial.mainTexture.name : "-"));
                float extent = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                camera.transform.position = b.center + new Vector3(0.7f, 0.35f, 0.7f).normalized * (extent * 1.6f + 1.5f); camera.transform.LookAt(b.center);
                yield return null; yield return new WaitForEndOfFrame(); Capture("veg-" + kind.Trim('@') + ".png");
            }
            // Remaster outer world: painted boundary walls are not drawn; a band with trees, plants, grass and the
            // continued path stands behind them.
            {
                var outer = FindFirstObjectByType<PrototypeRemasterOuterWorld>(); var geo = FindFirstObjectByType<PrototypeRemasterGeometry>();
                Require(outer != null && geo != null && geo.DroppedBackdropTriangles > 0 && outer.BandTriangles > 0 && outer.Trees > 0 && outer.Plants > 0 && outer.Tufts > 0 && (outer.PathSegments > 0 || PrototypeRemasterShell.TrailMeters > 5),
                    "Outer world: dropped " + (geo != null ? geo.DroppedBackdropTriangles : 0) + ", band " + (outer != null ? outer.BandTriangles : 0) + ", trees " + (outer != null ? outer.Trees : 0) + ", path " + (outer != null ? outer.PathSegments : 0));
                report.checks.Add("Remaster outer world: " + geo.DroppedBackdropTriangles + " painted wall tris hidden; band " + outer.BandTriangles + " tris, " + outer.Trees + " trees, " + outer.Plants + " plants, " + outer.Tufts + " tufts, path " + outer.PathSegments + " segments, " + outer.GroundCrops + " ground crops, " + outer.Blocked + " plants skipped under colliders; forest pages by colour: " + string.Join(" ", PrototypeRemasterOuterWorld.ForestPages) + "; " + PrototypeRemasterOuterWorld.PartialPanels + " faces kept with their panel; paths from " + string.Join(" ", outer.PathStarts.ConvertAll(x => x.ToString("F1"))));
                // Each continued path: a view from 5 m inside, along the path, both modes.
                int pathIndex = 0; var keepOrbit = FindFirstObjectByType<PrototypeCamera>(); keepOrbit.enabled = false; var cam = Camera.main;
                foreach (var start in outer.PathStarts)
                {
                    var wall = PrototypeRemasterOuterWorld.Walls.Find(w => w.path && Vector3.Distance((w.a + w.b + w.c) / 3, start) < 8);
                    Vector3 o = wall.outward; cam.transform.position = start - o * 5 + Vector3.up * 1.7f; cam.transform.LookAt(start + o * 6 + Vector3.up * 1.2f);
                    foreach (var m in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                    { PrototypeGraphics.Active.SetMode(m); yield return null; yield return null; yield return new WaitForEndOfFrame(); Capture("path-" + pathIndex + "-" + m.ToString().ToLowerInvariant() + ".png"); }
                    pathIndex++;
                }
                keepOrbit.enabled = true;
            }
            // Winding vs normals of the Remaster tree meshes: a front face (Unity: cross(b-a, c-a) toward the viewer)
            // must agree with its vertex normals, else the tree shows its inside.
            {
                int checkedMeshes = 0, flipped = 0, faces = 0; var worst = new List<string>();
                foreach (var t in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if (!(t.name.StartsWith("Tree_") || t.name == "OuterTree") || t.sharedMesh == null) continue;
                    var mesh = t.sharedMesh; var v = mesh.vertices; var nrm = mesh.normals; checkedMeshes++;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        int[] tri = mesh.GetTriangles(sub); int bad = 0;
                        for (int i = 0; i < tri.Length; i += 3)
                        {
                            Vector3 w = Vector3.Cross(v[tri[i + 1]] - v[tri[i]], v[tri[i + 2]] - v[tri[i]]);
                            if (Vector3.Dot(w, nrm[tri[i]] + nrm[tri[i + 1]] + nrm[tri[i + 2]]) < 0) bad++;
                        }
                        faces += tri.Length / 3; flipped += bad;
                        if (bad * 10 > tri.Length / 3 && worst.Count < 5) worst.Add(t.name + " sub " + sub + " " + bad + "/" + tri.Length / 3);
                    }
                }
                report.treeFlippedFaces = flipped; report.treeFaces = faces;
                // Bark (submesh 0) is drawn with back-face culling: none of its faces may disagree.
                int barkBad = 0; foreach (var t in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if (!(t.name.StartsWith("Tree_") || t.name == "OuterTree") || t.sharedMesh == null) continue;
                    var mesh = t.sharedMesh; var v = mesh.vertices; var nrm = mesh.normals; int[] tri = mesh.GetTriangles(0);
                    for (int i = 0; i < tri.Length; i += 3) if (Vector3.Dot(Vector3.Cross(v[tri[i + 1]] - v[tri[i]], v[tri[i + 2]] - v[tri[i]]), nrm[tri[i]] + nrm[tri[i + 1]] + nrm[tri[i + 2]]) < 0) barkBad++;
                }
                Require(barkBad == 0, "Tree bark faces inward (shows the inside of the trunks): " + barkBad);
                report.checks.Add("tree winding vs normals: " + flipped + " of " + faces + " faces disagree in " + checkedMeshes + " tree meshes" + (worst.Count > 0 ? " (" + string.Join("; ", worst) + ")" : ""));
            }
            // Close-ups of Remaster trees (user: "many trees look inside out").
            {
                var cam = Camera.main; var orbit2 = FindFirstObjectByType<PrototypeCamera>(); orbit2.enabled = false; int shot = 0;
                foreach (var t in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if (!(t.name.StartsWith("Tree_") || t.name == "OuterTree") || shot >= 6) continue;
                    if (t.name == "OuterTree" && shot < 4) continue;
                    Vector3 b0 = t.transform.position; cam.transform.position = b0 + new Vector3(2.5f, 1.6f, 2.5f); cam.transform.LookAt(b0 + Vector3.up * 2.5f);
                    PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster); yield return null; yield return new WaitForEndOfFrame(); Capture("tree-close-" + shot + ".png"); shot++;
                }
                orbit2.enabled = true;
            }
            // Game texts: original pad glyphs and button names become the bound keys (user: "что за icon 4").
            {
                var pi = FindFirstObjectByType<PrototypeInput>();
                string hint = pi.ButtonText("нажми и удерживаи кнопку Р1 потом жми  {icon 4}.");
                Require(!hint.Contains("{icon") && !hint.Contains("Р1") && hint.Contains(pi.BindingLabel("Sword")) && hint.Contains(pi.BindingLabel("Chakram")), "Button names not replaced: " + hint);
                report.checks.Add("texts show bound keys for pad glyphs/buttons: " + hint);
            }
            // Remaster level trees must stand on the ground under their trunk (user: two trees float over a rock).
            {
                var floating = new List<string>(); var outOfBounds = new List<string>(); int checkedTrees = 0;
                foreach (var t in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    bool outerTree = t.name == "OuterTree";
                    if (!t.name.StartsWith("Tree_") && !outerTree) continue; checkedTrees++;
                    Vector3 b = t.transform.position; var groundQuery = FindFirstObjectByType<PrototypeMotor>(); float g = groundQuery.GroundHeight(b + Vector3.up * 0.6f);
                    float hit = float.NegativeInfinity;
                    foreach (var h in Physics.RaycastAll(b + Vector3.up * 0.6f, Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore)) if (h.collider.GetComponentInParent<PrototypeMotor>() == null) hit = Mathf.Max(hit, h.point.y);
                    float support = Mathf.Max(g, hit);
                    // Trees on the outer band (no collider): the band's own height under the trunk.
                    var outerW = FindFirstObjectByType<PrototypeRemasterOuterWorld>();
                    if (outerW != null && outerW.BandHeightAt(b, out float band)) support = Mathf.Max(support, band);
                    if (outerTree && !float.IsNegativeInfinity(hit) && hit > b.y + 0.3f) { floating.Add(t.name + " under a collider at " + b.ToString("F1")); continue; }
                    // A tree over nothing at all is an original backdrop tree beyond the walls (as in Original):
                    // listed, not a failure; any tree above existing ground is.
                    if (float.IsNegativeInfinity(support)) { outOfBounds.Add(t.name + " " + b.ToString("F1")); continue; }
                    if (b.y - support > 0.3f) floating.Add(t.name + " base " + b.ToString("F1") + " support " + support.ToString("F2"));
                }
                report.floatingTrees = floating.Count; File.WriteAllText(Path.Combine(output, "floating-trees.txt"), string.Join(System.Environment.NewLine, floating));
                Require(floating.Count == 0, "Remaster trees float above their ground: " + string.Join("; ", floating));
                report.checks.Add("all " + checkedTrees + " Remaster trees (level + outer world) stand on their ground, none under a collider; beyond the level (original backdrop trees): " + string.Join(", ", outOfBounds));
            }
            {
                var env = FindFirstObjectByType<PrototypeRemasterEnvironment>();
                Require(env != null && env.BedTriangles > 0, "Remaster water has no opaque bed");
                report.checks.Add("Remaster water bed: " + env.BedTriangles + " opaque tris " + PrototypeRemasterEnvironment.BedDepth + " m under every water surface");
            }
            // Closed shell of the start pocket: Remaster only, no collider of its own, faces really replaced.
            if (PrototypeRemasterShell.Available)
            {
                var shell = FindFirstObjectByType<PrototypeRemasterShell>(); var shellObject = GameObject.Find("RemasterShell");
                Require(shell != null && shell.Triangles > 0 && shell.Plants > 0 && shellObject != null && shellObject.GetComponent<Collider>() == null, "Remaster shell missing or collidable");
                var terrainGeometry = FindFirstObjectByType<PrototypeRemasterGeometry>();
                Require(terrainGeometry != null && terrainGeometry.DroppedBackdropTriangles > 0, "Shell region faces not dropped from the Remaster terrain");
                PrototypeGraphics.Active.SetMode(XenaVisualMode.Original); yield return null;
                Require(GameObject.Find("RemasterShell") == null, "Shell visible in Original");
                PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster); yield return null;
                Require(shellObject.activeInHierarchy, "Shell hidden in Remaster");
                Require(terrainGeometry.SilhouetteCards > 0, "Flat silhouette cards with holes still drawn in Remaster");
                report.checks.Add("Remaster shell: " + shell.Triangles + " tris, " + shell.Plants + " plants, Original restored, no collider");
            }
            // Remaster gate: both leaves get volume (rounded planks), at most thickness + grain deep per side.
            var gate = FindFirstObjectByType<PrototypeRemasterGate>();
            Require(gate != null && gate.Leaf == 2 && gate.SmoothedPages == 1 && gate.RemasterTriangles > 0 && gate.MaxDepthMeters > 0.02f && gate.MaxDepthMeters <= PrototypeRemasterGate.ThicknessMeters + PrototypeRemasterGate.GrainMeters + 1e-4f,
                "Remaster gate relief: leaves " + (gate != null ? gate.Leaf : 0) + ", depth " + (gate != null ? gate.MaxDepthMeters : 0).ToString("F3"));
            // Front-oblique views at eye height (the original leaves are one-sided: front = their normal side).
            {
                var leaf = System.Array.Find(FindObjectsByType<MeshFilter>(FindObjectsSortMode.None), f => f.name == "6-@2doors"); // fixed leaf: same views every run
                PrototypeGraphics.Active.SetMode(XenaVisualMode.Original); yield return null;
                Vector3 n = Vector3.zero; foreach (var v in leaf.sharedMesh.normals) n += v; n = leaf.transform.TransformDirection(n); n.y = 0; n.Normalize();
                Bounds gb = leaf.GetComponent<Renderer>().bounds; foreach (var f in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)) if (f.name.Contains("@2doors")) gb.Encapsulate(f.GetComponent<Renderer>().bounds);
                Vector3 side = Vector3.Cross(Vector3.up, n);
                foreach (var (tag, offset) in new[] { ("front", n * 4.5f), ("oblique", n * 3f + side * 2.2f), ("close", n * 1.6f + side * 0.9f) })
                {
                    camera.transform.position = new Vector3(gb.center.x, gb.min.y + 1.6f, gb.center.z) + offset; camera.transform.LookAt(gb.center + Vector3.up * 0.2f);
                    foreach (var m in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                    { PrototypeGraphics.Active.SetMode(m); yield return null; yield return new WaitForEndOfFrame(); Capture("gate-" + tag + "-" + m.ToString().ToLowerInvariant() + ".png"); }
                    gate.ShowFlatForTest(true); yield return null; yield return new WaitForEndOfFrame(); Capture("gate-" + tag + "-remaster-flat.png"); gate.ShowFlatForTest(false);
                }
            }
            // The smoothed page copy must keep the HD page's colours (only the alpha changes).
            {
                var leafR = System.Array.Find(FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None), r => r.name == "6-@2doors");
                PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster); yield return null; yield return null;
                var copy = leafR.sharedMaterial.mainTexture as Texture2D;
                Require(copy != null && copy.name.EndsWith(PrototypeRemasterGate.SmoothSuffix), "Gate does not use its smoothed page copy");
                var px = copy.GetPixels32(); double sum = 0; int n = 0; foreach (var c in px) if (c.a > 128) { sum += (c.r + c.g + c.b) / 3.0; n++; }
                report.gateCopyMean = (float)(sum / Mathf.Max(1, n));
                Require(report.gateCopyMean > 34 && report.gateCopyMean < 46, "Gate page copy colours differ from the HD page (opaque mean " + report.gateCopyMean.ToString("F1") + ", page ~40)");
            }
            report.checks.Add("Remaster gate: 2 leaves with rounded plank volume (" + gate.RemasterTriangles + " tris, max depth " + gate.MaxDepthMeters.ToString("F3") + " m per side), original outline and collision");
            // Props for the image-to-3D trial: first signpost and the bridge, both modes.
            foreach (string prop in new[] { "Sign_pt", "@u_brg", "@2doors", "@rck_cx" })
            {
                Renderer target = null; foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if ((r.name.Contains(prop) || r.transform.root.name.Contains(prop) || (r.transform.parent != null && r.transform.parent.name.Contains(prop))) && r.enabled) { target = r; break; }
                if (target == null && prop == "Sign_pt") foreach (var body in FindObjectsByType<XenaAssetPreview>(FindObjectsSortMode.None)) if (body.name.StartsWith("Sign_pt")) { target = body.GetComponentInChildren<Renderer>(true); break; }
                if (target == null) { report.checks.Add("prop " + prop + ": none"); continue; }
                var pb = target.bounds; report.checks.Add("prop " + prop + " " + target.name + " size " + pb.size.ToString("F2") + " centre " + pb.center.ToString("F1"));
                float ext = Mathf.Max(pb.size.x, Mathf.Max(pb.size.y, pb.size.z));
                // Thin objects (doors): look along the thin axis from both sides; others from a corner.
                Vector3 thin = pb.size.x < pb.size.z * 0.4f ? Vector3.right : pb.size.z < pb.size.x * 0.4f ? Vector3.forward : Vector3.zero;
                // Thin objects also get an oblique view (-c) that shows their depth.
                var views = thin == Vector3.zero ? new[] { new Vector3(0.8f, 0.3f, 0.6f).normalized * (ext * 1.4f + 0.6f) }
                    : new[] { thin * (ext * 1.2f) + Vector3.up * 0.3f, -thin * (ext * 1.2f) + Vector3.up * 0.3f, (thin + Vector3.Cross(thin, Vector3.up)).normalized * (ext * 0.9f) + Vector3.up * 0.4f };
                for (int v = 0; v < views.Length; v++)
                {
                    camera.transform.position = pb.center + views[v]; camera.transform.LookAt(pb.center);
                    foreach (var m in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                    { PrototypeGraphics.Active.SetMode(m); yield return null; yield return new WaitForEndOfFrame(); Capture("prop-" + prop.Trim('@') + (v == 1 ? "-b" : v == 2 ? "-c" : "") + "-" + m.ToString().ToLowerInvariant() + ".png"); }
                }
            }
            orbit.enabled = true;
        }
        // Perimeter tour: painted walls grouped within 10 m; per group the lens stands on the corridor ground
        // 6 m inside (eye height), looking at the wall, in both modes: perimeter-<n>-{original,remaster}.png;
        // perimeter.json lists the groups.
        private IEnumerator PerimeterTour(PrototypeMotor motor, PrototypeCamera orbit, Camera camera)
        {
            var walls = PrototypeRemasterOuterWorld.Walls; if (walls == null) yield break;
            orbit.enabled = false;
            var groups = new List<(Vector3 c, Vector3 o, int n)>();
            foreach (var w in walls)
            {
                Vector3 c = (w.a + w.b + w.c) / 3; int g = groups.FindIndex(x => Vector3.Distance(x.c, c) < 10);
                if (g < 0) groups.Add((c, w.outward, 1)); else { var x = groups[g]; groups[g] = ((x.c * x.n + c) / (x.n + 1), x.o + w.outward, x.n + 1); }
            }
            var lines = new List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                Vector3 c = groups[i].c, o = groups[i].o; o.y = 0; o.Normalize();
                Vector3 eye = Vector3.zero; bool found = false;
                foreach (float back in new[] { 6f, 4f, 8f, 3f })
                {
                    Vector3 q = c - o * back; float g = motor.GroundHeight(new Vector3(q.x, c.y + 4, q.z));
                    if (!float.IsNegativeInfinity(g)) { eye = new Vector3(q.x, g + 1.7f, q.z); found = true; break; }
                }
                if (!found) { lines.Add(i + " no ground " + c.ToString("F1")); continue; }
                camera.transform.position = eye; camera.transform.LookAt(new Vector3(c.x, eye.y - 0.3f, c.z) + o * 4);
                foreach (var m in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                { PrototypeGraphics.Active.SetMode(m); yield return null; yield return null; yield return new WaitForEndOfFrame(); Capture("perimeter-" + i + "-" + m.ToString().ToLowerInvariant() + ".png"); }
                var outerWorld = FindFirstObjectByType<PrototypeRemasterOuterWorld>();
                outerWorld.ApplyVisualMode(XenaVisualMode.Original); yield return null; yield return new WaitForEndOfFrame(); Capture("perimeter-" + i + "-remaster-noouter.png"); outerWorld.ApplyVisualMode(XenaVisualMode.Remaster);
                lines.Add(i + " walls " + groups[i].n + " centre " + c.ToString("F1") + " eye " + eye.ToString("F1"));
            }
            // Around the light shafts (user: the right wall there let the view out of bounds): four headings each.
            int shaftIndex = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.name.Contains("@sunbm")) continue;
                Vector3 c = r.bounds.center; float g = motor.GroundHeight(new Vector3(c.x, c.y + 2, c.z));
                if (float.IsNegativeInfinity(g)) g = r.bounds.min.y;
                for (int h = 0; h < 4; h++)
                {
                    camera.transform.SetPositionAndRotation(new Vector3(c.x, g + 1.7f, c.z), Quaternion.Euler(0, h * 90, 0));
                    foreach (var m in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                    { PrototypeGraphics.Active.SetMode(m); yield return null; yield return null; yield return new WaitForEndOfFrame(); Capture("shaft-" + shaftIndex + "-" + h * 90 + "-" + m.ToString().ToLowerInvariant() + ".png"); }
                }
                lines.Add("shaft " + shaftIndex + " at " + c.ToString("F1")); shaftIndex++;
            }
            // The lake from above on four sides (user: the outer world had covered it with forest).
            foreach (var f in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!PrototypeRemasterEnvironment.IsWaterSurface(f.sharedMesh)) continue;
                Bounds lb = f.GetComponent<Renderer>().bounds;
                for (int h = 0; h < 4; h++)
                {
                    Vector3 dir = Quaternion.Euler(0, h * 90, 0) * Vector3.forward;
                    camera.transform.position = lb.center - dir * (Mathf.Max(lb.extents.x, lb.extents.z) + 4) + Vector3.up * 7; camera.transform.LookAt(lb.center);
                    foreach (var m in new[] { XenaVisualMode.Original, XenaVisualMode.Remaster })
                    { PrototypeGraphics.Active.SetMode(m); yield return null; yield return null; yield return new WaitForEndOfFrame(); Capture("lake-" + h * 90 + "-" + m.ToString().ToLowerInvariant() + ".png"); }
                }
                break;
            }
            // Overview: the whole level from 8 sides, 25 m up, 70 m out (hanging band edges, shores, water).
            {
                Bounds lv = new Bounds(motor.transform.position, Vector3.one); foreach (var r in GameObject.Find("OriginalLevel01").GetComponentsInChildren<MeshRenderer>()) lv.Encapsulate(r.bounds);
                PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster);
                for (int h = 0; h < 8; h++)
                {
                    Vector3 dir = Quaternion.Euler(0, h * 45, 0) * Vector3.forward;
                    camera.transform.position = lv.center - dir * (Mathf.Max(lv.extents.x, lv.extents.z) + 25) + Vector3.up * 25; camera.transform.LookAt(lv.center);
                    yield return null; yield return null; yield return new WaitForEndOfFrame(); Capture("overview-" + h * 45 + ".png");
                }
            }
            File.WriteAllText(Path.Combine(output, "perimeter.txt"), string.Join(System.Environment.NewLine, lines));
            report.checks.Add("perimeter tour: " + groups.Count + " painted-wall groups captured");
            orbit.enabled = true;
        }
        // Sky views: lens at Xena's eye height, 12 deg up, four headings (painted landscape check).
        private IEnumerator SkyViews(PrototypeMotor motor, PrototypeCamera orbit, Camera camera, string name)
        {
            orbit.enabled = false; motor.ResetToSpawn();
            for (int heading = 0; heading < 4; heading++)
            {
                camera.transform.SetPositionAndRotation(motor.transform.position + Vector3.up * 1.7f, Quaternion.Euler(-12, heading * 90, 0));
                yield return null; yield return new WaitForEndOfFrame();
                Capture(heading == 0 ? name : name.Replace(".png", "-" + heading * 90 + ".png"));
                if (heading == 0)
                    foreach (var probe in new[] { new Vector2(0.75f, 0.75f), new Vector2(0.6f, 0.45f), new Vector2(0.2f, 0.35f) })
                    {
                        // Which meshes the prominent shapes of this view belong to (nearest renderer hit by bounds+mesh).
                        var ray = camera.ViewportPointToRay(probe); string found = "none"; float best = float.MaxValue;
                        foreach (var f in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                        {
                            var r = f.GetComponent<Renderer>(); if (r == null || !r.enabled || f.sharedMesh == null || !r.bounds.IntersectRay(ray)) continue;
                            var temp = f.gameObject.AddComponent<MeshCollider>(); temp.sharedMesh = f.sharedMesh;
                            if (temp.Raycast(ray, out RaycastHit hit, 500) && hit.distance < best) { best = hit.distance; found = f.name + "/" + f.sharedMesh.name; }
                            Destroy(temp);
                        }
                        report.checks.Add("probe " + name + " " + probe + ": " + found + " at " + best.ToString("F1") + " m");
                    }
            }
            orbit.enabled = true;
        }
        private IEnumerator Gameplay(PrototypeMotor motor, PrototypeCamera orbit, string name, Vector3? at = null)
        {
            orbit.enabled = true;
            // Four headings from the spawn, camera centred behind Xena by R3.
            for (int heading = 0; heading < 4; heading++)
            {
                motor.ResetToSpawn();
                if (at.HasValue)
                {
                    var capsule = motor.GetComponent<CharacterController>(); capsule.enabled = false;
                    float ground = motor.GroundHeight(at.Value + Vector3.up * 1.5f); motor.transform.position = new Vector3(at.Value.x, ground + 0.02f, at.Value.z); capsule.enabled = true;
                }
                motor.transform.rotation = Quaternion.Euler(0, heading * 90, 0);
                InputSystem.QueueStateEvent(pad, new GamepadState { buttons = 1u << (int)GamepadButton.RightStick }); yield return null; yield return null;
                InputSystem.QueueStateEvent(pad, new GamepadState());
                for (int i = 0; i < 40; i++) yield return null;
                yield return new WaitForEndOfFrame(); Capture(heading == 0 ? name : name.Replace(".png", "-" + heading * 90 + ".png"));
            }
            motor.ResetToSpawn(); orbit.enabled = false;
        }
        private IEnumerator Portrait(Camera camera, Transform subject, string name)
        {
            camera.transform.position = subject.position + subject.forward * -3.2f + Vector3.up * 1.6f;
            camera.transform.LookAt(subject.position + Vector3.up * 1.0f);
            for (int i = 0; i < 20; i++) yield return null;
            yield return new WaitForEndOfFrame(); Capture(name);
        }
        private IEnumerator Front(Camera camera, Bounds fall, Vector3 facing, string name, float distance = 0)
        {
            Vector3 eye = new Vector3(fall.center.x, fall.min.y + 2.2f + distance * 0.15f, fall.center.z) + facing * (distance > 0 ? distance : Mathf.Max(9, fall.size.magnitude * 0.9f));
            camera.transform.position = eye; camera.transform.LookAt(fall.center);
            for (int i = 0; i < 20; i++) yield return null;
            yield return new WaitForEndOfFrame(); Capture(name);
        }
        private IEnumerator View(Camera camera, Bounds bounds, Vector3 direction, float distance, string name)
        {
            camera.transform.position = bounds.center + direction.normalized * Mathf.Max(2.5f, bounds.extents.magnitude * distance);
            camera.transform.LookAt(bounds.center);
            for (int i = 0; i < 12; i++) yield return null;
            yield return new WaitForEndOfFrame(); Capture(name);
        }
        private void Capture(string name) { var shot = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, name), shot.EncodeToPNG()); Destroy(shot); }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true; if (error != null) report.error = error.ToString();
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "presentation-player-verification.json"), JsonUtility.ToJson(report, true)); Application.Quit(error == null ? 0 : 1);
        }
    }
}
