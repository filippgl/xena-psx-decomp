# Damage, hit reactions and death: Xena and level-01 bandits (audit 2026-10-25)

Labels: **VERIFIED** = read directly from the MIPS or EXE data at the cited address, or reproduced by a
tool on existing RAM captures; **INFERRED** = a reading of code or data not checked dynamically;
**UNKNOWN** = not located. Sources: `xena.exe` (file offset = address - 0x8000F800),
`asm/nonmatchings/game/*.s`, XENA.UDA via `build/asset-audit/manifest.json`, and existing native RAM
captures (`build/combat-*reference`, `build/action-reference-*`, made by `build/port/xena-console.exe`;
`build/combat-reference/capture.json` dated 2026-10-23 22:09). Those captures come from the native port,
not PS1 hardware. No game, build, Unity or smoke run was started for this audit.

"Frame" means the actor clip counter `+0xE6`; "step" means one update of the actor. The rate on
PS1 is not measured here.

## 0. Tools added (read-only)

| Tool | Purpose |
| --- | --- |
| `tools/decode_action_scripts.mjs --class 5A\|95 [--windows]` | Decodes the per-class **action scripts** that set damage, from the EXE. |
| `tools/verify_damage_windows.mjs` | Compares the decoded Xena damage windows with `+0xFE` in 3266 RAM snapshots. |
| `tools/inspect_sma_damage.mjs` | Lists SMA type-6 context fields and clip lengths (showed that damage is **not** stored in the SMA). |

Verification result (VERIFIED, run 2026-10-25): 510 snapshots have a nonzero `+0xFE`. 480 of them fall
inside a decoded window with the same value. The other 30 are all `62` frames 1-10 = -35. That value is
carried over from the preceding `61`/`56` kick, because the clip opcode does not reset `+0xFE` (see section 1.1).
The observed value is nonzero for `start < +0xE6 <= end` of each window. This one-frame offset follows
from the interpreter, which yields after each frame advance.

## 1. Where damage comes from

### 1.1 Action-script interpreter `func_8004180C` — VERIFIED

Every actor runs a halfword script (stream pointer `+0xD8`, table pointer `+0xD4`). The class descriptor
at `+0x24` (descriptor table `0x800C33E4 + class*0x44`) points to the table. Xena (0x5A) uses `0x800C90D8`.
bandit, bandit1, bandit2 and bandit4 (0x95..0x98) all use `0x800C9CF8`, so their damage tables are
identical. Opcodes are `0x8000|n` (`jtbl_8001762C`). The ones used here:

| Opcode | Args | Effect (handler) |
| --- | --- | --- |
| 0x8000 | 0 | end; sets `+0x94 = 1` unless the actor is Xena (`0x80041BD4`) |
| 0x8002 | 1 | goto table[n] |
| 0x8005 / 0x8006 | 1 | wait n / rand(1,n) game frames (`+0xDC = D_800F16C0 + n`) |
| 0x8011..0x8016 | 1-2 | set/compare counters `+0xE2`/`+0xE4`; `+0xE2` holds the requested next action (slot number) |
| 0x8018 | 1 | play SMA slot n: `+0xD0 = n`, `+0xE6 = +0xE8 = 0` (`0x80041F9C`); does **not** touch `+0xFE` |
| 0x801B | 1 | **damage**: `+0xFE = n`. If the old value was 0 and n != 0, it clears the contact-point history at `+0x12C` (`0x8004201C`) |
| 0x801C | 1 | run until `+0xE6 == n` (9999 = to the end of the clip) |
| 0x801E | 1 | advance n frames immediately |

### 1.2 Xena -> enemy (`func_800367BC`, `0x8003CD8C..0x8003CF88`) — VERIFIED

```
d = Xena+0xFE                       ; from the script
if D_800F0D18 and d > 0: d = 100    ; flag of unknown purpose (UNKNOWN)
if D_800F1136 != 0:      d *= 2     ; power-up timer (section 2.3)
d += sign(d) * D_800F2786           ; sword level 0..4 (up_sword) adds to |d|
points = d >= 0 ? contact points 0..4 (sword) : points 5..6 (kick)   ; Xena class +0x3C list
for each point: func_80035070(Xena, d, cur+12*i, prev+12*i, ...)
```

`func_80035070` does three things. (1) It finds a target with `func_80070DFC`. (2) It doubles `d` for
three Xena-state / target-class pairs: 0x46 vs class 0x99, 0x4D vs 0x73, 0x5F vs 0x75. (3) It sets
**`target+0x12 = max(target+0x12, |d|)`** (`0x80035144..0x80035160`). Within one step, several points do
not add up; only the largest one counts. `attacker+0x10A = d`. It also plays a sound: block clang 0x1B
if the target is in states 0x80..0x83.

