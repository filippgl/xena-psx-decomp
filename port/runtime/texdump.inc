/* Texture dumping for replacement packs (--dump-textures DIR).
 *
 * Every textured primitive reports its texture page, CLUT and the UV rectangle it
 * samples. Rectangles that overlap for the same page+CLUT are merged, which recovers
 * the extent of each individual texture inside the page. On exit every merged
 * rectangle is decoded with its CLUT and written as raw RGBA (alpha 0 = transparent
 * texel) plus a manifest line; tools/texdump2png.py turns them into PNGs.
 *
 * Included from gpu.c. */

typedef struct {
    u16 page;          /* texpage bits: x/64, y/256, depth */
    u16 clut;          /* raw CLUT word (0 for 15-bit) */
    u8 u0, v0, u1, v1; /* inclusive texel rectangle */
    u8 used3d;
} DumpRect;

static DumpRect* dump_rects;
static int dump_n, dump_cap;
static const char* dump_dir;

void texdump_enable(const char* dir) { dump_dir = dir; }

static void texdump_record(u32 tp, u32 clut, int u0, int v0, int u1, int v1, int is3d) {
    if (!dump_dir) return;
    int depth = (tp >> 7) & 3;
    u16 page = (u16)(tp & 0x19F);
    u16 cl = depth == 2 ? 0 : (u16)clut;
    if (u0 < 0) u0 = 0;
    if (v0 < 0) v0 = 0;
    if (u1 > 255) u1 = 255;
    if (v1 > 255) v1 = 255;
    for (int i = 0; i < dump_n; i++) {
        DumpRect* r = &dump_rects[i];
        if (r->page != page || r->clut != cl) continue;
        /* merge with an overlapping or touching rectangle */
        if (u0 <= r->u1 + 1 && r->u0 <= u1 + 1 && v0 <= r->v1 + 1 && r->v0 <= v1 + 1) {
            if (u0 < r->u0) r->u0 = (u8)u0;
            if (v0 < r->v0) r->v0 = (u8)v0;
            if (u1 > r->u1) r->u1 = (u8)u1;
            if (v1 > r->v1) r->v1 = (u8)v1;
            r->used3d |= (u8)is3d;
            return;
        }
    }
    if (dump_n >= dump_cap) {
        dump_cap = dump_cap ? dump_cap * 2 : 1024;
        dump_rects = realloc(dump_rects, sizeof(DumpRect) * (size_t)dump_cap);
    }
    dump_rects[dump_n++] = (DumpRect){page, cl, (u8)u0, (u8)v0, (u8)u1, (u8)v1, (u8)is3d};
}

/* merging can make rectangles overlap each other later: repeat until stable */
static void texdump_coalesce(void) {
    int changed = 1;
    while (changed) {
        changed = 0;
        for (int i = 0; i < dump_n; i++)
            for (int j = i + 1; j < dump_n; j++) {
                DumpRect *a = &dump_rects[i], *b = &dump_rects[j];
                if (a->page != b->page || a->clut != b->clut) continue;
                if (b->u0 <= a->u1 + 1 && a->u0 <= b->u1 + 1 && b->v0 <= a->v1 + 1 && a->v0 <= b->v1 + 1) {
                    if (b->u0 < a->u0) a->u0 = b->u0;
                    if (b->v0 < a->v0) a->v0 = b->v0;
                    if (b->u1 > a->u1) a->u1 = b->u1;
                    if (b->v1 > a->v1) a->v1 = b->v1;
                    a->used3d |= b->used3d;
                    dump_rects[j] = dump_rects[--dump_n];
                    changed = 1;
                    j--;
                }
            }
    }
}

/* decode a texel rectangle into RGBA8; returns the FNV-1a hash of the decoded pixels */
u64 texture_decode(u32 page, u32 clut, int u0, int v0, int w, int h, u8* rgba) {
    TexState ts;
    u32 saved_mx = tw_mask_x, saved_my = tw_mask_y;
    tw_mask_x = tw_mask_y = 0; /* decode without the texture window */
    set_texstate(&ts, page, clut);
    u64 hash = 1469598103934665603ull;
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++) {
            u16 t = tex_fetch(&ts, u0 + x, v0 + y);
            u8* p = &rgba[(y * w + x) * 4];
            u32 c = (t & 31) << 3, g = ((t >> 5) & 31) << 3, b = ((t >> 10) & 31) << 3;
            p[0] = (u8)(c | c >> 5);
            p[1] = (u8)(g | g >> 5);
            p[2] = (u8)(b | b >> 5);
            p[3] = t == 0 ? 0 : 255;
            for (int k = 0; k < 4; k++) { hash ^= p[k]; hash *= 1099511628211ull; }
        }
    tw_mask_x = saved_mx;
    tw_mask_y = saved_my;
    return hash;
}

void texdump_write(void) {
    if (!dump_dir) return;
    texdump_coalesce();
    char path[1024];
    snprintf(path, sizeof(path), "%s/manifest.txt", dump_dir);
    FILE* mf = fopen(path, "a");
    if (!mf) { log_msg("texdump: cannot write %s", path); return; }
    int written = 0;
    static u8 rgba[256 * 256 * 4];
    for (int i = 0; i < dump_n; i++) {
        DumpRect* r = &dump_rects[i];
        int w = r->u1 - r->u0 + 1, h = r->v1 - r->v0 + 1;
        if (w < 4 || h < 4) continue;
        u64 hash = texture_decode(r->page, r->clut, r->u0, r->v0, w, h, rgba);
        int opaque = 0;
        for (int k = 0; k < w * h; k++) opaque += rgba[k * 4 + 3] != 0;
        if (!opaque) continue;
        char name[64];
        snprintf(name, sizeof(name), "%016llx_%dx%d", (unsigned long long)hash, w, h);
        snprintf(path, sizeof(path), "%s/%s.rgba", dump_dir, name);
        FILE* f = fopen(path, "rb");
        if (f) { fclose(f); continue; } /* already dumped (same content) */
        f = fopen(path, "wb");
        if (!f) continue;
        fwrite(rgba, 4, (size_t)w * h, f);
        fclose(f);
        /* name page clut u0 v0 w h used3d */
        fprintf(mf, "%s %04X %04X %d %d %d %d %d\n", name, r->page, r->clut, r->u0, r->v0, w, h, r->used3d);
        written++;
    }
    fclose(mf);
    log_msg("texdump: %d textures written to %s (%d regions)", written, dump_dir, dump_n);
    dump_n = 0;
}
