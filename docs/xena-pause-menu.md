# In-game pause screen (SLUS-00977)

What the pause screen (START during gameplay) draws, how it reacts to input and what each item does,
written so that it can be rebuilt in Unity. Exporter: `tools/export_pause_menu.mjs`. The font, glyph
metrics and text blend are the same as in `docs/xena-main-menu.md`.

Labels: **VERIFIED** means read from the MIPS code and/or confirmed with a native-port RAM dump or
screenshot. **INFERRED** means a reasonable reading that was not tested. **UNKNOWN** means not determined.
Coordinates are 320x240 PS1 framebuffer pixels from the top-left corner. "Clock" is `D_800F16C0`, +1 per
vblank.

## Opening and closing (VERIFIED)

* **Toggle.** `main` 0x8006628C..0x80066304 acts when the newly pressed mask `D_800F16D0 == 0x2`
  (START, and only START):
  * if not paused, it calls `func_800672F8`, which sets the request flag `D_800F1134`. The request is
    accepted only in game state 12, with `D_800F0E02 == 0` and camera mode ≠ 6;
  * if paused, it calls `func_800673E4` (resume).
* **Activation.** On the next pass `main` 0x80065EF0 calls `func_80067360`, which:
  * sets `D_800F0E00 = 1` (paused);
  * calls `func_80086A24`: the 24 SFX voices get volume 0, and `func_80086888`/`func_80087868` run
    (music pause, **INFERRED**);
  * clears the cursor `D_800F10D9`, the restart dialog `D_800F10DA`, the exit dialog `D_800F10DB` and the
    options flag `D_800ECEA4`;
  * starts the fade `func_800A7F88(cam, 2, 2, 22)` (11 on level 8).
* **While paused.** `main` 0x80066374 calls the HUD update `func_80075C2C`. It then calls this screen
  `func_80066CDC`, or the message box `func_8006E508` if one is open.
  * The game step `D_800ECEA0` does not advance (frozen at 965 in every capture).
  * The scene is still rendered and is unchanged.
* **Resume.** `func_800673E4`:
  * sets `D_800F0E00 = 0` and `D_800F125C = 0`;
  * calls `func_80086AAC`, which restores the volumes of voices still playing and resumes the music;
  * fades back with `func_800A7F88(cam, 2, 1, …)`.
* **Capture.** START at 9100: `D_800F0E00` = 1 at 9104, and the menu first appears in the screenshot
  at 9110.

## Background (VERIFIED code; port measurement)

The game draws no blur and no copy of the frame. Each frame:

1. The frozen scene is rendered.
2. `main` 0x80066B64 calls `func_800A7F88(cam, 0, 3, 0)` while `D_800F078E` is set. This draws a
   **320x240 subtractive quad** (`func_800B73F0`, semi, abr 2 = B − F) in grey 22·t/2: 0 and 11 on the
   first two passes, then 22 (0x16) until the resume fade.

Measured on the native software renderer: 5-bit channels drop by 0..2 in a dither-like pattern (by
about 2 on average, from the 3rd frame after the menu appears). On the hardware renderer at 2x they
drop by about 3 (about 22 in 8-bit). The softer look in the 2x captures comes from the port's
upscaling, not from the game.

## Layout (VERIFIED: code; rebuilt pixel-exact, see Verification)

Draw order:

1. scene;
2. fade quad;
3. icons (`DrawPrim` at once);
4. lines (`DrawPrim` at once);
5. text slots, drawn later by `func_800BA184` in their creation order: counts, title, then the items or
   the dialog lines.

The font is `func_800BA514(1,1)`: abr 1 (additive), every visible texel has STP. Style flags come from
`func_800BA4D4`; the base style is 8.

