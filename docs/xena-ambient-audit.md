# Level 01 pickups, ambient creatures and music (audit 2026-10-25)

Labels: **VERIFIED** = read from MIPS/EXE data at the cited address and, where stated, matched against
the native snapshot `build/level-assets/01/native-reference/ram_009200.bin` / `vram_009200.bmp` /
`frame_009200.bmp` (frame 9200 of `build/port/xena-console.exe`, the native port, not PS1 hardware);
**INFERRED** = reading of code not checked dynamically; **UNKNOWN** = not located.
Units: 400 u = 1 m, angles 4096 = 360°, PSX y points down, "frame" = one game update.
Damage/hit/death is out of scope (separate audit).

New worktree tools (read-only on the main tree; outputs go wherever `--out` points):

| Tool | Purpose |
| --- | --- |
| `tools/export_pickup_sprites.mjs` | pickup billboard PNGs + JSON (quad, blending, texel counts), RAM/VRAM checks |
| `tools/decode_sma_vertex.mjs` | SMA decoder with the type-2 and packed type-7 **vertex** commands (lark/butterfly wings), RAM check |
| `tools/export_music.mjs` | sound-set tables, VAB/SEP parser, VAG -> WAV, SEP -> MIDI, approximate SPU render -> WAV |

## 1. Pickups: billboard sprites, not TMDs — VERIFIED

* Creation `func_8006FF38`: descriptor byte `+0x40` selects the visual: **1** = TMD model
  (`func_80069C5C`, `0x800700B4`), **2** = sprite (`func_80087A44`, `0x8007011C`), else none.
  health2 (0x5C), scroll (0x5E), up_sword (0x62) have `+0x40 = 2` (and `+0x41` → actor `+0xFE`:
  health2 50). No `HEALTH2/SCROLL/UP_SWORD.TMD` exists in the UDA index (`tools/uda_find.py`: missing);
  the native file log loads only the `.STM`s.
* STM preload `func_8006276C` loads the 13 sprite STMs of table **`D_800C1D1C`** (12 B: name*, s16 w,
  s16 h, u8 semiTrans, u8 mode) via `"DATA\LEVELS\%s.STM"` (`D_800ECC90`); mode 0xFF → load flag 2.
* `func_80087A44` → `func_800A7914` instantiates the 2-triangle template TMD at **`0x800DA7C0`**;
  `func_800A7A4C` sets the quad to **x −w/2..+w/2, y 0..−h, z 0** (pivot bottom-centre at the actor
  position). `func_80087FE0(obj, 0)` maps the whole STM image and sets tpage abr = mode&3 and
  SetSemiTrans when semiTrans≠0. It is called only once with frame 0 → **no frame animation**.
* Draw `func_80071F9C`: translation = actor position (+h/2 only if obj `+0x20`; 0 for pickups), then
  `func_80010000`/`func_80014FD0` transform only the translation by the camera (`D_800F10EC+0x4C`)
  and use the identity rotation `D_800F0790` → **screen-aligned billboard**.
* Update `func_8003FBA0` moves nothing for 0x5C..0x62 (spin/sparkle code exists only for classes
  0x67..0x6B); RAM positions equal the PLRS positions → **no spin, no bob**.

| Sprite | Quad (u) | m | STM image (VRAM) | CLUT | Texels | Blend (RAM tpage) | Texels with STP |
| --- | --- | --- | --- | --- | --- | --- | --- |
| health2 | 120 × 240 | 0.30 × 0.60 | (1002,408) 4-bit | (768,6) | 32 × 64 | abr 0 = ½B+½F | 1196 of 1196 visible → translucent |
| scroll | 256 × 256 | 0.64 × 0.64 | (896,224) 4-bit | (784,10) | 32 × 32 | abr 3 = B+¼F | 0 → opaque |
| up_sword | 80 × 256 | 0.20 × 0.64 | (904,224) 8-bit | (768,8) | 10 × 32 | abr 1 = B+F | 0 → opaque |

Checks (`export-pickup-sprites` run): all 6 live sprite objects in `D_800F3748` have vertices equal to
the quad above, image records equal to the STM, frame 0, semi code bit set, abr as listed;
STM words vs `vram_009200.bmp`: 528/272/416 compared, **0 different**. Frame check: scroll #5 projects
(camera matrix from RAM, H fitted ≈ 372 from its 17 px height) to centre x ≈ 248 / bottom y ≈ 111;
it is seen at x 243..260, y 93..110 in `frame_009200.bmp`, width = height as for a 256×256 quad.
Colour 0x0000 = transparent. UNKNOWN: the final brightness (template TMD flag 0 = lit; the frame shows
the scroll brighter than the raw texture).

