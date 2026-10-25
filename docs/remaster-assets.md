# Remaster (F10) third-party and generated assets

Remaster is presentation only (see `.claude/rules/small-fixes.md`). Downloaded and generated
files live under the Unity projects' `Assets/Imported/` (not committed); the tools that fetch
or build them are committed.

| Asset | Source / tool | Licence | Used for |
| --- | --- | --- | --- |
| Sky HDRI `kloofendal_48d_partly_cloudy_puresky` (4k) | Poly Haven | CC0 1.0 | Remaster sky dome (`Xena/RemasterSky`) |
| Rock051 | ambientCG | CC0 1.0 | triplanar rock detail (`Xena/RemasterLit`) |
| `fern_02`, `shrub_02` | Poly Haven, `tools/fetch_polyhaven.py` | CC0 1.0 | ground plants replacing `@pretty` (`PrototypeRemasterVegetation`) |
| Tenor Sans | Google Fonts (`Runtime/Resources/Fonts`, licence file alongside) | SIL OFL 1.1 | main menu / exit menu text |
| Real-ESRGAN x4plus (ncnn-vulkan v0.2.5.0) | github.com/xinntao/Real-ESRGAN, local tool `../_tools/realesrgan` | BSD-3-Clause | 4x upscales: GAME OVER video, menu lightning, panorama |
| HUD, pickups, menu background and labels, panorama segments, level texture pages | Codex CLI image_gen from the original art (`tools/codex-remaster/`) | generated from the original game's art | Remaster HUD, menu, sky landscape, `page-N-4x.png` |