| element | position | colour | notes |
|---|---|---|---|
| title "ПАУЗА" (`D_800F0360`) | anchor (160,64), right-aligned | 255,255,255 | texel × 255/128, clamped |
| underline | LINE_F2 (105,79)–(157,79) white; shadow (107,81)–(159,81) colour (1,1,1) | | opaque |
| separator | LINE_F2 (180,49)–(180,179) white; shadow (182,51)–(182,181) | | opaque |
| items `D_800F0364[i]` | anchor (160, 90+20·i), right-aligned | 125,125,125 | selected item uses style 0xB |
| right column icons | shield centre (224,66); sword centre (224,110); scroll centre (231,155) | raw (0x7F) | opaque SPRT, top-left = centre − size/2 |
| counts "%dx" (`D_800ECEC8`) | right-aligned at x 216; y 59 / 103 / 148 | 110,110,110 | values: armour `D_800F2785`, sword `D_800F2786`, scrolls `D_800F277A` |
| inventory counts | right-aligned at x 292, y 27+20·i (7 HUD items `D_800F277E..`) | 110 | only drawn when non-zero (none in the capture) |

Items, in order (bytes in the JSON; the Russian build remaps Latin code points to Cyrillic glyphs):

| cursor | text | bytes |
|---|---|---|
| 0 | Продолжить | `4A 70 24 64 24 2B 77 6E 74 73 20 0A` |
| 1 | Повтор уровня | `4A 24 62 74 24 70 20 79 70 24 62 68 72 0A` |
| 2 | Опции | `30 6A 76 6E 6E 20 20 0A` |
| 3 | Вых | `42 66 78 20 0A` |

"Вых" is the complete string on the disc: the translation truncates "Выход". The trailing spaces count
in the right alignment (space advance 5). The first glyph of "Опции" is the digit `0`.

Icons (exported):

* `DATA\SCREENS\SHLD_HUD.TIM`, 16x24, top-left (216,54) (`D_800F1440`, loaded by `func_8007515C`);
* `DATA\LEVELS\UP_SWORD.STM` tile 0, 10x32, top-left (219,94);
* `DATA\LEVELS\SCROLL.STM` tile 0, 32x32, top-left (215,139). The sword and scroll pointers
  `D_800F2624` and `D_800F262C` are shared STMs #8 and #9 of `D_800C1D1C`.

### Glyph rendering (VERIFIED, `func_800B9CA0`, `func_800BA184`)

* Glyph g is drawn at its pen position. The pen advances by width + 3, and by 10 for digits.
* **Digits are drawn 9 px left of the pen** (`'2'` and `'4'`: 8 px; 0x800BA294..0x800BA2D4). The
  main-menu prompt has no digits, so this rule is new here.
* Per glyph, in this order:
  1. style bit 8: the shadow, the glyph at (+1,+1) and (+2,+2) with colour (1,1,1) and abr 0, which
     gives B/2;
  2. the glyph itself, abr 1: B + min(31, t·c>>7);
  3. style bits 1/2/4 (R/G/B), the glow: four copies at (+1,0) (−1,0) (0,−1) (0,+1), abr 3 (B + F/4).
     A selected channel = `clock & 0x20 ? (clock & 31)·7 : (32 − (clock & 31))·255 >> 5`; the other
     channels are 15.
* The selected item uses style 0xB, which is a pulsing yellow outline with a period of 64 vblanks. The
  others use style 8.

## Input (VERIFIED code and port)

The mask is the game's remapped pad word. With the default controls, the port's script buttons give
START 0x2, UP 0x4, DOWN 0x8, LEFT 0x10, RIGHT 0x20, TRIANGLE 0x400 and CROSS 0x800. CIRCLE does nothing
here (verified in a run).

| input | list | dialog |
|---|---|---|
| UP | cursor−1 if > 0, no wrap, sound 0x39 | from "Не" to "Да", sound 0x39 |
| DOWN | cursor+1 if < 3, sound 0x39 | from "Да" to "Не", sound 0x39 |
| CROSS | action below, no sound | "Да" → action; "Не" → close the dialog |
| TRIANGLE | resume | close the dialog |
| START | resume (main-loop toggle) | resume |

