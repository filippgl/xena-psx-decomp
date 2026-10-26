# Weapon contact points: Xena and level-01 bandits (audit 2026-10-26)

Follows `docs/xena-damage-audit.md` (windows, damage, `func_80070DFC`), `docs/xena-hit-reactions.md`
and `docs/xena-hit-effects.md`. Labels: **VERIFIED** = read from the MIPS at the cited address and, where
marked "RAM", reproduced exactly on native-port RAM dumps; **INFERRED** = reading not checked dynamically;
**UNKNOWN** = not found. Sources: `asm/nonmatchings/game/func_8006A6E8.s`, `func_80070DFC.s`,
`func_800367BC.s`, `func_8001B460.s`, `func_8004180C.s`, `func_8006FF38.s`, `asm/gte_asm.s`
(`func_80014DE4/80014E78`), class descriptors in `xena.exe`, XENA/BANDIT TMD/SMI/SMA in `XENA.UDA`.

Tool: `tools/export_contact_points.mjs` (read-only) writes the JSON and runs the RAM check (section 7).

## 1. Summary

- The contact "points" are **fixed offsets on skeleton parts**, read from a per-class list
  (descriptor `+0x3C`), posed with the same part matrices that are used for rendering, **every game step**
  (not only during the damage window). Nothing is hand-tuned per attack: the reach of an attack comes
  only from the animation.
- Xena has 9 points. Sword damage (d > 0) tests points 0..4: sword tip, mid-blade, sword hand, **pelvis**
  and the left hand. Kick damage (d < 0) tests points 5..6: the two feet. The bandit tests all 7 of its points
  (3 along the weapon, pelvis, other hand, two feet) for every attack.
- Contact test per point: target found if the point (or the closest point on the previous->current
  segment, see 4) is **horizontally within `r_target + 50`** of the target position (approximate distance
  `max + 3/8 min`) and **strictly between the feet and `feet - height`** in Y. The attack is not a cone:
  there is no facing test.
- The exported local points reproduce the native game's stored points with **0 units error** in 518 of 520
  checked Xena steps and in every checked live bandit attack step (section 7). The two exceptions are the
  step where the capture script poked Xena's rotation (`sD`, `kD` 9070).

## 2. How a point is computed — VERIFIED

List (descriptor `0x800C33E4 + class*0x44`, `+0x3C`): 16-byte entries `{s16 part, s16 0, s32 a, s32 b, s32 c}`,
terminated by `part == -1`. `func_8006A6E8(actor)` (`0x8006A6E8..0x8006A8B4`):

1. `func_80014DE4(actor+0xA8 instance, rootPart)` builds the part matrices (`instance+0x2C`, 32 bytes each)
   unless the instance flag `+0x28 & 2` says they are already built for this step. Part hierarchy and
   local matrices are the same as in `tools/lib/xena_transforms.mjs` (`Ry * Rx(-x) * Rz`, Q12, s16 local
   translations). The external matrix is `D_800F0790` (identity in every dump); the **root part record holds
   the actor's world position and rotation** (Xena root 27, bandit root 0; RAM: record = position, rotation
   `(0, rotY, 0)`), so the matrices are world matrices.
2. Once per game step (`actor+0x134 != D_800ECEA0`): store the stamp and **swap** the current (`+0x12C`) and
   previous (`+0x130`) arrays (12 bytes per point), then for each entry:
   `point = M[part].T - M[part].R * (a, b, c)` (`ApplyMatrixLV`, `>>12`), written to the current array.
   If `a == 0x13880` the point is instead `M.T + M.R * vertex[b]` of the part's mesh (vertex mode; not used by
   the Xena or bandit lists).
3. Arrays are allocated and filled twice at creation (`func_8006FF38`, `0x800703D8..0x80070400`), so current and
   previous are valid from the first step.

Lists (from `xena.exe`; part role names are INFERRED from the hierarchy and the pose, the numbers are VERIFIED):

