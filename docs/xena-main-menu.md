# Main menu: construction and animation (SLUS-00977)

What the main menu draws, how it animates and how it reacts to input, written so that it can be
rebuilt in Unity. Exporter: `tools/export_main_menu.mjs`.

Labels: **VERIFIED** means read from the MIPS code and/or confirmed with a native-port RAM dump or
screenshot. **INFERRED** means a reasonable reading that was not tested. **UNKNOWN** means not determined.
Coordinates are 320x240 PS1 framebuffer pixels, measured from the top-left corner.

## Functions and data

| What | Where |
|---|---|
| Menu init | `func_8006AA04` (called from `main` 0x80065CAC/0x8006642C, `func_80067FB8`, `func_8006E2BC`, `func_8006E508`) |
| Menu tick | `func_8006AC88`, called by `main` game state 2 (`D_800F2648 == 2`) while the idle timer `D_800F10E0 != 0` |
| Menu free | `func_8006AC00` |
| Background | `func_8006A8E4` loads `data\screens\menu\main.tim` into RAM; `func_8006A924` sends it to the draw buffer with `LoadImage` on every tick |
| STM loader | `func_800A7528(name, &descArray, mode)` returns the entry count. It uploads each TIM to its own VRAM rectangle and writes a 24-byte descriptor (+0 x, +2 y, +4 width in pixels, +6 h, +8/+A CLUT x/y, +C depth 0/1/2) |
| TIM loader | `func_800A7224(name, 0, mode, desc)` uses the same descriptor format |
| Sprite draw | `func_800B8D88(desc, cx, cy, r, g, b, abr, semi)` calls `func_800A5634` (DR_MODE + SPRT code 0x64) and then `DrawPrim` + `DrawSync` immediately |
| RNG | `func_800B42D8` (`rand`), `func_800B4350(lo, hi)` (range) |
| Context | `D_800F1238` points to 0x88 bytes on the heap (0x80114F4C in the native run) |

Context layout (**VERIFIED**: code and RAM dump at frame 7400):
`+0x00/+0x18/+0x30/+0x48` highlight descriptors · `+0x60` hair descriptor array, `+0x64` count (134) ·
`+0x65 + 3*i` hair channel i `{u8 seq, u8 frame}` · `+0x70` lightning descriptors, `+0x74` count (4) ·
`+0x75` a byte set to 20 at the first bolt (the menu never reads it) · `+0x76 + 4*i` bolt i `{s16 timer, s8 state, u8 index}`.

## Timing

* **VERIFIED.** A menu tick is one pass of the main loop: `VSync(0)` at 0x80066628, input read, then the
  state-2 handler. The loop has no fixed rate. A tick takes one vblank or more.
* **VERIFIED (native port only).** Ticks ran at about 34.7 Hz: 74 ticks in 128 vblanks (frames
  7392-7520), in a pattern of roughly 2,2,2,1 vblanks per tick. The idle timer also fell by 17-18 per
  30 vblanks.
* **UNKNOWN.** The tick rate on real PS1 hardware.
* **VERIFIED (port).** The state a tick writes to RAM at port frame F first appears in the screenshot
  at F+2.

## Draw order inside one tick (VERIFIED)

Each primitive goes to the GPU straight away, so the painter's order equals the call order:

1. Background `main.tim`: 320x240, 16-bit, copied with `LoadImage`, fully opaque. The four menu labels
   are part of this image.
2. Each lightning bolt 0..3 that is active this tick.
3. Hair channels 0, 1, 2: one tile each.
4. Highlight sprite for the current cursor.
5. The prompt text.

All sprites except the text are plain SPRTs with a raw texture. Colour is 0x7F,0x7F,0x7F, so
`SetShadeTex(0)` is skipped, and the sprites are not semi-transparent (abr 0, semi 0). A texel of
0x0000 is transparent and every other texel is opaque.

The sprite's top-left corner is `centre - trunc(size/2)` (`func_800A5634`).

The STM and highlight CLUTs are loaded in mode 0. `func_800A71B4` sets STP on every non-zero entry. This
has no visible effect because these sprites are not semi-transparent.

