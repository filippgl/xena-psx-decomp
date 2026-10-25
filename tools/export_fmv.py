#!/usr/bin/env python3
"""Export disc videos (MEDIA/<NAME>.STR, 320x240 MDEC + XA audio) for Unity, in two versions.

Original mode: <name>.webm, decoded with ffmpeg's psxstr demuxer/MDEC decoder in WSL
(tools/fmv_check.py: ffmpeg matches the port's MDEC output at ~50 dB) and re-encoded as
VP8/Vorbis WebM, which Unity imports as a VideoClip.

Remaster mode: <name>-hd.webm, every frame upscaled 4x (1280x960) with Real-ESRGAN
(realesrgan-ncnn-vulkan, BSD-3, https://github.com/xinntao/Real-ESRGAN release v0.2.5.0,
model realesrgan-x4plus; local GPU, free) and encoded with the original audio at the
stream's own frame rate. realesrgan-x4plus only supports its native 4x (`-s 2` gives a
tile mosaic), so no scale option is passed.

The file is found in the disc's ISO directory (tools/iso_ls.py) and read raw: 2352-byte
sectors from its LBA, ceil(size/2048) sectors, as the psxstr demuxer expects.

Which video plays when: docs/xena-fmv.md (boot: EA_LOGO, UNILOGO, INTRO).

usage: export_fmv.py [--disc "../Xena Warrior Princess.bin"] [--project unity/XenaUnity ...]
                     [--realesrgan ../_tools/realesrgan/realesrgan-ncnn-vulkan.exe]
                     [--work build/fmv] [--keep-frames] NAME [NAME ...]
"""
import argparse
import os
import shutil
import struct
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import iso_ls  # noqa: E402

PROJECTS = ["unity/XenaUnity", "unity/XenaLightingVerification"]
ENV = dict(os.environ, MSYS_NO_PATHCONV="1")


def wsl_path(p):
    p = os.path.abspath(p).replace("\\", "/")
    return "/mnt/" + p[0].lower() + p[2:]


def ffmpeg(*args):
    # "fatal": ffmpeg's mdec decoder prints "ac-tex damaged" for many packets of these STRs although
    # every frame decodes (EA_LOGO: 75/75 frames, as many as the file has); "error" floods the log.
    subprocess.run(["wsl", "ffmpeg", "-loglevel", "fatal", "-y", *args], check=True, env=ENV)


def find_media(disc, name):
    """(lba, size) of MEDIA/<name>.STR from the ISO 9660 directory"""
    with open(disc, "rb") as f:
        pvd = iso_ls.sector(f, 16)
        lba, size = struct.unpack_from("<I", pvd, 158)[0], struct.unpack_from("<I", pvd, 166)[0]
        for n, e, s, isdir in iso_ls.entries(f, lba, size):
            if n.upper() == "MEDIA":
                lba, size = e, s
                break
        else:
            sys.exit("MEDIA directory not on the disc")
        for n, e, s, isdir in iso_ls.entries(f, lba, size):
            if n.upper().split(";")[0] == name + ".STR":
                return e, s
    sys.exit(f"MEDIA/{name}.STR not on the disc")


def extract(disc, lba, size, out):
    with open(disc, "rb") as f:
        f.seek(lba * 2352)
        data = f.read((size + 2047) // 2048 * 2352)
    with open(out, "wb") as o:
        o.write(data)


def video_info(raw):
    """(frame rate as ffprobe reports it, e.g. '15/1'; number of video frames) of the STR"""
    r = subprocess.run(["wsl", "ffprobe", "-v", "fatal", "-f", "psxstr", "-select_streams", "v:0", "-count_frames",
                        "-show_entries", "stream=r_frame_rate,nb_read_frames", "-of", "csv=p=0", wsl_path(raw)],
                       check=True, env=ENV, capture_output=True, text=True)
    fps, count = r.stdout.strip().splitlines()[0].split(",")
    return fps, int(count)


def export(name, args):
    lower = name.lower()
    work = os.path.join(args.work, lower)
    os.makedirs(work, exist_ok=True)
    lba, size = find_media(args.disc, name)
    raw = os.path.join(work, name + ".STR")
    extract(args.disc, lba, size, raw)
    fps, count = video_info(raw)
    num, den = (int(x) for x in fps.split("/"))
    length = "%.3f" % (count * den / num)
    print(f"{name}.STR: LBA {lba}, {size} bytes, {count} frames at {fps} fps = {length} s")
    # Both versions are encoded from the decoded frames at the stream's frame rate, with the audio
    # cut to the video length (-t): the game's player stops with the video (docs/xena-fmv.md).
    # Re-encoding the psxstr input directly gave EA_LOGO an 8.9 s clip (with -t/-shortest only
    # 42 of its 75 frames), although its packets are 1/15 s apart and its XA audio is 5.01 s.
    frames, hd = os.path.join(work, "frames"), os.path.join(work, "hd")
    for d in (frames, hd):
        shutil.rmtree(d, ignore_errors=True)
        os.makedirs(d)
    ffmpeg("-f", "psxstr", "-i", wsl_path(raw), "-fps_mode", "passthrough", wsl_path(frames) + "/%04d.png")
    decoded = len(os.listdir(frames))
    if decoded != count:
        sys.exit(f"{name}: decoded {decoded} frames, the stream has {count}")

    def encode(src, out, bitrate):
        ffmpeg("-framerate", fps, "-i", wsl_path(src) + "/%04d.png", "-f", "psxstr", "-i", wsl_path(raw),
               "-map", "0:v", "-map", "1:a", "-c:v", "libvpx", "-b:v", bitrate, "-pix_fmt", "yuv420p",
               "-c:a", "libvorbis", "-t", length, wsl_path(out))

    webm = os.path.join(work, lower + ".webm")
    encode(frames, webm, "4M")
    outputs = {lower + ".webm": webm}
    if os.path.exists(args.realesrgan):
        subprocess.run([args.realesrgan, "-i", frames, "-o", hd, "-n", "realesrgan-x4plus", "-f", "png"],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        hdwebm = os.path.join(work, lower + "-hd.webm")
        encode(hd, hdwebm, "12M")
        outputs[lower + "-hd.webm"] = hdwebm
    else:
        print("Real-ESRGAN not found, Remaster video skipped:", args.realesrgan)
    if not args.keep_frames:
        for d in (frames, hd):
            shutil.rmtree(d, ignore_errors=True)
    for project in args.project or PROJECTS:
        out = os.path.join(project, "Assets", "Imported", "Resources", "Video")
        os.makedirs(out, exist_ok=True)
        for fn, source in outputs.items():
            shutil.copyfile(source, os.path.join(out, fn))
            print("wrote", os.path.join(out, fn), os.path.getsize(source))


def parser():
    ap = argparse.ArgumentParser()
    ap.add_argument("--disc", default="../Xena Warrior Princess.bin")
    ap.add_argument("--work", default="build/fmv")
    ap.add_argument("--realesrgan", default="../_tools/realesrgan/realesrgan-ncnn-vulkan.exe")
    ap.add_argument("--project", action="append", default=None,
                    help="Unity project(s); default: XenaUnity and XenaLightingVerification")
    ap.add_argument("--keep-frames", action="store_true", help="keep the PNG frames (HD: ~3 MB each)")
    return ap


def main():
    ap = parser()
    ap.add_argument("names", nargs="+", help="video names, e.g. EA_LOGO UNILOGO INTRO")
    args = ap.parse_args()
    for name in args.names:
        export(name.upper(), args)


if __name__ == "__main__":
    main()
