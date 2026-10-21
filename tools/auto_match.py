#!/usr/bin/env python3
"""Try m2c's output as-is for small functions and keep what matches.

usage: auto_match.py [--max-size 64] [--limit N]     (run inside tools/wsl.sh)

For each INCLUDE_ASM function in src/game.c up to --max-size bytes: decompile with m2c,
compile with the project's flags (tools/cmp_func.py) and, when the instructions match,
put the C in place of the INCLUDE_ASM. The whole EXE is then rebuilt and must hash to the
original (xena.exe.sha1); functions are added one batch at a time and a batch that breaks
the hash is bisected, so only C that reproduces the original bytes is kept.
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src", "game.c")
ASM = os.path.join(ROOT, "asm", "nonmatchings", "game")


def todo(max_size):
    out = []
    for f in re.findall(r'INCLUDE_ASM\("asm/nonmatchings/game", (\w+)\);', open(SRC).read()):
        m = re.search(rf"nonmatching {f}, 0x([0-9A-F]+)", open(os.path.join(ASM, f + ".s")).read())
        if m and int(m.group(1), 16) <= max_size:
            out.append((int(m.group(1), 16), f))
    return [f for _, f in sorted(out)]


def m2c(f):
    p = subprocess.run(["m2c", "--target", "mips-gcc-c", os.path.join(ASM, f + ".s")],
                       capture_output=True, text=True, cwd=ROOT)
    text = p.stdout.strip()
    if p.returncode or "?" in text or "M2C_ERROR" in text or "->unk" in text or "sp" in re.findall(r"\b\w+\b", text):
        return None
    # gp-relative access needs the size: a tentative definition (.comm, discarded by the
    # linker script; the data's real definition is in asm/data) instead of extern
    return re.sub(r"^extern ([\w ]+?\**) (D_[0-9A-F]{8});$", r"\1 \2;", text, flags=re.M)


def candidate_ok(f, code):
    with tempfile.NamedTemporaryFile("w", suffix=".c", dir=os.path.join(ROOT, "build"), delete=False) as t:
        t.write('#include "common.h"\n\n' + code + "\n")
        path = t.name
    try:
        p = subprocess.run([sys.executable, "-P", "tools/cmp_func.py", f, path], capture_output=True, text=True, cwd=ROOT)
        return p.returncode == 0
    finally:
        os.unlink(path)


def apply(codes):
    src = open(SRC).read()
    for f, code in codes.items():
        src = src.replace(f'INCLUDE_ASM("asm/nonmatchings/game", {f});', code, 1)
    open(SRC, "w").write(src)


def build_matches():
    if subprocess.run(["ninja"], capture_output=True, cwd=ROOT).returncode:
        return False
    return subprocess.run(["sha1sum", "-c", "xena.exe.sha1"], capture_output=True, cwd=ROOT).returncode == 0


def keep_matching(codes, original):
    """all at once if that reproduces the EXE, else one at a time"""
    apply(codes)
    if build_matches():
        return dict(codes)
    open(SRC, "w").write(original)
    kept = {}
    for f, code in codes.items():
        before = open(SRC).read()
        apply({f: code})
        if build_matches():
            kept[f] = code
        else:
            print(f"  rejected by the full build: {f}", flush=True)
            open(SRC, "w").write(before)
    return kept


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--max-size", type=lambda s: int(s, 0), default=64)
    ap.add_argument("--limit", type=int, default=1000)
    args = ap.parse_args()
    if not build_matches():
        sys.exit("the build does not match before starting")
    codes = {}
    for f in todo(args.max_size)[:args.limit]:
        code = m2c(f)
        if code and candidate_ok(f, code):
            codes[f] = code
            print(f"candidate {f}", flush=True)
    original = open(SRC).read()
    kept = keep_matching(codes, original) if codes else {}
    if not build_matches():
        open(SRC, "w").write(original)
        build_matches()
        sys.exit("final build does not match; restored src/game.c")
    print(f"kept {len(kept)} of {len(codes)} candidates")


if __name__ == "__main__":
    main()
