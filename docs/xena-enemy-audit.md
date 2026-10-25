# Level 01 enemies: assets, spawns and bandit AI (audit 2026-10-24)

Labels: **VERIFIED** = reproduced by a tool/oracle or read directly from the MIPS / EXE data at the
cited address; **INFERRED** = a reading of code or data that has not been checked dynamically;
**UNKNOWN** = not located. Units: 400 units = 1 m (project convention), angles 4096 = 360 deg.
Sources: `xena.exe` (original PS1 EXE), `disc/files/XENA.UDA`, the native RAM snapshot
`build/level-assets/01/native-reference/ram_009200.bin` (frame 9200 of `build/port/xena-console.exe`,
2026-10-22T18:11:35Z; the native port, not PS1 hardware) and `asm/nonmatchings/game/*.s`.

## 1. Inventory

### Spawns (PLRS.DAT, `build/level-assets/01/spawns-source.json`) — VERIFIED

No enemy is placed directly; bandits come only from generators (`g_*`, update `0x80032C18`).

| Generator | Count | Class spawned | Positions (x,y,z) | params +0x84..+0x90 |
| --- | ---: | --- | --- | --- |
| `g_bandit` | 5 | `bandit` (idx 0x95) | rec 32-36: (-4075,-1651,1283) (-1296,746,5063) (-2521,709,3956) (-11562,314,-3455) (9131,3987,20620) | `[0,1,1,0,153,0,10]` `[0,1,0,0,66,0,10]` `[0,1,0,0,66,0,15]` `[0,1,0,0,26,0,10]` `[40,2,-80,0,16,0,20]` |
| `g_bandit1` | 3 | `bandit1` (0x96) | rec 29-31: (17040,3771,15389) (6754,1600,13974) (15450,3947,17980) | `[10,1,0,0,18,0,10]` `[50,1,-20,0,0,0,50]` ×2 |
| `g_bandit2` | 1 | `bandit2` (0x97) | rec 28: (9741,3978,16425) | `[20,1,0,0,16,0,15]` |
| `g_bandit4` | 2 | `bandit4` (0x98) | rec 26-27: (10736,4093,19563) (3461,1557,11392) | `[10,1,0,0,16,0,10]` `[50,1,0,0,26,0,13]` |

Each generator record also carries three waypoint vectors (record vectorFields, actor +0x60/+0x6C/+0x78).
Non-hostile actors on the level: `siras` (1, NPC, update `0x80074FB8`), `frog` ×5, `bfly1/2` ×3,
`g_lark` ×7 (ambient birds). The frame-9200 snapshot has exactly one live bandit at `0x80151270`,
spawned by record 32 (same origin, actor +0x134 = generator `0x80150A00`).

Generator -> class (VERIFIED): `0x80033844 addiu $s0, name, 2` strips `g_`, then `func_80070654(name, pos)`
creates the actor. Copied into the spawned actor: generator +0x84 -> actor +0x84 and +0x90 -> +0x90
(`0x80033BB8..0x80033BCC`); +0x8C = 20 or generator +0x8E (`0x800339F4`, `0x80033A34`); generator
+0x86 is the remaining-spawn counter (decremented `0x80033BD0..0x80033BE8`). Snapshot agrees: bandit
+0x84..+0x90 = `0,0,0,0,20,0,10`, +0x60 = waypoint 0 of record 32. Generator params 2,3,5 and the
spawn trigger are UNKNOWN here (bandit +0x88 = 0 although generator +0x88 = 1).

### Asset files (manifest `build/asset-audit/manifest.json`, hash-matched names) — VERIFIED