Xena damage per clip (base `+0xFE` before the sword level is added; negative = kick points). Frames are
the script thresholds `start-end`; `+0xFE` is live on frames `start+1..end`.

| Clip | Move (from xena-combat-audit) | Damage | Window | RAM check |
| --- | --- | ---: | --- | --- |
| 51 | sword 1 | 30 | 4-10 | 5..10 = 30 |
| 52 | sword 2 | 40 | 2-10 | 3..10 = 40 |
| 3A | sword 3 | 45 | 11-18 | 12..18 = 45 |
| 53 | finisher (61-52-53) | 75 | 11-18 | 12..18 = 75 |
| 4F | finisher (51-62-4F) | 80 | 13-26 | 14..26 = 80 |
| 55 | finisher (61-62-55) | 50 | 7-16 | 8..16 = 50 |
| 61 | kick 1 | -35 | 3 up to frame 12 (or kept into 56 if the next input is a sword press) | 4..12 = -35 |
| 62 | kick 2 | -35 carried, then -40 | -40 at 10-18 | 1..10 = -35, 11..18 = -40 |
| 5A | kick 3 | -70 | 0-16 | 1..16 = -70 |
| 56 | kick -> sword link | -35 (carried) | whole clip | 1..3 = -35 |
| 46 | guard + attack | -70 | 14-20 | 15..20 = -70 |
| 5C | guard + attack | -60 | 14-38 | 15..38 = -60 |
| 3E / 3C / 4D | (other moves) | 30 / 15 / 20 | 0-36 / 1-40 / held | match |
| 42 | (special) | 10000 | 16-19 | 17..19 = 10000 |

The full list (3B 20, 3D 55, 3F -350, 40 150, 44 25, 45 30, 47 -35, 48 -35, 49 -35/-45/-50, 4A 35, 4B -70,
59/5B/63 -40, 60 -65) comes from `decode_action_scripts.mjs --windows`. Rows marked "end" were walked
linearly without branches, so they are upper bounds. The chakram uses its own actor and was not traced
(UNKNOWN).

### 1.3 Contact test `func_80070DFC` — VERIFIED (read)

It scans the shared list `D_800F131C` (link `+0x8`) and skips: the attacker itself, actors with radius
`+0x28 = 0`, and actors in states **0x8A..0x91** (dead). Coarse test: horizontal distance (max+3/8·min) from
the point to the target position is less than `2·(r_target + r_attacker)`. With a previous point, the
target is projected onto the segment prev->cur; the projection is used only if it lies inside the
segment's box, and the previous point is also tried. Fine test: point Y lies between `pos.y - height(+0x2A)`
and `pos.y`, and the horizontal distance is less than **`r_target + 50`** (bandit 150 -> 200 units,
0.5 m). On a hit: `target+0x18..0x20` = hit point, `target+0x24` = attacker. The first target found per
point is returned.

## 2. Damage intake

### 2.1 Health subtraction `func_80032904` — VERIFIED

`+0x10 = max(0, +0x10 - +0x12)`, `+0x12 = 0`, return `+0x10 == 0`. Called by Xena (`0x80037D68`), the bandit
AI and 11 other updates. It also drives a meter `D_800F1135` for enemies (descriptor flag 0x10):

- Kill by Xena while the enemy flees (`+0x108 == 0xFFFF`): -10.
- Kill while Xena is in 0x61/0x62/0x5A/0x60/0x49/0x55/0x3F/0x5C/0x5F: +10.
- At 96 or more: `D_800F1136 = 825` (power-up: Xena damage ×2, ticks down at `0x80037E34`; the meter is reset
  when it ends) and `D_800F1138++`.
- Classes 0xAA/0xAB killed by Xena: -30.

The game-facing meaning (fury/power meter) is INFERRED.

### 2.2 Bandit (`func_8001B460`) — VERIFIED unless marked

Order inside one step:
1. `0x8001D588`: damage from another enemy (attacker class flag 0x10, not Xena or companion `D_800EE178`)
   is discarded. All damage is discarded while `+0xE4 == 0x26` (the script sets this before clip 76;
   meaning INFERRED).
2. `0x8001E288`: **damage is discarded** while the hit timer `+0x118 != 0`, or while guarding (section 4).
   Exception: Xena in state 0x48. In block states 0x80..0x83 a spark is drawn (`func_800291A0(hit,d,1)`).
3. `0x8001E858`: `+0x10A = 2·clamp(d,10,50)` (knockback, section 3). `func_80023C84` puts every idle actor
   with the same AI (`+0x10C = 0`) into mode 1 (INFERRED: alert all).
4. `func_80032904`: subtract. If the result is 0, choose a death clip; otherwise choose a hit reaction (section 3).

Bandit health 75 (bandit1 100, descriptor +0x08). One sword string 51+52+3A = 115 + 3·level, so it kills
a bandit if all three hits connect.

