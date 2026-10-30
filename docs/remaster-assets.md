# Remaster (F10) third-party and generated assets

Remaster is presentation only (see `.claude/rules/small-fixes.md`). Downloaded and generated
files live under the Unity projects' `Assets/Imported/` (not committed); the tools that fetch
or build them are committed.

| Asset | Source / tool | Licence | Used for |
| --- | --- | --- | --- |
| Sky HDRI `kloofendal_48d_partly_cloudy_puresky` (4k) | Poly Haven | CC0 1.0 | Remaster sky dome (`Xena/RemasterSky`) |
| Rock051 | ambientCG | CC0 1.0 | triplanar rock detail (`Xena/RemasterLit`) |
| `bark_brown_02` (tree bark) | Poly Haven | CC0 1.0 | generated trees (`PrototypeRemasterTrees`) |
| `forrest_ground_01` (Diffuse 1k, `Resources/Remaster/Vegetation/ForestGround.jpg`, `tools/fetch_polyhaven_texture.py`) | Poly Haven | CC0 1.0 | outer-world ground band and path (`PrototypeRemasterOuterWorld`) |
| `fern_02`, `shrub_02` | Poly Haven, `tools/fetch_polyhaven.py` | CC0 1.0 | ground plants replacing `@pretty` (`PrototypeRemasterVegetation`) |
| Tenor Sans | Google Fonts (`Runtime/Resources/Fonts`, licence file alongside) | SIL OFL 1.1 | main menu / exit menu text |
| Real-ESRGAN x4plus (ncnn-vulkan v0.2.5.0) | github.com/xinntao/Real-ESRGAN, local tool `../_tools/realesrgan` | BSD-3-Clause | 4x upscales: boot videos (EA_LOGO, UNILOGO, INTRO), GAME OVER video, menu lightning, panorama |
| HUD, pickups, menu background, panorama segments, level texture pages, leaf and grass atlases | Codex CLI image_gen from the original art (`tools/codex-remaster/`) | generated from the original game's art | Remaster HUD, menu, sky landscape, `page-N-4x.png` |
| Hunyuan3D-2 / 2mini (shape), Hunyuan3D-2 Paint | github.com/Tencent/Hunyuan3D-2, local `../_tools/Hunyuan3D-2`, `tools/hunyuan_shape.py`, `tools/hunyuan_paint.py` | Tencent Hunyuan Non-Commercial / Community licence (not EU, UK, South Korea; non-commercial) | image-to-3D Remaster props (trial: signpost) |
| `rock_face` displacement 1k (`Resources/Remaster/Rocks/CliffHeight.bytes`, `tools/install_cliff_height.py`) | Poly Haven | CC0 1.0 | rock-wall relief of the Remaster terrain (`PrototypeRemasterGeometry`, +-0.5 m) |
| Scans `mountainside`, `namaqualand_cliff_01/02`, `rock_face_01/02`, `namaqualand_boulder_03`, `boulder_01` (geometry only, `../_tools/remaster-scans`, `tools/fetch_remaster_shell_assets.py`) | Poly Haven | CC0 1.0 | melted into the closed shell (`tools/build_remaster_shell.py`) |
| `rock_face`, `mossy_rock`, `forest_ground_04`, `brown_mud_leaves_01` (2k colour + normal, `Resources/Remaster/Shell`) | Poly Haven | CC0 1.0 | world-space layers of the shell (`Xena/RemasterShell`) |
