/* Memory-mapped I/O, interrupt controller, DMA, timers and the event scheduler. */
#include "hw.h"
#include <stdarg.h>
#include <stdlib.h>

u8 g_ram[RAM_SIZE];
u8 g_scratch[0x400];
u32 g_istat, g_imask;
int g_verbose;

void log_msg(const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    vfprintf(stderr, fmt, ap);
    va_end(ap);
    fputc('\n', stderr);
    fflush(stderr);
}

void fatal(const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    fprintf(stderr, "FATAL: ");
    vfprintf(stderr, fmt, ap);
    va_end(ap);
    fputc('\n', stderr);
    fflush(stderr);
    exit(1);
}

/* ---------------- scheduler ---------------- */

static struct {
    s64 when;
    EventFn fn;
    int active;
} events[EV_COUNT];

s64 g_cycles, g_next_event;

s64 hw_now(void) { return g_cycles; }

static void update_next(void) {
    s64 next = g_cycles + CYCLES_PER_FRAME;
    for (int i = 0; i < EV_COUNT; i++)
        if (events[i].active && events[i].when < next) next = events[i].when;
    g_next_event = next;
}

/* force the next POLL() to look at interrupts (after IRQ/mask/SR changes) */
void hw_kick(void) { g_next_event = g_cycles; }

void sched_event(int id, s64 delay, EventFn fn) {
    events[id].when = g_cycles + delay;
    events[id].fn = fn;
    events[id].active = 1;
    if (events[id].when < g_next_event) g_next_event = events[id].when;
}

void sched_cancel(int id) { events[id].active = 0; }

void hw_run_events(void) {
    for (;;) {
        int best = -1;
        for (int i = 0; i < EV_COUNT; i++)
            if (events[i].active && events[i].when <= g_cycles && (best < 0 || events[i].when < events[best].when))
                best = i;
        if (best < 0) break;
        events[best].active = 0;
        events[best].fn();
    }
    update_next();
}

/* used by blocking BIOS calls: jump the clock to the next event */
void hw_skip_to_next_event(void) {
    s64 next = -1;
    for (int i = 0; i < EV_COUNT; i++)
        if (events[i].active && (next < 0 || events[i].when < next)) next = events[i].when;
    if (next > g_cycles) g_cycles = next;
}

void irq_raise(int line) { g_istat |= 1u << line; hw_kick(); }

/* ---------------- timers ---------------- */

typedef struct {
    u32 mode, target;
    s64 base;      /* cycle at which counter was 0 */
    int divider;   /* cycles per tick */
} Timer;
static Timer timers[3];

static int timer_divider(int t) {
    u32 src = (timers[t].mode >> 8) & 3;
    if (t == 1 && (src & 1)) return 2152;          /* hblank */
    if (t == 2 && (src & 2)) return 8;             /* sysclk/8 */
    if (t == 0 && (src & 1)) return 5;             /* dotclock (approx.) */
    return 1;
}

static u32 timer_value(int t) {
    s64 ticks = (hw_now() - timers[t].base) / timers[t].divider;
    u32 limit = (timers[t].mode & 8) ? (timers[t].target ? timers[t].target : 0x10000) : 0x10000;
    return (u32)(ticks % (limit ? limit : 0x10000)) & 0xFFFF;
}

static void timer_fire(int t);
static void tmr0(void) { timer_fire(0); }
static void tmr1(void) { timer_fire(1); }
static void tmr2(void) { timer_fire(2); }
static const EventFn timer_fns[3] = {tmr0, tmr1, tmr2};

static void timer_schedule(int t) {
    Timer* tm = &timers[t];
    if (!(tm->mode & 0x30)) { sched_cancel(EV_TMR0 + t); return; }
    u32 cur = timer_value(t);
    u32 goal = (tm->mode & 0x10) ? tm->target : 0xFFFF;
    if (goal == 0) goal = 0x10000;
    u32 dist = goal > cur ? goal - cur : (0x10000 - cur) + goal;
    if (dist == 0) dist = 0x10000;
    sched_event(EV_TMR0 + t, (s64)dist * tm->divider, timer_fns[t]);
}

static void timer_fire(int t) {
    Timer* tm = &timers[t];
    tm->mode |= (tm->mode & 0x10) ? 0x800 : 0x1000;
    irq_raise(IRQ_TMR0 + t);
    bios_timer_irq(t);
    if (tm->mode & 0x40) timer_schedule(t); /* repeat */
}

u32 timer_read(u32 addr) {
    int t = (addr >> 4) & 3;
    if (t > 2) return 0;
    switch (addr & 0xF) {
    case 0: return timer_value(t);
    case 4: { u32 m = timers[t].mode; timers[t].mode &= ~0x1800u; return m | 0x400; }
    case 8: return timers[t].target;
    }
    return 0;
}

