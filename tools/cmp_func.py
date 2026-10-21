#!/usr/bin/env python3
"""Compile a C snippet and compare one function against the original asm.

usage: cmp_func.py <func_name> <file.c> [compiler_dir]
Run inside the Linux toolchain (tools/wsl.sh). Relocated operands are masked,
so only instruction encodings (and register/immediate choices) are compared.
"""
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
import configure  # noqa: E402  (reuse the exact build flags)


def target_words(name):
    words = []
    for d in ("asm/nonmatchings/game", "asm/nonmatchings"):
        path = os.path.join(ROOT, d, name + ".s")
        if os.path.exists(path):
            break
    else:
        sys.exit(f"no asm for {name}")
    for line in open(path):
        m = re.search(r"/\* [0-9A-F]+ [0-9A-F]{8} ([0-9A-F]{8}) \*/\s+(\S+)\s*(.*)", line)
        if m:
            raw = int(m.group(1), 16)
            w = int.from_bytes(raw.to_bytes(4, "big"), "little")
            words.append((w, m.group(2) + " " + m.group(3)))
    return words


def mask(w):
    op = w >> 26
    if op in (2, 3):                 # j / jal: target is relocated
        return w & 0xFC000000
    if op in (0x0F,) or op in range(0x08, 0x10) or op >= 0x20:
        return w & 0xFFFF0000        # immediates may be %hi/%lo/%gp_rel
    return w


def compile_func(cfile, compiler):
    out = tempfile.mktemp(suffix=".o")
    env = dict(os.environ, CPP_FLAGS=configure.CPP_FLAGS, CC_FLAGS=configure.CC_FLAGS,
               MASPSX_FLAGS=configure.MASPSX_FLAGS, AS_FLAGS=configure.AS_FLAGS)
    subprocess.run(["bash", "tools/cc.sh", compiler, cfile, out], check=True, cwd=ROOT, env=env)
    return out


def func_words(obj, name):
    syms = subprocess.run(["mips-linux-gnu-nm", obj], capture_output=True, text=True).stdout
    if not re.search(rf"\b{name}$", syms, re.M):
        sys.exit(f"{name} not in compiled object")
    dis = subprocess.run(["mips-linux-gnu-objdump", "-d", "-z", "--no-show-raw-insn",
                          f"--disassemble={name}", obj], capture_output=True, text=True).stdout
    raw = subprocess.run(["mips-linux-gnu-objdump", "-d", "-z", f"--disassemble={name}", obj],
                         capture_output=True, text=True).stdout
    words = []
    for line in raw.splitlines():
        m = re.match(r"\s+[0-9a-f]+:\s+([0-9a-f]{8})\s+(.*)", line)
        if m:
            words.append((int(m.group(1), 16), m.group(2)))
    return words


def main():
    name, cfile = sys.argv[1], sys.argv[2]
    compiler = sys.argv[3] if len(sys.argv) > 3 else configure.COMPILER
    want = target_words(name)
    got = func_words(compile_func(cfile, compiler), name)
    # trailing nops are alignment padding in the compiled object
    while got and got[-1][0] == 0 and len(got) > len(want):
        got.pop()
    ok = len(want) == len(got) and all(mask(a[0]) == mask(b[0]) for a, b in zip(want, got))
    for i in range(max(len(want), len(got))):
        a = want[i] if i < len(want) else (None, "")
        b = got[i] if i < len(got) else (None, "")
        flag = " " if a[0] is not None and b[0] is not None and mask(a[0]) == mask(b[0]) else "*"
        print(f"{flag} {a[1]:<40} | {b[1]}")
    print(f"{name}: {'MATCH' if ok else 'DIFF'} ({len(want)} vs {len(got)} instructions) [{os.path.basename(compiler)}]")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
