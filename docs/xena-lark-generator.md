# Lark generator, lark flight and creature wing animation (level 01)

Scope: `g_lark` generator (class 0x45), `lark` (0x46), SMA vertex-animation timing for LARK/BFLY1/BFLY2,
frogs. Labels: VERIFIED (code and/or RAM), INFERRED, UNKNOWN. Distances are the game metric
`max(|dx|,|dz|) + 3/8·min(|dx|,|dz|)` (horizontal only). One game tick = 2 vblanks in the port (30 Hz):
`D_800ECEA0` +1 per tick, `D_800F16C0` +1 per vblank (VERIFIED, RAM below).

RAM evidence: `build/port/xena-console.exe` (main tree, built 2026-10-22 21:11), headless, level 1 start
script `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS`, Xena position vector `0x801BB494` (actor
`0x8014E4E0` +0x5C) poked. Run A: dumps 8300..9200/10, 9300..9800/4, Xena moved to (140,-1855,-12724) at
frame 9300. Run B: dumps 7900..8298/2, 9790..9858/2, same poke plus x = -1523 at frame 9800. Dumps were kept
in the session scratchpad only (≈ 450 MB, not committed); the scan scripts are reproducible with the
commands above and `tools/export_creature_vertex.mjs --ram DIR`.

## 1. g_lark generator

Descriptor 0x45 (`0x800C33E4 + 0x45·0x44 = 0x800C4638`): name `g_lark`, update `0x80032C18` (the shared
generator routine), radius `+0x2C` = 3000, no animation script (`+0x24` = 0). Spawn routine `0x80033814`.

PLRS `parameterFields[0..6]` are copied to the generator's s16 fields `+0x84..+0x90` (VERIFIED,
`tools/inspect_level_spawns.mjs`). Position → `+0xAC` (= `*(+0x5C)`), first rotation token → yaw `+0xBA`
(`0x80070144..0x8007015C`).

| field | offset | level-01 values | meaning for g_lark | label |
|---|---|---|---|---|
| p0 | +0x84 | 50 | copied to the lark's +0x84 (`0x80033BB8`); not read by the lark routine | VERIFIED copy, use UNKNOWN |
| p1 | +0x86 | 1 or 2 | **spawn count**: spawn only if > 0; −1 per spawn while < 100 (`0x80033BD0..0x80033BE8`) | VERIFIED (RAM 2→1, 1→0) |
| p2 | +0x88 | 0, 8, 10 | **extra trigger radius ×100**: Xena must be closer than 3000 + 100·p2 (`0x80033228..0x80033258`) | VERIFIED |
| p3 | +0x8A | 0 | added to the child's +0x10 (min 1) when ≠ 0 | VERIFIED, unused here |
| p4 | +0x8C | 2, 3 | not read for g_lark (child +0x8C is always set to 20) | VERIFIED (code + RAM child 8C=20) |
| p5 | +0x8E | 0, 20 | read only with runtime flag 0x1000 (clear) | VERIFIED unused |
| p6 | +0x90 | 50 | copied to the lark's +0x90; not read by the lark routine | VERIFIED copy, use UNKNOWN |

There is **no perched/flying parameter and no spread**: every lark is placed exactly on the generator.

**When it spawns** (`0x80032C18`, class 0x45 takes the `s1 = 1` path, `0x80032C60`):
1. Only on ticks where `(slot index & 15) == (D_800ECEA0 & 15)`, slot index = (generator − list head
   `*D_800F1320`) / 0x1B0 (`0x80032CB4..0x80032CFC`): each generator runs once per 16 ticks with its own
   phase. VERIFIED RAM: gen 20 (slot 21, phase 5) spawned at clk 1077; gen 25 (slot 26, phase 10) at 1290;
   gen 19 (slot 20, phase 4) at 468.
2. `+0x86` (count) > 0.
3. No other non-generator actor with an update routine (Xena included, larks, butterflies, frogs, enemies)
   within **350** of the generator (`0x80033098..0x800331A0`).
4. Xena distance < 3000 + 100·p2 (3000 / 3800 / 4000 in level 01).
5. Camera line test `func_80032B6C` (generator → camera eye `*D_800F10EC+0x1C`, `func_800A2E98`) only
   when runtime flags `+0x144` have bit 0 and not bit 0x80. In all 700+ sampled dumps every g_lark had
   0x80 set (0x88/0xC8/0xC9/0x188/0x1C8/0x1C9), so the test is skipped. VERIFIED (RAM); what sets 0x80 is UNKNOWN.

Level-specific branches (levels 3/6/7/12/21/22) do not apply to level 1 (`D_800F2772` = 1, RAM).

