#!/usr/bin/env python3
"""Download the CC0 Poly Haven scans and surfaces used by tools/build_remaster_shell.py.
Rock scans (glTF geometry only) go to ../_tools/remaster-scans/<asset>/, outside the repository; the
surface textures go to Resources/Remaster/Shell/ of the Unity project (local data, not committed).
Nothing downloaded is executed: the builder only reads the JSON and binary buffers."""
import json, os, re, shutil, urllib.request

HEADERS = {"User-Agent": "xena-psx-decomp (remaster shell)"}
SCANS = ["mountainside", "namaqualand_cliff_01", "namaqualand_cliff_02", "rock_face_01", "rock_face_02", "namaqualand_boulder_03", "boulder_01"]
# Shell layer -> Poly Haven texture.
SURFACES = {"Rock": "rock_face", "Moss": "mossy_rock", "Ground": "forest_ground_04", "Path": "brown_mud_leaves_01"}
PROJECT = "unity/XenaLightingVerification/Assets/Imported/Resources/Remaster"


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS)) as r:
        return r.read()


def main():
    for asset in SCANS:
        root = os.path.join("..", "_tools", "remaster-scans", asset); os.makedirs(root, exist_ok=True)
        entry = json.loads(get("https://api.polyhaven.com/files/" + asset))["gltf"]["1k"]["gltf"]
        open(os.path.join(root, "model.gltf"), "wb").write(get(entry["url"]))
        for path, inc in entry["include"].items():
            if path.endswith(".bin"): open(os.path.join(root, os.path.basename(path)), "wb").write(get(inc["url"]))
        print("scan", asset)
    target = os.path.join(PROJECT, "Shell"); os.makedirs(target, exist_ok=True)
    meta = open(os.path.join(PROJECT, "RockNormal.jpg.meta"), encoding="utf-8").read()
    sources = {}
    for layer, asset in SURFACES.items():
        files = json.loads(get("https://api.polyhaven.com/files/" + asset))
        open(os.path.join(target, layer + "Color.jpg"), "wb").write(get(files["Diffuse"]["2k"]["jpg"]["url"]))
        open(os.path.join(target, layer + "Normal.jpg"), "wb").write(get(files["nor_gl"]["2k"]["jpg"]["url"]))
        guid = __import__("hashlib").md5((layer + "shellnormal").encode()).hexdigest()
        open(os.path.join(target, layer + "Normal.jpg.meta"), "w", encoding="utf-8").write(re.sub(r"guid: \w+", "guid: " + guid, meta, count=1))
        sources[layer] = "https://polyhaven.com/a/" + asset
        print("surface", layer, asset)
    json.dump({"licence": "CC0 1.0", "surfaces": sources, "scans": ["https://polyhaven.com/a/" + a for a in SCANS]}, open(os.path.join(target, "source.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
