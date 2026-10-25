#!/usr/bin/env python3
"""Install a Hunyuan3D-generated prop (tools/hunyuan_shape.py + hunyuan_paint.py output folder)
into both Unity projects as Assets/Imported/Remaster/Generated/<name>/model.obj + texture.png.
The editor build turns it into Resources/Remaster/Generated/<name>.prefab
(XenaMovementPrototype.CreateGeneratedPrefabs) and PrototypeRemasterProps places it.

usage: install_generated_prop.py build/hy3d/sign signpost
"""
import os
import shutil
import sys

import trimesh

PROJECTS = ["unity/XenaUnity", "unity/XenaLightingVerification"]


def main():
    src, name = sys.argv[1], sys.argv[2]
    mesh = trimesh.load(os.path.join(src, "textured.glb"), force="mesh")
    texture = mesh.visual.material.baseColorTexture if hasattr(mesh.visual.material, "baseColorTexture") else mesh.visual.material.image
    for project in PROJECTS:
        out = os.path.join(project, "Assets", "Imported", "Remaster", "Generated", name)
        shutil.rmtree(out, ignore_errors=True); os.makedirs(out)
        texture.convert("RGB").save(os.path.join(out, "texture.png"))
        # OBJ with UVs (+ the exporter's own .mtl/texture); the editor assigns texture.png.
        mesh.export(os.path.join(out, "model.obj"))
        assert any(line.startswith("vt ") for line in open(os.path.join(out, "model.obj"))), "OBJ lost its UVs"
        print("installed", out, len(mesh.faces), "faces", texture.size)


if __name__ == "__main__":
    main()
