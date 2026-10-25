#!/bin/bash
# usage: pano_next.sh N  -> continues out/pano-(N-1).png into out/pano-N.png (left 40% kept)
cd "$(dirname "$0")"; n=$1; p=$((n-1))
python -c "
from PIL import Image
src=Image.open('out/pano-$p.png').convert('RGB'); w,h=src.size; keep=int(w*0.4)
c=Image.new('RGB',(w,h),(128,128,128)); c.paste(src.crop((w-keep,0,w,h)),(0,0)); c.save('in/pano-$n-seed.png')"
cat > prompt-pano-$n.txt <<P
Use your built-in image_gen tool (imagegen skill, built-in mode, not the CLI), editing mode with the attached image.
The attached 1536x1024 image is the start of the next segment of a seamless 360-degree landscape panorama (background of a
game level): its LEFT 40% (614 px) is already painted, the grey area on the right is empty.
Keep the left 40% exactly as it is (same mountains, mist, sky, clouds, horizon height, colours and lighting) and paint the
empty right part as a natural continuation of the same landscape: forested hills in drifting mist, tall jagged grey crags and
spires in the distance, soft pale-blue daytime sky with light clouds, valley floor fading into the same pale blue-grey haze at
the bottom. Vary the shapes (do not repeat the left side). No text, no border, no foreground objects. Output 1536x1024.
Save the final selected image into this workspace as out/pano-$n.png (copy it from CODEX_HOME/generated_images). Do nothing else.
P
timeout 1500 codex exec -s workspace-write --skip-git-repo-check -C "$(pwd -W)" -i in/pano-$n-seed.png - < prompt-pano-$n.txt > codex-pano-$n.log 2>&1
echo "pano-$n exit=$? $(ls out/pano-$n.png 2>/dev/null)"
