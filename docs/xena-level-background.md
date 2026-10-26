# Level background and depth cueing (level 01)

Research note for the Unity Original mode (`PrototypeSky.cs`, `OriginalBackground`). Labels:
VERIFIED = code plus native RAM/pixel evidence, INFERRED = code reading without a direct native check,
UNKNOWN = not established.

Evidence executable: `build/port/xena-console.exe` (built 2026-10-22 21:11), disc
`../Xena Warrior Princess.bin`, script `7400:CROSS,7700:CROSS,8000:CROSS,8300:CROSS,8800:UP:900`,
native 1x options (`--render-scale 1 --aspect 4:3 --opt perspective=0 --opt texture_filter=0
--opt subpixel=0`) unless stated. RAM dumps and frames were temporary and have been deleted; the PNGs kept
are in `build/level-background/`.

## 1. ENVIRO.BIN (per level, 100 bytes)

Loader `func_8004D2E8` reads `data\levels\%02d\Enviro.bin` into `D_800C15F0`, checks the magic
`0x9999ABCD` (`+0x04` = valid flag) and clamps fields (`0x8004D37C..0x8004D508`). It is called from the level
loader `func_8006287C` (`0x80062AD8`). Level-01 values (identical in the file and in RAM at frame 9800, VERIFIED):

| off | RAM | level 01 | meaning |
|---|---|---|---|
| +0x08 | `D_800C15F8` | `00 4A 61` = RGB(0,74,97) | fill colour **below** the horizon band (RAM `D_800F0736..8`) |
| +0x0C | `D_800C15FC` | `15 61 D6` = RGB(21,97,214) | fill colour **above** the horizon strip (RAM `D_800F0739..B`) |
| +0x10 | | RGB(0,74,97) | copied to `D_800F073C`; use UNKNOWN |
| +0x14..+0x2C | | | light colours/directions (+0x14 ambient 2A2A2A); not covered here |
| +0x3C..+0x4C | `D_800C162C..163C` | 1, 13108, 9780, 0, RGB(0,74,97) | copied to camera `+0x100..+0x110` by `func_800645C4` |
| +0x40 | `D_800C1630` | 13108 | fog **far** (clamped 0..32000) |
| +0x44 | `D_800C1634` | 9780 | fog **near** (clamped to 0..far) |
| +0x4C | `D_800C163C` | RGB(0,74,97) | **fog/far colour** (camera `+0x110`) |
| +0x50 | `D_800C1640` | 16108 | camera `+0x6C` (= far + 3000); probably the far draw distance (INFERRED) |
| +0x54 | `D_800C1644` | 35 | LGT brightness bias (see `xena-level-scripts.md`) |

Camera struct pointer: `D_800F10F4` (level 01 = `0x800FAA90`); `+0x1C..+0x24` position, `+0x28` pitch (s16,
4096 per turn, positive = looking down), `+0x2A` yaw (s16), `+0x30..+0x38` forward vector (x4096),
`+0x44/+0x48` = cos/sin of yaw (x4096), `+0x74` projection H = 400. VERIFIED from RAM at frame 9800: yaw -399,
forward (2269,1050,3243); the camera-to-Xena heading `atan2(dx,dz)` = +399 units, so heading = -yaw.

## 2. Background (clear colour, sky strip)

`func_800A5D1C` (called from the main loop at `0x80066B40`; enabled by `D_800F0775`) builds the
background every frame before the scene. It does not use a world-space sky mesh: everything is screen-space
(VERIFIED by exact pixel match, see below).

* `A = ratan2(cam+0x48, cam+0x44)` = camera yaw (equal to `cam+0x2A`). `P = cam+0x28` (pitch).
* Horizon top row (PSX 240-line space): `s5 = trunc(-P * 45/64)`
  (`a3 = ((0x400-P)&0xFFF)-0x400`, `*240`, `*0x300C0301 >> 38`, rounded toward zero; `0x800A5DBC..5E60`).
