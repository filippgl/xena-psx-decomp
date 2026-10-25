# Xena hit reactions, knockback, invulnerability and death (audit 2026-10-25)

Follows `docs/xena-damage-audit.md` (sections 2.3 and 5), which covers damage numbers, armor and
the block sector table. Labels: **VERIFIED** = read from MIPS at the cited address, or measured in an
existing native-port RAM capture; **INFERRED** = reading of code not checked dynamically;
**UNKNOWN** = not located. Sources: `asm/nonmatchings/game/func_800367BC.s` (Xena update),
`func_8003F6AC.s`, `func_8004180C.s`, `func_8006D3EC.s`, `func_80076888.s`, `func_80053380.s`,
`func_80041228.s`, `xena.exe`, `node tools/decode_action_scripts.mjs --class 5A`,
`node tools/inspect_sma_damage.mjs --slots ...` (clip lengths). No game, build, Unity or smoke run was
started. RAM evidence is from existing captures made with `build/port/xena-console.exe`; the new
read-only `tools/trace_xena_hits.mjs [build]` prints them (5648 snapshots scanned, 32 hit-state rows,
all clip 0x72, no knockdown or death captured).

Correction to the damage audit: `func_8002770C` and `func_80027790` are **not** the reaction choice.
Both only call the pad vibration routine `func_800AA354` (gated by option `D_800F2771`): hit rumble
`(d+60, d/10|1, 4)` and block rumble `(d+60, 0, 7)`. The reaction choice is inline in
`func_800367BC` at `0x80037E68..0x800383F8`.

## 1. Order of the Xena intake step (`0x800371AC..0x800383F8`) — VERIFIED

1. Companion hit filter, armor `D_800F2785` (damage audit 2.3).
2. Item timer `D_800F0CC2` (see section 4). It is **not** a post-hit timer.
3. Dead states 0x8A..0x91: run the death camera `func_80053380` and skip the rest of the update (`0x80037908`).
4. Block (states 0x80..0x83, sector table in the damage audit): `d = d > 40 ? d - 40 : 0`, then `d <= 0 -> 1`;
   spark `func_800291A0(hit,d,1)`, block rumble, block push `D_800EC586 = 14`.
5. Damage cleared (`0x80037B60`) when Xena state `+0xD0` is 0x66..0x7F, or when `D_800F0C7C != 0`,
   state == `D_800F0C7C` and `+0x58 != 13`. Both are bypassed by fall damage (`D_800F0D0D`) and `D_800F0EF4`.
6. `d >= 4` and `+0x14 == 0`: hit spark `func_800291A0(hit,d,0)`. `+0x14 = d`.
7. `d >= 2`: hit rumble `func_8002770C(d)` and a blood particle `func_800B2F84` attached at `+0x104`.
8. `func_80032904` subtracts. Health 0 and not already dead: `func_8003F6AC(D_800F0D0D ? 0x8F : 0x91)`.
9. Potion auto-use below 350 (max 500 - 30 %), power-up timer tick.
10. **Reaction choice** only if `d >= 2` (so every blocked bandit hit, which deals 1, never staggers Xena).

## 2. Reaction choice (`0x80037F50..0x800383F8`) — VERIFIED

**No reaction** (damage is still applied). `D_800F0C7C = current state` is stored instead (`0x800383FC`),
so later hits are discarded until the state changes or restarts at frame 1 (`0x80037158..0x8003717C`):
- requested action `+0xE2` = 0x48, 0x4A, 0x4B, 0x4C, 0x4D, 0x5C, or 0x64 while `D_800F0D17` (item bit 4);
- state already 0x66..0x7F;
- state 0x2D..0x35 (jump / air moves; 0x2F = standing jump per `xena-jump-audit.md`), unless the attacker
  is class 0x78 and `D_800EE458 != 0`;
- `D_800F0CC8 != -1` and state != 0x5F (`D_800F0CC8` meaning UNKNOWN; -1 in all captures).
Attackers of class 0x92/0x93 never cause a reaction (`0x800383B4`). `func_8003F6AC` also refuses every
state change while Xena is in state 0x37 (pickup animation, `0x8003F6D4`).

**Inputs.** `rel` = `func_80032748(attacker pos, Xena)`: `atan2(dx, dz)` of (Xena - attacker), in
4096 units, minus Xena rotY (`ctx+0x18 -> +0xE`). Measured: Xena-attacker (440, 253) -> 684 absolute,
rotY 0x992, stored rel 0x919 = (684 - 0x992) & 0xFFF. `rel` near 0 means the attacker is in front
(INFERRED from the front guard 0x80, which blocks `rel` outside 0x400..0xC00). Sector index
`s = (rel + 0x200) & 0xFFF` (`0x800380A8..0x800380CC`). Height: `dy = Xena.y - hit.y` (`+0x1C`) against
Xena height 750 (descriptor `0x800C4BCC + 0x2A`): `dy < 250` low, `dy > 500` high, else mid.

