#!/bin/bash
# Codex repaint of level texture pages: ./pages.sh N [N...] (sequential). Input: 4x nearest page,
# transparent texels shown as magenta. Output out/page-N.png (1024x1024 RGB).
cd "$(dirname "$0")"
for n in "$@"; do
  [ -f "out/page-$n.png" ] && continue
  python -c "
from PIL import Image
im=Image.open('../../unity/XenaLightingVerification/Assets/Imported/Level01/page-$n.png').convert('RGBA')
bg=Image.new('RGBA',im.size,(255,0,255,255)); bg.alpha_composite(Image.composite(im,Image.new('RGBA',im.size,(255,0,255,255)),im.getchannel('A').point(lambda v:255 if v>=128 else 0)))
bg.convert('RGB').resize((1024,1024),Image.NEAREST).save('in/page-$n.png')"
  cat > prompt-page-$n.txt <<P
Use your built-in image_gen tool (imagegen skill, built-in mode, not the CLI), editing mode with the attached image.
The attached 1024x1024 image is a texture atlas page from level 1 (a green Greek forest valley) of the PS1 game
"Xena: Warrior Princess" (1999), enlarged 4x with hard pixels. It holds several separate textures packed in a grid of
rectangles (usually 512x512 or 256x256 cells); magenta (#FF00FF) areas are transparent/unused.
Repaint EVERY texture in place as a modern high-resolution, photoreal, detailed game texture (crisp grass blades, soil,
pebbles, bark, leaves, ferns, stone with lichen, wood grain, etc., whatever each cell shows), in the quality of a beautiful AAA
matte painting.
STRICT RULES (the textures are UV-mapped onto 3D geometry):
- keep the exact layout: every cell and every element inside it keeps its position, size, silhouette and orientation;
- keep magenta areas pure flat magenta #FF00FF exactly where they are (they are cut out);
- keep the same colours and brightness as the original (do not brighten, do not saturate, do not change the hue);
- cell edges keep the original edge colours so tiling and neighbouring polygons still connect; no frames or borders;
- no text, no new objects, soft neutral overhead light, no vignette; output exactly 1024x1024.
Save the final selected image into this workspace as out/page-$n.png (copy it from CODEX_HOME/generated_images). Do nothing else.
P
  timeout 1800 codex exec -s workspace-write --skip-git-repo-check -C "$(pwd -W)" -i in/page-$n.png - < prompt-page-$n.txt > codex-page-$n.log 2>&1
  echo "page-$n exit=$? $(ls out/page-$n.png 2>/dev/null)"
done
