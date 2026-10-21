/* High-level emulation of the PS1 BIOS kernel calls (A0/B0/C0 tables, syscalls,
 * exception dispatch) as used by the PsyQ libraries linked into the game. */
#include "hw.h"
#include <setjmp.h>
#include <stdlib.h>
#include <ctype.h>
#include <sys/stat.h>
#ifdef _WIN32
#include <direct.h>
#define MKDIR(p) _mkdir(p)
#else
#define MKDIR(p) mkdir(p, 0755)
#endif

#define IRQ_STACK 0x8000FF00u /* unused kernel RAM: stack for BIOS-level callbacks */

static u32 cop0[32];
#define SR cop0[12]

u32 cop0_read(int reg) { return cop0[reg]; }
void cop0_write(int reg, u32 v) { cop0[reg] = v; if (reg == 12) hw_kick(); }
void cop0_rfe(void) { SR = (SR & ~0xFu) | ((SR >> 2) & 0xF); }

/* ---------------- calling back into game code ---------------- */

static void call_guest(u32 func, u32 a0, u32 a1, int own_stack) {
    Cpu saved = cpu;
    cpu.r[4] = a0;
    cpu.r[5] = a1;
    if (own_stack) cpu.r[29] = IRQ_STACK;
    cpu.r[31] = 0;
    call_indirect(func);
    u32 v0 = cpu.r[2];
    cpu = saved;
    cpu.r[2] = v0;
}

/* ---------------- events ---------------- */

#define EV_MAX 32
typedef struct {
    u32 cls, spec, mode, func, status;
} BiosEvent;
static BiosEvent evs[EV_MAX];
static int in_irq;

static u32 open_event(u32 cls, u32 spec, u32 mode, u32 func) {
    for (int i = 0; i < EV_MAX; i++)
        if (evs[i].status == 0) {
            evs[i] = (BiosEvent){cls, spec, mode, func, 0x1000};
            LOG("OpenEvent(%08X,%04X,%04X,%08X) -> %d", cls, spec, mode, func, i);
            return 0xF1000000u | i;
        }
    return 0xFFFFFFFFu;
}

static BiosEvent* ev_get(u32 h) {
    u32 i = h & 0xFFFF;
    return i < EV_MAX ? &evs[i] : NULL;
}

void bios_deliver_event(u32 cls, u32 spec) {
    for (int i = 0; i < EV_MAX; i++) {
        BiosEvent* e = &evs[i];
        if (e->status != 0x2000 || e->cls != cls || !(e->spec & spec)) continue;
        if (e->mode == 0x2000) e->status = 0x4000;
        else if (e->mode == 0x1000 && e->func) call_guest(e->func, 0, 0, in_irq);
    }
}

/* ---------------- pads ---------------- */

static u32 pad_buf[2], pad_size[2];
static int pad_started;
static u32 pad_old_dest; /* OutdatedPadInitAndStart */

static void pad_update(void) {
    for (int p = 0; p < 2; p++) {
        if (!pad_buf[p]) continue;
        u16 b = pad_buttons(p);
        if (p == 0) {
            mem_w8(pad_buf[p], 0x00);
            mem_w8(pad_buf[p] + 1, 0x41);
            mem_w8(pad_buf[p] + 2, (u8)~b);
            mem_w8(pad_buf[p] + 3, (u8)(~b >> 8));
        } else {
            mem_w8(pad_buf[p], 0xFF);
        }
    }
    if (pad_old_dest) mem_w32(pad_old_dest, ~(u32)pad_buttons(0) & 0xFFFF);
}

/* ---------------- memory card (bu00:/bu10: on host files) ---------------- */

static const char* card_dir = "memcard";
typedef struct {
    FILE* f;
    char path[260];
    int async;
} CardFile;
static CardFile cfiles[16];
static int last_card_err;

static void card_event(u32 spec) {
    bios_deliver_event(0xF4000001u, spec); /* SwCARD */
    bios_deliver_event(0xF0000011u, spec); /* HwCARD */
}

static int card_path(const char* name, char* out, size_t n) {
    if (strncmp(name, "bu", 2) != 0 || !name[2] || !name[3] || name[4] != ':') return 0;
    char port = name[2];
    MKDIR(card_dir);
    snprintf(out, n, "%s/bu%c0", card_dir, port);
    MKDIR(out);
    snprintf(out, n, "%s/bu%c0/%s", card_dir, port, name + 5);
    return 1;
}

static void read_cstr(u32 addr, char* out, int n) {
    int i = 0;
    for (; i < n - 1; i++) {
        char c = (char)mem_r8(addr + i);
        if (!c) break;
        out[i] = c;
    }
    out[i] = 0;
}

