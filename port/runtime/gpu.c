/* PS1 GPU: GP0/GP1 command processing and a software rasterizer into 1 MB of VRAM. */
#include "hw.h"
#include <stdlib.h>

u16 g_vram[512 * 1024];
#define VRAM(x, y) g_vram[(((y) & 511) << 10) | ((x) & 1023)]

/* draw state */
static u32 texpage;          /* E1 bits 0-13 */
static u32 tw_mask_x, tw_mask_y, tw_off_x, tw_off_y;
static int da_x1, da_y1, da_x2, da_y2;
static int off_x, off_y;
static int mask_set, mask_check;
static int dither;

/* display state */
static u32 disp_x, disp_y, disp_mode, disp_on;
static u32 hrange1 = 0x260, hrange2 = 0xC60, vrange1 = 0x10, vrange2 = 0x100;
static u32 dma_dir;
static u32 gpu_irq;
static int field;

/* command FIFO */
static u32 fifo[256];
static int fifo_n, fifo_need;
static int polyline;          /* 1 while receiving a polyline */
/* CPU->VRAM / VRAM->CPU transfers */
static struct { int active, x, y, w, h, cx, cy; } xfer_in, xfer_out;
static u32 gpuread_latch;

void gpu_init(void) {
    memset(g_vram, 0, sizeof(g_vram));
    da_x2 = 1023;
    da_y2 = 511;
    disp_on = 0;
}

/* ---------------- pixel plotting ---------------- */

static inline u16 blend(u16 back, u16 front, int mode) {
    int br = back & 31, bg = (back >> 5) & 31, bb = (back >> 10) & 31;
    int fr = front & 31, fg = (front >> 5) & 31, fb = (front >> 10) & 31;
    int r, g, b;
    switch (mode) {
    case 0: r = (br + fr) >> 1; g = (bg + fg) >> 1; b = (bb + fb) >> 1; break;
    case 1: r = br + fr; g = bg + fg; b = bb + fb; break;
    case 2: r = br - fr; g = bg - fg; b = bb - fb; break;
    default: r = br + (fr >> 2); g = bg + (fg >> 2); b = bb + (fb >> 2); break;
    }
    if (r > 31) r = 31; if (r < 0) r = 0;
    if (g > 31) g = 31; if (g < 0) g = 0;
    if (b > 31) b = 31; if (b < 0) b = 0;
    return (u16)(r | (g << 5) | (b << 10) | (front & 0x8000));
}

static inline void plot(int x, int y, u16 c, int semi, int semi_mode) {
    if (x < da_x1 || x > da_x2 || y < da_y1 || y > da_y2) return;
    u16* p = &VRAM(x, y);
    if (mask_check && (*p & 0x8000)) return;
    if (semi) c = blend(*p, c, semi_mode);
    *p = c | (mask_set ? 0x8000 : 0);
}

static const int dither_tab[4][4] = {
    {-4, 0, -3, 1}, {2, -2, 3, -1}, {-3, 1, -4, 0}, {3, -1, 2, -2}};

static inline u16 rgb24to15(int r, int g, int b, int x, int y, int dith) {
    if (dith) {
        int d = dither_tab[y & 3][x & 3];
        r += d; g += d; b += d;
        if (r < 0) r = 0; if (r > 255) r = 255;
        if (g < 0) g = 0; if (g > 255) g = 255;
        if (b < 0) b = 0; if (b > 255) b = 255;
    }
    return (u16)((r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10));
}

/* texture fetch: returns 0 for fully transparent texel */
typedef struct {
    int bx, by, depth, cx, cy;
} TexState;

static inline u16 tex_fetch(const TexState* t, int u, int v) {
    u = (u & ~(int)(tw_mask_x * 8)) | (int)((tw_off_x & tw_mask_x) * 8);
    v = (v & ~(int)(tw_mask_y * 8)) | (int)((tw_off_y & tw_mask_y) * 8);
    u &= 255;
    v &= 255;
    switch (t->depth) {
    case 0: {
        u16 w = VRAM(t->bx + (u >> 2), t->by + v);
        int idx = (w >> ((u & 3) * 4)) & 15;
        return VRAM(t->cx + idx, t->cy);
    }
    case 1: {
        u16 w = VRAM(t->bx + (u >> 1), t->by + v);
        int idx = (w >> ((u & 1) * 8)) & 255;
        return VRAM(t->cx + idx, t->cy);
    }
    default:
        return VRAM(t->bx + u, t->by + v);
    }
}

