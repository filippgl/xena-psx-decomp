# Level 01: the burning gate (SLUS-00977)

What makes the wooden gate at the end of level 01 burn, written so that it can be rebuilt in Unity.
Exporter: `tools/export_gate_fire.mjs`.

Labels: **VERIFIED** means read from the MIPS code and/or confirmed with a native-port RAM dump or
screenshot. **INFERRED** means a reasonable reading that was not tested. **UNKNOWN** means not determined.
Units: PS1 world units, y points down (a larger y is lower). Unity = (x/400, -y/400, z/400) metres, the
project convention (PS1 (13760,3200,19720) = Unity (34.4,-8.0,49.3)). Clock = vblanks (60 Hz).

## Summary

* **VERIFIED.** The fire is **five camera-facing additive flame sprites** of class `fire` and **one dark
  smoke emitter** of class `smok_blk`. They stand in and behind the double gate (`@2doors`, `@2doorsr`).
* **VERIFIED.** Nothing else animates the gate:
  * no texture-page or CLUT animation on the gate model;
  * no light source and no sound in these classes;
  * the gate itself never opens (see `docs/xena-level-scripts.md`).
* **UNKNOWN.** The red-orange colour seen through the gaps between the planks. No effect actor draws
  it, so it belongs to the static level geometry. It was not decoded.

## Where the actors come from

LEVEL.BIN has a second table after the 74 static objects:

* count at +0x108, offset at +0x10C;
* 16 records of 40 bytes. Each record holds `name[16]`, then `u8 1` at +0x0C, `s32 x,y,z` at +0x10,
  `s16 rx,ry,rz` at +0x1C and `s16 sx,sy,sz` at +0x22.

The level loader `func_8006287C` (0x800630E8..0x80063108) calls `func_8006FF38(record)` for every
record. That function looks the name up in the class table `0x800C33E4` and spawns the actor at the
record position (**VERIFIED**: all 16 records match live actors in RAM, names and origins).

Gate records, level 01 (**VERIFIED**). The doors are `@2doors` (12506,3956,20246) and `@2doorsr`
(13749,4003,19773); their centre is (13128,3980,20010), Unity (32.82,-9.95,50.03).

| # | class | position | Unity | record scale | sprite size | distance to the gate centre (xz) |
|---|---|---|---|---|---|---|
| 3 | fire | (13117,3562,20515) | (32.79,-8.91,51.29) | 240,240,240 | 300x300 | 505 |
| 4 | fire | (13276,4043,19844) | (33.19,-10.11,49.61) | 240,240,240 | 300x300 | 222 |
| 5 | fire | (12999,3208,20291) | (32.50,-8.02,50.73) | 480,480,480 | 300x300 | 309 |
| 6 | fire | (12782,3562,20243) | (31.96,-8.91,50.61) | 240,240,240 | 300x300 | 417 |
| 15 | fire | (12933,3562,21003) | (32.33,-8.91,52.51) | 240,480,240 | 300x300 | 1012 |
| 2 | smok_blk | (13051,2952,21003) | (32.63,-7.38,52.51) | 240,480,240 | particles | 996 |

The same table also holds:

* `fire2` (17775,2165,17696), shown as a 480x720 sprite;
* two more `smok_blk` at (18090,2499,18700) and (17460,2755,19547);
* five `smok_sml` near (-6249..-2106, 767..1023, 12116..14891);
* two `splash` records.

All of them are in the JSON.

## Flame sprite (`fire`, class 0x15)

**Class (VERIFIED).** Descriptor `0x800C3978`: update `func_80027C60`, animation table `0x800CC2D4`,
render type +0x40 = 2 (sprite), radius +0x28 = 300. `fire2` (0x16) uses the same update with table
`0x800CC2F4`.

**Texture (VERIFIED).** `DATA\LEVELS\FIRE.STM` (LBA 757), 22 tiles `Fire0001..Fire0022`. Each tile is
4-bit, 32x72, with CLUT (800,10). The file CLUT has STP on all 15 non-zero entries. `fire2` uses
`DATA\LEVELS\01\FIRE2.STM` (LBA 3227): 10 tiles of 40x64, 8-bit, STP on all 186 non-zero entries.

**Size (VERIFIED).** `func_80087A44` takes the size from the name table `D_800C1D1C`: `fire` is
{300, 300, semi 1, abr 1}. The record scale is ignored. Names that are not in that table, or in
`D_800C1DB8`, use record +0x22/+0x24 as width and height and semi = abr = 1. That is why `fire2` is
480x720. RAM shows `w=300 h=300 semi=1 abr=1` for all five gate fires.

