/* Enhanced renderer: draws every primitive a second time into high-resolution
 * "surfaces" (one per on-screen framebuffer) for higher internal resolution and
 * widescreen. Native VRAM rendering stays authoritative for everything the game
 * reads back (textures, render-to-texture, StoreImage); surfaces are only displayed.
 *
 * Widescreen: the GTE squeezes projected X towards the screen centre (gte.c), and
 * primitives built from GTE vertices are stretched back over the full wide surface,
 * so the 3D view gets wider without distortion. 2D elements (HUD, menus) keep their
 * 4:3 shape in the centre. In frames with 3D, 2D elements covering the whole width
 * (fades, overlays) are stretched; frames without 3D (menus) stay entirely 4:3.
 *
 * Primitives are queued and rasterized by a pool of threads, each owning a band of
 * surface rows; the queue is flushed before anything reads or replaces surface
 * contents (presentation, uploads, copies).
 *
 * Included from gpu.c (shares its static state). */
#include "config.h"
#include <math.h>
#include <stdlib.h>
#include <SDL.h>

#define HD_MAX_SURF 4

typedef struct {
    int ox, oy, w, h;   /* native VRAM rectangle */
    int W, H;           /* surface pixels */
    u32* px;            /* 0x00RRGGBB, bit 24 = mask bit */
} HdSurf;

static HdSurf hd_surf[HD_MAX_SURF];
static int hd_nsurf;
static int hd_scale = 1;     /* vertical (and narrow horizontal) scale */
static float hd_wide = 1.0f; /* extra horizontal factor for widescreen */
static int hd_enabled;
static int hd_mode3d;        /* previous game frame contained 3D geometry */

int g_gte_ws_num = 1, g_gte_ws_den = 1;

static void hd_flush(void);

/* ---------------- GTE vertex tags (3D detection + sub-pixel positions) ---------------- */

u32 g_stat_rtp, g_stat_poly, g_stat_poly_tagged, g_stat_vtx_tagged;

#define TAG_SLOTS (1 << 16)
typedef struct {
    u32 key;   /* xy key | generation << 22 */
    float fx, fy;
} Tag;
static Tag tags[TAG_SLOTS];
static u32 tag_gen = 1;

static inline u32 tag_key(int x, int y) { return ((u32)x & 0x7FF) | (((u32)y & 0x7FF) << 11); }

void gte_tag_vertex(int x, int y, float fx, float fy) {
    if (!hd_enabled) return;
    g_stat_rtp++;
    u32 k = tag_key(x, y);
    u32 h = (k * 2654435761u) >> 16;
    for (int i = 0; i < 8; i++) {
        Tag* t = &tags[(h + i) & (TAG_SLOTS - 1)];
        u32 gen = t->key >> 22;
        if ((t->key & 0x3FFFFF) == k || (gen != tag_gen && gen != ((tag_gen - 1) & 0x3FF))) {
            t->key = k | (tag_gen << 22);
            t->fx = fx;
            t->fy = fy;
            return;
        }
    }
}

static int tag_lookup(u32 xyword, float* fx, float* fy) {
    int x = sx11(xyword), y = sx11(xyword >> 16);
    u32 k = tag_key(x, y);
    u32 h = (k * 2654435761u) >> 16;
    for (int i = 0; i < 8; i++) {
        Tag* t = &tags[(h + i) & (TAG_SLOTS - 1)];
        u32 gen = t->key >> 22;
        if ((t->key & 0x3FFFFF) == k && (gen == tag_gen || gen == ((tag_gen - 1) & 0x3FF))) {
            /* only trust the precise value when it rounds to the same pixel */
            if (fabsf(t->fx - x) < 1.0f && fabsf(t->fy - y) < 1.0f) { *fx = t->fx; *fy = t->fy; }
            else { *fx = (float)x; *fy = (float)y; }
            return 1;
        }
    }
    return 0;
}

/* called on every framebuffer flip, i.e. once per game frame */
static void hd_new_frame(void) {
    if (g_verbose && g_stat_poly)
        log_msg("hd: rtp=%u polys=%u wide=%u vtx_tagged=%u", g_stat_rtp, g_stat_poly, g_stat_poly_tagged, g_stat_vtx_tagged);
    hd_mode3d = g_stat_poly_tagged >= 30;
    g_stat_rtp = g_stat_poly = g_stat_poly_tagged = g_stat_vtx_tagged = 0;
    tag_gen = (tag_gen + 1) & 0x3FF;
    if (!tag_gen) tag_gen = 1;
}

