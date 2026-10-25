# Combat hit effects: flash, ring, sparks, blood (audit 2026-10-25)

Follows `docs/xena-damage-audit.md` and `docs/xena-hit-reactions.md` (they name the calls
`func_800291A0` "spark" and `func_800B2F84` "blood particle"; this file describes what those calls draw).
Labels: **VERIFIED** = read from the MIPS at the cited address and, where marked "capture", seen in a
native-port RAM dump or screenshot; **INFERRED** = reading not checked dynamically; **UNKNOWN** = not found.

Sources: `asm/nonmatchings/game/func_800291A0.s`, `func_80028EDC.s`, `func_80028E84.s`, `func_80028650.s`,
`func_80077260.s`, `func_800776C8.s`, `func_80076E5C.s`, `func_80076F94.s`, `func_8001B460.s`,
`func_800367BC.s`, `func_80070DFC.s`, `asm/psyq.s` (`func_800B2F84`, `func_800B192C`, `func_800B1DC4`,
`func_800B33C8`, `func_800B3760`, `func_800B73F0`, `func_800B5550`), colour table `D_800C0F4C` in `xena.exe`.

## 1. Summary

| Effect | Primitive | Blend | When | Count / life (game steps) |
| --- | --- | --- | --- | --- |
| Flash star (kind 0) | 2 x 5 POLY_G4 spikes, screen space | additive (abr 1) | every spark call | 1, life `min(d/4+4, 12)` |
| Shock ring (kind 1) | elliptical POLY_G4 ring | additive | spark call with d >= 26 | 1, life `trunc(flashLife*5/8)` (6 or 7) |
| Spark discs (kind 2) | elliptical POLY_G4 disc, grey centre | additive | every spark call | `clamp(d/4-1, 0, 6)`, life rand(10, 20) |
| Blood streaks | LINE_G2 red head -> black tail | opaque | unblocked damage | emitter of 40 slots, 8 / 10 / 20 emission steps, particle life 5..10 |

All VERIFIED (code + capture). **No texture, sprite, model or VRAM page is used**: `func_800B73F0` emits
GP0 0x38|2 (`0x800B7504`) untextured gouraud quads and the blood emitter is created with a NULL texture
descriptor, so `func_800B192C` builds GP0 0x50 lines (`0x800B1C34`). There is nothing to extract from
`XENA.UDA`; `tools/export_hit_effects.mjs` instead rasterises the original formulas (section 7).

**Not present** in the hit paths: screen shake, full-screen flash, decals, hit-stop. The intake code of
Xena (`0x800371AC..0x800383F8`) and of the bandit (`0x8001D588..0x8001F500`) calls only the effects above,
pad rumble (`func_8002770C` / `func_80027790` -> `func_800AA354`), sounds, the mover and state changes
(call list checked; INFERRED that none of the remaining callees shakes the camera). Capture: the camera
matrix translation (`*D_800F10EC + 0x4C + 0x14`) changes monotonically through the hits in `bt` and `sD`
(no oscillation), and the game-step counter `D_800ECEA0` never stalls (VERIFIED, capture).

## 2. Spawn rules (who, when, which colour)

`func_800291A0(pos, d, type)`: `pos` = victim `+0x18` (the contact point), `d` = victim `+0x12`.
The contact point is written by the contact test `func_80070DFC` (`0x800711A4..0x800711B4`): the attacker's
weapon/limb contact point in world space when it lies inside the victim's radius + 50 and height range
(VERIFIED). So every effect is emitted at the weapon tip / foot, not at the victim's centre. Measured
heights above the feet: sword on a bandit 418, kick 733, bandit on Xena 447 (captures `sD`, `kD`, `bt`).

| Call site | Victim | Condition | Type |
| --- | --- | --- | --- |
| `0x8001D7AC` | bandit (`func_8001B460`) | not guarding (sp+0x268 == 0), `+0x14 == 0`, `d >= 4` | 2 (yellow) if attacker is Xena (class 0x5A) in state 0x61, 0x62, 0x5A, 0x60, 0x49, 0x55, 0x3F, 0x5C or 0x5F; else 0 (red) |
| `0x8001E2F8` | bandit | guarding / hit timer (damage discarded), bandit state 0x80..0x83, `d != 0` | 1 (blue) |
| `0x80037AAC` | Xena (`func_800367BC`) | block sector accepted (states 0x80..0x83), called with the **raw** d before `-40` | 1 (blue) |
| `0x80037B88` | Xena | after the damage clears, `d >= 4`, `+0x14 == 0` | 0 (red), any attacker |
| `0x8003113C` | actor of `func_800305E0` (other actor type, not traced) | `d >= 4`, `D_800F0BD7 == 0` | 1 |

