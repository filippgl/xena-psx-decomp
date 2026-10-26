# Level 01: how the water moves (SLUS-00977)

This document answers one question: does the water in the original level 01 move, and how? It covers
the river surface `@_stream` (object 14), the waterfall `@rivlet2` (object 40), the falling-water faces of
the terrain (export page 9, u 0..64, v 80..190) and the `splash` effect actors.
Exporter: `tools/export_water_animation.py`.

Labels: **VERIFIED** means the claim was read from the MIPS code and confirmed with native-port RAM, VRAM or
screenshots. **INFERRED** means a reasonable reading that was not measured directly. **UNKNOWN** means
not determined. Units are PS1 world units, with y pointing down.

Clock: all rates below are **per game step**. A game step is one pass of the main loop. In the native-port
captures one game step took 2 vblanks (30 Hz): the wave phase and the CLUT ring both advanced exactly once
every 2 frames (**VERIFIED**). The rate on real hardware under heavy load is **UNKNOWN**.

## Summary

| Surface | What moves | Function | Rate | Blend |
|---|---|---|---|---|
| Falling water on the terrain, texture `NiagraL` (export page 9) | **CLUT rotation**: entries 128..178 of CLUT (768,25) | register `func_800A3C5C`, update `func_800A3D8C` | +1 entry per step; ring of 51 entries, period 51 steps (102 vblanks, 1.70 s) | opaque (TMD mode 0x34, no STP texels) |
| Waterfall `@rivlet2`, texture `rivlette` | **CLUT rotation**: entries 128..178 of CLUT (768,28) | same | same | semi-transparent, abr 1 (B+F, additive) on the 2124 STP texels; 6068 texels are 0x0000 and are not drawn |
| River/lake `@_stream`, texture `water` | **Vertex Y wobble** of the mesh. The texture, UVs and CLUT are static | `func_80029E70` | phase +0x200 per step, period 128 steps (256 vblanks, 4.27 s); amplitude ±64 units | semi-transparent, abr 1 (additive), all 16 CLUT entries STP |
| `splash` actors (2) | **Particle emitter** of untextured LINE_G2 streaks | `func_80027DFC` → `func_800B2F84` | 15 particles every 2nd step, 30 slots | opaque lines (abr −1) |

The following were checked and are **not** used for water in level 01 (**VERIFIED**):

* no UV scrolling of any prim: the UV-scroll list is empty, and the prim bytes of 322 mesh chunks were
  identical across five dumps over 100 frames;
* no per-frame MoveImage, LoadImage or DR_MOVE copy of a texture strip: across 64 VRAM dumps, only the
  two framebuffers and the two CLUT rows 25 and 28 changed;
* no vertex wobble on `@rivlet2` or on the terrain;
* no animated sprite.

## 1. CLUT rotation: `NiagraL` and `rivlette` (VERIFIED)

### Code

* **Name table `D_800DA980`.** 12-byte records: `char *name`, then `s16 x, y`, `s16 1, 0`. The loader fills
  in the CLUT position of each named STM image. Index 2 is `NiagraL` and index 3 is `rivlette`. The other
  entries (`XEhair`, `BTFace`, `fireshin`, `bathe2`, `Strmtop`, `wave1`, `wave2`, `Tport`, `BTBack`…)
  belong to other levels and characters.
* **Registration.** In the level loader `func_8006287C`, the level-1 case of `jtbl_80017A2C` starts at
  `0x800641F4`. It registers two slots:
  * `func_800A3C5C(type 0, first 0x80, last 0xB2, rate 1, "NiagraL")`;
  * the same call for `"rivlette"`.
* **Slot setup.** `func_800A3C5C` allocates a slot of 0x418 bytes (`D_800F15F0[4]`, count `D_800F06AC`). It
  stores type, first, last, name index and rate (+0x10, also the counter at +0x14). It then reads the
  256×1 CLUT row back with `StoreImage` into two copies at slot +0x18 and +0x218.
* **Update.** `func_80064D0C` calls `func_800A3D8C` once per game step. For type 0:
  * the counter is decremented, and when it reaches 0:
    * `s = c[last]`;
    * `memmove(&c[first+1], &c[first], (last-first)*2)`;
    * `c[first] = s`;
  * the copy at +0x218 is then sent with `LoadImage` to rect x = 768 + first (made even),
    y = row, w = last − first + 1 (made even, so 52), h = 1;
  * the counter is reset to `rate` (1), so the rotation runs on every step.