| File | Bytes | Content (parsed with the Xena parsers) |
| --- | ---: | --- |
| `01\BANDIT.TMD` / `.STM` | 11412 / 36072 | 18 objects, 206 vertices, 272 triangles; 15 TIMs, 8-bit, CLUT row y=12 |
| `01\BANDIT1.TMD` / `.STM` | 12956 / 39144 | 18 / 211 / 324; 15 TIMs incl. `Shield`, `MaceTop`; CLUT y=13 |
| `01\BANDIT2.TMD` / `.STM` | 12212 / 37560 | 18 / 203 / 302; 14 TIMs incl. `Shield`; CLUT y=14 |
| `01\BANDIT4.TMD` / `.STM` | 12972 / 39144 | 18 / 212 / 324; pixels identical to BANDIT1 (15/15), CLUT y=15 only differs (palette swap) |
| `01\BANDIT.SMI` | 364 | 18 × 20-byte joints + 4 bytes (XENA.SMI is 33 × 20 + 4, same layout) |
| `01\BANDIT.SMA` | 94600 | 146-slot table, 61 clips, 1732 frames, commands type 1/6/7 only (no vertex deformation) |
| `05\BANDIT.SMA/.SMI` | same | byte-identical to the level 01 copies |

The STM rectangles of the four variants and SIRAS do not conflict in VRAM (15 overlaps, all identical
data). Loader `func_80069C5C`: TMD/STM use the class name (`DATA\LEVELS\%02d\%s.TMD`, format at
`0x800ED490`); the SMA/SMI owner is descriptor +0x10 if nonzero, else the class itself
(`0x80069EA4 lw $s2,0x10($s4)` / `0x80069EB4`). bandit1/2/4 have +0x10 = 0x95, so all four share
BANDIT.SMA/SMI — VERIFIED. (Amazons have +0x10 = 0x5A = Xena: they would reuse XENA.SMA.)

Pose check — VERIFIED: the live bandit (state +0xD0 = 0x15, frame +0xE6-1 = 31) matches decoded
BANDIT.SMA slot 0x15 frame 31 in all 102 non-root joint components, and no other frame matches.
BANDIT.SMI is resident in RAM at `0x801DCB8C` (byte-identical). The resident TMD is converted at load,
so which TMD variant a class draws was not checked in RAM; the class-name path is the evidence.

## 2. Export tool: `tools/export_enemy_unity.mjs`

One step from disc data to the `.xenamodel` contract read by `XenaModelImporter.cs`, reusing
`tools/lib/xena_model.mjs`, `xena_animation.mjs`, `xena_transforms.mjs` (game trig table) and the
same float32 rounding as the glTF buffer stage. Writes `<Name>/<Name>.xenamodel`, `page-N.png`,
`<Name>.motion.json` (per-frame root motion) and `export-report.json` (counts, JS mirror of
`XenaDocumentValidator`, original class/action tables from the EXE).

```
node tools/export_enemy_unity.mjs --root <decomp> --out <dir>            # BANDIT, BANDIT1, BANDIT2, BANDIT4
node tools/export_enemy_unity.mjs --root <decomp> --out <dir> --xena-parity <Imported/Xena/Xena.xenamodel>
```

Parity check — VERIFIED: exporting XENA.TMD/SMA reproduces the imported `Xena.xenamodel` exactly:
446 752 numbers, 0 differences, 0 structural differences (67 nodes, 33 meshes, 426 tris, 81 clips,
1674 frames), and page-0..2.png are byte-identical.

| Model | Nodes | Meshes | Vertices | Triangles | Materials | Pages | Clips | Frames | Validator |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Bandit | 37 | 18 | 206 | 272 | 6 | 3 | 61 | 1732 | 0 problems |
| Bandit1 | 37 | 18 | 211 | 324 | 4 | 2 | 61 | 1732 | 0 problems |
| Bandit2 | 37 | 18 | 203 | 302 | 3 | 1 | 61 | 1732 | 0 problems |
| Bandit4 | 37 | 18 | 212 | 324 | 4 | 2 | 61 | 1732 | 0 problems |

Not established: no hidden-part baseline for bandits (all parts start visible; the bandit SMA has no
type-5 visibility commands, so this is a no-op); Unity Editor import not run; 30 steps/s is the
project convention.

## 3. Bandit AI

Class descriptors (EXE, table `0x800C33E4` + idx·0x44) — VERIFIED values, meanings as labelled:

