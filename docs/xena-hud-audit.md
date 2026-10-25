# Xena in-game HUD audit (SLUS-00977)

Goal: enough evidence to rebuild the original in-game HUD in Unity. Tool: `tools/export_hud.mjs`
(PNG per texture, STP masks, `hud_layout.json`, overlay/diff images). Snapshot used:
`build/level-assets/01/native-reference/{ram,vram,frame}_009200.*` (level 01, frame 9200).

Labels: **VERIFIED** = read from code and confirmed in the RAM/VRAM snapshot; **CODE** = read from the
MIPS code, no snapshot state exercised it; **INFERRED** = meaning/name deduced; **UNKNOWN**.

## 1. Screen space and primitive — VERIFIED

- PS1 resolution **320x240**: the VRAM snapshot holds two 320x240 framebuffers at (0,0) and (0,256); all
  HUD coordinates below are top-left pixels in that space. The port frame (427x240) shows the same HUD
  shifted **+53 px** horizontally (best offset search: 807/821 opaque texels exact; the rest are port-side).
- Sprite manager: table `D_800FC088`, stride 0x24, max 0x3A, count `D_800F078A` (57 in the snapshot).
  `func_800A7D3C(cx, cy, desc, r, g, b, abr, semi, layer)` -> `func_800A5634`: DR_MODE (dither off,
  tpage = GetTPage(depth, abr, x, y)) merged with SPRT (code 0x64, `SetShadeTex(1)` -> raw texture,
  no colour modulation). Top-left = centre - trunc(w/2, h/2). +0x20 visible (`func_800A7DE8`),
  +0x1A CLUT (`func_800A7E70`), SetSemiTrans (`func_800A7E20`), crop `func_800A7EEC`->`func_800A55D0`
  (x, y, u = (vramX&63)*k + du, v = (vramY&255) + dv, w, h).