| Actor | # | Part | Offset (a, b, c) | Chain to root | Role (INFERRED) | Used for |
| --- | --- | --- | --- | --- | --- | --- |
| Xena 0x5A (`0x800C2954`) | 0 | 26 | 0, 265, 0 | 26>22>21>20>0>28>27 | sword tip | d > 0 |
| | 1 | 22 | 0, 0, 0 | 22>21>... | sword hand | d > 0 |
| | 2 | 26 | 0, 140, 0 | 26>22>... | mid-blade | d > 0 |
| | 3 | 28 | 0, 0, 0 | 28>27 | pelvis | d > 0 |
| | 4 | 11 | -3, -22, -6 | 11>10>9>0>28>27 | left hand (chakram hand, part 32 child) | d > 0 |
| | 5 | 24 | 20, -4, 0 | 24>18>29>23>15>27 | foot | d < 0 |
| | 6 | 14 | -20, -4, 0 | 14>7>12>13>15>27 | other foot | d < 0 |
| | 7, 8 | 0 | 0,-134,-32 / 0,223,-39 | 0>28>27 | torso | not in the damage loop (UNKNOWN use) |
| bandit 0x95..0x98 (`0x800C2AA4`) | 0 | 7 | 70, 309, 220 | 7>6>5>3>2>0 | weapon far end | all |
| | 1 | 7 | 24, 32, 22 | | weapon at the hand | all |
| | 2 | 7 | 46, 158, 115 | | weapon middle | all |
| | 3 | 1 | 0, 0, 0 | 1>2>0 | pelvis / leg root | all |
| | 4 | 10 | 0, 0, 0 | 10>9>8>3>2>0 | other hand | all |
| | 5, 6 | 13, 16 | 0, 0, 0 | ...>1>2>0 | feet | all |

Selection (`func_800367BC 0x8003CE38..0x8003CF80`): `s0 = d < 0 ? 5 : 0`, loop while `s0 < 5` (d >= 0) or
`s0 < 7` (d < 0), calling `func_80035070(Xena, d, cur + 12*s0, prev + 12*s0, ...)` for each point. The other
arguments (0x15/0x18 or 0x1E/0x21, 0x7F, `s0 < 3`) depend on Xena's state (`jtbl_800174EC`) and are passed on
to the sound/flag logic, not to the geometry (INFERRED). Bandit: every point while its state is 0x3A..0x65
(damage audit).

