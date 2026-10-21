#!/usr/bin/env python3
r"""Look names up in XENA.UDA's index the way the game does (func_800A4B1C): the index
is 0x52A entries of {hash, lba, size, ...} at 0x800D5424 in the game EXE, the hash is
sum(lower(c) * weight[i]) over "\NAME;1" with the weights at 0x800DA6C4.

usage: uda_find.py xena.exe NAME...       e.g. uda_find.py xena.exe DATA\LEVELS\29\ENVIRO.BIN
"""
import struct
import sys

BASE = 0x80010000 - 0x800  # PS-X EXE header is 0x800 bytes


def load(path):
    d = open(path, "rb").read()
    table = {}
    for i in range(0x52A):
        h, lba, size = struct.unpack_from("<iII", d, 0x800D5424 - BASE + i * 16)
        table.setdefault(h, (lba, size))
    weights = struct.unpack_from("<256i", d, 0x800DA6C4 - BASE)
    return table, weights


def name_hash(name, weights):
    s = ("\\" + name.lstrip("\\") + ";1").upper()
    h = 0
    for i, c in enumerate(s.lower()):
        h = (h + ord(c) * weights[i]) & 0xFFFFFFFF
    return h - (1 << 32) if h & 0x80000000 else h


def main():
    table, weights = load(sys.argv[1])
    for n in sys.argv[2:]:
        hit = table.get(name_hash(n, weights))
        print(f"{'lba %6d size %8d' % hit if hit else 'missing':>26}  {n}")


if __name__ == "__main__":
    main()