/* ---------------- surfaces ---------------- */

static inline u32 c15to24(u16 p) {
    u32 r = (p & 31) << 3, g = ((p >> 5) & 31) << 3, b = ((p >> 10) & 31) << 3;
    return (r | (r >> 5)) << 16 | (g | (g >> 5)) << 8 | (b | (b >> 5)) | ((u32)(p >> 15) << 24);
}

/* map native coordinates to surface pixels; wide = stretched mapping */
static inline float map_x(const HdSurf* s, float x, int wide) {
    float k = wide ? hd_scale * hd_wide : (float)hd_scale;
    return s->W * 0.5f + (x - s->ox - s->w * 0.5f) * k;
}
static inline float map_y(const HdSurf* s, float y) { return (y - s->oy) * hd_scale; }

static void hd_upload_surf(HdSurf* s, int x, int y, int w, int h);

static HdSurf* hd_get_surface(int ox, int oy, int w, int h, int create) {
    for (int i = 0; i < hd_nsurf; i++)
        if (hd_surf[i].ox == ox && hd_surf[i].oy == oy && hd_surf[i].w == w && hd_surf[i].h == h) return &hd_surf[i];
    if (!create) return NULL;
    hd_flush();
    /* reuse a surface overlapping this one, or append, or recycle the first */
    int slot = -1;
    for (int i = 0; i < hd_nsurf; i++) {
        HdSurf* o = &hd_surf[i];
        if (ox < o->ox + o->w && o->ox < ox + w && oy < o->oy + o->h && o->oy < oy + h) { slot = i; break; }
    }
    if (slot < 0) slot = hd_nsurf < HD_MAX_SURF ? hd_nsurf++ : 0;
    HdSurf* s = &hd_surf[slot];
    free(s->px);
    s->ox = ox; s->oy = oy; s->w = w; s->h = h;
    s->W = (int)(w * hd_scale * hd_wide + 0.5f);
    s->H = h * hd_scale;
    s->px = calloc((size_t)s->W * s->H, 4);
    hd_upload_surf(s, ox, oy, w, h);
    return s;
}

static void hd_threads_init(void);

void gpu_hd_configure(void) {
    hd_flush();
    for (int i = 0; i < hd_nsurf; i++) { free(hd_surf[i].px); hd_surf[i].px = NULL; }
    hd_nsurf = 0;
    hd_scale = g_cfg.render_scale;
    float aspect = config_aspect_ratio();
    hd_wide = aspect / (4.0f / 3.0f);
    hd_enabled = hd_scale > 1 || g_cfg.aspect != ASPECT_4_3;
    /* GTE horizontal squeeze for widescreen: 4:3 / target */
    switch (g_cfg.aspect) {
    case ASPECT_16_9: g_gte_ws_num = 3; g_gte_ws_den = 4; break;
    case ASPECT_21_9: g_gte_ws_num = 4; g_gte_ws_den = 7; break;
    default: g_gte_ws_num = 1; g_gte_ws_den = 1; break;
    }
    if (hd_enabled) hd_threads_init();
}

/* copy a native VRAM rectangle into the surface (4:3 mapping; stretched when it spans
 * the whole width of a 3D frame) */
static void hd_upload_surf(HdSurf* s, int x, int y, int w, int h) {
    int x0 = x > s->ox ? x : s->ox, y0 = y > s->oy ? y : s->oy;
    int x1 = x + w < s->ox + s->w ? x + w : s->ox + s->w, y1 = y + h < s->oy + s->h ? y + h : s->oy + s->h;
    if (x0 >= x1 || y0 >= y1) return;
    int wide = hd_mode3d && x0 <= s->ox && x1 >= s->ox + s->w;
    float k = wide ? hd_scale * hd_wide : (float)hd_scale;
    int X0 = (int)floorf(map_x(s, (float)x0, wide)), X1 = (int)ceilf(map_x(s, (float)x1, wide));
    if (X0 < 0) X0 = 0;
    if (X1 > s->W) X1 = s->W;
    for (int Y = (int)map_y(s, (float)y0); Y < (int)map_y(s, (float)y1) && Y < s->H; Y++) {
        int vy = s->oy + Y / hd_scale;
        u32* row = s->px + (size_t)Y * s->W;
        for (int X = X0; X < X1; X++) {
            int vx = s->ox + (int)floorf((X + 0.5f - s->W * 0.5f) / k + s->w * 0.5f);
            if (vx < x0 || vx >= x1) continue;
            row[X] = c15to24(VRAM(vx, vy));
        }
    }
}