## Hair (hair.stm, LBA 1014): VERIFIED

There are 134 tiles, all 4-bit:

* 0..90 `H1all001..091`, 32x32.
* 91..111 `H2_01..21`, 16x16.
* 112..133 `H4_01..22`, 16x16.

Sequence table: starts `D_800C2880 = {0, 12, 72, 91, 112, 134}`, centres `D_800C2868` (x) and `D_800C2874` (y).

| seq | tiles | centre | top-left | step | next sequence |
|---|---|---|---|---|---|
| 0 | 0..11 | (260,49) | (244,33) | 1 | 1 |
| 1 | 12..71 | (260,49) | (244,33) | **2** (tiles 12,14,..,70) | `rand() & 4 ? 2 : 1` |
| 2 | 72..90 | (260,49) | (244,33) | 1 | 0 |
| 3 | 91..111 | (246,34) | (238,26) | 1 | 3 (loop) |
| 4 | 112..133 | (232,33) | (224,25) | 1 | 4 (loop) |

There are three independent channels. Every tick draws three tiles, one per channel:

* Channel 0 starts in seq 2. It is the big 32x32 hair: seq 2 → 0 → 1, and seq 1 repeats.
* Channel 1 is always seq 3.
* Channel 2 is always seq 4.

At init each channel's frame is the first tile of its sequence. Each tick does:

```
frame += (seq == 1) ? 2 : 1
if frame >= start[seq+1]: seq = next(seq); frame = start[seq]
draw hair[frame] centred at centre[seq]
```

The animation does not repeat on a fixed period. At the end of each seq-1 loop (30 ticks), one
`rand()` call decides whether to play the 19-tick seq 2 and then the 12-tick seq 0. Seq 3 (21 ticks) and
seq 4 (22 ticks) loop forever. Their periods differ, so they drift against each other.

RAM confirms the transitions 2→0 (frame 6669), 0→1 (6690), 1→1 (7432, rand 0xBA58 has bit 2 clear),
1→2 (7009) and 2→0 (7042).

## Lightning (ltng.stm, LBA 1045): VERIFIED

There are four 8-bit tiles: `LTNG1` 66x48, `LTNG2` 66x48, `LTNG3` 66x36 and `LTNG4` 66x48. Bolt i
always uses tile i. Centres come from `D_800ED570` (x) and `D_800ED578` (y):

| bolt | centre | top-left |
|---|---|---|
| 0 | (33,41) | (0,17) |
| 1 | (152,33) | (119,9) |
| 2 | (145,102) | (112,84) |
| 3 | (287,94) | (254,70) |

At init: `timer = rand(30,200)`, `state = 2`. Each tick, for bolts 0..3 in order:

```
timer = timer ? timer - 1 : rand(60,600)
if timer < 7:                         # the last 7 ticks of a countdown (6..0)
    if state >= 2: draw bolt
    state -= 1 (signed byte)
    if state <= 0: state = rand(0,3)
```

The result is a flicker of about 7 ticks. Only ticks with `state >= 2` draw the bolt, so a flash lasts
about 2-4 ticks. The bolt is an ordinary opaque sprite. There is no additive blend, and the sky and
background do not change.

The native RAM and screenshots show bolt 3 drawn on 3 ticks (frames 7588-7595) and bolt 2 on 4 ticks
(frames 6706-6715 and 7683-7691).

## Random number generator: VERIFIED

`rand()` updates the state `D_800F1748` 16 times:

```
bit = (s >>> 30) & 1
if (s & 4) bit ^= 1
s = (s << 1 | bit) >>> 0
```

It returns `int16(s & 0xFFFF)`. If the state is 0, it is first seeded from `VSync(-1)`.

`rand(lo, hi) = lo + (((hi - lo + 1) * (rand() & 0xFFFF)) >>> 16)`. Both ends are inclusive.

Order of calls within a tick:

1. Lightning bolt timer reload, then the bolt's state reroll, for each bolt.
2. The hair seq-1 exit test.

Simulating with these rules from one RAM snapshot reproduces every following tick exactly (see
Verification).

## Menu items, cursor, selection

