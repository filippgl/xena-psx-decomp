#!/usr/bin/env python3
"""Static recompiler: translate the PS1 executable's MIPS R3000 code into C.

usage: recomp.py <xena.exe> <syms.txt (readelf -sW of the splat ELF)> <hle.txt> <out_dir>

Every function region (from the splat ELF symbol table) becomes one C function per
entry point. CPU state lives in the global `cpu` (see runtime/recomp.h); memory goes
through the runtime's memory map, so the generated code is a faithful but native
translation of the original machine code.
"""
import os
import re
import struct
import sys
from collections import defaultdict

LOAD = 0x80010000
HDR = 0x800
TEXT_START = 0x80010000
TEXT_END = 0x800BFE80
FUNCS_PER_FILE = 120


class Exe:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        self.end = LOAD + len(self.data) - HDR

    def word(self, va):
        return struct.unpack_from("<I", self.data, va - LOAD + HDR)[0]

    def contains(self, va):
        return LOAD <= va < self.end


def sx16(v):
    return v - 0x10000 if v & 0x8000 else v


def hexs(v):
    """C literal for a signed 16-bit immediate."""
    return f"-0x{-v:X}" if v < 0 else f"0x{v:X}"


def load_symbols(path):
    regions = {}
    extra = set()
    names = {}
    for line in open(path):
        p = line.split()
        if len(p) < 8 or p[3] != "FUNC":
            continue
        addr, size, sec, name = int(p[1], 16), int(p[2]), p[6], p[7]
        if name.endswith(".NON_MATCHING"):
            continue
        if not (TEXT_START <= addr < TEXT_END):
            continue
        if sec == "ABS" or size == 0:
            extra.add(addr)
            continue
        if addr not in regions or size > regions[addr][0]:
            regions[addr] = (size, name)
        names[addr] = name
    return regions, extra, names


class Insn:
    __slots__ = ("va", "w", "op", "rs", "rt", "rd", "sa", "fn", "imm", "simm", "target")

    def __init__(self, va, w):
        self.va = va
        self.w = w
        self.op = w >> 26
        self.rs = (w >> 21) & 31
        self.rt = (w >> 16) & 31
        self.rd = (w >> 11) & 31
        self.sa = (w >> 6) & 31
        self.fn = w & 63
        self.imm = w & 0xFFFF
        self.simm = sx16(self.imm)
        self.target = ((va + 4) & 0xF0000000) | ((w & 0x3FFFFFF) << 2)

    # control-flow classification
    def is_branch(self):
        return self.op in (4, 5, 6, 7) or (self.op == 1 and self.rt in (0, 1, 0x10, 0x11))

    def branch_target(self):
        return (self.va + 4 + (self.simm << 2)) & 0xFFFFFFFF

    def is_j(self):
        return self.op == 2

    def is_jal(self):
        return self.op == 3

    def is_jr(self):
        return self.op == 0 and self.fn == 8

    def is_jalr(self):
        return self.op == 0 and self.fn == 9

    def has_delay(self):
        return self.is_branch() or self.op in (2, 3) or self.is_jr() or self.is_jalr()


def R(n):
    return "0" if n == 0 else f"R{n}"


def W(n, expr):
    """Assignment to GPR n (writes to $zero are dropped)."""
    if n == 0:
        return f"(void)({expr});"
    return f"R{n} = {expr};"


def addr_expr(i):
    base = R(i.rs)
    if i.simm == 0:
        return f"{base}"
    return f"{base} + {hexs(i.simm)}"