* `ClearImage` fills (direct VRAM fills, behind everything):
  1. rows `[0, s5)` with ENVIRO+0x0C RGB(21,97,214), only if `s5 > 0` (without a horizon file, up to `s5+128`);
  2. rows `[s5+128, s5+163)` (35 rows) with the fog colour cam+0x110 = RGB(0,74,97);
  3. rows `[s5+163, 240)` with ENVIRO+0x08 RGB(0,74,97).
* Horizon strip (`D_800F1690` = 1 when `HORIZ.STM` exists): rows `[s5, s5+128)` are 6 SPRT 64x128
  (`0x65`, raw texture: **no colour modulation and no fog**), added to the last OT entry `s6+0xFC`, so all
  geometry draws over them. Prims at `D_800FAE88`/`D_800FB788` (0x24 bytes each, 64 slots), built by
  `func_800A5988` at load.
* Texture: `DATA\LEVELS\01\HORIZ.STM`, 8 TIMs `horiz01..08`, 8-bit, 64x128 each, CLUT (768,18).
  Joined in file order they form a 512x128 panorama: `build/level-background/horiz01-panorama.png`.
  Its top row averages RGB(20,101,218) (= the ENVIRO+0x0C sky fill) and its bottom row RGB(2,76,101) (= the fog
  colour), so the strip joins both fills without a seam. The light "clouds" measured in HD frames
  (e.g. RGB(115,148,189) on rows 0..35 at 720p) are rows 32..100 of this image.
* Horizontal scroll: slot `k` shows `horiz((k+4) mod 8 + 1)`; the panorama repeats every 8 tiles. Screen
  column `x` (0..319) shows panorama column `X = (x - A/2 + 256) mod 512` (file order) in row `y - s5`.
  That is 0.5 px per yaw unit: 2048 px per turn, so the 512 px image is shown **4 times per 360 degrees**. When
  the camera turns right (heading up, yaw down), the image moves left. There is no vertical tiling; the
  vertical position follows only the pitch.
* Native check, frame 9800 (yaw -399, pitch 169, so `s5 = -118`): rows 0..9 match panorama rows 118..127 at
  `X = (x+199) mod 512` in slot order **exactly** (all sampled pixels). Rows 10..44 are RGB(0,74,99)
  (RGB(0,74,97) after 555 quantization; the HD path shows about (0,72,95)). Poking `0x800F1690=0` turns rows
  0..9 into RGB(21,97,214) fill. Images: `build/level-background/horizon-native-9800.png`
  (base | far colour poked red | horizon off).

## 3. Depth cueing (fog)

* Setup: `func_800645C4` -> `func_800A8188(h=400, &near, &far)` calls `SetFogNearFar(9780, 13108, 400)`, then
  overwrites DQA/DQB from the EXE table `D_800DA860[level]` (8 bytes per level). For level 01 the entry is
  DQA = -24653 and DQB = 66080452. This equals the Psy-Q formula for near 9780, far 13108 and h 400
  (`DQA = -near*far/(far-near)*256/h`, `DQB = far/(far-near)*2^24`; also matches levels 2 and 3). Level 7's
  entry is 0/0 (no fog) although its ENVIRO has near/far values. The far colour is set by `func_800A5368(0,74,97)`
  in scratchpad `0x1F800034..3C` (x16) and `0x1F800044` (packed), together with a flat CLUT at VRAM (768,0):
  entry 0 = 0 (transparent) and entries 1..255 = `0xC210` (neutral grey).
* Per vertex (level renderer `func_80010000`, `0x800102E4..0x800103A0`): RTPS gives
  `IR0 = clamp((DQB + DQA*H*65536/SZ) >> 12, 0, 4096)`, where SZ is the **view-space Z** (along the camera
  forward axis, world units). Then:
  `IR0 = 0`: no fog; `IR0 >= 4096`: base colour 0 plus the far colour; otherwise `DPCS` gives the base colour
  multiplied by `1-f` (with FC = 0) and the far colour multiplied by `f`.