## 2. Ambient creatures

Distance everywhere = `func_80032868`: horizontal max(|dx|,|dz|) + 3/8·min — VERIFIED.
Descriptors (`0x800C33E4`+idx·0x44): frog 0x4D flags 0x103, update `0x8001B12C`; bfly1 0x49 / bfly2
0x4A flags 0x4921, update `0x80019FE0`, bfly2 `+0x10` = 0x49 → shares BFLY1.SMA/SMI; lark 0x46 flags
0x4923, update `0x80019950`; g_lark 0x45 radius 3000.

**Frog** (`0x8001B12C`) — code VERIFIED, RAM-consistent (5 frogs, mode 0, hidden = [1,0,0]):
init hides mesh 0 (jump pose) and keeps mesh 1 (sitting); mode 0: on frames with `frame&15 ≠ 0`, if
Xena < **1000** → mode 1, vertical speed `+0x124 = 6500`, swap meshes 0/1, sound 0x10E6 (if not
playing). Mode 1 per frame: y −= v/100, v −= 500 (65 u/frame up, gravity 5 u/frame²), x += 40·sin(yaw),
z += 40·cos(yaw) (yaw = root rotY, RAM e.g. −1280/3264/1152/512); when ground (`func_8003423C`) < y:
`func_800847C0(0x3E+rand(0..4),…)` + `func_8002C0BC(pos)` (landing sound/splash, INFERRED) and despawn.
FROG.SMA is **not on disc** (native log: "file not found") — the frog is a static two-pose model.

**Butterfly** (`0x80019FE0`) — VERIFIED code; RAM: bfly2 #16 moved next to Xena, speed 7 = model `+0x24`.
Init speed `+0x11A` 5, `+0x2A` 50. Xena ≥ **5001** away → teleport to Xena ±1000 on one axis and
rand(−1000,1000) on the other, y = ground − 20. Else timer `+0x118`: at 0 → mode rand&1, timer
rand(3,30), speed ±1 clamped **5..10**. Mode 0: turn rand(−512,512) then mode 1 (straight). Mover
`func_8006E78C(rotY, speed)`; blocked → turn 180°, mode 2 (rest: speed 0, `+0xE0`=4) for rand(20,60).
Height target: < 1500 from Xena → Xena head − rand(100,200); > 3000 → ground − 20; between → wobbling
around Xena's height; ≥ 200 above ground when below. Vertical velocity `+0x2E` +1 to 5 (descending,
`+0xE0`=2) or −1 to −10 (climbing, `+0xE0`=1). `+0xE0` = SMA step period in vblanks (1/2 → one frame per tick, 4 → every 2nd tick; `docs/xena-lark-generator.md` §3).
Wings = SMA vertex animation (4 frames, below).

**Lark** (`0x80019950`, shared with classes 0x3E..0x46) — VERIFIED code; no lark alive in the snapshot.
Spawned by g_lark (`+0x138` = generator) → mode 0 perched, else mode 3. Lifetime `+0x118` = **210**
frames, decremented every frame, despawn at 0. Mode 0: Xena (or `D_800EE17C`) < **2000** → mode 3,
lifetime 210. Mode 3: fly straight, velocity (−sin, −cos)(yaw) · 4096/150 ≈ **27 u/frame**, climb **9** (1/3) or
**13** (2/3) u/frame, bank 0. Modes 1/4 (circling; not entered by level-01 larks): turn rate → ±50
(±10/frame, sign flips at 10 %), bank −8·rate, 9 %/frame check: > 3000 from home → mode 2 (return,
turn ≤ 20/frame until < 1000). Chirp sound 0x4B/0x4C with probability 1/256 per frame. Generator, perched lifetime and wing timing: `docs/xena-lark-generator.md`.

**Exporter** (`tools/export_enemy_unity.mjs`, main tree, run to scratchpad): FROG `--static` OK
(7 nodes, 3 meshes, 18 vertices, 13 triangles, 2 materials, 0 validator problems); LARK and BFLY1/2
**fail** with SMA ("Vertex deformation command not yet supported at 622"); with `--static`: LARK 5 nodes,
2 meshes, 18 v, 15 tris, 3 materials; BFLY1/BFLY2 5 nodes, 2 meshes, 9 v, 5 tris, 3 materials; 0 problems.