Several points can hit in one step; the victim keeps `max(|d|)` and **the last point that hit** writes the
hit point `+0x18` (RAM: sword hits in `sD` stored point 2 (mid-blade), kicks in `kD` stored point 5 or 6,
bandit hits in `bt` stored point 2 or 0, each **equal to the attacker's current point to the unit**).

## 3. Timing — VERIFIED (RAM)

- The points are recomputed every step whether or not damage is live (`sD`/`kD`/`bt`: exact matches in idle,
  walk and attack steps).
- Frame mapping: in the step where the clip counter `+0xE6 == n`, the points use **SMA frame `n - 1`**
  (0-based, the pose decoded by `tools/lib/xena_animation.mjs`), and the contact test of that same step uses
  those points with the damage `+0xFE` live at counter `n` (windows: `start+1..end`, damage audit).
  The JSON stores `counter = k + 1` with the damage/active point set already resolved.
- Position: the root record used is the actor position/rotation **before this step's movement**. With the
  position of the same dump, errors equal the step's movement (up to 77 units); with the previous step's
  position the error is 0.
- First live step: when the action script sets damage from 0 to nonzero (`0x8004201C..0x80042098`) it writes
  **0 to the x component only** of each point of the current array, in the same step before the points are
  recomputed; after the swap these become the "previous" points. RAM: `sD` 9082 (`51` counter 5) and `kD`
  9080 (`61` counter 4) have previous points `(0, y, z)`. Effect on the test: see 4.

## 4. Contact test `func_80070DFC(attacker, cur, prev)` — VERIFIED (read), first-hit behaviour RAM

For every actor of the list `D_800F131C` (link `+8`), skipping the attacker, radius `+0x28 == 0`, states
0x8A..0x91:

1. Coarse: `dist(cur, target)` < `2 * (r_target + r_attacker)` (600 for bandit vs Xena), with
   `dist = max(|dx|,|dz|) + 3/8 * min(|dx|,|dz|)` (integer `(3*min)>>3`).
2. Candidate point: with a previous point, `n = VectorNormal(prev - cur)` (Q12) and the projection
   `p = cur + n * (n . (target - cur) >> 12) >> 12`. `p` is used only if **`cur < p < prev` strictly on all three
   axes** (the code compares only in this direction, so a segment whose components are not all increasing
   from cur to prev never uses the projection). Otherwise `cur` is tested and, if it fails, `prev` is tested.
3. Fine: `feet - height < p.y < feet` (PSX y down; Xena/bandit height 750) and
   `dist(p, target) < r_target + 50` (same approximate distance; 200 for Xena and the bandits).
4. Hit: `target+0x18..0x20 = p`, `target+0x24 = attacker`; returns the first target in list order.

In all 8 captured hits the stored point equals the current point exactly (no projection, no previous point).
The zeroed-x previous point of the first live step (section 3) makes the "test prev" fallback fail, but in
principle allows a projection on a long segment toward x = 0 if all three strict inequalities hold (INFERRED;
not seen). A port can treat the previous point as invalid on the first live step.

## 5. Bandit upper-body aim twist — VERIFIED (read + RAM), bearing formula INFERRED

Before its contact block, the bandit update (`func_8001B460 0x8001F6F4..0x8001F9AC`) twists two parts
(`D_800C0158[family]`, family `actor+0x38`; family 0 = parts 4 and 3):

- skipped entirely (no twist, no contact) in states 0x66..0x7F and 0x8A..0x91; skipped (contact only) in 0x2F;
- unless `+0xE6 == 1`, the previous twist is first subtracted (`part4.rotY -= +0x114`, `part3.rotY -= +0x11E`);
- target bearing `a` (`+0x110`) = `ratan2` of (self - target `actor+0xEC`) minus own rotY, wrapped to +-0x800, 0
  unless `+0x95 != 0` and `+0x34 == 0`; zeroed outside -0x384..0x384 (family 1: -0x190..0x4B0);
  `+0x112 = trunc(a/3)`, `+0x110 = a - trunc(a/3)`;
- `+0x114` steps 90 toward `+0x110` and `+0x11E` steps 60 toward `+0x112`, **only when farther than the step**
  (a dead band: the value can stay e.g. 60 with target 0; RAM `sD` 9006..9040);
- `part4.rotY += +0x114`, `part3.rotY += +0x11E`.

Part 4 is not on any point chain; **part 3 carries the weapon and the other hand (points 0, 1, 2, 4)**. With the
dump's `+0x11E` applied, the bandit points match exactly (`sD`/`kD`/`swB` clip 3C, twist 60: 46..62 units error
without it, 0 with it). The exact `ratan2` argument order and reference angle were not reproduced from positions
(UNKNOWN); a port can drive `+0x11E` from its own bearing to the target with the limits above.
The JSON gives per frame `twistPivots` (origin, axis in attacker-local space): a rotY of `delta` on part P
rotates every point whose `chain` contains P about `axis` through `origin` by `delta * 2*pi/4096`
(right-handed about the axis, same sign as the part rotation).

## 6. Exported data

`node tools/export_contact_points.mjs --root <decomp> [--out FILE]` (default
`build/contact-points/xena_contact_points.json`, ignored by Git because it is derived from the original
animation data; regenerate it locally). It checks five instruction words in `xena.exe` (the 0x13880 marker,
`r + 0x32`, the 5/7 loop bounds) before export.

Frame and units: attacker-local PSX coordinates, origin = actor position (feet), **y down**, facing **-z**
(forward), x lateral (from the handedness of the GTE frame, +x is the actor's left; INFERRED, the sword
hand is at -x). World = `pos + Ry(rotY) * local` with `Ry = [[c,0,s],[0,1,0],[-s,0,c]]`, `rotY` in
4096 units. Unity importer chain (`export_xena_gltf` scale (1,-1,-1)/400, then Z reflection): metres
`(x, -y, z) / 400` (400 units = 1 m is a project convention).

Content: per actor `points` (part, offset, chain), `radius`/`height`, root part, and per attack clip
`windows` (damage, start, end, live counters, point set) and `frames[k] = {k, counter, damage, active,
points[9 or 7][3]}` (+ `twistPivots` for the bandit). 15 Xena windows in 14 clips, 6 bandit clips, 530 frames.