* Draw: every textured triangle is emitted twice (`0x8001099C..`): a normal textured GT3 with the attenuated
  colours, and an additive (`abr=1`) GT3 with the same UVs and the flat CLUT, coloured `far*f`. The result is
  `texel*colour*(1-f) + far*f`. Cut-out texels (index 0) stay transparent in both passes.
  Gouraud interpolation of `f` is affine in screen space.
* Actors (TMD renderer `func_800118F0`, `0x80011CC0..0x80011D34`) use the same two-pass fog with one `f` per
  triangle (the IR0 after RTPT). Billboards drawn through `func_80071F9C -> func_80010000` take the same code
  path (INFERRED). The horizon and background fills are never fogged.
* Level-01 formula, with z = SZ/400 in metres:
  `f(z) = clamp(3.938705 - 96.3008 / z, 0, 1)`, which is `(32.77/(32.77-24.45)) * (1 - 24.45/z)`.
  The fog starts at **24.45 m** and is solid at **32.77 m**. It is hyperbolic, not linear: at the midpoint (28.6 m)
  f = 0.57, not 0.5. Values: 26 m 0.235, 28 m 0.499, 30 m 0.729, 32 m 0.929. The camera `+0x6C` limit is
  16108 = 40.27 m.
* Native check: the far colour was poked to red in scratchpad at frame 9400, so `f = (R_red - R_base)/255`
  (the level-01 far R is 0). Fully fogged walls turned solid red, and the near terrain, Xena and a distant
  bandit stayed unchanged. Level vertices from RAM, projected with the camera forward/yaw/pitch, gave for instance
  z 12843 (32.1 m) predicted 0.939 / measured 0.937, z 12292 0.805/0.776, z 12343 0.818/0.839, z 13226 1.0/1.0,
  and every visible vertex below 9000 measured 0. Samples on occlusion and polygon edges scatter (median error
  0.16) because the projection is approximate, so the per-pixel match is INFERRED. Image:
  `build/level-background/fog-native-9500.png` (base | red far colour | f map).

## 4. Unity Original-mode recipe

1. **Fog in the shader** (built-in linear/exp fog cannot express it): `z` = view-space depth in metres
   (`-viewPos.z`, not the radial distance), `f = saturate(3.938705 - 96.3008/z)`,
   `rgb = lerp(rgb, (0,74,97)/255, f)`. Apply it to level meshes and actors (per vertex reproduces the original,
   per pixel is acceptable) but not to the background, and keep alpha cut-outs. Other levels:
   `f = saturate(F/(F-N) * (1 - N/z))` with N = ENVIRO+0x44/400 and F = ENVIRO+0x40/400, except level 7 (no fog).
2. **Background** as a screen-space pass behind everything, in a virtual 320x240 space scaled to the
   viewport (for widescreen, extend x at the same px scale):
   `s5 = trunc(-pitch*45/64)` (pitch in 4096 units, down positive), then rows `<s5` RGB(21,97,214),
   rows `s5..s5+127` the panorama (unlit, point-sampled, `u = ((x - yaw/2 + 256) mod 512)/512` with
   yaw = -heading in 4096 units, `v = (y-s5)/128`), rows `s5+128..` RGB(0,74,97).
   Replace the camera clear with RGB(0,74,97); it equals the bottom of the panorama and the fog colour.
3. **Oracle** (run against the native frames with the same script):
   * Background, frame 9800 (pitch 169, yaw -399): PSX rows 0..9 = panorama rows 118..127 at
     `X = (x+199) mod 512` in slot order (`(x+455) mod 512` in file order); rows 10..44 = RGB(0,74,97±2).
   * Fog: a surface of colour C at view depth z gives `C*(1-f) + (0,74,97)*f`. For example, grey (128,128,128)
     gives (128,128,128) at 24 m, (64,101,113) at 28 m, (35,89,105) at 30 m and (0,74,97) at 33 m or more.
     Native spot checks: frame 9500 vertices at 32.1 m: f 0.94 and at 30.7 m: f 0.78..0.81 (±0.03).
