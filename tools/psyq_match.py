#!/usr/bin/env python3
"""Find PsyQ SDK library objects in the executable using lab313ru/psx_psyq_signatures.

usage: psyq_match.py <exe> <sig_repo> [version]
Without a version, scores every SDK version and reports the best one.
With a version, prints symbol_addrs.txt lines for every matched object.
"""
import json
import os
import re
import sys

LOAD = 0x80010000
HDR = 0x800


def sig_regex(sig):
    parts = []
    for tok in sig.split():
        parts.append(b"." if tok == "??" else re.escape(bytes([int(tok, 16)])))
    return re.compile(b"".join(parts), re.DOTALL)


def load_version(repo, ver):
    objs = []
    vdir = os.path.join(repo, ver)
    for fn in sorted(os.listdir(vdir)):
        if not fn.endswith(".json"):
            continue
        for o in json.load(open(os.path.join(vdir, fn))):
            if len(o.get("sig", "").split()) < 24:
                continue  # too short to be unique
            objs.append((fn[:-5], o))
    return objs


def match(exe, objs):
    found = []
    for lib, o in objs:
        rx = sig_regex(o["sig"])
        hits = [m.start() for m in rx.finditer(exe) if m.start() % 4 == 0 and m.start() >= HDR]
        if len(hits) == 1:
            found.append((hits[0], lib, o))
    return found


def main():
    exe = open(sys.argv[1], "rb").read()
    repo = sys.argv[2]
    if len(sys.argv) < 4:
        for ver in sorted(v for v in os.listdir(repo) if v.isdigit()):
            objs = load_version(repo, ver)
            found = match(exe, objs)
            nbytes = sum(len(o["sig"].split()) for _, _, o in found)
            print(f"{ver}: {len(found)} objects, {nbytes} bytes")
        return
    found = match(exe, load_version(repo, sys.argv[3]))
    seen = set()
    for off, lib, o in sorted(found, key=lambda f: f[0]):
        for lab in o["labels"]:
            name = lab["name"]
            if re.match(r"(loc|text|data|bss|rdata|sdata|sbss)_", name):
                continue
            addr = LOAD + off - HDR + lab["offset"]
            if name in seen:
                continue
            seen.add(name)
            print(f"{name} = 0x{addr:08X}; // type:func lib:{lib}/{o['name']}")


if __name__ == "__main__":
    main()
