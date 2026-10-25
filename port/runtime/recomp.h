/* Runtime interface shared by the generated code and the native runtime. */
#pragma once
#include <stdint.h>
#include <string.h>

typedef uint8_t u8;
typedef int8_t s8;
typedef uint16_t u16;
typedef int16_t s16;
typedef uint32_t u32;
typedef int32_t s32;
typedef uint64_t u64;
typedef int64_t s64;

typedef struct {
    u32 r[32];
    u32 hi, lo;
} Cpu;

extern Cpu cpu;


/* ---- memory ---- */
#define RAM_SIZE 0x200000
extern u8 g_ram[RAM_SIZE];
extern u8 g_scratch[0x400];

u32 io_read(u32 addr, int size);
void io_write(u32 addr, u32 val, int size);

/* KUSEG/KSEG0/KSEG1 all map onto the same physical space */
#define PHYS(a) ((a) & 0x1FFFFFFF)

static inline u8* ram_ptr(u32 addr) { return &g_ram[addr & (RAM_SIZE - 1)]; }

static inline u32 mem_r8(u32 a) {
    u32 p = PHYS(a);
    if (p < 0x800000) return g_ram[p & (RAM_SIZE - 1)];
    if ((p & 0xFFFFFC00) == 0x1F800000) return g_scratch[p & 0x3FF];
    return io_read(p, 1);
}
static inline u32 mem_r16(u32 a) {
    u32 p = PHYS(a);
    u16 v;
    if (p < 0x800000) { memcpy(&v, &g_ram[p & (RAM_SIZE - 2)], 2); return v; }
    if ((p & 0xFFFFFC00) == 0x1F800000) { memcpy(&v, &g_scratch[p & 0x3FE], 2); return v; }
    return io_read(p, 2);
}
static inline u32 mem_r32(u32 a) {
    u32 p = PHYS(a);
    u32 v;
    if (p < 0x800000) { memcpy(&v, &g_ram[p & (RAM_SIZE - 4)], 4); return v; }
    if ((p & 0xFFFFFC00) == 0x1F800000) { memcpy(&v, &g_scratch[p & 0x3FC], 4); return v; }
    return io_read(p, 4);
}
static inline void mem_w8(u32 a, u32 v) {
    u32 p = PHYS(a);
    if (p < 0x800000) { g_ram[p & (RAM_SIZE - 1)] = (u8)v; return; }
    if ((p & 0xFFFFFC00) == 0x1F800000) { g_scratch[p & 0x3FF] = (u8)v; return; }
    io_write(p, v & 0xFF, 1);
}
static inline void mem_w16(u32 a, u32 v) {
    u32 p = PHYS(a);
    u16 h = (u16)v;
    if (p < 0x800000) { memcpy(&g_ram[p & (RAM_SIZE - 2)], &h, 2); return; }
    if ((p & 0xFFFFFC00) == 0x1F800000) { memcpy(&g_scratch[p & 0x3FE], &h, 2); return; }
    io_write(p, h, 2);
}
static inline void mem_w32(u32 a, u32 v) {
    u32 p = PHYS(a);
    if (p < 0x800000) { memcpy(&g_ram[p & (RAM_SIZE - 4)], &v, 4); return; }
    if ((p & 0xFFFFFC00) == 0x1F800000) { memcpy(&g_scratch[p & 0x3FC], &v, 4); return; }
    if (a == 0xFFFE0130) return; /* cache control */
    io_write(p, v, 4);
}
/* Unaligned word access (little-endian semantics of the R3000) */
static inline u32 mem_lwl(u32 a, u32 old) {
    u32 w = mem_r32(a & ~3u);
    switch (a & 3) {
    case 0: return (old & 0x00FFFFFF) | (w << 24);
    case 1: return (old & 0x0000FFFF) | (w << 16);
    case 2: return (old & 0x000000FF) | (w << 8);
    default: return w;
    }
}
static inline u32 mem_lwr(u32 a, u32 old) {
    u32 w = mem_r32(a & ~3u);
    switch (a & 3) {
    case 0: return w;
    case 1: return (old & 0xFF000000) | (w >> 8);
    case 2: return (old & 0xFFFF0000) | (w >> 16);
    default: return (old & 0xFFFFFF00) | (w >> 24);
    }
}
static inline void mem_swl(u32 a, u32 v) {
    u32 al = a & ~3u, w = mem_r32(al);
    switch (a & 3) {
    case 0: w = (w & 0xFFFFFF00) | (v >> 24); break;
    case 1: w = (w & 0xFFFF0000) | (v >> 16); break;
    case 2: w = (w & 0xFF000000) | (v >> 8); break;
    default: w = v; break;
    }
    mem_w32(al, w);
}
static inline void mem_swr(u32 a, u32 v) {
    u32 al = a & ~3u, w = mem_r32(al);
    switch (a & 3) {
    case 0: w = v; break;
    case 1: w = (w & 0x000000FF) | (v << 8); break;
    case 2: w = (w & 0x0000FFFF) | (v << 16); break;
    default: w = (w & 0x00FFFFFF) | (v << 24); break;
    }
    mem_w32(al, w);
}

/* ---- CPU helpers ---- */
static inline void cpu_div(u32 a, u32 b) {
    s32 n = (s32)a, d = (s32)b;
    if (d == 0) { cpu.lo = n >= 0 ? 0xFFFFFFFFu : 1u; cpu.hi = a; }
    else if (n == INT32_MIN && d == -1) { cpu.lo = a; cpu.hi = 0; }
    else { cpu.lo = (u32)(n / d); cpu.hi = (u32)(n % d); }
}
static inline void cpu_divu(u32 a, u32 b) {
    if (b == 0) { cpu.lo = 0xFFFFFFFFu; cpu.hi = a; }
    else { cpu.lo = a / b; cpu.hi = a % b; }
}
void cpu_break(u32 pc, u32 code);
void cpu_bad_insn(u32 pc, u32 word);
u32 cop0_read(int reg);
void cop0_write(int reg, u32 v);
void cop0_rfe(void);
void bios_syscall(u32 pc);

/* ---- GTE ---- */
u32 gte_read_data(int reg);
u32 gte_read_ctrl(int reg);
void gte_write_data(int reg, u32 v);
void gte_write_ctrl(int reg, u32 v);
void gte_command(u32 cmd);
void gte_store_sxy(u32 addr, int reg); /* swc2 of SXY0-2: remembers where vertices go */

/* ---- control flow ---- */
typedef void (*RecompFunc)(void);
typedef struct {
    u32 addr;
    RecompFunc fn;
} FuncEntry;
extern const FuncEntry g_func_table[];
extern const int g_func_count;
void call_indirect(u32 addr);
void probe_player_update(void);
void probe_player_steer(void);
void probe_player_start(void);
void probe_file_lookup(void);
void probe_frame_swap(void);

/* "Interrupt" delivery point, emitted on loop back-edges */
/* Virtual time: every loop iteration (back-edge) costs CYCLES_PER_LOOP cycles of the
 * 33.8688 MHz clock. Timing-sensitive library code (timeouts counted in iterations)
 * then behaves as on hardware; real-time pacing happens once per frame. */
#define CYCLES_PER_LOOP 24
extern s64 g_cycles, g_next_event;
void irq_poll(void);
#define POLL() do { g_cycles += CYCLES_PER_LOOP; if (g_cycles >= g_next_event) irq_poll(); } while (0)

/* debug: log the first call of every function (--trace) */
extern int g_trace;
void trace_func(u32 addr);
#define TRACE(a) do { if (g_trace) trace_func(a); } while (0)
