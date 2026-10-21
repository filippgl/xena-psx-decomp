/* Emulated PS1 hardware (everything except the CPU, which is recompiled). */
#pragma once
#include "recomp.h"
#include <stdio.h>

#define CPU_HZ 33868800LL
#define CYCLES_PER_FRAME (CPU_HZ / 60)

/* interrupt lines (I_STAT bits) */
enum {
    IRQ_VBLANK = 0,
    IRQ_GPU = 1,
    IRQ_CDROM = 2,
    IRQ_DMA = 3,
    IRQ_TMR0 = 4,
    IRQ_TMR1 = 5,
    IRQ_TMR2 = 6,
    IRQ_PAD = 7,
    IRQ_SIO = 8,
    IRQ_SPU = 9,
};

/* logging */
extern int g_verbose;
void log_msg(const char* fmt, ...);
void fatal(const char* fmt, ...);
#define LOG(...) do { if (g_verbose) log_msg(__VA_ARGS__); } while (0)

/* scheduler: virtual cycle clock that follows real time */
s64 hw_now(void);
typedef void (*EventFn)(void);
void sched_event(int id, s64 delay_cycles, EventFn fn);
void sched_cancel(int id);
enum { EV_VBLANK, EV_CD, EV_CD2, EV_TMR0, EV_TMR1, EV_TMR2, EV_SPU, EV_SIO, EV_COUNT };

/* interrupt controller */
extern u32 g_istat, g_imask;
void irq_raise(int line);
void hw_kick(void);
void hw_skip_to_next_event(void);
void hw_init(void);
void hw_run_events(void);

/* DMA */
void dma_write(u32 addr, u32 val);
u32 dma_read(u32 addr);

/* timers */
u32 timer_read(u32 addr);
void timer_write(u32 addr, u32 val);

/* GPU */
void gpu_init(void);
void gpu_gp0(u32 v);
void gpu_gp1(u32 v);
u32 gpu_read(void);
u32 gpu_stat(void);
void gpu_vblank(void);
/* Current display as XRGB8888; returns 0 if display is off */
int gpu_get_display(u32* out, int* w, int* h, int max_w, int max_h);
extern u16 g_vram[512 * 1024];

/* CD-ROM */
int cd_open_image(const char* path);
void cd_init(void);
u8 cd_read_reg(u32 addr);
void cd_write_reg(u32 addr, u8 v);
u32 cd_dma_read_word(void);
/* raw 2352-byte sector access for loaders */
int cd_read_sector_raw(u32 lba, u8* out2352);
int cd_find_file(const char* path, u32* lba, u32* size);

/* SPU */
void spu_init(void);
u16 spu_read(u32 addr);
void spu_write(u32 addr, u16 v);
void spu_dma_write(u32 word);
u32 spu_dma_read(void);
void spu_xa_push(const s16* samples, int count, int stereo, int rate);
void spu_render(s16* out, int frames);

/* MDEC */
void mdec_init(void);
void mdec_write(u32 addr, u32 v);
u32 mdec_read(u32 addr);
void mdec_dma_in(u32 word);
u32 mdec_dma_out(void);
int mdec_out_ready(void);

/* pads */
void sio_write(u32 addr, u32 v, int size);
u32 sio_read(u32 addr, int size);
void pad_poll_host(void);
u16 pad_buttons(int port); /* active-high PS1 button bits */

/* BIOS HLE */
void bios_init(void);
void bios_call(u32 table, u32 fn);
void bios_vblank(void);
void bios_deliver_event(u32 cls, u32 spec);
void bios_timer_irq(int t);
void irq_deliver(void);

/* platform */
int platform_init(int scale);
void platform_present(const u32* pixels, int w, int h);
int platform_pump(void); /* returns 0 when the window was closed */
void platform_wait_until(s64 cycles);
s64 platform_ticks_us(void);
void platform_audio_init(void);
extern int g_turbo;