**Geometry and anchor (VERIFIED).** `func_800A7914` / `func_800A7A4C` build a 2-triangle TMD quad from
the template `D_800DA7C0`:

* vertices (-w/2,0,0) (-w/2,-h,0) (w/2,-h,0) (w/2,0,0), so the record position is the **bottom centre**
  and the flame rises by h (300 = 0.75 m);
* the whole STM tile is mapped on the quad. A 32x72 tile on a 300x300 quad is stretched about 2.2x
  horizontally.

**Billboard (VERIFIED, code).** `func_80071F9C` → `func_80010000` transforms only the position by the
camera matrix. It then draws the quad with the identity rotation `D_800F0790` in view space, so the
quad is **screen-aligned**, facing the camera at every angle.

**Blend (VERIFIED, code and RAM).**

* The prims are semi-transparent (TMD mode 0x36).
* The tpage in RAM is 0x002E (4-bit, abr 1) for `fire` and 0x00A8 (8-bit, abr 1) for `fire2`.
* abr 1 means framebuffer + texel, clamped per channel. Every non-zero texel is added; texel 0x0000 is
  not drawn. In Unity, use an additive shader (One One).
* **UNKNOWN.** The vertex colour that modulates the texture. The TMD flag is 0, so light calculation is
  on, but the colour was not measured.

**Animation script (VERIFIED).** The actor holds the pointer to the current script word at +0xD8, the
time of the next frame at +0xDC and the frame delay at +0xE0 (interpreter `func_8004180C`):

* `0x8008 n`: frame delay = n vblanks;
* `0x8002 n`: jump to sequence n;
* any other value: show that tile.

| class | script | frames | delay | period |
|---|---|---|---|---|
| fire | `8008 4, 0..21, 8002 0` | 22 | 4 vblanks | 88 vblanks = 1.467 s |
| fire2 / fire18 | `8008 3, 0..9, 8002 0` | 10 | 3 vblanks (4 in practice, see Timing) | 40 vblanks |
| fire3 | `8008 6, 0..9, 8002 0` | 10 | 6 | 60 |
| fire4 | `8008 12, 0..12, 8002 0` | 13 | 12 | 156 |

### Timing

* **VERIFIED.** A new frame is taken on the first game step on which the clock `D_800F16C0` (+1 per
  vblank) is at or past +0xDC. Then +0xDC = clock + delay. The port runs one game step per 2 vblanks,
  so a 3-vblank delay lasts 4 vblanks.
* **VERIFIED (RAM).** Over 31 dumps (frames 9400..9460) all six sprite actors advanced exactly one tile
  every 4 vblanks with a fixed phase.
* **VERIFIED (code and RAM).** The UVs written in a step are those of the tile index that the actor held
  *before* the step (0x800418F8..0x80041A70). The visible tile is therefore `stored − 1`. The cyclic
  order is unchanged. RAM: 186 of 186 prim UV sets equal tile[stored−1].
* **VERIFIED (code).** Start phase: on its first update `func_80027C60` runs the interpreter
  `rand(0,8)` times. Each fire therefore starts on a random tile; the gate fires showed tiles
  12/11/9/6/8 at frame 9400.
* **VERIFIED (code).** The fire actors are static: no parent actor, and the lifetime +0x118 is not used.

### Visibility (VERIFIED code, INFERRED meaning)

`func_80071850` sets actor +0x144 bit 0x40 when the view depth is between `D_800EE184` (10) and the
camera far limit +0x6C (16108 in the capture). It sets bit 0x01 when the actor is inside the view cone,
using the radius +0x28 = 300. A sprite is drawn only when both bits are set (`func_80071C60`). In the
captures, actors out of view lacked bit 0x01 but kept animating.

## Smoke (`smok_blk`, class 0x1F; `smok_sml`, 0x1E)

`func_8002845C` creates one particle emitter per actor with `func_800B3108` (the same family as the
blood emitter in `docs/xena-hit-effects.md`). The emitter is moved to the actor position on every update.
All values below are code immediates, and the exporter checks them in `xena.exe`.