0x61/0x62/0x5A are kicks 1..3 and 0x55 the kick-kick-sword finisher (`xena-combat-audit.md`); the other
yellow states are the same list that gives the +10 kill bonus in the damage audit (move names UNKNOWN).
Sword 51/52/3A, the one-hit move 0x42 and the chakram give red. Captures (VERIFIED):

| Run | Hit | d | Flash life | Ring | Sparks | Colours |
| --- | --- | --- | --- | --- | --- | --- |
| `bt` 9148 | bandit -> Xena | 15 | 7 | no | 2 (life 18, 13) | type 0 |
| `swB` 9034 | bandit -> Xena (in sword 51) | 30 | 11 | 6 | 6 | type 0 |
| `blkB` 9034 | bandit -> Xena blocking (0x81) | 30 raw, 1 dealt | 11 | 6 | 6 | type 1 |
| `sD` 9086 | Xena sword 51 -> bandit | 30 | 11 | 6 | 6 | type 0 |
| `kD` 9086 | Xena kick 61 -> bandit | 35 | 12 | 7 | 6 | type 2 |
| `kD` 9154 | Xena kick 62 kills bandit | 40 | 12 | 7 | 6 | type 2 |
| `chD` 9132 | chakram (actor class 0x20) kills bandit | 300 (`+0x14`) | 12 | 7 | 6 | type 0 |
| `swA` 9036 | Xena 0x42 kills bandit | 10000 | 12 | 7 | 6 | type 0 |

The chakram damage of 300 is a single observation (attacker `+0x24` = 0x80151930, class 0x20).

Blood (`func_800B2F84`), stored in the victim `+0x104` so only one emitter per victim exists at a time:
- Xena `0x80037BB0..0x80037D64`: final `d >= 2` and `+0x104 == 0`. A block leaves `d = 1`, so **blocked hits
  never bleed** (VERIFIED code; capture `blkB`: no emitter).
- Bandit `0x8001E920..0x8001EB48`: `+0x14 >= 4` and `+0x104 == 0`; emission 20 steps if the hit kills
  (`s3`), else 10 (`0x8001EA84..0x8001EA90`). Correction to the damage audit: 10/20 is the number of
  emission steps, not a particle count.
- Blood colour: Xena always (255,0,0); bandit (255,0,0), or (0,128,0) when actor `+0x38` (animation family)
  == 2 (`0x8001EA2C..0x8001EA6C`; which enemy has family 2 is UNKNOWN).

## 3. Burst pool and update (`func_80028650`, once per game step) — VERIFIED

Pool `D_800F1898`, 12 slots of 0x30: `+0` pos, `+0xC` velocity, `+0x18` three colours (from `D_800C0F4C`),
`+0x24` s16 tilt, `+0x26` size (= initial life), `+0x27` life, `+0x2C` kind. `func_80028E84` takes a free
slot, or a kind 2/3 slot with life < 5; when all 12 are busy the effect is dropped. `func_80028EDC` sets
`tilt = rand(0x100, 0x2AA)` with random sign (`rand(0x100,0x155)` for kind 3). Life drops by 1 every step;
the pool is updated from the game step `func_80076888` (`0x8007698C`) in game states 9 and 12. The port
runs one step per 2 video frames (`D_800ECEA0` +1 per 2 frames in every capture).

Projection: `func_800B5550` = RotTransPers with the camera matrix; `w = 256 - SZ/32` scales the flash and
the discs (nothing is drawn when `w <= 0`, i.e. SZ >= 8192); SZ/32 is also the OT depth.

Colour table `D_800C0F4C` (RGB, VERIFIED from `xena.exe`):

| type | c0 (star tip) | c1 (star sides / ring inner) | c2 (star centre / ring outer) |
| --- | --- | --- | --- |
| 0 hit | (32,0,0) | (255,32,0) | (0,0,0) |
| 1 block | (0,0,32) | (0,32,255) | (0,0,0) |
| 2 special | (32,32,0) | (150,150,32) | (0,0,0) |

