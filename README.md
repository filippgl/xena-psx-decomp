# Xena: Warrior Princess (PS1, SLUS-00977) — decompilation

Matching decompilation of the PlayStation game, the first step towards a native PC port.
Goal: C source that rebuilds the original executable byte-for-byte, then a PC build that
replaces the PS1 hardware (GPU/GTE/SPU/CD/pad) with SDL/OpenGL.

No game data is included. You need your own disc image.

## Disc layout

| File | What it is |
|---|---|
| `PSX.EXE` | Boot loader: plays `GOLDLEON.STR`, then `LoadExec("cdrom:\MEDIA\XA2.XA;1")` |
| `MEDIA\XA2.XA` | **The real game executable** (PS-X EXE, despite the name) → `xena.exe` |
| `XENA.UDA` | Archive with all game data (`data\levels\NN\level.tmd`, `.tim`, `.stm`, sounds…) |
| `MEDIA\*.STR`, `XA1.XA` | FMV and streamed audio |

## Executable layout (`xena.exe`, sha1 `f31a850d…3130`)

| VRAM | Contents |
|---|---|
| `80010000–800169AC` | Hand-written GTE assembly (`asm/gte_asm.s`) |
| `800169AC–800181B4` | Game rodata (migrated into `src/game.c`) |
| `800181B4–80019224` | PsyQ rodata |
| `80019224–800889AC` | **Game code** (`src/game.c`, 587 functions) |
| `800889AC–800BFE80` | PsyQ 4.3 libraries (`asm/psyq.s`, names in `symbol_addrs.txt`) |
| `800BFE80–800F09D8` | data / sdata (`$gp = 800EC210`) |
| `800F09D8–80101C98` | bss |

Compiler: PsyQ 4.3, GCC 2.7.2 (`-O2 -G8`), assembled through maspsx.

## Building

Toolchain runs in WSL (Ubuntu). One-time setup:

```sh
bash tools/wsl.sh bash tools/setup_linux.sh   # old-gcc, maspsx, Linux venv
```

Extract the game executable from your disc image (Windows or Linux Python):

```sh
python tools/extract_disc.py "../Xena Warrior Princess.bin" disc
cp disc/files/MEDIA/XA2.XA xena.exe
```

Build and verify:

```sh
bash tools/wsl.sh bash -c "source .venv-linux/bin/activate && python3 configure.py && ninja"
```

`ninja` ends with `build/xena.exe: OK` when the output matches the original.

## Decompiling a function

1. `m2c`: `python3 -m m2c.main --target mipsel-gcc-c asm/nonmatchings/game/func_XXXXXXXX.s`
2. Write the C into a scratch file and iterate with
   `python3 tools/cmp_func.py func_XXXXXXXX scratch.c`
3. Replace its `INCLUDE_ASM(...)` line in `src/game.c`, run `ninja`, keep it only if `OK`.
4. `python3 tools/progress.py`

Notes:
- Uninitialized globals in sbss must be declared *without* `extern` (tentative definition)
  to get `$gp`-relative code; they stay common symbols thanks to `--use-comm-section`.
- `INCLUDE_ASM`/`INCLUDE_RODATA` use the maspsx reorder hack, because cc1 2.7.2 moves
  top-level asm ahead of functions.

## Native PC port (`port/`)

A playable Windows build made by **static recompilation**: `port/recomp/recomp.py`
translates every MIPS function of `xena.exe` into C, and `port/runtime/` replaces the
PS1 hardware (no CPU emulation):

| Part | File |
|---|---|
| GPU (software rasterizer, 1 MB VRAM) | `gpu.c` |
| GTE (geometry coprocessor) | `gte.c` |
| CD-ROM controller on the `.bin` image | `cdrom.c` |
| SPU voices + CD-XA music | `spu.c` |
| MDEC (FMV) | `mdec.c` |
| Controller port (DualShock protocol) | `pad.c` |
| DMA, IRQ, timers, virtual clock | `hw.c` |
| BIOS kernel calls (HLE), memory card on host files | `bios.c` |
| SDL2 window/input/audio | `platform.c` |

Virtual time advances by a fixed cost per loop iteration, so library timeouts behave
as on hardware; output is paced to real time once per frame.

Build (WSL, after the decomp build so `build/xena.exe.elf` exists):

```sh
bash tools/wsl.sh bash -c "source .venv-linux/bin/activate && bash port/build.sh"
bash port/package.sh ../XenaPC     # xena.exe + SDL2.dll + README
```

Testing helpers: `--headless --frames N --shots K DIR --script "frame:BUTTON:len,..."`,
`--wav out.wav`, `--trace`, `--watch addr,...`, `--verbose`.