* **Direction.** The colour of entry *i* moves to entry *i*+1, and entry 128 takes the old colour of
  entry 178. Whole 16-bit words move, so the STP bits travel with the colours.
* **Type 1 (other levels only).** Type 1 is a brightness pulse over all 256 entries.

### Textures (LEVEL.STM, VERIFIED)

| Image | bpp | VRAM image | CLUT | Texels | Used by (RAM prims) |
|---|---|---|---|---|---|
| `NiagraL` | 8 | (320,336), 64×128 texels | (768,25) | 6057 of 8192 texels in 128..178 | 188 prims, tpage 0x0095 (page (320,256)), CLUT 0x0670, mode 0x34, u 0..62, v 80..206 (terrain object 1 and object 39) |
| `rivlette` | 8 | (800,384) | (768,28) | 2279 in range; 47 of the 51 cycled entries have STP | 20 prims of `@rivlet2`, tpage 0x00BC (abr 1), CLUT 0x0730, mode 0x36, u 64..126, v 128..254 |

### Apparent flow

* **VERIFIED (texture data).** Inside the cycled range the palette index mostly grows by +1 per texel
  along +v. `NiagraL` has vertical neighbour steps of +1: 1639, −1: 379 and 0: 2492. `rivlette` has
  +1: 533, −1: 129 and 0: 800.
* **VERIFIED (geometry).** On the faces, v grows downwards: 162 of 176 `NiagraL` terrain triangles and 20 of
  20 `@rivlet2` triangles.
* **INFERRED.** The colour bands therefore run **downwards** at about **1 texel per step** (≈30 texels/s)
  and repeat every 51 steps. This is not a rigid scroll: no texel and no UV moves, so the speed is set by the
  local index gradient. The `fallsnb` screenshots show a median of +1 screen pixel per step downwards in the
  top-left curtain. That measurement is weak because the error difference was small.

## 2. `@_stream` vertex wave (VERIFIED)

* **Object list.** Objects with flag 0x80 (name token `stream`) are listed in `D_801013D0` (count
  `D_800F17E0`) by `0x800BB108..0x800BB130`. In level 01 this is object 14, which the loader splits into
  8 chunks with 36, 36 and 6×39 vertices (306 in total).
* **Phase.** `func_80029E70` runs once per step from the main loop (`0x8006675C`). It does
  `phase(D_800F0B08) += D_800F0A68`:
  * the step is 0x200;
  * it is 0x32 when rocking objects `D_800F0B40` exist; there are none in level 01 (RAM 0);
  * the phase starts at 0x4000 after the first call.
* **Height.** For every vertex: `y = base + trunc(sin(phase + ((x & 7) << 13)) / 256)`.
  * `sin` is `func_800AD94C`: 65536 per turn, 1.0 = 16384, a table of 258 entries at `0x800DAB18`.
  * The amplitude is ±64 units (0.16 m at 400 units/m).
  * `base = D_800EC480` is 0 in level 01.
  * The period is 128 steps.
* **Not a travelling wave.** The phase offset depends only on the low three bits of the vertex x. Each
  vertex therefore bobs in one of 8 phase groups 45° apart, which reads as a shimmering, choppy surface.
* **Static texture.** The texture, UVs and CLUT of `@_stream` do not change. The surface is drawn additive
  (abr 1, all 16 CLUT entries STP).
* **Level 04 only.** A z deformation `0x8002A334..0x8002A3F4` runs only when `D_800F2772 == 4`. It was not
  decoded.

## 3. UV scroll path (not used in level 01, VERIFIED)

`func_80029E70 0x8002A14C..0x8002A29C` scrolls V for the objects in list `D_80100AB8`:

* V of every prim corner (+5, +9, +0xD, +0x15, +0x19, +0x1D) decreases by 1 per step for 126 steps, then
  jumps by +126;
* at initialisation, V values below 127 are clamped to 127.

The list holds flag-0x10000 objects. Those flags come only from the name tokens `terr_lav`, `terr_lv` and
`lavafal`, so the path is for lava. In level 01, `D_800F17AE = 0` in every dump.

## 4. `splash` actors (VERIFIED code, RAM)

Two LEVEL.BIN effect records, class 0x1C:

* (1249,−2883,−12923) at `@rivlet2`;
* (7273,4212,4793) at the foot of the lower `NiagraL` faces by the river.

**Update `func_80027DFC`.**

* On the first update it calls `func_800847C0(19, 70, 31, 0, pos)`. **INFERRED** that this is a sound,
  because it is the routine of the script sound opcode. The meaning of the arguments is **UNKNOWN**.