* The cursor resets to 0 each time the pause opens.
* A dialog opens with the cursor on "Не" (value 3).
* The only sound is `func_80084A34(0x39)` on cursor moves. The SFX voices are muted while paused.

## Actions (VERIFIED code; captures)

| item | action |
|---|---|
| Продолжить | `func_800673E4`: resume |
| Повтор уровня | dialog `D_800F0398` = {"Повтор уровня", "Ты уверен?", "Да", "Не"}. "Да" → `func_800673E4`, then `func_8006C264`: `D_800EE200` = current level, `func_8006C04C(1)` reloads the level from its start; then `func_800659EC` |
| Опции | `D_800ECEA4 = 1`, `func_8006E1EC` (options init). Then `func_8006DE28` runs every frame (centred text, colour 0x7F) until it returns 1 |
| Вых | dialog `D_800F03B8` = {"Вых", "Ты уверен?", "Да", "Не"}. "Да" → `func_8006C2A4`: game state 19. The capture shows the GAME OVER sequence (rolling chakram), then the main menu (state 2 at frame 9900) |

The dialog lines are drawn by `func_80066BD4` at the item positions with the same styles. Lines 0 and
1 are static text.

## Options screen (shared with the main menu; VERIFIED code, captures)

Title "ОПЦИИ". The prompt reads "Нажми ⓧ для настроики / или жми △ чтобы не менять". Items, top to
bottom:

| # | item | values and input |
|---|---|---|
| 0 | Музыка | volume slider (`func_800870A8` / `func_800870B4`) |
| 1 | Эффекты | volume slider (`func_800871AC` / `func_800871B8`). Plays a test sound every 32 vblanks while selected |
| 2 | Аудио: Моно / Стерео | held LEFT → mono, held RIGHT → stereo (`func_8008701C`); test sounds |
| 3 | Вибрация: Выкл / Вк | LEFT/RIGHT toggles `D_800F2771`, sound 0x39, rumble test `func_800AA354` |
| 4 | Конфигурация: A..D | LEFT/RIGHT, 0..3, sound 0x39. While this item is selected the screen shows the button layout of the configuration, e.g. A = "□ – Удар, △ – Прыж, ⓧ – Руби, ○ – Блок, Л1 – Бег / Идти, Р1 – Смот" |
| 5 | Движения: Ходьба / Бег | LEFT/RIGHT toggles `D_800F278C`, sound 0x39 |

* **Slider** (`func_8006D7F0`):
  * value v from 0 to 127; held LEFT: v −= 2, below 28 → 0; held RIGHT: v += 2, minimum 28;
  * label "Вык" when v < 28, otherwise "%d%%" of v − 27 (1..100%; the `%` glyph looks like "п");
  * parts: `BLOOD.TIM` centred at (160,y); `SWORD.TIM` cropped to width w − 127 + v (w − 100 when off)
    with its left edge fixed; `HILT.TIM` centred at (95,y);
  * label colour (175,50,200) when selected, (175,200,50) otherwise.
* **Exit.** CROSS keeps the changes and returns to the pause list (VERIFIED capture: CROSS at 9660 →
  `D_800ECEA4` = 0; the kept value was not re-read). TRIANGLE first restores the backups taken by `func_8006E1EC` (`func_8006DCA8`), so it cancels
  (code only, not run).
* **Selected item.** It is violet in the capture. The highlight of the non-slider items comes from the
  generic menu helpers `func_80068BA4`/`func_80068BBC`, which were not decoded (**UNKNOWN** exact
  colours).
* **INFERRED.** The register flow for which slider is music, read together with the screen order.
* **UNKNOWN.** The real-hardware audio behaviour.

## Verification

Executable: `build/port/xena-console.exe`, 2026-10-22 21:11:35, sha1 C71C6BB06864.

