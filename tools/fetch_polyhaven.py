#!/usr/bin/env python3
"""Download CC0 Poly Haven models (https://polyhaven.com, licence CC0 1.0) for the Remaster
vegetation into each Unity project's Assets/Imported/Remaster/Vegetation/<asset>/ (local data,
not committed). Uses the public API: /files/<asset> -> fbx/<res>/fbx {url, include{path: {url}}}.

usage: fetch_polyhaven.py [--res 2k] asset [asset ...]
"""
import argparse
import json
import os
import urllib.request

PROJECTS = ["unity/XenaUnity", "unity/XenaLightingVerification"]
HEADERS = {"User-Agent": "xena-psx-decomp (remaster vegetation)"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS)) as r:
        return r.read()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("assets", nargs="+")
    ap.add_argument("--res", default="2k")
    args = ap.parse_args()
    for asset in args.assets:
        files = json.loads(get("https://api.polyhaven.com/files/" + asset))
        entry = files["fbx"][args.res]["fbx"]
        payload = {os.path.basename(entry["url"]): get(entry["url"])}
        for path, inc in entry.get("include", {}).items():
            payload[path] = get(inc["url"])
        info = json.loads(get("https://api.polyhaven.com/info/" + asset))
        for project in PROJECTS:
            root = os.path.join(project, "Assets", "Imported", "Remaster", "Vegetation", asset)
            for path, data in payload.items():
                out = os.path.join(root, path); os.makedirs(os.path.dirname(out), exist_ok=True)
                open(out, "wb").write(data)
            json.dump({"source": "https://polyhaven.com/a/" + asset, "licence": "CC0 1.0", "authors": info.get("authors"),
                       "dimensions_mm": info.get("dimensions"), "files": sorted(payload)}, open(os.path.join(root, "source.json"), "w"), indent=1)
            # RGBA albedo for the cut-out foliage material: diffuse + alpha map.
            tex = os.path.join(root, "textures")
            diff = [f for f in os.listdir(tex) if "_diff_" in f]; alpha = [f for f in os.listdir(tex) if "_alpha_" in f]
            if diff:
                from PIL import Image
                image = Image.open(os.path.join(tex, diff[0])).convert("RGB")
                a = Image.open(os.path.join(tex, alpha[0])).convert("L").resize(image.size) if alpha else Image.new("L", image.size, 255)
                image.putalpha(a); image.save(os.path.join(root, asset + "_albedo.png"))
        print(asset, len(payload), "files", sum(len(v) for v in payload.values()) // 1024, "KiB")


if __name__ == "__main__":
    main()