static u32 file_open(u32 name_addr, u32 flags) {
    char name[64], path[260];
    read_cstr(name_addr, name, sizeof(name));
    if (!card_path(name, path, sizeof(path))) {
        LOG("open(%s) unsupported", name);
        return 0xFFFFFFFFu;
    }
    for (int fd = 2; fd < 16; fd++) {
        if (cfiles[fd].f) continue;
        FILE* f = NULL;
        if (flags & 0x200) { /* FCREAT: allocate (flags >> 16) blocks of 8 KB */
            f = fopen(path, "wb+");
            if (f) {
                u32 blocks = flags >> 16 ? flags >> 16 : 1;
                static u8 zero[8192];
                for (u32 i = 0; i < blocks; i++) fwrite(zero, 1, sizeof(zero), f);
                fflush(f);
                fseek(f, 0, SEEK_SET);
            }
        } else {
            f = fopen(path, (flags & 2) ? "rb+" : "rb");
        }
        if (!f) {
            LOG("open(%s) failed", name);
            last_card_err = 2;
            return 0xFFFFFFFFu;
        }
        cfiles[fd].f = f;
        cfiles[fd].async = (flags & 0x8000) != 0;
        snprintf(cfiles[fd].path, sizeof(cfiles[fd].path), "%s", path);
        LOG("open(%s, %X) -> %d", name, flags, fd);
        return (u32)fd;
    }
    return 0xFFFFFFFFu;
}

static u32 file_rw(u32 fd, u32 buf, u32 n, int write) {
    if (fd == 1 && write) { /* stdout */
        for (u32 i = 0; i < n; i++) fputc((int)mem_r8(buf + i), stdout);
        fflush(stdout);
        return n;
    }
    if (fd >= 16 || !cfiles[fd].f) return 0xFFFFFFFFu;
    u8* tmp = malloc(n ? n : 1);
    u32 r;
    if (write) {
        for (u32 i = 0; i < n; i++) tmp[i] = (u8)mem_r8(buf + i);
        r = (u32)fwrite(tmp, 1, n, cfiles[fd].f);
        fflush(cfiles[fd].f);
    } else {
        r = (u32)fread(tmp, 1, n, cfiles[fd].f);
        for (u32 i = 0; i < r; i++) mem_w8(buf + i, tmp[i]);
    }
    free(tmp);
    if (cfiles[fd].async) card_event(0x0004);
    return r;
}

/* directory iteration for firstfile/nextfile */
static char dir_pattern[64];
static int dir_index;

static int wild_match(const char* pat, const char* s) {
    for (; *pat; pat++, s++) {
        if (*pat == '*') return 1;
        if (*pat == '?') { if (!*s) return 0; continue; }
        if (toupper((u8)*pat) != toupper((u8)*s)) return 0;
    }
    return *s == 0;
}

#ifdef _WIN32
#include <windows.h>
static int list_dir(const char* dir, int index, char* name, long* size) {
    char pat[300];
    snprintf(pat, sizeof(pat), "%s/*", dir);
    WIN32_FIND_DATAA fd;
    HANDLE h = FindFirstFileA(pat, &fd);
    if (h == INVALID_HANDLE_VALUE) return 0;
    int i = 0;
    do {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
        if (!wild_match(dir_pattern, fd.cFileName)) continue;
        if (i++ == index) {
            snprintf(name, 21, "%s", fd.cFileName);
            *size = (long)fd.nFileSizeLow;
            FindClose(h);
            return 1;
        }
    } while (FindNextFileA(h, &fd));
    FindClose(h);
    return 0;
}
#else
#include <dirent.h>
static int list_dir(const char* dir, int index, char* name, long* size) {
    DIR* d = opendir(dir);
    if (!d) return 0;
    struct dirent* e;
    int i = 0;
    while ((e = readdir(d))) {
        if (e->d_name[0] == '.') continue;
        if (!wild_match(dir_pattern, e->d_name)) continue;
        if (i++ == index) {
            char p[600];
            snprintf(p, sizeof(p), "%s/%s", dir, e->d_name);
            struct stat st;
            stat(p, &st);
            snprintf(name, 21, "%s", e->d_name);
            *size = (long)st.st_size;
            closedir(d);
            return 1;
        }
    }
    closedir(d);
    return 0;
}
#endif

static char dir_port;

static u32 dir_entry(u32 out) {
    char dir[260], name[32];
    long size;
    snprintf(dir, sizeof(dir), "%s/bu%c0", card_dir, dir_port);
    if (!list_dir(dir, dir_index, name, &size)) return 0;
    dir_index++;
    for (int i = 0; i < 20; i++) mem_w8(out + i, i < (int)strlen(name) ? (u8)name[i] : 0);
    mem_w32(out + 20, 0x50);
    mem_w32(out + 24, (u32)size);
    mem_w32(out + 28, 0);
    mem_w32(out + 32, 0);
    return out;
}