| Field | bandit / 1 / 2 / 4 | Use |
| --- | --- | --- |
| +0x04 | 0xC13 (RAM 0xC93: bit 0x80 set at load) | class flags; 0xC93 = the "class C93" of earlier notes |
| +0x08 | 75 / 100 / 75 / 75 | max health; copied to actor +0x10 (snapshot 75). Xena 500 |
| +0x1C / +0x20 | `0x8001B460` / `0x8001B374` | update (AI) / destructor (frees +0x100, +0x104) |
| +0x28 / +0x2A | 150 / 750 | actor radius (0.375 m) / height (meaning of 750 INFERRED); copied to actor +0x28 |
| +0x2C | 9500 | notice range (23.75 m), + 100·actor+0x88 |
| +0x2E | 700 | attack range (1.75 m) |
| +0x30 | 1700 | walk/run and engage boundary (4.25 m) |
| +0x32 | 2000 | mode 3 leave range (5 m) |
| +0x34 | 550 | too close: back off (1.375 m) |
| +0x36 | 64 | max turn per step (5.6 deg) and heading jitter scale |
| +0x3C | `0x800C2AA4` | contact points: part 7 ×3 (offsets 70,309,220 / 24,32,22 / 46,158,115), parts 1, 10, 13, 16 |

Actor fields used by `func_8001B460`: +0x10 health, +0x38 anim family (bandits 0, table `0x800BFE8C`),
+0xD0 current SMA slot, +0xEC target (snapshot: Xena), +0x10C AI mode, +0x108 attack cooldown
(0xFFFF = flee), +0x84 aggression, +0x90 block %, +0x34/+0x36 patrol flags, +0x60 waypoint, +0x98 home.
Family-0 action table `0x800C0184` (47 words; slots VERIFIED, roles INFERRED from use below):
idle 07/0A, hit/death set 8A..91, attacks 3D/3E/3F/50, lunges 3C/40, backstep 23, block 80,
walk 15, run 1A, sidesteps 26 (+90 deg) / 27 (-90 deg).

Perception — VERIFIED (`func_80032748`, oracle-run on the snapshot): distance = max(|dx|,|dz|) +
3/8·min (horizontal), dy separate; relative angle = atan2(actor-target) - rotY, where 0 = target dead
ahead (snapshot: patrolling bandit has angle 0 to its waypoint, 4454 units away).

Mode decisions (`switch actor+0x10C`, `jtbl_80016A90` at `0x800204E4`), then mode actions
(`jtbl_80016AB0` at `0x80020FE0`):

| Mode | Decision (address) | Action (address) |
| --- | --- | --- |
| 0 idle | `0x800204FC`: dist < 9500 and target not in rear ±45 deg (0x600..0xA00) -> mode 1 or 2 (rand&1), alert sound | `0x80020FF8` idle clip |
| 1 patrol / hold | `0x80020648`: patrolling (+0x34): sees player if dist < 6000 within ±35 deg or dist < 1000 within ±90 deg, plus line test `func_800A2E98` 400 u above the feet -> mode 2. Else dist > 1700 -> 2, dist < 700 -> 5 | `0x80021478` steer (≤64/step, raycast avoidance `0x8002168C`); walk 15 while patrolling or in mode 1 below 1700 (`0x80021DE4`), otherwise run 1A |
| 2 approach | `0x800209FC`: dist < 1700, no ally in 3C/40, mover not blocked, 1/3 chance -> lunge 3C (40 if target in states 0x19..0x1F) and mode 5; dist < 700 -> 5 | as mode 1 (run) |
| 3 wait | entered from modes 1-3 below 2000 when allies in mode 5 on the same target weigh ≥ 2 (`0x8002012C` loop, weight 1 per bandit); if a second target `D_800EE178` is set it is taken instead. In mode 3: dist > 2000 -> 1; sidestep 26 or 27, each 1/51 per step (`0x80020CF4`) | `0x8002130C`: < 700 -> 5; < 1700 -> backstep 23 |
| 4 block | from `0x800202CC`: dist < 1000, AI+0x3E = 0, rand(1,100) ≤ +0x90, target in states 0x3A..0x65 and facing ±70 deg; only classes 0x96..0x98 (or family 1/3) — plain `bandit` never takes it | clip 80, AI+0x40 = 15; leaves to 5 (< 700) or 1 |
| 5 attack | `0x80020D8C`: flee flag -> 2; dist > 700 -> 1; 1/101 idle sidestep | `0x80021F38`: turn to target; < 550 -> backstep 23; ally within radius+700 -> sidestep; when cooldown 0: attack = weighted pick (`func_80023794`, weights 1 each: 3D 3/11, 3E 2/11, 3F 3/11, 50 2/11, none 1/11) |
| 6 avoid | `0x80020048`: mover blocked by actor +0xF4 and dist ≥ 3001 | `0x8002103C` steer round it (its radius + 700) |
| 7 delay | `0x80020FAC`: wait until frame counter `D_800ECEA0` ≥ actor+0xDC -> 5 | none |