* **VERIFIED: highlight sprites.** The highlight TIMs are `data\screens\menu\%s.tim` with names
  `D_800ED538 = ng_sel, lg_sel, ta_sel, op_sel` (8-bit). Their centres come from `D_800ED568` and
  `D_800ED56C`:

  | item | file | size | centre | top-left |
  |---|---|---|---|---|
  | New game | `ng_sel` | 98x34 | (76,133) | (27,116) |
  | Load game | `lg_sel` | 102x34 | (80,156) | (29,139) |
  | Training arena | `ta_sel` | 128x34 | (95,179) | (31,162) |
  | Options | `op_sel` | 88x34 | (69,203) | (25,186) |

  The highlight replaces the baked-in label with a brighter version.

* **VERIFIED: no blink.** The highlight of `D_800ED534` is drawn on every tick and nothing animates it.
  The cursor keeps its value between menu visits (it is not reset in init).
* **VERIFIED: up and down.** Input comes from the newly pressed mask `D_800F16D0`:
  * Bit 0x4 moves the cursor up: `cursor--` if it is above 0. There is no wrap.
  * Bit 0x8 moves it down: `cursor++` if it is below 3.

  Both play sound `func_80084A34(0x39)`. Port script `DOWN` moved the cursor 0→1→2→3.
* **INFERRED: button names.** `D_800F16D0` is the game's remapped pad word (`func_80068EDC`), so which
  physical button sets each bit was not checked.
* **VERIFIED: confirm.** Bit 0x800 is accepted only while the delay `D_800F10F0` is 0. Menu init sets
  the delay to 5 and `main` decrements it once per tick. Confirming plays sound `func_80084A34(0)`,
  calls `func_8006AC00` (free menu), then runs the action for the cursor:

  | cursor | item | action |
  |---|---|---|
  | 0 | New game | `func_8006A9B0(1)` |
  | 1 | Load game | `func_80067640`, the memory-card load screen (**INFERRED**). With cheat bit `D_800F1258 & 1`: `func_8006BB70` and `D_800F123C = 0x1A` (**UNKNOWN** meaning; possibly a level select) |
  | 2 | Training arena | `func_8006A9B0(0)` |
  | 3 | Options | `func_8006E1EC` |

  `func_8006A9B0(n)` sets `D_800EE200 = n` and calls `func_8007674C(1)` and `func_8006C04C(1)`.

* **VERIFIED: cheat codes.** The last 8 newly-pressed masks are kept in a ring buffer
  (`D_800F11D0..D_800F11F0`). Each tick the ring is compared with 4 sequences of 8 words at
  `D_800C27E8`; the values are in the JSON. Matching sequence 0 clears `D_800F1258`. Matching
  sequence i>0 sets bit (i-1). Either plays sound `func_80084A34(0xF)`. What each bit does elsewhere is
  **UNKNOWN**.

## Prompt "Нажми ⓧ для выбора"

* **VERIFIED: string and placement.** The text is `D_800CCEA8[0]` = bytes
  `48 61 77 6D 6E 20 04 64 2B 72 20 62 66 26 24 70 61`. In this Russian build the Latin code points map
  to Cyrillic glyphs.
  * It is drawn every tick: `func_800BA47C` (style reset), `func_800BA4E4(1)` (right-align),
    `func_800BA598(300, 202)`, then `func_800B9300(text)`.
  * Glyph index comes from `func_800BA3E4`: lower case maps to c-0x21; `A`..`^` and space..`@` map to
    c-0x20; bytes 1/2/3/4 map to glyphs 0x5D/0x5C/0x5A/0x5B. Byte 0x04 is the button glyph, which shows
    as a circled X on screen.
* **VERIFIED: font.** The font is `data\screens\xenafont.tim` (LBA 476): 4-bit, 256x128, in VRAM at
  (960,256).
  * Glyph g sits at `u = (g%16)*16`, `v = 32 + (g/16)*16` and is 16x16.
  * Width = the rightmost non-transparent column of the cell (`func_800B9168`). Space is set to 2,
    `g` gets -1, and the digits are 7.
  * Advance = width + 3; digits advance 10.
  * Y offset comes from `func_800B9A78`: upper case 0; lower case -1 (j +3; g, p, q, y +2); digits and
    punctuation (0x10..0x1F) +1.
  * Right alignment subtracts the full extent, including the trailing 3 px. The result is the box
    x 131..299, y 201..219.
