#!/usr/bin/env python3
"""Convert 24-bit BMP screenshots to PNG (no dependencies). usage: bmp2png.py file.bmp... """
import struct
import sys
import zlib


def convert(path):
    d = open(path, "rb").read()
    off, = struct.unpack_from("<I", d, 10)
    w, h = struct.unpack_from("<ii", d, 18)
    row = (w * 3 + 3) & ~3
    raw = bytearray()
    for y in range(h):
        src = d[off + (h - 1 - y) * row: off + (h - 1 - y) * row + w * 3]
        raw.append(0)
        for x in range(w):
            b, g, r = src[x * 3: x * 3 + 3]
            raw += bytes((r, g, b))

    def chunk(t, data):
        c = struct.pack(">I", len(data)) + t + data
        return c + struct.pack(">I", zlib.crc32(t + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b"")
    open(path[:-4] + ".png", "wb").write(png)


for p in sys.argv[1:]:
    convert(p)
