# Chakram damage rules (audit 2026-10-25)

Labels: **VERIFIED** = read from the MIPS at the cited address, and "capture" where a native-port RAM dump
shows it; **INFERRED** = a reading of code that was not checked dynamically; **UNKNOWN** = not found.
Builds on `xena-damage-audit.md` (intake, reactions) and `xena-hit-effects.md` (effects).

Sources: `asm/nonmatchings/game/func_8003DDEC.s` (chakram update), `func_8006E78C.s` (mover, actor contact
at `0x8006F51C..0x8006F878`), `func_80070BD4.s` (body overlap test), `func_8003F3F0.s` (relocate),
`func_8001B460.s` (bandit-AI intake), `func_80032904.s` (health/karma), `func_80041228.s` (item use),
class descriptors `0x800C33E4 + class*0x44` in `xena.exe`.

Captures: `build/port/xena-console.exe` (build 2026-10-22 21:11, the native static recompilation), run from
`decomp/` by `build/chakram-damage/tools/run.py`, analysed by `build/chakram-damage/tools/scan.py`
(full per-step tables in `build/chakram-damage/<run>/scan.txt`; RAM dumps kept only for the hit frames).

## 1. The chakram actor

| Item | Value | Label |
| --- | --- | --- |
| Class | 0x20, name string `chakram` (descriptor `0x800C3C64`, name ptr `0x800EE02C`) | VERIFIED |
| Update function | `func_8003DDEC` (descriptor +0x1C) | VERIFIED |
| Actor pointer | `D_800EE17C` (written in Xena's update at `0x80039800`); 0x80151930 in every capture | VERIFIED (code + capture) |
| Body radius / height | +0x28 = 100, +0x2A = 20 (descriptor `00140064`; same in the live actor) | VERIFIED (capture) |
| Spawn | Xena x/z, y = Xena y - 500 (capture: Xena -1837, chakram -2337 / -2327) | VERIFIED (capture) |
| Speed | first step about 400 units (init move 300 at `0x8003DF98` + 100), then 2 sub-moves of 100 = about 200 units/step out and back | VERIFIED (code + capture: 398, then 198 per step) |
| Lifetime | step counter +0xDC; after 180 steps (150 once anything was hit, +0x10A = 1) it turns to Xena (`0x8003F1F4..0x8003F29C`) | VERIFIED (code) |
| Catch | contact with Xena in the mover (`+0xF0 == Xena`, `0x8003EB7C`) or, while returning, distance < 200 (`0x8003E698`) -> `func_8003F52C` | VERIFIED (code + capture `hpR` 9134) |

Flags (all gp globals): `D_800F0D3D` = returning to Xena, `D_800F0D3C` = steer to Xena, `D_800F0D3E` = already
relocated once, `D_800F0D38` = contact counter, `D_800F0D40` = homing target, `D_800F0D17` = homing item active.

## 2. How it finds an enemy — VERIFIED (code + capture)

The chakram does **not** use the weapon-point test `func_80070DFC`. Each step `func_8003DDEC` calls the
mover `func_8006E78C(chakram, heading+0x800, ±100, 0)` up to twice. The mover tests the proposed position
(current + step) with the body-overlap test `func_80070BD4` (`0x8006F548`):

- candidates: every actor in `D_800F131C` except the chakram, with radius != 0 and height != 0, descriptor
  flag 0x40 clear; actors with health 0 or in states 0x8A..0x91 are skipped (except classes 0xB2 dragon,
  0xA0 ogreg); actors with descriptor flag 0x8 only collide with Xena;
- vertical: open overlap of `[y-20, y]` (chakram) with `[y_t - h_t, y_t]` (bandit h = 750);
- horizontal: `max(|dx|,|dz|) + 3/8*min(|dx|,|dz|) < 100 + r_t` (bandit r = 150, so 250);
- the first actor found is returned; the sub-move is blocked and `chakram+0xF0 = target` (`0x8006F674`).

Exceptions in the mover for the chakram (`0x8006F5B0..0x8006F638`): while a homing target other than Xena
exists (`D_800F0CBE`) Xena is ignored; with the homing item the companion `D_800EE178` and peasants 0xAA/0xAB
are ignored. Class 0x63 (`exit`) is only hit by Xena. On a hit the mover also writes the target mover
context: `+0x26` = angle chakram->target, `+0x24` = the step length, and puts an idle bandit-AI target
(`func_8001B460`/`func_80023018`) into alert `+0x10C = 1` unless it is 0x99 or 0x73.

Additionally, every step, any idle bandit-AI actor within approximate distance 360 of the chakram is set to
`+0x10C = 1` (`0x8003DFE4..0x8003E114`, not class 0x99). Meaning "alert" is INFERRED.

## 3. Damage — VERIFIED (code + capture)

On a step with `chakram+0xF0 = T` and `T != Xena` (`0x8003EB60..0x8003EC5C`):

```
if D_800F0D17 (homing item):  T+0x12 = 500          ; overwrite
else:                         T+0x12 += 300         ; added to pending damage (no max rule)
if T.class == 0x78 (sorcerer): T+0x12 -= 299        ; 300 -> 1, 500 -> 201
T+0x24 = chakram ; T+0x18..0x20 = chakram position (contact point for effects/height)
```

`D_800F0D64` ("held actor", gets `+0x12 = +0x10`, i.e. its full health) is only ever cleared in the
game asm; no nonzero writer found (UNKNOWN/unused).

Not involved (VERIFIED): `func_8003DDEC` reads neither the sword level `D_800F2786`, the power-up timer
`D_800F1136`, throw charge nor distance. There is no charge: damage is a constant per contact.

| Run (`build/chakram-damage/`) | Setup | Hit frame | Bandit +0x14 / hp | Notes |
| --- | --- | --- | --- | --- |
| `base` (= old `chD`) | sword 0, R1 held | 9132 | 300 / 75 -> 0 | death clip 8D, caught 9138 |
| `sw4` | `D_800F2786 = 4` | 9132 | 300 / 75 -> 0 | sword level ignored |
| `pu` | `D_800F1136 = 825` (power-up) | 9132 | 300 / 75 -> 0 | no x2 |
| `hm` | `D_800F0D17 = 1` | 9132 | **500** / 75 -> 0 | target lock 0x80151270, then Xena; item flag cleared at catch |
| `hp` | bandit hp 2000, R1 held | 9132 | 300 / 2000 -> 1700 | reaction 76, knockback about 100/step, one hit |
| `hpR` | hp 2000, R1 released | 9132 | 300 / 2000 -> 1700 | reaction 67, chakram bounced into Xena, caught 9134 |
| `far` | hp 2000, bandit 2500 units out | 9154 | 300 / 2000 -> 1700 | outbound hit, bounce, wall at 9186, return |
| `ret` | hp 2000, bandit placed on the return path | 9248 | 300 / 2000 -> 1700 | **hit while returning**; contact again at 9250/9252 discarded (hit timer 7, 6) |

Common pokes (frame 8998, size 4): bandit pos `0x801E09BC/C0/C4` = (-8062, -1837, -18358); Xena rotY
`9068:0x801BB4A2:0x1B0:2`; script `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS,9070:R1:120,9090:R1+CROSS:8`
(R1 released runs: `9070:R1:28`). `free`/`far`/`ret` move the bandit away (`8998:0x801E09C4:-1000:4`) and
re-place it at frame 9126 / 9220. Exact command lines: `<run>/cmd.txt`. The `--poke` default size is not
4 bytes: a negative value without `:4` was written truncated (first `sw4`/`pu` attempt, discarded).

## 4. Hits per throw, repeat hits, bounce — VERIFIED (code + capture) unless marked

- **Both legs deal damage**: the contact branch is not gated by the return flag (capture `ret`).
- **No per-throw hit list.** The same enemy can be hit again later in the throw. Repeat contacts in
  consecutive steps are absorbed by the target, not by the chakram: bandit-AI enemies discard all damage
  while their hit timer `+0x118` (8 steps after each reaction) runs (`ret` 9250/9252: hp stays 1700).
  `+0x14` still shows 300 there because the bandit copies `+0x12` to `+0x14` (`0x8001D7C4`) before the
  discard; use health, not `+0x14`, as the oracle.
- **Number of enemies**: no explicit limit. Without the homing item every contact step (enemy or wall)
  increments `D_800F0D38`; on the 4th (> 3) the chakram is relocated by `func_8003F3F0` to 2000 units in front
  of Xena, y = Xena y - 400, and flies back (`ret` 9252); a second overflow ends the throw (`func_8003F52C`).
  With the homing item, contacts with enemies (descriptor flag 0x10) do not count (`0x8003ED94..0x8003EDD8`).
- **After an enemy hit**:
  - steering mode (R1 held: `D_800F10F4->+0x114->+4 == 2`, `sp+0x1D8`) -> the chakram returns to Xena at once
    (`base`, `hp`);
  - otherwise it reflects: `heading = heading + 0x800 - 2*(atan(v) - atan(chakram - target))`
    (`0x8003EC7C..0x8003ED50`; `hpR` 0x189 -> 0x921, `far` 0x992 -> 0xECE) and keeps flying.
  The blocked sub-move means it does not pass through the enemy on that step.
- Homing (`D_800F0D17`): target = nearest (approx distance < 100000) actor with flag 0x10, not Xena/companion,
  not class 0x74/0xA0/0xAA/0xAB/0x99/0x73, not dying, `+0x12 == 0`, and not in a hit reaction except bandit-script
  states 0x6B/0x6C/0x74/0x75 (`0x8003E158..0x8003E2F8`); none -> Xena. Steering is cancelled while homing
  (`0x8003E838..0x8003E894`). After the kill the next target was Xena in `hm` (only one enemy present).
- Wall contact (mover blocked without an actor): reflect off the wall normal (`func_800A2E98`), yellow spark
  emitter (255,255,100) at the chakram, sound rand(0x19,0x1A), always set returning, count +1; rumble
  `func_8002770C(300)` only in steering mode (`0x8003EE24..0x8003EFAC`, `0x8003F2A0..0x8003F3B8`).

## 5. Target side

Everything after `+0x12` is the target's normal intake (`xena-damage-audit.md` 2.2-3). For bandit-AI
enemies (`func_8001B460`, bandit/bandit1/2/4, bdtking, amqueen, king, kalabrax, sldrelte):

| Rule | Detail | Label |
| --- | --- | --- |
| Immune | classes 0x99 `bdtking` and 0x73 `amqueen` clear damage whose attacker is the chakram (`0x8001C058..0x8001C0A8`) | VERIFIED (code) |
| Reduced | 0x78 `sorcerer`: 1 per hit (201 with homing) | VERIFIED (code) |
| Block | guard flag (bandit1/2/4 clip 80 early frames) or hit timer -> all damage discarded, blue spark; applies to the chakram as well | VERIFIED (code, generic path) |
| Elite soldier | class 0x9D `sldrelte`: while a non-homing chakram exists, flag `sp+0x270` drives its guard/block decisions (`0x8001B518..0x8001B54C`, `0x8001DEE0`, `0x8001E1D0`, `0x80020410`) | INFERRED (meaning not traced) |
| Enemy-attacker filter | not applied (chakram flags 0x26 lack 0x10) | VERIFIED |
| Health | `hp = max(0, hp - d)`; 300 kills every level-01 bandit (75/100) | VERIFIED (capture) |
| Hit effect | `func_800291A0(contact, d, 0)`: type 0 red (attacker not Xena), d = 300 -> flash life 12, ring 7, 6 sparks | VERIFIED (capture `chD`) |
| Blood | `+0x14 >= 4` -> emitter, 10 steps (20 on a kill) | VERIFIED (code) |
| Reaction / death | normal table with the contact point = chakram centre (500 above the feet -> mid band for h 750): survived 67 (`hpR`), 6A rear (`far`, `ret`), 76 random (`hp`); died 8D (`base`) | VERIFIED (capture) |
| Death facing | death clip 8B/8D by the chakram -> bandit heading = chakram heading + 0x800 (`0x8001EDCC..0x8001EE20`) | VERIFIED (code) |
| Knockback | `2*clamp(300,10,50) = 100` units/step for 8 frames | VERIFIED (capture: about 100/step) |
| Karma meter | no change: `func_80032904` only scores kills whose attacker is Xena (`0x800329CC`) | VERIFIED (code) |
| Hit sound | class 0xB4 `c1`: 0x16; else 0x1B or 0x1C (random) via `func_80084AD0` at the target | VERIFIED (code) |

The companion is not excluded by the mover without the homing item, so a non-homing chakram could deal
300 to it (INFERRED, not captured). Non-bandit-AI enemies (ogreg, minotaur, dragon, ...) use their own
intake (not traced; UNKNOWN).

## 6. Implementation table (Unity)

| Rule | Value |
| --- | --- |
| Contact shape | chakram cylinder r 100 / h 20 vs target cylinder r_t / h_t, at the next 100-unit sub-step, approx-distance metric `max + 3/8 min` |
| Damage per contact step | 300 (homing item: 500); sorcerer 1 (201); bdtking/amqueen 0 |
| Scaling | none (sword level, power-up, charge, distance ignored) |
| When | outbound and returning; first actor found per sub-step |
| Re-hit | allowed; target's own hit timer (bandit 8 steps) absorbs consecutive contacts |
| After enemy hit | R1-steered: return to Xena; else reflect heading off the target and continue |
| Contact budget | 4 contact steps (enemy or wall, enemies not counted with homing) -> relocate 2000 ahead of Xena and return; next overflow ends the throw |
| Lifetime | 180 steps, 150 after any hit -> return to Xena; catch within 200 units or on body contact |
| Target intake | normal: guard/hit-timer discard, reaction table, knockback 100/step x 8, blood, red spark |
| Karma | none for chakram kills |
| Speed | about 200 units/step (400 on the first) |

## 7. Unknowns

- Name and pickup of item bit 4 (`D_800F2787 == 4` sets `D_800F0D17`, `func_80041228` `0x80041410`).
- Writer of `D_800F0D64`; meaning of `sp+0x270` for class 0x9D; intake of non-bandit-AI enemies.
- PS1 hardware timing (all captures are from the native port).
