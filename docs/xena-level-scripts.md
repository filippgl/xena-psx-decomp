# Level 01 "scripts": triggers, pickups, signs, generators, level end (audit 2026-10-25)

Labels: **VERIFIED** = read directly from MIPS/EXE data at the cited address and, where stated, matched
against the native RAM snapshot `build/level-assets/01/native-reference/ram_009200.bin` (frame 9200 of
`build/port/xena-console.exe`, 2026-10-22T18:11:35Z — the native port, not PS1 hardware);
**INFERRED** = reading of code not checked dynamically; **UNKNOWN** = not located.
Units: 400 u = 1 m (project convention), angles 4096 = 360°, "frame" = one game update.

Exporter: `tools/export_level_scripts.mjs --root <decomp> --out <json>` (reads xena.exe, XENA.UDA,
manifest; validates against the RAM snapshot: **42/42 checks pass**).

## 1. Where the "scripts" live

* **No bytecode/script interpreter** (INFERRED negative). The native file log for level 01
  (`native-reference/reference.json`) loads only `ENVIRO.BIN, LEVEL.STM/BIN/TMD/LGT/MIP/CLS, HORIZ.STM,
  FLEVEL.TMD, FIRE2.STM, PLRS.DAT` + model files. None is a script. All gameplay events are
  hard-coded **per-class update functions** (descriptor table `0x800C33E4`, 0x44 bytes, **249 classes**
  0..0xF8 — earlier tools stopped at 0xB2; `sign`=0xF7, `sign_pt`=0xF8, doors 0xC9..0xD7) plus
  level-number checks on `D_800F2772` (RAM = 1 on level 01).
* **Data**: `DATA\LEVELS\01\PLRS.DAT` (37 ASCII records, layout in `docs/level-resource-audit.md`).
  Loader `func_8006287C` (`0x80063940..0x80063E28`): params p0..p6 -> actor `+0x84..+0x90`;
  waypoints -> `+0x60/+0x6C/+0x78`. For `g_*` records p4 is decoded into actor flags `+0x144`
  (table in §3); for other records `+0x10 = descriptor health + p0` (`0x80063E04`, VERIFIED in RAM:
  signs 20/21/22, scrolls 1/2) and `+0xFE += p1`.
* EXE tables: level config `0x800C18E4` (40 B/level), messages `0x800CCCD0`, music `0x800CC37C`,
  sound sets `0x800EE550`, banks `0x800EE478`, Siras camera path `0x800C804C`.
* `ENVIRO.BIN` (100 B, magic `0x9999ABCD`) holds colours (bytes `00 4A 61` = the measured sky
  RGB(0,72,95)) and the value 35 (= LGT brightness bias seen in RAM). Environment, not script (INFERRED).

## 2. Level 01 inventory (PLRS index, class, update)

| # | Actor | Pos (x,y,z) | Behaviour (code) | Data |
|---|---|---|---|---|
| 0-2 | sign_pt ×3 | (-5283,-1900,-9432) (-9998,333,-6501) (10888,3712,7945) | sign `0x800406C8` | p0 = 0/1/2 -> messages 0x46/0x47/0x48 |
| 3 | siras | (12385,3961,18354) | level-end NPC `0x80074FB8` | §5 |
| 4 | up_sword | (-2197,-1453,1797) | pickup `0x8003FBA0` | sword level +1 (max 4), first-time msg 0x45 |
| 5 | scroll | (-5675,-1764,-15865) (near start) | pickup | message 2 |
| 6 | scroll | (-4631,-1652,-1777) | pickup | message 3 (chakram tip) |
| 7-9 | health2 ×3 | (-2559,1306,12404) (2262,2227,5940) (-12895,-1867,-13274) | pickup | potion, first-time msg 0x36 |
| 10 | xena | (-8098,-1840,-18382) yaw 3474 | player `0x800367BC` | start point |
| 11-15 | frog ×5 | | `0x8001B12C` ambient | not audited |
| 16-18 | bfly2 ×2, bfly1 | | `0x80019FE0` ambient | not audited |
| 19-25 | g_lark ×7 | | generator `0x80032C18` | 10 larks total, bypass live limit |
| 26-36 | g_bandit* ×11 | | generator | 12 bandits total, §3 |

