# Sound effects: level 01 and core mechanics (audit 2026-10-26)

Labels: **VERIFIED** = read from the MIPS code/EXE data at the cited address (and, where stated, seen in the
native RAM snapshot `build/level-assets/01/native-reference/ram_009200.bin` or in a native-port audio
capture); **INFERRED** = role read from context, not checked dynamically; **UNKNOWN** = not resolved.
"Native port" = `build/port/xena-console.exe` (build of 2026-10-22 21:11), not PS1 hardware.
Units: 400 u = 1 m; angles in `func_800ADB9C` units (65536 = 360°); rand(a,b) inclusive (`func_800B4350`).

| Tool | Purpose |
| --- | --- |
| `tools/export_sfx.mjs` | renders every effect of a sound set to 44.1 kHz WAV + `sfx.json` (default set 1 = level 01) |
| `tools/sfx_triggers.json` | hand-curated trigger table (call sites, conditions, labels) read by the exporter |
| `tools/sfx_match.py` | finds renders inside a native `--wav` capture (EVENT − BASE isolation, cross-correlation) |

```
node tools/export_sfx.mjs --root <main decomp> --out build/sfx/export [--xa referenced|all|none] [--hold 2] [--raw]
```
Reads `xena.exe`, `disc/files/XENA.UDA`, `build/asset-audit/manifest.json`, `disc/filelist.txt` and the BIN
(`--bin`, default `../Xena Warrior Princess.bin`, for the CD-XA voices). Output of the default run: 82 VAB
effects (+ 6 note variants, + 5 natural-rate loop samples), 67 XA voice clips (+ 25 `_fullrecording` files),
185 WAV, `sfx.json`. `--xa all` exports all 231 XA clips of `XA1.XA`.

## 1. The SFX system — VERIFIED

**One entry point** `func_800847C0(id, note, vol, pan, pos)`:

| id | path |
| --- | --- |
| `& 0x2000` | music sequence `id & 0x1FFF`, `func_800865A8` → `SsSepPlay` (note = loop count) |
| `& 0x1000` | CD-XA voice clip, `func_8008751C` (section 3) |
| else | VAB program: `D_800CC424[id]` = {s16 bank slot, s16 program} (139 entries, ends at `D_800CC650`) |