**Placement** (`0x80033814`): `func_80070654("lark", generator +0xAC)` — position = generator position
exactly (y included, no ground snap); yaw = generator yaw `+0xBA` if `(+0xBA & 0xFFF) ≠ 0` (true for all
seven level-01 generators), else the direction from Xena (`func_80032748`). Child `+0x138` = generator.
VERIFIED RAM: lark at (-2642,-1855,-12724) yaw −960 from gen 20; (-2547,-1843,-12888) yaw −640 from gen 25.

**Respawn / max alive**: no timer and no explicit cap. Each generator spawns at most p1 larks per level
load (the only `+0x86` increment is `0x80036668`, an enemy-recycling path gated by `D_800F0C62`, which
the g_lark path never raises — INFERRED no refund; RAM counts never increased). A perched lark sits on
its generator, so it blocks that generator **and every generator within 350** until it leaves or
despawns. VERIFIED RAM: gen 25 (190 from gen 20) spawned only at the first phase tick after gen 20's lark
expired; the second gen-19 lark at level start spawned on the first phase-4 tick after the first had
flown > 350 away. Level start: gens 19/21/22 (next to Xena's start) spawn during loading and fly at once.

## 2. Lark (`0x80019950`, class 0x46)

Init (`+0x95` = 0): generator-spawned → mode `+0x10C` = 0 (perched), `+0xE0` = 0 (wings frozen),
`+0x13C..+0x13E` = 0xFF (meaning UNKNOWN); otherwise mode 3. Lifetime `+0x118` = **210**, `+0x11E` = 50.

Every tick: rand(0,100) (`func_800B4350` is inclusive); ground `+0x48` refreshed every 8 ticks (unused by
0x46); lifetime −1, at 0 `+0x94` = 1 (removed by `0x80041640`). **The lifetime also runs while perched**:
a lark Xena never approaches sits 210 ticks (7 s) and vanishes without flying (VERIFIED RAM run A, life
208 → 0 at the same spot, then removed).

- Mode 0: distance to Xena (or to `D_800EE17C` if set) < **2000** → mode 3, lifetime = 210, `+0xE0` = 1;
  else return (no movement, no sound). VERIFIED RAM: x = −1523 (1024 away) → mode 3, life 210.
- Mode 3 (`0x80019C54`): bank `rotZ` = 0, yaw unchanged, velocity `+0x2C/+0x30` =
  `trunc(−rsin(yaw)/150)`, `trunc(−rcos(yaw)/150)` (|v| ≤ 27; note the minus signs), `+0x2E` =
  rand(0,2) == 0 ? −9 : −13 every tick (1/3, 2/3; negative = up), position += velocity. VERIFIED RAM:
  yaw −1856 → (7, −9|−13, 26); −640 → (22, −13, −15); 2368 → (12, ·, 24); 1984 → (−2, ·, 27).
  210 ticks ≈ 5.7k units horizontally, ≈ 2.45k up on average.
- Chirp (flying only): `(func_800B42D8() & 0xFF) == 0` → sound 0x4B + rand bit, volume 0x3C
  (`func_80084A70`), p = 1/256 per tick. VERIFIED code.
- Modes 1/2/4/5 (circle/return) belong to other classes of the shared routine; class 0x46 never enters them.

The audit's numbers (2000, 27 u/frame, climb 9/13, lifetime 210, 1/256 chirp) are confirmed. Corrections:
direction is (−sin, −cos)·27 of the yaw, the lifetime also counts down while perched, perched larks show
the rest pose with frozen wings.

## 3. SMA vertex-animation timing

Players: `0x80041640` (called from `0x800769A4`) runs `func_8004180C` once per tick for every actor with a
script table `+0xD4` (set at creation from descriptor +0x24 by `0x80041538`: `+0xE0` = 1, `+0xD8` =
script, `+0xDC` = now) unless `D_800F0E00` (pause) is set. `func_8004180C`:
- `+0xE0` = 0 → nothing (frozen). `+0xDC − D_800F16C0 > 0` → wait.
- If `+0xE6 < +0xE8` and `+0xE6 <` frame count (`model+0x22`): `func_80069B80` applies **one** SMA frame
  (`+0xE6`++), then `+0xDC = D_800F16C0 + +0xE0`; at most one frame per tick.
- Otherwise run script opcodes: 8008 n `+0xE0 = n`; 8018 s select slot (`+0xD0`, stream, `+0xE6 = +0xE8 = 0`);
  801C n `+0xE8 = n`; 8002 i goto table[i]; (8005/8006 wait, 8017, 801E, … see `0x8001762C`).

Scripts (EXE): LARK `0x800CC1A0` = `8008 1, 8018 0, 801C 9999, 8002 1`; BFLY1/BFLY2 `0x800CB11C` =
`8018 0, 801C 9999, 8002 1`. So: slot 0 plays **forward and loops** (no ping-pong); the wrap and the
next frame happen in the same tick. Displayed state = decoded `frames[+0xE6 − 1]`; `+0xE6` = 0 is the
untouched rest pose. Frame 0 re-writes the rest pose (`frame0EqualsRest`), so loops never drift.

Rate: a frame is applied on a tick once `D_800F16C0 ≥ +0xDC`. With 2 vblanks per tick, `+0xE0` 1 or 2 →
one frame **every tick**, 4 → every 2nd tick, 0 → frozen. VERIFIED RAM: lark/butterflies with `+0xE0` 1
and butterfly with `+0xE0` 2 both advanced `+0xE6` by exactly 1 per tick (run B 8236..8298,
9790..9858); `+0xE0` = 4 not sampled (INFERRED from code). Not speed-dependent beyond that.

- LARK: 8 frames, flying `+0xE0` = 1 → 8 ticks per wing beat (0.267 s); the LARK script re-sets
  `+0xE0` = 1 on every loop. Perched: `+0xE0` = 0, `+0xE6` = 0 → rest pose, frozen (VERIFIED RAM). After the
  0→3 switch the first frame is applied on the next tick (RAM 9802 `+0xE6` 0, 9804 1).
- BFLY1/BFLY2: 4 frames, sawtooth wing tip y −2, −18, −30, −36, back to −2. `+0xE0` is set by the
  butterfly AI: 1 climbing, 2 descending (both 1 frame/tick = 0.133 s per beat), 4 resting (mode 2: half
  rate, wings keep moving — not frozen).
- Frog: descriptor 0x4D `+0x24` = 0 → `+0xD4` = 0 (`0x80070180`), FROG.SMA is not on disc: **no
  animation**, only the jump/sit mesh swap.

## 4. Vertex export

`tools/export_creature_vertex.mjs` (uses `decode_sma_vertex.mjs`, mirror of `func_80069040`):

```
node tools/export_creature_vertex.mjs --root "G:/Games/Xena Warrior Princess/decomp" --out build/creature-vertex/01/creature-vertex.json
   [--check-static DIR]   # DIR = node tools/export_enemy_unity.mjs --static --anim LARK --model LARK --out DIR
                          #       (+ --anim BFLY1 --model BFLY1 --model BFLY2); compares frame 0 with <Prefix>.xenamodel
   [--ram DIR]            # compares every live lark/bfly1/bfly2 in DIR/ram_*.bin with frames[+0xE6-1]
```

JSON: `{ formatVersion, units, playback, models: [ { actorClass, unityPrefix, model, files{tmd,smi,sma,sha256},
slot 0, frameCount, partTransformsConstant, frame0EqualsRest, sameVerticesAs, meshes: [ { index,
vertexCount, faceCount, unityPrimitiveVertexCounts } ], restVertices[mesh][vertex][xyz],
frames: [ { frame, e6 = frame+1, tag, vertices[mesh][vertex][xyz], unity[mesh][primitive][flat floats] } ] } ],
checks }`.

- `vertices`: TMD vertex order (= RAM `mesh+0x3C`, 16-byte stride), PS1 units, mesh-local, Y down.
- `unity`: identical order and values to `.xenamodel meshes[m].primitives[p].positions` from
  `export_enemy_unity.mjs`: mesh m = TMD object m; primitives = material groups in first-use order
  (texture page/clut, double-sided, flat colour); un-indexed triangle corners, face order, corners [0,2,1];
  value = float32 (x, y, −z) in PS1 units. Scaling (1/400, −1/400, −1/400) is the exporter's
  `<Prefix>_SourceToYUp` root node. Part transforms are constant in all frames (only vertices move).
  LARK: mesh 0 = 24 + 18 corners (14 faces), mesh 1 = 3; BFLY1/BFLY2: 6 + 6, 3. BFLY2 vertices = BFLY1.

Verification (2026-10-26): `--check-static` frame 0 == exporter positions for LARK/BFLY1/BFLY2 (135/45/45
floats, exact). `--ram`: run B 1219/1219 live actor samples matched (LARK 528 at `+0xE6` 0..8, BFLY1 228,
BFLY2 463); run A 847/847; audit reference `build/level-assets/01/native-reference/ram_009200.bin` 3/3
(butterflies `+0xE6` = 2 = frame 1).