Static LEVEL.BIN objects `@2doors` (#6) and `@2doorsr` (#11): **no class** — the EXE contains no
`2doors` string and PLRS has no door record, so the doors never open (VERIFIED absence; their CLS
groups make them solid). No `exit`, `continue`, `key1` actor exists on level 01.

## 3. Generators (`func_80032C18`, spawn `func_80033814`) — VERIFIED code, RAM-consistent

Params: p0 aggression -> spawned +0x84; **p1 remaining count** (+0x86, decremented per spawn only if
<100, so ≥100 = infinite, `0x80033BD0`); **p2 radius bonus** (×100); p3 health delta to spawned +0x10
(min 1, `0x80033B88`); **p4 flags**; p5 -> spawned +0x8C if flag 0x1000; p6 block % -> +0x90.
p4 -> `+0x144`: base 0x8; 1->0x100, 2->0x80, 4->0x800, 8->0x200, 0x10->0x400, 0x20->0x1000,
0x40->0x2000, 0x80->0x4000, 0x100->0x80000, 0x200->0x200000, 0x400->0x800000 (`0x80063A24..0x80063BAC`;
all 18 generators match RAM; extra runtime bits 0x41 on larks / 0x40 on #35 — UNKNOWN).

Per tick (each generator only when `slot & 15 == frame & 15`, `0x80032CB4..0x80032CFC`):
1. `remaining == 0` -> idle. If live enemies `D_800F2774` ≥ **level limit 3** (`cfg+3`), wait
   (larks `0x45` and class `0x2F` bypass, `0x80032C54..0x80032C94`).
2. No other non-generator actor within **350** of the generator (`0x80033098..0x8003318C`).
3. Trigger: **radius** — `dist(Xena, gen) ≤ desc+0x2C + 100·p2` (bandit gens 9000, lark 3000;
   distance = max(|dx|,|dz|)+3/8·min), or **segment** (flag 0x400) — Xena within 800 of the point
   projected on waypoint1->waypoint2 and inside the segment box (`0x80033268..0x800334C4`).
   Flag 0x2000 also requires |dy| ≤ 1000.
4. Flag 0x100 without 0x80: line test spawn-point->camera (`func_80032B6C` -> `func_800A2E98`);
   spawn refused when the test returns 0 (polarity meaning INFERRED: spawn out of camera view).
5. Rate limit: after 3 spawns, next not before `frame + (D_800F0A58 ? 480·D_800F0A58+301 : 1)`
   (RAM `D_800F0A58` = 0).
6. Spawn class = name without `g_`, at the generator position, yaw = record yaw or facing Xena if
   yaw&0xFFF = 0; flag 0x200 copies waypoint 0 as patrol target.

So there are no scripted "waves": each generator releases `p1` enemies one at a time while its
trigger holds, at most 3 alive level-wide. Level 01 bandit triggers (export JSON has all):
radius 9000 at #33/#34 (bandit), 7000 at #30/#31 (bandit1); segments at #26/#28/#29/#36/#27/#35/#32;
#32 (first bandit, `0x4708`) = segment (-615,-1734,-13720)->(-6844,-2104,-10141), camera line test,
walks to waypoint (-2237,-1424,-4091). Snapshot: #32 remaining 0, one bandit alive, `D_800F2774`=1.

## 4. Pickups, signs, messages — VERIFIED

* Collection (Xena update `0x80038F28..0x80039030`): Xena's contact actor `+0xF0` with class flag 8
  while Xena state ∈ {0..5, 0x14..0x1F, 0x37} -> Xena plays state 0x37, at frame `+0xE6 == 11` sets
  pickup `+0x10C = 1`. `up_armor`/`up_sword` are refused (sound 0x39) when their level is 4.
  Pickup radius/height 100/300 (descriptor +0x28/+0x2A). Then the pickup despawns, sound 15 ×2.
* Effects (switch `jtbl_80017594`): **health2** potions `D_800F277E`+1; auto-used at once if
  `health + 150 < 500` (`func_800410C0`: +30 % max, capped; companion +20 %); **scroll** `D_800F277A`
  +1 (cap 2) and message `level·2 + (+0x10) − 1`; **up_sword** `D_800F2786`+1 (<4), `D_800F0CB1`+1.
  First pickup of a kind shows message `0x36 + log2(bit)` once (`func_8006E468`, mask `D_800F1190`).
* Signs (`0x800406C8`): touching sets `+0x10C` (`0x80038F08`); message `p0 + 0x46`; 60-frame
  cooldown, re-armed while Xena is within 400.
* Message box: `func_800672F8` (only in game state 12), drawn by `func_8006E508` from up to 8 line
  pointers, dismissed after 15 frames by pad bit 0x800 (Start, INFERRED).
* Text is a Russian translation stored with remapped Latin glyphs. A..Z mapping read from the font
  glyphs in VRAM (960,256) of the snapshot; `$ & + # @ 0 3 4 6` = о б л ч з О З Ч Б are INFERRED but
  produce consistent Russian. Level 01: scroll#5 "Быстро следуй к Обалусу… И остерегайся пиратов по
  пути."; scroll#6 "Совет: Для броска Шакрама нажми и удерживай кнопку Р1, потом жми {icon 4}" (byte 0x04 =
  a pad-icon glyph); signs "К Обалусу" / "Впереди Обалус" / "Привет в Обалусе. Население: …"; title
  (`cfg+0x14`) "Путь к Обалусу". Raw bytes are kept in the JSON for glyph-exact rendering.

## 5. Level end = Siras (`0x80074FB8`, `func_8007E410`) — VERIFIED code

Siras triggers when Xena is within **1450** (same metric) and Xena `+0xF8 == 0` (UNKNOWN meaning; 0
in RAM). Cutscene: Xena placed at Siras +(130, ground, −330), yaw 0xAE3; camera follows 10 keyframes
`0x800C804C` relative to Siras in 5 linear segments (161/155/83/62/102 frames); RAM copy matches EXE.
Then `func_8006C2D0` sets game state `D_800F2648 = 13`, the same call the `exit` class uses
(`0x80027640`), i.e. level complete. Not skippable on level 1 (pad check excludes level 1,
`0x8007E458`, INFERRED). No checkpoint/save actor on level 01 (`continue` would store the respawn
point `D_800F0D20`).

## 6. Music/sound — VERIFIED

`cfg+4` = sound set 1 -> `func_80086DCC`: music `m_vill` (`M_VILL.SEP/VH/VB`), banks core, npcm,
village, valarian — matches the native file log and RAM `D_800F158C` = 1. No per-trigger music cue
was found on level 01 (cutscene calls `func_80086A24`/`func_8008777C`, UNKNOWN role).

## 7. Unity plan (evidence-backed first)

1. Load the JSON; spawn pickup/sign/Siras triggers at the PLRS positions (convert like `Spawns.json`).
2. Pickups: contact radius 100/height 300, pickup animation (Xena state 0x37, collect at frame 11),
   effects and first-time messages from §4; health auto-use rule exactly as above.
3. Signs/scrolls: modal message box with the original raw lines; render with the original font
   (glyph atlas from `XENAFONT.TIM`/VRAM) or the transliteration as a fallback.
4. Generator controller: 16-frame stagger, live limit 3, occupancy 350, radius/segment triggers,
   remaining counts, waypoint copy — replaces the current "11 idle bandits in Awake".
5. Level end: Siras radius 1450 -> camera path from `cutscene.keys` -> level-complete state.
6. Music: play `m_vill` on level start.
7. Later: camera line test polarity, `D_800F0C62/D_800F0C5A` pending counters, Xena `+0xF8`,
   ambient frog/butterfly/lark AI, flag 0x4000, potion manual use.