static void hd_upload(int x, int y, int w, int h) {
    if (!hd_nsurf) return;
    hd_flush();
    for (int i = 0; i < hd_nsurf; i++) hd_upload_surf(&hd_surf[i], x, y, w, h);
}

/* ---------------- queued commands ---------------- */

enum { HC_TRI, HC_RECT, HC_FILL };

typedef struct {
    float x, y, r, g, b, u, v;
} HV;

typedef struct {
    int bx, by, depth, cx, cy;      /* texture page / CLUT */
    int twmx, twmy, twox, twoy;     /* texture window */
} HdTex;

typedef struct {
    u8 type, surf, gour, tex, raw, semi, mode, mset, mcheck, flipx, flipy;
    int x0, y0, x1, y1;             /* clip (TRI) or covered area (RECT/FILL), exclusive */
    union {
        struct { HV a, b, c; } tri;
        struct { float X0, Y0, kx; int u0, v0; u32 color; } rect;
        struct { u32 color; } fill;
    };
    HdTex t;
} HdCmd;

static HdCmd* q;
static int qn, qcap;

static HdCmd* hd_push(void) {
    if (qn >= qcap) {
        if (qcap >= 65536) hd_flush();
        else {
            qcap = qcap ? qcap * 2 : 4096;
            q = realloc(q, sizeof(HdCmd) * (size_t)qcap);
        }
    }
    return &q[qn++];
}

static void hd_capture_tex(HdTex* t, const TexState* ts) {
    t->bx = ts->bx; t->by = ts->by; t->depth = ts->depth; t->cx = ts->cx; t->cy = ts->cy;
    t->twmx = (int)tw_mask_x * 8; t->twmy = (int)tw_mask_y * 8;
    t->twox = (int)((tw_off_x & tw_mask_x) * 8); t->twoy = (int)((tw_off_y & tw_mask_y) * 8);
}

/* ---------------- pixel ops (run on worker threads) ---------------- */

static inline u16 hd_tex_fetch(const HdTex* t, int u, int v) {
    u = ((u & ~t->twmx) | t->twox) & 255;
    v = ((v & ~t->twmy) | t->twoy) & 255;
    switch (t->depth) {
    case 0: {
        u16 w = VRAM(t->bx + (u >> 2), t->by + v);
        return VRAM(t->cx + ((w >> ((u & 3) * 4)) & 15), t->cy);
    }
    case 1: {
        u16 w = VRAM(t->bx + (u >> 1), t->by + v);
        return VRAM(t->cx + ((w >> ((u & 1) * 8)) & 255), t->cy);
    }
    default:
        return VRAM(t->bx + u, t->by + v);
    }
}

static inline u32 blend24(u32 back, u32 front, int mode) {
    int br = (back >> 16) & 255, bg = (back >> 8) & 255, bb = back & 255;
    int fr = (front >> 16) & 255, fg = (front >> 8) & 255, fb = front & 255;
    int r, g, b;
    switch (mode) {
    case 0: r = (br + fr) >> 1; g = (bg + fg) >> 1; b = (bb + fb) >> 1; break;
    case 1: r = br + fr; g = bg + fg; b = bb + fb; break;
    case 2: r = br - fr; g = bg - fg; b = bb - fb; break;
    default: r = br + (fr >> 2); g = bg + (fg >> 2); b = bb + (fb >> 2); break;
    }
    r = r < 0 ? 0 : r > 255 ? 255 : r;
    g = g < 0 ? 0 : g > 255 ? 255 : g;
    b = b < 0 ? 0 : b > 255 ? 255 : b;
    return (u32)(r << 16 | g << 8 | b) | (front & 0x01000000u);
}

