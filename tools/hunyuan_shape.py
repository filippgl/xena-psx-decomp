#!/usr/bin/env python3
"""Image -> 3D mesh with Tencent Hunyuan3D-2 (local, GPU) for Remaster props.

Runs inside the Hunyuan3D-2 venv (../_tools/Hunyuan3D-2/venv). Licence: Tencent Hunyuan
Non-Commercial / Community licence (not for use in the EU, UK or South Korea); outputs are
used for this non-commercial fan port only (docs/remaster-assets.md).
Background is removed (rembg) unless the image already has alpha. The mesh is simplified to
--faces and written as OBJ (Unity imports OBJ natively) plus a GLB copy.

usage: <venv>/python tools/hunyuan_shape.py image.png out_dir [--model tencent/Hunyuan3D-2mini] [--faces 20000]
"""
import argparse
import os

import torch
from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("out")
    ap.add_argument("--model", default="tencent/Hunyuan3D-2mini")
    ap.add_argument("--subfolder", default="hunyuan3d-dit-v2-mini")
    ap.add_argument("--faces", type=int, default=20000)
    ap.add_argument("--steps", type=int, default=30)
    ap.add_argument("--seed", type=int, default=1999)
    args = ap.parse_args()
    from hy3dgen.rembg import BackgroundRemover
    from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline, FaceReducer, FloaterRemover, DegenerateFaceRemover
    image = Image.open(args.image)
    if image.mode != "RGBA" or image.getextrema()[3][0] == 255:
        image = BackgroundRemover()(image.convert("RGB"))
    os.makedirs(args.out, exist_ok=True)
    image.save(os.path.join(args.out, "input-rgba.png"))
    pipeline = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained(args.model, subfolder=args.subfolder, use_safetensors=True, device="cuda")
    mesh = pipeline(image=image, num_inference_steps=args.steps, octree_resolution=380, num_chunks=20000,
                    generator=torch.manual_seed(args.seed), output_type="trimesh")[0]
    mesh = FloaterRemover()(mesh); mesh = DegenerateFaceRemover()(mesh); mesh = FaceReducer()(mesh, max_facenum=args.faces)
    mesh.export(os.path.join(args.out, "shape.glb")); mesh.export(os.path.join(args.out, "shape.obj"))
    print("faces", len(mesh.faces), "extents", [round(float(x), 3) for x in mesh.extents])


if __name__ == "__main__":
    main()
