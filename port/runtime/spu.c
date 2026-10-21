/* SPU: register file, 512 KB sound RAM and transfers. (Voice mixing: see spu_render.) */
#include "hw.h"

static u16 regs[0x200];  /* 0x1F801C00..0x1F801FFF as halfwords */
static u8 spu_ram[0x80000];
static u32 xfer_addr;    /* current transfer address in bytes */

#define REG(a) regs[((a) - 0x1F801C00) >> 1]
#define SPUCNT REG(0x1F801DAA)
#define SPUSTAT REG(0x1F801DAE)

void spu_init(void) {
    memset(regs, 0, sizeof(regs));
    memset(spu_ram, 0, sizeof(spu_ram));
}

u16 spu_read(u32 a) {
    switch (a) {
    case 0x1F801DAE: return (SPUCNT & 0x3F) | (SPUSTAT & ~0x3Fu & ~0x400u);
    case 0x1F801DA6: return (u16)(xfer_addr >> 3);
    case 0x1F801D9C: case 0x1F801D9E: return 0xFFFF; /* ENDX: all voices ended */
    }
    if (a >= 0x1F801C00 && a < 0x1F801D80 && (a & 0xF) == 0xC) return 0; /* ADSR volume */
    return REG(a);
}

void spu_write(u32 a, u16 v) {
    REG(a) = v;
    switch (a) {
    case 0x1F801DA6: xfer_addr = (u32)v << 3; break;
    case 0x1F801DA8: /* manual transfer FIFO */
        spu_ram[xfer_addr & 0x7FFFF] = (u8)v;
        spu_ram[(xfer_addr + 1) & 0x7FFFF] = (u8)(v >> 8);
        xfer_addr = (xfer_addr + 2) & 0x7FFFF;
        break;
    }
}

void spu_dma_write(u32 w) {
    for (int i = 0; i < 4; i++) spu_ram[(xfer_addr + i) & 0x7FFFF] = (u8)(w >> (i * 8));
    xfer_addr = (xfer_addr + 4) & 0x7FFFF;
}

u32 spu_dma_read(void) {
    u32 w = 0;
    for (int i = 0; i < 4; i++) w |= (u32)spu_ram[(xfer_addr + i) & 0x7FFFF] << (i * 8);
    xfer_addr = (xfer_addr + 4) & 0x7FFFF;
    return w;
}

void xa_decode_sector(const u8* raw) { (void)raw; }
void spu_xa_push(const s16* s, int n, int st, int rate) { (void)s; (void)n; (void)st; (void)rate; }
void spu_render(s16* out, int frames) { memset(out, 0, (size_t)frames * 4); }
