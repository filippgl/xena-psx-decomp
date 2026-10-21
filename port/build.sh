#!/usr/bin/env bash
# Build the native Windows port (run inside WSL/Linux from the repo root).
#   needs: xena.exe (game executable extracted from your disc), the matching
#   decomp build (build/xena.exe.elf) for function boundaries, mingw-w64, SDL2.
set -euo pipefail
cd "$(dirname "$0")/.."
OUT=build/port
SDL=port/third_party/SDL2-2.30.9/x86_64-w64-mingw32
CC=${CC:-x86_64-w64-mingw32-gcc}
CFLAGS="-O2 -fno-strict-aliasing -fwrapv -w -Iport/runtime -I$OUT/gen -I$SDL/include/SDL2"
mkdir -p $OUT/obj

[ -f build/xena.exe.elf ] || { echo "build the decomp first (ninja)"; exit 1; }
mips-linux-gnu-readelf -sW build/xena.exe.elf > $OUT/syms.txt
python3 port/recomp/recomp.py xena.exe $OUT/syms.txt port/recomp/hle.txt $OUT/gen

compile() {
    local src=$1 obj=$OUT/obj/$(basename "${1%.c}").o
    if [ ! -f "$obj" ] || [ "$src" -nt "$obj" ] || [ port/runtime/recomp.h -nt "$obj" ] || [ port/runtime/hw.h -nt "$obj" ]; then
        $CC $CFLAGS -c "$src" -o "$obj"
    fi
}
export -f compile
export CC CFLAGS OUT
ls $OUT/gen/*.c port/runtime/*.c | xargs -P "$(nproc)" -I{} bash -c 'compile {}'

# xena.exe: windowed game (logs to xena.log); xena-console.exe: same with a console for testing
$CC -o $OUT/xena.exe $OUT/obj/*.o -L$SDL/lib -lmingw32 -lSDL2main -lSDL2 -mwindows -static-libgcc
$CC -o $OUT/xena-console.exe $OUT/obj/*.o -L$SDL/lib -lmingw32 -lSDL2main -lSDL2 -mconsole -static-libgcc
cp $SDL/bin/SDL2.dll $OUT/
echo "built $OUT/xena.exe"