static inline u16 modulate(u16 tex, int r, int g, int b) {
    int tr = (tex & 31) * r >> 7, tg = ((tex >> 5) & 31) * g >> 7, tb = ((tex >> 10) & 31) * b >> 7;
    if (tr > 31) tr = 31;
    if (tg > 31) tg = 31;
    if (tb > 31) tb = 31;
    return (u16)(tr | (tg << 5) | (tb << 10) | (tex & 0x8000));
}

static void set_texstate(TexState* t, u32 tp, u32 clut) {
    t->bx = (tp & 15) * 64;
    t->by = ((tp >> 4) & 1) * 256;
    t->depth = (tp >> 7) & 3;
    if (t->depth == 3) t->depth = 2;
    t->cx = (clut & 0x3F) * 16;
    t->cy = (clut >> 6) & 0x1FF;
}

/* ---------------- triangles ---------------- */

typedef struct {
    int x, y;
    int r, g, b;
    int u, v;
} Vtx;

static inline s64 edge(const Vtx* a, const Vtx* b, int px, int py) {
    return (s64)(b->x - a->x) * (py - a->y) - (s64)(b->y - a->y) * (px - a->x);
}

/* top-left rule: a pixel on an edge is drawn only for top or left edges */
static inline int is_top_left(const Vtx* a, const Vtx* b) {
    return (a->y == b->y && b->x < a->x) || (b->y < a->y);
}

static void draw_triangle(Vtx v0, Vtx v1, Vtx v2, int gouraud, int textured, int raw,
                          int semi, int semi_mode, const TexState* ts) {
    s64 area = edge(&v0, &v1, v2.x, v2.y);
    if (area == 0) return;
    if (area < 0) { Vtx t = v1; v1 = v2; v2 = t; area = -area; }

    int minx = v0.x, maxx = v0.x, miny = v0.y, maxy = v0.y;
    if (v1.x < minx) minx = v1.x; if (v1.x > maxx) maxx = v1.x;
    if (v2.x < minx) minx = v2.x; if (v2.x > maxx) maxx = v2.x;
    if (v1.y < miny) miny = v1.y; if (v1.y > maxy) maxy = v1.y;
    if (v2.y < miny) miny = v2.y; if (v2.y > maxy) maxy = v2.y;
    if (maxx - minx >= 1024 || maxy - miny >= 512) return; /* hardware rejects these */
    if (minx < da_x1) minx = da_x1;
    if (miny < da_y1) miny = da_y1;
    if (maxx > da_x2) maxx = da_x2;
    if (maxy > da_y2) maxy = da_y2;
    if (minx > maxx || miny > maxy) return;

    int b0 = is_top_left(&v1, &v2) ? 0 : -1;
    int b1 = is_top_left(&v2, &v0) ? 0 : -1;
    int b2 = is_top_left(&v0, &v1) ? 0 : -1;
    int dith = dither && (gouraud || (textured && !raw));

    for (int y = miny; y <= maxy; y++) {
        s64 w0 = edge(&v1, &v2, minx, y);
        s64 w1 = edge(&v2, &v0, minx, y);
        s64 w2 = edge(&v0, &v1, minx, y);
        s64 d0 = -(s64)(v2.y - v1.y), d1 = -(s64)(v0.y - v2.y), d2 = -(s64)(v1.y - v0.y);
        for (int x = minx; x <= maxx; x++, w0 += d0, w1 += d1, w2 += d2) {
            if (w0 + b0 < 0 || w1 + b1 < 0 || w2 + b2 < 0) continue;
            int r = v0.r, g = v0.g, b = v0.b;
            if (gouraud) {
                r = (int)((w0 * v0.r + w1 * v1.r + w2 * v2.r) / area);
                g = (int)((w0 * v0.g + w1 * v1.g + w2 * v2.g) / area);
                b = (int)((w0 * v0.b + w1 * v1.b + w2 * v2.b) / area);
            }
            u16 c;
            int px_semi = semi;
            if (textured) {
                int u = (int)((w0 * v0.u + w1 * v1.u + w2 * v2.u) / area);
                int v = (int)((w0 * v0.v + w1 * v1.v + w2 * v2.v) / area);
                u16 t = tex_fetch(ts, u, v);
                if (t == 0) continue;
                px_semi = semi && (t & 0x8000);
                if (raw) c = t;
                else if (dith) {
                    int tr = (t & 31) * r >> 4, tg = ((t >> 5) & 31) * g >> 4, tb = ((t >> 10) & 31) * b >> 4;
                    c = rgb24to15(tr > 255 ? 255 : tr, tg > 255 ? 255 : tg, tb > 255 ? 255 : tb, x, y, 1) | (t & 0x8000);
                } else c = modulate(t, r, g, b);
            } else {
                c = rgb24to15(r, g, b, x, y, dith);
            }
            plot(x, y, c, px_semi, semi_mode);
        }
    }
}