* **VERIFIED: blend.** The template is copied from `D_801002C8`:
  * colour 0x7F,0x7F,0x7F;
  * `func_800BA514(1,1)` gives abr 1 (B+F, additive) and semi-transparent;
  * each glyph is a SPRT16 with code 0x7C, so it is modulated: `c5 = t5*127 >> 7`.

  The CLUT in RAM (`D_800FF400`) has STP set on all 15 non-zero entries (`0x8842..0xFFFF`). The file
  CLUT has STP only on entries 1-3. Therefore every glyph texel is added to the background, clamped to
  31 per channel. The code that sets these STP bits is **UNKNOWN**: the TIM loader's mode 2 does not
  touch the CLUT.
* **VERIFIED: no effects.** No shadow, outline or glow is active, because style flags `D_801002E8 = 0`.

## Idle and attract mode

* **VERIFIED: idle timeout.** `D_800F10E0` is set to 900 at init and reset to 900 on any newly pressed
  bit. `main` decrements it once per tick (0x80065FE4).
* **VERIFIED: what happens at 0.** The state-2 handler calls `func_8006AC00` and `func_8006E314`, so the
  menu is not restarted. `func_8006E314`:
  * shows `data\levscr\load%02d.tim` for demo level `D_800ED748` (initially 2), the level intro/loading
    screen;
  * sets game state 8;
  * advances the demo level 2→11→15→2 (and 6→11).

  The native run showed it at frame ~8200 with no input: state 2 → 8 → 12, `D_800ED748` 2 → 11. The
  frame-8500 screenshot is the level-02 intro text screen.
* **INFERRED.** State 8/12 plays a level demo. How the game returns to the menu was not traced.

## Verification

Tested on the native port `build/port/xena-console.exe` (2026-10-22 21:11:35, sha1 c71c6bb06864),
headless, `--render-scale 1 --opt perspective=0 --opt texture_filter=0`. Every-frame screenshots and RAM
dumps were taken from the scratchpad runs `run2`..`run5`.

`node tools/export_main_menu.mjs --root DECOMP --out DIR --verify-shots SHOTS --verify-ram RAM` checks
two things:

* **Simulation.** It starts from the first RAM state and applies `tick()` whenever the RAM state changes.
  Then it compares the hair, lightning, cursor and RNG seed with the dump.
* **Composite.** It builds each tick's frame only from the exported PNGs and the RAM state. Then it
  compares the frame with the screenshot in 15-bit colour.

| window (frames) | ticks | simulated = RAM | composites bit-exact at +2 frames | covers |
|---|---|---|---|---|
| 7390-7520, idle | 74 | 74/74 | 74/74 (76800/76800 px each) | hair seq 1/3/4 and the seq-1 rand test |
| 7560-7700, idle | 80 | 80/80 | 80/80 | lightning bolts 3 and 2 |
| 6560-6760 + 6990-7070, `DOWN` x2 | 99 | 97/99 (the 2 misses are the cursor-input ticks, which `tick()` does not simulate) | 99/99 | menu entry, seq 2→0→1, 1→2, bolt 2, cursor 0/1/2 |
| 7040-7120, `DOWN` x3 | 45 | 43/45 (the 2 input ticks) | 45/45 | cursor 3 |

At screenshot offsets +0 and +1 no frame, or only some frames, are fully exact. The worst +0 PSNR is
22.6 dB.

Before the CLUT-STP finding, the prompt rendered as opaque text and differed by 500-700 px per frame.

## Unity notes

* Run the menu logic on a fixed tick. The original tick rate is not fixed: the port measured about
  34.7 Hz, and 30 Hz or 60 Hz are also possible. Choose a rate and document it.
* Draw each tick's sprites with no interpolation. These are frame flips, not movement.
* Use point filtering, and place each sprite at its top-left corner at integer pixels.
* For the prompt, use additive blending, or add the pre-rendered `font/prompt.png` (glyph texels × 127/128).
