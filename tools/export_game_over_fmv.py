#!/usr/bin/env python3
"""Export the GAME OVER video MEDIA/CHAKDETH.STR (320x240, 15 fps, 5.8 s, XA audio) for Unity.

The game plays it in state 0x13 after "Хочешь выйти?" -> "Да" on the death menu
(native capture build/gameover). Writes chakdeth.webm (Original) and chakdeth-hd.webm
(Remaster, Real-ESRGAN 4x) with tools/export_fmv.py, which documents the pipeline.

usage: export_game_over_fmv.py [--disc "../Xena Warrior Princess.bin"] [--project unity/XenaUnity ...]
                               [--realesrgan ../_tools/realesrgan/realesrgan-ncnn-vulkan.exe]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import export_fmv  # noqa: E402


def main():
    ap = export_fmv.parser()
    ap.set_defaults(work="build/gameover/fmv")
    args = ap.parse_args()
    export_fmv.export("CHAKDETH", args)


if __name__ == "__main__":
    main()
