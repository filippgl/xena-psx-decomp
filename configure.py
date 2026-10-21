#!/usr/bin/env python3
"""Generate build.ninja for the Xena: Warrior Princess (SLUS-00977) decompilation."""
import argparse
import os
import subprocess
import sys
from pathlib import Path

import ninja_syntax

ROOT = Path(__file__).parent.resolve()
YAML = "config/xena.yaml"
TARGET = "xena.exe"
BUILD = "build"
LD_SCRIPT = "xena.exe.ld"
ELF = f"{BUILD}/xena.exe.elf"
EXE = f"{BUILD}/xena.exe"

CROSS = "mips-linux-gnu-"
PYTHON = "python3"  # run from the activated venv; repo path contains spaces

COMPILER = "tools/gcc-2.7.2-psx"
INCLUDES = "-Iinclude -Isrc"
CPP_FLAGS = f"{INCLUDES} -undef -Wall -nostdinc -ffreestanding " \
            "-D__GNUC__=2 -D__OPTIMIZE__ -Dmips -D__mips__ -D__mips -Dpsx -D__psx__ " \
            "-D__psx -D_PSYQ -D__EXTENSIONS__ -D_MIPSEL -D_LANGUAGE_C -DLANGUAGE_C -DNON_MATCHING=0"
CC_FLAGS = "-O2 -G8 -mips1 -mcpu=3000 -quiet -fgnu-linker -mgas -msoft-float -g0"
MASPSX_FLAGS = "--aspsx-version=2.79 --expand-div --use-comm-section -G8"
AS_FLAGS = "-Iinclude -march=r3000 -mtune=r3000 -no-pad-sections -EL -G0 -O1"
LD_FLAGS = f"-EL -nostdlib --no-check-sections -T {LD_SCRIPT} " \
           "-T undefined_syms_auto.txt -T undefined_funcs_auto.txt " \
           f"-Map {BUILD}/xena.map"


def split():
    subprocess.run([sys.executable, "-m", "splat", "split", YAML], check=True, cwd=ROOT)


def collect_objects():
    """Object list in link order, taken from the splat linker script."""
    objs = []
    for line in (ROOT / LD_SCRIPT).read_text().splitlines():
        line = line.strip()
        if line.startswith(f"{BUILD}/") and "(" in line:
            o = line.split("(")[0]
            if o not in objs:
                objs.append(o)
    return objs


def source_for(obj):
    """build/asm/foo.s.o -> asm/foo.s ; build/src/foo.c.o -> src/foo.c"""
    path = obj[len(BUILD) + 1:]
    assert path.endswith(".o")
    path = path[:-2]
    if not path.endswith((".s", ".c")):
        path += ".c" if (ROOT / (path + ".c")).exists() else ".s"
    return path


def write_ninja(objs):
    with open(ROOT / "build.ninja", "w") as f:
        n = ninja_syntax.Writer(f, width=120)
        n.variable("cross", CROSS)
        n.variable("python", PYTHON)
        n.variable("compiler", COMPILER)
        n.newline()

        n.rule("as", f"${{cross}}as {AS_FLAGS} -o $out $in", description="as $in")
        n.rule("cc",
               f"CPP_FLAGS='{CPP_FLAGS}' CC_FLAGS='{CC_FLAGS}' MASPSX_FLAGS='{MASPSX_FLAGS}' "
               f"AS_FLAGS='{AS_FLAGS}' bash tools/cc.sh $compiler $in $out",
               description="cc $in", depfile="$out.d", deps="gcc")
        n.rule("ld", f"${{cross}}ld {LD_FLAGS} -o $out", description="ld $out")
        n.rule("elf2exe", "$python tools/elf2exe.py $in $out", description="exe $out")
        n.rule("check", f"sha1sum -c {TARGET}.sha1 && touch $out", description="check")
        n.newline()

        for o in objs:
            src = source_for(o)
            if src.endswith(".c"):
                n.build(o, "cc", src)
            else:
                n.build(o, "as", src)
        n.newline()
        n.build(ELF, "ld", objs, implicit=[LD_SCRIPT])
        n.build(EXE, "elf2exe", ELF)
        n.build(f"{BUILD}/ok", "check", EXE)
        n.default(f"{BUILD}/ok")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-split", action="store_true", help="skip running splat")
    args = ap.parse_args()
    if not args.no_split:
        split()
    objs = collect_objects()
    write_ninja(objs)
    print(f"build.ninja written ({len(objs)} objects)")


if __name__ == "__main__":
    main()