Run settings:

* headless, `--render-scale 1 --opt renderer=software --opt perspective=0 --opt texture_filter=0`;
* script `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS,9100:START,9200:DOWN,9260:CROSS`;
* screenshots every frame and RAM dumps per frame.

Saved frames are in `build/pause-menu/capture/`.

**Method.** `node tools/export_pause_menu.mjs --root . --out build/pause-menu/export --verify-shots
build/pause-menu/capture --verify-ram build/pause-menu/capture --pair 9260,9268` rebuilds the list
frame from a dialog frame. The background, fade, icons, lines, title and counts are the same in both
frames. The tool adds the modelled item text (shadow, additive glyphs, glow at the RAM clock) and
compares the result pixel by pixel in 15-bit colour.

| pair (list, dialog) | cursor | clock (screenshot − 2 frames) | modelled pixels exact | pixels outside both texts identical |
|---|---|---|---|---|
| 9260, 9268 | 1 (glow) | 9240 | 1533/1533 | 68950/68950 |
| 9206, 9270 | 1 | 9186 | 1533/1533 | 68950/68950 |
| 9138, 9269 | 0 | 9118 | 1533/1533 | 68950/68950 |
| 9130, 9270 | 0 | 9110 | 1533/1533 | 68950/68950 |
| 9120, 9266 | 0 | 9100 | 1533/1533 | 68950/68950 |

**Results.**

* The pairs cover both halves of the glow ramp (clock & 0x20 = 0 and = 0x20).
* At screenshot offset 0 two pairs match only 1459..1489 pixels, which confirms the 2-frame lag known
  from the main menu.
* Before the digit offset was added, "Опции" failed on 196 pixels.
* The dialog text was checked in the reverse direction: 440/440, 167/167, 23/23 and 16/16 pixels.

Other runs, with overview sheets in `build/pause-menu/overview/`:

* 2x hardware sheet `pause_hw2x_9090_9400.png`;
* both dialogs and both cancel buttons `dialogs_9100_9640.png`;
* options navigation, config A/B/C and the CROSS/TRIANGLE exits `options_9200_9820.png`;
* "Вых" → GAME OVER → main menu `quit_9300_9900.png`;
* before/after darkening `before_after_9100_9120.png`.

## Export

`build/pause-menu/export/` contains:

* `labels/<name>.png`: the additive layer, texel × colour >> 7, to add to the screen.
* `labels/<name>_shadow.png`: the mask whose pixels are halved.
* `labels/<name>_glow.png`: the 1-px outline mask. Tint it with the pulse colour and add a quarter of it.
* The labels are the title, the 4 items, the restart dialog lines, "Вых" and "0x". Each JSON entry
  gives `originOffset` and `extent` for alignment.
* `icons/shield.png`, `sword.png`, `scroll.png`; `options/hilt.png`, `sword.png`, `blood.png`.
* `pause_menu.json`: layout, colours, styles, input, actions, sounds, strings (bytes + Russian) and the
  options model.
* `verify/pair_9260_9268.png`: screenshot | model | evaluated pixels (green exact, red wrong).

## Unity notes

* Pause the simulation clock and keep rendering the frozen scene.
* Darken the scene with a subtractive full-screen grey of 22/255. Fade it in over 2 frames and out over
  2 frames on resume.
* Mute the SFX and pause the music.
* Place the labels right-aligned at x = 160 (items) or 216 (counts) in a 320x240 canvas scaled with
  point filtering.
* Draw each label as shadow (multiply by 0.5 at +1/+2 px), then the additive glyphs, then the glow on
  the selected line. The glow pulse is R = G = `clock & 32 ? (clock & 31)·7 : (32 − (clock & 31))·255/32`,
  B = 15, on a 60 Hz clock.
* Use the cursor rules in Input: no wrap, the cursor resets on open, dialogs default to "Не".
  TRIANGLE/Escape backs out; START toggles.
