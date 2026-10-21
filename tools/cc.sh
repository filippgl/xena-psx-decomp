#!/usr/bin/env bash
# Compile one C file the way PsyQ's ccpsx did: cpp -> cc1 -> maspsx -> as
# usage: cc.sh <compiler_dir> <in.c> <out.o>  (flags come from the environment)
# The host cpp is used: the 32-bit PsyQ cpp fails with EOVERFLOW on WSL /mnt drives.
set -eo pipefail
COMPILER=$1
IN=$2
OUT=$3

cpp $CPP_FLAGS -MMD -MF "$OUT.d" -MT "$OUT" "$IN" \
    | "$COMPILER/cc1" $CC_FLAGS \
    | python3 tools/maspsx/maspsx.py $MASPSX_FLAGS \
    | mips-linux-gnu-as $AS_FLAGS -o "$OUT"
