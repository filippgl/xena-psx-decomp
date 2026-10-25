#!/bin/bash
# Closing segment 4: left 30% = right of pano-3, right 30% = left of pano-1.
cd "$(dirname "$0")"
python -c "
from PIL import Image
a=Image.open('out/pano-3.png').convert('RGB'); b=Image.open('out/pano-1.png').convert('RGB'); w,h=a.size; k=int(w*0.3)
c=Image.new('RGB',(w,h),(128,128,128)); c.paste(a.crop((w-k,0,w,h)),(0,0)); c.paste(b.crop((0,0,k,h)),(w-k,0)); c.save('in/pano-4-seed.png')"
cat > prompt-pano-4.txt <<P
Use your built-in image_gen tool (imagegen skill, built-in mode, not the CLI), editing mode with the attached image.
The attached 1536x1024 image is the last segment of a seamless 360-degree landscape panorama (background of a game level):
its LEFT 30% and RIGHT 30% are already painted, the grey band in the middle is empty.
Keep both painted sides exactly as they are and paint the middle so that the left side flows continuously into the right
side: same horizon height, same mist, forested hills, jagged grey crags in the distance, soft pale-blue daytime sky with light
clouds, valley floor fading into the same pale blue-grey haze at the bottom. No text, no border, no foreground objects. 1536x1024.
Save the final selected image into this workspace as out/pano-4.png (copy it from CODEX_HOME/generated_images). Do nothing else.
P
timeout 1500 codex exec -s workspace-write --skip-git-repo-check -C "$(pwd -W)" -i in/pano-4-seed.png - < prompt-pano-4.txt > codex-pano-4.log 2>&1
echo "pano-4 exit=$? $(ls out/pano-4.png 2>/dev/null)"