Wrappers: `func_80084A34(id)` = (id, 60 — 64 for XA —, 127, 64, NULL) i.e. 2D; `func_80084AD0(id,pos)` = (id, 60,
127, 64, pos); `func_80084A70(id,note,pos)`; `func_80084AA0(id,vol,pos)`; `func_80084E80(list,n,pos,chance)`:
if rand(0,4095) < chance play list[rand(0,n−1)] (8-byte {u32 id, s16 note, s16 vol}; vol < 0 → random vol/pan).
Stop: `func_80084AFC(id,pos)` (key-off of that program's voices, optionally only those attached to pos),
`func_80084CF4(pos)` (everything at pos). Actor scripts (`func_8004180C`): 0x8007 `sound` (2D), 0x801A
`soundRand(a,b)` (2D), 0x801F `soundAt(id)` (id −1 = footstep `func_800416E8`), 0x8020 `soundAtRand(a,b)`,
0x8021 `soundFull(lo, hi|−1, note, vol, unused)` — all positional ones use actor `+0x5C` and need actor `+0xA8`.

**Banks.** `func_80086DCC(set)` loads slot 0 `core` plus the set's banks into `D_800F3508 + slot·16`; a VAB id
whose slot is not loaded is silently skipped. Set 1 (level 01) = core, npcm, village, valarian → ids
**0x00–0x42 core p0–66, 0x43–0x4A npcm p0–7, 0x4B–0x4D village p0–2, 0x4E–0x51 valarian p0–3** (82 sounds).
All VAB files are in `XENA.UDA` (`DATA\SND\*.VH/.VB`, format as in `docs/xena-ambient-audit.md`).

**Keying.** Tone mode byte := 4 (reverb) if `D_800F1599` else 0; `SsVoKeyOn((vabId<<8)|prog, note<<8, 1, 1)`
starts one voice per tone whose [min,max] holds the note (several layered tones for 0x17–0x1C); pitch
= `SsPitchFromNote(note, 0, center, shift)` (44100 Hz · pitch/4096, max 0x3FFF); then
`SsUtSetVVol(voice, L, R)` writes SPU voice volume **L·129 / R·129** (libsnd `0x80095508`) — tone, program
and VAB volumes are overwritten. ADSR comes from the tone. A per-voice record (32 B, 24 voices) at
`D_800F31C8` keeps id/note/vol/pan/scale/pos; `func_80084FAC` re-evaluates every positional voice each frame
and also runs volume/pan/pitch slides. RAM frame 9200: voice 0 = {prog 0x13, note 70, pos `0x800ECC24`},
voice 3 = {0x13, 70, pos `0x8014CDEC`, an actor position — splash actor INFERRED}, others 0x2F/0x31/0x32
note 60 (footsteps) — **VERIFIED**.

**Volume law** `func_800844BC(vol, pan, scale, master)`: v = vol; scale ≠ 127 → v = (scale·vol+64)>>7;
master < 127 → v = (master·v+64)>>7; v ≤ 127; stereo and pan ≠ 64: pan < 64 → R = v·pan>>6, else L =
v·(127−pan)>>6. 2D: scale 127. Master `D_800F1596` = SFX volume option (127 in RAM). Note that a call's vol is
the *scale* of a positional sound; values > 127 (frog 1016, cyclops 0x4F6…) saturate at 127.

**3D attenuation** `func_800845C8(pos)` — listener is the **camera** `D_800F10EC` (+0x1C/+0x20/+0x24), not Xena:

* d = √(dx²+dy²+dz²); vol = 127 if d ≤ 2500 (6.25 m), else 127 − ⌊(d−2500)·64/10000⌋, floor **10** (reached at
  d ≈ 20 781 = 52 m; sounds never fade out completely);
* rel = atan(dz, dx) − atan(cam+0x44, −cam+0x48); behind (|rel| > 90°) → vol −= vol>>2 and rel mirrored to
  the front (−0x8000 − rel);
* pan = clamp(64 + rel·64 / `D_800F158A`, 0, 127), `D_800F158A` = atan(H 400, half width 160) = **3969**
  (21.8°, RAM) → full left/right at the edge of the view; mono option (`D_800F1598` = 0): pan 64.
* final L/R = `func_800844BC(distVol, pan, callVol, master)`.

**Voices, priority, reverb.** 24 SPU voices shared with the music (seq 2 of m_vill uses up to 26 —
`docs/xena-ambient-audit.md`); no reservation found, libsnd allocates (all SFX programs/tones of the four
level-01 banks have priority 255; stealing order INFERRED). Duplicate guards: many sites call
`func_8008628C(id,pos)`/`func_8008626C(id)` ("already playing") first. Pause mutes all 24 voices
(`func_80086A24`). Reverb type 3, depth 0x27; SFX reverb `D_800F1599` is 1 only on levels 8, 15–18 (level
load `0x80062B28`) and in one map region (`0x80038AFC`) → **level 01 SFX are dry** (RAM: 0). Stereo flag 1.

## 2. Effects of level 01 and the core mechanics

Full per-site detail is in `sfx.json` (`triggers`, `randomGroup`). Frames are script frame thresholds.

| id | file / role | trigger (label) |
| --- | --- | --- |
| 0x00–0x02 | sword_swing a–c | Xena script rand(0,2) at clip 0x51 (sword 1) f7, 0x3A f11, 0x4F f13, 0x53 f15, 0x55 f10, 0x3C roll f1, 0x3E run-attack f36, 0x46 f14 … ; bandit attack clips 0x3A–0x41, 0x45, 0x46, 0x48, 0x4C, 0x4F, 0x50, 0x58, 0x59 (VERIFIED). 0x00 is also **menu confirm** (`func_80084A34(0)`) |
| 0x03–0x07 | kick_swing a–e | Xena rand(3,7): clip 0x61 kick 1 f8, 0x5A kick 3 f16, 0x59/0x5B/0x63 f8–9, 0x60 f10, 0x49 f14, 0x48 f3, 0x3F f7 (VERIFIED) |
| 0x08 | chakram_whoosh | chakram flight every rand(8,16) frames (`0x8003F034`), Xena clip 0x4A f23 and 0x5C f14; stopped at catch (VERIFIED) |
| 0x09–0x0E | Xena hit impact | rand(9,14) at Xena when she takes ≥ 2 damage (`0x80037F48`, role INFERRED) |
| 0x0F | pickup_chime | any `func_8003FBA0` pickup collected (health2, scroll, up_sword …) 2D; also menu code sequence (VERIFIED) |
| 0x13 | water_loop (looped) | level-01 load: note 70, vol 63 at (−3750, 1515, 12475); each `splash` actor (2 on level 01): note 70, vol 31 (VERIFIED, RAM); item effect vol 25 at Xena |
| 0x14 | loop | item effect `D_800EC584`: on each damaging hit by Xena (INFERRED) |
| 0x15–0x18 | kick_hit | Xena connects with a kick-set clip (0x3F, 0x46, 0x5A–0x64 …, `jtbl_800174EC`) or bandit kick: rand(0x15,0x18), note rand(60,62), vol 127, at target (VERIFIED) |
| 0x16 | — | chakram hits class 0xB4 |
| 0x19–0x1A | chakram_ricochet | chakram hits wall / range limit (VERIFIED) |
| 0x1B | block_clang | any hit on a target in states 0x80–0x83 (block): note rand(55,57) (VERIFIED); chakram hit on actors: 0x1B/0x1C |
| 0x1D | chakram_catch | `func_8003F52C` at Xena (VERIFIED) |
| 0x1E–0x21 | sword_hit | Xena sword connects / bandit sword: rand(0x1E,0x21), note rand(60,62) (VERIFIED); 0x1E note 40 = item use |
| 0x26 | options_effects_test | SFX-volume slider test (VERIFIED) |
| 0x29–0x37 | footsteps | `func_800416E8`: surface = actor+0x58 or level default (`D_800C1909[level·40]`, level 01 = **3**): {2,4,11}→0x29+, {3,7,9,12,14}→**0x2E+**, others→0x33+; rand(0,4), note 60, **vol 31**; Xena walk clip 0x15 f11/f22, run 0x1A f6/f17; bandits 0x15 f4/f24, 0x1A f2/f14 (VERIFIED; RAM shows 0x2F/0x31/0x32 playing) |
| 0x2E | landing | jump landing at frame `D_800C1468[type]`, **note 55** (0x3E on water) (VERIFIED + capture); bandit death falls 0x2E/0x30 (clips 0x8A–0x91) |
| 0x38 | level_end_row | level-end statistics rows `func_8006CCF4` (VERIFIED call) |
| 0x39 | menu_move | menu/pause/options cursor (VERIFIED + capture); note 35 variant at `0x80038FE4` (role UNKNOWN) |
| 0x3B | item_select_tick | `func_80040E34`, vol 40 (INFERRED) |
| 0x3D | powerup_loop (looped) | `func_8003DCF8`: note 71, vol 31 at Xena until power-up timer `D_800F1136` ends (VERIFIED) |
| 0x3E–0x42 | water_step | surface 5: note 58, vol 63; frog landing (note 58) (VERIFIED) |
| 0x4B–0x4C | lark_chirp | lark, 1/256 per frame, note 60 (VERIFIED) |
| XA 0x10C2 | xena_jump_yell | clips 0x2F/0x31/0x35 f0 — **silent in game** (section 3) |
| XA 0x1013/14 | xena_death | clip 0x91 f2 rand (VERIFIED) |
| XA 0x1008, 0x1011, 0x1012, 0x1018 | Xena voices | hazard surface 13, kill taunt, clip 0x4C end, water (INFERRED roles) |
| XA 0x1000, 0x1001, 0x1025/2E/2F | bandit hurt/death | clip 0x7B f10; clip 0x88 end; clip 0x88 f10 50 % list (VERIFIED) |
| XA lists | bandit barks | alert 0x102D (mode 0), sees player 0x10BA (mode 1), approach list 5 % (mode 2), attack taunts 0.5 %/step (mode 5), hurt list 9 × 50 % (hit path `0x8001F5F0`), hits-Xena list 7 × 50 % (`0x80038184`) (VERIFIED calls, roles INFERRED) |
| XA 0x1019, 0x101E, 0x10E6 | level end / exit / frog croak | Siras end, exit class, frog jump (VERIFIED calls) |

Not found / negative: the sign (`sign_pt`, update `0x800406C8`) and the message window call no sound
(VERIFIED negative for the update; message box UNKNOWN); fire/fire2/smoke have none (`docs/xena-gate-fire.md`);
no bird/frog ambient loop exists on level 01 except the larks' chirps; the ambient list `D_800C1E00` is
levels 10–12 only. Loaded but no level-01 caller found: 0x10–0x12, 0x22–0x25, 0x27, 0x28 (loop), 0x3A, 0x3C
(loop), 0x43–0x4A, 0x4D–0x51. Surface ids of level-01 faces (actor+0x58) UNKNOWN (collision groups have
flags 2 and 3); only the default 3 is confirmed (RAM footsteps).

## 3. CD-XA voices — VERIFIED code and disc data

`MEDIA\XA1.XA` (LBA 109144, 15 104 sectors): 8 groups at `D_800CC650` = 0, 5504, 6144, 6848, 7616, 8352,
8992, 13184 sectors; 31 channels interleaved per 32-sector cycle (sector 31 of each cycle is data), 4-bit
mono 18 900 Hz (coding 0x04). Clip id 0x1000 + 31·group + channel. The first data sector (+31) holds s32
`length[31]`. Playback (`func_8008751C`, callback `func_800872B8`): CdlSetfilter, ReadS from the group start,
end = start + 64 + length[ch], +32 per data sector → **2 + length/32 audio sectors** (0.213 s each) are heard.
Only one clip at a time; a new one needs priority ≥ the playing one, priority | 0x80 queues it (8 slots).

Disc scan: the recordings start ≈ 0, 0–1, 1–2, 2–3, 3–4, 4–5, 5–6, 6–7 cycles into groups 0–7. The 2-cycle
preroll covers groups 0–2 (all 93 clips complete); in groups 3–7 the window ends before the recording does:
of the 67 referenced clips 42 are complete, **22 truncated, 3 silent (0x10BA, 0x10C2, 0x10E6)**; of all 231:
104 complete, 118 truncated, 9 silent. The native port agrees for 0x10C2: a jump produced no voice energy
(only the landing; capture below). Exported WAVs are what the game plays; `*_fullrecording.wav` holds the
whole recording (not original behaviour, flagged in `sfx.json` `xa.content`). Real-hardware behaviour UNKNOWN.

## 4. Rendering (`export_sfx.mjs`)

VAB: transcription of the port's SPU (`port/runtime/spu.c`: PS-ADPCM, 12-bit pitch counter, linear
interpolation, ADSR) with the default call note 60, vol 127, pan 64, no position, master 127·129, dry;
layered tones summed; extra files for the notes the game uses (`_n70` water, `_n71` power-up, `_n55`
landing, `_n58` water steps, `_n35` menu). Loops are held `--hold` 2 s, then key-off; the near-silent tail
of an exponential release (step truncates to 0) ends the voice. Looping samples are also written at their
natural rate with a `smpl` loop (`_vagN_raw.wav`, loop frames in `sfx.json`; at note 60 — use
`variants[].spuPitch` for other notes). XA: decoded and resampled 18 900 → 44 100 like the port (linear).
Approximations: linear (not gaussian) interpolation, no reverb (correct for level 01), no voice stealing,
XA resampler of the port (the CD-ROM's zig-zag filter is not modelled).

## 5. Verification against the native port

Captures (`xena-console.exe --headless --frames N --script S --wav`), deterministic: two runs differing by
one input are identical until it; `sfx_match.py BASE EVENT build/sfx/export t0 t1 --exact`:

| event (script) | match | start | corr | gain | residual rms |
| --- | --- | --- | --- | --- | --- |
| main menu DOWN @7400 | 0x39_menu_move | frame 7403.00 | 1.000000 | 1.0000 | 0.00015 (100 % of samples within 1 LSB) |
| level 01 CROSS @8700 | 0x00_sword_swing_a | frame 8718.00 | 0.999999 | 0.9840 (vol 125: camera ≈ 7 m) | 0.0015 |
| level 01 TRIANGLE @8700 | 0x2E…_n55 (landing) | frame 8762.00 | 0.999972 | 0.9411 | 0.0080 |
| level 01 CROSS+SQUARE @8700 | XA 0x1012 | 145.777 s | 0.646 whole clip; 0.988–0.997 per 100 ms | 0.98 | — |

The XA mismatch is timing, not content: the port's XA stream jumps by 440→292→145→0 samples during the
first 0.7 s (best per-window lag), afterwards lag 1 and correlation 0.992–0.997; RMS per window equals the
render within 2 %. Level-start scripts: `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS`. Captures, logs and
scripts of this audit: session scratchpad `cap/` (menu_base/menu_down, m_base/m_cross/m_tri, l1_base2/l1_cs).

## 6. Open points

1. Per-face surface ids on level 01 (which faces give 0x29/0x33/0x3E steps) and the meaning of surfaces.
2. Roles of the bandit lines marked UNKNOWN (`0x8001C9E8`, `0x8001DED8`, `0x8001F11C`, `0x8001FCB0`) and
   Xena `0x80038FE4`; which item sets `D_800EC584`.
3. libsnd voice stealing with 26 music voices; reverb settings on the levels that enable it.
4. XA truncation on real hardware; CD-ROM resampler; `SsSetSerialVol` scale (capture gain ≈ vol/127).
