/* MDEC: macroblock decoder used by the FMV player (libpress DecDCTin/DecDCTout). */
#include "hw.h"
#include <stdlib.h>

static u8 qt_y[64], qt_c[64];
static s16 scale[64];

static u32 cmd;               /* current command word */
static u32 params_left;       /* words still expected for the command */
static u16* in_buf;           /* decode input (halfwords) */
static u32 in_n, in_cap;
static int qt_idx, sc_idx;
static u32* out_buf;          /* decoded output words */
static u32 out_n, out_pos, out_cap;
static int busy;

static const u8 zigzag[64] = {
    0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
    12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63};

void mdec_init(void) {
    cmd = 0;
    params_left = 0;
    in_n = out_n = out_pos = 0;
}

static void out_push(u32 w) {
    if (out_n >= out_cap) {
        out_cap = out_cap ? out_cap * 2 : 65536;
        out_buf = realloc(out_buf, out_cap * 4);
    }
    out_buf[out_n++] = w;
}

static inline int sx10(u16 v) { return ((s32)((u32)v << 22)) >> 22; }

/* decode one 8x8 block; returns 0 at end of data */
static int rl_decode_block(s16* blk, const u8* qt, u32* pos) {
    memset(blk, 0, 64 * sizeof(s16));
    /* skip padding */
    while (*pos < in_n && in_buf[*pos] == 0xFE00) (*pos)++;
    if (*pos >= in_n) return 0;
    u16 n = in_buf[(*pos)++];
    int q = n >> 10;
    int k = 0;
    int val = q == 0 ? sx10(n) * 2 : sx10(n) * qt[0];
    for (;;) {
        if (val < -0x400) val = -0x400;
        if (val > 0x3FF) val = 0x3FF;
        if (q == 0) blk[k] = (s16)val;
        else blk[zigzag[k]] = (s16)val;
        if (*pos >= in_n) break;
        n = in_buf[(*pos)++];
        if (n == 0xFE00) break;
        k += (n >> 10) + 1;
        if (k > 63) break;
        val = q == 0 ? sx10(n) * 2 : (sx10(n) * qt[k] * q + 4) / 8;
    }
    return 1;
}

static void idct(s16* blk) {
    s64 tmp[64];
    for (int x = 0; x < 8; x++)
        for (int y = 0; y < 8; y++) {
            s64 sum = 0;
            for (int u = 0; u < 8; u++) sum += (s64)blk[u * 8 + x] * scale[u * 8 + y];
            tmp[x + y * 8] = sum;
        }
    for (int x = 0; x < 8; x++)
        for (int y = 0; y < 8; y++) {
            s64 sum = 0;
            for (int u = 0; u < 8; u++) sum += tmp[u + y * 8] * scale[u * 8 + x];
            s32 v = (s32)((sum >> 32) + ((sum >> 31) & 1));
            v = ((s32)((u32)v << 23)) >> 23; /* 9-bit signed */
            if (v < -128) v = -128;
            if (v > 127) v = 127;
            blk[x + y * 8] = (s16)v;
        }
}

static void yuv_to_rgb(const s16* cr, const s16* cb, const s16 y[4][64], int depth, int is_signed, int set15) {
    u8 rgb[16 * 16 * 3];
    for (int py = 0; py < 16; py++)
        for (int px = 0; px < 16; px++) {
            int bi = (py >= 8 ? 2 : 0) + (px >= 8 ? 1 : 0);
            int Y = y[bi][(py & 7) * 8 + (px & 7)];
            int ci = (py >> 1) * 8 + (px >> 1);
            int R = cr[ci], B = cb[ci];
            /* R = Y + 1.402 Cr, G = Y - 0.3437 Cb - 0.7143 Cr, B = Y + 1.772 Cb */
            int r = Y + ((1436 * R) >> 10);
            int g = Y + ((-352 * B - 731 * R) >> 10);
            int b = Y + ((1815 * B) >> 10);
            if (r < -128) r = -128; if (r > 127) r = 127;
            if (g < -128) g = -128; if (g > 127) g = 127;
            if (b < -128) b = -128; if (b > 127) b = 127;
            if (!is_signed) { r += 128; g += 128; b += 128; }
            u8* p = &rgb[(py * 16 + px) * 3];
            p[0] = (u8)r; p[1] = (u8)g; p[2] = (u8)b;
        }
    if (depth == 2) { /* 24-bit: pack bytes into words */
        u32 w = 0;
        int nb = 0;
        for (int i = 0; i < 16 * 16 * 3; i++) {
            w |= (u32)rgb[i] << (nb * 8);
            if (++nb == 4) { out_push(w); w = 0; nb = 0; }
        }
    } else { /* 15-bit */
        for (int i = 0; i < 256; i += 2) {
            u32 h[2];
            for (int k = 0; k < 2; k++) {
                const u8* p = &rgb[(i + k) * 3];
                h[k] = (p[0] >> 3) | ((p[1] >> 3) << 5) | ((p[2] >> 3) << 10) | (set15 ? 0x8000 : 0);
            }
            out_push(h[0] | (h[1] << 16));
        }
    }
}