### 2.3 Xena (`func_800367BC`, `0x800371AC..0x80037DB8`) — VERIFIED

- Armor: if `d >= 5` and `D_800F2785 > 0`: `d = max(0, d - D_800F2785)`. That `D_800F2785` is up_armor
  (0..4, capped like the sword at `0x80040580`) is INFERRED.
- Invulnerability timer `D_800F0CC2`: decremented every step. While it is nonzero, damage is cleared
  (`0x80037318`; there are exceptions for `+0x58 == 13` and `D_800F0D0D`).
- **Blocking** (states 0x80..0x83). `a = ratan2(Xena - attacker)>>4 - rotY` (0 = attacker straight ahead):

  | State | Blocks when |
  | --- | --- |
  | 0x80 | a outside 0x400..0xC00 (front ±90°) |
  | 0x81 | 0x401 ≤ a ≤ 0xBFF |
  | 0x82 | a > 0x800 |
  | 0x83 | a < 0x800 |

  Blocked: `d = d > 40 ? d - 40 : 1`, spark, `func_80027790(d)`, `D_800EC586 = 14`. **Every bandit hit
  (≤ 30) does 1 damage through a block.** Attacks from class 0xB2, and from class 0x9F in state 0x1A,
  cannot be blocked.
- Damage is cleared while Xena is in hit states 0x66..0x7F (`0x80037B28..0x80037B60`), so her reaction
  clip works as her invulnerability window.
- Health 500. After damage: if potions `D_800F277E > 0` and health < 500 - 500·30/100 = 350, a potion is
  used (`func_800410C0`).
- Death: health 0 and not already in 0x8A..0x91 -> `func_8003F6AC(0x91)`. It uses 0x8F instead when
  `D_800F0D0D` is set (meaning UNKNOWN). The game-over flow after that is UNKNOWN.

Bandit -> Xena damage (bandit script, VERIFIED; points = the whole bandit `+0x3C` list `0x800C2AA4`,
7 points, no level scaling, `0x8001F9D8..0x8001FB44`, only while the bandit state is 0x3A..0x65):

| Clip | Role (enemy audit) | Damage | Window | Clip frames |
| --- | --- | ---: | --- | ---: |
| 3D | attack (3/11) | 25 | 11-20 | 26 |
| 3E | attack (2/11) | 15 | 7-13 | 30 |
| 3F | attack (3/11) | 15 | 8-15 | 22 |
| 50 | attack (2/11) | 15 | 9-16 | 26 |
| 3C | lunge | 30 | 12-20 | 32 |
| 40 | lunge | 15 | 13-23 | 26 |

Other bandit windows from the script: 3A 15, 3B 30, 41 15, 45 15, 46 15, 48 25, 4C 15, 4F 25, 58 15, 59 15.

## 3. Bandit hit reactions, knockback, death — VERIFIED (read), angle meaning INFERRED

Hit height `s2` (`0x8001E7FC`): `dy = pos.y - hit.y` against `h = descriptor+0x2A` (750). dy < h/3 -> low (0);
dy > h - h/3 -> high (2); otherwise mid (1). `rel` = sp+0x122, the relative angle to the target from
`func_80032748` (0 = ahead); "rear" means 0x400 ≤ rel ≤ 0xC00. Slots come from the family-0 table
`0x800C0184` (index: slot).

| Case (checked in this order) | Slot |
| --- | --- |
| **Survived**: `+0x14 >= 11`, already in 0x66..0x7F, rand(0, `+0x90`/5) = 0 | 7E (idx 18) |
| 1/16 (`func_800B42D8 & 15 == 0`) | 76 (idx 42) |
| rear (any height) | 6A (idx 13-15) |
| Xena in 0x4A..0x4E or 0x5C | 68 (idx 16) |
| Xena in 0x46 | 6F (idx 17) |
| front high / mid / low | 66 / 67 / 6D (idx 10/11/12) |
| **Died**: in 7D/7E | 90 (idx 7) |
| flag sp+0x250 (UNKNOWN) | 8C |
| `+0x144 & 0x20000` and bandit script | 8A |
| rear | 8A (idx 2) |
| attacker in state 0x4F | 90 (idx 3) |
| Xena 0x46 / Xena 0x60 | 8E (idx 8) / 8D (idx 9) |
| high / mid / low | 8C / 8D / 8B (idx 4/5/6) |

Bandit clip lengths (BANDIT.SMA): 66:20 67:28 68:40 6A:36 6D:35 6F:30 76:32 7E:18 8A:76 8B:37 8C:49
8D:33 8E:73 90:81. Slot 0x91 (idx 44) has no clip in BANDIT.SMA. Slot 0x8F is not in the bandit script.