static inline void hd_plot(u32* p, u32 c, int semi, const HdCmd* cmd) {
    if (cmd->mcheck && (*p & 0x01000000u)) return;
    if (semi) c = blend24(*p, c, cmd->mode);
    *p = c | (cmd->mset ? 0x01000000u : 0);
}

static inline u32 hd_texel(u16 t, int r, int g, int b, int raw) {
    u32 c = c15to24(t);
    if (raw) return c;
    int tr = (int)((c >> 16) & 255) * r >> 7, tg = (int)((c >> 8) & 255) * g >> 7, tb = (int)(c & 255) * b >> 7;
    if (tr > 255) tr = 255;
    if (tg > 255) tg = 255;
    if (tb > 255) tb = 255;
    return (u32)(tr << 16 | tg << 8 | tb) | (c & 0x01000000u);
}

static inline int ffloor(float f) {
    int i = (int)f;
    return i - (f < (float)i);
}

static void hdx_tri(const HdCmd* c, int band0, int band1) {
    const HdSurf* s = &hd_surf[c->surf];
    const HV *a = &c->tri.a, *b = &c->tri.b, *cc = &c->tri.c;
    float minx = fminf(a->x, fminf(b->x, cc->x)), maxx = fmaxf(a->x, fmaxf(b->x, cc->x));
    float miny = fminf(a->y, fminf(b->y, cc->y)), maxy = fmaxf(a->y, fmaxf(b->y, cc->y));
    int X0 = ffloor(minx), X1 = (int)ceilf(maxx), Y0 = ffloor(miny), Y1 = (int)ceilf(maxy);
    if (X0 < c->x0) X0 = c->x0;
    if (X1 > c->x1) X1 = c->x1;
    if (Y0 < c->y0) Y0 = c->y0;
    if (Y1 > c->y1) Y1 = c->y1;
    if (Y0 < band0) Y0 = band0;
    if (Y1 > band1) Y1 = band1;
    if (X0 >= X1 || Y0 >= Y1) return;

    float area = (b->x - a->x) * (cc->y - a->y) - (b->y - a->y) * (cc->x - a->x);
    float A0 = b->y - cc->y, B0 = cc->x - b->x, C0 = b->x * cc->y - b->y * cc->x;
    float A1 = cc->y - a->y, B1 = a->x - cc->x, C1 = cc->x * a->y - cc->y * a->x;
    float A2 = a->y - b->y, B2 = b->x - a->x, C2 = a->x * b->y - a->y * b->x;
    float inv = 1.0f / area;
#define GRAD(f, dx, dy) \
    float dx = ((b->f - a->f) * (cc->y - a->y) - (cc->f - a->f) * (b->y - a->y)) * inv; \
    float dy = ((cc->f - a->f) * (b->x - a->x) - (b->f - a->f) * (cc->x - a->x)) * inv;
    GRAD(r, drx, dry) GRAD(g, dgx, dgy) GRAD(b, dbx, dby) GRAD(u, dux, duy) GRAD(v, dvx, dvy)
#undef GRAD
    u32 flat = (u32)((int)a->r << 16 | (int)a->g << 8 | (int)a->b);
    int fr = (int)a->r, fg = (int)a->g, fb = (int)a->b;

    for (int Y = Y0; Y < Y1; Y++) {
        float py = Y + 0.5f;
        /* exact span where all three edge functions are >= 0 */
        float lo = (float)X0, hi = (float)X1;
        float E[3] = {B0 * py + C0, B1 * py + C1, B2 * py + C2}, A[3] = {A0, A1, A2};
        int empty = 0;
        for (int k = 0; k < 3; k++) {
            /* e(x) = A*(x+0.5) + E >= 0 */
            if (A[k] > 0) { float t = -E[k] / A[k] - 0.5f; if (t > lo) lo = t; }
            else if (A[k] < 0) { float t = -E[k] / A[k] - 0.5f; if (t < hi) hi = t; }
            else if (E[k] < 0) { empty = 1; break; }
        }
        if (empty) continue;
        int xs = (int)ceilf(lo), xe = ffloor(hi) + 1;
        if (xs < X0) xs = X0;
        if (xe > X1) xe = X1;
        if (xs >= xe) continue;
        float px = xs + 0.5f, ox = px - a->x, oy = py - a->y;
        float r = a->r + drx * ox + dry * oy, g = a->g + dgx * ox + dgy * oy, bl = a->b + dbx * ox + dby * oy;
        float u = a->u + dux * ox + duy * oy, v = a->v + dvx * ox + dvy * oy;
        u32* row = s->px + (size_t)Y * s->W;
        if (c->tex) {
            for (int X = xs; X < xe; X++, r += drx, g += dgx, bl += dbx, u += dux, v += dvx) {
                u16 t = hd_tex_fetch(&c->t, ffloor(u), ffloor(v));
                if (t == 0) continue;
                u32 col = c->gour ? hd_texel(t, (int)r, (int)g, (int)bl, c->raw) : hd_texel(t, fr, fg, fb, c->raw);
                hd_plot(&row[X], col, c->semi && (t & 0x8000), c);
            }
        } else if (c->gour) {
            for (int X = xs; X < xe; X++, r += drx, g += dgx, bl += dbx) {
                int ir = (int)r, ig = (int)g, ib = (int)bl;
                ir = ir < 0 ? 0 : ir > 255 ? 255 : ir;
                ig = ig < 0 ? 0 : ig > 255 ? 255 : ig;
                ib = ib < 0 ? 0 : ib > 255 ? 255 : ib;
                hd_plot(&row[X], (u32)(ir << 16 | ig << 8 | ib), c->semi, c);
            }
        } else {
            for (int X = xs; X < xe; X++) hd_plot(&row[X], flat, c->semi, c);
        }
    }
}