| Sector `s` | Meaning (rel) | Low | Mid | High |
| --- | --- | --- | --- | --- |
| < 0x400 | front +/-45 deg | 0x70 | rand -> 0x66 (2/3) / 0x67 (1/3) | same as mid |
| front and (`d >= 30` or attacker class 0xB2) | knockdown | 0x7C | 0x7C | 0x7C |
| 0x400..0x7FF | side A (0x200..0x5FF) | 0x70 | 0x74 | 0x74 |
| 0x800..0xBFF | rear +/-45 deg (0x600..0x9FF) | 0x72 | 0x72 | 0x72 |
| >= 0xC00 | side B (0xA00..0xDFF) | 0x74 | 0x75 | 0x75 |

`rand` is `func_800B4350(0,2)` (inclusive, `0x800B43A4`). Left/right naming of the two sides is
UNKNOWN (sign of rotY vs. model forward not checked). Knockdown is only possible from the front
sector. Bandit damage is 15..30 (damage audit), so only the 30-point bandit clips 3B/3C knock Xena
down from the front. Big front hits also play a voice/sound by attacker class (`jtbl_80017414`, classes
0x75..0x9D; default: sound 0x1063 1/4 on levels 10/11).

**Overrides** (applied in this order after the table): attacker class 0x9E in state 0x3C -> 0x7B, in 0x3B -> 0x70;
class 0x90 or 0x8B -> 0x7B; class 0x9F -> 0x70 (its 0x1A/0x41 -> 0x7C write at `0x80038368` is always
overwritten by `0x80038398`); flag `D_800EE457` -> 0x70 and clear it.

**After choosing** (`0x800382C8..0x800383F0`): `+0x10A = min(d, 30)`; `func_8003F6AC(clip, 0)`, which
for 0x66..0x7F calls `func_80041568(Xena, clip, 1)` immediately; `D_800F0D30 = rel`;
`D_800F0D2C = 4` unless the clip is 0x7B/0x7C; `D_800F0C7C = 0`.

Clips present in XENA.SMA (frames): 66:9, 67:18, 70:37, 72:10, 74:10, 75:10, 7B:13, 7C:26, 88:37,
8F:10, 91:65. 0x71 (10) and 0x73 (26) exist but no code path above selects them (UNKNOWN use).
**There is no block-hit clip**: a block gives spark, rumble and push only. A blocked hit that still deals
`d >= 2` (raw > 41) leaves the guard through the normal table.

## 3. Knockback and control lock — VERIFIED (code + capture), distance scale INFERRED

- **Push** (`0x80038834..0x80038894`): while state is 0x66..0x7F at `+0xE6 == 1` and `D_800F0D2C > 0`:
  `+0xE0 = 0` (script frozen: the interpreter returns at `0x80041840` when `+0xE0 == 0`), `D_800F0D2C--`,
  mover `ctx+0x24 = +0x10A`, `ctx+0x26 = D_800F0D30`. When it reaches 0, `+0xE0 = 1` and the clip runs.
  Capture `build/action-reference-bicycle-target` 9148..9178 (bandit clip 50, d = 15, clip 0x72):
  four pushes of (+12, +7) = 13.9 units each = 55.6 units total, **away from the attacker**, then frames
  1..10, then state 0. Total lock = 4 + clip frames. 0x7B/0x7C get no push (root motion from the SMA, INFERRED).
- **Block push** (`0x800387B8..0x80038830`): `ctx+0x24 = D_800EC586` (14, 12, ..., 2 per step, 56 total), along `rel`.
- **Control lock** (`0x80038DF8..0x80038E84`): the whole input section is skipped while `+0xD0` or `+0xE2`
  is 0x66..0x7F (or 0x8A..0x91). Capture: SQUARE pressed at 9152 during the freeze queued nothing; in
  `build/btnprobe` a held run was taken (`+0xE2 = 0x15`) on the first idle step (10414).
- **Get-up**: script `0x800C8C20` / `0x800C8C44`: `clip 7B|7C; untilEnd; clip 88; untilEnd`. During 0x88 the
  state is outside 0x66..0x7F but `+0xE2` stays 0x7B/0x7C, so input stays locked while damage and new
  reactions are allowed again (INFERRED from the range checks; not captured).