**Flash (kind 0)**, `size = life0 = min(trunc(d/4) + 4, 12)`; no motion.
- Colour factor `f = min((life+2)*32, 255)` applied to all three colours (full while life >= 6, then fades).
- Radius `R = (w * trunc(trunc(life*192/size) * 7/32)) >> 8`: 42 px at birth for w = 256, linear to 0.
- Star 1 (`func_80077260`): 5 spikes, angle `tilt + 64*life` (turns 5.6 deg per step), each spike a quad
  tip (R, c0) - sides (angle +-0x1D0 = +-40.8 deg, R/8, c1) - centre (0, c2).
- Star 2: same, angle +0x199 (36 deg), radius `trunc(3R/4)`, half-width 0x320 (70.3 deg).

**Ring (kind 1)**, only `d >= 26`; life `trunc(life0*5/8)`.
- `func_800776C8`: inner radius `((size-(life-1))*6) & ~1` = 6, 12, ... px; thickness `(life*4)|2`;
  **screen pixels, not depth scaled**. Inner edge c1, outer edge c2, factor `min(life*14, 255)`.
- Shape: x = sin(a)*r, y = cos(a + tilt)*r (a sheared circle = tilted ellipse); 4/6/8/12/16 segments for
  inner+thickness < 6/12/20/40/else; spin `(frame & 0xFFF) << 4`.

**Spark discs (kind 2)**, count `clamp(trunc(d/4) - 1, 0, 6)`.
- Velocity `rand(-18, 18)` per axis (world units per step), life `rand(10, 20)` inclusive (`func_800B4350`).
- Motion: `pos += vel`; every second step (`D_800ECEA0 & 1 == 0`) each component moves 1 toward 0 and
  stops at -1..1. **No gravity.** Capture `bt` 9148..9182: v (6,-12,-1) -> (5,-11,-1) -> ... -> (1,-4,-1).
- Disc radius `(w * trunc(trunc(((life>>1)+4)*256/size) * 5/32)) >> 8` (27..35 px at birth for life 20..10,
  7..15 px in the last step, w = 256); centre grey `trunc(min(life*8+16, 255) * 99/256)`, edge black.
  The table colour is **not** used for discs, they are grey for every type.
- Tilted ellipse as for the ring, spin `(frame & 0x3FF) << 6`.

`D_800F0CF2 == 2` kills kinds 0 and 1 immediately (meaning UNKNOWN). Kind 3 is the breath puff of
`func_80029030` (every 64 steps on levels 20..22), not a hit effect.

## 4. Blood emitter (`func_800B2F84` -> `func_800B192C`, update `func_800B1DC4`, draw `func_800B33C8`)

Descriptor values (VERIFIED code; Xena `sp+0x48` at `0x80037BC0..0x80037D60`, bandit `sp+0x1A8` at
`0x8001E944..0x8001EB44`), particle behaviour VERIFIED code + capture (`sB`, `kD`, `swA`):

| Field | Value | Effect |
| --- | --- | --- |
| desc+0x00 | 40 | particle slots |
| desc+0x10 | 8 Xena / 10 bandit / 20 bandit kill | emission steps (`+0x50`, -1 per step) |
| desc+0x12, +0x14 | 1, 1 | delay after a spawn = 1 step, so particles spawn on every 2nd step |
| desc+0x16, +0x18 | 2, 8 | particles per spawn = 2 + trunc(rand*8) = 2..9 |
| desc+0x34..0x3C | 30, 30, 30 | spawn box: emitter position +-15 per axis |
| desc+0x1C / +0x28 | v-100 / v+200 | direction per axis = (v-100) + rand*(v+200), normalised |
| desc+0x40, +0x42 | 5, 15 | speed 5 + trunc(rand*15) = 5..19 units/step |
| desc+0x44, +0x46 | 5, 6 | particle life 5..10 steps |
| desc+0x4A | 3 | gravity +3/step^2 on y (PSX y is down) while abs(vy) < 200 (desc+0x4E) |
| desc+0x4C | 0 | no drag |
| desc+0x52.. / +0x55.. | (255,0,0) / (0,0,0) | line head colour / tail colour |
| desc+0x70 | -1 | opaque (no semi-transparency) |

- `v` = the victim's rotation (`RotMatrixYXZ(-rx, ry, rz)`) applied to (0,-200,-500) for Xena,
  (0,-80,-500) for a bandit hit and (0,-170,-500) for a bandit kill. For Xena this is her facing direction
  plus upward (capture `sB` 9036: first velocity (5,-4,9), Xena forward (0.57, 0.82)); for the bandit the
  rotation source sp+0x238 is INFERRED to be its own. The `v+200` range (not 200) makes the cone
  asymmetric; that is what the code does.
