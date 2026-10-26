# AI progress

Read at the start of every session; update after each completed task and before compaction.
One issue per task. Nothing here is a claim without the command/measurement that produced it.

## Current task — Unity combat and original collision audit (2026-10-24)

- CLAUDE 2026-10-26 08:03 build (DLL 08:03:48 local): weapon CONTACT POINTS ported.
  `tools/install_contact_points.py` flattens build/contact-points/contact_points.json (from
  tools/export_contact_points.mjs) into Resources/Combat/contact_points.json (both projects, ignored).
  `PrototypeContactPoints` poses the per-frame points (local (-x,-y,-z)/400 under the root yaw; frame
  counter n -> SMA frame n-1; root/yaw from the previous step) and runs func_80070DFC (coarse
  2(r_t+r_a), projection only when strictly cur<p<prev, fine r_t+50 / 0<up<750, max+3/8 min).
  Xena: sword points 0..4 / kick 5..6 replace the 1.65/1.25 m cone (cone kept only without data);
  sparks/blood at the hit point. Bandits: 7 points + part-3 twist (bearing/3 stepped 60, zero outside
  +-0x384; sign checked: +twist moves the weapon to the bandit's right = +bearing); Xena's spark at the
  bandit's contact point. Bandit smoke adds a pose oracle (51 counter 7 tip = (241,443,570) units), the
  r+50/750 test and contact-hit counters. All 8 suites + aggregate PASS: build/unity-import/claude-v143-*.txt.
- CLAUDE 2026-10-26 08:11 build (DLL 08:11:39): hit-height reactions from the bandit contact point
  (< 250 low -> 0x70 front/side A, 0x74 side B; > 500 high = mid). BUG FIXED: the reaction sector used
  atan2(Xena - attacker) with Unity's +z facing, so every front hit played the rear clip 0x72; now
  atan2(attacker - Xena) - yaw (PSX models face -z). Smoke: low 0x70, mid 0x66, rear 0x72, live intake 0x67.
  All 8 suites + aggregate PASS: build/unity-import/claude-v145-*.txt.
- CLAUDE 2026-10-26 08:30 build (claude-v148 build): ORIGINAL SFX wired. Export merged from the agent
  (tools/export_sfx.mjs, docs/xena-sfx.md; 4 renders matched to native --wav). tools/install_sfx.py ->
  Resources/Sfx (+ index.json, loop bodies from the raw samples, gain APPROXIMATED). PrototypeSfx: 24
  voices, func_800845C8 camera attenuation/pan, notes as pitch, one XA stream, EffectsVolume (the effects
  slider no longer changes AudioListener.volume = music). PrototypeSfxEvents: Xena swings/kicks/whoosh/
  footsteps/death voice by clip frame, water loops 0x13, power-up loop 0x3D, chakram whoosh/ricochet/hit/
  catch. Damage: connect (kick set 0x15.., sword 0x1E..), clang 0x1B, impact 9..14. Bandits: swings, steps,
  thud, 0x1000, barks (pacing INFERRED). Menus: move 0x39, confirm 0x00, level-end rows 0x38, slider 0x26.
  Landing 0x2E n55 fires on touchdown (jump-type frame INFERRED). Smoke: swing+connect log, water loop,
  attenuation oracle (127/64, 63, 48 behind, floor 10, pan 127), run footsteps 3 per 31 steps.
  All 8 suites + aggregate PASS: build/unity-import/claude-v148-*.txt.
- CLAUDE 2026-10-26 claude-v150 build: AMBIENT CREATURES. tools/install_creatures.py exports BFLY1/BFLY2
  (BFLY1 anim set)/LARK/FROG --static -> Resources/Creatures. PrototypeCreatures: 3 butterflies (follow /
  teleport >= 5001, timer modes, rest when blocked, height targets; mid-range wobble APPROXIMATED) and 5
  frogs (jump < 1000, v 6500 -500/step, 40 u/step, landing sound 0x3E.. n58, despawn), render-interpolated;
  XenaAssetPreview disabled on instances (its debug GUI showed). Smoke: 3+5 placed, frog jump+sound,
  butterfly 0/399 stalled frames at 120 FPS; build/unity-import/creatures-butterfly-close.png.
  All 8 suites + aggregate PASS (claude-v150-*). Wings still static: agent researching g_lark generator +
  SMA wing timing + vertex-frame export (docs/xena-lark-generator.md).
- CLAUDE 2026-10-26 claude-v158 build: PATROL + LARKS + WINGS. Merged agent research
  (docs/xena-lark-generator.md, tools/export_creature_vertex.mjs, 1219/1219 RAM samples).
  Patrol (flag 0x200, 3 level-01 generators): mode 1 walks to waypoint 0, sight test 6000/+-35 deg or
  1000/+-90 deg + line cast 1 m up -> approach; stop at waypoint and "hit ends patrol" INFERRED.
  Larks: g_lark phase/count/radius/350 exclusion, perched at generator pos+yaw, 210-step life, flight
  (-rsin/150, -9|-13, -rcos/150), chirps. Wings: tools/install_creature_vertex.py -> SMA vertex frames,
  one per step (butterfly rest every 2nd, perched lark frozen), blended per render frame.
  Smoke: patrol walk 1.1 m unseen then sighting; wings 0/119 stalled, 19 shapes; lark v = (26,-13,7)
  for yaw -1216 (expected 26/7); build/unity-import/creatures-lark-close.png. All suites PASS (claude-v158-*).
  CANDIDATE BUG (unverified): PSX rotY and Unity yaw are the same matrix and PSX models face local -z,
  so bandit heading = rotY + 2048; PrototypeBandits uses spawnYaw as heading directly -> bandits may
  spawn facing backwards. Check against a native capture before changing (the frog's +sin/+cos jump
  direction from the audit has the same open sign question).
  -> RESOLVED claude-v159: facing = (-sin rotY, -cos rotY) from the RAM-verified patrol snapshot (relative
  angle 0 to the waypoint), so PrototypeBandits.SpawnHeading = rotY + 2048 (bandits spawned backwards).
  Frog asm 0x8001B278..: x += 40 rsin/4096, z += 40 rcos/4096 -> frogs drawn without the 180 deg turn.
  All suites PASS (build/unity-import/claude-v159-*.txt).
- claude-v160: bandit hurt voice list D_800C0BF4 (50 % on a non-lethal hit) and Xena knockdown voice list
  D_800C142C (50 % on 0x7C from a bandit) via PrototypeSfx.PlayListAt (func_80084E80). All suites PASS.
- claude-v167: XENA ROOT MOTION. Native RAM (xena-console 2026-10-22; scratchpad rootmo/, rootmo2/):
  0x51 moves Xena 481.6 units (steps 10.8, 10.8, 1.4, 0, 14.4, 47, 66, ...) and 0x2A back evade 22/114/112/...;
  the displacement at clip counter n = SMA moves[n-1] with per-axis trunc. tools/install_xena_motion.py ->
  Resources/Xena/XenaMotion.json; PrototypeCombat applies it to every combat clip with data (replaces the
  provisional constant evade/running-attack speeds). Motor ActorLimit: steps ending within 300 units of a
  live bandit / the dummy and closer are dropped (mover actor contact). Tests updated where they assumed a
  stationary attack (combat/advanced: displacement <= root travel, dummy not passed; bandit: Xena back to
  spawn before intake; lark 24 wait 100 steps for generator 23's 350 exclusion). Sword root travel 493 u
  (native 481.6 at another yaw). All suites PASS (build/unity-import/claude-v167-*.txt).
  NOT yet: reaction clips' root motion (0x67 373 u back, 0x7C knockdown 2464 u back, 0x88 377 u) — INFERRED,
  no native capture with a knockdown. Mode 6 avoid research merged: docs/xena-bandit-avoid.md (port next).
- claude-v170: MODE 6 AVOID ported (docs/xena-bandit-avoid.md): bandit-bandit mover contact (300, escape
  rule, sticky LastContact), entry when blocked by a non-target and goal >= 3001, three steering branches
  (R 850, turn 64), wake idle/patrolling blocker, exit to mode 1. APPROXIMATED: while already in mode 6 the
  action steers even if the step is still blocked (original freezes until an UNKNOWN walk-family clip
  switch 0x15 -> 0x16). Smoke: blocked approach -> 1 entry, blocker woken, exit, 3.56 m progress.
  Note: with a blocker that walks the same way, the documented exit (|lat| >= 850) can keep a bandit in
  mode 6 for a long time (as the algorithm reads). All suites PASS (build/unity-import/claude-v170-*.txt).
- claude-v171: REACTIONS vs NATIVE. Forced hits on xena-console by poking Xena's intake (+0x12 = 40,
  +0x18..0x20 hit point, +0x24 attacker = bandit 0x80151270 moved in front; Xena rotY via actor+0xA8 -> +0x18
  -> +0xE = 2450, facing (-sin, -cos) confirmed by the 0x51 path). 0x72: 4 push steps of 30 + frame 1 + frames
  2..10 = 14 steps (port had 17); 0x7C: frames 1..26 with SMA root motion (2449 units back), get-up 0x88
  frames 1..37 (368 forward) = 63 steps. Missing frames in the SMA move list mean 0 (0x88 frame 27).
  Port: reaction lengths = SMA frame counts, fresh-frame step, root motion via PrototypeCombat.RootStep
  (exact frame). Smoke: rear 14 steps, knockdown 63 steps / 2816 units. All suites PASS (claude-v171-*).
  User chose option 2 (finish level 1 first).
- claude-v174: main menu Опции opens the shared options screen (as func_8006E1EC), drawn at depth -1150 over a
  0.88 dim; Back returns to the menu. Added the original items Аудио Моно/Стерео (mono centres SFX pan) and
  Движения Ходьба/Бег (D_800F278C read as the default gait the run key inverts: INFERRED). Load game and
  Training arena stay "not available" (both need further levels). All suites PASS; first run had two
  flakes, both PASS on rerun: advanced-moves PNG sharing violation (file lock) and bandit "Blocked bandit hit
  did not deal 1: 25" (likely a second bandit's unblocked hit in the block wait; not reproduced) —
  build/unity-import/claude-v174-*.txt and *-rerun.txt.
- claude-v175: bandit block-test flake addressed: the intake/block checks pause every other bandit (a second
  bandit's side hit mixed into LastIntake / reactions / blood), and the check reads LastBlockedIntake.
  Bandit smoke 3/3 PASS (unblockedDuringBlock 0), all suites PASS (build/unity-import/claude-v175-*.txt).
- claude-v176: main menu "Пока недоступно" note moved to the top (it overlapped the Enter prompt).
- claude-v178: REMASTER GATE = PrototypeRemasterGate (image-to-3D dropped for the flat leaves): each original
  leaf triangle subdivided 24x, front/back layers pushed apart by a rounded profile of the distance to the
  nearest transparent texel (3.5 cm + 0.6 cm grain per side), same outline/UV/HD texture, original
  collision; visible from behind too (the original one-sided leaves vanish from the back). 50688 tris.
  Style check captures: build/unity-import/gate-{front,oblique,close}-{original,remaster}.png. PENDING the
  user's verdict (prominent object). Known: stair-stepped gap edges from the HD page alpha. All suites PASS
  (claude-v178-*). Next: user verdict on the gate; then smooth the alpha edges if wanted.
- CLAUDE 2026-10-25 10:50 build (DLL 10:50:29), all 8 suites + aggregate PASS
  (build/unity-import/claude-v32-*.txt), bandit 6/6 runs 0 stalled.
  GAME OVER (native capture build/gameover, --poke 8700:0x8014E4F2:600:2 = Xena +0x12):
  clip 0x91 -> state 0x11 ~230 native frames later; menu "Повтор уровня"/"Вых" over a
  top-down death camera (func_80053380: 3000 u above Xena); Retry restarts the level;
  Exit -> "Хочешь выйти?" Да/Не (default Нет) -> state 0x13 plays MEDIA/CHAKDETH.STR -> main
  menu (state 2). Unity: PrototypeGameOver (menu at DeathFrame 110, level reload after the
  video until the main menu is ported), death camera in PrototypeCamera, video exported by
  tools/export_game_over_fmv.py; Remaster plays a Real-ESRGAN x4plus 1280x960 version
  (../_tools/realesrgan, BSD-3). Bandit stall metric: the first frame after a standstill
  (alpha~0, prior AI step 0) is no longer counted (captured 2/78 cases were exactly that).
  MAIN MENU: decoded by a subagent (docs/xena-main-menu.md, tools/export_main_menu.mjs,
  298/298 frames bit-exact vs native). Codex HD background tools/codex-remaster/out/menu-main.png
  (1536x1024, menu text removed). NEXT: port the main menu to Unity (Original: exact
  hair/lightning sprites; Remaster: HD background + animated hair), route GAME OVER to it.
- CLAUDE 2026-10-25 19:05 build: MAIN MENU in Unity (PrototypeMainMenu; tools/install_main_menu.py):
  Original = CPU composite per 30 Hz tick (bg, bolts, hair ch0..2, highlight, prompt abr1);
  Remaster = Codex HD bg + Codex HD labels (tools/codex-remaster/prompt-label*.txt), Real-ESRGAN
  lightning (alpha = brightness gain over main.tim: LTNG tiles are opaque) and prompt, CPU hair
  wind warp. GAME OVER -> video -> menu. PAINTED 360 LANDSCAPE: 4 Codex segments
  (pano_next.sh / pano_close.sh) stitched by tools/stitch_panorama.py (wrap crossfades,
  Real-ESRGAN 4x then Lanczos 2x: x4plus with -s 2 produces a tile mosaic), sampled in
  XenaWithLandscape (sky + far fog), horizon 0.55. All 8 suites + aggregate PASS
  (claude-v43-*.txt). User feedback pending: hair warp moves shoulder/sky, Google Fonts for
  menu/exit screen, enemy blood, walls see-through when close, Codex repaint of level textures.
- CLAUDE 2026-10-25 19:55 build (DLL 19:55:39), all 8 suites + aggregate PASS (claude-v56-*.txt):
  HIT EFFECTS (docs/xena-hit-effects.md, subagent): PrototypeHitEffects = burst pool (flash,
  ring d>=26, grey discs) as screen-space overlay meshes (GL in endCameraRendering never reached
  the URP frame), blood emitters (Original lines, Remaster droplets), colours linearised; checks
  for the sword spark log, blue block w/o blood, disc continuity 0/39 stalled. CODEX PAGES: 22
  pages repainted (tools/codex-remaster/pages.sh, tools/install_codex_pages.py: original alpha,
  per-cell palette lock, 12-px seam band, magenta/transparent texels from the pre-Codex
  reference; bug fixed where a rerun used the installed Codex page as reference); 22 HD pages
  bound (test now counts the document bindings). FOG: partly fogged surfaces fade into flat
  haze, only ~fully fogged take the painted landscape (user: see-through walls at 30 m).
  AI: crowd test before the <700 attack test (third bandit attacked; audit line 123).
  NEXT: user asks for less boxy environment models and SpeedTree-like trees in Remaster.
- CLAUDE 2026-10-25 22:20 build (DLL 22:20:54), all 8 suites + aggregate PASS (claude-v86-*.txt):
  relief/rounded boulders/tone blending, fog 37..85 m, light shaft, Poly Haven ground plants,
  generated trees, ~7.8k grass tufts, boot videos EA_LOGO/UNILOGO/INTRO (HD in Remaster,
  docs/xena-fmv.md: no video after New game), menu Space no longer jumps. IMAGE-TO-3D: Hunyuan3D-2
  installed in ../_tools/Hunyuan3D-2 (transformers 4.57.1, diffusers 0.35.2, hub<1.0; texgen
  extensions built by build_texgen.cmd with VS2022 + CUDA 12.8). Pipeline: Codex concept ->
  tools/hunyuan_shape.py (2mini, ~50 s) -> tools/hunyuan_paint.py -> tools/install_generated_prop.py
  -> PrototypeRemasterProps (signpost over 3 Sign_pt, yaw -90). NEXT: more props via the same
  pipeline (gates, fence/railing, rocks), fine-tune signpost facing from the original mesh.
- CLAUDE 2026-10-26 00:12 build (DLL 00:12:16), all 8 suites + aggregate PASS (claude-v115-*.txt):
  chakram 300/contact (docs/xena-chakram-damage.md); guard+mouse turns Xena; Sign_pt facing fixed
  (native capture build/signdir) + layout-locked Codex HD texture (Hunyuan signpost dropped: garbled
  letters); grass strips = Codex atlas bleed + dissolve -> padded atlas + per-tuft dropout;
  generated boulder (footprint +0 %, own Remaster collider), gate v2 REJECTED by the footprint rule
  (+33 %, IoU 0.17: Hunyuan folded the leaves); river flat faces use the lake water; horizon water
  (2554 tris) continues the lake into the haze. Bandit smoke sword strike flaky ~1/4 (strikeState
  logged).
- CLAUDE 2026-10-26 01:05 build (DLL 01:05:57), all 8 suites + aggregate PASS (claude-v121-*.txt):
  GATE FIRE (docs/xena-gate-fire.md, subagent): PrototypeFire = original 22-frame FIRE.STM /
  fire2 additive billboards + smoke emitters from the LEVEL.BIN effect table; Remaster particle
  flames/embers/smoke + flickering light. PAUSE MENU (docs/xena-pause-menu.md): PrototypePauseMenu
  (original layout, Original draws exported labels, Remaster Tenor Sans; Нет default, no wrap);
  Опции = music/effects volume, graphics, mouse, invert, Управление (rebinding moved from the
  camera overlay); Выход -> GAME OVER -> main menu. OPEN: Original label x alignment for
  "Опции"/"Вых" is still a few px off (originOffset semantics), lake edge pale triangles.

- CLAUDE SESSION 2026-10-24 22:20 (continued Codex level-presentation work).
  Build DLL build/unity-movement/.../Xena.Assets.dll 2026-10-24 22:19:51; all7
  suites + check_prototype_reports PASS sequentially (logs build/unity-import/claude-v3-*.txt).
  1) Codex @sunbm additive/HD-page staging verified (presentation PASS, screenshots).
  2) Test-timing fixes, no expectation changed: sprint capture now once (unguarded
     repeated capture skipped sim ticks -> frame11 missing, reproducible 2/2);
     combat yields one frame after combat-block capture (fixed catch-up consumed
     Ticks(3) before Input System processed the press, 3/3 fail -> 2/2 PASS).
  3) GHOSTING ROOT CAUSE: root moved only in 60Hz FixedUpdate, camera SmoothDamp
     per render frame. Strafe D+Shift @120FPS: 45/90 frames drawn model stalled,
     screen second-difference 18.0px. PrototypeMotor render-only interpolation of
     model root pos/rot between last two ticks (restored before each tick; teleports
     snap) + camera focuses PresentationPosition: 0/90 stalled, 2.75px. New check.
     Cartwheel/flip share this root path but were not separately measured.
  4) OBJECT COLLISION: CLS(26 groups)=tree trunk prisms/3dtrees/doors; Unity
     collider correct (capsule slides around, min axis .46m; new check). @rck_cx,
     @plr_cx1/2 have NO CLS: height query returns NoHeight in their footprint (holes;
     subagent MIPS read of mover 8006E78C: probes +-45deg 150u ahead + end point,
     no surface or step-up>300u blocks). Implemented OriginalTerrainLimit (grounded
     only; slide uses probe dir instead of unverified surface normal). Rock test:
     50/90 ticks inside hole -> 0. plr_cx not separately tested.
  USER REPORT 2026-10-24: chakram flies through stone pillars (@plr_cx1/2) and
  probably other solids. Likely cause: those objects have no Unity collider (terrain
  holes only); chakram uses Physics sweeps. FIXED 22:57 build: PrototypeMotor adds
  runtime MeshColliders from visible meshes 65/66/67 (rck_cx/plr_cx1/plr_cx2).
  New chakram check vs plr_cx1: before entered pillar (true), after 1 contact, not
  entered. Chakram suite now faces longest open line from spawn (rock was 5.7m
  ahead and correctly stopped the steering throw; no expectation changed).
  All7 suites+aggregate PASS (claude-v4-*.txt). Original projectile collision
  still NOT traced (subagent runs were cut off twice); other props unaudited.
  REMASTER (F10) 23:18 build, all7+aggregate PASS (claude-v7-*.txt):
  PrototypeRemasterEnvironment: Xena/RemasterWater on @_stream, RemasterWaterfall
  on @rivlet2 + terrain submesh page-9 (all 180 tris = waterfall column), mist
  particles per tall fall cluster, RemasterRainbow additive on rndbow.
  PrototypeRemasterGeometry: PN-triangle 3x3 subdivision of terrain+props (2432->
  21888 tris), crease70deg, walkable faces/edges exact (max walk offset 5e-6m,
  cliffs up to .33m), averaged baked corner colours, Xena/RemasterLit shape light.
  Colliders/height query/CLS untouched; Original restores meshes+materials (tested).
  Materials are Resources/Remaster/*.mat recreated each build. Next: texture-seam
  crossfade, trees, screenshots in build/unity-import/presentation-*.png.
  23:41 build all7+aggregate PASS (claude-v9-*.txt). Added: seam crossfade on steep
  faces (712 edges), sky (Original solid RGB(0,72,95) measured from native frame
  9200; Remaster CC0 Poly Haven kloofendal_48d_partly_cloudy_puresky 4k, source
  build/remaster-sources/sky, copied to both Unity projects' Assets/Imported/Remaster/Sky),
  PrototypeRemasterFalls: lip chains -> Chaikin -> ballistic curtains to pool level,
  hidden boxy falling faces drawn as wet rock, plunge foam strips (Xena/RemasterFoam),
  mist+spray. Screens: presentation-bigfall-{original,remaster}.png.
  USER VERDICT: better than original but still mediocre. Honest gaps: no post (bloom/
  tonemap/AO), no fog/atmosphere, flat unshadowed lighting, boxy cliffs around falls,
  milky uniform falls, pale pools. Enemy research agent runs in a separate worktree.
  2026-10-25 00:01 build all7+aggregate PASS (claude-v11-*.txt): falls now particle
  streams (velocity-stretched, lip-mesh emitters, prewarm + AlwaysSimulate so they
  are present at distance), curtains/foam on @rivlet2 too (free-standing faces clipped),
  URP post (renderer had null PostProcessData; Neutral tonemap keeps palette, bloom,
  grading, SMAA), XenaSkyFog.hlsl: aerial perspective into the HDRI panorama 14..52m
  on all Remaster level shaders (trees/bushes now RemasterLit), follow camera kept
  inside level (ground under lens + above it). Screens presentation-gameplay-*.png.
  ENEMIES (subagent worktree .claude/worktrees/agent-ad9d1aed306272328, uncommitted):
  tools/export_enemy_unity.mjs (Xena parity 0 diffs; BANDIT/1/2/4 37 nodes,18 meshes,
  61 shared clips), docs/xena-enemy-audit.md (AI func_8001B460, modes 0..7, ranges,
  crowd max 2 attackers). Damage/hit/death code still not located. Next: import bandits.
  00:27 build all7+aggregate PASS (claude-v14-*.txt): fog 30..85m (user: 14m too
  close), 4-heading gameplay shots, sun shadows (URP 4 cascades/70m/4096/soft; importer
  disables renderer shadows -> enabled in Remaster for level/Xena/bandits; ShadowCaster
  passes in RemasterLit+OriginalUnlit), CC0 ambientCG Rock051 triplanar relief on steep
  grey surfaces of subdivided terrain/props (build/remaster-sources/rock051 -> Resources/
  Remaster/Rock*.jpg). Bandits: tools/export_enemy_unity.mjs + docs/xena-enemy-audit.md
  merged from worktree; models in Resources/Bandits, spawns Resources/Level01Spawns.json;
  PrototypeBandits places 11 generator bandits idle 07 (spawn in Awake; Start+SetMode
  broke saved Original mode -> fixed). NEXT: bandit AI per func_8001B460, then user asks
  in-game level scripts (events/triggers).
  00:32 build all8 (new tools/check_bandit_player.mjs) + aggregate PASS (claude-v15-*).
  PrototypeBanditBrain: func_8001B460 subset at 30 steps/s (modes 0/1/2/3/5, ranges
  9500/700/1700/2000/550, turn 64, weighted attacks + cooldown, <=2 attackers, root
  motion from <Model>Motion.json written by exporter). Smoke: 363 steps, 16 attacks,
  max 2 attackers, closest 413u. AI disabled in player-mechanics smokes (isolation).
  NOT done: damage, hit/death, block, patrol waypoints, avoid, flee, generator trigger.
  NEXT (user): in-game level scripts; research agent launched.
  LEVEL SCRIPTS (00:5x): no interpreter in level01; tools/export_level_scripts.mjs +
  docs/xena-level-scripts.md (merged from worktree; 42/42 RAM checks) -> Resources/
  Level01Scripts.json. PrototypeBandits now = original generators (16-frame slots,
  radius/segment/|dy| triggers, max 3 alive, 350 occupancy; 12 bandits from 11 gens);
  PrototypeLevelScripts: signs (0x46+p, rearm 400), scroll (level*2+p0), health2/
  up_sword first-pickup messages, Siras 1450 level end, modal Russian message box.
  check_bandit_player covers generator, AI and scripts. Open: sign/pickup visuals
  (SIGN_PT has no SMA; pickups STM-only sprites), Siras cutscene path, m_vill music,
  pickup anim 0x37, larks/frogs/butterflies, damage/hit/death, camera line test.
  Props: exporter --shared/--static (SIGN_PT has no SMA -> SMI rest pose; bandit output
  byte-identical, Xena parity 0 diffs); SIGN_PT x3 + SIRAS placed (Resources/Props).
  Siras ending: 563-frame camera path from table 0x800C804C (yaw=+0x2A, pitch=+0x28
  confirmed by key 0 looking at Siras), Xena placed (130,-330) yaw 2787, then level end.
  Damage/hit/death research agent running (worktree). Pickup sprite rendering unknown.
  DAMAGE (06:xx): docs/xena-damage-audit.md + tools decode_action_scripts/verify_damage_
  windows/inspect_sma_damage merged. PrototypeOriginalDamage: Xena 500 hp, per-clip windows
  (+0xFE live start+1..end), |d|+sword level, potions <350, block d>40?d-40:1, death 0x91.
  Bandits: reactions 67/6D/6A, death 8D/8B/8A, 8-step invuln, knockback, retaliation,
  removal after clip+300 (Live = not removed). Contact geometry APPROXIMATED (reach/cone).
  Smoke: sword 31 (30+lvl1), death 0x8D + removal, intake 15, blocked 1. Also: hazed sky
  (Xena/RemasterSky, haze below ~37deg) + haze fog 22..70m; bandit render interpolation
  (run stalled 14/28 -> 0/28); fall particles minParticleSize; water reflects HDRI.
  Open: contact points, Xena hit reactions, power-up meter, chakram damage, game over.
  06:4x: pickups = screen-facing sprites (tools/export_pickup_sprites.mjs, sizes D_800C1D1C,
  bottom-centre pivot), m_vill music (tools/export_music.mjs, approx render; seq0 explore,
  seq1/2 combat) via PrototypeMusic (muted in smokes; package dep com.unity.modules.audio),
  docs/xena-ambient-audit.md merged. Potions now used at once when health+150<500 (user
  report confirmed by jtbl_80017594). HUD research agent running; user wants HD HUD via Codex.
  Remaster keeps bandit corpses (removed from live count, last death pose); Original hides them.
  HUD (07:xx): docs/xena-hud-audit.md + tools/export_hud.mjs merged; PrototypeHud draws
  original hilt/sword/blood (len=clamp(109hp/500,3,109), +1px/frame), 4 dagger enemy bars
  (60 frames after change, 4000 range), karma ring (meter not ported -> unlit), potion icon.
  Remaster anchors to screen edges and loads Resources/HUD/<name>-4x.png when present;
  originals to upscale in build/hud-for-upscale/. Bandit smoke press held over real frames
  (fixed catch-up after the 4x ending lost presses 1/3 -> 4/4 pass). Xena hit-reaction RE running.
  Xena hit reactions (docs/xena-hit-reactions.md merged): sector table of func_800367BC
  (0x66/0x67/0x72/0x74/0x75, knockdown 0x7C+getup 0x88 at d>=30), 4-step freeze push
  min(d,30), input lock, invulnerable during reaction; none for 1-damage blocked hits.
  Frontal bandit hit -> 0x72 (same as native capture). Bandit continuity metric now counts
  frames where the AI root moved but the body froze; strict 0 (78 frames).
  CODEX HD ART (07:3x): Codex CLI 0.163 `codex exec -s workspace-write -i <ref>` with the
  built-in image_gen; inputs build/codex-remaster/in (nearest x9..16, magenta bg), prompts
  tools/codex-remaster/, fit tools/finish_codex_remaster.py (alpha-bbox crop, own alpha) ->
  HUD/<name>-4x.png (8) and Props/Pickups/<name>-hd.png (3). Remaster uses them (checked).
  Karma meter + power-up (x2 damage, 825 steps) + 29-segment ring; frogs (0x8001B12C jump,
  despawn, interpolated). Bandit AI steps on integer ticks; stall metric uses step travel.
  Unverified/next: original Xena radius obj+0x28=150u (.375m) vs capsule .28m;
  normal-Y<3200 slope block not implemented (query has no normal); narrowphase
  800A129C slide math. Next: cartwheel/backflip continuity measurement; then F10
  Remaster environment (geometry/water/waterfall/rainbow/trees) design.

- COMPLETE user-requested close chakram camera + collision sparks/rebound.
  Baseline18chakramchecksPASS on89bc DLL. New camera measured actual original
  mesh width0.319383viewport anddistance0.828734m, looksdown/forward, no lensclip;
  screenshotchakram-close-flight.png inspected. Fixed horizontalFOV acrossaspects,
  nearclip.02, originalplayerlens restoredonautomaticreturn; noflightpositionlag.
  Solidcollisionburst18sparks, reverseheading and.12sec24m/sinitialreturn phase,
  then20m/sbreadcrumbreturn. Practice target hits stillpass through asbefore.
  ParticleSystemModule initiallyabsent; localUPMpackage now declares builtin
  com.unity.modules.particlesystem1.0.0, auto-resolvedbyUPM, no Editor download.
  First Windows run passedcamera/returnchecks butfailedpausedparticleschanged;
  Emit canresume automatic simulation. ExplicitPause addedafterEmit/Simulate,
  fresh builds andtargetedWindowschecksPASS. Initial close-return screenshot
  exposedcamera turningintoXena; returning nowrestoresplayercamera immediately.
- USER EXTENSION COMPLETE: F10Original/Remaster swaps realmaterial bindings and
  halo withouttouchingcurrentpose/flight/combat. Preference persists across
  processes; defaultsrestoreRemaster. PrototypeGraphics/IXenaVisualModeTarget/
  XenaVisualVariantRendererbindings formhooks forfuturematchedHDmodels/light;
  noHDmodelset orPS1rasterparityclaim. Allcontrollers/colliders/clocks remainshared.
  Replacedribbon withPrototypeSpinHalo:4localplaneafterimageages/6arcs,radius
  .14..20m,fade.16s,originalopaque discsharp; Original hideshalo. Imageinspected.
  NeweffectMeshFilter excludedONLYfromoriginalgeometrycount test; originalresource
  still2meshes46tris, distincthalo bounded/planar/fading separatelyverified.
- FINAL143WindowschecksPASS:26movement/38combat/17settings/26chakram/13special/
  23advanced. Fresh8logs clean; aggregate rejectsoldcode/assets. FinalDLL
  2026-10-24T18:13:40.321Z SHAd4adde7ff870232008061329df11d3ad7e7240708275f0cb6af1ae8fe2c122b0;
  resources2026-10-24T18:13:39.938Z SHA0dd02e7f950436de208c52260b1cb38419b967a2d59a04ac989554d363666d8f.
  ActualWindows build/unity-movement/XenaMovementPrototype.exe128765523bytes.
  Finalviewportwidth.313356,closecamera.828734m; novertexnearclip.
  MainPipeline7PASS/113motorTicks/81clips/2meshes46tris/0errorswarnings;
  ownEditors42304/39396 gracefullyCLOSED. UserHub/CLIkept.
  Checkpoint01ff16e. WindowsZIP200files41914642bytes,UnityZIP277files19133587bytes;
  both archive-read verified and packagedDLLmatches finaltestedSHA.
  NEXTpending:originalBanditimport andbicycleeligibilityaudit below; noclaimported.

- USER-STEERED CAMERA/ACROBATICS/CHAKRAM FIX COMPLETE (24Oct20:32MSK build):
  Guard synchronizes camera yaw to body BEFORE movement/combo selection; S/A/D
  therefore remain back/left/right with previous orbit90/180off. Horizontal orbit
  locked through defensive move even after release and through backflip flight.
  Free orbit returns outside these states; zoom/pitch/wall spherecast retained.
  Original STEP clips unchanged: render-only adjacent local-pose interpolation
  covers cartwheels/handsprings/rolls/backflips/runningflips; visibility stays discrete.
  Ghosting NOT independently reproduced: live camera SolidColor/no additional
  URP camera data/no post, exactly33player renderers; baseline capture one silhouette.
  Build removes pose stepping; do NOT claim display ghosting conclusively fixed.
- Chakram mesh localXZ bounds .29/.01/.285m. Root Euler65/worldY spin caused
  normal precession. Travel frame * localY spin900deg/s fixes wobble including
  steering/return. Rim .145m leaves .16sec bounded widening fading line trail;
  simulation ages freeze onpause, clear oncatch/reset/launch. Resources material
  retains new transparent URP shader in standalone. No flight-physics parity claim.
- FINAL131WindowschecksPASS:26movement/38combat/14settings/18chakram/13special/
  22advanced; aggregate8currentlogs clean. DLL2026-10-24T17:32:36.003Z
  SHA89bc449b98ed67e51c3920d66de744f76778a4e9577a60d82d6b31925529fb7a;
  resources2026-10-24T17:32:35.609Z
  SHAdf70a56624734bfa4f83c7b6b9084383894ce0b0f8cca3b154e669773412ab5b.
  Windows executable build/unity-movement/XenaMovementPrototype.exe128751123bytes.
  Targeted advanced22/chakram18 PASS before final rebuild, allsix fresh after.
  Initial concurrent combat run duringEditor startup failed short3tick guard-sword
  expectation after21checks; SAME DLL then passed38 twice, no expectation weakened.
  Possible inputUpdate/fixed-tick test timing sensitivity remains; retain diagnosis
  if it recurs. Opposite-camera handspring test intentionally now usesS insteadW.
- Main live Pipeline7checksPASS,114motorTicks/81clips/2chakrammeshes46tris,
  0errors/warnings. Own Editor2728 gracefully CLOSED; userHub/CLI preserved.
  Checkpoint7a73441. WindowsZIP197files40655986bytes; UnityZIP268files17977206bytes;
  both archive-read verified, packaged runtime DLL hash matches tested build.
- PRIOR AUDIT interrupted by explicit user priorities: Bandit import NOT started.
  Uncommitted research tools/capture_action_reference.mjs (delayedtarget fixture)
  and tools/lib/mips_pose_oracle.mjs (DIVU1B) retained separately, NOT part camera fix.
  New MIPS evidence: target149 range369..461/relativeangle-26..-162/classC93
  satisfies5F target gate, request3F6AC returns1/pending5F; modeF2010+4=1 not2.
  Delayed20frame Kick->Jump+Kick fixture still42/61 not5F, so geometry/earlypriority
  are insufficient explanations. Next inspectgp4B24/inputevent/list at actual gate,
  then import Level01BANDIT.TMD/STM with BANDIT.SMI/SMA; do notrepeat samefixture.

- LATEST ADVANCED BATCH COMPLETE: spinning sword4C18->4D7paired->4E31, chord
  LMB+F, freshkick extendsonepair perpair up to6additionalpairs; holdingdoesnot.
  Grace2precontactframes permitsstagger, noextra singlehit. Guard/chakrampriority
  retained, heldguard resumesSAMETICK atspinexit (extraassertionaddedbeforefinal).
  BackwardJump35 native32frameposes with4/12/21/31phases, facingpreserved and
  provisionalbackwardsvelocity. Backwardbaseattacks rotate toworldrequestbefore
  original51/61; modernintentbehavior, notclaimingexacttankturnmath.
- Audit0e66921: spin253samples48576components,classicdirection228/43776PASS.
  Native tap/hold/repeats produce2/2/4spin cycles; sourceE4latch+gp4AB0budget6.
  WithmodernnativecontrolsDownjumpgave2F; isolatedclassicoptionreveals35. Native
  EXE unchanged4515087bytes/2026-10-22T18:11:35.259Z, allusernativeeditsstagedrename
  preserved. No guestRAM fixture ororiginalresource forcedintoGit.
- FINAL Windows123checks PASS:26movement/38combat/14settings/15chakram/13special/
  17advanced. Allfreshafterguardexitfixbuild; aggregate8logsnoexceptions. Runtime
  build/unity-movement/XenaMovementPrototype.exe128737619bytes, DLL2026-10-24T17:07:20.378Z
  SHA78cd820b84c5a8239ae80abfa5314024b9e39cf922012d08132800ffd78dbf21;
  resources.assets2026-10-24T17:07:19.996Z SHA5a745ba987acef51ed0da9187b6d6e68523cf3ff542fdb9a19239eb1e3697297.
  actions-final-verification.json containscode+assetstamps; stalegreensrejected.
  Advanced testsobserve2/2/4/14cycles,HPonceperloop,chordpriority/grace,pause/reset,
  gamepad,backflipwall/landing/bodyrelativeanddirectionalattacks. Testplatformnot
  savedscene. FinalHUD/settings andspin/backflipimages inspected1280x720.
- MainlivePipeline7checksPASS:81clips,109motorTicks,chakramReady2meshes46tris,
  Playadvances,0errors/warnings. OwnfinalEditor45340 gracefullyCLOSED (earlierown
  12956closedbeforeguardfixrebuild). UserHub/CLIkept. Packages readverified:
  Windows197files40649800bytes,Unity266files18017561bytes. Allnew controls documented.
- NEXT three tasks:1) bicycle kick eligibility/input on REALoriginalenemy;2) native
  hitactivation/volumes/reactions;3) originalenemy model+AI inUnity. BicycleNOTPORTED:
  empty/stagger/relocatedNPCguest--poke fixtures didnotprove5F (2F/42/61instead).
  SourceA794gateclass10/range1000/angle700 andinputtable5Fknown, exactconditionnot.
  Oracle32748angleattemptstoppedunsupportedDIVU1B at800ADC3C; interpreterNOTchanged.
  docs/xena-advanced-moves.md records limits. Chakramflight stillprovisional,
  NPC/AI/events/saves/fullgamecombatparity pending; do notreportbicycle asworking.

- IN PROGRESS advancedbatch. Baselinespecial13PASS on fc75... build. Native spin
  253/48576components andclassicdirectional228/43776PASS:4C18->4D7(pair)->4E31,
  tap/hold/repeats=2/2/4cycles; nativebudget6 fromgp4AB0 andE4pairlatch4/5.
  ClassicDown+Jump35backflip32frames,headingheld,phasetable4/13/22/40. Modern
  nativecontrols initiallyhid35 behind2F; new isolated--optmodern_controls=0.
  Bicycleattemptswithouttarget/withstagger/guestRAMrelocatedNPC didNOTprove5F.
  SourceA794requiresclassflag10/range1000/angle700; reaction/eligibilityunresolved.
  Do notinventbicycle behavior. Newruntime editsnotyetbuilt/verified:spinpaired
  loops+inputchord,backflip35,directionalbaseattackturn. NextadvancedWindows smoke,
  regress,commit. Detailsdocs/xena-advanced-moves.md. Allusernativeedits preserved.

- LATEST BATCH COMPLETE: five more original actions playable. Runningkick=3C41frame
  roll, runningsword=3E40frame movingattack, runningjump=3147frame frontflip,
  blockkick=5C39frame radialpracticekick, blocksword=4628frame frontstrike. Original
  clips/nativephase table verified; displacement8mroll/5mslash andhitwindows/damage
  PROVISIONAL. Jump31 retimed6->19->32air/33..46landing toexistingmodernballistics.
  Ordinary2Fjump/rollwallcollision/reset/pause/gamepad retained. No invulnerability.
- Audit checkpoint ac6f7f9. Native running243samples46656posecomponents,
  guardattacks233/44736 PASS; nativeEXE untouched. 4C->4D->4E observed butnotported
  until loop conditions verified. Docs/xena-special-moves.md hascommands/evidence.
  New PrototypeSpecialMovesSmoke tests on temporarysolidplatform NOTsavedscene.
- FINAL Windows106checks PASS:26movement/38combat/14settings/15chakram/13special.
  All41rollframes observed, noheldrepeat, targethitonce,runningfrontfliplanding,
  regularjump unchanged,guardcombinationpriority, gamepadtested. Oldguardtest now
  requires46combo thenno staleattack (intendedbehaviorchange, originalaudited).
  Final executable build/unity-movement/XenaMovementPrototype.exe,128720355bytes.
  DLL2026-10-24T03:54:50.503Z SHAfc75ef0f7c0dda7ee43d3023e7dd4a8968841933da03e14ad377ebe8e60febab;
  resources.assets2026-10-24T03:54:50.068Z SHA1d5f597858936a0f1cb69aa4ef2cb9fe8755a987400f5bb31e118666d0aa7a36.
  check_prototype_reports PASS allreportsafterfinalbuild+7logsnoexceptions.
- MainlivePipeline7checksPASS:81clips,119motorTicks,originalchakramReady2/46,
  Playadvances,0errors/warnings. OwnEditor38428 gracefullyCLOSED; user'sHub kept.
  HUD+scrollsettings inspected720p, newcontrols visible. Flip/roll screenshots
  ontestplatform inspected. Packages readverified Windows193files40615812bytes,
  Unity258files17722376bytes. Usernative edits andstagedrename preserved.
- Next three tasks: 1) spinning sword4C/4D/4E repeat/exit audit andinput;
  2) bicyclekick/backflip/directional180attacks;3) nativehitactivation/volumes and
  originalenemyreactions. Chakramtrajectory/enemyAI/events/saves stillprovisional
  orpending; do notclaim completegame/fulloriginalcombatparity.

- IN PROGRESS new batch: baseline combat38 PASS, priorfinal93 buildunchanged at
  start. Controlled running243/46656posecomponents andguardattacks233/44736 PASS.
  Native1A->3Croll41,1A->3Erunningslash40,1A->31frontflip47;80->5Cspinkick39,
  80->46guardstrike28. Source31phasetable800C1474=[6,20,34,85]. Audit4C/4D/4E
  simultaneoussword+kick alsoobserved, notporteduntilrepeatconditionsunderstood.
  PrototypeCombat/Motor edits IN PROGRESS (not yetbuilt/tested); do notclaimPASS.
  Next addtargeted specialmovessmoke, build, regress thencommit. Native/useredits
  andstagedrename preserved. Relevantdoc docs/xena-special-moves.md.

- FINAL USER FIX BATCH COMPLETE: fd68557 distinct native run1A20frames +stop1F5
  replaces acceleratedwalk15; 06b7ad5 block+direction originalcartwheel28/29 and
  backwardhandspring2A. Q/RBaim +LMB/gamepadWest throw, mouse/rightsticksteering,
  releaseaim recalls, native64throw21/65catch19. Full flight is PROVISIONAL
 14m/s out/20return/150deg steering/2.2s outbound cap; notnativephysicsparity.
- Standalone CHAKRAM.TMD/SMI/STM exported viaexport_chakram_unity:2meshes39source
  vertices46triangles1palettepage135UVcorners validated, hashesinreport. Handmesh32
  firstprobe includedfingers, REPLACED bystandalone. Firststandaloneexportfailed
  onerootvalidator; addedroot and mandatoryEditorbuild resourceimport+2/46check.
  That failedWindowschakram run is not counted asPASS. Finalassetsverifiedbelow.
- FINAL Windows93checks PASS:26movement/38combat/14settings/15chakram. Allafter
  finalresourcebuild, sixplayerlogs checkednoexceptions; check_prototype_reports.mjs
  rejects stale reports after either code or Resources assetbuild changes.
  Executable build/unity-movement/XenaMovementPrototype.exe, finalbytes128706692.
  DLL2026-10-24T03:32:21.830Z SHA05ed82ad24e22f35e1db50e9dca07284fc005277fe92b8356475fd38d4a36986;
  resources.assets2026-10-24T03:34:30.162Z SHAb562705e38669ad824fd91d5ac1be1a211a49e968db8e23790468472fe07a055.
  actions-final-verification.json recordsboth. Nativecaptures465snapshots89280pose
  componentsPASS totalmovement/defense/chakram. Screenshotrun/cartwheel/cleanring
  visuallyinspected1280x720; newHUD instructions allvisible.
- MainlivePipeline7checksPASS:81clips,113motorTicks,chakramReady2meshes46tris,
  advancingPlay,capture,no consoleerrors/warnings. OwnEditor43240 gracefullyCLOSED.
  13actions/12rebindrows. Mainproject+private use sharedruntime andoriginalresource.
  Archives readverifiedWindows189files40582104bytes,Unity249files17343503bytes.
  Native/user edits/stagedrename preserved. No disk/sourceasset forcedintoGit.
- Next user-gameplay work: originalchakram flight/ricochet/eventsalgorithm; remaining
  runningroll/frontflip/attackandblockattackvariants; originalhitactivation/volumes.
  EnemyAI/events/saves/completelevel stillpending; do not claimalloriginalmoves done.

- DEFENSIVE ACROBATICS implemented/verified: block+body-relativedirection uses
  native28left24frames/29right25/2Aback38. Motor capsule displacement provisional
  2.35m side/4mback, facing held, no practice attack or invented invulnerability.
  Held direction single trigger, finishes onrelease, returns heldguard, Rclears.
  Real Windows combat38checks PASS inclleft/right/back180deg-relative/wall/reset/
  gamepad, DLL2026-10-24T03:23:25.148Z SHA4e73f92f3fd65af9f095935979c3155cd35aaed9cddded076b69cbdb73a13db1.
  Runfix alreadyfd68557. Next Qaim/LMBchakram original64throw/65catch and projectile,
  mouse steering/camera/return, then final regression and package. User asked all3.

- USER PRIORITY Oct24: sprint is walking, chakram absent, block+cartwheel absent.
  Root cause CONFIRMED:15isWALK, notrun! ControlledUP+L1 uses1A20frames,stop1F5.
  New capture_action_reference/verify_action_reference movement141snapshots27072
  posecomponents PASS; defense153/29376 confirms28left24frames,29right25,2Aback38;
  chakram171/32832 confirms64throw21frames,65catch19. Native unchanged.
  Sprint FIXED distinct1A loop and1Fstop,phasepreserved betweenwalk/run;walk0.6
  rate removed. Real Windows movement26checksPASS inclall20sprintframes andwrap,
  DLL2026-10-24T03:20:07.861Z SHA3ce4463b7dc7f56648b3ad019f96e42b37f6d9633940e63fc95b85e78c896156.
  Next implement block+direction acrobatics, then Qaim/LMBchakram controlled flight,
  verify and commit each checkpoint; do not revert to general collider audit now.

- LATEST: mixed combat now PLAYABLE. Native-confirmed SKS51-57-62(resume5)-4F,
  KKS61-62-55 and KSS61-56-52(resume2)-53. Bridges cause no practice contact.
  Recognized follow-up reserved beyond generic0.65s expiry; unrecognized input
  still expires. Pause/guard/reset retained. Windows71 checks PASS(31combat,
  26movement,14settings), same SHA586a4769c2be67e80691f5ba9b959bf573210f5b45a20f455eb5eec78e0d3baf,
  DLL2026-10-24T02:44:50.428Z. Build128324590bytes,0errors. combat-mixed.png
  inspected1280x720. Main live Pipeline6checks PASS,81clips/advancing111motorTicks/
  zero console errors. Runtime committed f37a2c0; audit b21a2c8. Own Editor45472
  gracefully CLOSED, user Hub/CLI process retained. Native console unchanged
  4515087bytes/2026-10-22T18:11:35.259Z; user native edits/staged rename preserved.
- ANCHOR AUDIT COMPLETE within stated scope: verify_xena_combat_anchors.mjs
  nine class+3C points/16byte records;485snapshots/4365rotations/13095components/
  270630original MIPS8008E40C instructions PASS. 4320current-buffer points match
  exact pre-root-motion position from independently captured previous root.
  Five interval-first snapshots excluded from buffer comparison, still rotation
  checked. Initial world mismatch9006 (-6,0,-9) resolved by measuredrootdelta.
  Previous buffer is NOT immutable history:144differences (e.g.Xzero,YZretained),
  reported UNRESOLVED, excluded from PASS. No hit-volume/damage parity claim.
  docs/xena-combat-anchors.md records evidence and limits, build/combat-geometry/
  anchors-verification.json has definitions+differences, original assets untracked.
- Packages read-verified:Windows181files/40531139bytes,Unity229files/16005592bytes,
  include mixed audit+anchor report and updated instructions; mixed screenshot
  included Unity zip. Final all71reports SHA cross-check PASS, same tested DLL.
- Next three tasks: identify zeroing of previous contact X and native activation
  actor+FE; audit80070D84 sweep/radii/victim filters; then replace provisional
  practice collision with original joint-based logic, verifying before claiming
  parity. Original enemy models/AI/reactions/chakram still pending.

- IN PROGRESS: mixed combat audit checkpoint. Baseline Windows combat24 PASS,
  codeSHA8e4e53c0.../DLL2026-10-23T19:53:04.625Z. New native --mixed scenario
  234RAM snapshots/44928pose components PASS. Confirmed SKS51-57-62(resume5)-4F,
  KKS61-62-55, KSS61-56-52(resume2)-53 and SKpair without finisher. Source
  table800C1384 corroborates routes. Reports build/combat-mixed-reference/.
  First probe exceeded native256dump limit (missing9512); fixed intervals234,
  successful rerun. Native EXE untouched. Next implement these three routes,
  bridge poses/resumed frames, verify real Windows input and existing regressions.

- LATEST: THREE-MILESTONE BATCH COMPLETE. fc134d4 official plugin+Pipeline+MCP;
  71db8b4 persistent keyboard/mouse rebinding+camera settings; 9f80144 camera
  C/R3 centering + mouse wheel zoom. Original user native edits/staged rename remain.
  Generated Pipeline project config reviewed: autoStart=true,port0(auto),browser
  clients=false,storeEvalSource=false,background command override=false. Store it
  for reproducible Editor startup; it contains no auth credentials or local paths.
- New input profile12actions,11rebind rows. C/R3 centers behind actual character;
  wheel radius2..7 with existing sphere obstruction; cameraDistance persists in
  version1 preferences, missing field defaults4. HUD shows current assignments.
  Esc fixed; gamepad action rebinding not yet implemented. Scroll menu inspected720p,
  all settings readable, scrollbar available. Existing source scenes preserved.
- FINAL Windows64checks PASS:movement26/combat24/settings14. Settings exercised in
  three fresh processes, isolated file, old controls inactive/new controls functional,
  mouse and keyboard rebinding, duplicate rejection, Esc cancellation, actual loaded
  inversion, persisted distance, defaults and malformed-file preservation.
  SAME codeSHA8e4e53c046355a493ff7eb1d3383b2e71179bbe4d077dd41cd57d25645a94b69,
  Xena.Assets.dll2026-10-23T19:53:04.625Z. Launcher EXE retains copied18:13:23Z mtime.
  Final build128320494bytes/0errors. Reports *-player-verification.json include hash.
  Logs camera-{movement,combat,settings,import}-regression.log.
- Final original Editor regression81clips/1674frames/115506transforms PASS;74level
  meshes+35955LGT retained. No shader/asset geometry changes. No running Editor,
  user's Hub only. Native console unchanged4515087bytes/2026-10-22T18:11:35.259Z.
  Archives read-verified:movement178files/40510842bytes,Unity225files/15102187bytes.
- Next gameplay work: native combat volumes/mixed combo transition audit; original
  enemy models/AI/reactions; chakram/events/saves. Current practice damage/reach are
  explicitly provisional. Plugin installed/enabled globally, MCP unity-xena pinned
  main project; open its Editor for live operations. CLIbeta13 used successfully;
  beta14 available (reported), unsupported --tools removed from MCP config.

- LATEST: PERSISTENT SETTINGS + KEYBOARD/MOUSE REBINDING done. fc134d4 is plugin/
  livePipeline/MCP checkpoint. PrototypePreferences loads version1 JSON at
  Application.persistentDataPath/XenaPrototype.settings.json, atomic temp+replace,
  malformed file preserved/defaults active. --settings-file and --no-settings
  isolate tests from user preferences. PrototypeInput has10rebind rows, duplicate
  rejection, Escapecancel stays paused, defaultrestore, dynamic HUD labels.
  Existing debug OnGUI maintained per installed ui-imgui skill; not a final UI.
- Actual final Windows59checks PASS:22movement/24combat/13settings in3separate
  write/read/malformed processes. Includes actual mouse rebind, old buttons inactive,
  persisted inversion affecting camera, defaults and corrupt-file preservation.
  Same gameCodeSHA6c0ec97f021d462ee8b9120ed0948f7f672584cc070bef33155fec2ad001af59,
  Managed/Xena.Assets.dll2026-10-23T19:45:46.212Z, build128317450bytes/0errors.
  EXE launcher copied mtime remains18:13:23Z. settings-menu.png inspected720p.
- Final81clip Editor regression PASS1674frames/115506transforms/74levelmeshes after
  Pipeline installed. Live verification+MCP C#eval repeated PASS after domain reload.
  Initial recompile request timed out120s while files changed; later compile0errors/
  0warnings and cold build/import succeeded; timeout reported, not called PASS.
  Main own EditorPID43668 and privatePID43244 gracefully CLOSED; user Hub kept.
- Next: checkpoint settings, then original-plan C/R3 camera centering and mouse-wheel
  distance control. Retain59checks, extend camera checks and repackage final build.
  Native console unchanged4515087bytes/2026-10-22T18:11:35.259Z; user edits preserved.

- LATEST: OFFICIAL PLUGIN CONNECTED. User explicitly authorized installing plugin,
  autonomous multi-step work and milestone commits. unity@unity-agent-plugin0.1.8-beta
  installed/enabled via codex CLI; read installed unity-cli skill+live verification
  recipe. Pipeline0.9.1-exp.1 first private then MAIN manifest. ExistingCLIbeta13;
  beta14 notification surfaced, not updated. No license/auth changes needed.
- Live private+main Editor six-check verifier PASS: explicit routing, C#version,
  Play frames actually advance, original81clips+dummy100,1280x720camera PNG viewed,
  zero console errors. Helpers tools/unity_cli.mjs and verify_unity_pipeline.mjs.
  Fixed helper UTF16 PowerShell redirection and global flags after -- separator;
  capture.savedPath is authoritative (Pipeline prefixes Assets). No test weakened.
- Registered global Codex MCP unity-xena pinned MAIN project. MCP protocol initialize,
  list166tools and C#eval version PASS via check_unity_mcp.mjs. CLIbeta13 does NOT
  accept documented newer --tools flag: initial MCP exited, removed unsupported
  filter, successful retest. No other MCP entries overwritten.
- Own private EditorPID43244 gracefully closed via CLI. Own MAIN Editor running
  hidden batch PID saved pipeline-main-editor.pid; user previously had Hub only.
  Both restored stopped/backgroundoverridefalse/autotickfalse after live tests.
  New Pipeline dependency not yet tested in rebuilt Windows Player (runtime was
  unchanged at this checkpoint). Next development: persistent camera preferences
  and interactive keyboard/mouse rebinding, tested independently of user settings,
  then rebuilt movement/combat regressions. Native source edits/staged rename preserved.

- LATEST STEERING RESEARCH COMPLETE: docs/unity-agent-tooling.md compares official
  Unity-Technologies/unity-agent-plugin+skills, Unity CLI/Pipeline and third-party
  CoplayDev/unity-mcp using primary docs. Existing LOCALAPPDATA/Unity/bin/unity.exe
  28111840bytes,version1.0.0-beta.13 found (not PATH). Read-only status returns
  STATUS_NO_INSTANCES; no open Editor/Pipeline installed, NOT license regression.
  uv exists. No new plugins/packages/global config installed. Official Pipeline
  claims6.0+;6000.3 fits declared range but integration not yet exercised. CLI is
  experimental; own CLI MCP supported, old ai.assistant MCP deprecated. No Unity
  in current curated OpenAI skills/app plugin search. Official Unity plugin exists
  outside that app catalog. Candidate skills CLI/physics/UI/URP/audio/navigation.
- Combat runtime checkpoint f1fd75c committed after46actual player checks and
  original81clip import regression. Previous owned checkpoints923ff3d and41623d3.
  User native source edits/staged rename preserved. No push requested/performed.
- NEXT: test official Pipeline in private verification project before adoption;
  preserve existing independent Windows smoke gates. Original hit volumes/mixed
  combos/enemyAI remain gameplay work, unaddressed by generic skills.

- LATEST: PRACTICE COMBAT COMPLETE in movement build. User now explicitly requests
  larger autonomous batches and commits at milestones. Baseline/import/movement
  checkpoint923ff3d and native combat audit41623d3 committed with --only exact
  owned paths. User native edits and staged dis.py->disasm.py remain untouched.
- Native controlled capture tools/capture_combat_reference.mjs:251RAM samples,
  original console4515087bytes/2026-10-22T18:11:35.259Z. inspect_xena_combat.mjs
  PASS48192local components. Sword51->52->3A, kick61->62->5A; block80. Mixed bridge57
  and resumed62 observed, not ported. Full hit volumes/class+3C remain unaudited.
- New PrototypeCombat/Input10actions: LMBsword/Fkick/holdRMBguard, gamepadWest/North/
  leftShoulder. Two-request bufferTTL.65s game time, observed chain frames10/10/9/22,
  NOT proven earliest native windows. Attacks/guard lock movement, no auto-repeat,
  buffer cleared by reset/airborne/guard; pause freezes clock. Original clips sampled.
- PracticeTarget placed3mright at terrain floor, trigger primitive, HP100, single hit
  per swing, red flash, wall/direction/range rejection. Practice ONLY reach1.65/1.25,
  contactframes51=5..7/61=6..8/othersmidpoint+2,damage20/15. No original hitbox parity.
  Optional pause toggle retaliates every2s at60Hz,near1.8,damage10; front guard protects,
  rear bypasses. R resets health+disables retaliation. No enemyAI/death/reactions/audio.
- Final actual player46checks PASS (22movement+24combat), same codeSHA256
  e594a04bac423941c269a143a7dfe7fb94764e27be67bfce50c274c2bfb5f3a9.
  XenaMovementPrototype.exe launcher timestamp remains18:13:23Z; NEW managed
  Xena.Assets.dll mtime2026-10-23T19:18:57.626Z, final build127596563bytes/0errors.
  Both wrappers now record testedBuild path/mtime/hash and reject code changing
  during run. Logs combat-player-smoke.log/combat-movement-regression.log.
  Combat sword/kick/block PNGs viewed; all original poses visible, UI legible.
- Main Editor import regression PASS81clips/1674frames/115506transforms,74levelmeshes,
  35955LGT components. Shared .NET PASS combat-dotnet-regression.log. No shader changes.
  Archives read-verified:movement175files/40240450bytes,Unity212files/13547023bytes.
  Docs movement-prototype.md/xena-combat-audit.md explain controls+provisional rules.
- CURRENT STEERING: user asks research Unity-specific skills/MCP to ease development
  and testing, while preserving completed combat work. Read skill-installer and
  plugin-management skills. Curated OpenAI list has no Unity skill. Available plugin
  directory search did not return Unity. Found official Unity-Technologies/skills
  (README links official plugin+Unity CLI) and third-party CoplayDev/unity-mcp.
  Research primary docs/compatibility before selecting. Nothing installed/configured yet.
- NEXT THREE: finish skill/MCP research; audit original hit volumes/mixed combo rules;
  connect original enemy assets and reactions only after format/AI audit. User scene
  modifications preserved. Source disc/native files unchanged. No live user Editor.

- LATEST: FULL GROUND LOCOMOTION CYCLE FIXED. Previous prototype looped5frame14
  (start only). tools/inspect_xena_locomotion.mjs checks archived btnprobe2/chakram2/
  chakram3:66 locomotion poses,12672 non-root components EXACT;188total samples
  including122 following idle samples (idle poses not re-compared in this audit).
  Native transitions14->15,15frame24->0,15->17,17->00 verified;all25frames15 captured.
  Roles:14start5frames,15loop25frames,17stop7frames. Slot16 remains UNKNOWN, excluded.
- PrototypeMotor ground phase reset on slot change; start and stop one-shot,
  run15 loops. Shift changes rate without restarting; movement interrupts stop;
  jump/reset clear ground phase. Capsule movement speeds/response unchanged.
  Ground animation30fps sprint/.6walk are still provisional; no separate walk clip
  established and no claim of native real-time speed parity.
- Baseline18checks PASS before fix:locomotion-baseline-smoke.log. New actual Windows
  player22checks PASS, including all25loop frames+wrap/no repeated start,Shift
  retains loop,stop->idle,restart interrupts stop. Existing18checks retained,
  walk0.666668/run1.466667,jump0.85,30/120FPS0.8333359/0.8333321 unchanged.
  Screenshot movement-run-cycle.png viewed1280x720. Logs locomotion-player-smoke.log,
  locomotion-build-wrapper.log. Rebuilt build/unity-movement/XenaMovementPrototype.exe
  with total127575526bytes,0errors; launcher EXE copied timestamp still18:13:23Z,
  NEW XenaMovementPrototype_Data/Managed/Xena.Assets.dll2026-10-23T19:03:41Z.
- Main Editor regression PASS81clips/1674frames/115506transforms,74levelmeshes,
  geometry+35955LGT components unchanged;locomotion-import-regression.log.
  Whitespace/node syntax PASS. No native edits; console4515087bytes/2026-10-22T18:11:35Z.
  Docs movement-prototype.md + xena-locomotion-audit.md updated. Archives read-verified:
  movement172files/40215416bytes; Unity200files/10949535bytes. Existing launchers work.
- NEXT THREE: (1) audit native sword/kick/block clip mappings and transition windows,
  (2) add input buffer + original action animations in isolated prototype,
  (3) connect test target after hit timing/range audit. Combat/AI/chakram/events/
  saves remain absent. Preserve user scene edits and native source modifications.

- LATEST: ORIGINAL JUMP CONNECTED in Windows movement prototype. tools/inspect_xena_jump.mjs
  checks84 archived native RAM captures (flatjump/jf1),16128 non-world-root joint
  components EXACT against decoded SMA. Native2F state really uses slot2F despite
  obj+E2=0. Frames zero-based6takeoff/17apex/29touchdown,44total;794source-unit height.
  IMPORTANT correction:35 in jf1 moves up+forward, NOT a standalone landing clip.
  Do not rely on old game_notes tentative label. No user/native files edited.
- PrototypeMotor now samples2F ascent6..17/descent17..28 by vertical velocity,
  then grounded29..43 once at provisional30fps; movement/new jump interrupts tail.
  Preparation skipped to preserve responsive Space. Original physics NOT ported;
  walk/run/jump speeds unchanged; edge falls provisionally reuse descending2F.
  Reset clears pose immediately. XenaAssetPreview.SampleSlotFrame clamps frames
  without looping; CurrentSlot/CurrentFrame expose playback for diagnostics.
- Baseline14check smoke PASS before changes (jump-baseline-smoke.log). New player
 18checks PASS: adds actual animated joint change+air phase range, landing recovery
  toIdle, movement interrupts recovery, reset clears pose. No weakened old tests.
  movement-jump-air.png and movement-jump-land.png visually inspected1280x720.
  Walk0.666668/run1.466667, jump0.85,30/120FPS0.8333359/0.8333321 unchanged.
  Report movement-player-verification.json; log jump-player-smoke.log.
- Rebuilt build/unity-movement/XenaMovementPrototype.exe, total127571942bytes,
  zero errors. Unity launcher EXE retains copied mtime18:13:23Z; ACTUAL CHANGED GAME
  CODE XenaMovementPrototype_Data/Managed/Xena.Assets.dll mtime2026-10-23T18:45:33Z.
  Existing Play-Unity-Prototype.cmd runs new data/code. Main Editor81clip regression
  PASS1674frames/115506transforms, level geometry/LGT unchanged; jump-import-regression.log.
  Source whitespace and node syntax PASS. Native console EXE unchanged4515087bytes/
  2026-10-22T18:11:35Z. Hub only, no user Editor closed or left running.
- Docs/movement-prototype.md and docs/xena-jump-audit.md updated. Archives read-verified:
  movement170files/40202637bytes, Unity197files/10051228bytes, include jump audit.
- NEXT THREE: (1) map full native locomotion cycle (14currently5frames;15..17 need
  native pose validation), (2) original sword/kick/block input buffer in isolated
  arena, (3) connect a test target only after hit timing/range audit. Combat/AI/
  chakram/events/saves still absent. Preserve user's scene edits on all future runs.

- LATEST: WINDOWS MOVEMENT PROTOTYPE ready. Root Play-Unity-Prototype.cmd launches
  build/unity-movement/XenaMovementPrototype.exe (all sidecars required),1280x720.
  Root Open-Xena-Movement.cmd opens main project separate scene
  Assets/Scenes/Level01MovementPrototype.unity. Scene created in main project via
  --movement-scene, verified PASS; existing asset-view scenes not overwritten.
- Input System1.20.1 compatibility verified official6000.3 docs, actual Editor
  resolved package. Runtime+Editor asmdefs reference Unity.InputSystem; package
  adds physics module. Both input backends enabled in main/private ProjectSettings.
  Input/XenaPrototype.inputactions:7actions for keyboard/mouse/gamepad, generated
  stable IDs via tools/prepare_movement_input.mjs. No runtime rebinding UI yet.
- Runtime PrototypeInput/Motor/Camera + PrototypeSmoke. Original Xena4x + original
  Idle0/Run14 sampled via new SampleSlot API, preview component disabled while motor
  drives pose. No verified jump clip yet: airborne usesIdle. Scale400 assumed;
  walk2.5/run5.5/jump6/gravity20 are prototype tuning, not original movement parity.
  CharacterController1.8h/.28radius approximates movement only, no combat hitboxes.
  Ground uses original2290triangle mesh+integer height query;119CLS source triangles
  staged as two-sided collider. All1071CLS values compared to native snapshots by
  prepare_movement_prototype.mjs. Camera smooth focus, mouse/gamepad orbit and
  sphere sweep; pause has sensitivity and invertY (session only).
- Actual Windows smoke14checks PASS with virtual Input System devices restricted
  to test actions (no global OS key injection):60Hz clock,ground,WASD,sprint,
  normalized diagonal,30/120FPS consistency,mouse look,jump/landing,pause/resume,
  both gamepad sticks,reset,capsule wall blocking,camera obstruction. Measurements:
  walk0.666668/run1.466667 over16ticks; jump0.85; yaw18deg; wall0.4033;
  30FPS0.8333359 vs120FPS0.8333321. PNG movement-player.png viewed (earlier1280x900;
  final smoke1280x720 also PASS). Scene was built/tested in private project first.
- Fixed real issue: Editor Time.fixedDeltaTime assignment did not serialize player
  TimeManager setting; set60Hz explicitly in motor Awake and assert in player.
  First narrow unrotated test wall allowed sideways sliding; corrected fixture to
  a wide wall perpendicular to movement, retaining same blocking assertion.
  Final EXE mtime2026-10-23T18:13:23Z, build totals127568358bytes,0errors.
- All original Editor imports and115506pose checks PASS after new dependencies;
  8-frame GPU shader regression PASS, character hashes unchanged. Shared .NET
  check PASS (movement-dotnet-regression.log), whitespace PASS. No running Editor
  intentionally left open; user's previously closed state preserved. Native
  xena-console.exe unchanged4515087bytes/2026-10-22T18:11:35.259Z.
- Packages read-verified: Xena-Windows-movement-prototype.zip168files/40193697bytes;
  Xena-Unity-import.zip193files/8193158bytes. Docs/movement-prototype.md has controls,
  rebuild commands and limits. Tools/run_unity_import --movement builds separate
  executable, --movement-scene creates/checks scene only; --project selects private
  verification project. Tools/check_movement_player.mjs is actual standalone test.
- Final1280x720 player screenshot inspected: original Xena/terrain visible, controls
  legible, no pink materials. Next animation audit baseline: docs/game_notes.md
  records state2F jump,1A/1E leap,2A fall,35 landing from native screenshots. These
  are STATE IDs, not yet proven SMA slot mappings; do not connect blindly. Existing
  build/jumpfind/ram_*.bin captures can establish animation pointers without another
  broad native run. Prototype currently still deliberately samples Idle airborne.
- NEXT: map original jump/locomotion clips and measure motion parameters, or add sword/
  kick/block input buffering in a controlled arena. Full combat/AI/chakram/events/
  saves and whole-level traversal unimplemented. Avoid claiming original gameplay
  parity from movement smoke. Preserve user's new scene edits on later turns.
- LATEST PLRS audit PASS: tools/inspect_level_spawns.mjs and encodePlrsRecord in
  lib/xena_level.mjs. Executes original63340..638F4 parser+889AC numeric routine,
  all37records/3108decodedbytes exact; int32/int16 boundary synthetic PASS.
  PoseOracle added only correct unsigned SLTU (fn2B) required by original atoi.
  Whole animation3297597instr and matrices14776398instr regressions PASS.
- Native actor listF1320 has54 live objects;37 unique source matches by class
  name+stored origin(+98), no assumptions about allocation order. All444origin
  bytes and1332extra vector bytes(+60) exact.255/259parameter shorts(+84) exact;
  4differences only generator+86. Original33BB8..33BEC decrement block replayed
  5times reaches observed values; does NOT claim full AI history replay. Total
  130784instructions. Decode temp buffer was freed; zero native temp matches are
  expected and not called an error.18g_* sources all use updater80032C18.
- Artifacts: spawns-source.json, spawns-records.bin, spawns-native.json and
  spawns-verification.json. Spawns.json staged for Unity DATA ONLY. Browser
  optional overlay37 markers, orange generators/cyanXena/whiteother objects,
  individual source selector. No actual NPC meshes/AI spawned. Chrome QA14checks
  PASS, screenshot viewed. Existing81clip viewer PASS after shared renderer update.
- Full build_level_assets.mjs --browser PASS, log
  build/unity-import/level-spawns-pipeline.log; geometry/LGT/terrain .NET regression
  included. Diff whitespace PASS. Native xena-console.exe unchanged4515087bytes,
  mtime2026-10-22T18:11:35.259Z. No running Editor; Hub only.
- Archives read-verified after PLRS: Unity167files/7304971bytes;
  Level01 71files/4850413bytes. Standalone character viewer unchanged.
- NEXT CONCRETE TASK: resource/import audit of FIRST LEVEL is sufficient to start
  an isolated third-person movement/camera prototype using original Xena and terrain.
  Preserve user project scenes and native source edits. Verify current official
  Input System package compatibility before adding it; don't guess versions.
  Full game audit/AI/generator histories/STP/billboard/render flags/event scripts
  remain incomplete; do not let unproven @billboarding block controlled movement.
  Original motion speed/logic tick rate not yet measured; mark preview assumptions.
- LATEST object flags/water audit PASS. tools/inspect_level_flags.mjs runs WHOLE
  original800BAA3C..800BAD88 classifier (196625instructions),74 names+58synthetic
  cases, matches all148 native chunk flags under7FFF0, rotations and positions.
  tools/lib/xena_level_flags.mjs reads original EXE tokensGP4708..477C. Do NOT use
  String.includes: original BFB70 forward-only substring search differs on ttree.
  PoseOracle now accepts optional initial register overrides and stopAt for this
  bounded original block; default callers unchanged. Full animation3297597instr
  and matrix14776398instr regressions PASS after this extension.
- Confirmed @prefix/bit20 means preserve absolute placement height (72objects),
  NOT evidence of camera-facing behavior. terr_/bit10 static, tree/bit40 category
  (26objects), stream/bit80 water. rndbow/index0 ground query changesY905->971,
  exact native match. Other bits retained without speculative semantics.
- tools/inspect_level_water.mjs validates all65536 sine phases against AD94C
  (2785280instructions),302 native watervertices atphase48128/base0. Shared
  tools/preview/xena-water.js reproduces original integer interpolation/Y wave.
  Browser now has optional grounded placement and manual waterphase controls;
  Chrome QA checks visible canvas changes, passes all existing controls. Shot viewed.
  No game tick rate assumed, no level04 Z deformation claimed. Flags.json and
  Water.json staged for Unity DATA ONLY; Unity positions/water remain static.
- Full node tools/build_level_assets.mjs --browser PASS; log
  build/unity-import/level-flags-water-pipeline.log. Native reference EXE unchanged
  (4515087bytes,mtime2026-10-22T18:11:35.259Z). No live Editor on this turn, Hub only.
- Packaged/read-verified after flags/water: Unity165files/7301448bytes,
  Level01 67files/4838311bytes. Whitespace PASS. Rabbitizer was installed only
  under ignored build/python-tools for a diagnostic disassembly; no global install.
- Next: camera-facing behavior remains UNVERIFIED; don't rotate trees based on @.
  Recover render path if evidence identifies it, or audit PLRS37-record loader.
- LATEST Unity LGT integration PASS: original byte RGB flows via custom glTF
  _XENA_LGT attribute (not standard normalized COLOR_0) -> primitive.lightingRgb
  int[] -> Mesh.colors32, alpha255. Validator rejects missing/invalid colors.
  35955 components independently verified against raw archive, glTF, .NET DTO,
  and actual Unity Mesh. Importer version bumped2 to refresh old asset caches.
- Shader uses _UseVertexLighting and independent _LightBias, clamps channels
  before interpolation, modulates encoded RGB, converts to/from Unity linear RGB.
  Added explicit Core Color.hlsl include after actual shader compiler identified
  missing LinearToSRGB. XenaLevelLighting Play controls use per-renderer property
  blocks; shared materials/source colors untouched. Character default LGT off.
- Isolated Unity project created at unity/XenaLightingVerification (ignored),
  copying Assets/Packages/ProjectSettings only, shared local UPM package. User's
  earlier Editor PID39164 was already closed on this turn, only Hub running.
  Main scenes not changed. Wrapper --project supports isolated verification.
  Real Editor import PASS:81clips/1674frames/115506transforms+35955levelRGBvalues.
  GPU8-frame PASS on RTX4070SUPER/D3D12: levelunlit/LGT0/LGT35 differ, neutral
  RGBprobe64,96,128 preserved; bias35->82,122,163 within2. Level shots viewed.
  All3 character frame hashes exactly equal render-before-lighting.json.
- .NET PASS9 malformed cases, all95880 geometry values+35955lighting components,
  1387 heightqueries; native assembly vertex verification PASS11985vertices.
  API compiler now includes all package C# files absent from old Editor RSP,
  preventing omission of new runtime/editor classes. Native files untouched.
- Next: billboard/name flags and PLRS meanings; precise PS1 STP/dither/fog and
  pixel parity still unverified. Windows viewer build is still the earlier
  character-only build; no new standalone level viewer claimed.
- Packaged after integration, archives fully read-verified: Unity163files/
  7297572bytes, Level01 62files/4827838bytes. Whitespace check PASS.
- LATEST LGT audit: tools/inspect_level_lighting.mjs PASS. LEVEL.LGT11744bytes:
  2936 source vertices/74 objects,4 bytes per vertex (RGB + retained code, NOT
  alpha). Loader800BD14C: vertex+12 sourceRGB/code44, +8 signed-short bias/clamp
  RGB + original fourth byte. InitialEXE bias800C1644=0; native snapshot=35.
  Native comparison covers all source vertices after68static+80dynamic chunks:
  3959 vertices/31672colorbytes,3995faceindices exact. Artifacts lighting-source.json
  and lighting-verification.json. Lighting.json staged next to Unity level model;
  Unity shader now applies it (latest entry above). Do not claim PS1 pixel parity.
- Subdivision helper now supports FIFO mesh libraries (BDB28 counterpart) while
  single-mesh wrapper remains compatible. Native dynamic geometry differs only
  by water update: object14 @_stream/flag80,302 vertices. Verifier reproduces Y
  using current native phase/base and original sine800AD94C (12395instructions).
  Source water mesh is not changed. No arbitrary exemptions to geometry checks.
- Browser Level viewer has optional LGT RGB modulation and independent brightness
  slider (snapshot35), approximate pixels only. Chrome QA PASS for lighting toggle,
  brightness, existing74-object/assembly controls; existing Xena81-clip viewer also
  PASS with updated shared renderer. Screenshot viewed. Geometry/assembly/grid/
  1387 height query regressions PASS. Open Editor PID39164 preserved/responding.
- Previous next step (completed): apply LGT in isolated Unity verification;
  remaining billboard/name flag semantics and PLRS. Never claim
  browser modulation equals PS1 pixel output; STP/fog/dithering still separate.
- Packaging after LGT: Unity zip156files/6453694bytes, Level01zip62files/4780515bytes,
  fully read-verified. .NET regression PASS (lighting-regression-dotnet.log), diff
  whitespace PASS. Tested native reference remains xena-console.exe4515087bytes,
  mtime2026-10-22T18:11:35.259Z. No native source or executable changes this turn.
- LATEST 2026-10-23: sandbox removed by user environment; real Editor now runs.
  editor-verification.json PASS:33 meshes,81 clips,1674 frames,115506 transforms;
  level74 meshes/3995 triangles. Both Unity preview scenes created and saved.
  render-verification.json PASS: RTX4070SUPER/D3D12, original/upscale/different
  pose/level screenshots; shader compiled, no error magenta. Model+level viewed.
- INTERACTIVE EDITOR LEFT OPEN intentionally for user: PID39164, window title
  XenaUnity - Level01AssetPreview - Unity6.3LTS6000.3.25f1 <DX12>, Responding=True.
  Root Open-Xena-Level-01.cmd actual launch verified; interactive-editor.log loaded
  scene successfully. Do not start batch work on this same project while open;
  preserve user's scene edits. Stop only agent-owned automation as appropriate.
- Windows standalone built PASS,118826798 bytes. Actual player smoke PASS:
  81clips/33renderers, original/upscale frames differ; screenshots viewed.
  Root Preview-Xena-Unity.cmd starts build/unity-preview/XenaAssetPreview.exe.
  This is an asset viewer, no movement/combat/level traversal claimed.
  Rebuild: run_unity_import.mjs --editor ../Unity63/Editor/Unity.exe --build;
  verify: node tools/check_unity_player.mjs. GPU Editor check uses --render.
- Runtime SetUpscale now public and shared by GUI/verification. Editor lifecycle
  OnEnable does not run like Play mode: toggle explicitly when capturing Editor.
  Player capture needs a visible render window; windowsHide:true produced null
  screenshot. Fixed harness to show its own temporary player window; smoke exits.
  Added built-in screencapture module dependency for this opt-in diagnostic.
- Earlier sandbox/Editor blockers below are historical and superseded. Native
  port sources/binaries and original disc/EXE left unchanged. Next resource tasks:
  LGT lighting, billboard flags, PLRS semantics; no full controller before audit.
- USER STEERING: Hub says no projects, Preview-Level does not work. Project
  already exists at unity/XenaUnity; don't create another. Root launchers now
  directly open short-path Editor with -projectPath and -executeMethod creating
  Xena or Level01 preview scene. New Open-Xena-Level-01.cmd. interactive-editor.log
  captures user-run startup. Real batch Editor success now confirmed; Hub registration
  requires Add project from disk in user's UI (no external profile writes).
- Preview launcher no longer uses Windows HTML association; open_asset_preview.mjs
  validates sidecars and explicitly launches Chrome/Edge/Firefox using file URI.
  cmd.exe /d /c Preview-Level-01.cmd exit0; Chrome level QA PASS, screenshot viewed.
  User-facing Chrome launch requested successfully; visible user window unverified.
- LATEST: complete player/level pipelines PASS. Logs:
  build/unity-import/player-pipeline-after-repair.log and
  build/unity-import/level-pipeline-verification.log. Native EXE unchanged.
- LATEST grid recovery: build_level_terrain.mjs reconstructs the complete ordered
  grid using archive+EXE only, no RAM. Original BEBC4 static subdivision ->68
  chunks, all2523 ordered vertices and2290 face indices match native RAM. Original
  BD604/BCBBC stable integer mean-Y sort fixes the62 differing cell orders.
  All256 cell lists and oriented triangles now match exactly. Membership helper
  oracle237932instructions; both source/native grids match all1387 height queries
  (8859737 original instructions). terrain-unity.json now comes from this builder.
  Source: tools/lib/xena_subdivision.mjs, EXE config800C18E4+6 radius15000,
  sqrt table800CE598. Scope is first-level static terrain; other levels unverified.
  Pipeline with --api PASS; log build/unity-import/level-pipeline-archive-terrain.log.
- Packaged and read-verified: build/Xena-Unity-import.zip (prepared scenes,
  settings and Editor/GPU/Windows reports), Xena-Windows-asset-preview.zip
  (166files,38746826bytes), Xena-Level01-assets.zip (60files,4757473bytes). Diff whitespace check
  PASS. Fresh editor-verification.json now PASS. All owned batch Editor sessions
  finished or stopped; user's Hub processes left untouched.
- Level BIN placements recovered:74x40 bytes at header+104, all2960 match RAM.
  Original 8008E8EC generates matrices (9125 instructions). Assembled glTF
  verifies11985 vertices against original GTE affine procedure (143820 instructions).
  Original78 zero normals replaced geometrically in diagnostic glTF only.
  Viewer now supports full assembly and individual objects; Chrome QA PASS.
- Original height query recovered: runtime grid256cells/66populated; all2290
  triangles match TMD object1. JS AND C# match original800142EC for1387 queries,
  1005hits/382misses,8859737 original instructions. Runtime/XenaTerrainQuery.cs
  is engine-independent, compiled and checked. Cached queries/normal outputs,
  cached query/normal outputs and movement integration remain unverified.
- CLS check strengthened: all6444 bytes including header match; all26 groups
  have absolute Y flag2, no inferred terrain offsets needed for this level.
- Level staged for Unity:75nodes,74meshes,3995triangles,26textures,0clips plus
  Terrain.json. Generic importer retains player behavior. New level-scene menu
  and batch optional level check compile via actual Unity assemblies:PASS.
  Shared .NET check additionally compares95880 level values and1387 height queries.
  Package files compile/syntax-check as applicable. Engine import/GPU now verified
  separately; api-compilation.json remains a narrower Roslyn-only check.
- Installed-package cache also lost9 sample files: restored only missing files
  from exact matching installed versions via repair_unity_cache.mjs. No existing
  cached file overwritten. Installer calls this before verification on reruns.
- Rebuild level: node tools/build_level_assets.mjs --native-reference --browser --api.
  Package both previews: powershell -NoProfile -File tools/package_asset_previews.ps1.
  Next: final package/doc checks; Engine import blocker resolved after sandbox removed.
  Lighting and billboard flags are
  the next independent resource tasks. Details: docs/level-resource-audit.md.

Earlier notes below are chronological; the latest results above supersede them.

- User asked for autonomous work without questions, economical until 01:16 MSK.
- Unity Personal is active. Current blocker is agent sandbox IPC: new Editor
  cannot connect to Hub's LicenseClient-filip (connection refused); new client
  cannot acquire existing mutex and WMI BIOS read is denied. Stopped own waiting
  session 14250 with Ctrl+C. Do not repeatedly retry or alter Hub/account security.
- User-run failure was DIFFERENT: URP CS0246 MaterialReferenceChanger missing.
  Installer path reached 271 chars. Compared new installation: old path lost
  666 files. Reinstalled official signed 6000.3.25f1 successfully, exit 0, at
  `../Unity63/Editor/Unity.exe` (short path). Required URP source/meta present.
  Optional missing dependency is profiler firewall configuration only; untouched.
- Old URP cache preserved under build/unity-import/urp-cache-before-short-path;
  restored PackageCache from complete short installation. Original failing log
  is editor-before-short-path.log; installation-path-audit.json lists all losses.
- Actual Roslyn compile with installed Unity assemblies PASS: restored URP.Editor
  (including both previously missing files), Xena.Assets and Xena.Assets.Editor.
  This IS Unity API compilation, NOT Editor import/animation sampling/GPU rendering.
  No fresh editor-verification.json exists. Install/Open launchers now use Unity63.
- First-level tools: inspect_level_assets.mjs, verify_level_assets.mjs,
  capture_level_reference.mjs, inspect_level_collision.mjs, lib/xena_level.mjs.
  Outputs under build/level-assets/01. 74 objects, 3995 triangles, 59 STM images,
  26 pages. glTF has one scene per source object, default mesh1; no guessed placements.
  PASS 95,880 independently checked geometry/normal/UV values; all UV/CLUT corners
  populated. PS1 semitransparency flagged, not reproduced. Scale400 is assumed.
- Fresh native level01 reference captured via --verbose, --poke7420:800EE200:1,
  RAM at9200. Unchanged build/port/xena-console.exe mtime2026-10-22T18:11:35.259Z.
  Log names identify LEVEL.BIN/LGT/MIP/CLS, PLRS.DAT, FLEVEL.TMD, HORIZ.STM.
  Archive audit now 385 named entries (was222), original EXE/UDA hashes unchanged.
- CLS partial format from original loader800BCEFC:26 groups,119 triangles.
  PASS all6440 group bytes vs native RAM after relocation,357 vertices. Terrain
  dependent Y displacement observed in RAM, not independently recalculated.
  PLRS:37 text records,23 integers each. Unknown parameters retained.
- Root Preview-Level-01.cmd opens offline74-object viewer. Chrome QA PASS for
  level viewer and existing Xena viewer; screenshots in respective browser-qa.png.
  Xena glTF verification PASS; shared .NET validator PASS,553183 comparisons.
- Next: document/package repaired setup; recover main terrain height query and
  placement semantics. Engine import still needs a normal user-context run or
  future session with permitted licensing IPC. No playable Unity level claimed.

## Previous — Unity installation (2026-10-22)

- UPDATE after user signed in: actual agent batch log resolves Unity Personal,
  Type Assigned, Expiration Unlimited. License issue is resolved.
- Agent-run batch attempt did NOT import: editor.log ends with return code 1
  immediately after changing project path, before Library/API compile or method.
  UPM log says parent process stopped. Wrapper session 18109 remained waiting and
  was interrupted with Ctrl+C; do not report import PASS. Root cause of early
  exit is not established. Agent terminal has restricted filesystem/network.
- Next: user reruns existing Install-Xena-Unity.cmd from Explorer (reuses installed
  Editor). Inspect newly generated editor.log/report and fix actual import errors.

- UPDATE 2026-10-23: user ran installer outside sandbox; Editor is now installed
  at build/toolchains/Unity/6000.3.25f1/Editor/Unity.exe. ProductVersion verified
  as 6000.3.25f1_e1dba0a9aba4. The earlier download blocker below is historical.
- Actual user-run batch attempt exited 198. Read build/unity-import/editor.log:
  "No valid Unity Editor license found. Please activate your license."
  No account access token / zero entitlements. Import/API compilation did not run.
  Unity Hub not found at C:/Program Files/Unity Hub/Unity Hub.exe.
- Next required user step: install Unity Hub, sign in and activate own license,
  then rerun Install-Xena-Unity.cmd (existing Editor reused, no redownload).
  Authentication requires the user's account; do not fabricate activation.

- User explicitly authorized installing Unity. Selected official Unity 6.3 LTS
  6000.3.25f1, release changeset e1dba0a9aba4. G: has 192,233,762,816 free bytes.
- Installation NOT completed: terminal download blocked. curl fails with
  Schannel SEC_E_NO_CREDENTIALS; Node fetch fails connect EACCES to port 443.
  No sandbox escalation requested; current policy rejects it. No installer
  downloaded and no actual Unity import report exists.
- Prepared root Install-Xena-Unity.cmd -> tools/install_unity.ps1. Running it
  outside the restricted agent terminal downloads the official signed installer,
  verifies Authenticode publisher, records SHA256, installs under ignored
  build/toolchains/Unity/6000.3.25f1, checks Windows standalone support, and runs
  the real import verifier. Existing installation is reused on rerun.
- PASS: powershell.exe -NoProfile -File tools/install_unity.ps1 -CheckOnly.
  This checks configuration only, not downloading, installation, or Editor APIs.
- Root Open-Xena-Unity.cmd opens the prepared project after installation.
- Next: execute installer in an environment with download access, activate a
  Unity license if requested, then complete actual Editor/API/shader checks.

## Previous task — Unity import preparation (continued 2026-10-22)

- User asked to continue autonomously, then requested and received texture upscale.
  Current work: import original model/animations into Unity before gameplay code.
- Editor not found in PATH, standard Hub/editor directories, user Hub editor lists
  or Unity App Paths keys. Asked asynchronously for a custom Unity.exe location;
  no answer yet. No Unity installation, Unity API compile, GPU shader compile or
  Editor run has been claimed. This is a tool availability limitation, not approval.
- Added project `unity/XenaUnity`, local UPM package `unity/Packages/com.xena.original-assets`,
  Unity 6000.3 baseline, URP 17.3.0 dependency. Source shader/asmdef/script GUIDs stable.
- `tools/export_xena_unity.mjs` stages ignored `Assets/Imported/Xena/Xena.xenamodel`
  + 6 PNGs from the verified glTF. Reflects Z/quaternion XY, reverses triangles,
  flips V. 67 nodes, 33 meshes, 426 triangles, 81 clips / 1674 frames.
- Importer source creates native Unity prefab, Mesh, Texture2D, two material sets,
  and STEP AnimationClip subassets. Preview scene menu configures URP and camera;
  preview runtime supports clips/frames/play/orientation/original-vs-4x textures.
- PASS: `node tools/check_unity_assets.mjs`. Shared C# schema/validator compiled
  using local .NET 9, 0 errors/warnings; 553,183 independently compared values
  against glTF/binary. Seven malformed cases rejected. All five C# package files
  pass Roslyn C#9 syntax. **Unity-dependent classes were NOT compiled.**
  Report: `build/unity-import/dotnet-verification.json`.
- `node tools/run_unity_import.mjs --editor PATH` is ready: actual Engine import,
  subasset totals, all 1674 frames via SampleAnimation, preview scene. Logs/report
  go under build/unity-import. Wrapper requires Editor exit 0 AND report PASS.
- Rebuild data: `node tools/build_xena_assets.mjs --unity`.
  Workflow/official API links: `docs/unity-import.md`.
- Full asset rebuild with --unity PASS; log `build/unity-import/pipeline-verification.log`.
  Missing-Editor wrapper test PASS: exit 2, no fabricated Editor report. No actual
  `editor-verification.json` exists yet. Packaged project, UPM source, staged model,
  six PNGs, instructions and .NET report: `build/Xena-Unity-import.zip` (37 entries;
  required project/package/model paths checked). Original native EXE untouched.
- Next: run real Unity import when Editor path is available; check shader/UV/culling
  visually; then extract first-room geometry/collisions. Existing native-port work
  remains unchanged. Previous asset-audit details follow.

## Previous verified asset extraction and upscale

- New scope: audit original assets and animations before starting Unity runtime.
  Existing native-port changes and the previous handoff below are preserved.
- Added `tools/audit_assets.mjs` (Node 22, no dependencies), report
  `docs/unity-resource-audit.md`; run from `decomp/`:
  `node tools/audit_assets.mjs --disc "../Xena Warrior Princess.bin"`.
- Verified all 31,535,148 archive bytes against raw disc sectors. Index: 1322
  entries, 1300 inside UDA, 222 with candidate names; 21 LEVEL.TMD candidates.
- XENA.TMD: 33 objects, 369 vertex records, 426 primitives; all vertex/normal
  ranges and primitive packet walks fit. XENA.SMA: 146 slots, 81 nonzero;
  all nonzero offsets point beyond the table with readable record headers.
- Loader `func_80069C5C` 0x8006A094..0x8006A10C relocates 146 SMA offsets
  relative to file+4; `func_80041568` selects table slots by action.
- Completed next pass: `tools/lib/xena_animation.mjs` decodes SMI and all
  Xena SMA clips. SMI has 33 x 20-byte transforms, one root at object 27;
  loader builds child lists from parent field +18. Format: `docs/formats/sma-smi.md`.
- `node tools/inspect_xena_animation.mjs`: 81 clips, 1674 frames, 7838 commands,
  no unparsed bytes. Types 1/5/6/7; no vertex deformation commands in Xena clips.
- `node tools/verify_xena_animation.mjs`: PASS. Independent interpreter executes
  3,297,597 original MIPS instructions from func_80069040; every command's consumed
  bytes and every frame's transforms, visibility and metadata match. Mesh lookup
  for visibility is stubbed. Truncation, bad offsets and cyclic SMI are rejected.
- Artifacts: `build/animation-audit/summary.json`, `slot-XX.json`, `verification.json`.
  Poses are extracted game data, keep under ignored build/.
- COMPLETED: world transforms `tools/lib/xena_transforms.mjs`: original trig table,
  Ry(y)*Rx(-x)*Rz(z), exact Q12 operation order, hierarchy composition. Independent
  original-instruction check PASS for 55,242 matrices / 14,776,398 instructions.
- COMPLETED: `xena_model.mjs` parses Xena TMD + 18 embedded STM TIMs. All 426
  triangle indices and UV corners checked; three texture pages exported.
- Visibility correction: context+0xC stores HIDDEN (renderer 0x80011940..1195C).
  Type 5 writes zero to show, one to hide. Earlier Boolean interpretation was
  inverted; decoder and independent validation now use correct semantics.
- Fresh native reference capture on unchanged xena-console.exe, modified
  2026-10-22T18:11:35.259Z (21:11 Moscow): frames 9000/9200, idle. 64 non-root
  part poses and all 66 camera-space matrices match; hidden parts are 31/32.
  Tools `capture_xena_reference.mjs`, `verify_xena_reference.mjs`; logs/metadata
  under `build/animation-reference/`. This is native runtime, not PS1 emulation.
- COMPLETED: offline viewer `build/xena-preview/preview.html`, root launcher
  `../Preview-Xena.cmd`. 81 clips, frame scrub, play, orbit/zoom, hierarchy,
  original/4x toggle. Chrome QA PASS; screenshot `browser-qa.png`.
- User additionally requested texture upscale. Built-in imagegen v1 saved to
  `build/xena-upscale/xena-atlas-ai-v1.png`, prompt.txt records exact prompt.
  Registered 18 islands, restored original alpha, final `xena-atlas-4x.png`
  2048x2048; 3x1024 texture pages + 18 individual images. Mask check: 0 mismatches.
  prepare/apply tools retain original data; registration is specific to v1.
- COMPLETED: xena-original.gltf / xena-4x.gltf + xena.bin, 33 meshes, 81 clips;
  fixed joint origins baked into float translations. 617,706 vertex comparisons,
  max error 0.910003 PS1 units. Units-per-meter=400 and 30 steps/sec are explicit
  preview assumptions. Unity import and Khronos validator not run.
- Full instructions and caveats: `docs/xena-asset-preview.md`. Local rebuild:
  `node tools/build_xena_assets.mjs --native-reference --browser` (no AI/network).
- Full rebuild with native reference and browser flags PASS; complete log
  `build/xena-assets-verification.log`. Packaged viewer + both glTF variants,
  buffers, PNGs, atlas, comparison, prompt and reports into
  `build/Xena-asset-preview.zip` (21 entries; package list verified).
- Next three tasks: verify import in Unity when Editor/importer is available;
  obtain emulator references/timing and stateful transitions; inspect first-room
  geometry, collisions and event data. Do not claim a playable Unity prototype.
- Audit changes do not modify/rebuild the native game executable.

## Current task (chakram button IDENTIFIED, VERIFIED 2026-10-22, probes on the 21:11 build; key mapping already COMPLETED and VERIFIED)

- Working build: `build/port/xena.exe` + `build/port/xena-console.exe`, live ini
  `build/port/xena.ini`, sources `port/runtime/`; rebuild `bash tools/wsl.sh bash port/build.sh`.
  Both EXE rebuilt 21:11 (`modified_ns 1792692695258731800`). `Play-latest.cmd` runs the game.
  `wslpath` is not in Git Bash; run probes there with native Windows paths directly.
- COMPLETED, VERIFIED (attack-state naming, visual, by 4x arena probe; this session can
  read images): SQUARE press -> state 0x61 is the KICK: full-window 4x contact sheet
  (`build/arenapeak/sheet.png`, frames 10514-10528, state 61 10504-10536) shows the leg
  extended horizontally ending in the dark-red boot, the arm is the counterbalance, and the
  sword is only held as in idle. CROSS press -> state 0x51 is the SWORD SLASH: the bright
  blade is in hand and extended during the forward travel (4x crops `build/arenahd/c_CR_11220.png`,
  `c_CR_11231.png`). CIRCLE = block (0x80) unchanged. The old `config.c` comment that called
  0x51 "kick" was the wrong assumption; the mouse mapping was reversed against the user's plan.
- FIX applied (config.c defaults + live `build/port/xena.ini`, in sync): keyboard
  `Cross = K` (sword), `Square = J, F` (kick; F as in the user's plan), `Circle = L, Left Ctrl`
  (block), `Triangle = Space, I` (jump), `L1 = Q, Left Shift, Right Shift` (run); mouse
  `Left = Cross` (sword), `Right = Circle` (block). Routing was already verified in the
  20:17 entry; `F` resolves in `inicheck` (no unknown-key lines). Mouse turning/capture untouched.
- Test results on the 21:11 build: targeted `modern_controls` PASS 6.6s, `mouse_turn` PASS 7.2s;
  full Windows matrix 19/19: `build/smoke-runs/20261022T181231157151Z-39732.log`.
- IDENTIFIED, VERIFIED (chakram): the game has no dedicated chakram button. The throw is
  CROSS pressed while R1 (lock/aim) is held. Probes `build/chakram3/4/5` on the 21:11 build +
  `asm/nonmatchings/game/func_800367BC.s`:
  - R1 held is the aim modifier (`andi $v1, 0x200` gates at 0x80038D8C and 0x80039F70).
  - Probes enter aim state 0x64 from an R1-held + CROSS press (visuals: chakram in hand,
    then raised overhead). The C entry at 0x80039E80-0x80039EDC (`func_8003F6AC(0x64, 0)`)
    is gated on input bit 0x800 + field 0xE2 in {0x64, 0x65}; the Cross-edge dispatch at
    0x8003C8E0 tests the same bits as combos (0x1000 alone = slash 0x51, +0x2000 = 0x5C,
    +0x800 = 0x46). Which raw pad button feeds bit 0x800 (probe name CIRCLE or the game's
    control-scheme action table, `D_800F16D0/D_800F16D8`) is an open question; the probe
    scripts for chakram3-5 were not saved, so the press->state mapping needs one more
    scripted run to close.
  - In aim state, at anim frame 0xE6 == 0x12 the spatial search `func_80070654` runs
    (0x8003978C-0x80039800) — the only non-zero store to the target global 0x800EE17C,
    followed by the fixed-point angle snap and lock-counter work (0x80039804-0x800398B4).
  - With a target locked, the Cross press dispatches at the target (target state word 0xDC
    set to 0x96/0xB4, 0x8003C918-0x8003C958). chakram5 (kickshape corridor, where kickshape
    showed an enemy present): state 64 from 9254, target global populated 9288-9384,
    chakram object visible in-scene 9296-9384 (Xena off-frame, `png/f9296.png`,
    `png/f9304.png`, `png/f9344.png`), Xena state 00 during the flight, catch pose 0x65
    9386-9422, then idle. Without a target the aim pose auto-stows after ~40 frames
    (64->65->00) and Triangle cancels it; re-pressing Cross with no target does nothing.
  - CHAKRAM.SMA is absent from the game index (`uda_find`: CHAKRAM.TMD lba 780, XENA.SMA
    lba 821 present; `build/files_29.txt` is a tool listing, not runtime truth), so the game's
    own `file not found` log lines are faithful behavior and the thrown object runs without
    its animation script (wireframe + static ring in f9344). Not fixable; nothing on this disc.
- Key-mapping consequence: no dedicated chakram key is needed; the existing mapping already
  covers the throw as R1 + Cross (E + K, WheelDown + LMB). Q stays the L1/run alias.
  Remaining open plan items (need their own evidence): Tab lock-on, E interact, C center,
  wheel zoom; RMB held-block tap-vs-hold semantics not separately tested.
- Next three tasks: (1) states 40/51/28 heading loss from the level1_fuzz diag lines;
  (2) wheel zoom via the GTE `div` register in `gte.c` rtp; (3) verify the throw through the
  mouse mapping (LMB Cross under held WheelDown = R1) with a targeted probe on the current build.
- Earlier COMPLETED, VERIFIED (mouse turning in modern mode): a mouse flick now rotates the
  requested heading (`mturn`/`aimed` in `modern_controls`, `platform.c`) instead of
  pressing Left/Right pulses, which the modern path used to discard (`!modern_active`
  gate) and which would have meant strafing anyway. A flick with no direction held
  turns on the spot; the camera-follow drift yields while `aimed`. Test hook: script
  step `frame:MOUSE=dx` (`main.c`), `--opt mouse_turn=1/2` (`config.c`).
  Measured (script `build/mouse.sh`, dumps `build/mo_before/mo_after/mo_after2`):
  a 1200-count flick during a forward run on the 16:22 build: heading 1420 -> 1420
  (0 turned); on the fixed 17:46 build: 1420 -> 2620 (+1200 exactly) and held; an
  interim window-based drift skip had eroded it to +650 - that is why `aimed` gates
  the drift (the scripted camera on that corridor never swings behind a flicked turn).
  New scenario `mouse_turn` pins this measurement into the matrix.
- Regression: targeted `mouse_turn` PASS and `modern_controls` PASS; full Windows
  matrix now 19/19 with `mouse_turn`: `build/smoke-runs/20261022T145036922735Z-16724.log`
  (exe modified_ns 1792680373908156700).
- `mouse_turn` still defaults to 0 (menu "Мышь: поворот"); capture happens on the
  first mouse click, Alt releases it. Not changed: defaulting it on would capture the
  cursor in menus, needs its own UX decision.
- The user's priorities are modern key mapping and correct mouse turning/aiming,
  alongside movement and graphics. Preserve the current agent's work on these.
- COMPLETED, VERIFIED earlier (leap/fall heading, camera-relative controls): `air_state()`
  (`0x35` leap after a running jump, `0x2A` falling) added to the steering in
  `platform.c` `modern_controls()`; buttons stay as pressed there (no forced Up).
  Measured before/after on the 16:22 build (`build/port/xena-console.exe`,
  modified_ns 1792675337359340700):
  * state 35: turn `obj+0x10E` was the game's own +10 every sampled leap frame in
    `build/jf2/log.txt`; it is 0 (heading held) in `build/ja_down/log.txt`; diag
    28/29 frames heading lost -> 1/29. RIGHT/LEFT/UP leap runs
    (`build/ja_right/ja_left/ja_up`) logged no heading loss at all.
  * state 2A in level1_fuzz: 68/114 -> 2/76 frames (transition frames only).
  * still losing heading in fuzz (pre-existing, not touched): states 40 (83/133,
    game never turns there), 51 (99/510, turned in 70), 28 (17/44) - candidate
    follow-up movement issue, not started.
- Regression: targeted `modern_controls` PASS (`build/smoke-runs/20261022T132352214860Z-37028.log`);
  full Windows matrix 18/18 on the same exe
  (`build/smoke-runs/20261022T133850725816Z-15504.log`).
- Modern key mapping, as far as it is verified (requested: run=Shift, sword=LMB, kick=RMB,
  jump=Space, movement=WASD, block=Ctrl):
  * Applied in `port/runtime/config.c` defaults and in the live `build/port/xena.ini`:
    `Cross = K`, `Square = J`, `Circle = L, Left Ctrl`, `Triangle = Space, I`,
    `L1 = Q, Left Shift, Right Shift`, mouse `Left = Square`, `Right = Cross`;
    WASD already on Up/Down/Left/Right. Rebuilt 20:17.
  * Routing verified: `build/keymap.c` (+ `build/port/keymap.exe`, links with
    `-lmingw32 -lSDL2main -lSDL2 -I.../include`) resolves every name in the ini - Space=44,
    Left Ctrl=224, Left Shift=225, Right Shift=229, W/A/S/D=26/4/22/7, K=14, J=13, L=15 -
    nothing unresolved except the pre-existing `FastForward = \``. `platform.c` polls
    `st[SDL_GetScancodeFromKey(key)]`, and `config.c`'s button bits (Triangle 0x1000,
    Circle 0x2000, Cross 0x4000, Square 0x8000, L1 0x0400) match the game's pad bits, so a
    key press produces the game's own button. Single-word names do not resolve
    ("Shift", "Ctrl", "LCtrl" -> unknown): full SDL names are required.
  * Game actions confirmed by measured behaviour on the button each key now reaches:
    L1 = the game's run (`build/flatjump`: UP walk ~26 units/frame vs UP+L1 ~48-60; the user
    also reports Q = L1 runs); Circle = block (state 0x80, static press; the user reports the
    block currently comes from RMB, which was Circle); Triangle = jump (state 0x2F, documented
    as jump in `docs/game_notes.md` line 39, screenshot-verified in an earlier session, and the
    jump probes used TRIANGLE).
  * **Not confirmed, and cannot be in this session:** which of the two attack states is the
    sword and which is the kick. Idle presses give Square -> state 0x61 (stationary) and
    Cross -> state 0x51 (travels forward ~38 units/frame); the state table in
    `docs/game_notes.md` calls both "attacks/combos" without naming them, and the game has no
    control text in the ROM (`KICK`/`HIT`/`JMP` string hits are coincidental byte patterns,
    checked in context). The shape test `build/kickshape.sh` + `build/shapecheck.py` is
    inconclusive because the camera moves during the action windows - in `build/kickshape`
    the changed-pixel fraction against the median image was 12.9% (idle) and 13.6% (Square)
    but 43.3% (Cross) and 51.9% (Circle), far above a character blob, so the mask is the
    moving scene, not Xena. This session cannot
    read images ("server has no image encoder"), so no visual identification is possible here.
    The mapping therefore puts the two *distinct* game attacks on the two mouse buttons as the
    user asked; the user can confirm in a few seconds in-game which one is the slash, and if it
    is the reversed way the fix is one line: swap `Square`/`Cross` in the ini and the defaults.
  * Target checks on the 20:17 build (`modified_ns 1792689425918287200`): `modern_controls`
    PASS `build/smoke-runs/20261022T171721589768Z-7568.log`, `mouse_turn` PASS
    `build/smoke-runs/20261022T171736652235Z-8920.log`; `build/inicheck.sh` boot has no
    "unknown key" lines. Full matrix not re-run after this build.
- Existing probe scripts in `build/` (ignored): jumpfree/jumpall/jumpshot/steercov/
  mouse.sh; steer diagnostics print at `--frames` exit via `dump_steer_diag` in `hle.c`.
- Older handoff facts still valid: harness defaults to 2 workers, refuses overlapping
  runs, every run writes its own summary under `build/smoke-runs/`; WSL interop needs
  `wslpath -w` for paths opened by the game. WSL 18/18 predates the 16:22 build, do
  not cite it as verification of a newer build.
- Next three tasks: (1) key mapping - only the visual naming of the two attack states is
  open (`0x61` stationary vs `0x51` traveling): confirm in-game which is the sword slash, and
  if the user's LMB/kick expectation is reversed, swap `Square`/`Cross` in `config.c` +
  `build/port/xena.ini` (one line, then re-run `inicheck.sh`); this session cannot read images;
  (2) states 40/51/28 heading loss (from level1_fuzz diag lines); (3) wheel zoom idea
  (GTE `div` register in `gte.c` rtp) - only after (1) and (2).

## Verified project state (2026-10-22)

- Decomp build matches the original EXE: `ninja` ended with `build/xena.exe: OK`
  (`.ninja_log` entry `build/ok`, sha1 of `build/xena.exe` recorded in `xena.exe.sha1`).
  Not re-run in this session; nothing in `src/`, `asm/`, `config/` was touched.
- Native port builds: `bash port/build.sh` → `built build/port/xena.exe` (WSL2, mingw-w64,
  SDL2 2.30.9 in `port/third_party`, regenerated `build/port/gen` from a fresh
  `build/port/syms.txt` - 10773 lines).
- Historical smoke baseline **18/18 two ways**, before the latest gameplay build, about 3 minutes at
  `--jobs 2`: Windows `python tools/smoke_test.py --jobs 2` (`build/smoke_matrix.log`) and WSL
  `.venv-linux/bin/python3 tools/smoke_test.py --jobs 2` (`build/smoke_matrix_wsl.log`). At the
  start of this session it could not run at all; see "Second" and "Third task of the day" for
  what had to be repaired first.
- Direct game runs (no harness) are still reproducible with `bash build/pacing_run.sh 0 1` and
  `bash build/regress_run.sh` (helper scripts in the ignored `build/`).
- Uncommitted work was already in the tree when this session started (`frame_lock` option,
  settings-menu entry, `probe_frame_swap`, `steady_30fps` scenario). It had **no verification**
  and its measurement tool did not work.

## Issue of this session: play runs at an uneven, too-high frame rate

ISSUE: on level 1, in play, the picture changed after 1 vblank every fourth time and after 2
vblanks otherwise (sampled 8700-8760: `1 2 2 2 1 2 2 2 ...`, 33 pictures in 61 vblanks =
1.85 vblanks per picture, ~32 logic steps/s instead of the console's 30). Motion is therefore
jittery and about 8% faster than on the PlayStation.

ROOT CAUSE: the play loop (`0x80040AF0`) flips the display buffers
(`func_800A5AC8`: exchanges the env bytes at `+0x100/+0x101`, writes the display env to
`0x1F802014/0x1F802028/0x1F80202C`) and then waits for **one** vsync
(`VSync(0)` = `0x80090FEC`, called at `0x80040CF4`). The only gate on the loop is that single
vsync. The port's GPU finishes primitives as they are written, so nothing costs the time a real
GPU/CV-idle period would cost, and the loop can land inside one vblank; the cadence then depends
on how much emulated time the frame's work happens to take - hence the 1/2 mix.
Mitigation used (already in the tree, verified here): in play (`0x800F2648 == 0x0C`) the buffer
swap waits, in emulated time, until 2 vblanks have passed since the previous swap
(`probe_frame_swap` registered in `port/recomp/hle.txt` at `800A5AC8`), option `frame_lock`
(default 1, also in the settings menu).

WHAT THIS SESSION ADDED (the actual code fix): the option that the pacing measurement needs,
`--shots-from N`, was advertised in `--help` and its value was used by the screenshot code, but
it was **never parsed** - the argument was silently dropped and screenshots were taken from
frame 0 (8760 BMPs, ~8 GB, for this scenario; the check then finds the wrong set of files).
That is why the previous session could not decide whether `frame_lock` works.
Fixed in `port/runtime/main.c` (one line, next to `--shots`).

## Test results

Pacing, level 1, script `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS,8640:UP:120`,
`--headless --frames 8760 --shots 1 <dir> --shots-from 8700`, picture duration measured as
runs of identical BMP bytes (`md5sum | uniq -c`, no numpy needed):

| frame_lock | reached last frame | halt / FATAL | picture lengths in vblanks |
|---|---|---|---|
| 0 (bug) | yes | 0 / 0 | `1 2 2 2 1 2 2 2 1 2 2 2 ... 1 2 2 2` (33 pictures / 61 vblanks) |
| 1 (fixed, default) | yes | 0 / 0 | `2 2 2 2 2 2 ... 2 1` (the trailing 1 is cut by the sample window) |

`--shots-from` now works: 61 BMPs per run named `frame_008700..008760` (before: from frame 0).

Regression (each scenario run twice, lock off vs on, same script):

| scenario | frames | reached | halt | FATAL | screenshots | level variable seen | `file not found` lines |
|---|---|---|---|---|---|---|---|
| level1_walk | 9700 | both | 0 | 0 | 1 each | `FFFF, 00010001, 0001FFFE` in both | 18 in both, identical set |
| arena (native 1x, 4:3) | 9300 | both | 0 | 0 | 1 each | `FFFF, 00010000, 0001FFFE` in both | 31 in both, identical set |

Final pictures differ between the two modes, which is expected: at 30 rather than 32 steps/s the
world state is not the same at the sampled frame. Wall time 6-8 s per run, so level loading is
not slowed.

## Blockers (status 2026-10-22, they block verification, not the game)

1. **RESOLVED** (was: the whole smoke harness was dead). `tools/dis.py` shadowed the stdlib
   module `dis`: with WSL python 3.14.4 `python3 tools/smoke_test.py` died inside stdlib
   (`AttributeError: module 'dis' has no attribute 'COMPILER_FLAG_NAMES'`,
   `/usr/lib/python3.14/inspect.py:171`; the same failure is recorded in `build/smoke_run.log`;
   later the very same shadowing failed on `import rabbitizer`). Fixed by renaming the tool to
   `tools/disasm.py` (`git mv`, no content change, nothing imported it) - verified:
   `.venv-linux/bin/python3 tools/disasm.py 80040CEC 80040CF8` disassembles and the harness
   imports fine from the `tools/` directory.
2. **RESOLVED**: the Linux venv had lost `numpy`/`PIL` after being rebuilt against python 3.14.4
   (`.venv-linux/bin/python3 -c "import numpy"` → `ModuleNotFoundError`). Installed
   `numpy 2.5.4`, `pillow 13.0.0`, `rabbitizer` into `.venv-linux`.
3. **RESOLVED** (was: the harness could not drive the Windows build from inside WSL, it handed
   `/mnt/g/...` paths to a Windows process). Now `child_path()` in `tools/smoke_test.py` converts
   what the game opens and writes with `wslpath -w`; verified both ways on 2026-10-22:
   WSL `.venv-linux/bin/python3 tools/smoke_test.py --jobs 2` → 18/18, and Windows
   `python tools/smoke_test.py --jobs 2` → 18/18. See "Third task of the day".
4. Still true: `build/port/syms.txt` can be left empty (0 bytes) by an interrupted build; a
   fresh `port/build.sh` regenerates it (10773 lines), so `build/port/gen` is fine. Do not run
   `recomp.py` by hand with an empty `syms.txt`.

## Second task of the day: the smoke matrix was not runnable at all

ISSUE: not one scenario could be run - `python tools/smoke_test.py` / `python3 tools/smoke_test.py`
died before reaching the game; every result in `build/smoke/` was a stale baseline.
ROOT CAUSE: three independent things, all confirmed by running them:
 1. the tracked file `tools/dis.py` shadows the stdlib module `dis`, which `inspect.py` uses at
    import time (`dis.COMPILER_FLAG_NAMES`), so any import chain through `logging`/`dataclasses`
    crashed before the harness could start;
 2. `.venv-linux` (rebuilt against python 3.14.4) had no `numpy`/`pillow`;
 3. even then, from WSL the harness hands `os.path.abspath()` values (`/mnt/g/...`) to the
    Windows-built game exe, which cannot open them - the game exits with
    `cannot open disc image (pass the path to your Xena .bin file)`.
FIX: renamed the tool (`git mv tools/dis.py tools/disasm.py`, content untouched, nothing in the
repo imports it); installed `numpy 2.5.4`, `pillow 13.0.0`, `rabbitizer` into `.venv-linux`.
No test was weakened and no scenario was changed. Cause 3 is **not** fixed in code - it is only
documented: run the matrix with Windows python (`python tools/smoke_test.py`).
TEST RESULT: before - import-time crash (recorded in `build/smoke_run.log`, reproduced again on
2026-10-22). After - `python tools/smoke_test.py --jobs 2` → **18/18 passed**
(`build/smoke_matrix.log`): intro_fmv 1.8s, main_menu 7.6, level1_walk 10.4, level1_fuzz 48.6,
level1_fuzz2 48.7, arena_fuzz 31.7, pause_menu 13.7, options_menu 7.7, load_menu 7.3,
software_hd 15.4, native_4x3 6.9, memcard_selftest 7.3, modern_controls 8.8, save_load 8.7,
hd_matches_native 8.5, gl_matches_native 9.4, steady_30fps 10.3, gl_8x_219_ssaa 10.7.
This is the first green full-matrix record, and it includes the pacing scenario of task 1.
REGRESSIONS: none in the game. The disassembler is now `python tools/disasm.py 80040AF0 80040B30`
and needs `rabbitizer`.

## Third task of the day: the harness could not drive the Windows build from WSL

ISSUE: from inside WSL every scenario failed at once - the game logged
`cannot open disc image (pass the path to your Xena .bin file)`, produced no output, and the
harness reported `did not reach the last frame; no screenshot` (`steady_30fps` FAIL in 0.0 s;
log in `build/smoke/steady_30fps/log.txt` at that moment). Only Windows python could run the
matrix.
ROOT CAUSE: `tools/smoke_test.py` passed its own Linux paths (`os.path.abspath()` →
`/mnt/g/Games/...`) to a Windows PE process. A Windows process cannot open a `/mnt/g/...` path,
and the same was true for everything it was told to create (`--shots` dir, `--wav`, `--memcard`,
`{out}` substitutions), so nothing came back to the harness.
FIX (`tools/smoke_test.py` only): a `child_path()` helper that converts a path with `wslpath -w`
when the harness runs on Linux and the target program is a `.exe`; applied to the disc path, the
per-scenario output directory, `--shots`, `--wav`, `--memcard` and every `{out}` substitution, in
`run()` and in both `prepare_*` helpers. The executable itself is deliberately launched with the
Linux path (WSL interop requires it; only paths opened *by the game* must be Windows ones), and
the harness keeps Linux paths for its own reading, both views being the same directory. No
scenario, check or threshold was touched.
TEST RESULT:
  before: WSL - 0/1 passed, disc could not even be opened. Windows - the only working way.
  after:  spot check from WSL: `steady_30fps` PASS 9.9 s; `save_load` PASS 8.1 s (the save is
          written by the Windows game into a Windows path, then read back by Linux python and
          re-read by the second run - both path views confirmed).
          Full matrix from WSL: `.venv-linux/bin/python3 tools/smoke_test.py --jobs 2`
          → **18/18 passed** (`build/smoke_matrix_wsl.log`: intro_fmv 1.8, main_menu 7.0,
          level1_walk 9.7, level1_fuzz 47.6, level1_fuzz2 53.2, arena_fuzz 33.0, pause_menu 14.4,
          options_menu 8.0, load_menu 7.7, software_hd 16.0, native_4x3 7.5, memcard_selftest 7.2,
          modern_controls 8.3, save_load 9.0, hd_matches_native 9.0, gl_matches_native 8.6,
          steady_30fps 10.5, gl_8x_219_ssaa 10.6).
REGRESSIONS: none. Windows invocation re-run after the change: `python tools/smoke_test.py
--jobs 2` → 18/18 passed again.
Operational correction: a detached matrix is not guaranteed to stop when `wsl.exe` returns.
The earlier background run later wrote two 1800-second timeouts into the same summary file
that the foreground run had used. Do not detach or overlap matrix runs. Run them in the
foreground and cite the unique log printed by the harness.

## Failed / rejected approaches

- Pixel comparison of BMPs with numpy for the pacing check: unusable while numpy is missing;
  byte-level comparison of the BMP files gives the same answer (the BMP writer is deterministic)
  and needs no dependency.
- Counting swaps from a log line per swap: rejected, it would need a new probe/log per frame.
- Making the port's GPU charge realistic time for primitives (the physically correct fix) not
  attempted here: bigger change, needs its own calibration measurement; the 2-vblank clamp gives
  the measured console cadence without it.

## Open questions

- Did the console ever take 3+ vblanks per picture in the heaviest scenes (then a 2-vblank floor
  is right but a cap is not the same thing as a cost model)?
- `docs/game_notes.md` still open: which stage numbers 9, 13, 14, 19, 23, 27-30 are reached in
  normal play, and how the game numbers stages ("Этап 5" for a save made on level 4).

## Next three tasks

1. Finish and document the active controls fix using the existing targeted reproduction;
   the leap change still needs its remaining directional/regression checks.
2. Continue the user's current key-mapping and mouse turning/aiming task. Confirm a
   concrete mismatch and check it before/after; preserve the movement fix already made.
3. After the active gameplay changes pass targeted checks, run one full smoke matrix and
   record its unique summary path and build identity. Only rerun in both Windows and WSL
   when interop/harness changes require it. Leave unrelated sweeps and commits for later.
