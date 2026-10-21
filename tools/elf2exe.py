#!/usr/bin/env python3
"""Convert the linked ELF into a PS-X EXE laid out exactly like the original."""
import subprocess
import sys

EXE_SIZE = 0xE1800  # original file size: data is padded to a 2 KB boundary


def main():
    elf, out = sys.argv[1], sys.argv[2]
    tmp = out + ".bin"
    subprocess.run(["mips-linux-gnu-objcopy", "-O", "binary",
                    "--remove-section=*bss*", elf, tmp], check=True)
    data = open(tmp, "rb").read()
    if len(data) > EXE_SIZE:
        sys.exit(f"output too large: 0x{len(data):X} > 0x{EXE_SIZE:X}")
    data += b"\0" * (EXE_SIZE - len(data))
    with open(out, "wb") as f:
        f.write(data)


if __name__ == "__main__":
    main()
