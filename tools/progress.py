#!/usr/bin/env python3
"""Report how much of the game code is decompiled to matching C."""
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASM_DIR = os.path.join(ROOT, "asm", "nonmatchings", "game")


def func_sizes():
    sizes = {}
    for fn in os.listdir(ASM_DIR):
        if fn.startswith("func_") or fn == "main.s":
            text = open(os.path.join(ASM_DIR, fn)).read()
            name = fn[:-2]
            m = re.search(rf"^nonmatching ({name}), (0x[0-9A-F]+)", text, re.M)
            if m:
                sizes[m.group(1)] = int(m.group(2), 16)
    return sizes


def main():
    sizes = func_sizes()
    src = open(os.path.join(ROOT, "src", "game.c")).read()
    pending = set(re.findall(r'INCLUDE_ASM\("[^"]+", (\w+)\)', src))
    done = {f: s for f, s in sizes.items() if f not in pending}
    total = sum(sizes.values())
    done_bytes = sum(done.values())
    print(f"game code: {len(done)}/{len(sizes)} functions, "
          f"{done_bytes}/{total} bytes ({100 * done_bytes / total:.2f}%)")


if __name__ == "__main__":
    main()