static void hdx_rect(const HdCmd* c, int band0, int band1) {
    const HdSurf* s = &hd_surf[c->surf];
    int ya = c->y0 > band0 ? c->y0 : band0, yb = c->y1 < band1 ? c->y1 : band1;
    int r = c->rect.color & 255, g = (c->rect.color >> 8) & 255, b = (c->rect.color >> 16) & 255;
    u32 flat = (u32)(r << 16 | g << 8 | b);
    for (int Y = ya; Y < yb; Y++) {
        int j = (int)((Y + 0.5f - c->rect.Y0) / hd_scale);
        u32* row = s->px + (size_t)Y * s->W;
        for (int X = c->x0; X < c->x1; X++) {
            if (c->tex) {
                int i = (int)((X + 0.5f - c->rect.X0) / c->rect.kx);
                u16 t = hd_tex_fetch(&c->t, c->flipx ? c->rect.u0 - i : c->rect.u0 + i, c->flipy ? c->rect.v0 - j : c->rect.v0 + j);
                if (t == 0) continue;
                hd_plot(&row[X], hd_texel(t, r, g, b, c->raw), c->semi && (t & 0x8000), c);
            } else {
                hd_plot(&row[X], flat, c->semi, c);
            }
        }
    }
}

static void hdx_fill(const HdCmd* c, int band0, int band1) {
    const HdSurf* s = &hd_surf[c->surf];
    int ya = c->y0 > band0 ? c->y0 : band0, yb = c->y1 < band1 ? c->y1 : band1;
    for (int Y = ya; Y < yb; Y++) {
        u32* row = s->px + (size_t)Y * s->W;
        for (int X = c->x0; X < c->x1; X++) row[X] = c->fill.color;
    }
}

/* ---------------- worker pool ---------------- */

#define HD_MAX_THREADS 16
static int hd_nthreads;      /* workers besides the main thread */
static SDL_sem* sem_go[HD_MAX_THREADS];
static SDL_sem* sem_done;
static int hd_nbands;

static void run_band(int band) {
    for (int i = 0; i < qn; i++) {
        const HdCmd* c = &q[i];
        int H = hd_surf[c->surf].H;
        int b0 = H * band / hd_nbands, b1 = H * (band + 1) / hd_nbands;
        switch (c->type) {
        case HC_TRI: hdx_tri(c, b0, b1); break;
        case HC_RECT: hdx_rect(c, b0, b1); break;
        case HC_FILL: hdx_fill(c, b0, b1); break;
        }
    }
}

static int worker(void* arg) {
    int band = (int)(intptr_t)arg;
    for (;;) {
        SDL_SemWait(sem_go[band - 1]);
        run_band(band);
        SDL_SemPost(sem_done);
    }
    return 0;
}

