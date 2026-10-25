#!/usr/bin/env python3
"""Export the GAME OVER video MEDIA/CHAKDETH.STR (320x240, 15 fps, 5.8 s, XA audio) for Unity.

The game plays it in state 0x13 after "Хочешь выйти?" -> "Да" on the death menu
(native capture build/gameover). Decoded with ffmpeg's psxstr demuxer/MDEC decoder in WSL
(tools/fmv_check.py: ffmpeg matches the port's MDEC output at ~50 dB) and re-encoded
as VP8/Vorbis WebM, which Unity imports as a VideoClip.

Remaster: every frame is upscaled 4x (1280x960) with Real-ESRGAN (realesrgan-ncnn-vulkan,
BSD-3, https://github.com/xinntao/Real-ESRGAN release v0.2.5.0, model realesrgan-x4plus; local
GPU, free) and encoded as chakdeth-hd.webm with the original audio. Pass --realesrgan EXE.

usage: export_game_over_fmv.py [--disc "../Xena Warrior Princess.bin"] [--project unity/XenaUnity ...]
                               [--realesrgan ../_tools/realesrgan/realesrgan-ncnn-vulkan.exe]
"""
import argparse
import os
import shutil
import subprocess

LBA, SIZE = 15875, 1785856  # disc/filelist.txt: RAW MEDIA/CHAKDETH.STR


def wsl_path(p):
    p = os.path.abspath(p).replace("\\", "/")
    return "/mnt/" + p[0].lower() + p[2:]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--disc", default="../Xena Warrior Princess.bin")
    ap.add_argument("--work", default="build/gameover/fmv")
    ap.add_argument("--realesrgan", default="../_tools/realesrgan/realesrgan-ncnn-vulkan.exe")
    ap.add_argument("--project", action="append",
                    default=None, help="Unity project(s); default: XenaUnity and XenaLightingVerification")
    args = ap.parse_args()
    projects = args.project or ["unity/XenaUnity", "unity/XenaLightingVerification"]
    os.makedirs(args.work, exist_ok=True)
    raw = os.path.join(args.work, "CHAKDETH.STR")
    with open(args.disc, "rb") as f:
        f.seek(LBA * 2352)
        open(raw, "wb").write(f.read((SIZE + 2047) // 2048 * 2352))
    webm = os.path.join(args.work, "chakdeth.webm")
    env = dict(os.environ, MSYS_NO_PATHCONV="1")
    subprocess.run(["wsl", "ffmpeg", "-loglevel", "error", "-y", "-f", "psxstr", "-i", wsl_path(raw),
                    "-c:v", "libvpx", "-b:v", "4M", "-c:a", "libvorbis", wsl_path(webm)], check=True, env=env)
    outputs = {"chakdeth.webm": webm}
    if os.path.exists(args.realesrgan):
        frames, hd = os.path.join(args.work, "frames"), os.path.join(args.work, "hd")
        for d in (frames, hd):
            shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
        subprocess.run(["wsl", "ffmpeg", "-loglevel", "error", "-y", "-f", "psxstr", "-i", wsl_path(raw),
                        "-fps_mode", "passthrough", wsl_path(frames) + "/%04d.png"], check=True, env=env)
        subprocess.run([args.realesrgan, "-i", frames, "-o", hd, "-n", "realesrgan-x4plus", "-f", "png"],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        hdwebm = os.path.join(args.work, "chakdeth-hd.webm")
        subprocess.run(["wsl", "ffmpeg", "-loglevel", "error", "-y", "-framerate", "15", "-i", wsl_path(hd) + "/%04d.png",
                        "-f", "psxstr", "-i", wsl_path(raw), "-map", "0:v", "-map", "1:a", "-c:v", "libvpx",
                        "-b:v", "12M", "-pix_fmt", "yuv420p", "-c:a", "libvorbis", "-shortest", wsl_path(hdwebm)],
                       check=True, env=env)
        outputs["chakdeth-hd.webm"] = hdwebm
    else:
        print("Real-ESRGAN not found, Remaster video skipped:", args.realesrgan)
    for project in projects:
        out = os.path.join(project, "Assets", "Imported", "Resources", "Video")
        os.makedirs(out, exist_ok=True)
        for name, source in outputs.items():
            data = open(source, "rb").read()
            open(os.path.join(out, name), "wb").write(data)
            print("wrote", os.path.join(out, name), len(data))


if __name__ == "__main__":
    main()
