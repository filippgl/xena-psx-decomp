# Original English font (note for the later English localisation)

User request 2026-10-26: record what font the original English release used. Nothing is
implemented from this yet; UI text currently uses Underdog (Original) / Tenor Sans (Remaster),
see `PrototypeMenuText`.

- **Font asset (VERIFIED for the disc we have):** all in-game text is drawn from one bitmap font,
  `DATA\SCREENS\XENAFONT.TIM` (LBA 476, 4-bit, 256x128, 16x16 glyph cells, proportional widths =
  rightmost opaque column, advance = width + 3, digits advance 10, glyph y offsets by class).
  Rendering rules: docs/xena-main-menu.md ("Prompt") and docs/xena-message-window.md.
- **Our disc is a Russian translation (VERIFIED):** the text bytes are Latin code points whose
  glyph cells were repainted with Cyrillic letters (e.g. 'J' -> 'П', 'F' -> 'Ы', 'G' -> 'Ш';
  table in tools/export_message_window.mjs `GLYPH`). The digits, punctuation and button icons
  (bytes 1-4: ○, ⓧ, ...) are unchanged. So the original English Latin glyphs are NOT on this disc.
- **English release (UNKNOWN look):** Xena: Warrior Princess, PlayStation, Universal Interactive /
  EA, 1999 (NTSC-U SLUS-00977). It uses the same XENAFONT.TIM with Latin glyphs, a custom bitmap
  font, not a standard typeface. The glyph shapes are unknown until an English disc image (or a
  lossless English capture) is available; searches (MobyGames, eBay listings) gave no readable
  text screenshots.
- **How to get it later:** with an English BIN/CUE, run `python tools/uda_find.py xena.exe
  DATA\SCREENS\XENAFONT.TIM` against that disc's EXE, extract the TIM like the Russian one
  (tools/export_main_menu.mjs font export) and compare the Latin cells; or identify the face from
  a lossless capture of the English pause menu ("Continue / Restart Level / Options / Quit").