def translate(i, ctx):
    """C statement(s) for a non-control-flow instruction."""
    op, rs, rt, rd, sa, fn = i.op, i.rs, i.rt, i.rd, i.sa, i.fn
    if i.w == 0:
        return ""  # nop
    if op == 0:
        if fn == 0x00: return W(rd, f"{R(rt)} << {sa}")
        if fn == 0x02: return W(rd, f"{R(rt)} >> {sa}")
        if fn == 0x03: return W(rd, f"(u32)((s32){R(rt)} >> {sa})")
        if fn == 0x04: return W(rd, f"{R(rt)} << ({R(rs)} & 31)")
        if fn == 0x06: return W(rd, f"{R(rt)} >> ({R(rs)} & 31)")
        if fn == 0x07: return W(rd, f"(u32)((s32){R(rt)} >> ({R(rs)} & 31))")
        if fn == 0x0C: return f"bios_syscall(0x{i.va:08X});"
        if fn == 0x0D: return f"cpu_break(0x{i.va:08X}, {(i.w >> 16) & 0x3FF});"
        if fn == 0x10: return W(rd, "HI")
        if fn == 0x11: return f"HI = {R(rs)};"
        if fn == 0x12: return W(rd, "LO")
        if fn == 0x13: return f"LO = {R(rs)};"
        if fn == 0x18: return f"{{ s64 p_ = (s64)(s32){R(rs)} * (s32){R(rt)}; LO = (u32)p_; HI = (u32)((u64)p_ >> 32); }}"
        if fn == 0x19: return f"{{ u64 p_ = (u64){R(rs)} * {R(rt)}; LO = (u32)p_; HI = (u32)(p_ >> 32); }}"
        if fn == 0x1A: return f"cpu_div({R(rs)}, {R(rt)});"
        if fn == 0x1B: return f"cpu_divu({R(rs)}, {R(rt)});"
        if fn in (0x20, 0x21): return W(rd, f"{R(rs)} + {R(rt)}")
        if fn in (0x22, 0x23): return W(rd, f"{R(rs)} - {R(rt)}")
        if fn == 0x24: return W(rd, f"{R(rs)} & {R(rt)}")
        if fn == 0x25: return W(rd, f"{R(rs)} | {R(rt)}")
        if fn == 0x26: return W(rd, f"{R(rs)} ^ {R(rt)}")
        if fn == 0x27: return W(rd, f"~({R(rs)} | {R(rt)})")
        if fn == 0x2A: return W(rd, f"(s32){R(rs)} < (s32){R(rt)}")
        if fn == 0x2B: return W(rd, f"{R(rs)} < {R(rt)}")
    if op in (8, 9): return W(rt, f"{R(rs)} + {hexs(i.simm)}")
    if op == 10: return W(rt, f"(s32){R(rs)} < {i.simm}")
    if op == 11: return W(rt, f"{R(rs)} < 0x{i.simm & 0xFFFFFFFF:X}u")
    if op == 12: return W(rt, f"{R(rs)} & 0x{i.imm:X}")
    if op == 13: return W(rt, f"{R(rs)} | 0x{i.imm:X}")
    if op == 14: return W(rt, f"{R(rs)} ^ 0x{i.imm:X}")
    if op == 15: return W(rt, f"0x{i.imm << 16:X}u")
    if op == 0x10:  # COP0
        if rs == 0: return W(rt, f"cop0_read({rd})")
        if rs == 4: return f"cop0_write({rd}, {R(rt)});"
        if rs == 0x10 and fn == 0x10: return "cop0_rfe();"
    if op == 0x12:  # COP2 (GTE)
        if rs & 0x10: return f"gte_command(0x{i.w & 0x1FFFFFF:X});"
        if rs == 0: return W(rt, f"gte_read_data({rd})")
        if rs == 2: return W(rt, f"gte_read_ctrl({rd})")
        if rs == 4: return f"gte_write_data({rd}, {R(rt)});"
        if rs == 6: return f"gte_write_ctrl({rd}, {R(rt)});"
    a = addr_expr(i)
    if op == 0x20: return W(rt, f"(u32)(s32)(s8)mem_r8({a})")
    if op == 0x21: return W(rt, f"(u32)(s32)(s16)mem_r16({a})")
    if op == 0x22: return W(rt, f"mem_lwl({a}, {R(rt)})")
    if op == 0x23: return W(rt, f"mem_r32({a})")
    if op == 0x24: return W(rt, f"mem_r8({a})")
    if op == 0x25: return W(rt, f"mem_r16({a})")
    if op == 0x26: return W(rt, f"mem_lwr({a}, {R(rt)})")
    if op == 0x28: return f"mem_w8({a}, {R(rt)});"
    if op == 0x29: return f"mem_w16({a}, {R(rt)});"
    if op == 0x2A: return f"mem_swl({a}, {R(rt)});"
    if op == 0x2B: return f"mem_w32({a}, {R(rt)});"
    if op == 0x2E: return f"mem_swr({a}, {R(rt)});"
    if op == 0x32: return f"gte_write_data({rt}, mem_r32({a}));"
    if op == 0x3A and rt in (12, 13, 14): return f"gte_store_sxy({a}, {rt});"
    if op == 0x3A: return f"mem_w32({a}, gte_read_data({rt}));"
    ctx.warn(f"unknown instruction {i.w:08X} at {i.va:08X}")
    return f"cpu_bad_insn(0x{i.va:08X}, 0x{i.w:08X});"