static void hd_threads_init(void) {
    if (hd_nthreads || hd_nbands) return;
    int cpus = SDL_GetCPUCount();
    hd_nthreads = cpus - 1;
    if (hd_nthreads > HD_MAX_THREADS) hd_nthreads = HD_MAX_THREADS;
    if (hd_nthreads < 0) hd_nthreads = 0;
    hd_nbands = hd_nthreads + 1;
    sem_done = SDL_CreateSemaphore(0);
    for (int i = 0; i < hd_nthreads; i++) {
        sem_go[i] = SDL_CreateSemaphore(0);
        SDL_CreateThread(worker, "hd", (void*)(intptr_t)(i + 1));
    }
}

static void hd_flush(void) {
    if (!qn) return;
    if (hd_nbands <= 1) {
        hd_nbands = 1;
        run_band(0);
    } else {
        for (int i = 0; i < hd_nthreads; i++) SDL_SemPost(sem_go[i]);
        run_band(0);
        for (int i = 0; i < hd_nthreads; i++) SDL_SemWait(sem_done);
    }
    qn = 0;
}

/* ---------------- recording (main thread) ---------------- */

static void hd_clip_for(const HdSurf* s, int wide, HdCmd* c) {
    float fx0 = map_x(s, (float)da_x1, wide), fx1 = map_x(s, (float)(da_x2 + 1), wide);
    float fy0 = map_y(s, (float)da_y1), fy1 = map_y(s, (float)(da_y2 + 1));
    c->x0 = fx0 < 0 ? 0 : (int)floorf(fx0 + 0.5f);
    c->x1 = fx1 > s->W ? s->W : (int)floorf(fx1 + 0.5f);
    c->y0 = fy0 < 0 ? 0 : (int)floorf(fy0 + 0.5f);
    c->y1 = fy1 > s->H ? s->H : (int)floorf(fy1 + 0.5f);
}

static void hd_set_common(HdCmd* c, int si, int semi, int mode) {
    c->surf = (u8)si;
    c->semi = (u8)semi;
    c->mode = (u8)mode;
    c->mset = (u8)mask_set;
    c->mcheck = (u8)mask_check;
}

static inline int draw_area_touches(const HdSurf* s) {
    return !(da_x2 < s->ox || da_x1 >= s->ox + s->w || da_y2 < s->oy || da_y1 >= s->oy + s->h);
}

static void push_tri(int si, const HdSurf* s, int wide, const HV* a, const HV* b, const HV* cc,
                     int gour, int tex, int raw, int semi, int mode, const TexState* ts) {
    float area = (b->x - a->x) * (cc->y - a->y) - (b->y - a->y) * (cc->x - a->x);
    if (fabsf(area) < 1e-6f) return;
    HdCmd* c = hd_push();
    c->type = HC_TRI;
    hd_set_common(c, si, semi, mode);
    hd_clip_for(s, wide, c);
    c->gour = (u8)gour;
    c->tex = (u8)tex;
    c->raw = (u8)raw;
    c->tri.a = *a;
    if (area > 0) { c->tri.b = *b; c->tri.c = *cc; }
    else { c->tri.b = *cc; c->tri.c = *b; }
    if (tex) hd_capture_tex(&c->t, ts);
}

/* a polygon from gpu.c; xy holds the raw GP0 vertex words (before the drawing offset) */
static void hd_polygon(const Vtx* v, const u32* xy, int nv, int gour, int tex, int raw, int semi,
                       int mode, const TexState* ts) {
    if (!hd_enabled || !hd_nsurf) return;
    float fx[4], fy[4];
    int tagged = 0;
    for (int i = 0; i < nv; i++) {
        float tx, ty;
        if (tag_lookup(xy[i], &tx, &ty)) {
            tagged++;
            g_stat_vtx_tagged++;
            if (g_cfg.subpixel) { fx[i] = tx + off_x; fy[i] = ty + off_y; continue; }
        }
        fx[i] = (float)v[i].x;
        fy[i] = (float)v[i].y;
    }
    g_stat_poly++;
    if (tagged >= 2) g_stat_poly_tagged++;
    int minx = v[0].x, maxx = v[0].x;
    for (int i = 1; i < nv; i++) { if (v[i].x < minx) minx = v[i].x; if (v[i].x > maxx) maxx = v[i].x; }
    for (int si = 0; si < hd_nsurf; si++) {
        HdSurf* s = &hd_surf[si];
        if (!draw_area_touches(s)) continue;
        int wide = tagged >= 2 || (hd_mode3d && minx <= s->ox && maxx >= s->ox + s->w - 1);
        HV h[4];
        for (int i = 0; i < nv; i++) {
            h[i].x = map_x(s, fx[i], wide);
            h[i].y = map_y(s, fy[i]);
            h[i].r = (float)v[i].r; h[i].g = (float)v[i].g; h[i].b = (float)v[i].b;
            h[i].u = (float)v[i].u; h[i].v = (float)v[i].v;
        }
        push_tri(si, s, wide, &h[0], &h[1], &h[2], gour, tex, raw, semi, mode, ts);
        if (nv == 4) push_tri(si, s, wide, &h[1], &h[2], &h[3], gour, tex, raw, semi, mode, ts);
    }
}

