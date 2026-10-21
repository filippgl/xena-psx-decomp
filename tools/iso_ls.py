#!/usr/bin/env python3
"""List a directory of the game disc (raw 2352-byte MODE2 sectors, ISO9660).

usage: iso_ls.py DISC.bin [PATH]      e.g. iso_ls.py ../Xena.bin DATA/LEVELS/29
"""
import struct
import sys


def sector(f, lba):
    f.seek(lba * 2352 + 24)
    return f.read(2048)


def entries(f, lba, size):
    out = []
    for s in range((size + 2047) // 2048):
        d = sector(f, lba + s)
        i = 0
        while i < 2048 and d[i]:
            n = d[i]
            ext, sz = struct.unpack_from("<I", d, i + 2)[0], struct.unpack_from("<I", d, i + 10)[0]
            flags, nl = d[i + 25], d[i + 32]
            name = d[i + 33:i + 33 + nl].decode("latin-1")
            if name not in ("\x00", "\x01"):
                out.append((name, ext, sz, bool(flags & 2)))
            i += n
    return out


def main():
    f = open(sys.argv[1], "rb")
    pvd = sector(f, 16)
    root = pvd[156:156 + 34]
    lba, size = struct.unpack_from("<I", root, 2)[0], struct.unpack_from("<I", root, 10)[0]
    path = sys.argv[2].strip("/").split("/") if len(sys.argv) > 2 and sys.argv[2].strip("/") else []
    for part in path:
        for name, e, s, isdir in entries(f, lba, size):
            if isdir and name.upper() == part.upper():
                lba, size = e, s
                break
        else:
            sys.exit(f"{part}: not found")
    for name, e, s, isdir in entries(f, lba, size):
        print(f"{'<DIR>' if isdir else s:>10}  lba {e:6d}  {name}")


if __name__ == "__main__":
    main()