Timing — VERIFIED: cooldown after an attack = rand(4q, 6q) steps, q = (100 - aggression)/5
(`func_80023D40`, aggression = actor+0x84); +0x108 decreases by 1 per step (`0x8001DCBC`). Aggression
0 -> 80..120 steps; 50 -> 40..60. `func_800B4350(a,b)` = uniform integer a..b. In the path at
`0x8001F504` (probably the hit reaction; context INFERRED): +0xFC = rand(90,220), cooldown reset to 0; if health < max/3, 1/4 chance to flee
(+0x108 = 0xFFFF): turn away, and at dist ≥ 14001 set +0x94 = 1 (despawn INFERRED).

Movement — VERIFIED: the AI passes the SMA context fields to the mover `0x8006E78C(actor,
rotY + ctx+0x26, ctx+0x24, flag)` at `0x8001E144`/`0x8001FD58`, then clears ctx+0x24. The speeds are
therefore root motion stored per frame in BANDIT.SMA (`Bandit.motion.json`): walk 15 = 931 units over
40 moving frames (23.3/step), run 1A = 1839/23 (80/step), backstep 23 = 374 at offset 0, sidesteps
26/27 ≈ 550 at ±1023, lunges 3C ≈ 1037 and 40 = 1771 forward. At 30 steps/s (unverified for PS1) walk ≈
1.75 m/s, run ≈ 6 m/s. The mover's u8 result goes to sp+0x240 (a lunge requires 1; 0 = blocked is INFERRED); on contact the other actor (+0xF0 ->
+0xF4) triggers mode 6. Collision candidates are the shared list `D_800F131C` (snapshot: the 12-actor
tail of the actor list starting at Xena: Xena, pickups, the bandit, siras, signs). The same list is
scanned for crowd counting (mode 3) and ally spacing (mode 5).

UNKNOWN: damage numbers in either direction and hit windows (bandit attacks call `func_8006A6E8` /
`func_80035070` with the contact list; who subtracts health was not found), hit/death state handling
in the first ~2000 instructions, reactions to Xena states 0x3F/0x46/0x58..0x5D/0x5F..0x62
(`jtbl_80016A00` at `0x8001E21C`), generator spawn trigger, the stuck-recovery and leash code
(`0x80021864..0x80021DDC`), `func_8002387C`/`func_80023C84`/`func_80023D9C`.

## 4. Unity port plan

1. Import `Bandit*.xenamodel` like Xena (same importer; no code change needed — validated in JS only),
   one shared clip set. Check in Editor: 37 nodes, 61 clips, triangle counts above.
2. Spawner: place the 11 generators from `Spawns.json`; the class is the name without `g_`; copy
   aggression (+0x84) and block % (+0x90); waypoint 0 is the patrol target.
3. Drive movement from `Bandit.motion.json` (speed and direction per frame) through the existing
   original-terrain mover, radius 150 u; do not add invented speeds.
4. Implement modes 0/1/2/5 first with the ranges 9500/1700/700/550, turn rate 64/step, attack pick
   and cooldown formula; then mode 3 (two attackers at most), mode 4 block for bandit1/2/4 only, mode 6.
5. Keep damage, hit reactions and death as clearly marked prototype rules until their MIPS is traced.