static void hd_rect(int x, int y, int w, int h, u32 color, int tex, int raw, int semi, int mode,
                    int u0, int v0, const TexState* ts, u32 xyword) {
    if (!hd_enabled || !hd_nsurf) return;
    float tx, ty;
    int tagged = tag_lookup(xyword, &tx, &ty);
    for (int si = 0; si < hd_nsurf; si++) {
        HdSurf* s = &hd_surf[si];
        if (!draw_area_touches(s)) continue;
        int full = hd_mode3d && x <= s->ox && x + w >= s->ox + s->w;
        int wide = full || tagged;
        HdCmd* c = hd_push();
        c->type = HC_RECT;
        hd_set_common(c, si, semi, mode);
        hd_clip_for(s, wide, c);
        /* full-width rects stretch; GTE-positioned sprites keep their size but move wide */
        float kx = full ? hd_scale * hd_wide : (float)hd_scale;
        float X0 = map_x(s, (float)x, wide), Y0 = map_y(s, (float)y);
        int xa = (int)floorf(X0 + 0.5f), xb = (int)floorf(X0 + w * kx + 0.5f);
        int ya = (int)floorf(Y0 + 0.5f), yb = ya + h * hd_scale;
        if (xa > c->x0) c->x0 = xa;
        if (xb < c->x1) c->x1 = xb;
        if (ya > c->y0) c->y0 = ya;
        if (yb < c->y1) c->y1 = yb;
        if (c->x0 >= c->x1 || c->y0 >= c->y1) { qn--; continue; }
        c->tex = (u8)tex;
        c->raw = (u8)raw;
        c->flipx = (u8)((texpage >> 12) & 1);
        c->flipy = (u8)((texpage >> 13) & 1);
        c->rect.X0 = X0;
        c->rect.Y0 = Y0;
        c->rect.kx = kx;
        c->rect.u0 = u0;
        c->rect.v0 = v0;
        c->rect.color = color;
        if (tex) hd_capture_tex(&c->t, ts);
    }
}

/* lines become thin quads (two triangles) as wide as one native pixel */
static void hd_line(Vtx a, Vtx b, u32 xa, u32 xb, int gour, int semi, int mode) {
    if (!hd_enabled || !hd_nsurf) return;
    float ax, ay, bx, by;
    int ta = tag_lookup(xa, &ax, &ay), tb = tag_lookup(xb, &bx, &by);
    if (!ta || !g_cfg.subpixel) { ax = (float)a.x; ay = (float)a.y; } else { ax += off_x; ay += off_y; }
    if (!tb || !g_cfg.subpixel) { bx = (float)b.x; by = (float)b.y; } else { bx += off_x; by += off_y; }
    if (!gour) b.r = a.r, b.g = a.g, b.b = a.b;
    for (int si = 0; si < hd_nsurf; si++) {
        HdSurf* s = &hd_surf[si];
        if (!draw_area_touches(s)) continue;
        int wide = ta || tb;
        float x0 = map_x(s, ax + 0.5f, wide), y0 = map_y(s, ay + 0.5f);
        float x1 = map_x(s, bx + 0.5f, wide), y1 = map_y(s, by + 0.5f);
        float dx = x1 - x0, dy = y1 - y0, len = sqrtf(dx * dx + dy * dy);
        float half = hd_scale * 0.5f;
        float nx = len > 0 ? -dy / len * half : half, ny = len > 0 ? dx / len * half : 0;
        HV p[4];
        for (int i = 0; i < 4; i++) {
            const Vtx* src = i < 2 ? &a : &b;
            p[i].r = (float)src->r; p[i].g = (float)src->g; p[i].b = (float)src->b;
            p[i].u = p[i].v = 0;
        }
        p[0].x = x0 + nx; p[0].y = y0 + ny;
        p[1].x = x0 - nx; p[1].y = y0 - ny;
        p[2].x = x1 + nx; p[2].y = y1 + ny;
        p[3].x = x1 - nx; p[3].y = y1 - ny;
        push_tri(si, s, wide, &p[0], &p[1], &p[2], 1, 0, 0, semi, mode, NULL);
        push_tri(si, s, wide, &p[1], &p[2], &p[3], 1, 0, 0, semi, mode, NULL);
    }
}