- **Invulnerability**: after every reaction `+0x118 = 8` (`0x8001F4D0`), counting down 1 per step at
  `0x8001DD00`. Damage is ignored while it is nonzero. Dead actors (0x8A..0x91) are skipped by the contact test.
- **Knockback** (`0x8001E06C..0x8001E0CC`): in 0x66..0x7F or 0x8A..0x91 (not 7D/7E) with `+0xE6 < 8`, the mover
  gets distance `ctx+0x24 = 2·clamp(d,10,50)` (20..100 units per step) and offset `ctx+0x26 = rel`. This
  replaces the SMA root motion for the first 8 frames. The direction sign is INFERRED; check it with an
  oracle run before porting.
- **Stun / retaliation**: if `+0xFC == 0` then `+0xFC = rand(90,220)` and attack cooldown `+0x108 = 0`.
  While `+0xFC != 0`, no new cooldown is set after an attack (`0x80022510`) and the idle sidestep is
  suppressed (`0x80020DC4`). The bandit therefore attacks again without delay (INFERRED gameplay meaning).
  Flee: health < max/3 (formula from the enemy audit), 1/4 -> `+0x108 = 0xFFFF`.
- **Blood/spark**: `+0x14 >= 4` -> particle `func_800B2F84` (count 10, or 20 on death), stored at `+0x104`.
- **Death sequence** (script `0x800C9BF6..0x800C9CF2`): clip 8A/8B/8C/8D/8E/90 with sound 46 at frame
  29/19/17/14/33/12 and sound 48 at frame 62/32/31/31/43/52. After that: `setE4 99`, wait 300 game frames,
  `end` -> `+0x94 = 1`. That `+0x94 = 1` leads to removal is INFERRED (enemy audit). The removal itself is
  `func_80070724`.
- **Counters** (VERIFIED): on creation (`0x8007043C`), class flag 0x10 increments `D_800F2774` (live) and
  `D_800F2773`. On removal (`0x80070978..0x800709B8`): if `+0x10 == 0` then `D_800F2775++` (kills), and in
  every case `D_800F2774--` (if > 0). Generator `func_80032C18` compares `D_800F2774` with the limit table
  `D_800C18E7` (INFERRED: maximum number alive).

## 4. Bandit block (mode 4) — VERIFIED (read)

The AI puts bandit1/2/4 into clip 80 (enemy audit). Guard flag sp+0x268 (`0x8001C350..0x8001C3D0`), family 0:
state 0x80 with `+0xE6 < 15`, or state 0x82 with `+0xE6 < 14`. While the flag is set (or `+0x118 != 0`),
**all** incoming damage is discarded (`0x8001E300`), with a spark, and the hit spark at `0x8001D6AC` is
skipped. The one exception is Xena in state 0x48. There is no partial reduction for bandits. Xena hits a
blocking bandit with clang sound 0x1B (`0x80035210`). The script after clip 80 is: clip 82, until frame 9,
then stop.

## 5. Unknowns

- Who writes AI `+0x24` (the per-Xena-state hit lock checked at `0x8001D60C`; the bandit only clears it).
- Flags `D_800F0D18`, `D_800F0D0D`, sp+0x250; Xena's own hit-reaction choice (`func_8002770C`) and the
  game-over flow.
- Chakram damage; the meaning of the special values 10000 / -350 / 150.
- PS1 timing (the native port was used); the knockback direction sign.

## 6. Unity implementation plan

1. Export the action-script windows as data (`decode_action_scripts.mjs --windows` -> JSON). Damage is
   per clip and frame: `+0xFE` is live on frames start+1..end. Reproduce the carry-over (a clip change
   does not clear damage) and the history reset when damage goes from 0 to nonzero.
2. Xena hit: damage = base (×2 under power-up) + sign·swordLevel. Sword points 0..4 for d > 0, kick points
   5..6 for d < 0. Per target per step keep max(|d|). Contact = segment prev->cur, horizontal radius
   `r_target + 50`, vertical span [feet - height, feet]. Skip dead targets.
3. Enemy intake: discard while hit timer > 0 (8 steps), while guarding (bandit1/2/4 clip 80 < 15 frames),
   and for enemy-on-enemy hits. Then knockback (2·clamp(d,10,50) per step for 8 frames), reaction table
   (section 3), retaliation timer rand(90,220), flee rule.
4. Death: clip from the death table, then 300 frames, then despawn; update live/kill counters on removal.
5. Xena intake: armor subtraction, the block sector table, `d > 40 ? d - 40 : 1`, invulnerable during hit
   states 0x66..0x7F, potion auto-use below 350, death clip 0x91.
6. Before porting, oracle-check two items on a RAM snapshot pair: the knockback direction and the frame
   `+0xE6` vs Unity frame index.
   Keep the current Unity prototype numbers (20/15/10 damage, 1.65/1.25 radii) only until steps 1-5 replace them.