static u32 first_file(u32 pat_addr, u32 out) {
    char pat[64];
    read_cstr(pat_addr, pat, sizeof(pat));
    if (strncmp(pat, "bu", 2) != 0 || strlen(pat) < 5) return 0;
    dir_port = pat[2];
    snprintf(dir_pattern, sizeof(dir_pattern), "%s", pat + 5);
    dir_index = 0;
    char d[260];
    snprintf(d, sizeof(d), "%s", card_dir);
    MKDIR(d);
    snprintf(d, sizeof(d), "%s/bu%c0", card_dir, dir_port);
    MKDIR(d);
    return dir_entry(out);
}

/* ---------------- root counter / vblank events ---------------- */

static int rcnt_autoclear[4] = {1, 1, 1, 1};

void bios_timer_irq(int t) { (void)t; }

void bios_vblank(void) {
    if (pad_started) pad_update();
}

/* ---------------- exception dispatch ---------------- */

static u32 hook_jmpbuf; /* SetCustomExitFromException (HookEntryInt) */
static jmp_buf irq_env;

void irq_deliver(void) {
    if (in_irq) return;
    if ((SR & 0x401) != 0x401) return;
    u32 pending = g_istat & g_imask;
    if (!pending) return;

    Cpu saved = cpu;
    u32 saved_sr = SR;
    in_irq = 1;
    SR &= ~1u;

    /* BIOS-owned handlers first: root counter events (RCnt0-2, VBlank as RCnt3) */
    for (int t = 0; t < 4; t++) {
        u32 bit = t == 3 ? 1u << IRQ_VBLANK : 1u << (IRQ_TMR0 + t);
        if (!(pending & bit)) continue;
        cpu.r[29] = IRQ_STACK;
        bios_deliver_event(0xF2000000u + t, 0x0002);
        if (rcnt_autoclear[t]) g_istat &= ~bit;
    }

    {
        static int n;
        if (g_verbose && n++ < 8)
            log_msg("irq: pending=%03X after-bios=%03X hook=%08X clr3=%d", pending, g_istat & g_imask,
                    hook_jmpbuf, rcnt_autoclear[3]);
    }
    if (hook_jmpbuf && (g_istat & g_imask)) {
        u32 jb = hook_jmpbuf;
        cpu.r[31] = mem_r32(jb + 0);
        cpu.r[29] = mem_r32(jb + 4);
        cpu.r[30] = mem_r32(jb + 8);
        for (int i = 0; i < 8; i++) cpu.r[16 + i] = mem_r32(jb + 12 + i * 4);
        cpu.r[28] = mem_r32(jb + 44);
        cpu.r[2] = 1;
        if (!setjmp(irq_env)) {
            call_indirect(cpu.r[31]);
        }
    }

    cpu = saved;
    SR = saved_sr;
    in_irq = 0;
    if (g_istat & g_imask) /* not acknowledged: retry a little later */
        if (g_next_event > g_cycles + 2000) g_next_event = g_cycles + 2000;
}

static void return_from_exception(void) {
    if (in_irq) longjmp(irq_env, 1);
    LOG("ReturnFromException outside of an interrupt");
}

/* ---------------- kernel call tables ---------------- */

void bios_syscall(u32 pc) {
    (void)pc;
    switch (cpu.r[4]) {
    case 1: { /* EnterCriticalSection */
        int was = (SR & 0x401) == 0x401;
        SR &= ~0x401u;
        cpu.r[2] = was;
        break;
    }
    case 2: /* ExitCriticalSection */
        SR |= 0x401;
        hw_kick();
        break;
    default:
        LOG("syscall(%u)", cpu.r[4]);
        break;
    }
}

static int warned[3][256];

static void unimpl(int t, u32 fn) {
    if (!warned[t][fn & 255]) {
        warned[t][fn & 255] = 1;
        log_msg("BIOS %c0:%02X not implemented (a0=%08X a1=%08X)", 'A' + t, fn, cpu.r[4], cpu.r[5]);
    }
    cpu.r[2] = 0;
}

static void bios_a0(u32 fn) {
    u32 a0 = cpu.r[4], a1 = cpu.r[5];
    switch (fn) {
    case 0x39: case 0x44: case 0x70: case 0x72: cpu.r[2] = 0; break; /* InitHeap, FlushCache, _bu_init, CdRemove */
    case 0x49: gpu_gp0(a0); cpu.r[2] = 0; break;                      /* GPU_cw */
    case 0xAB: case 0xAC: card_event(0x0004); cpu.r[2] = 1; break;    /* _card_info / _card_load */
    default: unimpl(0, fn); (void)a1; break;
    }
}

