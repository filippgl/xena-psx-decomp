#!/usr/bin/env bash
# One-time toolchain setup for building the Xena decomp (run inside Linux/WSL).
set -euo pipefail
cd "$(dirname "$0")/.."

OLD_GCC_VER=0.17
mkdir -p tools/gcc-2.7.2-psx tools/gcc-2.8.1-psx

fetch_gcc() {
    local name=$1
    local dst=tools/$name
    if [ ! -x "$dst/cc1" ]; then
        curl -sSL "https://github.com/decompals/old-gcc/releases/download/$OLD_GCC_VER/$name.tar.gz" | tar xz -C "$dst"
    fi
}
fetch_gcc gcc-2.7.2-psx
fetch_gcc gcc-2.8.1-psx

if [ ! -d tools/maspsx ]; then
    git clone -q https://github.com/mkst/maspsx.git tools/maspsx
fi

# Linux-side venv (the Windows .venv cannot be used from WSL)
if [ ! -d .venv-linux ]; then
    python3 -m venv .venv-linux
fi
.venv-linux/bin/pip install -q -U pip
.venv-linux/bin/pip install -q "splat64[mips]" spimdisasm ninja_syntax pyyaml

ls tools/gcc-2.7.2-psx tools/gcc-2.8.1-psx