/* ---------------- lines and rectangles ---------------- */

static void draw_line(Vtx a, Vtx b, int gouraud, int semi, int semi_mode) {
    int dx = b.x - a.x, dy = b.y - a.y;
    if (abs(dx) >= 1024 || abs(dy) >= 512) return;
    int n = abs(dx) > abs(dy) ? abs(dx) : abs(dy);
    for (int i = 0; i <= n; i++) {
        int x = n ? a.x + dx * i / n : a.x;
        int y = n ? a.y + dy * i / n : a.y;
        int r = a.r, g = a.g, bl = a.b;
        if (gouraud && n) {
            r = a.r + (b.r - a.r) * i / n;
            g = a.g + (b.g - a.g) * i / n;
            bl = a.b + (b.b - a.b) * i / n;
        }
        plot(x, y, rgb24to15(r, g, bl, x, y, dither && gouraud), semi, semi_mode);
    }
}

static void draw_rect(int x, int y, int w, int h, u32 color, int textured, int raw, int semi,
                      int semi_mode, int u0, int v0, const TexState* ts) {
    int r = color & 255, g = (color >> 8) & 255, b = (color >> 16) & 255;
    int flipx = (texpage >> 12) & 1, flipy = (texpage >> 13) & 1;
    u16 flat = rgb24to15(r, g, b, 0, 0, 0);
    for (int j = 0; j < h; j++) {
        int py = y + j;
        if (py < da_y1 || py > da_y2) continue;
        for (int i = 0; i < w; i++) {
            int px = x + i;
            if (px < da_x1 || px > da_x2) continue;
            if (textured) {
                int u = flipx ? u0 - i : u0 + i;
                int v = flipy ? v0 - j : v0 + j;
                u16 t = tex_fetch(ts, u, v);
                if (t == 0) continue;
                plot(px, py, raw ? t : modulate(t, r, g, b), semi && (t & 0x8000), semi_mode);
            } else {
                plot(px, py, flat, semi, semi_mode);
            }
        }
    }
}

/* ---------------- command decoding ---------------- */

static inline int sx11(u32 v) { return ((s32)(v << 21)) >> 21; }

static int cmd_length(u32 cmd) {
    u32 op = cmd >> 24;
    if (op >= 0x20 && op < 0x40) {
        int quad = op & 8, gour = op & 0x10, tex = op & 4;
        int verts = quad ? 4 : 3;
        int per = 1 + (tex ? 1 : 0) + (gour ? 1 : 0);
        return 1 + verts * per - (gour ? 1 : 0);
    }
    if (op >= 0x40 && op < 0x60) {
        if (op & 8) return -1; /* polyline: variable */
        return (op & 0x10) ? 4 : 3;
    }
    if (op >= 0x60 && op < 0x80) {
        int len = 2 + ((op & 4) ? 1 : 0);
        if (((op >> 3) & 3) == 0) len++;
        return len;
    }
    if (op >= 0x80 && op < 0xA0) return 4;
    if (op >= 0xA0 && op < 0xC0) return 3;
    if (op >= 0xC0 && op < 0xE0) return 3;
    if (op == 0x02) return 3;
    return 1;
}

static void exec_polygon(void) {
    u32 op = fifo[0] >> 24;
    int quad = op & 8, gour = op & 0x10, tex = op & 4, semi = op & 2, raw = op & 1;
    int nv = quad ? 4 : 3;
    Vtx v[4];
    /* layout: color0 xy0 [uv0] {color_i (gouraud only) xy_i [uv_i]}... */
    int idx = 1;
    u32 clut = 0, tp = texpage;
    for (int i = 0; i < nv; i++) {
        u32 col = (i > 0 && gour) ? fifo[idx++] : fifo[0];
        u32 xy = fifo[idx++];
        v[i].r = col & 255;
        v[i].g = (col >> 8) & 255;
        v[i].b = (col >> 16) & 255;
        v[i].x = sx11(xy) + off_x;
        v[i].y = sx11(xy >> 16) + off_y;
        if (tex) {
            u32 uv = fifo[idx++];
            v[i].u = uv & 255;
            v[i].v = (uv >> 8) & 255;
            if (i == 0) clut = uv >> 16;
            if (i == 1) tp = uv >> 16;
        } else {
            v[i].u = v[i].v = 0;
        }
    }
    TexState ts;
    int semi_mode = (texpage >> 5) & 3;
    if (tex) {
        texpage = (texpage & ~0x9FFu) | (tp & 0x9FF);
        semi_mode = (tp >> 5) & 3;
        set_texstate(&ts, tp, clut);
        if (raw) for (int i = 0; i < nv; i++) v[i].r = v[i].g = v[i].b = 128;
    }
    draw_triangle(v[0], v[1], v[2], gour, tex, raw, semi, semi_mode, &ts);
    if (quad) draw_triangle(v[1], v[2], v[3], gour, tex, raw, semi, semi_mode, &ts);
}

