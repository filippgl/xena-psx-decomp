/* Texture replacement packs: textures/<hash>_<w>x<h>.png next to the program plus
 * textures/manifest.txt (lines written by the dumper: name page clut u0 v0 w h used3d).
 *
 * A textured primitive uses a replacement when an entry with the same texture page and
 * CLUT contains its UV rectangle and the entry's region in VRAM still decodes to the
 * dumped hash. Verification results are cached until a VRAM transfer touches the
 * texture page or the CLUT row. Replacements are used by the OpenGL backend only.
 *
 * Included from gpu_hd.inc. */
#include "glfuncs.h"
#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_PNG
#define STBI_NO_STDIO_UNUSED
#include "stb_image.h"

typedef struct {
    u64 hash;
    u16 page, clut;
    u8 u0, v0;
    u16 w, h;
    char file[48];
    unsigned gl_tex;   /* 0 = not loaded yet */
    s8 load_failed;
    u32 checked_gen;   /* VRAM generation of the last verification */
    s8 valid;
} RepEntry;

static RepEntry* reps;
static int nreps;
static char rep_dir[1024];
static u32 vram_gen_page[32]; /* per texture page (16 x 2) */
static u32 vram_gen_any;
u64 texture_decode(u32 page, u32 clut, int u0, int v0, int w, int h, u8* rgba);

/* called for every VRAM transfer/fill/copy rectangle */
static void rep_vram_written(int x, int y, int w, int h) {
    if (!nreps) return;
    vram_gen_any++;
    for (int py = 0; py < 2; py++)
        for (int px = 0; px < 16; px++) {
            int bx = px * 64, by = py * 256;
            if (x < bx + 64 && bx < x + w && y < by + 256 && by < y + h) vram_gen_page[py * 16 + px] = vram_gen_any;
        }
}

void texreplace_load(const char* dir) {
    snprintf(rep_dir, sizeof(rep_dir), "%s", dir);
    char path[1100];
    snprintf(path, sizeof(path), "%s/manifest.txt", dir);
    FILE* f = fopen(path, "r");
    if (!f) return;
    char line[256];
    while (fgets(line, sizeof(line), f)) {
        char name[64];
        unsigned page, clut;
        int u0, v0, w, h, used3d;
        if (sscanf(line, "%63s %x %x %d %d %d %d %d", name, &page, &clut, &u0, &v0, &w, &h, &used3d) != 8) continue;
        snprintf(path, sizeof(path), "%s/%s.png", dir, name);
        FILE* t = fopen(path, "rb");
        if (!t) continue;
        fclose(t);
        reps = realloc(reps, sizeof(RepEntry) * (size_t)(nreps + 1));
        RepEntry* r = &reps[nreps++];
        memset(r, 0, sizeof(*r));
        r->hash = strtoull(name, NULL, 16);
        r->page = (u16)page;
        r->clut = (u16)clut;
        r->u0 = (u8)u0;
        r->v0 = (u8)v0;
        r->w = (u16)w;
        r->h = (u16)h;
        r->checked_gen = 0xFFFFFFFFu;
        snprintf(r->file, sizeof(r->file), "%s.png", name);
    }
    fclose(f);
    if (nreps) log_msg("texture replacements: %d", nreps);
}

/* find a verified replacement for a primitive's texture rectangle */
static int rep_find(const TexState* ts, int umin, int vmin, int umax, int vmax) {
    if (!nreps || !g_hd_gl || !g_cfg.texture_pack) return -1;
    u16 page = (u16)((ts->bx / 64) | ((ts->by / 256) << 4) | (ts->depth << 7));
    u16 clut = ts->depth == 2 ? 0 : (u16)((ts->cx / 16) | (ts->cy << 6));
    for (int i = 0; i < nreps; i++) {
        RepEntry* r = &reps[i];
        if (r->page != page || r->clut != clut) continue;
        if (umin < r->u0 || vmin < r->v0 || umax >= r->u0 + r->w || vmax >= r->v0 + r->h) continue;
        u32 gen = vram_gen_page[(page & 15) + ((page >> 4) & 1) * 16];
        if (clut) {
            int cp = ((clut & 0x3F) * 16) / 64 + ((clut >> 6) >= 256 ? 16 : 0);
            if (vram_gen_page[cp] > gen) gen = vram_gen_page[cp];
        }
        if (r->checked_gen != gen) {
            static u8 rgba[256 * 256 * 4];
            r->valid = texture_decode(page, clut, r->u0, r->v0, r->w, r->h, rgba) == r->hash;
            r->checked_gen = gen;
        }
        if (r->valid && !r->load_failed) return i;
    }
    return -1;
}

static unsigned hd_rep_rect(int rep) {
    const RepEntry* r = &reps[rep];
    return (u32)r->u0 | ((u32)r->v0 << 8) | ((u32)(r->w - 1) << 16) | ((u32)(r->h - 1) << 24);
}

static unsigned hd_rep_texture(int rep) {
    RepEntry* r = &reps[rep];
    if (!r->gl_tex && !r->load_failed) {
        char path[1100];
        snprintf(path, sizeof(path), "%s/%s", rep_dir, r->file);
        int w, h, n;
        u8* px = stbi_load(path, &w, &h, &n, 4);
        if (!px) { r->load_failed = 1; log_msg("texture replacement: cannot load %s", path); return 0; }
        unsigned t;
        p_glGenTextures(1, &t);
        p_glBindTexture(GL_TEXTURE_2D, t);
        p_glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
        p_glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, px);
        p_glGenerateMipmap(GL_TEXTURE_2D);
        p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        stbi_image_free(px);
        r->gl_tex = t;
    }
    return r->gl_tex;
}
