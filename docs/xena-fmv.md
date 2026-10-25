# Videos (FMV): boot sequence, New game, Unity export

Labels: **VERIFIED** (code read and/or measured in the native port), **INFERRED**, **UNKNOWN**.
Port measurements: `build/port/xena-console.exe` built 2026-10-22 21:11, headless, frame = one
vblank (60/s); `--verbose` logs every file the game opens (`file: MEDIA\...`).

## Movie table and player

* **VERIFIED (EXE).** Movie table `D_800C28C4`, 8-byte entries `{char *name, s16 flag, u16 count}`:

  | idx | name | flag | count | idx | name | flag | count |
  |---|---|---|---|---|---|---|---|
  | 0 | UNILOGO | 1 | 207 | 9 | LEV10E | 1 | 122 |
  | 1 | CHAKDETH | 1 | 84 | 10 | LEV12E | 1 | 703 |
  | 2 | CREDITS | 1 | 1272 | 11 | LEV18E | 1 | 641 |
  | 3 | END1 | 1 | 829 | 12 | LEV20B | 1 | 601 |
  | 4 | END2 | 1 | 812 | 13 | LEV22E | 1 | 503 |
  | 5 | INTRO | 1 | 1255 | 14 | LEV26B | 1 | 416 |
  | 6 | LEV03E | 1 | 329 | 15 | LEV26E | 0 | 357 |
  | 7 | LEV07E | 1 | 451 | 16 | LEV26M | 0 | 167 |
  | 8 | LEV08E | 1 | 444 | 17 | EA_LOGO | 1 | 72 |

  `func_800AF208(idx)` plays entry idx (`func_800AF29C`); idx 18 (0x12) means "no video" and returns.
  The player stops when it fetches frame >= count; frames 1..count-2 are visible
  (docs/game_notes.md "Video"). After a flag-1 movie it sets `D_800F0789 = 1` (purpose UNKNOWN).
* **VERIFIED (code).** Skip: each frame, if `D_800F08C4 != 0` and `func_800AA078(0) & 2`, the player
  aborts (`$s5 = 2`). `D_800F08C4` starts as 1 and is written by `func_800AF1FC(a0)`.

## Boot sequence

`func_8006C1B4` (called from `main` init, `main.s` 0x80065BE8):

```
if (!D_800ED67D) { func_800AF1FC(0); D_800ED67D = 1; }   // first time only: not skippable
func_800AF208(0x11);  // EA_LOGO
func_800AF208(0);     // UNILOGO
func_800AF1FC(1);     // skippable from here on
func_800AF208(5);     // INTRO
```

* **VERIFIED (port, no input).** Order EA_LOGO -> UNILOGO -> INTRO -> main menu. Files opened at
  f30 (EA_LOGO), f330 (UNILOGO), f1140 (INTRO); `D_800F08C4` 1 -> 0 at f60, -> 1 at f1170;
  `data\screens\menu\main.tim` at f6540, game state `D_800F2648` 0 -> 2 (menu) at f6630.
  So EA_LOGO ~5.0 s, UNILOGO ~13.5 s, INTRO ~84 s (5400 vblanks incl. loading).
* **VERIFIED (port).** On the first boot EA_LOGO and UNILOGO cannot be skipped: START or CROSS held
  20 frames at f100 (EA_LOGO) or START at f500 (UNILOGO) changes nothing (INTRO still opens at f1140).
* **VERIFIED (port).** INTRO is skipped by **START only**: each of START, CROSS, CIRCLE, TRIANGLE,
  SQUARE, SELECT, L1, R1, L2, R2 held 20 frames at f1500; only START ended it (main.tim at f1860,
  state 2 at f1950); with the others the menu appears at f6540 as without input.
* **INFERRED (code).** `main` state 0 (`jtbl_80017AFC[0]`, 0x80066044) calls `func_8006C1B4` again
  (logos are then skippable, `D_800ED67D` is 1) and plays CREDITS if `D_800ECE9C == 0`. When state 0
  is entered (e.g. after the attract demo) is UNKNOWN; the first boot stays in state 0 until the menu.

## New game

* **VERIFIED (port).** CROSS on "Новая игра" at f7400: state 2 -> 10 at f7410 (`func_8006C04C`, start
  level), `data\levscr\load01.tim` (loading screen) at f7380-7410, level files from f7440, state 12
  (in level) at f7470. **No STR is opened** between the menu and level 01 (no `MEDIA\` file after f1140,
  MDEC idle after f6210).
* **VERIFIED (EXE).** `func_8006C04C` plays the level's movie `D_800C18F2[40 * level]` before the
  loading screen. It is 18 (none) for every level except 20 (LEV20B) and 26/27 (LEV26B). So the
  "intro" a player sees before New game is INTRO.STR at boot, not a video after the menu.

## Streams (ffprobe, psxstr)

| file | LBA | bytes | frames in file | fps | length | shown by the game | audio |
|---|---|---|---|---|---|---|---|
| EA_LOGO.STR | 29499 | 1583104 | 75 | 15 | 5.00 s | 1..70 | XA 37800 Hz stereo |
| UNILOGO.STR | 107040 | 4308992 | 210 | 15 | 14.00 s | 1..205 | XA 37800 Hz stereo |
| INTRO.STR | 46752 | 25772032 | 1258 | 15 | 83.87 s | 1..1253 | XA 37800 Hz stereo |

All are 320x240. INTRO is letterboxed (black bars top and bottom inside the 320x240 frame).
ffmpeg's mdec decoder prints many "ac-tex damaged" messages for these files, but every frame
decodes (frame count = ffprobe packet count).

## Unity export

`python tools/export_fmv.py EA_LOGO UNILOGO INTRO` (from `decomp/`) writes
`unity/XenaUnity` and `unity/XenaLightingVerification` `Assets/Imported/Resources/Video/`
`<name>.webm` (Original, 320x240) and `<name>-hd.webm` (Remaster, Real-ESRGAN x4plus 1280x960),
VP8/Vorbis, all frames of the file at the stream's frame rate, audio cut to the video length.
The game stops 2-5 frames (<= 0.33 s) before the file's end; the clips keep those near-black frames.
`tools/export_game_over_fmv.py` does the same for CHAKDETH.
