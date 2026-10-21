/* In-game self tests that drive the game's own library code (--selftest NAME FRAME). */
#include "hw.h"
#include <stdlib.h>

/* PsyQ libmcrd entry points in the game executable (see symbol_addrs.txt) */
#define MemCardExist 0x8009C0A8u
#define MemCardAccept 0x8009C2E4u
#define MemCardCreateFile 0x8009D304u
#define MemCardDeleteFile 0x8009D50Cu
#define MemCardWriteFile 0x8009CD54u
#define MemCardReadFile 0x8009CB34u
#define MemCardSync 0x8009D1E8u

#define SCRATCH 0x80008000u /* unused kernel RAM: strings, buffers, results */
#define STACK 0x8000E000u

static u32 guest(u32 fn, u32 a0, u32 a1, u32 a2, u32 a3, u32 a4) {
    Cpu saved = cpu;
    cpu.r[29] = STACK;
    cpu.r[4] = a0; cpu.r[5] = a1; cpu.r[6] = a2; cpu.r[7] = a3;
    mem_w32(STACK + 16, a4); /* fifth argument on the stack */
    cpu.r[31] = 0;
    call_indirect(fn);
    u32 v0 = cpu.r[2];
    cpu = saved;
    return v0;
}

static void put_str(u32 addr, const char* s) {
    do mem_w8(addr++, (u8)*s); while (*s++);
}

/* MemCardSync(0, &cmd, &result) blocks until the asynchronous call finishes */
static s32 sync_result(void) {
    guest(MemCardSync, 0, SCRATCH + 0x100, SCRATCH + 0x104, 0, 0);
    return (s32)mem_r32(SCRATCH + 0x104);
}

static int memcard_test(void) {
    const char* name = "BASLUS-00977SELFTEST";
    u32 nm = SCRATCH, wbuf = SCRATCH + 0x200, rbuf = SCRATCH + 0x600;
    put_str(nm, name);
    int fails = 0;

    guest(MemCardExist, 0, 0, 0, 0, 0);
    s32 r = sync_result();
    log_msg("selftest memcard: exist -> %d", r);
    guest(MemCardAccept, 0, 0, 0, 0, 0);
    r = sync_result();
    log_msg("selftest memcard: accept -> %d", r);

    r = (s32)guest(MemCardCreateFile, 0, nm, 1, 0, 0);
    log_msg("selftest memcard: create -> %d (0 ok, 6 already exists)", r);
    if (r != 0 && r != 6) fails++;

    for (u32 i = 0; i < 128; i++) mem_w8(wbuf + i, (u8)(i * 7 + 3));
    r = (s32)guest(MemCardWriteFile, 0, nm, wbuf, 0, 128);
    log_msg("selftest memcard: write call -> %d", r);
    r = sync_result();
    log_msg("selftest memcard: write -> %d", r);
    if (r != 0) fails++;

    for (u32 i = 0; i < 128; i++) mem_w8(rbuf + i, 0);
    r = (s32)guest(MemCardReadFile, 0, nm, rbuf, 0, 128);
    log_msg("selftest memcard: read call -> %d", r);
    r = sync_result();
    int same = 1;
    for (u32 i = 0; i < 128; i++) same &= mem_r8(rbuf + i) == (u8)(i * 7 + 3);
    log_msg("selftest memcard: read -> %d, data %s", r, same ? "matches" : "DIFFERS");
    if (r != 0 || !same) fails++;

    r = (s32)guest(MemCardDeleteFile, 0, nm, 0, 0, 0);
    log_msg("selftest memcard: delete -> %d", r);
    return fails;
}

void selftest_run(const char* name) {
    int fails = -1;
    if (!strcmp(name, "memcard")) fails = memcard_test();
    log_msg("selftest %s: %s", name, fails == 0 ? "PASS" : fails > 0 ? "FAIL" : "unknown test");
}