static void exec_rect(void) {
    u32 op = fifo[0] >> 24;
    int tex = op & 4, semi = op & 2, raw = op & 1;
    int idx = 1;
    u32 xy = fifo[idx++];
    int x = sx11(xy) + off_x, y = sx11(xy >> 16) + off_y;
    int u = 0, v = 0;
    u32 clut = 0;
    if (tex) {
        u32 uv = fifo[idx++];
        u = uv & 255;
        v = (uv >> 8) & 255;
        clut = uv >> 16;
    }
    int w, h;
    switch ((op >> 3) & 3) {
    case 0: w = fifo[idx] & 0x3FF; h = (fifo[idx] >> 16) & 0x1FF; break;
    case 1: w = h = 1; break;
    case 2: w = h = 8; break;
    default: w = h = 16; break;
    }
    TexState ts;
    if (tex) set_texstate(&ts, texpage, clut);
    draw_rect(x, y, w, h, fifo[0], tex, raw, semi, (texpage >> 5) & 3, u, v, &ts);
}

static void exec_line(void) {
    u32 op = fifo[0] >> 24;
    int gour = op & 0x10, semi = op & 2;
    Vtx a, b;
    a.r = fifo[0] & 255; a.g = (fifo[0] >> 8) & 255; a.b = (fifo[0] >> 16) & 255;
    a.x = sx11(fifo[1]) + off_x; a.y = sx11(fifo[1] >> 16) + off_y;
    u32 c2 = gour ? fifo[2] : fifo[0];
    u32 xy2 = gour ? fifo[3] : fifo[2];
    b.r = c2 & 255; b.g = (c2 >> 8) & 255; b.b = (c2 >> 16) & 255;
    b.x = sx11(xy2) + off_x; b.y = sx11(xy2 >> 16) + off_y;
    draw_line(a, b, gour, semi, (texpage >> 5) & 3);
}

/* polyline state */
static Vtx pl_last;
static u32 pl_color;
static int pl_gour, pl_semi, pl_expect_color, pl_have_first;

static void polyline_word(u32 w) {
    if ((w & 0xF000F000) == 0x50005000 && pl_have_first) { polyline = 0; return; }
    if (pl_gour && pl_expect_color) { pl_color = w; pl_expect_color = 0; return; }
    Vtx v;
    v.r = pl_color & 255; v.g = (pl_color >> 8) & 255; v.b = (pl_color >> 16) & 255;
    v.x = sx11(w) + off_x; v.y = sx11(w >> 16) + off_y;
    if (pl_have_first) draw_line(pl_last, v, pl_gour, pl_semi, (texpage >> 5) & 3);
    pl_last = v;
    pl_have_first = 1;
    pl_expect_color = 1;
}

static void vram_fill(void) {
    u32 c = fifo[0];
    u16 col = rgb24to15(c & 255, (c >> 8) & 255, (c >> 16) & 255, 0, 0, 0);
    int x = fifo[1] & 0x3F0, y = (fifo[1] >> 16) & 0x1FF;
    int w = ((fifo[2] & 0x3FF) + 15) & ~15, h = (fifo[2] >> 16) & 0x1FF;
    for (int j = 0; j < h; j++)
        for (int i = 0; i < w; i++) VRAM(x + i, y + j) = col;
}