void timer_write(u32 addr, u32 val) {
    int t = (addr >> 4) & 3;
    if (t > 2) return;
    Timer* tm = &timers[t];
    switch (addr & 0xF) {
    case 0: tm->base = hw_now() - (s64)(val & 0xFFFF) * tm->divider; break;
    case 4:
        tm->mode = (val & 0x3FF) | 0x400;
        tm->divider = timer_divider(t);
        tm->base = hw_now();
        break;
    case 8: tm->target = val & 0xFFFF; break;
    }
    timer_schedule(t);
}

/* ---------------- DMA ---------------- */

typedef struct {
    u32 madr, bcr, chcr;
} DmaChan;
static DmaChan dma[7];
static u32 dpcr = 0x07654321, dicr;
static int mdec_out_pending;

static void dma_finish(int ch) {
    dma[ch].chcr &= ~0x01000000u;
    if ((dicr & (1u << (16 + ch))) && (dicr & 0x800000)) {
        dicr |= 1u << (24 + ch);
    }
    u32 was = dicr & 0x80000000u;
    if ((dicr & 0x800000) && (dicr & (dicr >> 8) & 0x7F0000)) dicr |= 0x80000000u;
    else dicr &= ~0x80000000u;
    if (!was && (dicr & 0x80000000u)) irq_raise(IRQ_DMA);
}

static void dma_run(int ch) {
    DmaChan* c = &dma[ch];
    u32 chcr = c->chcr;
    int sync = (chcr >> 9) & 3;
    int from_ram = chcr & 1;
    int step = (chcr & 2) ? -4 : 4;
    u32 addr = c->madr & 0x1FFFFC;

    if (ch == 6) { /* OTC: build reverse-linked empty ordering table */
        u32 n = c->bcr & 0xFFFF;
        if (!n) n = 0x10000;
        while (n--) {
            u32 v = n == 0 ? 0x00FFFFFF : ((addr - 4) & 0x1FFFFC);
            memcpy(&g_ram[addr], &v, 4);
            addr = (addr - 4) & 0x1FFFFC;
        }
        dma_finish(ch);
        return;
    }

    if (sync == 2) { /* linked list (GPU only) */
        int guard = 0;
        for (;;) {
            u32 hdr;
            memcpy(&hdr, &g_ram[addr], 4);
            u32 n = hdr >> 24;
            for (u32 i = 0; i < n; i++) {
                u32 w;
                memcpy(&w, &g_ram[(addr + 4 + i * 4) & 0x1FFFFC], 4);
                gpu_gp0(w);
            }
            if ((hdr & 0x800000) || ++guard > 0x100000) break;
            addr = hdr & 0x1FFFFC;
        }
        c->madr = 0x00FFFFFF;
        dma_finish(ch);
        return;
    }

    if (ch == 3) LOG("dma3: %08X bcr=%08X chcr=%08X", c->madr, c->bcr, chcr);
    u32 words = (sync == 0) ? (c->bcr & 0xFFFF ? c->bcr & 0xFFFF : 0x10000)
                            : (c->bcr & 0xFFFF) * (c->bcr >> 16);
    for (u32 i = 0; i < words; i++) {
        u32 w;
        if (from_ram) {
            memcpy(&w, &g_ram[addr], 4);
            switch (ch) {
            case 0: mdec_dma_in(w); break;
            case 2: gpu_gp0(w); break;
            case 4: spu_dma_write(w); break;
            default: break;
            }
        } else {
            switch (ch) {
            case 1: w = mdec_dma_out(); break;
            case 2: w = gpu_read(); break;
            case 3: w = cd_dma_read_word(); break;
            case 4: w = spu_dma_read(); break;
            default: w = 0; break;
            }
            memcpy(&g_ram[addr], &w, 4);
        }
        addr = (addr + step) & 0x1FFFFC;
    }
    if (sync == 1) c->madr = addr;
    dma_finish(ch);
}

void dma_write(u32 addr, u32 val) {
    u32 off = addr - 0x1F801080;
    if (off >= 0x70) {
        if (addr == 0x1F8010F0) dpcr = val;
        else if (addr == 0x1F8010F4) {
            u32 ack = val & 0x7F000000;
            dicr = (dicr & ~0x00FF803Fu & ~ack) | (val & 0x00FF803F);
            dicr &= ~ack;
            if ((dicr & 0x800000) && (dicr & (dicr >> 8) & 0x7F0000)) dicr |= 0x80000000u;
            else dicr &= ~0x80000000u;
        }
        return;
    }
    int ch = off >> 4;
    switch (off & 0xF) {
    case 0: dma[ch].madr = val & 0xFFFFFF; break;
    case 4: dma[ch].bcr = val; break;
    case 8:
        dma[ch].chcr = val;
        if (val & 0x01000000) {
            /* devices are always ready, so transfers complete immediately; the one exception
             * is MDEC output, which waits until the decoder has produced data */
            if (ch == 1 && !mdec_out_ready()) { mdec_out_pending = 1; break; }
            dma_run(ch);
            if (ch == 0 && mdec_out_pending && mdec_out_ready()) { mdec_out_pending = 0; dma_run(1); }
        }
        break;
    }
}