static void bios_b0(u32 fn) {
    u32 a0 = cpu.r[4], a1 = cpu.r[5], a2 = cpu.r[6], a3 = cpu.r[7];
    BiosEvent* e;
    switch (fn) {
    case 0x07: bios_deliver_event(a0, a1); break;
    case 0x08: cpu.r[2] = open_event(a0, a1, a2, a3); break;
    case 0x09: if ((e = ev_get(a0))) e->status = 0; cpu.r[2] = 1; break;
    case 0x0A: /* WaitEvent */
        if ((e = ev_get(a0)) && e->status) {
            int guard = 0;
            while (e->status != 0x4000 && guard++ < 100000) { hw_skip_to_next_event(); irq_poll(); }
            if (e->status == 0x4000) e->status = 0x2000;
        }
        cpu.r[2] = 1;
        break;
    case 0x0B: /* TestEvent */
        cpu.r[2] = 0;
        if ((e = ev_get(a0)) && e->status == 0x4000) { e->status = 0x2000; cpu.r[2] = 1; }
        break;
    case 0x0C: if ((e = ev_get(a0)) && e->status) e->status = 0x2000; cpu.r[2] = 1; break;
    case 0x0D: if ((e = ev_get(a0)) && e->status) e->status = 0x1000; cpu.r[2] = 1; break;
    case 0x12: pad_buf[0] = a0; pad_size[0] = a1; pad_buf[1] = a2; pad_size[1] = a3; cpu.r[2] = 2; break;
    case 0x13: pad_started = 1; cpu.r[2] = 1; break;
    case 0x14: pad_started = 0; cpu.r[2] = 1; break;
    case 0x15: pad_old_dest = a1; pad_started = 1; cpu.r[2] = 2; break;
    case 0x17: return_from_exception(); break;
    case 0x18: hook_jmpbuf = 0; break;
    case 0x19: hook_jmpbuf = a0; LOG("HookEntryInt(%08X)", a0); break;
    case 0x32: cpu.r[2] = file_open(a0, a1); break;
    case 0x33: /* lseek */
        if (a0 < 16 && cfiles[a0].f) {
            fseek(cfiles[a0].f, (long)a1, a2 == 0 ? SEEK_SET : a2 == 1 ? SEEK_CUR : SEEK_END);
            cpu.r[2] = (u32)ftell(cfiles[a0].f);
        } else cpu.r[2] = 0xFFFFFFFFu;
        break;
    case 0x34: cpu.r[2] = file_rw(a0, a1, a2, 0); break;
    case 0x35: cpu.r[2] = file_rw(a0, a1, a2, 1); break;
    case 0x36:
        if (a0 < 16 && cfiles[a0].f) { fclose(cfiles[a0].f); cfiles[a0].f = NULL; }
        cpu.r[2] = a0;
        break;
    case 0x41: cpu.r[2] = 1; break; /* format */
    case 0x42: cpu.r[2] = first_file(a0, a1); break;
    case 0x43: cpu.r[2] = dir_entry(a0); break;
    case 0x45: { /* erase */
        char name[64], path[260];
        read_cstr(a0, name, sizeof(name));
        cpu.r[2] = card_path(name, path, sizeof(path)) && remove(path) == 0;
        break;
    }
    case 0x4A: case 0x4B: case 0x4C: cpu.r[2] = 1; break; /* InitCard/StartCard/StopCard */
    case 0x4E: card_event(0x0004); cpu.r[2] = 1; break;  /* _card_write */
    case 0x50: cpu.r[2] = 1; break;                       /* _new_card */
    case 0x56: case 0x57: cpu.r[2] = 0; break;
    case 0x5B: cpu.r[2] = 0; break;                       /* ChangeClearPad */
    default: unimpl(1, fn); break;
    }
}

static void bios_c0(u32 fn) {
    u32 a0 = cpu.r[4], a1 = cpu.r[5];
    switch (fn) {
    case 0x02: LOG("SysEnqIntRP(%u, %08X)", a0, a1); cpu.r[2] = 0; break;
    case 0x03: cpu.r[2] = 0; break;
    case 0x0A: /* ChangeClearRCnt */
        LOG("ChangeClearRCnt(%u, %u)", a0, a1);
        if (a0 < 4) { cpu.r[2] = (u32)rcnt_autoclear[a0]; rcnt_autoclear[a0] = (int)a1; }
        break;
    default: unimpl(2, fn); break;
    }
}

void bios_call(u32 table, u32 fn) {
    switch (table) {
    case 0xA0: bios_a0(fn & 0xFF); break;
    case 0xB0: bios_b0(fn & 0xFF); break;
    case 0xC0: bios_c0(fn & 0xFF); break;
    }
}

void bios_init(void) {
    SR = 0x40000401;
    memset(evs, 0, sizeof(evs));
}