- Each particle draws one LINE_G2 from its position (red) to its previous-step position (black), so the
  streak length is one step of motion; not faded over life; skipped when SZ/32 < 9 (`0x800B3588`).
- The emitter follows the victim: every step its position = victim part `+0x12C` + the offset `+0x120`
  stored at the hit (`0x80037C30`, `0x8001D7E8`). The game keeps it alive (flag 0x8000) while any particle
  lives and frees it with `func_800B3250` afterwards (`0x8001D8A8`).
- Capture counts: Xena hit `sB` alive 2, 2, 4, 4, 13, 13, 22 then decays, last particle at step +18;
  bandit kill `swA` emission 20 steps, about 20 alive, gone after about 30 steps.

## 5. What the screenshots show

Crops (2x, 16 steps each, from `build/port/xena-console.exe`, 2026-10-22 21:11:35, 320x240, software,
perspective=0, texture_filter=0): `build/hit-effects/strip_sword_hit_red.png`,
`strip_kick_hit_yellow.png`, `strip_kick_kill_yellow.png`, `strip_xena_block_blue.png`,
`strip_xena_hit_d15.png`, zoom `swA_zoom.png`. Visible: an orange (sword), yellow (kick) or blue (block)
10-point star with a near-white core where the additive discs overlap, large faint grey tilted ellipses
(the discs) drifting away, a thin ring for d >= 26 and small red specks/streaks that fall from the contact
point. In these `--shots` runs an effect appears in the screenshot about 6 video frames after the first
RAM dump that contains it (observation of this port's capture timing only).

## 6. Reproduction

From `decomp/`, `python build/hit-effects/run_bt.py NAME END SCRIPT FIRST POKES...` runs
`build/port/xena-console.exe --headless --render-scale 1 --aspect 4:3 --opt renderer=software
--opt perspective=0 --opt texture_filter=0 --opt modern_controls=0` with the level-01 start
`7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS`, shots every frame and RAM dumps every 2 frames;
`python build/hit-effects/scan.py DIR` lists the pool and emitters. Bandit = actor 0x80151270 (class 0x95);
its position pokes are at `0x801E09BC/C0/C4` (frame 8998).

| Run | Bandit poke (x, z) | Extra poke | Script after the start |
| --- | --- | --- | --- |
| `bt` | -7571, -17638 | - | `9000:UP+SQUARE+TRIANGLE:8,9040:SQUARE:8,9060:SQUARE:8,9080:SQUARE:8` |
| `swA` | -7571, -17638 | - | `9000:CROSS:8,9020:CROSS:8,9040:CROSS:8,...` |
| `swB` / `blkB` | -8062, -18358 | - | CROSS presses / `9000:CIRCLE:250` |
| `sD` / `kD` | -8062, -18358 | `9068:0x801BB4A2:0x1B0:2` (Xena rotY) | `9070:CROSS:8,9090:CROSS:8,...` / same with SQUARE |
| `chD` | -8062, -18358 | same | `9070:R1:120,9090:R1+CROSS:8` |

y poke is -1837 in all runs. Artefacts are under `build/hit-effects/<run>/` (`scan.txt`, `ram_*.bin`,
`frame_*.bmp`).

## 7. Exporter

`node tools/export_hit_effects.mjs --root . --out DIR [--depth 256]` checks 15 instruction words in
`xena.exe` (every constant it uses), reads the colour table and writes `hit_effects.json` (all rules above)
and strips (96x96 per step, additive colour, alpha = max channel): `flash_<type>_d15/d30.png`,
`ring_<type>_d30.png`, `spark_disc_life10/20.png`. The strips are the screen-space shapes only; motion,
depth scale and the random tilt/velocity are described in the JSON. Test output:
`build/hit-effects/export/` (depth 200), preview `build/hit-effects/export_preview.png`.

## 8. Unknowns

- Meaning of `D_800F0CF2 == 2`, of the yellow states 0x60/0x49/0x3F/0x5C/0x5F and which enemy uses
  family 2 (green blood).
- Effects of the other `func_800B2F84` / `func_800291A0` callers (other enemy types, `func_800305E0`).
- Exact behaviour on real hardware (all captures are from the native port).
