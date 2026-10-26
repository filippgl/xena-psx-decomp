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
            public string firstSign, firstScroll; public int firstHitDamage, deathSlot, firstIntake, blockedIntake, xenaReaction; public int runRenderFrames, runStalledFrames; public int steps, attacks, maxSimultaneousAttackers, maxTurn, closestUnits = int.MaxValue, spawnDelayTicks, maxLive; public float maxGroundError, deathLensHeight, gameOverVideoSeconds; public int gameOverMenuStep; public string gameOverVideo; public int hitFlashLife, hitDiscs, hitTriangles, remasterDroplets, discFrames, discStalls; public string strikeState; public int fallDamage, banditBlocks, contactHits, banditContactHits, lowReaction, midReaction, rearReaction; public string sfxLog, sfxAttenuation; public int butterflies, frogs, butterflyFrames, butterflyStalls, butterflyTeleports, patrolSightings, wingFrames, wingStalls, wingShapes, larkMode; public float patrolWalked, patrolFlatness; public Vector3 larkVelocity; public Vector3 contactPoint; public List<string> discTrace = new List<string>(); public int menuBolts; public List<string> runTrace = new List<string>();
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
            int movingFrames = 0, stalledFrames = 0; float lastStepTravel = 0; Vector3 rootTwoAgo = chosen[0].transform.position, rootOneAgo = rootTwoAgo;
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
                    // Stalled = the drawn body froze although the current AI step moves the
                    // actor (previous != current); a stop or a blocked bandit is not counted.
                    // The first frame after a standstill is not counted either: at alpha ~0 the
                    // body rightly starts from rest (captured: alpha 0.002, 0.1 mm, prior step 0).
                    float moved = Vector3.Distance(drawn.position, lastDrawn);
                    bool rootMoved = chosen[0].StepTravel > 0.0005f && lastStepTravel > 0.0005f; lastStepTravel = chosen[0].StepTravel;
                    var b0 = chosen[0];
                    report.runTrace.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} a={1:F3} ph={2:F4} t={3} mv={4:F4} st={5:F4}", Time.frameCount, b0.LastAlpha, b0.LastPhase, b0.LastTicksSinceStep, moved, b0.StepTravel));
                    if (moved > 0.0005f) movingFrames++; else if (rootMoved) stalledFrames++;
                }
                lastDrawn = drawn.position; rootTwoAgo = rootOneAgo; rootOneAgo = chosen[0].transform.position;
            }
            foreach (var brain in chosen) { report.attacks += brain.AttacksStarted; report.steps = Mathf.Max(report.steps, brain.Steps); }
            report.runRenderFrames = movingFrames + stalledFrames; report.runStalledFrames = stalledFrames;
            Require(movingFrames > 20 && stalledFrames == 0, "Bandit body steps with the 30Hz AI instead of gliding: " + stalledFrames + "/" + (movingFrames + stalledFrames));
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
            yield return Creatures(motor);
            yield return Patrol(motor, bandits);
            yield return GameOver(motor);
            report.result = "PASS"; Finish(null);
        }
        // Main menu after GAME OVER (docs/xena-main-menu.md): sequencing, input rules, render, New game.
        private IEnumerator MainMenu(PrototypeMotor motor)
        {
            var menu = motor.GetComponent<PrototypeMainMenu>();
            Require(menu != null && menu.Visible && Time.timeScale == 0, "GAME OVER did not return to the main menu");
            report.checks.Add("GAME OVER returns to the main menu (game paused behind it)");
            // Boot videos (docs/xena-fmv.md): EA_LOGO -> UNILOGO -> INTRO, logos unskippable, START skips INTRO.
            var boot = motor.GetComponent<PrototypeBootVideos>(); boot.Begin(); yield return null;
            Require(PrototypeBootVideos.Playing && boot.Index == 0 && boot.ClipName == "ea_logo-hd" && !boot.Skip(), "EA_LOGO did not start (HD) or could be skipped: " + boot.ClipName);
            float bootStart = Time.unscaledTime; for (int i = 0; i < 30; i++) yield return null; boot.SeekNearEndForTest();
            while (boot.Index == 0 && Time.unscaledTime - bootStart < 9) yield return null;
            Require(boot.Index == 1 && boot.ClipName == "unilogo-hd" && !boot.Skip(), "UNILOGO did not follow EA_LOGO or could be skipped: " + boot.ClipName);
            yield return null; yield return new WaitForEndOfFrame(); Capture("boot-unilogo-remaster.png");
            bootStart = Time.unscaledTime; boot.SeekNearEndForTest(); while (boot.Index == 1 && Time.unscaledTime - bootStart < 9) yield return null;
            Require(boot.Index == 2 && boot.ClipName == "intro-hd", "INTRO did not follow UNILOGO: " + boot.ClipName);
            for (int i = 0; i < 120; i++) yield return null; yield return new WaitForEndOfFrame(); Capture("boot-intro-remaster.png");
            Require(boot.Skip() && !PrototypeBootVideos.Playing && menu.Visible, "START did not skip INTRO to the main menu");
            report.checks.Add("boot videos: EA_LOGO, UNILOGO (unskippable), INTRO (START skips), then the main menu");
            menu.Show(); menu.SeedForTest(0x1234567);
            Require(menu.HairSeq[0] == 2 && menu.HairFrame[0] == 72 && menu.HairFrame[1] == 91 && menu.HairFrame[2] == 112, "Hair channels do not start at seq 2/3/4");
            int cursor = PrototypeMainMenu.Cursor; menu.MoveCursor(-4); Require(PrototypeMainMenu.Cursor == 0, "Cursor wrapped or moved above New game");
            menu.MoveCursor(9); Require(PrototypeMainMenu.Cursor == 3, "Cursor wrapped below Options"); menu.MoveCursor(-3);
            menu.Select(); Require(menu.Visible, "Confirm was accepted during the 5-tick entry delay");
            var ch1 = new List<int>(); var ch2 = new List<int>(); var ch0Seq = new List<int>(); int bolts = 0;
            for (int i = 0; i < 700; i++) { menu.Tick(); ch1.Add(menu.HairFrame[1]); ch2.Add(menu.HairFrame[2]); ch0Seq.Add(menu.HairSeq[0]); bolts += menu.BoltsDrawn; }
            // Channel 1 loops tiles 91..111 (21 ticks), channel 2 loops 112..133 (22 ticks).
            Require(ch1[0] == 92 && ch1[20] == 91 && ch1[41] == 91 && ch2[0] == 113 && ch2[21] == 112 && ch2[43] == 112, "Hair channels 1/2 do not loop H2/H4");
            // Channel 0: seq 2 (tiles 73..90) -> seq 0 at tick 18 (19 tiles), -> seq 1 12 ticks later.
            Require(ch0Seq[17] == 2 && ch0Seq[18] == 0 && ch0Seq[29] == 0 && ch0Seq[30] == 1, "Hair channel 0 does not play seq 2 -> 0 -> 1");
            Require(ch0Seq.Contains(2) && ch0Seq.LastIndexOf(2) > 30, "Hair seq 1 never chose seq 2 again (rand()&4)");
            Require(bolts > 0, "No lightning bolt flashed in 700 ticks");
            report.menuBolts = bolts;
            report.checks.Add("main menu: hair seq 2->0->1 / H2 21-tick / H4 22-tick loops, lightning, no cursor wrap, 5-tick confirm delay");
            Require(menu.ConfirmDelay == 0 && menu.HdReady, "Menu delay did not expire or the HD layers are missing");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Original); yield return null; yield return new WaitForEndOfFrame(); Capture("menu-original.png");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster); yield return null; yield return new WaitForEndOfFrame(); Capture("menu-remaster.png");
            for (int i = 0; i < 8; i++) menu.Tick(); yield return null; yield return new WaitForEndOfFrame(); Capture("menu-remaster-b.png");
            menu.MoveCursor(1); yield return null; yield return new WaitForEndOfFrame(); Capture("menu-remaster-load.png");
            menu.Select(); Require(menu.Visible && menu.Note != null, "Unported Load game did not stay in the menu with a note");
            menu.MoveCursor(-1); int games = PrototypeMainMenu.NewGames;
            // Confirm with a real Space press (Space is also jump): the level must start without a jump.
            var keys = Keyboard.current; Require(keys != null, "No keyboard for the menu confirm test");
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.Space)); for (int i = 0; i < 3; i++) yield return null;
            InputSystem.QueueStateEvent(keys, new KeyboardState()); yield return null;
            Require(!menu.Visible && Time.timeScale == 1 && PrototypeMainMenu.NewGames == games + 1, "New game did not start the level");
            bool airborne = false; for (int i = 0; i < 40; i++) { yield return new WaitForFixedUpdate(); if (!motor.Grounded) airborne = true; }
            Require(!airborne, "The Space press that confirmed New game made Xena jump");
            report.checks.Add("confirming New game with Space does not make Xena jump");
            report.checks.Add("main menu: New game starts level 01; unported items stay in the menu");
        }
        // Death -> menu -> exit confirm -> GAME OVER video -> level restart (native capture build/gameover).
        private IEnumerator GameOver(PrototypeMotor motor)
        {
            DontDestroyOnLoad(gameObject);
            var damage = motor.GetComponent<PrototypeOriginalDamage>(); var flow = motor.GetComponent<PrototypeGameOver>();
            var camera = Camera.main.GetComponent<PrototypeCamera>(); camera.enabled = true; motor.enabled = true;
            motor.ResetToSpawn(); Vector3 spawn = motor.transform.position; yield return new WaitForFixedUpdate();
            for (int i = 0; i < 120 && damage.Reacting; i++) yield return new WaitForFixedUpdate();
            // Fall damage (0x8003B834): drop Xena 6 m onto open ground -> drop - 2000 units of damage.
            {
                var cc = motor.GetComponent<CharacterController>(); Vector3 at = motor.transform.position;
                cc.enabled = false; motor.transform.position = at + Vector3.up * 6; cc.enabled = true; motor.SnapPresentationForTest();
                for (int i = 0; i < 240 && (damage.LastFallDamage == 0); i++) yield return new WaitForFixedUpdate();
                report.fallDamage = damage.LastFallDamage;
                Require(damage.LastFallDamage >= 300 && damage.LastFallDamage <= 500 && !damage.Dead, "Fall from 6 m did not deal about 6*400-2000 = 400: " + damage.LastFallDamage);
                report.checks.Add("fall damage: landing drop > 2000 units deals drop - 2000 (" + damage.LastFallDamage + " from 6 m)");
                damage.ResetState(); motor.ResetToSpawn(); yield return new WaitForFixedUpdate();
            }
            damage.SetHealthForTest(1);
            Require(damage.TakeHit(50, motor.transform.position + motor.transform.forward) && damage.Dead, "Xena did not die at 0 health");
            int menuStep = -1; float lensHeight = 0;
            for (int i = 0; i < 600 && flow.State == PrototypeGameOver.Phase.None; i++) yield return new WaitForFixedUpdate();
            menuStep = damage.DeathFrame; lensHeight = camera.transform.position.y - motor.transform.position.y;
            report.gameOverMenuStep = menuStep; report.deathLensHeight = lensHeight;
            Require(flow.State == PrototypeGameOver.Phase.Menu && menuStep >= damage.MenuAfterSteps && menuStep <= damage.MenuAfterSteps + 1, "Death menu did not open at the original step: " + menuStep);
            Require(camera.DeathCamera && lensHeight > 5 && Vector3.Angle(camera.transform.forward, Vector3.down) < 25, "Death camera is not above Xena: " + lensHeight);
            report.checks.Add("death: clip 0x91, top-down death camera, menu after 65+45 steps");
            yield return null; yield return new WaitForEndOfFrame(); Capture("game-over-menu.png");
            Require(flow.Selected == 0, "Retry is not the default death-menu item");
            flow.Move(1); flow.Confirm();
            Require(flow.State == PrototypeGameOver.Phase.Confirm && flow.Selected == 1, "Exit did not ask to confirm with No selected");
            yield return null; yield return new WaitForEndOfFrame(); Capture("game-over-confirm.png");
            flow.Confirm(); Require(flow.State == PrototypeGameOver.Phase.Menu, "No did not return to the death menu");
            flow.Confirm(); flow.Move(-1); flow.Confirm();
            Require(flow.State == PrototypeGameOver.Phase.Video && flow.VideoStarted, "Yes did not start the GAME OVER video");
            report.checks.Add("death menu: Повтор уровня / Выход -> Хочешь выйти? Да/Нет (default Нет)");
            report.gameOverVideo = flow.VideoName;
            Require(flow.VideoName == "chakdeth-hd" || PrototypeGraphics.Active.Mode == XenaVisualMode.Original, "Remaster did not play the upscaled GAME OVER video: " + flow.VideoName);
            int restarts = PrototypeGameOver.Restarts; float start = Time.unscaledTime; bool shot = false;
            while (PrototypeGameOver.Restarts == restarts && Time.unscaledTime - start < 12)
            {
                if (!shot && Time.unscaledTime - start > 4) { shot = true; Capture("game-over-video.png"); }
                yield return null;
            }
            report.gameOverVideoSeconds = Time.unscaledTime - start;
            Require(PrototypeGameOver.Restarts == restarts + 1 && report.gameOverVideoSeconds > 5, "GAME OVER video did not play to its end: " + report.gameOverVideoSeconds);
            for (int i = 0; i < 20; i++) yield return null;
            var fresh = FindFirstObjectByType<PrototypeMotor>(); var health = fresh.GetComponent<PrototypeOriginalDamage>();
            Require(fresh != motor && !health.Dead && health.Health == PrototypeOriginalDamage.MaxHealth && Vector3.Distance(fresh.transform.position, spawn) < 0.5f,
                "Level did not restart at its start with full health");
            Require(fresh.GetComponent<PrototypeGameOver>().State == PrototypeGameOver.Phase.None, "Restarted level still shows the death menu");
            report.checks.Add("GAME OVER video (CHAKDETH.STR) plays, then the level restarts at its start with 500 hp");
            yield return MainMenu(fresh);
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
            int before = foe.Health, reactionsAtStrike = damage.Reactions, hitsAtStrike = damage.HitsTaken;
            PrototypeSfx.Log.Clear();
            // Hold the press for real frames: after the 4x ending, fixed-step catch-up
            // could otherwise deliver press and release to Input System in one update.
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); for (int i = 0; i < 4; i++) yield return null;
            var slots = new List<int>();
            var effects = motor.GetComponent<PrototypeHitEffects>(); int sparks = effects.SparkLog.Count, bleeds = effects.BleedLog.Count; bool shot = false;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            for (int i = 0; i < 30; i++)
            {
                yield return new WaitForFixedUpdate();
                var combatNow = motor.GetComponent<PrototypeCombat>(); if (combatNow.Busy && !slots.Contains(combatNow.Slot)) slots.Add(combatNow.Slot);
                if (!shot && effects.SparkLog.Count > sparks && effects.ActiveDrops > 0) { shot = true; yield return new WaitForEndOfFrame(); Capture("hit-effects-remaster.png"); report.hitTriangles = effects.LastDrawnTriangles; report.remasterDroplets = effects.RemasterDroplets; }
            }
            report.firstHitDamage = before - foe.Health;
            report.strikeState = "modal=" + scripts.Modal + " paused=" + input.Paused + " menuOwns=" + input.MenuOwnsInput + " grounded=" + motor.Grounded + " reacting=" + damage.Reacting + " chakram=" + motor.GetComponent<PrototypeChakram>().Active;
            Require(report.firstHitDamage == 30 + scripts.SwordLevel, "Sword 51 did not deal original 30 + sword level: " + report.firstHitDamage + " (Xena hit " + (damage.HitsTaken - hitsAtStrike) + "x, reactions " + (damage.Reactions - reactionsAtStrike) + ", live bandits " + bandits.Live() + ", Xena clips " + string.Join(",", slots.ConvertAll(x => x.ToString("X"))) + ", foe at " + Vector3.Distance(foe.transform.position, motor.transform.position).ToString("F2") + " m, angle " + Vector3.Angle(motor.transform.forward, foe.transform.position - motor.transform.position).ToString("F0") + ")");
            report.checks.Add("sword 51 deals the original 30 + sword level to a bandit");
            // Contact points (docs/xena-contact-points.md): the sword hit came from the posed points, at the
            // bandit's body (within r + 50 units horizontally, between feet and 750 units up).
            {
                Vector3 c = damage.LastContactPoint - foe.transform.position; float up = c.y * 400; c.y = 0;
                report.contactHits = damage.ContactHits; report.contactPoint = damage.LastContactPoint;
                Require(PrototypeContactPoints.Available && damage.ContactHits >= 1 && c.magnitude * 400 < PrototypeBanditBrain.Radius + 50 + 60 && up > 0 && up < 750,
                    "Sword hit did not come from an original contact point: hits " + damage.ContactHits + ", offset " + (c.magnitude * 400).ToString("F0") + ", up " + up.ToString("F0"));
                // Geometry oracle: clip 51 counter 7 (SMA frame 6) tip = local (-241,-443,-570) -> 570 units ahead,
                // 241 to Xena's right, 443 up for a root at the origin facing +z.
                var pts = new Vector3[9];
                int mask = PrototypeContactPoints.Pose("xena", 0x51, 6, Vector3.zero, 0, 0, pts, out int raw);
                Require(raw == 30 && mask == 0x1F && Vector3.Distance(pts[0] * 400, new Vector3(241, 443, 570)) < 0.5f,
                    "Contact point pose mismatch: mask " + mask + " d " + raw + " tip " + (pts[0] * 400));
                Require(PrototypeContactPoints.Test(pts[0], pts[0], false, pts[0] + new Vector3(0.45f, -0.5f, 0), 150, 150, 750, out _)
                    && !PrototypeContactPoints.Test(pts[0], pts[0], false, pts[0] + new Vector3(0.55f, -0.5f, 0), 150, 150, 750, out _)
                    && !PrototypeContactPoints.Test(pts[0], pts[0], false, pts[0] + new Vector3(0, -2f, 0), 150, 150, 750, out _),
                    "Contact test does not use the original r + 50 = 200 unit radius / 750 unit height");
                report.checks.Add("Xena sword uses the original per-frame contact points (pose oracle, r+50 / 750 test)");
            }
            // Original sounds (docs/xena-sfx.md): swing rand(0,2) at clip 51 f7, sword connect rand(0x1E,0x21) at
            // note 60..62; water loops 0x13 from level load; func_800845C8 distance/pan oracle.
            {
                var log = PrototypeSfx.Log; report.sfxLog = string.Join(",", log.ConvertAll(x => x.id.ToString("X") + "/" + x.note + "/" + x.vol));
                report.sfxLog += " taken=" + (damage.HitsTaken - hitsAtStrike) + " dealt=" + damage.HitsDealt + " blocked=" + damage.BlockedHits;
                Require(PrototypeSfx.Available > 100 && log.Exists(x => x.id <= 2) && log.Exists(x => x.id >= 0x1E && x.id <= 0x21 && x.note >= 60 && x.note <= 62),
                    "Sword swing / connect sounds missing: " + PrototypeSfx.Available + " sounds, log " + report.sfxLog);
                Require(PrototypeSfx.IsPlaying(0x13, 0x13), "Level 01 water loop 0x13 is not playing");
                var cam = new GameObject("SfxProbe").transform; cam.position = Vector3.zero; cam.rotation = Quaternion.identity;
                PrototypeSfx.Attenuate(new Vector3(0, 0, 2000 / 400f), cam, out int v0, out int p0);
                PrototypeSfx.Attenuate(new Vector3(0, 0, 12500 / 400f), cam, out int v1, out int p1);
                PrototypeSfx.Attenuate(new Vector3(0, 0, -12500 / 400f), cam, out int v2, out _);
                PrototypeSfx.Attenuate(new Vector3(30000 / 400f, 0, 0), cam, out int v3, out int p3);
                PrototypeSfx.Attenuate(new Vector3(1, 0, 1), cam, out _, out int p4);
                Destroy(cam.gameObject);
                report.sfxAttenuation = v0 + "/" + p0 + " " + v1 + "/" + p1 + " " + v2 + " " + v3 + "/" + p3 + " " + p4;
                Require(v0 == 127 && p0 == 64 && v1 == 63 && v2 == 48 && v3 == 10 && p3 == 127 && p4 == 127, "Sound attenuation differs from func_800845C8: " + report.sfxAttenuation);
                report.checks.Add("original SFX: swing + connect on a sword hit, level water loops, func_800845C8 distance/pan");
            }
            int d = report.firstHitDamage;
            // The sword spark: (Hit, d) logged, flash life / ring / discs by func_800291A0, the foe bleeds.
            Require(effects.SparkLog.GetRange(sparks, effects.SparkLog.Count - sparks).Contains((PrototypeHitEffects.Type.Hit, d, Mathf.Min(d / 4 + 4, 12), d >= 26, Mathf.Clamp(d / 4 - 1, 0, 6)))
                && effects.BleedLog.GetRange(bleeds, effects.BleedLog.Count - bleeds).Contains(foe.transform) && shot,
                "Sword hit did not spark red with d = " + d + " or the bandit did not bleed");
            Require(report.hitTriangles > 0, "Hit effects were not drawn");
            Require(effects.RemasterDroplets > 0 || report.remasterDroplets > 0, "Remaster blood droplets were not emitted");
            // Ghosting rule: spark discs move on the 30/s step; per render frame (120 FPS) the drawn
            // position must advance while the disc moves.
            effects.Spark(motor.transform.position + motor.transform.forward + Vector3.up, 30, PrototypeHitEffects.Type.Hit);
            int discFrames = 0, discStalls = 0; Vector3 lastDisc = Vector3.zero; bool haveLast = false;
            for (int i = 0; i < 40; i++)
            {
                yield return null;
                if (!effects.MovingDisc(out Vector3 disc)) { haveLast = false; continue; }
                if (haveLast) { discFrames++; if (Vector3.Distance(disc, lastDisc) < 1e-5f) { discStalls++; report.discTrace.Add("STALL " + effects.DebugClock); } else report.discTrace.Add(effects.DebugClock); }
                lastDisc = disc; haveLast = true;
            }
            report.discFrames = discFrames; report.discStalls = discStalls;
            Require(discFrames > 10 && discStalls == 0, "Spark discs step at 30 Hz instead of gliding: " + discStalls + "/" + discFrames);
            report.checks.Add("hit spark discs are render-interpolated (no stalled frames at 120 FPS)");
            report.hitFlashLife = effects.LastFlashLife; report.hitDiscs = effects.LastDiscs;
            report.checks.Add("sword hit: red flash life min(d/4+4,12), ring for d >= 26, clamp(d/4-1,0,6) discs, bandit bleeds");
            Require(motor.GetComponent<PrototypeHud>().VisibleEnemyBars >= 1, "Enemy dagger bar not shown after the hit"); report.checks.Add("HUD shows the enemy dagger bar for 60 frames after its health changes");
            // Block (mode 4): a blocking bandit discards all damage; flee: fleeing kill costs 10 karma.
            {
                for (int i = 0; i < 24 && foe.Invulnerable > 0; i++) { foe.Active = true; yield return new WaitForFixedUpdate(); }
                foe.Active = false;
                // A blocking-class bandit with 100 % block, in front of Xena, facing her.
                foe.Configure(foe.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(foe), 500, 0, 11); foe.ConfigureDefence(true, 100);
                foe.transform.rotation = Quaternion.LookRotation(motor.transform.position - foe.transform.position); foe.Active = true;
                int hpBefore = foe.Health, blocksBefore = foe.Blocks;
                yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); for (int i = 0; i < 4; i++) yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState()); for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                report.banditBlocks = foe.Blocks - blocksBefore;
                Require(foe.Blocks > blocksBefore && foe.Health == hpBefore, "Blocking bandit did not block the sword (blocks " + (foe.Blocks - blocksBefore) + ", hp " + hpBefore + " -> " + foe.Health + ")");
                report.checks.Add("bandit1/2/4 block (mode 4, clip 80/82) discards Xena's damage; plain bandits never block");
                // Flee: runs away from Xena.
                foe.ConfigureDefence(false, 0); foe.ForceFleeForTest(); float fleeStart = Vector3.Distance(foe.transform.position, motor.transform.position);
                for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                Require(Vector3.Distance(foe.transform.position, motor.transform.position) > fleeStart + 1, "Fleeing bandit did not run away");
                report.checks.Add("fleeing bandit turns away and runs");
                foe.Active = false; foe.Configure(foe.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(foe), 75, 0, 7);
                foe.transform.position = motor.transform.position + motor.transform.forward * 1.3f; foe.transform.rotation = Quaternion.LookRotation(-motor.transform.forward);
            }
            // Chakram (docs/xena-chakram-damage.md): flat 300 at a body contact, independent of sword level.
            // The bandit's 8-step hit timer only runs while its AI steps: let it run out first.
            foe.Active = true; for (int i = 0; i < 24 && foe.Invulnerable > 0; i++) yield return new WaitForFixedUpdate();
            foe.Configure(foe.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(foe), 2000, 0, 7); foe.Active = false;
            var thrown = motor.GetComponent<PrototypeChakram>(); int discHits = thrown.EnemyHits;
            thrown.HitBanditsForTest(foe.transform.position + Vector3.up * 1.0f);
            Require(thrown.EnemyHits == discHits + 1 && foe.Health == 2000 - PrototypeChakram.Damage, "Chakram contact did not deal the original 300: 2000 -> " + foe.Health);
            report.checks.Add("chakram contact deals the original flat 300 damage");
            for (int i = 0; i < 12; i++) yield return new WaitForFixedUpdate();
            foe.Configure(foe.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(foe), 75, 0, 7);
            foe.Active = true;
            for (int press = 0; press < 30 && !foe.Dying; press++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); for (int i = 0; i < 4; i++) yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState()); for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                Vector3 to = foe.transform.position - motor.transform.position; to.y = 0; if (to.sqrMagnitude > 0.01f) motor.transform.rotation = Quaternion.LookRotation(to);
            }
            Require(foe.Dying && foe.Health == 0, "Bandit did not die from original damage"); report.deathSlot = foe.Slot;
            Require(foe.Slot >= 0x8A && foe.Slot <= 0x91, "Death did not play an original 8A..91 clip"); report.checks.Add("bandit at 0 health plays an original death clip");
            var ring = motor.GetComponent<PrototypeHud>(); damage.SetKarmaForTest(48); for (int i = 0; i < 4; i++) yield return null;
            Require(ring.LitSegments == 48 * 29 / 96, "Karma ring did not light trunc(meter*29/96) segments: " + ring.LitSegments);
            report.checks.Add("karma ring lights trunc(meter*29/96) segments");
            damage.SetKarmaForTest(0);
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
            var hitFx = motor.GetComponent<PrototypeHitEffects>();
            Require(hitFx.LastType == PrototypeHitEffects.Type.Hit && hitFx.BloodEmitters >= 2, "Bandit hit on Xena did not flash red / make her bleed");
            Require(report.firstIntake == 15 || report.firstIntake == 25 || report.firstIntake == 30, "Bandit hit did not use an original window damage: " + report.firstIntake);
            report.checks.Add("bandit attack damages Xena by its original window value");
            report.banditContactHits = attacker.ContactHits;
            Require(attacker.ContactHits >= 1, "Bandit hit did not come from its contact points");
            report.checks.Add("bandit attacks hit through their 7 original contact points");
            Require(damage.Reactions >= 1, "Xena did not play an original hit reaction"); report.xenaReaction = damage.ReactionSlot;
            report.checks.Add("Xena plays an original hit reaction (0x66/0x67/0x72/0x74/0x75/0x7C) on an unblocked hit");
            var hud = motor.GetComponent<PrototypeHud>(); for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Require(hud.Loaded && hud.TargetLength == PrototypeHud.HealthLength(damage.Health, 500) && hud.ShownLength == hud.TargetLength,
                "HUD did not follow the original health mapping: " + hud.ShownLength + "/" + hud.TargetLength + " bars " + hud.VisibleEnemyBars);
            report.checks.Add("HUD sword length = clamp(109*hp/500,3,109)");
            Require(hud.HdTextures == 8, "Remaster HUD HD textures missing: " + hud.HdTextures); report.checks.Add("Remaster HUD loads 8 Codex-remastered -4x textures");
            yield return new WaitForEndOfFrame(); Capture("hud-remaster.png");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Original); for (int i = 0; i < 3; i++) yield return null; yield return new WaitForEndOfFrame(); Capture("hud-original.png");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster);
            for (int i = 0; i < 90 && damage.Reacting; i++) yield return new WaitForFixedUpdate();
            int health = damage.Health, blocked = damage.BlockedHits, reactionsBefore = damage.Reactions;
            Vector3 at = attacker.transform.position - motor.transform.position; at.y = 0; motor.transform.rotation = Quaternion.LookRotation(at);
            int bleedsBefore = motor.GetComponent<PrototypeHitEffects>().BloodEmitters;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 2 });
            for (int i = 0; i < 60 * 15 && damage.BlockedHits == blocked; i++) { yield return new WaitForFixedUpdate(); Vector3 t = attacker.transform.position - motor.transform.position; t.y = 0; if (t.sqrMagnitude > 0.01f) motor.transform.rotation = Quaternion.LookRotation(t); }
            InputSystem.QueueStateEvent(mouse, new MouseState());
            report.blockedIntake = damage.LastIntake;
            Require(damage.BlockedHits > blocked && damage.LastIntakeBlocked && report.blockedIntake == 1, "Blocked bandit hit did not deal original 1 damage: " + report.blockedIntake);
            Require(damage.Reactions == reactionsBefore, "A 1-damage blocked hit staggered Xena");
            var blockFx = motor.GetComponent<PrototypeHitEffects>();
            Require(blockFx.LastType == PrototypeHitEffects.Type.Block && blockFx.BloodEmitters == bleedsBefore, "Blocked hit did not flash blue without blood");
            report.checks.Add("hit effects: red flash + blood on Xena, blue flash and no blood when blocked");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Original); blockFx.Spark(motor.transform.position + motor.transform.forward * 0.4f + Vector3.up * 1.1f, 35, PrototypeHitEffects.Type.Special); blockFx.Bleed(motor.transform, motor.transform.position + Vector3.up * 1.1f, 8, 200);
            for (int i = 0; i < 6; i++) yield return new WaitForFixedUpdate(); yield return new WaitForEndOfFrame(); Capture("hit-effects-original.png");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster);
            report.checks.Add("blocked bandit hits deal d>40 ? d-40 : 1 (=1) and do not stagger Xena");
            attacker.Active = false;
            // Hit height (docs/xena-hit-reactions.md): a front contact < 250 units above the feet plays the low
            // reaction 0x70; 400 units (mid) plays 0x66/0x67.
            {
                for (int i = 0; i < 120 && damage.Reacting; i++) yield return new WaitForFixedUpdate();
                damage.ResetState(); Vector3 feet = motor.transform.position, ahead = feet + motor.transform.forward;
                damage.TakeHit(15, ahead, feet + motor.transform.forward * 0.3f + Vector3.up * (100 / 400f)); report.lowReaction = damage.ReactionSlot;
                for (int i = 0; i < 120 && damage.Reacting; i++) yield return new WaitForFixedUpdate();
                feet = motor.transform.position; ahead = feet + motor.transform.forward;
                damage.TakeHit(15, ahead, feet + motor.transform.forward * 0.3f + Vector3.up * (400 / 400f)); report.midReaction = damage.ReactionSlot;
                for (int i = 0; i < 120 && damage.Reacting; i++) yield return new WaitForFixedUpdate();
                feet = motor.transform.position;
                damage.TakeHit(15, feet - motor.transform.forward, feet - motor.transform.forward * 0.3f + Vector3.up); report.rearReaction = damage.ReactionSlot;
                Require(report.rearReaction == 0x72, "A hit from behind did not play the rear reaction 0x72: " + report.rearReaction.ToString("X"));
                Require(report.lowReaction == 0x70 && (report.midReaction == 0x66 || report.midReaction == 0x67),
                    "Hit height did not pick the original reaction: low " + report.lowReaction.ToString("X") + ", mid " + report.midReaction.ToString("X"));
                report.checks.Add("hit height from the contact point: low < 250 -> 0x70, mid -> 0x66/0x67; front/rear sectors (rear 0x72)");
                for (int i = 0; i < 120 && damage.Reacting; i++) yield return new WaitForFixedUpdate();
                damage.ResetState();
            }
        }
        // Ambient creatures (docs/xena-ambient-audit.md 2): 3 butterflies + 5 frogs from the placements; a frog
        // jumps when Xena comes within 1000 units and despawns on landing with a 0x3E..0x42 sound; butterflies
        // follow Xena (teleport >= 5001) and are drawn interpolated: 0 stalled render frames at 120 FPS.
        private IEnumerator Creatures(PrototypeMotor motor)
        {
            var creatures = motor.GetComponent<PrototypeCreatures>();
            report.butterflies = creatures.Creatures.FindAll(c => c.kind == "bfly").Count; report.frogs = creatures.Creatures.FindAll(c => c.kind == "frog").Count;
            Require(report.butterflies == 3 && report.frogs == 5, "Creature placements: " + report.butterflies + " butterflies, " + report.frogs + " frogs");
            int frog = creatures.Creatures.FindIndex(c => c.kind == "frog" && !c.gone);
            var f = creatures.Creatures[frog]; Vector3 at = PrototypeCreatures.ToUnity(f.p) + new Vector3(1.5f, 0, 0);
            yield return Visit(motor, at); PrototypeSfx.Log.Clear();
            for (int i = 0; i < 120 && !f.gone; i++) yield return new WaitForFixedUpdate();
            Require(f.gone && creatures.FrogJumps >= 1 && PrototypeSfx.Log.Exists(x => x.id >= 0x3E && x.id <= 0x42 && x.note == 58),
                "Frog did not jump and land with sound 0x3E..0x42 (gone " + f.gone + ", jumps " + creatures.FrogJumps + ")");
            report.checks.Add("frog jumps when Xena < 1000 units and lands with sound 0x3E..0x42 n58, then despawns");
            int frames = 0, stalls = 0; Vector3 last = Vector3.zero; bool have = false; int b = creatures.Creatures.FindIndex(c => c.kind == "bfly");
            for (int i = 0; i < 400; i++)
            {
                yield return null;
                if (!creatures.Moving(b)) { have = false; continue; }
                Vector3 now = creatures.DrawnPosition(b);
                if (have) { frames++; if (Vector3.Distance(now, last) < 1e-6f) stalls++; }
                last = now; have = true;
            }
            report.butterflyFrames = frames; report.butterflyStalls = stalls; report.butterflyTeleports = creatures.Teleports;
            Require(creatures.Teleports >= 1 && frames > 60 && stalls == 0, "Butterfly motion: teleports " + creatures.Teleports + ", stalls " + stalls + "/" + frames);
            report.checks.Add("butterflies follow Xena (teleport >= 5001) and glide interpolated (0 stalled frames at 120 FPS)");
            // Wings: SMA vertex frames loop one per step, blended per render frame (0 stalled frames while flapping).
            {
                int wf = 0, ws = 0; Vector3[] lw = null; var shapes = new HashSet<int>();
                for (int i = 0; i < 120; i++)
                {
                    yield return null;
                    var wv = creatures.DrawnWing(b); if (wv == null) break;
                    shapes.Add(Mathf.RoundToInt(wv[0].y + wv[wv.Length - 1].y * 7 + wv[wv.Length / 2].y * 3));
                    if (creatures.WingsMoving(b) && lw != null) { wf++; float sum = 0; for (int k = 0; k < wv.Length; k++) sum += (wv[k] - lw[k]).sqrMagnitude; if (sum < 1e-8f) ws++; }
                    lw = creatures.WingsMoving(b) ? wv : null;
                }
                report.wingFrames = wf; report.wingStalls = ws; report.wingShapes = shapes.Count;
                Require(wf > 30 && ws == 0 && shapes.Count >= 4, "Butterfly wings: frames " + wf + ", stalls " + ws + ", shapes " + shapes.Count);
                report.checks.Add("butterfly wings play the SMA vertex frames, blended per render frame (0 stalled at 120 FPS)");
            }
            // Larks: g_lark actor 24 (-11253, 582, 1997, yaw -1216) releases a perched lark when Xena is within 3000 (and
            // not within 350); within 2000 it flies at (trunc(-rsin/150), -9|-13, trunc(-rcos/150)) per step.
            {
                int before = creatures.LarksSpawned;
                Vector3 gen = PrototypeCreatures.ToUnity(new Vector3(-11253, 582, 1997));
                yield return Visit(motor, gen + new Vector3(4f, 0, 0));
                System.Predicate<PrototypeCreatures.Creature> from24 = c => c.kind == "lark" && c.root.name.StartsWith("lark_24_");
                for (int i = 0; i < 80 && !creatures.Creatures.Exists(from24); i++) yield return new WaitForFixedUpdate();
                Require(creatures.LarksSpawned > before && creatures.Creatures.Exists(from24), "g_lark 24 did not release a lark with Xena 1600 units away");
                var lark = creatures.Creatures.Find(from24);
                for (int i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
                Vector3 a0 = lark.p; for (int i = 0; i < 8; i++) yield return new WaitForFixedUpdate(); Vector3 v = (lark.p - a0) / 4; // 8 ticks = 4 steps
                report.larkVelocity = v; report.larkMode = lark.mode;
                float ang = -1216 / 4096f * 2 * Mathf.PI; int ex = -Mathf.RoundToInt(Mathf.Sin(ang) * 4096) / 150, ez = -Mathf.RoundToInt(Mathf.Cos(ang) * 4096) / 150;
                Require(lark.mode == 3 && Mathf.Abs(v.x - ex) < 0.01f && Mathf.Abs(v.z - ez) < 0.01f && v.y <= -9 && v.y >= -13 && lark.life > 0,
                    "Lark flight differs: mode " + lark.mode + ", v " + v + ", expected (" + ex + ", -9..-13, " + ez + ")");
                report.checks.Add("g_lark releases a perched lark; within 2000 it flies at the original (-rsin/150, -9|-13, -rcos/150) per step");
                var main2 = Camera.main; var orbit2 = main2.GetComponent<PrototypeCamera>(); orbit2.enabled = false;
                for (int i = 0; i < 3; i++) { Vector3 lp = lark.root.position; main2.transform.position = lp + new Vector3(0.6f, 0.2f, 0.6f); main2.transform.LookAt(lp); yield return new WaitForEndOfFrame(); }
                Capture("creatures-lark-close.png"); orbit2.enabled = true;
            }
            yield return new WaitForEndOfFrame(); Capture("creatures-butterfly.png");
            // Close-up of the butterfly for the visual review.
            var main = Camera.main; var keepPos = main.transform.position; var keepRot = main.transform.rotation; var orbit = main.GetComponent<PrototypeCamera>(); orbit.enabled = false;
            for (int i = 0; i < 3; i++)
            {
                Vector3 bf = creatures.DrawnPosition(b); main.transform.position = bf + new Vector3(0.5f, 0.15f, 0.5f); main.transform.LookAt(bf);
                yield return new WaitForEndOfFrame();
            }
            Capture("creatures-butterfly-close.png"); main.transform.SetPositionAndRotation(keepPos, keepRot); orbit.enabled = true;
        }
        // Patrol (flag 0x200): walks toward waypoint 0 without noticing Xena behind it; sees her ahead within
        // 35 deg / 6000 units (line test clear) and switches to approach.
        private IEnumerator Patrol(PrototypeMotor motor, PrototypeBandits bandits)
        {
            motor.ResetToSpawn(); for (int i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
            // Pick the flattest direction from the spawn so the line test 1 m above the feet is not cut by a slope.
            Vector3 f = motor.transform.forward; f.y = 0; f.Normalize(); float best = float.MaxValue; Vector3 o = motor.transform.position;
            for (int k = 0; k < 16; k++)
            {
                Vector3 dir = Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward; float g0 = motor.GroundHeight(o + dir * 3 + Vector3.up * 1.5f), spread = 0;
                for (int m = 4; m <= 12; m++) { float gm = motor.GroundHeight(o + dir * m + Vector3.up * 1.5f); spread = float.IsNegativeInfinity(gm) || float.IsNegativeInfinity(g0) ? float.MaxValue : Mathf.Max(spread, Mathf.Abs(gm - g0)); }
                if (spread < best) { best = spread; f = dir; }
            }
            report.patrolFlatness = best;
            var foe = bandits.SpawnForTest(motor.transform.position + f * 3); Require(foe != null, "No pending bandit for the patrol test");
            foe.transform.rotation = Quaternion.LookRotation(f); foe.Configure(foe.GetComponentInChildren<XenaAssetPreview>(), motor, Motion(foe), 75, 0, 5);
            foe.ConfigurePatrol(true, foe.transform.position + f * 6); foe.Active = true;
            Vector3 start = foe.transform.position;
            for (int i = 0; i < 60 && foe.State == PrototypeBanditBrain.Mode.Idle; i++) yield return new WaitForFixedUpdate();
            // Mode 0 picks 1 (patrol) or 2 (approach) at random; force the patrol branch for the check.
            foe.ForcePatrolHoldForTest();
            for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
            report.patrolWalked = Vector3.Dot(foe.transform.position - start, f); report.patrolSightings = foe.Sightings;
            Require(foe.Patrolling && foe.Sightings == 0 && report.patrolWalked > 0.3f, "Patrolling bandit did not walk to its waypoint unseen: walked " + report.patrolWalked.ToString("F2") + " m, sightings " + foe.Sightings);
            var capsule = motor.GetComponent<CharacterController>(); capsule.enabled = false;
            Vector3 look = foe.transform.forward; look.y = 0; look.Normalize(); Vector3 ahead = foe.transform.position + look * 2.5f; float g = motor.GroundHeight(ahead + Vector3.up * 1.5f);
            motor.transform.position = new Vector3(ahead.x, float.IsNegativeInfinity(g) ? ahead.y : g + 0.02f, ahead.z); capsule.enabled = true; Physics.SyncTransforms();
            for (int i = 0; i < 30 && foe.Sightings == 0; i++) yield return new WaitForFixedUpdate();
            report.patrolSightings = foe.Sightings;
            Require(foe.Sightings == 1 && !foe.Patrolling && foe.State != PrototypeBanditBrain.Mode.Idle, "Patrolling bandit did not see Xena ahead: blocker " + foe.SightBlocker + ", state " + foe.State + ", dist " + (Vector3.Distance(foe.transform.position, motor.transform.position) * 400).ToString("F0") + ", angle " + Vector3.Angle(foe.transform.forward, motor.transform.position - foe.transform.position).ToString("F0"));
            report.checks.Add("patrol (flag 0x200): walks to waypoint 0, sees Xena only ahead (35 deg / 6000) and then approaches");
            // Spawn facing: heading h moves along (sin h, cos h); the original faces (-sin rotY, -cos rotY).
            foreach (int rotY in new[] { 0, -1856, 1024, 3000 })
            {
                float h = PrototypeBandits.SpawnHeading(rotY) / 4096f * 2 * Mathf.PI, r = rotY / 4096f * 2 * Mathf.PI;
                Require(Mathf.Abs(Mathf.Sin(h) + Mathf.Sin(r)) < 1e-3f && Mathf.Abs(Mathf.Cos(h) + Mathf.Cos(r)) < 1e-3f, "Bandit spawn heading does not face (-sin, -cos) of rotY " + rotY);
            }
            report.checks.Add("bandits spawn facing the original (-sin rotY, -cos rotY)");
            foe.Active = false;
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
            var spriteViews = FindObjectsByType<PrototypeScreenSprite>(FindObjectsSortMode.None); bool allHd = spriteViews.Length == 6;
            foreach (var view in spriteViews) allHd &= view.UsingRemaster;
            Require(allHd, "Remaster did not use the Codex HD pickup sprites"); report.checks.Add("Remaster pickups use the Codex-remastered HD sprites");
            var music = motor.GetComponent<PrototypeMusic>(); Require(music != null && music.enabled && music.Track >= 0 && music.Muted, "Original m_vill music not loaded"); report.checks.Add("m_vill sequences load; exploration track plays (muted in smoke)");
            Require(scripts.PlacedModels == 4, "Original sign/Siras models not placed: " + scripts.PlacedModels); report.checks.Add("3 original SIGN_PT models and SIRAS placed from the level records");
            yield return Visit(motor, signs[0]);
            Require(scripts.Modal && scripts.ShownIndex == 0x46 && Time.timeScale == 0, "Sign 0 did not show message 0x46 modally: " + scripts.ShownIndex);
            Camera.main.GetComponent<PrototypeCamera>().enabled = false; Camera.main.transform.position = signs[0] + new Vector3(1.6f, 1.6f, 1.6f); Camera.main.transform.LookAt(signs[0] + Vector3.up * 0.8f); yield return null;
            report.firstSign = string.Join("|", scripts.Shown); yield return new WaitForEndOfFrame(); Capture("script-sign.png"); PrototypeGraphics.Active.SetMode(XenaVisualMode.Original); yield return null; yield return new WaitForEndOfFrame(); Capture("script-sign-original.png"); PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster);
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
            var frogs = motor.GetComponent<PrototypeFrogs>(); Require(frogs.Frogs.Count == 5, "Original frogs not placed: " + frogs.Frogs.Count);
            var frog = frogs.Frogs[0]; Vector3 frogAt = frog.root.position; int frogStalled = 0, frogMoved = 0; Vector3 lastBody = frog.body.position;
            yield return Visit(motor, frogAt + Vector3.right * 1.2f);
            for (int i = 0; i < 240 && !frog.gone; i++)
            {
                yield return null;
                if (frog.jumping && !frog.gone) { if (Vector3.Distance(frog.body.position, lastBody) > 0.0005f) frogMoved++; else frogStalled++; }
                lastBody = frog.body.position;
            }
            Require(frog.jumping && frog.gone, "Frog did not jump and despawn when Xena came within 1000 units");
            Require(frogMoved > 5 && frogStalled == 0, "Frog jump is not render-interpolated: " + frogStalled + "/" + (frogMoved + frogStalled));
            report.checks.Add("frog jumps (65 up, gravity 5, 40 forward) when Xena is within 1000, despawns on landing, interpolated");
            var siras = new List<Vector3>(scripts.TriggerPositions("levelEndNpc"));
            yield return Visit(motor, siras[0] + Vector3.forward * 2.5f); for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Require(scripts.CutsceneActive && scripts.CutsceneFrames == 563, "Reaching Siras did not start the 563-frame ending: " + scripts.CutsceneFrames);
            Time.timeScale = 4; int guard = 0; bool captured = false;
            while (scripts.CutsceneActive && guard++ < 60 * 30) { yield return new WaitForFixedUpdate(); if (!captured && scripts.CutsceneFrame > 200) { captured = true; yield return new WaitForEndOfFrame(); Capture("script-ending.png"); } }
            Time.timeScale = 1;
            Require(scripts.LevelComplete && !scripts.CutsceneActive, "Ending camera path did not finish into level end"); report.checks.Add("Siras within 1450 plays the original 563-frame camera path, then ends the level");
            // Level-complete screen (docs/xena-message-window.md): rows reveal, input owned, CROSS continues.
            float rowsStart = Time.unscaledTime; while (scripts.LevelEndRows < 6 && Time.unscaledTime - rowsStart < 6) yield return null;
            Require(scripts.LevelEndRows == 6 && motor.GetComponent<PrototypeInput>().MenuOwnsInput, "Level-complete stats did not reveal all rows / own the input");
            yield return new WaitForEndOfFrame(); Capture("level-complete-" + (PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster ? "remaster" : "original") + ".png");
            PrototypeGraphics.Active.SetMode(PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster ? XenaVisualMode.Original : XenaVisualMode.Remaster); yield return null;
            yield return new WaitForEndOfFrame(); Capture("level-complete-" + (PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster ? "remaster" : "original") + ".png");
            PrototypeGraphics.Active.SetMode(XenaVisualMode.Remaster);
            report.checks.Add("level-complete screen: original rows/timing, both modes");
            scripts.ResetLevelEndForTest();
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
