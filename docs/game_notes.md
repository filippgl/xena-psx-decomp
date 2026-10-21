# Game internals found while porting

Facts are marked with how they were established. **Open** marks what is not known yet;
don't build on it as if it were.

## Files

- Assets are in `XENA.UDA`. The game looks names up with `func_800A4B1C` (its
  CdSearchFile): `"\NAME;1"` upper-cased, hashed as `sum(lower(c) * weight[i])` with the
  weights at `0x800DA6C4`, against 0x52A entries `{hash, lba, size, ...}` at `0x800D5424`
  in the EXE. *Code read; `tools/uda_find.py` reproduces it and agrees with the game.*
- "file not found" lines in the log are the game's own `printf` for names missing from
  the index; many are optional objects. *Observed on every level.*
- `--verbose` logs every lookup (`file: ...`).

## Levels

- The level to load is the halfword at `0x800EE200` (set after New Game; -1 once loaded).
  *Watched in runs.*
- Level numbers with data in the index: 1-8, 10-12, 15-18, 20-22, 24-26 (21 levels).
  Numbers 9, 13, 14, 19, 23, 27-30 have no `LEVELS\NN\` files; poking them makes the game
  show an error code ("18", "12") or wait forever. *Index checked with uda_find.py.*
- **Open:** whether 9, 13, 14, 19, 23, 27 are reached in normal play some other way
  (cut-scenes, bosses) and how the game numbers its stages ("Этап 5" was shown for the
  save made on level 4).

## Game mode

- `0x800F2648` drives the main loop's switch (jtbl_80017AFC): 0 boot, 2 front end,
  0x0A starting a level, 0x0C level (loading and play), 3 load screen, 4 save screen.
  *Watched; 3 and 4 tested by poking.*

## Xena

- Her control update is `func_800367BC(obj)`, called through her character table at
  `0x800C4BE8`. *Probe.*
- `*(*(obj+0xA8)+0x18)`: transform; s32 x, y, z at +0/+4/+8, rotY (12-bit) at +0xE; she
  moves along rotY + 2048. *RAM dumps while moving.*
- `obj+0xD0`: state. Seen: 00 idle, 14-17 running, 2B/2C pivot, 2F jump, 1A/1E leap,
  51 61 80 40 4C-4E 5C 86 attacks/combos, 28 29 37 crouch, 3B 46 block, 44 81 82 hit or
  grabbed, 2A fall, 35 landing. *Screenshots per state;* **open:** 1F, 3E, 3F, 2A/35
  details, anything not seen in fuzzed play (ledges, swimming?).
- `obj+0x10E`: this tick's turn. Standing, the game compares it with `$s4` at
  `0x8003BF14` (run or pivot); it is applied by `func_80034FD0` at `0x8003C6D4`.
- Original controls: Up runs along the level's guide directions (list walked at
  `0x8003C340`), not the camera; Left/Right run while turning; Down turns her round. In
  attacks the pressed direction snaps her in 8 camera-relative steps. *Measured.*
- Camera yaw copy: `0x800F0F28` (camera object +0x1A); a camera behind her has yaw =
  rotY + 2048. *Measured.*

## Video

- MDEC output matches ffmpeg's decoder: UNILOGO 160/160 non-black frames at ~50 dB PSNR
  (15-bit output vs ffmpeg's 24-bit). *tools/fmv_check.py.*
- Videos stop by the movie table at `0x800C28C4` ({name, flag, count}, EXE): the player
  (`func_800AF29C`) ends when it fetches frame number >= count; it decodes one frame ahead
  and its clean-up replaces the last decoded frame before the next vblank, so frames
  1..count-2 are visible (EA_LOGO: 70 of the 75 in the file, UNILOGO: 205 of 210). The
  port stops at exactly that frame. *Code read; end condition logged with a probe.*
