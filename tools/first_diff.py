#!/usr/bin/env python3
"""Show where a rebuilt EXE first diverges from the original."""
import sys
a = open(sys.argv[1], "rb").read()
b = open(sys.argv[2], "rb").read()
print(f"sizes: 0x{len(a):X} 0x{len(b):X}")
diffs = [i for i in range(min(len(a), len(b))) if a[i] != b[i]]
print(f"{len(diffs)} differing bytes")
for i in diffs[:16]:
    print(f"  file 0x{i:06X} vram 0x{0x80010000 + i - 0x800:08X}: {a[i]:02X} -> {b[i]:02X}")
