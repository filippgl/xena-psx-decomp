# Bandit AI mode 6 ("avoid") and the mover's contact result (2026-10-26)

Scope: `func_8001B460` (bandit update) mode 6 entry/action/exit and what `func_8006E78C` (mover) returns,
enough to port to Unity. Labels: VERIFIED = code read and confirmed in a native RAM run; CODE = read from the
asm, not exercised; INFERRED; UNKNOWN. Builds on `xena-enemy-audit.md` section 3 (mode table, perception).

Conventions (VERIFIED): angles are 4096 per turn. The transform struct (`actor+0x5C`, same as `ctx+0x18`)
holds x,y,z at +0/+4/+8 and rotY at +0xE. Forward is `F = (sin(rotY+0x800), cos(rotY+0x800))` in (x,z):
run D, ctx+0x26 = 0x800 and ctx+0x24 = +23 for walk, and the bandit moves +23 along F. The lateral axis
used below is `L = (cos(rotY+0x800), -sin(rotY+0x800))`; `rotY += d` turns F toward L. `func_80034FD0(a, d)`
= `rotY = (rotY + d) & 0xFFF`. Distances are `max(|dx|,|dz|) + 3/8 min` (horizontal). The AI runs on every
second frame of the port (positions change on alternate frames). `turnMax` = descriptor +0x36 = 64.

## 1. Mover contact (`func_8006E78C`, `0x8006F51C..0x8006F878`) — CODE + VERIFIED where marked

- Runs only if `self+0x28` (radius) != 0. Candidate = first actor of list `D_800F131C` (linked by +0x8)
  returned by `func_80070BD4(self, proposedPos)` (`0x80070BD4`):
  - skip self, actors with radius +0x28 = 0 or height +0x2A = 0, descriptor flag `+4 & 0x40`;
  - skip dead ones (`+0x10` health 0 or clip `+0xD0` in 0x8A..0x91) unless class 0xB2/0xA0;
  - descriptor flag `+4 & 0x8`: only Xena (`D_800EE174`) collides with it. Level-01 pickups, siras and signs
    (classes 0x5C/0x5E/0x62/0xF8, flags 0x40D, RAM 0x48D) have it, so for bandits the only blockers in
    level 01 are Xena (flags 0x817) and other bandits (0xC93) — VERIFIED (RAM run D: sign collides only
    after clearing bit 0x8);
  - vertical spans `[y - h, y]` (h = +0x2A; 750 bandit) must overlap strictly;
  - horizontal: `dist(proposed, other) < self.r + other.r` (bandit vs bandit: < 300).
- Escape rule `0x8006F570..0x8006F5AC`: if the blocker has no flag 0x8 and is not class 0x63, the test is
  repeated at the *current* position; if self already overlaps that same actor there, the contact is ignored
  (moving out of an overlap is allowed). VERIFIED (run C: a blocker placed on top of the bandit, with bit 0x8
  cleared, never stopped it; the 0x40 poke in that run was a mistake, rerun as D).
- On a contact: `self+0xF0 = blocker` (`0x8006F674`), the blocker gets the step as a push:
  `blocker.ctx+0x26 = atan(blocker - self) - blocker.rotY`, `blocker.ctx+0x24 = step` (`0x8006F7AC..D0`)
  (VERIFIED written: sign ctx 23/2276 in run D; the blocker's own mover would then move it away from the pusher
  by one step — that is INFERRED, the sign never moves). If the blocker is a bandit (`update` 0x8001B460 or
  0x80023018) in mode 0 or patrolling (+0x34), it is woken: mode 1, +0x34 = 0 (CODE). **Return 0, position
  unchanged.** VERIFIED (run D: 12 AI steps with the position and rotY frozen).
- `+0xF0` is not cleared by the mover or the bandit AI; it stayed = the sign while the bandit moved freely
  (run D 9043..9161). The AI copies it to `+0xF4` after each mover call when non-zero (`0x8001E14C`,
  `0x8001FD60`). So **+0xF4 = last non-null contact, sticky**. (A capture had +0xF0 = 0, +0xF4 = Xena, so
  something else clears +0xF0 at times; site UNKNOWN.)

## 2. Mover return value (as needed for the bandit) — CODE

Return is `s7`, or 0 on early exits. Order: displacement `(sin h, cos h) * ctx+0x24` with
`h = rotY + ctx+0x26` (if the distance is 0 the velocity +0x2C/+0x2E/+0x30 is used); wall sweep
`func_800A2E98` with the actor radius:
- wall hit -> `s7 = 0`, position = slid/corrected point (`0x8006ED18..0x8006EE70`);
- no hit -> `s7 = 1`, then two whisker sweeps at +-0x200 (45 deg) (`0x8006EE84..0x8006F4A0`) that can set
  `s7 = 0` for non-Xena actors;
- actor contact (section 1) -> return 0, no move;
- ground `func_8003423C`: step up larger than 300 (`sp+0x100`) for a non-Xena actor -> return `s7` **without
  moving** (`0x8006FA28 -> 0x8006FC80`); some special-class cases return 0 (`0x8006FA54..0x8006FB18`).
  Otherwise the position is written and `s7` returned.

Port meaning: **1 = moved without touching a wall/actor; 0 = blocked by an actor (no move) or slid along a
wall (moved).** A too-high step keeps the previous result (usually 1) and does not move. The AI stores it at
sp+0x240; it gates the lunge in mode 2 and the mode-6 entry.

## 3. Mode 6 entry (`0x80020048`) — CODE, entry VERIFIED

Runs every AI step for every mode, after the mover, the heading jitter / flee code and the clamp of the
target turn (sp+0x110), unless the bandit is in a hit/attack/death clip (0x66..0x7F, 0x8A..0x91, 0x3A..0x65
skip all decisions at `0x8001FEDC`). Condition:

```
moverResult == 0 && blk = self+0xF4 != null && blk != self+0xEC (target, Xena)
&& dist >= 3001       // sp+0x114: distance to the current goal point (below)
```
then: if `blk.mode (+0x10C) == 6` -> end this AI step (no mode change, **no action**); else `mode = 6` and end
the step (no action this step). The goal point (`0x8001DE10`): patrolling (+0x34 != 0) -> waypoint +0x60 when
+0x36 != 0, else home +0x98; not patrolling -> target position. So a bandit within 3000 of Xena (or of its
goal) never avoids; it just stays blocked. Entry possible from modes 0, 1, 2, 3, 4, 5, 7 (any mode).

Because the check pre-empts the action, a bandit whose next step still overlaps the blocker stays frozen
(no move, no turn) every step until the blocker moves (push / its own AI) or the clip's root motion changes
direction. VERIFIED (run D: frozen 9017..9041, freed when the walk clip 15 -> 16 changed the step; frozen
again 9063..9075 and 9085..9089).

## 4. Mode 6 decision and action — VERIFIED

The decision entry for mode 6 (`jtbl_80016A90[6]` = `0x80020FCC`) does nothing. Action `0x8002103C`:

```
blkPos = blk+0x5C; R = blk.radius(+0x28) + 700      // bandit blocker: 850
d   = dist(self, blk)                                // max + 3/8 min
dot = (dx*sin(a) + dz*cos(a)) / 4096                 // a = rotY + 0x800, dx,dz = blk - self (F component)
lat = (dx*cos(a) - dz*sin(a)) / 4096                 // L component
if dot < 0 && d > R:                                 // blocker behind and clear
    rotY += turnToGoal                               // sp+0x110, clamped to +-64
    if self+0x78 != 0: mode = 1                      // +0x78 = has leash anchor (+0x6C..0x74) INFERRED
else if |lat| < R:
    rotY += (lat < 0) ? +64 : -64                    // turn away from the blocker's side
else:
    rotY += turnToGoal; mode = 1
clip: sp+0x250 set -> raw clip 4 (meaning UNKNOWN); target class 0xAA/0xAB or fleeing -> run;
      else d(blocker) < 1700 (desc +0x30) -> walk 0x15 (kept if clip is 0x14..0x18), else run 0x1A
      (kept if 0x19..0x1F). Family-0 words 38/39 (`D_800C021C`/`D_800C0220`) = 0x15/0x1A (EXE).
```

So the bandit turns away at 64 per step (5.6 deg) while walking (~23 units per step); once the blocker is
behind it keeps turning until the blocker is straight behind (lat -> 0, visible as a +-64 dither), walks away
until `d > R`, then turns toward its goal; it leaves to mode 1 when the blocker is ahead/side with
`|lat| >= R` (its goal line clears the blocker), or right when it is behind and clear if `+0x78 != 0`.
There is no timer. The observed level-01 bandit has `+0x78 = 0` (all runs), so it uses only the last exit.

Evidence (`build/port/xena-console.exe`, built 2026-10-22 21:11; level 01, CROSS script
`7400,7700,8000,8300`, bandit 0x80151270, its pos struct 0x801E09BC, ctx 0x801BDFB0, sign 0x8014D400 at pos
struct 0x80119E18, sign descriptor flags word 0x800C75C8; `8998:0x801E09C4:-1000:4` in all runs):
- A: `9000` mode=6 (`0x8015137C:6:2`), +0xF4 = sign, sign 4374 ahead, |lat| 4144 -> mode 1 next step. VERIFIED
  third branch.
- B: same, sign 300 ahead / lat +200: rotY 1836 -> 1772 -> ... -64 per step, behind then dither, d > 850 at
  9085 -> +64/step toward the goal still in mode 6 (+0x78 = 0), dot >= 0 with lat 1061 at 9121 -> mode 1.
- D: natural entry: sign bit 0x8 cleared (`9000:0x800C75C8:0x485:4`), sign at (-3684,-1358,-700) on the walk
  path, home moved (`8998:0x80151310:6000:4`): contact at 9017 (d 262 before the step), +0xF0 = +0xF4 = sign,
  mode 6, frozen while blocked, then the behaviour of B.
  Probe scripts were run from the session scratchpad (`avoid_run*.sh`, `avoid_scan.py`); the pokes above
  reproduce them.

## 5. Port recipe (Unity)

1. Mover: return `blocked = true` and do not move when the proposed position overlaps another collider actor
   (bandits, Xena) horizontally (`< rA + rB`, 150 each) and vertically, unless already overlapping it at the
   current position. Record `lastContact = other` (sticky). Optional: push the other actor by the step,
   away from the mover. Wall slide also returns "blocked" but moves.
2. After movement, if not in a hit/attack/death clip: if `blocked && lastContact != null && lastContact !=
   target && dist(self, goal) >= 3001`: if `lastContact.mode != Avoid`, `mode = Avoid`; skip the rest of the
   step either way.
3. Avoid step: compute dot/lat on the forward/right axes with `R = other.radius + 700`; apply the three
   branches above with turn 64 (units of 4096) per AI step (30 Hz); walk (clip 15) when the blocker is within
   1700, else run (1A). Exit to mode 1 as above. No timer.

UNKNOWN: who else clears +0xF0; the meaning of sp+0x250 (raw clip 4); whether a pushed bandit's anim update
overwrites ctx+0x24 before its mover consumes the push; the one-step clip 0 / 0x14 / 0x16 / 0x17 changes seen
inside the walk family (not set by the mode-6 action).
