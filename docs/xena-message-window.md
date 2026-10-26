# Message window (signs, scrolls, first pickups) and the level-complete screen (SLUS-00977)

What the game draws when Xena reads a sign or picks up a scroll, and what follows the Siras cutscene
at the end of level 01, written so that both can be rebuilt in Unity. Exporter:
`tools/export_message_window.mjs`. The font, glyph metrics, shadow/glow styles and blend are the same
as in `docs/xena-main-menu.md` and `docs/xena-pause-menu.md`.

Labels: **VERIFIED** = read from the MIPS code and confirmed with native-port RAM dumps and/or
screenshots; **INFERRED** = a reading of the code that was not tested; **UNKNOWN** = not determined.
Coordinates are 320x240 PS1 framebuffer pixels from the top-left corner. A "pass" is one main-loop
iteration (`main`); "clock" is the vblank counter `D_800F16C0`.

## 1. Message window

### Opening (VERIFIED code + RAM)

Three openers set the message id `D_800ED74A`, the flag `D_800F125C = 1`, the delay `D_800F10F0 = 15`
and call `func_800672F8`, the same pause request START uses:

| opener | caller | message id |
|---|---|---|
| `func_8006E4CC(p0)` | sign / sign_pt on contact (`func_800406C8`) | `0x46 + p0` |
| `func_8006E41C(n)` | scroll pickup (`func_8003FBA0`), n = scroll `+0x10` | `level*2 + n - 1` |
| `func_8006E468(bit)` | first pickup of an item kind, once (mask `D_800F1190`) | `0x36 + log2(bit)` |

* The request is ignored unless game state is 12, the game is not paused and `D_800F0E02 == 0`.
* On the next pass `func_80067360` pauses the game exactly like START: `D_800F0E00 = 1`, the 24 SFX
  voices muted, music paused (INFERRED), fade `func_800A7F88(cam, 2, 2, 22)`.
* **The game is paused.** The game step `D_800ECEA0` stops (frozen in every capture). The frozen scene
  and the HUD (`func_80075C2C`) are still drawn every frame. `main` 0x80066384 calls `func_8006E508`
  instead of the pause screen while `D_800F125C` is set.
* Capture: RAM shows `D_800F125C = 1` at frame 8636 (scroll), `D_800F0E00 = 1` at 8638. The text first
  appears in the screenshot at 8640.

### What is drawn (VERIFIED, pixel-exact)

**There is no box.** No frame, panel, background polygon, portrait or icon is drawn. The window is:

1. the frozen scene;
2. a full-screen subtractive grey quad, (0,0)-(320,239), abr 2 (B − F). Grey is 0 and 11 on the first
   two passes, then 22. This is the pause fade (`func_800A7F88` mode 2, details in
   `docs/xena-pause-menu.md`);
3. the HUD, which is not darkened;
4. the text.

Text rules (`func_8006E508`, font stream `func_800B95BC`):

* **Lines.** The lines are `D_800CCCD0[id]`, an array of up to 8 string pointers ending at NULL. A NULL
  table entry falls back to `D_800CCA08`. n is the number of lines. An empty row is the
  one-space string `0x800EE5D4`.
* **Position.** The pen starts at **(160, 114 − 8·n)**. Each line is printed with `"%s\n"`. A newline
  advances y by **17** (1 + the 16-px glyph cell), so line i has its cell top at `114 − 8n + 17i`. The
  block is therefore vertically centred on about y 122.
* **Alignment.** Each line is centred: it is shifted left by `trunc(extent/2)`. The extent is the pen
  advance (glyph width + 3, digits 10) and includes trailing spaces. Many strings have trailing
  spaces, and they shift the line left.
* **Colour and style.** Colour **(50,150,150)** (cyan), style 8: a drop shadow at (+1,+1) and (+2,+2)
  that halves the background, then the additive glyph (`min(31, t5·c >> 7)`). There is no glow.
* **Prompt.** When the delay reaches 0, the prompt `D_800CCEA4` = "Нажми ⓧдля продолж" is drawn,
  centred at (160, **194**), colour **(64,200,64)** (green), style 8. The byte 0x04 is the circled-X
  pad glyph.

Level-01 messages (all have no box; top = cell top of line 0):

