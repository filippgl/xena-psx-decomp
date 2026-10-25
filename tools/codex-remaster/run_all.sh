#!/bin/bash
cd "$(dirname "$0")"
declare -A DESC=(
 [health2]="a small glass potion flask with violet/blue liquid (health potion pickup)"
 [up_sword]="a vertical sword (sword upgrade pickup), blade pointing up"
 [1ichlth]="a tiny HUD inventory icon of a violet health potion flask"
 [hilt]="the ornate hilt and cross-guard of a sword, seen from the side, part of a health bar HUD (the blade attaches on the right)"
 [sword]="a long thin polished steel sword blade seen from the side, horizontal, pointing right (full health bar of a HUD)"
 [blood]="the same long thin sword blade, horizontal, pointing right, but dark and blood-stained (empty part of a health bar HUD)"
 [dagger]="a small horizontal dagger seen from the side, pointing right (enemy health bar HUD, full)"
 [daggered]="the same small horizontal dagger, darkened/blood-stained (enemy health bar HUD, empty)"
 [karma-ring]="a thin circular metal ring segmented into small studs, seen in perspective as an ellipse (karma meter HUD, unlit)"
 [karma-ring-lit]="the same thin elliptical segmented metal ring, its segments glowing (karma meter HUD, lit)"
)
declare -A SIZE=([health2]=1024x1536 [up_sword]=1024x1536 [1ichlth]=1024x1536 [hilt]=1536x1024 [sword]=1536x1024 [blood]=1536x1024 [dagger]=1536x1024 [daggered]=1536x1024 [karma-ring]=1536x1024 [karma-ring-lit]=1536x1024)
for n in health2 up_sword 1ichlth hilt sword blood dagger daggered karma-ring karma-ring-lit; do
  [ -f "out/$n.png" ] && continue
  cat > "prompt-$n.txt" <<P
Use your built-in image_gen tool (imagegen skill, built-in mode, not the CLI) to remaster the attached game sprite.
The attached image is a tiny PS1 sprite from "Xena: Warrior Princess" (1999), shown enlarged with nearest-neighbour pixels. Magenta (#FF00FF) is the transparent background.
It shows ${DESC[$n]}.
Create a modern high-resolution hand-painted version of exactly this object: same silhouette, same orientation, same proportions and the same colour palette as the sprite.
The object must fill the canvas edge to edge in the same way as the sprite fills its frame. No new elements, no text, no border, no drop shadow, no background.
Transparent background (real alpha). Output size ${SIZE[$n]}.
Save the final selected image into this workspace as out/$n.png (copy it from CODEX_HOME/generated_images). Do nothing else.
P
  timeout 900 codex exec -s workspace-write --skip-git-repo-check -C "$(pwd -W)" -i "in/$n.png" - < "prompt-$n.txt" > "codex-$n.log" 2>&1
  echo "$n exit=$? $(ls out/$n.png 2>/dev/null)"
done