- Draw: `func_800A57B8` AddPrims each visible entry to OT[0] (layer 1). AddPrim prepends, so the
  **highest sprite id is drawn first, id 0 last**. Proof: with that order 821/821 opaque HUD texels
  match framebuffer (0,0) and (0,256) exactly; in creation order pixel (41,213) fails (the hilt overlaps
  the blade's first column).
- Transparency: texel 0x0000 never drawn (PNG alpha 0). Semi-transparency applies only to texels with
  STP (bit 15; `*-stp.png` white). All 763 blended texels in the frame fit abr 0 (R = (B+F)>>1).
- Textures are uploaded at their own TIM VRAM rectangle by `func_800A7224` (no relocation).

## 2. Loading — `func_8007515C` (from main 0x80065C7C), reset `func_8007674C`, update `func_80075C2C`

`func_80075C2C` runs every frame (0x8006D26C, 0x8006637C, 0x800769E0). Enemy/boss bars come from
`func_80076258(actor, force)`, called by the enemy AIs and by damage `func_80032904` (0x800329A0).

## 3. Elements (320x240, top-left)

| Element | Texture (VRAM px, CLUT, depth) | Position / size | Blend | Sprite id var | State |
|---|---|---|---|---|---|
| Xena hilt | HILT.TIM (976,385) clut (768,8) 8bpp | (16,206) 28x14 | opaque (abr1, semi off) | D_800F1418 | VERIFIED |
| Xena blade (full part) | SWORD.TIM (990,385) clut (784,6) 4bpp | (41,209) len x6 | opaque | D_800F1478 | VERIFIED |
| Xena blade (empty part) | BLOOD.TIM (990,391) clut (800,6) 4bpp | (41+len,209) (112-len)x6, u+=len | opaque | D_800F13E0 | VERIFIED |
| Boss hilt / blade / empty | HILT / SWORD / BLOOD | (93,24) / (118,27) / (118+len,27) | opaque | D_800F1332, D_800F2F10, D_800F2F11 | CODE (hidden in snapshot) |
| Enemy bar slot i=0..3 | DAGGER.TIM (990,397) clut (768,8) 8bpp + DAGGERED.TIM (990,408) clut (800,6) 4bpp | (40+65i,25) 48x11 | opaque | D_800F2ED8+12i (+8/+9) | CODE |
| Companion bar | DAGGER + DAGGERED | (16,193) 48x11 | opaque | D_800F1333 / D_800F1334 | CODE |
| Karma ring, 29 segments | KARMA.STM (29 8bpp images), clut (768,11); lit clut = KARMAG3.TIM (768,7) | (250+dx,191+dy), offsets D_800C76C0 | semi abr0, all texels STP | D_800F1380[29] | VERIFIED (unlit) |
| Inventory icons 0..6 | 1ICHLTH (4bpp 12x24), 1ICLTNG/DETH/FIRE/ROCK/SNOW/LYRE (8bpp 16x16) clut (768,5)/(768,6) | hlth (295,20); others (293, 48+20(i-1)) | abr0; semi when not selected; all texels STP | D_800F2E28+0x1C*i | CODE (hidden: counts 0) |
| Man icons 0..3 | MAN_ICON.TIM (968,385) 14x20 8bpp | (176+18i,200) | opaque | D_800F1358[4] | CODE |
| Key icon | KEY1_HUD.TIM (964,489) 8x16 4bpp | (296,170) | opaque | D_800F1378 | CODE |

Icon order is the exe table D_800EE204: hlth, ltng, deth, fire, rock, snow, lyre (VERIFIED).

## 4. Value -> visual mapping

- **Xena health (CODE, VERIFIED at full health)**: actor `D_800EE174`, hp +0x10, max (+0xC)->+8 (500).
  `len = hp<=0 ? 0 : clamp(trunc((112-3)*hp/max), 3, 109)`. Shown length `D_800F1330` rises 1 px per
  frame toward len and drops to len at once. Snapshot: hp 500/500 -> blade w 109, empty x 150 u 229 w 3.
  No colour change and no low-health blink exists in this code.
- **Boss bar (CODE)**: enemy with descriptor (+0xC)->+6 bit0 (not when level `D_800F2772`==0x12); max =
  desc+8 + ((+0x138) ? (+0x138)->+0x8A : +0x84); len as Xena, no ramp. Shows the hilt and hides enemy
  slots 1-2.
- **Enemy bars (CODE)**: shown for enemies within 4000 units of Xena (class 0xAF: 30000; boss: 7500), checked
  every 8 frames per actor unless forced. `len = clamp(trunc((48-22)*hp/max)+18, 18, 44)`.
  Shown for 60 frames after each update (slot +4 counts down in `func_80075C2C`, paused while
  `D_800F0E00`/`D_800F0E25`); hidden when hp<=0.
- **Companion (CODE)**: `D_800EE178`; same dagger formula, hp<=0 -> 16; hidden when no companion.
- **Karma ring (CODE; unlit state VERIFIED)**: meter `D_800F1135` (0..96). `n = trunc(meter*29/96)`;
  segments `(i+15)%29` for i<n use the lit CLUT `D_800F13A0`=0x1F0, the rest the normal `D_800F139E`=0x2F0.
  It fills from segment 15 (offset (21,24), bottom centre). While power-up `D_800F1136`>0:
  phase=(frame `D_800ECEA0`>>1)&3; segments phase,phase+4,... get SetSemiTrans(1); phase+1,+5,... get
  the lit CLUT and SetSemiTrans(0) (rotating chase). Snapshot: meter 0, all 29 use clut (768,11).
- **Inventory icons (CODE)**: icon i visible iff count `D_800F277E+i` != 0 (D_800F277E = potions); not
  selected (`D_800F2787 & (1<<i)` == 0) -> semi (50% blend; every texel has STP), selected -> opaque.
  Item use/selection: `func_80041228` (pad bit 0x800), `func_80040E34`.
- **Man icons (CODE)**: only on level 2: n = 4 - `D_800F2777`; icon n blinks with bit 1 of `D_800F132A`.
  Elsewhere they are hidden while `D_800F264C` < 5. Meaning: INFERRED (rescue/escort count), UNKNOWN.
- **Key (CODE)**: levels 2, 3, 10, 25: visible = `D_800F277D` & 1.
- **Message caption** (not a sprite): while `D_800F132A`>0 the HUD prints string `D_800F03F4` at (160,178)
  with the font (see xena-level-scripts.md).

## 5. Not part of the in-game HUD / unknowns

- No chakram indicator exists in the HUD code. The bottom-right ring is the karma meter (`D_800F1135`),
  not a chakram state. Whether the chakram has any other on-screen indicator is UNKNOWN.
- Sword level `D_800F2786`, armour `D_800F2785` and SHLD_HUD.TIM (loaded to `D_800F1440`) are drawn
  only by `func_80066CDC`, a pause/inventory screen (INFERRED). Potion counts are not shown as digits
  on the HUD; only icon visibility shows them.
- Not observed in the snapshot (code only): lit/animated karma, icons, boss/enemy/companion bars,
  man and key icons, and partial health.
- The port frame differs from the PS1 framebuffer in 14 of 821 opaque HUD texels at offset 53. The cause is
  port-side and UNKNOWN.

## 6. Reproduce

```
node tools/export_hud.mjs --root <decomp> --out <dir> \
  --ram build/level-assets/01/native-reference/ram_009200.bin \
  --vram build/level-assets/01/native-reference/vram_009200.bmp \
  --frame build/level-assets/01/native-reference/frame_009200.bmp
```
Result (2026-10-25): 74 textures plus 2 ring composites; 0 VRAM word mismatches; all RAM sprite checks
pass; framebuffer 821/821 exact, 763/763 blends consistent; creation-order control 1 mismatch.