static void hd_fill(int x, int y, int w, int h, u16 col) {
    if (!hd_enabled) return;
    u32 cval = c15to24(col) & 0xFFFFFF;
    for (int si = 0; si < hd_nsurf; si++) {
        HdSurf* s = &hd_surf[si];
        int x0 = x > s->ox ? x : s->ox, y0 = y > s->oy ? y : s->oy;
        int x1 = x + w < s->ox + s->w ? x + w : s->ox + s->w, y1 = y + h < s->oy + s->h ? y + h : s->oy + s->h;
        if (x0 >= x1 || y0 >= y1) continue;
        int full = x0 <= s->ox && x1 >= s->ox + s->w; /* full-width clears include the sides */
        HdCmd* c = hd_push();
        c->type = HC_FILL;
        c->surf = (u8)si;
        c->x0 = full ? 0 : (int)floorf(map_x(s, (float)x0, 0) + 0.5f);
        c->x1 = full ? s->W : (int)floorf(map_x(s, (float)x1, 0) + 0.5f);
        c->y0 = (int)map_y(s, (float)y0);
        c->y1 = (int)map_y(s, (float)y1);
        if (c->x0 < 0) c->x0 = 0;
        if (c->x1 > s->W) c->x1 = s->W;
        if (c->y1 > s->H) c->y1 = s->H;
        c->fill.color = cval;
    }
}

static void hd_copy(int sx, int sy, int dx, int dy, int w, int h) {
    if (!hd_enabled || !hd_nsurf) return;
    hd_flush();
    /* surface-to-surface copies of whole screens keep the high resolution */
    HdSurf *src = NULL, *dst = NULL;
    for (int i = 0; i < hd_nsurf; i++) {
        HdSurf* s = &hd_surf[i];
        if (sx == s->ox && sy == s->oy && w == s->w && h == s->h) src = s;
        if (dx == s->ox && dy == s->oy && w == s->w && h == s->h) dst = s;
    }
    if (src && dst && src != dst && src->W == dst->W && src->H == dst->H) {
        memcpy(dst->px, src->px, (size_t)src->W * src->H * 4);
        return;
    }
    hd_upload(dx, dy, w, h);
}

/* ---------------- presentation ---------------- */

static void hd_display_changed(void) {
    if (!hd_enabled || (disp_mode & 0x10)) return;
    static const int widths[4] = {256, 320, 512, 640};
    int w = (disp_mode & 0x40) ? 368 : widths[disp_mode & 3];
    int h = ((disp_mode & 0x24) == 0x24) ? 480 : 240;
    hd_get_surface((int)disp_x, (int)disp_y, w, h, 1);
}

/* the frame to show; returns NULL when the native picture should be used */
const u32* gpu_hd_frame(int* w, int* h) {
    if (!hd_enabled || !disp_on || (disp_mode & 0x10)) return NULL;
    static const int widths[4] = {256, 320, 512, 640};
    int dw = (disp_mode & 0x40) ? 368 : widths[disp_mode & 3];
    int dh = ((disp_mode & 0x24) == 0x24) ? 480 : 240;
    HdSurf* s = hd_get_surface((int)disp_x, (int)disp_y, dw, dh, 1);
    hd_flush();
    *w = s->W;
    *h = s->H;
    return s->px;
}