def branch_cond(i):
    rs, rt = R(i.rs), R(i.rt)
    if i.op == 4: return f"{rs} == {rt}"
    if i.op == 5: return f"{rs} != {rt}"
    if i.op == 6: return f"(s32){rs} <= 0"
    if i.op == 7: return f"(s32){rs} > 0"
    if i.op == 1:
        if i.rt in (0, 0x10): return f"(s32){rs} < 0"
        if i.rt in (1, 0x11): return f"(s32){rs} >= 0"
    raise ValueError(hex(i.w))


class Func:
    def __init__(self, start, size, name):
        self.start = start
        self.end = start + size
        self.name = name
        self.entries = {start}

    def contains(self, va):
        return self.start <= va < self.end


class Recompiler:
    def __init__(self, exe, regions, extra, names, hle):
        self.exe = exe
        self.names = names
        self.hle = hle
        self.warnings = []
        # "@probe ADDR fn": call a native function on entry, or before the instruction at
        # ADDR inside a function (the original code still runs)
        self.probes = {}
        for item in hle.get("@probe", "").split(","):
            if item:
                addr, fn = item.split(":")
                self.probes[int(addr, 16)] = fn
        self.funcs = sorted((Func(a, s, n) for a, (s, n) in regions.items()), key=lambda f: f.start)
        self._fix_overlaps()
        self.starts = [f.start for f in self.funcs]
        self.insns = {}
        for f in self.funcs:
            for va in range(f.start, f.end, 4):
                self.insns[va] = Insn(va, exe.word(va))
        self.entry_points = set()
        for f in self.funcs:
            self.entry_points.add(f.start)
        for va in extra:
            self.add_entry(va)
        for i in list(self.insns.values()):
            if i.is_jal():
                self.add_entry(i.target)
            elif i.is_branch() or i.is_j():
                # branches leaving their function (hand-written asm) become tail calls
                t = i.branch_target() if i.is_branch() else i.target
                f = self.func_at(i.va)
                if f is not None and not f.contains(t):
                    self.add_entry(t)
        self.add_pointer_entries()
        # setjmp-style functions: their callers are resumed right after the call
        for target in hle.get("@return_entries", "").split(","):
            if not target:
                continue
            t = int(target, 16)
            for i in list(self.insns.values()):
                if i.is_jal() and i.target == t:
                    self.add_entry(i.va + 8)

    def add_pointer_entries(self):
        """Code addresses taken as values (function pointers, interrupt hooks, saved
        return addresses) must be callable: lui/addiu pairs in code, words in data."""
        table_words = set()
        for f in self.funcs:
            for va in range(f.start, f.end, 4):
                i = self.insns[va]
                if i.is_jr() and i.rs != 31:
                    t = self.jumptable_location(f, i)
                    if t:
                        table_words.update(t)
        cands = set()
        his = {}
        for va in sorted(self.insns):
            i = self.insns[va]
            if i.op == 15:
                his[i.rt] = (i.imm << 16, va)
            elif i.op in (9, 13) and i.rs in his and va - his[i.rs][1] <= 64:
                hi = his[i.rs][0]
                cands.add((hi + (i.simm if i.op == 9 else i.imm)) & 0xFFFFFFFF)
            if i.has_delay() and not i.is_jal():
                his.clear()
        for va in range(LOAD, self.exe.end, 4):
            if va in self.insns or va in table_words:
                continue
            cands.add(self.exe.word(va))
        for c in cands:
            if TEXT_START <= c < TEXT_END and not c & 3 and c not in self.entry_points:
                f = self.func_at(c)
                if f is not None and c != f.start:
                    f.entries.add(c)
                    self.entry_points.add(c)

    def jumptable_location(self, f, i):
        """Addresses of the words of the jumptable used by `jr` (see jumptable())."""
        reg = i.rs
        hi = lo = base = None
        va = i.va - 4
        while va >= max(f.start, i.va - 64):
            p = self.insns[va]
            if lo is None and p.op == 0x23 and p.rt == reg:
                lo = p.simm
                base = p.rs
            elif lo is not None and p.op == 15 and p.rt == base:
                hi = p.imm << 16
                break
            va -= 4
        if hi is None:
            return None
        table = (hi + lo) & 0xFFFFFFFF
        words = []
        while self.exe.contains(table) and f.contains(self.exe.word(table)):
            words.append(table)
            table += 4
        return words

    def warn(self, msg):
        self.warnings.append(msg)

    def _fix_overlaps(self):
        for a, b in zip(self.funcs, self.funcs[1:]):
            if a.end > b.start:
                a.end = b.start

    def func_at(self, va):
        lo, hi = 0, len(self.funcs) - 1
        while lo <= hi:
            mid = (lo + hi) // 2
            f = self.funcs[mid]
            if va < f.start:
                hi = mid - 1
            elif va >= f.end:
                lo = mid + 1
            else:
                return f
        return None

    def add_entry(self, va):
        f = self.func_at(va)
        if f is None:
            if va in (0xA0, 0xB0, 0xC0):
                return
            self.warn(f"call target {va:08X} outside known code")
            return
        f.entries.add(va)
        self.entry_points.add(va)

    def fname(self, va):
        return f"F_{va:08X}"

    def jumptable(self, f, i):
        """Targets of `jr reg` by locating the lw from a table (lui/addu/lw pattern)."""
        reg = i.rs
        hi = lo = None
        va = i.va - 4
        while va >= max(f.start, i.va - 64):
            p = self.insns[va]
            if lo is None and p.op == 0x23 and p.rt == reg:
                lo = p.simm
                base = p.rs
            elif lo is not None and p.op == 15 and p.rt == base:
                hi = p.imm << 16
                break
            va -= 4
        if hi is None or lo is None:
            return None
        table = (hi + lo) & 0xFFFFFFFF
        targets = []
        while self.exe.contains(table):
            t = self.exe.word(table)
            if not f.contains(t) or t & 3:
                break
            targets.append(t)
            table += 4
        return targets or None

    def gen_func(self, f, entry, out):
        name = self.fname(entry)
        hle = self.hle.get(self.names.get(entry, "")) or self.hle.get(name)
        if hle is not None and entry == f.start:
            out.append(f"void {name}(void) {{ {hle}(); }}\n")
            return

        # labels: branch targets, jumptable targets, secondary entries
        labels = set(f.entries)
        jtabs = {}
        for va in range(f.start, f.end, 4):
            i = self.insns[va]
            if i.is_branch() and f.contains(i.branch_target()):
                labels.add(i.branch_target())
            elif i.is_j() and f.contains(i.target):
                labels.add(i.target)
            elif i.is_jr() and i.rs != 31:
                t = self.jumptable(f, i)
                if t:
                    jtabs[va] = t
                    labels.update(t)
                else:
                    self.warn(f"jr ${i.rs} at {va:08X} without jumptable: treated as tail call")

        body = []
        emit = body.append
        if entry != f.start:
            emit(f"    goto L_{entry:08X};")
        va = f.start
        while va < f.end:
            i = self.insns[va]
            if va in labels:
                emit(f"L_{va:08X}:;")
            if va in self.probes and va != entry:
                emit(f"    {self.probes[va]}();")
            if i.has_delay():
                d = self.insns.get(va + 4) if va + 4 < f.end else None
                dcode = translate(d, self) if d is not None else ""
                self.gen_control(f, i, d, dcode, jtabs.get(va), emit)
                if d is not None and (va + 4) in labels:
                    # the delay slot is also a branch target: keep a standalone copy
                    emit(f"    goto L_{va + 8:08X};")
                    labels.add(va + 8)
                    emit(f"L_{va + 4:08X}:;")
                    emit(f"    {dcode}")
                va += 8
                continue
            code = translate(i, self)
            if code:
                emit(f"    {code}")
            va += 4
        # Falling off the end of a region (or branching to its end) continues into
        # the next function, like the original straight-line code would.
        emit(f"L_{f.end:08X}:;")
        emit(f"    {self.fname(f.end)}(); return;" if f.end in self.entry_points else "    return;")
        probe = self.probes.get(entry)
        probe_call = f"    {probe}();\n" if probe else ""
        out.append(f"void {name}(void) {{\n    TRACE(0x{entry:08X}u);\n{probe_call}")
        out.append("\n".join(body))
        out.append("\n}\n\n")

    def call_stmt(self, target, link_va=None):
        link = f"R31 = 0x{link_va:08X}u; " if link_va is not None else ""
        if target in self.entry_points:
            return f"{link}{self.fname(target)}();"
        return f"{link}call_indirect(0x{target:08X}u);"

    def gen_control(self, f, i, d, dcode, jtab, emit):
        va = i.va
        poll = ""
        if i.is_branch():
            t = i.branch_target()
            cond = branch_cond(i)
            link = i.op == 1 and i.rt in (0x10, 0x11)
            if t <= va and f.contains(t):
                poll = " POLL();"
            emit(f"    {{ int c_ = {cond};")
            if link:
                emit(f"    R31 = 0x{va + 8:08X}u;")
            if dcode:
                emit(f"    {dcode}")
            if f.contains(t):
                emit(f"    if (c_) {{{poll} goto L_{t:08X}; }} }}")
            else:
                emit(f"    if (c_) {{ {self.call_stmt(t)} return; }} }}")
            return
        if i.is_j():
            t = i.target
            if dcode:
                emit(f"    {dcode}")
            if f.contains(t):
                if t <= va:
                    emit("    POLL();")
                emit(f"    goto L_{t:08X};")
            else:
                emit(f"    {self.call_stmt(t)} return;")
            return
        if i.is_jal():
            emit(f"    {dcode}" if dcode else "")
            emit(f"    {self.call_stmt(i.target, va + 8)}")
            return
        if i.is_jr():
            if i.rs == 31:
                if dcode:
                    emit(f"    {dcode}")
                emit("    return;")
                return
            emit(f"    {{ u32 t_ = {R(i.rs)};")
            if dcode:
                emit(f"    {dcode}")
            if jtab:
                emit("    switch (t_) {")
                for t in sorted(set(jtab)):
                    emit(f"    case 0x{t:08X}u: goto L_{t:08X};")
                emit(f"    default: call_indirect(t_); return; }} }}")
            else:
                emit("    call_indirect(t_); return; }")
            return
        if i.is_jalr():
            emit(f"    {{ u32 t_ = {R(i.rs)};")
            if dcode:
                emit(f"    {dcode}")
            emit(f"    {W(i.rd, f'0x{va + 8:08X}u') if i.rd else ''} call_indirect(t_); }}")
            return
        raise ValueError(hex(i.w))

    def generate(self, out_dir):
        os.makedirs(out_dir, exist_ok=True)
        entries = sorted(self.entry_points)
        with open(os.path.join(out_dir, "funcs.h"), "w") as h:
            h.write("/* generated by recomp.py — do not edit */\n#pragma once\n")
            for e in entries:
                h.write(f"void {self.fname(e)}(void);\n")
        with open(os.path.join(out_dir, "table.c"), "w") as t:
            t.write('#include "recomp.h"\n#include "funcs.h"\n\n')
            t.write("const FuncEntry g_func_table[] = {\n")
            for e in entries:
                t.write(f"    {{0x{e:08X}u, {self.fname(e)}}},\n")
            t.write("};\n")
            t.write(f"const int g_func_count = {len(entries)};\n")
        file_idx = 0
        n = 0
        out = None
        files = []
        for f in self.funcs:
            if out is None or n >= FUNCS_PER_FILE:
                if out:
                    out.close()
                path = os.path.join(out_dir, f"code_{file_idx:03d}.c")
                files.append(path)
                out = open(path, "w")
                out.write('/* generated by recomp.py — do not edit */\n#include "regs.h"\n#include "funcs.h"\n\n')
                file_idx += 1
                n = 0
            for e in sorted(f.entries):
                buf = []
                self.gen_func(f, e, buf)
                out.write("".join(buf))
            n += 1
        out.close()
        with open(os.path.join(out_dir, "warnings.txt"), "w") as w:
            w.write("\n".join(self.warnings) + "\n")
        return files


def load_hle(path):
    hle = {}
    if os.path.exists(path):
        for line in open(path):
            line = line.split("#")[0].strip()
            if line:
                parts = line.split()
                if parts[0] == "@probe":
                    sym, impl = "@probe", f"{parts[1]}:{parts[2]}"
                else:
                    sym, impl = parts
                if sym in hle:
                    impl = hle[sym] + "," + impl
                hle[sym] = impl
    return hle


def main():
    exe = Exe(sys.argv[1])
    regions, extra, names = load_symbols(sys.argv[2])
    hle = load_hle(sys.argv[3])
    rc = Recompiler(exe, regions, extra, names, hle)
    files = rc.generate(sys.argv[4])
    print(f"{len(rc.funcs)} regions, {len(rc.entry_points)} entry points, "
          f"{len(files)} files, {len(rc.warnings)} warnings")


if __name__ == "__main__":
    main()