u32 g_stat_mdec;
static void decode_all(void) {
    g_stat_mdec++;
    int depth = (cmd >> 27) & 3;
    int is_signed = (cmd >> 26) & 1;
    int set15 = (cmd >> 25) & 1;
    u32 pos = 0;
    out_n = out_pos = 0;
    if (depth >= 2) {
        s16 cr[64], cb[64], y[4][64];
        for (;;) {
            if (!rl_decode_block(cr, qt_c, &pos)) break;
            if (!rl_decode_block(cb, qt_c, &pos)) break;
            int ok = 1;
            for (int i = 0; i < 4 && ok; i++) ok = rl_decode_block(y[i], qt_y, &pos);
            if (!ok) break;
            idct(cr); idct(cb);
            for (int i = 0; i < 4; i++) idct(y[i]);
            yuv_to_rgb(cr, cb, (const s16 (*)[64])y, depth, is_signed, set15);
        }
    } else {
        s16 blk[64];
        while (rl_decode_block(blk, qt_y, &pos)) {
            idct(blk);
            if (depth == 1) {
                for (int i = 0; i < 64; i += 4) {
                    u32 w = 0;
                    for (int k = 0; k < 4; k++) w |= (u32)(u8)(blk[i + k] + (is_signed ? 0 : 128)) << (k * 8);
                    out_push(w);
                }
            } else {
                for (int i = 0; i < 64; i += 8) {
                    u32 w = 0;
                    for (int k = 0; k < 8; k++) w |= (u32)(((blk[i + k] + (is_signed ? 0 : 128)) >> 4) & 15) << (k * 4);
                    out_push(w);
                }
            }
        }
    }
    in_n = 0;
}

static void data_word(u32 w) {
    u32 op = cmd >> 29;
    if (!params_left) return;
    params_left--;
    if (op == 1) {
        if (in_n + 2 > in_cap) {
            in_cap = in_cap ? in_cap * 2 : 65536;
            in_buf = realloc(in_buf, in_cap * 2);
        }
        in_buf[in_n++] = (u16)w;
        in_buf[in_n++] = (u16)(w >> 16);
        if (!params_left) decode_all();
    } else if (op == 2) {
        for (int k = 0; k < 4; k++) {
            u8 b = (u8)(w >> (k * 8));
            if (qt_idx < 64) qt_y[qt_idx] = b;
            else if (qt_idx < 128) qt_c[qt_idx - 64] = b;
            qt_idx++;
        }
    } else if (op == 3) {
        if (sc_idx < 64) scale[sc_idx++] = (s16)w;
        if (sc_idx < 64) scale[sc_idx++] = (s16)(w >> 16);
    }
    busy = params_left != 0;
}

void mdec_write(u32 addr, u32 v) {
    if (addr == 0x1F801824) {
        if (v & 0x80000000u) { cmd = 0; params_left = 0; in_n = out_n = out_pos = 0; busy = 0; }
        return;
    }
    if (params_left) { data_word(v); return; }
    cmd = v;
    switch (v >> 29) {
    case 1: params_left = v & 0xFFFF; in_n = 0; break;
    case 2: params_left = (v & 1) ? 32 : 16; qt_idx = 0; break;
    case 3: params_left = 32; sc_idx = 0; break;
    default: params_left = 0; break;
    }
    busy = params_left != 0;
}

u32 mdec_read(u32 addr) {
    if (addr == 0x1F801820) return mdec_dma_out();
    u32 s = 0;
    if (out_pos >= out_n) s |= 1u << 31;
    if (busy) s |= (1u << 29) | (1u << 28);
    if (out_pos < out_n) s |= 1u << 27;
    s |= ((cmd >> 25) & 0xF) << 23;
    s |= (params_left - 1) & 0xFFFF;
    return s;
}

void mdec_dma_in(u32 w) { data_word(w); }

u32 mdec_dma_out(void) { return out_pos < out_n ? out_buf[out_pos++] : 0; }

int mdec_out_ready(void) { return out_pos < out_n; }