* It keeps one emitter in actor +0x104. On every step it sets emitter flag 0x8000. When emitter flag 0x80 is
  clear (**INFERRED**: the emitter is spent), it frees the emitter with `func_800B3250` and creates a new one
  in the same step.

**Emitter descriptor (immediates at `0x80027EA8..0x80027F94`).** Field meanings follow
`docs/xena-hit-effects.md`.

| Field | Value |
|---|---|
| slots | 30 |
| emission | 30000 steps (endless) |
| delay after a spawn | 1 + rand·1 = 1, so a spawn on every 2nd step |
| particles per spawn | 15 |
| direction | (−10 + rand·50, −100, −10 + rand·50), normalised: up, leaning to +x/+z |
| spawn box | x ±250, z ±250, y 0 |
| speed | 5 + rand·80 per step |
| life | 10 + rand·15 steps |
| gravity | +7 per step² (downwards) |
| velocity limit | 200 |
| drag | 0 |
| desc+0x50 | actor y + 100 (meaning **UNKNOWN**) |
| head colour | (80,190,220) |
| tail colour | (30,150,200) |
| blend | abr −1, opaque |

**Drawing.** The texture is NULL, so each particle is a LINE_G2 from its position to its previous position.
RAM confirms two such emitters (headers near `0x801E1000` and `0x801E99A0`, colours, gravity 7, slots 30).
Their +0x58 value equals record y + 100 for both actors.

## Verification

**Setup.**

* Executable: `build/port/xena-console.exe`, 2026-10-22 21:11:35, sha1 C71C6BB06864.
* Settings: `--headless --render-scale 1 --aspect 4:3 --opt renderer=software --opt texture_pack=0
  --opt perspective=0 --opt texture_filter=0 --opt modern_controls=0`.
* Level-01 start: `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS`.
* No input after that.
* Xena was teleported on frames 8800..8803, by poking the position vector at `0x801BB494/98/9C:4`
  (actor `0x8014E4E0` +0x5C) and yaw `0x801BB4A2:2`.
* The camera has settled by about frame 9240.

**Artefacts.** They are in the worktree under `build/water-anim/`, which is git-ignored. The probe scripts
are in `build/water-anim/probe-scripts/`.

| Capture | Setup | Result |
|---|---|---|
| `river/` | (9906,3600,7291), yaw 1024; shots every frame 9200..9263; `--verbose` VRAM BMPs; 32 RAM dumps | VRAM: only framebuffers and CLUT rows 25/28 x 896..946 changed (`river/vramdiff.txt`). Stream texture/CLUT, `NiagraL` and `rivlette` texels: 0 changes. Ring shift = +1 entry per 2 frames, 31 shifts in 62 frames |
| `wrap/` | dumps 9200, 9202, 9302, 9456 | CLUT ring equal after 51 steps; wave Y equal after 128 steps |
| `river2/` | 9400..9463, static camera | changed pixels only on the `@_stream` strips, Xena's idle pose and a butterfly (`change_9400_9463.png`) |
| `fallsnb/` | (−3178,1200,12291), yaw 2048; new bandits blocked (`0x800F2774:3:1`, 8800..8820) | waterfall faces change on every step (about 6550 of 13500 pixels in the top-left box per step); grass box 0. Frames 9440 = 9441 |
| `r1/objscan.txt` | dumps 8800..8806, 8900 | 322 mesh chunks: prims changed only on 6 fire/fire2 sprite quads (their tile animation); vertices changed only on the 8 `@_stream` chunks (byte +2 = Y) and 3 sprite quads |

**Exporter run.** `python tools/export_water_animation.py --root DECOMP --out build/water-anim/export
--verify-ram build/water-anim/river` returned **PASS**:

* the slots match the EXE (type 0, 128..178, rate 1, rows 25/28);
* the CLUT ring rotated by the number of steps between dumps: 62/62;
* wave vertices match the formula: 9792/9792;
* the UV list is empty.

The run with `build/water-anim/wrap` also passed (6/6, 1224/1224).

## Remaster notes

* Keep the three mechanisms and their timings: a 51-step colour ring flowing down the falls, a ±64-unit
  8-phase bob of the river mesh with a 128-step period, and blue-white splash streaks.
* A modern reimplementation can drive a scrolling or flow-map shader on the `NiagraL` and `rivlette` faces at
  about 1 texel per step downwards, which is ≈30 texels/s, with the original pattern repeating every 1.7 s.
* The gameplay geometry stays unchanged. The river wobble is visual only; it was not traced whether
  collision uses these vertices (**UNKNOWN**).