| field | value | effect |
|---|---|---|
| texture | `DATA\LEVELS\SMOKE3.STM` tile `smokeE`, 64x64 4-bit, CLUT (1008,6) | preloaded shared STM #0 (`D_800F25E4`) |
| slots | 6 | at most 6 particles alive |
| emission | 30000 steps | in practice endless |
| delay / per spawn | 5 (+rand·5) steps / 2 particles | |
| spawn box | x ±200, z ±200, y 0 | |
| direction / speed | 0 / 10 + rand·15 | the zero direction gives zero initial velocity (INFERRED, matches RAM) |
| gravity | −2 per step (upwards) | |
| velocity limit | 50 per step | |
| life | 50 + rand·60 steps | |
| blend | `smok_blk` abr **2** (B − F, subtractive = dark smoke); `smok_sml` abr 1 (additive) | `GetTPage(abr = desc+0x70)` in `func_800B192C` |
| draw distance | classes 0x1C/0x1E/0x1F use near + ¾·(far − near) (12276 in the capture) | `func_80071850` |

The gate emitter (actor 0x8014BC60, emitter 0x801E171C) was captured in RAM (**VERIFIED**):

* 6 slots, emission 30000, gravity −2, abr 2.
* Pairs of particles rise from about record y + 400 at −50 per step. The newest pair was at −46 after
  23 steps of −2.
* Lives were 19..104 steps.

**UNKNOWN.** The size of the textured particle quad, because the textured draw path was not decoded.
In the screenshots the dark smoke is hard to see against the night rock.

## Light and sound

* **VERIFIED.** `func_80027C60` and `func_8002845C` call no light, palette or sound routine.
* **VERIFIED.** The fire, fire2 and smoke descriptors have no sound list (+0x3C = 0).
* **VERIFIED.** The fire scripts use only the opcodes 0x8008 and 0x8002. The interpreter has a sound
  opcode (`func_800847C0`), but these scripts do not use it.
* **VERIFIED.** The level lighting is the static per-vertex `LEVEL.LGT`.
* **UNKNOWN.** Ambient level audio near the gate was not traced.

## Verification

Executable: `build/port/xena-console.exe`, 2026-10-22 21:11:35, sha1 C71C6BB06864. Settings: headless,
level-01 start `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS`.

Xena was teleported for frames 9000..9008 to x=12000, y=3900, z=19950 with yaw 1024:

* pokes `0x801BB494/98/9C:...:4` (the position vector at actor +0x5C) and `0x801BB4A2:1024:2`;
* `0x800F2774:3:1` every 4 frames, to stop new bandits;
* the y value must be poked on several frames, otherwise she drops from the old height and dies.

`node tools/export_gate_fire.mjs --root . --out build/gate-fire/export --verify-ram build/gate-fire/capture`
checks the following against 31 RAM dumps (frames 9400..9460, every 2 frames):

| check | result |
|---|---|
| LEVEL.BIN effect records = live actors (name + origin) | 16/16 |
| sprite w/h/semi/abr = the rule above | 186/186 |
| prim UVs = tile[stored − 1] | 186/186 |
| tpage abr = 1 | 186/186 |
| frame timing (one tile per 4 vblanks, fixed phase) | 6/6 actors |
| gate smoke emitter: 6 slots, emission 30000, gravity −2, abr 2 | yes |

Artefacts:

* screenshots `build/gate-fire/overview/gate_9400.png` (2x, hardware renderer) and `gate_9420.png`;
* the camera fly-in sheet `teleport_sheet_9150_9600.png`;
* RAM dumps and BMPs in `build/gate-fire/capture/`;
* the export in `build/gate-fire/export/`:
  * `fire/fire_00..21.png` and `fire/fire_strip.png`;
  * `fire2/fire2_00..09.png`;
  * `smoke3/smoke3_00.png`;
  * `gate_fire.json`.

## Unity notes

* Make each flame a camera-facing quad of 0.75 x 0.75 m (300 units), pivoted at the bottom centre on
  the record position.
* Use an additive material with point filtering. Draw the 22 tiles in script order, each for 4/60 s
  (period 1.467 s). Give each fire a random start tile 0..8. There is no interpolation: these are frame
  flips.
* For the smoke, build a 6-particle system with dark (subtractive-looking) puffs:
  * 2 particles every 5..9 steps of 1/30 s;
  * spawn about 1 m below the marker, ±0.5 m in x and z;
  * accelerate upwards at 2 units per step², capped at 50 units per step;
  * life 50..109 steps.
  * The quad size is unknown; match it to the reference captures.
* Do not add a light or a sound unless a later audit finds one.
