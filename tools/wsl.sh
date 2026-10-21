#!/usr/bin/env bash
# Run a command inside the WSL Ubuntu toolchain from Git Bash, in the repo root.
REPO="$(cd "$(dirname "$0")/.." && pwd -W)"
MSYS_NO_PATHCONV=1 WSL_UTF8=1 wsl.exe -d Ubuntu -u root --cd "$REPO" -- "$@" 2> >(grep -v "прокси\|proxy" >&2)