| id | where | n | top y | text |
|---|---|---|---|---|
| 0x46 | sign #0 (-5283,-1900,-9432) | 1 | 106 | К Обалусу |
| 0x47 | sign #1 (-9998,333,-6501) | 1 | 106 | Впереди Обалус |
| 0x48 | sign #2 (10888,3712,7945) | 4 | 82 | Привет в Обалусе / – / Население : / Висят как мыши |
| 0x02 | scroll #5 (near start) | 4 | 82 | Быстро следуи / к Обалусу ... / И остерегаися / пиратов по пути. |
| 0x03 | scroll #6 | 3 | 90 | Совет: Для броска Шакрама / нажми и удерживаи кнопку Р1 / потом жми ⓧ. |
| 0x36 | first health2 potion | 4 | 82 | Порция жизни! / – / Больше / Здоровье! |
| 0x45 | first up_sword | 5 | 74 | Апгреид Меча ! / – / Увеличение количест- / Ксены / ва наносит ушерб врагу |

The spellings (for example "следуи", "ушерб") are exactly what the font draws. The Russian translation
has no й or щ glyphs.

### Closing (VERIFIED)

* **CROSS.** `func_8006E508` tests the **held** pad word `D_800F16D8 & 0x800` (CROSS) only after the
  delay is 0. The delay is 15 passes: `main` 0x80066008 decrements it once per pass.
  * In the port a paused pass is 2 vblanks, so the prompt appears 30 frames after opening (8636 → 8666
    in RAM).
  * Holding CROSS from before the window opened closes it on the first pass with delay 0 (run
    `scroll5c`: CROSS held from 8650, closed at 8666).
  * A press after the prompt closes it about 2 frames later (sign 0: CROSS at 8720, closed at 8722).
* **START.** START closes the window at any time, including during the delay. `main` 0x80066294
  calls `func_800673E4` whenever the game is paused, without checking `D_800F125C`. Run `m02start`:
  START at 8648 with the delay at 9, closed at 8650.
* **Resume.** `func_800673E4`: `D_800F125C = 0`, `D_800F0E00 = 0`, voices and music restored, fade out
  `func_800A7F88(cam, 2, 1, 22)` over 2 passes. The game resumes on the next pass.
* **Message 0x71.** Message 0x71 ("Поздравления ! / Твое умение возросло") calls `func_8006AA04`, the
  main menu, instead of resuming. It is not used on level 01 (INFERRED: training arena).
* **No animation.** There is no open/close animation besides the 2-pass fade, and no sound.
* **Sign re-arm.** A sign re-arms 60 frames after it is read while Xena stays within 400
  (`docs/xena-level-scripts.md`).

## 2. Level-complete screen (end of level 01)

### Sequence (VERIFIED code + capture)

1. Siras (`func_8007E410`) finishes its camera path, then `func_8006C2D0` sets game state 13. Capture:
   teleport near Siras at 8600, state 13 at 9738.
2. State 13 runs one pass of `func_8006C2E4`:
   * `func_8006C4D0` computes the stats, sets state **14**, sets the delay to 10 and clears
     `D_800ED6AF`/`D_800ED6AE`/`D_800ED663`. It also calls `func_8008699C` (audio, UNKNOWN).
   * Level config byte `cfg+0xF` is 0x12 on level 01. That value means **no background picture**: the
     screen is drawn over the live 3D scene, and the HUD stays visible.
   * Any other value sets state 15, loads `data\screens\<cfg+0x10>.tim` (or `bk_slate.tim`) and draws
     it with `func_8006A924`.
3. Fade `func_800A7F88(cam, 32, 2, 32)`: a subtractive grey that ramps 0..32 over 32 passes (t·32/32),
   then holds. The HUD and text are drawn after it.
4. Every pass, `func_8006CCF4` draws the screen and handles input. In state 14 the main loop keeps
   running, so the pass counter advances, but the captured scene does not change.

### Layout (VERIFIED, pixel-exact)

Font abr 1. Text calls in creation order:

| element | when | anchor / align | colour | style | text |
|---|---|---|---|---|---|
| title | always | (160,34) centre, `"%s\n\n"` | (125,125,70) | 8 | `cfg+0x14` "Путь к Обалусу" |
| row "Время" | step ≥ 1 | label left x 45; value right x 270 `"%d:%02.2d\n"` (with hours `"%d:%02.2d:%02.2d\n"`) | (100,100,100) | 8 | level time |
| row "Убито врагов :" | step ≥ 3 | label x 45; value right x 270 `"%d\n"` | (100,100,100) | 8 | `D_800F1224` |
| row "Заложники :" | step ≥ 5 and value ≠ 0 | as above | | | `D_800F1228`, not shown on level 01 |
| row "Собрано вешеи :" | step ≥ 7 | as above | | | `D_800F122C` |
| row "Свитки :" | step ≥ 9 | value `"%3d   "` right x 257, then `" %d\n"` left x 257 | (100,100,100) | 8 | `D_800F1230` and the constant 2 |
| row "\nВсего свитков:" | step ≥ 11 | leading newline = blank row; values as "Свитки" | (125,125,70) | 8 | `D_800F277B` and `D_800F1232` |
| prompt "Нажми ⓧдля продолж" | delay == 0 | (160,210) centre | (175,200,50) | 0xA | `D_800CCEA4` |
| prompt "Нажми ○для сохр" | done, delay 0, card state allows | (160,194) centre | inherited (175,200,50) | 0xA | `D_800F0168` |

* **Row positions.** Rows start at y 68 (34 + 2·17) and advance 17 px. Level 01 has no hostages, so
  the rows are at y 68, 85, 102, 119 and the total at 153.
* **Labels and values.** The labels have no newline. The value call ends the row.
* **Glow.** Style 0xA = shadow + green glow (4 one-pixel copies, abr 3). The green channel pulses
  `clock & 32 ? (clock & 31)·7 : (32 − (clock & 31))·255/32`; R and B are 15.
* **Save prompt.** It is drawn unless `D_800F114C == 1`, or `D_800F114C == −1` with `D_800F1148 == 1`.
  These are memory-card states (INFERRED).
* **Level time.** `D_800F16B8` (vblanks, /60 → seconds) is split into h, m, s. The capture shows 0:28.
* **Final level.** On level 26 (0x1A) the screen is different (INFERRED, strings only): "Квест
  завершен !", "Твое место : {Боги|Лорд|Воин|Ученик}", "\nТвои результаты:", no scroll-of-2 row, no
  save prompt. CROSS returns to the main menu.

### Timing and input (VERIFIED)

* **Reveal.** The reveal step `D_800F1244` starts at 0. Once the delay is 0, `step += 2` every **30
  vblanks** (by clock), with sound **0x38** each time. At step 4 with no hostages it adds 2 more, so
  the hostage row is skipped. At step ≥ 11, `D_800ED6AF = 1` (done).
  * Capture: step 2 at 9770, 6 at 9802, 8 at 9830, 10 at 9862 and 12/done at 9894 (frame numbers are
    RAM frames).
* **Skip.** Any new press while the delay is 0 and step < 10 sets step 10 and delay 10, and the same
  pass adds 2, giving 12 (done). Capture: TRIANGLE at 9775 → step 12 at 9780. The prompts then return
  after the 10-pass delay.
* **CROSS.** A new press with step ≥ 11 and delay 0 plays sound 0 and sets `D_800ED6AE = 1`. On the
  next pass `func_8006C04C(0)` runs: state 10, next level = `cfg+1` = **2**, and the loading picture
  `data\levscr\load02.tim` (320x240, story text baked in, "Беда в Убеалосе").
  * Capture: CROSS at 9950 → state 10 at 9954, level 2 / state 12 at 9994.
  * Frames 9954-9988 show load02 darker (mean −41 per 8-bit channel). The cause is INFERRED: the
    stats fade is still on, since `D_800F078E` = 1. From 9989 it is pixel-identical to the TIM
    (76800/76800).
  * From 9997 a progress bar grows in rows 211-214 until gameplay of level 2 at about 10590. Which code
    draws the bar is UNKNOWN. Load time on hardware is UNKNOWN.
* **CIRCLE.** `0x1000` (CIRCLE) when done opens the memory-card save screen (`func_800675F0`, state 4).
  Capture: CIRCLE at 9950 → state 4, "След: Беда в Обалусе / Выбери 1 : / Записана : Этап 5 /
  Возможно ×3 / Вых". The return to the stats screen (`D_800F1170`) was not traced.

## 3. Verification

Executable: `build/port/xena-console.exe`, 2026-10-22 21:11:35, sha1 c71c6bb06864 (the native port,
not PS1 hardware).

Run settings:

* headless, `--render-scale 1 --aspect 4:3 --opt perspective=0 --opt texture_filter=0 --opt renderer=software`;
* script `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS,...`;
* Xena teleported by `--poke F:801BB494/98/9C:x/y/z:4`. Her position vector is stable in this run;
  `D_800EE174` = 0x8014E4E0, +0x5C → 0x801BB494;
