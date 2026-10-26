#!/usr/bin/env python3
"""Download a CC0 Poly Haven texture map (https://polyhaven.com, licence CC0 1.0) into each Unity
project's Assets/Imported/Resources/Remaster/Vegetation/<Name>.jpg with a <Name>.source.json
(local data, not committed).

usage: fetch_polyhaven_texture.py asset Name [--map Diffuse] [--res 1k]
e.g.   fetch_polyhaven_texture.py forrest_ground_01 ForestGround"""
import argparse, json, os, urllib.request

PROJECTS = ["unity/XenaUnity", "unity/XenaLightingVerification"]
HEADERS = {"User-Agent": "xena-psx-decomp (remaster)"}

def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS)) as r:
        return r.read()

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("asset"); ap.add_argument("name")
    ap.add_argument("--map", default="Diffuse"); ap.add_argument("--res", default="1k"); a = ap.parse_args()
    files = json.loads(get("https://api.polyhaven.com/files/" + a.asset))
    data = get(files[a.map][a.res]["jpg"]["url"])
    for project in PROJECTS:
        d = os.path.join(project, "Assets", "Imported", "Resources", "Remaster", "Vegetation"); os.makedirs(d, exist_ok=True)
        open(os.path.join(d, a.name + ".jpg"), "wb").write(data)
        json.dump({"source": "https://polyhaven.com/a/" + a.asset, "map": a.map, "licence": "CC0 1.0"}, open(os.path.join(d, a.name + ".source.json"), "w"))
    print(a.asset, a.map, a.res, len(data), "bytes")

if __name__ == "__main__":
    main()
