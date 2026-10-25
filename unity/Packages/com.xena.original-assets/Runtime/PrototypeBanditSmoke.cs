using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Xena.Assets
{
    // --bandit-smoke: three original bandits around Xena; checks the
    // func_8001B460 subset (notice, approach, attack range, attack clips,
    // turn limit, max two attackers, actor radius, original ground).
    internal sealed class PrototypeBanditSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public string result = "FAIL", error; public List<string> checks = new List<string>();
            public string firstSign, firstScroll; public int firstHitDamage, deathSlot, firstIntake, blockedIntake; public int runRenderFrames, runStalledFrames; public int steps, attacks, maxSimultaneousAttackers, maxTurn, closestUnits = int.MaxValue, spawnDelayTicks, maxLive; public float maxGroundError;
        }
        private readonly Report report = new Report();
        private string output; private bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--bandit-smoke");
            if (at >= 0 && at + 1 < args.Length) new GameObject("BanditSmoke").AddComponent<PrototypeBanditSmoke>().output = Path.GetFullPath(args[at + 1]);
        }
        private void Awake() { Application.logMessageReceived += OnLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type) { if (!finished && type == LogType.Exception) Finish(new Exception(message + stack)); }
        private void Require(bool value, string message) { if (!value) { Finish(new Exception(message)); throw new Exception(message); } }
        private IEnumerator Start()
        {
            Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            var motor = FindFirstObjectByType<PrototypeMotor>();
            for (int i = 0; i < 10; i++) yield return null;
            var bandits = motor.GetComponent<PrototypeBandits>();
            Require(bandits.GeneratorCount == 11 && bandits.Spawned + bandits.TotalRemaining == 12, "Original generator records differ");
            // Generator #33: radius 9000 around (-1296,746,5063), flag 0x2000 |dy| <= 1000.
            Vector3 centre = new Vector3(-1296, -746, 5063) / 400;
            Vector3 stand = Vector3.zero; bool found = false;
            for (float r = 6; r < 18 && !found; r += 0.5f)
                for (int k = 0; k < 16 && !found; k++)
                {
                    Vector3 p = centre + Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward * r; float ground = motor.GroundHeight(p + Vector3.up * 2);
                    if (!float.IsNegativeInfinity(ground) && Mathf.Abs(ground - centre.y) * 400 < 800) { stand = new Vector3(p.x, ground, p.z); found = true; }
                }
            Require(found, "No original ground inside generator #33 trigger");
            int before = bandits.Spawned;
            var capsule = motor.GetComponent<CharacterController>(); capsule.enabled = false; motor.transform.position = stand + Vector3.up * 0.05f; capsule.enabled = true; Physics.SyncTransforms();
            int waited = 0; while (bandits.Spawned == before && waited++ < 60 * 3) yield return new WaitForFixedUpdate();
            report.spawnDelayTicks = waited;
            Require(bandits.Spawned > before && waited <= 40, "Generator inside its trigger did not release within one 16-frame period: " + waited);
            report.checks.Add("generator releases a bandit within one 16-frame period once Xena is inside its trigger");
            for (int i = 0; i < 60 * 6; i++) { yield return new WaitForFixedUpdate(); report.maxLive = Mathf.Max(report.maxLive, bandits.Live()); }
            Require(report.maxLive <= bandits.MaxLiveEnemies, "More than 3 enemies alive"); report.checks.Add("at most 3 generator enemies alive level-wide");
            bandits.GeneratorsEnabled = false;
            foreach (var brain in bandits.Brains) { brain.Active = false; brain.gameObject.SetActive(false); }
            motor.ResetToSpawn(); yield return new WaitForFixedUpdate();
            // AI: three bandits 4m in front of Xena at the original spawn.
            Vector3 forward = motor.transform.forward, right = motor.transform.right;
            var chosen = new List<PrototypeBanditBrain>();
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = motor.transform.position + forward * 4 + right * (i - 1) * 1.6f;
                var made = bandits.SpawnForTest(p); Require(made != null, "No pending generator left for AI test");
                made.transform.rotation = Quaternion.LookRotation(-forward); made.Configure(made.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(made), 75, 50, 101 + i);
                chosen.Add(made);
            }
            bool noticed = false, inRange = false, attacked = false;
            // Drawn-body continuity while the first bandit runs in (120 FPS render).
            Transform drawn = chosen[0].GetComponentInChildren<XenaAssetPreview>().transform; Vector3 lastDrawn = drawn.position;
            int movingFrames = 0, stalledFrames = 0; float lastStep = 0;
            for (int tick = 0; tick < 60 * 12; tick++)
            {
                yield return new WaitForFixedUpdate();
                int attackers = 0;
                foreach (var brain in chosen)
                {
                    if (brain.State != PrototypeBanditBrain.Mode.Idle) noticed = true;
                    if (brain.State == PrototypeBanditBrain.Mode.Attack) attackers++;
                    if (Array.IndexOf(PrototypeBanditBrain.Attacks, brain.Slot) >= 0) attacked = true;
                    Vector3 d = (motor.transform.position - brain.transform.position) * 400;
                    int units = Mathf.RoundToInt(new Vector2(d.x, d.z).magnitude); report.closestUnits = Mathf.Min(report.closestUnits, units);
                    if (units <= PrototypeBanditBrain.AttackRange + 50) inRange = true;
                    float ground = motor.GroundHeight(brain.transform.position + Vector3.up * 0.5f);
                    report.maxGroundError = Mathf.Max(report.maxGroundError, Mathf.Abs(ground - brain.transform.position.y));
                    report.maxTurn = Mathf.Max(report.maxTurn, brain.MaxTurnObserved);
                }
                report.maxSimultaneousAttackers = Mathf.Max(report.maxSimultaneousAttackers, attackers);
                if (tick == 60 * 6) { yield return new WaitForEndOfFrame(); Capture("bandit-engage.png"); }
                if (chosen[0].Slot == PrototypeBanditBrain.Run || chosen[0].Slot == PrototypeBanditBrain.Walk)
                {
                    yield return new WaitForEndOfFrame();
                    float moved = Vector3.Distance(drawn.position, lastDrawn);
                    if (moved > 0.0005f) { movingFrames++; lastStep = moved; } else if (lastStep > 0) stalledFrames++;
                }
                lastDrawn = drawn.position;
            }
            foreach (var brain in chosen) { report.attacks += brain.AttacksStarted; report.steps = Mathf.Max(report.steps, brain.Steps); }
            report.runRenderFrames = movingFrames + stalledFrames; report.runStalledFrames = stalledFrames;
            Require(movingFrames > 20 && stalledFrames * 10 <= movingFrames + stalledFrames, "Bandit body steps with the 30Hz AI instead of gliding: " + stalledFrames + "/" + (movingFrames + stalledFrames));
            report.checks.Add("running bandits are render-interpolated between AI steps (no stalled frames)");
            Require(noticed, "Bandits did not notice Xena inside 9500 units ahead"); report.checks.Add("bandits notice Xena within 9500 units, not from the rear");
            Require(inRange, "Bandits did not close to the 700-unit attack range"); report.checks.Add("bandits close to the 700-unit attack range using clip root motion");
            Require(attacked && report.attacks > 0, "Bandits never started an original attack clip"); report.checks.Add("attack mode plays original 3D/3E/3F/50 clips");
            Require(report.maxSimultaneousAttackers <= 2, "More than two bandits attacked the same target"); report.checks.Add("at most two bandits attack Xena at once; others wait");
            Require(report.maxTurn <= PrototypeBanditBrain.Turn, "Bandit turned faster than 64/4096 per step"); report.checks.Add("turn rate limited to 64/4096 per AI step");
            Require(report.closestUnits >= 2 * PrototypeBanditBrain.Radius - 40, "Bandit entered Xena's actor radius"); report.checks.Add("actor radii 150+150 keep bandits out of Xena");
            Require(report.maxGroundError < 0.01f, "Bandit left the original ground"); report.checks.Add("bandits stay on the original height query ground");
            Require(Mathf.Abs(report.steps - 360) <= 6, "AI did not run at 30 steps per second: " + report.steps); report.checks.Add("AI runs 30 steps per second");
            foreach (var brain in chosen) { brain.Active = false; brain.gameObject.SetActive(false); }
            yield return Scripts(motor);
            yield return Damage(motor, bandits);
            report.result = "PASS"; Finish(null);
        }
        private IEnumerator Damage(PrototypeMotor motor, PrototypeBandits bandits)
        {
            motor.enabled = true; Camera.main.GetComponent<PrototypeCamera>().enabled = true; motor.ResetToSpawn();
            var damage = motor.GetComponent<PrototypeOriginalDamage>(); damage.ResetState();
            var scripts = motor.GetComponent<PrototypeLevelScripts>();
            var input = motor.GetComponent<PrototypeInput>(); var keyboard = InputSystem.AddDevice<Keyboard>(); var mouse = InputSystem.AddDevice<Mouse>();
            input.UseDevices(keyboard, mouse, InputSystem.AddDevice<Gamepad>());
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Vector3 front = motor.transform.position + motor.transform.forward * 1.3f;
            var foe = bandits.SpawnForTest(front); Require(foe != null, "No pending bandit for damage test");
            foe.transform.rotation = Quaternion.LookRotation(-motor.transform.forward); foe.Configure(foe.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(foe), 75, 0, 7);
            foe.Active = false; // stand still while Xena strikes
            int before = foe.Health;
            // Hold the press for real frames: after the 4x ending, fixed-step catch-up
            // could otherwise deliver press and release to Input System in one update.
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); for (int i = 0; i < 4; i++) yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState()); for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            report.firstHitDamage = before - foe.Health;
            Require(report.firstHitDamage == 30 + scripts.SwordLevel, "Sword 51 did not deal original 30 + sword level: " + report.firstHitDamage);
            report.checks.Add("sword 51 deals the original 30 + sword level to a bandit");
            Require(motor.GetComponent<PrototypeHud>().VisibleEnemyBars >= 1, "Enemy dagger bar not shown after the hit"); report.checks.Add("HUD shows the enemy dagger bar for 60 frames after its health changes");
            foe.Active = true;
            for (int press = 0; press < 30 && !foe.Dying; press++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); for (int i = 0; i < 4; i++) yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState()); for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                Vector3 to = foe.transform.position - motor.transform.position; to.y = 0; if (to.sqrMagnitude > 0.01f) motor.transform.rotation = Quaternion.LookRotation(to);
            }
            Require(foe.Dying && foe.Health == 0, "Bandit did not die from original damage"); report.deathSlot = foe.Slot;
            Require(foe.Slot >= 0x8A && foe.Slot <= 0x91, "Death did not play an original 8A..91 clip"); report.checks.Add("bandit at 0 health plays an original death clip");
            int liveBefore = bandits.Live(); Time.timeScale = 4;
            for (int i = 0; i < 60 * 25 && !foe.Removed; i++) yield return new WaitForFixedUpdate();
            Time.timeScale = 1;
            Require(foe.Removed && bandits.Live() == liveBefore - 1 && bandits.Kills >= 1, "Dead bandit was not removed after the 300-frame wait");
            report.checks.Add("dead bandit is removed after the clip + 300 frames; live counter drops, kill counts");
            var graphics = PrototypeGraphics.Active;
            Require(graphics.Mode == XenaVisualMode.Remaster && foe.CorpseVisible && foe.gameObject.activeSelf, "Remaster did not keep the corpse");
            graphics.SetMode(XenaVisualMode.Original); Require(!foe.CorpseVisible, "Original kept the corpse");
            graphics.SetMode(XenaVisualMode.Remaster); Require(foe.CorpseVisible, "Corpse did not return in Remaster");
            report.checks.Add("Remaster keeps corpses in their death pose; Original hides them");
            // Intake: an attacking bandit next to Xena.
            damage.ResetState();
            var attacker = bandits.SpawnForTest(motor.transform.position + motor.transform.forward * 1.2f); Require(attacker != null, "No pending bandit for intake test");
            attacker.transform.rotation = Quaternion.LookRotation(-motor.transform.forward); attacker.Configure(attacker.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(attacker), 75, 90, 11);
            for (int i = 0; i < 60 * 15 && damage.HitsTaken == 0; i++) yield return new WaitForFixedUpdate();
            report.firstIntake = damage.LastIntake;
            Require(report.firstIntake == 15 || report.firstIntake == 25 || report.firstIntake == 30, "Bandit hit did not use an original window damage: " + report.firstIntake);
            report.checks.Add("bandit attack damages Xena by its original window value");
            var hud = motor.GetComponent<PrototypeHud>(); for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Require(hud.Loaded && hud.TargetLength == PrototypeHud.HealthLength(damage.Health, 500) && hud.ShownLength == hud.TargetLength,
                "HUD did not follow the original health mapping: " + hud.ShownLength + "/" + hud.TargetLength + " bars " + hud.VisibleEnemyBars);
            report.checks.Add("HUD sword length = clamp(109*hp/500,3,109)");
            yield return new WaitForEndOfFrame(); Capture("hud-remaster.png");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Original); for (int i = 0; i < 3; i++) yield return null; yield return new WaitForEndOfFrame(); Capture("hud-original.png");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster);
            int health = damage.Health, blocked = damage.BlockedHits;
            Vector3 at = attacker.transform.position - motor.transform.position; at.y = 0; motor.transform.rotation = Quaternion.LookRotation(at);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 });
            for (int i = 0; i < 60 * 15 && damage.BlockedHits == blocked; i++) { yield return new WaitForFixedUpdate(); Vector3 t = attacker.transform.position - motor.transform.position; t.y = 0; if (t.sqrMagnitude > 0.01f) motor.transform.rotation = Quaternion.LookRotation(t); }
            InputSystem.QueueStateEvent(mouse, new MouseState());
            report.blockedIntake = damage.LastIntake;
            Require(damage.BlockedHits > blocked && damage.LastIntakeBlocked && report.blockedIntake == 1, "Blocked bandit hit did not deal original 1 damage: " + report.blockedIntake);
            report.checks.Add("blocked bandit hits deal d>40 ? d-40 : 1 (=1 for bandits)");
            attacker.Active = false;
        }
        private IEnumerator Visit(PrototypeMotor motor, Vector3 at)
        {
            float ground = motor.GroundHeight(at + Vector3.up * 1.5f); Require(!float.IsNegativeInfinity(ground), "No ground at script actor");
            var capsule = motor.GetComponent<CharacterController>(); capsule.enabled = false; motor.transform.position = new Vector3(at.x, ground + 0.02f, at.z); capsule.enabled = true; Physics.SyncTransforms();
            for (int i = 0; i < 12; i++) yield return null;
        }
        private IEnumerator Scripts(PrototypeMotor motor)
        {
            var scripts = motor.GetComponent<PrototypeLevelScripts>();
            var signs = new List<Vector3>(scripts.TriggerPositions("sign"));
            Require(scripts.PlacedSprites == 6, "Pickup sprites not placed: " + scripts.PlacedSprites); report.checks.Add("6 original pickup sprites (health2 x3, scroll x2, up_sword) placed screen-facing");
            var music = motor.GetComponent<PrototypeMusic>(); Require(music != null && music.enabled && music.Track >= 0 && music.Muted, "Original m_vill music not loaded"); report.checks.Add("m_vill sequences load; exploration track plays (muted in smoke)");
            Require(scripts.PlacedModels == 4, "Original sign/Siras models not placed: " + scripts.PlacedModels); report.checks.Add("3 original SIGN_PT models and SIRAS placed from the level records");
            yield return Visit(motor, signs[0]);
            Require(scripts.Modal && scripts.ShownIndex == 0x46 && Time.timeScale == 0, "Sign 0 did not show message 0x46 modally: " + scripts.ShownIndex);
            Camera.main.GetComponent<PrototypeCamera>().enabled = false; Camera.main.transform.position = signs[0] + new Vector3(1.6f, 1.6f, 1.6f); Camera.main.transform.LookAt(signs[0] + Vector3.up * 0.8f); yield return null;
            report.firstSign = string.Join("|", scripts.Shown); yield return new WaitForEndOfFrame(); Capture("script-sign.png");
            Camera.main.GetComponent<PrototypeCamera>().enabled = true;
            scripts.Close(); for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            Require(!scripts.Modal, "Sign repeated while Xena stayed within 400 units"); report.checks.Add("sign shows message param+0x46 modally and does not repeat while Xena stays near");
            yield return Visit(motor, signs[0] + Vector3.right * 3); for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            yield return Visit(motor, signs[0]);
            Require(scripts.Modal && scripts.ShownIndex == 0x46, "Sign was not re-armed after leaving 400 units"); scripts.Close();
            report.checks.Add("sign re-arms after Xena leaves 400 units");
            var pickups = new List<Vector3>(scripts.TriggerPositions("pickup"));
            var vitality = motor.GetComponent<PrototypeOriginalDamage>(); vitality.ResetState();
            // Export order: up_sword#4, scroll#5, scroll#6, health2#7..#9.
            { var cam = Camera.main; var orbit = cam.GetComponent<PrototypeCamera>(); orbit.enabled = false;
              cam.transform.position = pickups[3] + new Vector3(1.4f, 1.0f, 1.4f); cam.transform.LookAt(pickups[3] + Vector3.up * 0.3f);
              for (int i = 0; i < 5; i++) yield return null; yield return new WaitForEndOfFrame(); Capture("script-potion-sprite.png");
              cam.transform.position = pickups[1] + new Vector3(1.4f, 1.0f, 1.4f); cam.transform.LookAt(pickups[1] + Vector3.up * 0.3f);
              for (int i = 0; i < 5; i++) yield return null; yield return new WaitForEndOfFrame(); Capture("script-scroll-sprite.png"); orbit.enabled = true; }
            yield return Visit(motor, pickups[1]);
            Require(scripts.Modal && scripts.ShownIndex == 2, "Scroll #5 did not show message level*2+0 = 2"); report.firstScroll = string.Join("|", scripts.Shown);
            yield return new WaitForEndOfFrame(); Capture("script-scroll.png"); scripts.Close();
            report.checks.Add("scroll shows message level*2+p0");
            yield return Visit(motor, pickups[3]); Require(scripts.Modal && scripts.ShownIndex == 54 && scripts.Potions == 1, "First potion did not show message 54"); scripts.Close();
            yield return Visit(motor, pickups[4]); for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Require(!scripts.Modal && scripts.Potions == 2, "Second potion repeated its first-pickup message"); report.checks.Add("first-pickup message once; later potions are silent");
            Require(vitality.Health == 500 && scripts.Potions == 2, "Potions picked at full health were not kept");
            vitality.SetHealthForTest(300); yield return Visit(motor, pickups[5]); for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Require(scripts.Potions == 2 && vitality.Health == 450, "Potion picked at 300/500 was not used at once (+150): " + vitality.Health + " potions " + scripts.Potions);
            report.checks.Add("health2 is used at once when health+150 < 500, otherwise kept");
            yield return Visit(motor, pickups[0]); Require(scripts.Modal && scripts.ShownIndex == 69 && scripts.SwordLevel == 1, "Sword upgrade did not apply"); scripts.Close();
            report.checks.Add("up_sword raises sword level and shows message 69");
            var siras = new List<Vector3>(scripts.TriggerPositions("levelEndNpc"));
            yield return Visit(motor, siras[0] + Vector3.forward * 2.5f); for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Require(scripts.CutsceneActive && scripts.CutsceneFrames == 563, "Reaching Siras did not start the 563-frame ending: " + scripts.CutsceneFrames);
            Time.timeScale = 4; int guard = 0; bool captured = false;
            while (scripts.CutsceneActive && guard++ < 60 * 30) { yield return new WaitForFixedUpdate(); if (!captured && scripts.CutsceneFrame > 200) { captured = true; yield return new WaitForEndOfFrame(); Capture("script-ending.png"); } }
            Time.timeScale = 1;
            Require(scripts.LevelComplete && !scripts.CutsceneActive, "Ending camera path did not finish into level end"); report.checks.Add("Siras within 1450 plays the original 563-frame camera path, then ends the level");
        }
        private static Dictionary<int, PrototypeBanditBrain.MotionClip> Motion(PrototypeBanditBrain brain)
        {
            var clips = new Dictionary<int, PrototypeBanditBrain.MotionClip>();
            foreach (var clip in JsonUtility.FromJson<PrototypeBanditBrain.MotionFile>(Resources.Load<TextAsset>("Bandits/Bandit/BanditMotion").text).clips) clips[clip.slot] = clip;
            return clips;
        }
        private void Capture(string name) { var shot = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, name), shot.EncodeToPNG()); Destroy(shot); }
        private void Finish(Exception error)
        {
            if (finished) return; finished = true; if (error != null) report.error = error.ToString();
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "bandit-player-verification.json"), JsonUtility.ToJson(report, true)); Application.Quit(error == null ? 0 : 1);
        }
    }
}
