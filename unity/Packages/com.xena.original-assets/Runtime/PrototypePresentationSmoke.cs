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
        [Serializable] private sealed class Report { public string result = "FAIL", error; public int highResolutionPages, stpTexels, originalTriangles, remasterTriangles; public float maxTerrainOffset, maxWalkableOffset; public int seamEdges, curtains, foams, bandits; public List<string> checks = new List<string>(), wallSpots = new List<string>(); public int wallSpotsTested, wallLensBehind; }
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
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F10)); yield return null; yield return null;
            var pages = new HashSet<Texture>();
            foreach (var item in level.GetComponentsInChildren<MeshRenderer>()) foreach (var material in item.sharedMaterials)
                if (material.mainTexture != null && material.mainTexture.width == 1024) pages.Add(material.mainTexture);
            report.highResolutionPages = pages.Count;
            Require(graphics.Mode == XenaVisualMode.Remaster && preview.UsingUpscale && pages.Count == 16 && motor.transform.position == position, "F10 did not bind all16 real high-resolution environment pages");
            report.checks.Add("actual F10 input changes environment256px to16 real1024px pages without moving actor");
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
            yield return WallCamera(motor, orbit, camera, level);
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
            Require(geometry.RemasterActive && geometry.RemasterTriangles == geometry.OriginalTriangles * perFace && terrain.sharedMesh.triangles.Length / 3 == 2290 * perFace
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
            Require(camera.clearFlags == CameraClearFlags.SolidColor && background.r == 0 && background.g == 72 && background.b == 95, "Original background is not measured RGB(0,72,95)");
            report.checks.Add("Original clears to the measured native background RGB(0,72,95)");
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
        // Sky views: lens at Xena's eye height, 12 deg up, four headings (painted landscape check).
        private IEnumerator SkyViews(PrototypeMotor motor, PrototypeCamera orbit, Camera camera, string name)
        {
            orbit.enabled = false; motor.ResetToSpawn();
            for (int heading = 0; heading < 4; heading++)
            {
                camera.transform.SetPositionAndRotation(motor.transform.position + Vector3.up * 1.7f, Quaternion.Euler(-12, heading * 90, 0));
                yield return null; yield return new WaitForEndOfFrame();
                Capture(heading == 0 ? name : name.Replace(".png", "-" + heading * 90 + ".png"));
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