Live-window summary (active points only; reach = horizontal distance from the attacker origin; h = height
above the feet; angle 0 = straight ahead, + = toward +x):

| Actor | Clip | d | Live counters | Max reach (point, counter, h) | h range | angle range (deg) |
| --- | --- | ---: | --- | --- | --- | --- |
| Xena | 51 sword 1 | 30 | 5-10 | 626 (tip, 8, 327) | 327..838 | -113..93 |
| Xena | 52 sword 2 | 40 | 3-10 | 517 (tip, 9, 524) | 195..713 | -40..168 |
| Xena | 3A sword 3 | 45 | 12-18 | 697 (tip, 17, 385) | 303..790 | -12..165 |
| Xena | 53 | 75 | 12-18 | 627 (tip, 12, 466) | 88..466 | -169..48 |
| Xena | 4F | 80 | 14-26 | 795 (tip, 19, 399) | 237..532 | -45..68 |
| Xena | 55 | 50 | 8-16 | 555 (tip, 14, 749) | 196..754 | -87..61 |
| Xena | 3E | 30 | 1-36 | 524 (tip, 32, 355) | 50..703 | -172..179 |
| Xena | 3C | 15 | 2-40 | 613 (tip, 11, 540) | 10..721 | -166..154 |
| Xena | 61 kick 1 | -35 | 4-12 | 493 (foot 5, 12, 381) | -17..743 | -74..116 |
| Xena | 62 kick 2 | -35/-40 | 1-18 | 491 (foot 6, 13, 665) | -21..694 | -168..168 |
| Xena | 5A kick 3 | -70 | 1-16 | 516 (foot 6, 11, 688) | -14..703 | -165..171 |
| Xena | 56 | -35 | 1-3 | 437 (foot 5, 1, 613) | 3..613 | -28..138 |
| Xena | 46 | -70 | 15-20 | 422 (foot 6, 16, 455) | -10..583 | -175..179 |
| Xena | 5C | -60 | 15-38 | 460 (foot 6, 17, 744) | -15..744 | -174..174 |
| bandit | 3D | 25 | 12-20 | 818 (weapon end, 14, 465) | 37..789 | -120..152 |
| bandit | 3E | 15 | 8-13 | 917 (weapon end, 8, 555) | -13..555 | -73..79 |
| bandit | 3F | 15 | 9-15 | 857 (weapon end, 12, 180) | 50..790 | -112..162 |
| bandit | 50 | 15 | 10-16 | 811 (weapon end, 12, 480) | -13..1039 | -172..176 |
| bandit | 3C | 30 | 13-20 | 714 (weapon end, 13, 746) | 41..746 | -178..174 |
| bandit | 40 | 15 | 14-23 | 715 (weapon end, 20, 576) | 50..749 | -179..178 |

The wide angle ranges come from the feet, pelvis and hands, which sit near the body (the pelvis point is about
150 units ahead at h 390 during sword 51): any target whose cylinder (`r + 50` = 200) contains them is hit
during the window, from any side. Points with h < 0 or h > 750 can never pass the height test against a target
standing on the same floor.

Example, Xena 51 (sword 1), `(x, h, forward)` = `(x, -y, -z)`:

| Counter | tip (0) | hand (1) | mid-blade (2) | pelvis (3) | left hand (4) |
| --- | --- | --- | --- | --- | --- |
| 5 | -246, 838, -106 | -197, 616, 47 | -229, 739, -32 | 34, 403, 152 | 68, 571, 327 |
| 6 | -392, 744, 278 | -214, 540, 239 | -313, 648, 266 | 48, 389, 168 | 181, 441, 378 |
| 7 | -241, 443, 570 | -60, 410, 367 | -153, 427, 482 | 56, 385, 154 | 298, 463, 196 |
| 8 | 298, 327, 550 | 159, 374, 318 | 240, 347, 441 | 64, 385, 122 | 310, 507, 67 |
| 9 | 414, 387, 439 | 186, 392, 284 | 314, 387, 364 | 58, 393, 108 | 281, 528, 5 |
| 10 | 461, 419, 322 | 191, 399, 254 | 341, 407, 287 | 50, 405, 122 | 259, 527, -12 |

