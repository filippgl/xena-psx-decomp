#!/usr/bin/env python3
"""Texture a Hunyuan3D shape (tools/hunyuan_shape.py output) with Hunyuan3D-2 Paint from the same
reference image. Runs in the Hunyuan3D-2 venv; needs the custom_rasterizer and
differentiable_renderer extensions (../_tools/Hunyuan3D-2/build_texgen.cmd). Same licence notes
as hunyuan_shape.py. Writes textured.glb and textured.obj (+ material/texture) next to the shape.

usage: <venv>/python tools/hunyuan_paint.py out_dir [--model tencent/Hunyuan3D-2]
"""
import argparse
import os

import trimesh
from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--model", default="tencent/Hunyuan3D-2")
    args = ap.parse_args()
    from hy3dgen.texgen import Hunyuan3DPaintPipeline
    mesh = trimesh.load(os.path.join(args.out, "shape.glb"), force="mesh")
    image = Image.open(os.path.join(args.out, "input-rgba.png"))
    pipeline = Hunyuan3DPaintPipeline.from_pretrained(args.model)
    try:
        pipeline.enable_model_cpu_offload()  # 12 GB GPUs
    except Exception as error:  # older pipeline versions
        print("cpu offload unavailable:", error)
    textured = pipeline(mesh, image=image)
    textured.export(os.path.join(args.out, "textured.glb"))
    textured.export(os.path.join(args.out, "textured.obj"))
    print("textured", len(textured.faces), "faces")


if __name__ == "__main__":
    main()
