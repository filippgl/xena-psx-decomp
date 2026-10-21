#!/usr/bin/env python3
"""Extract the ISO9660 filesystem from a raw MODE2/2352 PS1 disc image."""
import os
import struct
import sys

SECTOR = 2352


class Disc:
    def __init__(self, path):
        self.f = open(path, "rb")
        self.f.seek(0, 2)
        self.num_sectors = self.f.tell() // SECTOR

    def raw(self, lba):
        self.f.seek(lba * SECTOR)
        return self.f.read(SECTOR)

    def read(self, lba, size):
        """Read `size` bytes of Form 1 user data starting at `lba`."""
        out = bytearray()
        while len(out) < size:
            sec = self.raw(lba)
            submode = sec[18]
            if submode & 0x20:  # Form 2 (XA audio / STR video): 2324 bytes
                out += sec[24:24 + 2324]
            else:
                out += sec[24:24 + 2048]
            lba += 1
        return bytes(out[:size])

    def read_raw_form2(self, lba, count):
        """Raw sectors, used for XA/STR files that mix forms."""
        return b"".join(self.raw(lba + i) for i in range(count))


def parse_dir(disc, lba, size, prefix, entries):
    data = disc.read(lba, size)
    pos = 0
    while pos < len(data):
        rec_len = data[pos]
        if rec_len == 0:
            pos = (pos // 2048 + 1) * 2048
            continue
        rec = data[pos:pos + rec_len]
        ext_lba = struct.unpack_from("<I", rec, 2)[0]
        ext_size = struct.unpack_from("<I", rec, 10)[0]
        flags = rec[25]
        name_len = rec[32]
        name = rec[33:33 + name_len]
        pos += rec_len
        if name in (b"\x00", b"\x01"):
            continue
        name = name.decode("ascii", "replace").split(";")[0]
        # XA system use area: attributes tell us if the file is Form 2 / interleaved
        su_off = 33 + name_len + (1 - name_len % 2)
        xa_attr = 0
        if su_off + 14 <= rec_len and rec[su_off + 6:su_off + 8] == b"XA":
            xa_attr = struct.unpack_from(">H", rec, su_off + 4)[0]
        path = prefix + "/" + name if prefix else name
        if flags & 2:
            parse_dir(disc, ext_lba, ext_size, path, entries)
        else:
            entries.append((path, ext_lba, ext_size, xa_attr))


def main():
    if len(sys.argv) != 3:
        print(f"usage: {sys.argv[0]} <image.bin> <out_dir>")
        sys.exit(1)
    disc = Disc(sys.argv[1])
    out_dir = sys.argv[2]
    pvd = disc.read(16, 2048)
    assert pvd[1:6] == b"CD001", "no ISO9660 PVD"
    root = pvd[156:156 + 34]
    root_lba = struct.unpack_from("<I", root, 2)[0]
    root_size = struct.unpack_from("<I", root, 10)[0]

    entries = []
    parse_dir(disc, root_lba, root_size, "", entries)
    entries.sort(key=lambda e: e[1])

    listing = []
    for path, lba, size, xa in entries:
        dst = os.path.join(out_dir, "files", path)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        interleaved = bool(xa & 0x2000) or bool(xa & 0x4000)
        if interleaved:
            # Keep raw 2352-byte sectors so XA/STR can be decoded later.
            count = (size + 2047) // 2048
            data = disc.read_raw_form2(lba, count)
        else:
            data = disc.read(lba, size)
        with open(dst, "wb") as f:
            f.write(data)
        listing.append(f"{lba:7d} {size:10d} {xa:04X} {'RAW ' if interleaved else ''}{path}")

    # Save system area (license + logo), sectors 0-15
    with open(os.path.join(out_dir, "system_area.bin"), "wb") as f:
        f.write(disc.read(0, 16 * 2048))
    with open(os.path.join(out_dir, "filelist.txt"), "w") as f:
        f.write("\n".join(listing) + "\n")
    print("\n".join(listing))
    print(f"{len(entries)} files, disc has {disc.num_sectors} sectors")


if __name__ == "__main__":
    main()