static void vram_copy(void) {
    int sx = fifo[1] & 0x3FF, sy = (fifo[1] >> 16) & 0x1FF;
    int dx = fifo[2] & 0x3FF, dy = (fifo[2] >> 16) & 0x1FF;
    int w = ((fifo[3] - 1) & 0x3FF) + 1, h = (((fifo[3] >> 16) - 1) & 0x1FF) + 1;
    static u16 line[1024];
    for (int j = 0; j < h; j++) {
        for (int i = 0; i < w; i++) line[i] = VRAM(sx + i, sy + j);
        for (int i = 0; i < w; i++) {
            u16* p = &VRAM(dx + i, dy + j);
            if (mask_check && (*p & 0x8000)) continue;
            *p = line[i] | (mask_set ? 0x8000 : 0);
        }
    }
}

static void exec_env(u32 w) {
    switch (w >> 24) {
    case 0xE1:
        texpage = w & 0x3FFF;
        dither = (w >> 9) & 1;
        break;
    case 0xE2:
        tw_mask_x = w & 31;
        tw_mask_y = (w >> 5) & 31;
        tw_off_x = (w >> 10) & 31;
        tw_off_y = (w >> 15) & 31;
        break;
    case 0xE3: da_x1 = w & 0x3FF; da_y1 = (w >> 10) & 0x1FF; break;
    case 0xE4: da_x2 = w & 0x3FF; da_y2 = (w >> 10) & 0x1FF; break;
    case 0xE5: off_x = sx11(w); off_y = sx11(w >> 11); break;
    case 0xE6: mask_set = w & 1; mask_check = (w >> 1) & 1; break;
    }
}

static void exec_fifo(void) {
    u32 op = fifo[0] >> 24;
    if (op >= 0x20 && op < 0x40) exec_polygon();
    else if (op >= 0x40 && op < 0x60) exec_line();
    else if (op >= 0x60 && op < 0x80) exec_rect();
    else if (op >= 0x80 && op < 0xA0) vram_copy();
    else if (op >= 0xA0 && op < 0xC0) {
        xfer_in.x = fifo[1] & 0x3FF;
        xfer_in.y = (fifo[1] >> 16) & 0x1FF;
        xfer_in.w = ((fifo[2] - 1) & 0x3FF) + 1;
        xfer_in.h = (((fifo[2] >> 16) - 1) & 0x1FF) + 1;
        xfer_in.cx = xfer_in.cy = 0;
        xfer_in.active = 1;
    } else if (op >= 0xC0 && op < 0xE0) {
        xfer_out.x = fifo[1] & 0x3FF;
        xfer_out.y = (fifo[1] >> 16) & 0x1FF;
        xfer_out.w = ((fifo[2] - 1) & 0x3FF) + 1;
        xfer_out.h = (((fifo[2] >> 16) - 1) & 0x1FF) + 1;
        xfer_out.cx = xfer_out.cy = 0;
        xfer_out.active = 1;
    } else if (op == 0x02) vram_fill();
    else if (op == 0x1F) gpu_irq = 1;
    else if (op >= 0xE1 && op <= 0xE6) exec_env(fifo[0]);
}

static void xfer_in_pixel(u16 px) {
    u16* p = &VRAM(xfer_in.x + xfer_in.cx, xfer_in.y + xfer_in.cy);
    if (!(mask_check && (*p & 0x8000))) *p = px | (mask_set ? 0x8000 : 0);
    if (++xfer_in.cx >= xfer_in.w) {
        xfer_in.cx = 0;
        if (++xfer_in.cy >= xfer_in.h) xfer_in.active = 0;
    }
}

u32 g_stat_gp0;
void gpu_gp0(u32 w) {
    g_stat_gp0++;
    if (xfer_in.active) {
        xfer_in_pixel((u16)w);
        if (xfer_in.active) xfer_in_pixel((u16)(w >> 16));
        return;
    }
    if (polyline) { polyline_word(w); return; }
    if (fifo_n == 0) {
        u32 op = w >> 24;
        if (op >= 0x40 && op < 0x60 && (op & 8)) {
            polyline = 1;
            pl_gour = op & 0x10;
            pl_semi = op & 2;
            pl_color = w;
            pl_have_first = 0;
            pl_expect_color = 0;
            return;
        }
        fifo_need = cmd_length(w);
    }
    fifo[fifo_n++] = w;
    if (fifo_n >= fifo_need) {
        exec_fifo();
        fifo_n = 0;
    }
}