u32 dma_read(u32 addr) {
    u32 off = addr - 0x1F801080;
    if (addr == 0x1F8010F0) return dpcr;
    if (addr == 0x1F8010F4) return dicr;
    if (off >= 0x70) return 0;
    int ch = off >> 4;
    switch (off & 0xF) {
    case 0: return dma[ch].madr;
    case 4: return dma[ch].bcr;
    case 8: return dma[ch].chcr;
    }
    return 0;
}

/* ---------------- I/O dispatch ---------------- */

static u32 memctrl[16];

u32 io_read(u32 p, int size) {
    if (p >= 0x1F801C00 && p < 0x1F802000) {
        u32 v = spu_read(p & ~1u);
        if (size == 4) v |= (u32)spu_read((p + 2) & ~1u) << 16;
        return v;
    }
    if (p >= 0x1F801080 && p < 0x1F801100) return dma_read(p & ~3u) >> ((p & 3) * 8);
    if (p >= 0x1F801100 && p < 0x1F801130) return timer_read(p & ~3u);
    switch (p & ~3u) {
    case 0x1F801070: return g_istat;
    case 0x1F801074: return g_imask;
    case 0x1F801810: return gpu_read();
    case 0x1F801814: return gpu_stat();
    case 0x1F801820: return mdec_read(0x1F801820);
    case 0x1F801824: return mdec_read(0x1F801824);
    }
    if (p >= 0x1F801800 && p < 0x1F801804) return cd_read_reg(p);
    if (p >= 0x1F801000 && p < 0x1F801040) return memctrl[(p >> 2) & 15];
    if (p >= 0x1F801040 && p < 0x1F801050) return sio_read(p, size);
    if (p >= 0x1F801050 && p < 0x1F801060) return (p & 0xF) == 4 ? 0x5 : 0; /* SIO1 */
    if (p >= 0x1F000000 && p < 0x1F800000) return 0xFFFFFFFF; /* expansion 1 */
    if (p >= 0x1FC00000 && p < 0x1FC80000) return 0;           /* BIOS ROM */
    if (p == 0x1F801060) return 0xB88;
    LOG("io_read%d unhandled %08X", size * 8, p);
    return 0;
}

void io_write(u32 p, u32 v, int size) {
    if (p >= 0x1F801C00 && p < 0x1F802000) {
        spu_write(p & ~1u, (u16)v);
        if (size == 4) spu_write((p + 2) & ~1u, (u16)(v >> 16));
        return;
    }
    if (p >= 0x1F801080 && p < 0x1F801100) {
        u32 al = p & ~3u;
        if (size < 4) { /* merge partial writes; DICR flag bits only acknowledge what is written */
            int sh = (p & 3) * 8;
            u32 m = (size == 1 ? 0xFFu : 0xFFFFu) << sh;
            u32 cur = dma_read(al);
            if (al == 0x1F8010F4) cur &= 0x00FFFFFF;
            v = (cur & ~m) | ((v << sh) & m);
        }
        dma_write(al, v);
        return;
    }
    if (p >= 0x1F801100 && p < 0x1F801130) { timer_write(p & ~3u, v); return; }
    switch (p & ~3u) {
    case 0x1F801070: g_istat &= v; return;
    case 0x1F801074: g_imask = v & 0x7FF; hw_kick(); return;
    case 0x1F801810: gpu_gp0(v); return;
    case 0x1F801814: gpu_gp1(v); return;
    case 0x1F801820: case 0x1F801824: mdec_write(p & ~3u, v); return;
    }
    if (p >= 0x1F801800 && p < 0x1F801804) { cd_write_reg(p, (u8)v); return; }
    if (p >= 0x1F801000 && p < 0x1F801040) { memctrl[(p >> 2) & 15] = v; return; }
    if (p >= 0x1F801040 && p < 0x1F801050) { sio_write(p, v, size); return; }
    if (p >= 0x1F801050 && p < 0x1F801060) return; /* SIO1 (serial port) */
    if (p == 0x1F801060) return;
    if (p >= 0x1F802000 && p < 0x1F803000) return; /* expansion 2 / debug */
    LOG("io_write%d unhandled %08X = %08X", size * 8, p, v);
}

/* ---------------- vblank ---------------- */

void host_frame(void);

static void vblank_event(void) {
    sched_event(EV_VBLANK, CYCLES_PER_FRAME, vblank_event);
    irq_raise(IRQ_VBLANK);
    gpu_vblank();
    bios_vblank();
    host_frame();
}

void hw_init(void) {
    for (int t = 0; t < 3; t++) { timers[t].divider = 1; timers[t].base = 0; }
    sched_event(EV_VBLANK, CYCLES_PER_FRAME, vblank_event);
}