* pickups and signs need walking contact: teleport on the object, then `8610:UP:10,8640:DOWN:25`.

Each run's exact command line is in `build/message-window/<run>/cmd.txt`.

**Method.** Each scenario was run twice with identical inputs. The second ("bg") run also pokes the
first byte of every drawn string to 0 (`--poke 8600:ADDR:0:1`), so it renders the same frames with
no text. The exporter composes `bg screenshot + modelled text` and compares it with the text run over
the full 320x240 frame in 15-bit colour. The runs are deterministic: frames before the text are
byte-identical.

Command:

`node tools/export_message_window.mjs --root . --out build/message-window/export --verify build/message-window --stats-run endtext2`

| scenario (runs) | frames compared | exact frames (76800/76800 px) |
|---|---|---|
| sign 0x46 (`m46`/`m46bg`) | 70 (8600-8740, 62 with text, 48 with prompt) | 70 |
| sign 0x47 (`m47`) | 70 | 70 |
| sign 0x48 (`m48`) | 70 (26 with text) | 70 |
| scroll 0x02 (`m02`) | 70 (51 with text) | 70 |
| scroll 0x03 (`m03`, pad glyph) | 70 | 70 |
| potion 0x36 (`m36`) | 70 | 70 |
| sword 0x45 (`m45`, 5 lines) | 70 | 70 |
| level complete (`endtext2`/`endbg`), every frame 9742-9976 | 233 | **233** |

**Lag rules.**

* **Message window.** The screenshot at F shows the state of the RAM dump at F−2. Text starts on the
  first dump with `D_800F125C && D_800F0E00`.
* **Stats screen.** State 14 is not frame-locked in the port (passes of 1-2 vblanks). The glow needs
  the exact pass:
  * screenshot F shows pass `p = D_800ECEA0(F) − 1`;
  * the pass draws the state of its last dump and the clock of its first dump.
  * With a fixed "RAM F−2" rule only 153/235 frames match, every miss being one glow step.
* **State 13.** The state-13 pass already draws the title.

Evidence images are in `build/message-window/export/verify/`: `m*.png` and `stats_pass_rule.png`,
each laid out as screenshot | model | difference (red = differs).

Other captures:

* `scroll5b`/`scroll5c` (delay, held CROSS);
* `sign0` (CROSS close);
* `m02start` (START close);
* `siras`, `siras2` (CROSS → load02 → level 2);
* `siras3` (skip + CIRCLE save screen).

## 4. Export (`build/message-window/export/`)

* `labels/msg_<id>.png`: the whole message block in screen position. `msg_<id>_line<i>.png` holds
  single lines and `msg_prompt.png` the prompt.
  * Each image is the additive layer: texel·colour>>7, to add to the screen.
  * `_shadow.png` is the mask of pixels to halve. Draw it before the glyphs.
  * `screenTopLeft` in the JSON places the image.
* `labels/stats_*.png`: title, row labels, level-01 values, both prompts (with `_glow.png`), the
  hostage label, and `stats_level01_full*` (the whole captured screen).
* `font/xenafont_glyphs.png` and `font.glyphs` in the JSON: byte → glyph cell, width, advance, digit
  draw offset (−9, or −8 for '2'/'4') and y offset. These render any value with the rules above.
* `load/load02.png`: the next-level loading picture.
* `message_window.json`:
  * layout rules, colours, styles, timing and input;
  * all strings (bytes + Russian);
  * `executableChecks` (40 immediates);
  * `verification`.

## 5. Unity notes

* **Message window.**
  * Pause the simulation and mute SFX. Darken the scene with a subtractive grey 22/255 over 2 frames.
  * Do not draw any box.
  * Place the lines centred at x 160 from y `114 − 8n`, 17 px apart, in cyan (50,150,150) with the
    half-dark shadow at +1/+2.
  * After 15 passes (≈0.5 s at 30 Hz, INFERRED for hardware) show the green prompt at y 194. Accept a
    held CROSS (Confirm) to close.
  * START (Pause) closes at any time.
  * Fade out over 2 frames and resume.
* **Level complete.**
  * After the Siras camera path, keep rendering the scene and HUD.
  * Fade a subtractive grey to 32/255 over 32 passes.
  * Reveal the rows every 0.5 s (30 vblanks) with sound 0x38; any button skips.
  * Show the pulsing green-glow prompts.
  * CROSS loads level 2 behind `load02.png`; CIRCLE opens the save screen.