void gpu_gp1(u32 v) {
    switch (v >> 24) {
    case 0x00:
        fifo_n = 0; polyline = 0; xfer_in.active = xfer_out.active = 0;
        texpage = 0; disp_on = 0; gpu_irq = 0; dma_dir = 0;
        da_x1 = da_y1 = 0; da_x2 = 1023; da_y2 = 511; off_x = off_y = 0;
        mask_set = mask_check = 0;
        disp_mode = 0;
        break;
    case 0x01: fifo_n = 0; polyline = 0; xfer_in.active = 0; break;
    case 0x02: gpu_irq = 0; break;
    case 0x03: disp_on = !(v & 1); LOG("gpu: display %s", disp_on ? "on" : "off"); break;
    case 0x04: dma_dir = v & 3; break;
    case 0x05: disp_x = v & 0x3FE; disp_y = (v >> 10) & 0x1FF; break;
    case 0x06: hrange1 = v & 0xFFF; hrange2 = (v >> 12) & 0xFFF; break;
    case 0x07: vrange1 = v & 0x3FF; vrange2 = (v >> 10) & 0x3FF; break;
    case 0x08: disp_mode = v & 0xFF; break;
    case 0x10:
        switch (v & 7) {
        case 2: gpuread_latch = (tw_mask_x) | (tw_mask_y << 5) | (tw_off_x << 10) | (tw_off_y << 15); break;
        case 3: gpuread_latch = da_x1 | (da_y1 << 10); break;
        case 4: gpuread_latch = da_x2 | (da_y2 << 10); break;
        case 5: gpuread_latch = (off_x & 0x7FF) | ((off_y & 0x7FF) << 11); break;
        case 7: gpuread_latch = 2; break;
        }
        break;
    }
}

u32 gpu_read(void) {
    if (!xfer_out.active) return gpuread_latch;
    u32 w = 0;
    for (int k = 0; k < 2 && xfer_out.active; k++) {
        w |= (u32)VRAM(xfer_out.x + xfer_out.cx, xfer_out.y + xfer_out.cy) << (16 * k);
        if (++xfer_out.cx >= xfer_out.w) {
            xfer_out.cx = 0;
            if (++xfer_out.cy >= xfer_out.h) xfer_out.active = 0;
        }
    }
    return w;
}

u32 gpu_stat(void) {
    u32 s = (texpage & 0x7FF) | (mask_set << 11) | (mask_check << 12) | (1u << 13);
    s |= ((disp_mode >> 6) & 1) << 16;
    s |= (disp_mode & 3) << 17;
    s |= ((disp_mode >> 2) & 1) << 19;
    s |= ((disp_mode >> 3) & 1) << 20;
    s |= ((disp_mode >> 4) & 1) << 21;
    s |= ((disp_mode >> 5) & 1) << 22;
    s |= (disp_on ? 0 : 1u) << 23;
    s |= gpu_irq << 24;
    s |= 0x1C000000; /* ready for commands, VRAM->CPU and DMA */
    if (dma_dir) s |= 1u << 25;
    s |= dma_dir << 29;
    s |= (u32)field << 31;
    return s;
}

void gpu_vblank(void) { field ^= 1; }

int gpu_get_display(u32* out, int* pw, int* ph, int max_w, int max_h) {
    static const int widths[4] = {256, 320, 512, 640};
    int w = (disp_mode & 0x40) ? 368 : widths[disp_mode & 3];
    int h = ((disp_mode & 0x24) == 0x24) ? 480 : 240;
    int vis_h = (int)(vrange2 - vrange1);
    if (h == 480) vis_h *= 2;
    if (vis_h > 0 && vis_h < h) h = vis_h;
    if (w > max_w) w = max_w;
    if (h > max_h) h = max_h;
    *pw = w;
    *ph = h;
    if (!disp_on) {
        memset(out, 0, (size_t)w * h * 4);
        return 0;
    }
    int is24 = (disp_mode >> 4) & 1;
    for (int y = 0; y < h; y++) {
        u32* row = out + (size_t)y * w;
        int vy = (int)disp_y + y;
        if (is24) {
            const u8* src = (const u8*)&VRAM(0, vy);
            for (int x = 0; x < w; x++) {
                int bo = (int)disp_x * 2 + x * 3;
                u8 r = src[(bo) & 2047], g = src[(bo + 1) & 2047], b = src[(bo + 2) & 2047];
                row[x] = (r << 16) | (g << 8) | b;
            }
        } else {
            for (int x = 0; x < w; x++) {
                u16 p = VRAM(disp_x + x, vy);
                u32 r = (p & 31) << 3, g = ((p >> 5) & 31) << 3, b = ((p >> 10) & 31) << 3;
                row[x] = (r << 16) | (g << 8) | b | ((r >> 5) << 16) | ((g >> 5) << 8) | (b >> 5);
            }
        }
    }
    return 1;
}