## 4. Invulnerability — VERIFIED

- Hit states 0x66..0x7F only (no Xena equivalent of the bandit `+0x118 = 8` timer). Lengths:
  66: 13, 67: 22, 70: 41, 72/74/75: 14 steps (72 measured: 14); 7B: 13, 7C: 26, then get-up 0x88 (37) vulnerable.
- Super armor (`D_800F0C7C`, section 2): the first hit lands without reaction, later ones are void until the state changes.
- Dead states 0x8A..0x91: the contact test `func_80070DFC` skips the actor.
- `D_800F0CC2` = 450 steps set by **item use** (`func_80041228`, pad bit 0x800): item bit 0x10 (count
  `D_800F2782`) sets `D_800F0CDC` and all damage is cleared while the timer runs (`0x80037318`); items
  bit 2/8/0x20 use the same timer for other effects. Item names are UNKNOWN.
- Respawn: `D_800F0CD4 = 60` blinks Xena (`+0x144` bit 4 toggled, `0x80036AC4`); no damage gate found for it.

## 5. Death and what follows — VERIFIED unless marked

- Clip 0x91 (script `0x800C8C68`): play 0x91, at frame 2 opcode 0x8021 = sound rand(0x1013, 0x1014)
  (`0x8004212C`), to end (65), wait 90 game frames, opcode 0x8001 `stop` (`+0xE0 = 0`, `0x80041C14`).
- Clip 0x8F when the killing damage was fall damage (`D_800F0D0D`, set at `0x8003B834` when
  landing drop > 2000: `d = drop - 2000`, or 1000 when `D_800F0EF4`): 10 frames, wait 180, stop.
- While dead: death camera `func_80053380`, `D_800F0F90 = 1` (blocks `func_800410C0` potions). A hit taken
  in pickup state 0x37 delays death until that state ends (death is retried every step, INFERRED).
- Game over trigger (`0x800769E8`): Xena in 0x8A..0x91, `+0xE0 == 0` and `func_8008626C(0x10DD) == 0`
  -> `func_8006D3EC(0)`. Falling more than 2000 below `+0x48` (levels 1..3) or to y > 20000 (other
  levels) calls it directly, without a death clip (`0x80036E0C..0x80036E64`).
- `func_8006D3EC(0)` with continues `D_800F277C > 0` (added by the continue pickup, which also stores
  `D_800F0D20`): health = 500, position = `D_800F0D20`, state 0, `D_800F277C--`, blink 60. Otherwise:
  `D_800F2648 = 0x11` (game-over screen, INFERRED), `D_800F10E0 = 9000`, `D_800F10F0 = 20`; from game
  state 9 it calls `func_80068E90` + `func_8006C18C` instead. Reason 1 (companion dead, levels 9/10/11/21)
  -> 0x12, reason 10 -> 0x10. The screens themselves were not traced (UNKNOWN).

## 6. Unity implementation table

| Rule | Value | Status |
| --- | --- | --- |
| React threshold | final damage >= 2 | VERIFIED |
| Sector | `s = (rel + 512) & 4095`, rel 0 = attacker in front | VERIFIED code, front INFERRED |
| Height bands (h = 750) | low < 250 <= mid <= 500 < high, measured from the feet | VERIFIED |
| Front clip | low 0x70; mid/high 0x66 (2/3) or 0x67 (1/3); d >= 30 -> 0x7C | VERIFIED |
| Side A / rear / side B | 0x70/0x74/0x74; 0x72; 0x74/0x75/0x75 (low/mid/high) | VERIFIED |
| No reaction | attack actions 0x48, 0x4A..0x4D, 0x5C; jump/air 0x2D..0x35; pickup 0x37 | VERIFIED |
| Knockback | 4 steps x min(d,30) units/step (13.9 measured for 15) away from attacker, clip frozen on frame 1 | VERIFIED / scale INFERRED |
| Knockdown | 0x7B (13) or 0x7C (26), then get-up 0x88 (37), no push | VERIFIED |
| Input lock | whole reaction incl. freeze; also get-up | VERIFIED / get-up INFERRED |
| Invulnerable | while in reaction clip; not during get-up | VERIFIED |
| Block feedback | spark + push 14,12..2; no clip; d > 41 still staggers | VERIFIED |
| Death | 0x91 (+sound at frame 2) or 0x8F after a fall, then wait 90/180, then continue or game over | VERIFIED |
| Respawn | full HP at last continue point, idle, 60-step blink | VERIFIED |

Before porting, check the side-A/side-B mirror and the knockdown root motion with a capture that
contains 0x74/0x75/0x7C (none exists in `build/`).