Xena 61 (kick 1), feet 5 / 6: counter 4 (-176,319,50)/(52,-17,59), 6 (-152,682,315)/(70,4,37),
7 (-23,742,397)/(45,34,-22), 8 (96,743,393)/(46,26,21), 10 (319,595,349)/(48,0,51), 12 (363,381,333)/(13,13,47).
The measured contact heights of the effects audit follow: sword 418 (`sD`, mid-blade at counter 7, local h 427),
kick 733 (`kD`, foot 5 at counter 7, local h 742), bandit 447 (`bt`, clip 50 counter 12).

## 7. Verification on the native port — VERIFIED (RAM)

`node tools/export_contact_points.mjs --root <decomp> --verify DIR [--actor xena|0x80151270] [--json FILE]`
reads each `ram_*.bin`, takes the actor's state/clip counter, its current points (`+0x12C`), the root record of
the previous dump (stamp `+0x134` exactly one step earlier) and, for the bandit, `+0x11E`, and compares:
**exact** = original integer chain with the dump's root record; **port** = the exported float local point
(+ twist pivot) transformed by `pos + Ry(rotY)`, which is what a port would do with the JSON.

Dumps: existing `build/hit-effects/{sD,kD,bt,swB}` (made 2026-10-25 with `build/port/xena-console.exe` built
2026-10-22 21:11:35; one dump per game step; no new game run for this audit). Results (logs and JSON in
`build/contact-points/verify_{xena,bandit}_{sD,kD,bt,swB}.{txt,json}`):

| Actor / clip | Steps (live) | Exact chain error | Port (float) error max / mean |
| --- | --- | --- | --- |
| Xena 51 sword (`sD`, `swB`) | 100 (42) | 0 in all | 7.9 / 1.6..4.0 |
| Xena 52 sword (`sD`) | 20 (1) | 0 in all | 4.9 / 1.9 |
| Xena 61 kick (`kD`, `bt`) | 73 (39) | 0 in all | 8.2 / 1.7..4.1 |
| Xena 62 kick (`kD`) | 47 (24) | 0 in all | 5.6 / 1.8 |
| Xena idle/jump/reaction 0, 2F, 72 | 280 | 0 except `sD`/`kD` 9070 (rotY poke, 407) | 8.9 / 3.8..4.3 (without 9070) |
| bandit 3C lunge (`sD`, `kD`, `swB`) | 93 (24) | 0 in live steps; <= 5 in 6 pre-window steps | 10.0 / 5.0 |
| bandit 3D (`swB`), 3F (`kD`) | 21 (6) | 0 | 9.4 / 4.9 |
| bandit 50 (`bt`) | 25 (7) | <= 2 (uniform, root y changed by 1..2 during the step) | 7.7 / 4.5 |

Comparing with SMA frame `n` or `n - 2` instead of `n - 1` gives errors of 40..560 units, so the frame mapping is
unambiguous. The float error comes from Q12 rounding in the original matrices (a few units, below 2 % of the
200-unit hit radius). Not covered: bandit hit reactions (0x66/0x67, where twist/contact are skipped) and other clips
of the table (their poses come from the same decoder, which is exact on every clip above).

## 8. Port recipe (replaces the 1.65 m / 1.25 m cone)

Per attacker step, with the position before this step's movement:
1. `counter = clip frame + 1`; `damage`, `active` from the JSON frame (keep the damage audit's carry-over rules).
2. For each active point: world = `pos + Ry(rotY) * local` (bandit: twist part 3 first). Keep the previous
   step's world points; on the first live step mark them invalid.
3. Per target (skip dead, radius 0): coarse `dist < 2 (r_t + r_a)`; candidate = projection if strictly inside
   `cur < p < prev` on all axes, else `cur` then `prev`; hit if `feet - 750 < y < feet` and `dist < r_t + 50`
   with `dist = max + 3/8 min` of |dx|, |dz|. Store the hit point (last hitting point wins), keep `max(|d|)`.

## 9. Unknowns

- Use of Xena points 7 and 8; meaning of the extra `func_80035070` arguments.
- The bandit bearing (`func_800ADB9C` argument order / reference) and families other than 0.
- Behaviour on real PS1 hardware (all checks use the native port's RAM).