**Vertex commands** (`func_80069040`) — VERIFIED: type 2 (`0x80069440`): mesh = word>>8, s16 count,
bit 0x40 absolute, 0x08 packed 5:5:5 (index word unless bit 15 → previous+1), 0x80 byte form (u8 index
delta, s8 xyz), else s16 index/xyz; type 7 with bit 0x08 (`0x800698C4`): mesh 0, vertices
0..(word>>8)−1, u16 = 4:4:4 signed deltas << (bits 0-3). Deltas mutate `mesh+0x3C` (16-byte stride).
`decode_sma_vertex.mjs`: BFLY1/BFLY2 slot 0 = 4 frames, LARK slot 0 = 8 frames (only slot 0 exists);
**both live butterflies match decoded frame 1 with `+0xE6` = 2** (all vertices). Wing tip y: butterfly
−2/−18/−30/−36; lark tips swing +45..−63.

## 3. Music and sound — VERIFIED tables, standard Sony formats

`func_80086DCC(set)`: `D_800EE550[set]` = {music, banks…, −1}; set 1 = {0, 1, 2, 3} → music `m_vill`
(table `0x800CC37C`, 3 sequences), banks `D_800EE478[]` = core (always), npcm, village, valarian.
Files: `DATA\SND\M_VILL.SEP` (pQES v0, 28554 B), `.VH` (pBAV v7) + `.VB` for m_vill/core/npcm/village/
valarian. Sizes check exactly (VH = 32+2048+ps·512+512; VB = Σ VAG sizes; SEP 0 trailing bytes).

| VAB | programs | tones | VAGs (looped) |
| --- | --- | --- | --- |
| m_vill | 15 | 22 | 22 (6) |
| core | 67 | 77 | 39 (5) |
| npcm / village / valarian | 8 / 3 / 4 | 8 / 3 / 4 | 8 / 3 / 4 (0) |

| SEP seq | tempo | metre | notes | length |
| --- | --- | --- | --- | --- |
| 0 | 110 bpm (545454 µs), 480 ppqn | 6/4 | 671 | 105.2 s |
| 1 | 140 bpm | 4/4 | 1213 | 96.0 s |
| 2 | 144 bpm | 4/4 | 1825 | 86.8 s |

Playback (VERIFIED calls, roles INFERRED): game loop `func_80064D0C` calls `func_80064680(Xena+0xF8)`
then `func_80064750()` each frame (not on levels 0/8/15). Seq **0** (0x2000) is started whenever it is
not playing; when Xena `+0xF8` ≠ 0 (combat target, INFERRED) seq 0 is stopped and seq **1/2 alternate**
(`D_800F1090`); 100 frames after `+0xF8` returns to 0 the combat seq fades out and seq 0 fades in over
120 (`func_80085830`). All via `SsSepPlay(access, seq, 1, l_count 0)`; no NRPN loop markers in any
sequence → the whole sequence repeats (l_count 0 = infinite in libsnd, INFERRED).

Pitch: libsnd `SsPitchFromNote` (`0x80096A68`, tables `D_800D46B4`/`D_800D46CC`): 0x1000 = 44100 Hz at
note == centre, +shift/128 semitone; ported exactly. Plausibility: autocorrelation of the looped m_vill
samples gives the predicted natural pitch (centre − shift/128) within the method's ~±70 cents
resolution (e.g. VAG 14: 1323 Hz predicted, 1336 measured).

`export_music.mjs --samples --midi --render`: 76 VAG WAVs (44.1 kHz mono), 3 MIDI files, 3 stereo
44.1 kHz renders 109.2/100.0/90.8 s (sequence + 4 s tail), non-silent in every 10 s window.
Renderer approximations (not original): no reverb, linear interpolation, volume/pan law, pitch-bend
range, no 24-voice limit (seq 2 peaks at 26 voices), one tone (prog 10 note 77) outside every range.

## 4. Unknowns / next checks

1. Sprite brightness (lighting of the lit template TMD) and `+0xE0` on butterflies/larks.
2. Exact SPU ADSR/volume law in a dynamic capture; reverb settings (`SsUtSetReverbType`).
3. Xena `+0xF8` meaning (combat target) — check with the damage/hit audit.
4. Integrate type-2/packed-7 vertex commands into `tools/lib/xena_animation.mjs` + `.xenamodel` contract.
