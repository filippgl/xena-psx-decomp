/* OpenGL backend for the enhanced renderer: surfaces are framebuffer objects and the
 * queued HdCmd list is turned into vertex batches; a fragment shader reproduces PS1
 * texturing (CLUT decoding, texture window, modulation, transparent texel 0,
 * semi-transparency bit) from the native VRAM uploaded as an integer texture.
 * Semi-transparent textured primitives are drawn in two passes (opaque texels without
 * blending, then semi texels with the PS1 blend equation).
 *
 * Included from gpu_hd.inc. */
#include "glfuncs.h"

int g_hd_gl;                     /* hardware backend active */
static GLuint gl_prog, gl_vao, gl_vbo, gl_vram_tex;
static GLint u_size, u_pass, u_semi, u_vram, u_rep;
static u16 gl_vram_shadow[512 * 1024]; /* last uploaded VRAM, to skip redundant uploads */

typedef struct {
    float x, y;
    float u, v, q;
    u8 r, g, b, a;
    u32 p0, p1, p2, p3; /* texture page, CLUT, flags, texture window */
    u32 c0, c1;         /* UV clamp rect, replacement rect */
} GlVtx;

enum { GF_TEX = 4, GF_RAW = 8, GF_BILINEAR = 16, GF_PERSP = 32, GF_REP = 64, GF_VRAMCOPY = 128 };

static const char* gl_vs =
    "#version 330 core\n"
    "in vec2 a_pos; in vec3 a_uvq; in vec4 a_col; in uvec4 a_p; in uvec2 a_c;\n"
    "uniform vec2 u_size;\n"
    "noperspective out vec3 v_uvq; noperspective out vec4 v_col; flat out uvec4 v_p; flat out uvec2 v_c;\n"
    "void main() {\n"
    "  gl_Position = vec4(a_pos / u_size * 2.0 - 1.0, 0.0, 1.0);\n"
    "  v_uvq = a_uvq; v_col = a_col; v_p = a_p; v_c = a_c;\n"
    "}\n";

static const char* gl_fs =
    "#version 330 core\n"
    "uniform usampler2D u_vram; uniform sampler2D u_rep;\n"
    "uniform int u_pass; uniform int u_semi;\n"
    "noperspective in vec3 v_uvq; noperspective in vec4 v_col; flat in uvec4 v_p; flat in uvec2 v_c;\n"
    "out vec4 o_color;\n"
    "uint vram(int x, int y) { return texelFetch(u_vram, ivec2(x & 1023, y & 511), 0).r; }\n"
    "uint fetch(int u, int v) {\n"
    "  uint tw = v_p.w;\n"
    "  u = ((u & ~int(tw & 255u)) | int((tw >> 16) & 255u)) & 255;\n"
    "  v = ((v & ~int((tw >> 8) & 255u)) | int(tw >> 24)) & 255;\n"
    "  int bx = int(v_p.x & 0xFFFFu), by = int(v_p.x >> 16), cx = int(v_p.y & 0xFFFFu), cy = int(v_p.y >> 16);\n"
    "  uint depth = v_p.z & 3u;\n"
    "  if (depth == 0u) { uint w = vram(bx + (u >> 2), by + v); return vram(cx + int((w >> uint((u & 3) * 4)) & 15u), cy); }\n"
    "  if (depth == 1u) { uint w = vram(bx + (u >> 1), by + v); return vram(cx + int((w >> uint((u & 1) * 8)) & 255u), cy); }\n"
    "  return vram(bx + u, by + v);\n"
    "}\n"
    "vec3 c15(uint t) { return vec3(float(t & 31u), float((t >> 5) & 31u), float((t >> 10) & 31u)) * (255.0 / 31.0); }\n"
    "void main() {\n"
    "  uint f = v_p.z;\n"
    "  if ((f & 128u) != 0u) { o_color = vec4(c15(vram(int(v_uvq.x), int(v_uvq.y))) / 255.0, 1.0); return; }\n"
    "  vec3 col = v_col.rgb;\n"
    "  bool semi_px = u_semi != 0;\n"
    "  if ((f & 4u) != 0u) {\n"
    "    vec2 uv = v_uvq.xy;\n"
    "    if ((f & 32u) != 0u) uv /= v_uvq.z;\n"
    "    vec2 lo = vec2(float(v_c.x & 255u), float((v_c.x >> 8) & 255u));\n"
    "    vec2 hi = vec2(float((v_c.x >> 16) & 255u), float(v_c.x >> 24)) + 0.999;\n"
    "    uv = clamp(uv, lo, hi);\n"
    "    vec3 tc; uint stp;\n"
    "    if ((f & 64u) != 0u) {\n"
    "      vec2 r0 = vec2(float(v_c.y & 255u), float((v_c.y >> 8) & 255u));\n"
    "      vec2 rs = vec2(float((v_c.y >> 16) & 255u), float(v_c.y >> 24)) + 1.0;\n"
    "      vec4 r = texture(u_rep, (uv - r0) / rs);\n"
    "      if (r.a < 0.5) discard;\n"
    "      tc = r.rgb * 255.0;\n"
    "      stp = fetch(int(uv.x), int(uv.y)) >> 15;\n"
    "    } else if ((f & 16u) != 0u) {\n"
    "      vec2 p = uv - 0.5; vec2 b = floor(p); vec2 fr = p - b;\n"
    "      uint ts[4]; float ws[4]; float best = -1.0; uint nearest = 0u;\n"
    "      for (int k = 0; k < 4; k++) {\n"
    "        vec2 d = vec2(float(k & 1), float(k >> 1));\n"
    "        vec2 c = clamp(b + d, lo, floor(hi));\n"
    "        ws[k] = (d.x > 0.0 ? fr.x : 1.0 - fr.x) * (d.y > 0.0 ? fr.y : 1.0 - fr.y);\n"
    "        ts[k] = fetch(int(c.x), int(c.y));\n"
    "        if (ws[k] > best) { best = ws[k]; nearest = ts[k]; }\n"
    "      }\n"
    "      /* semi-transparency is per texel: only blend texels of the nearest texel's class */\n"
    "      vec3 acc = vec3(0.0); float aw = 0.0, cov = 0.0;\n"
    "      for (int k = 0; k < 4; k++) {\n"
    "        if (ts[k] == 0u) continue;\n"
    "        cov += ws[k];\n"
    "        if ((ts[k] >> 15) != (nearest >> 15)) continue;\n"
    "        acc += ws[k] * c15(ts[k]); aw += ws[k];\n"
    "      }\n"
    "      if (cov < 0.5 || nearest == 0u) discard;\n"
    "      if (aw < 0.5) discard;\n"
    "      tc = acc / aw; stp = nearest >> 15;\n"
    "    } else {\n"
    "      uint t = fetch(int(floor(uv.x)), int(floor(uv.y)));\n"
    "      if (t == 0u) discard;\n"
    "      tc = c15(t); stp = t >> 15;\n"
    "    }\n"
    "    semi_px = semi_px && stp != 0u;\n"
    "    col = (f & 8u) != 0u ? tc : min(tc * col / 128.0, vec3(255.0));\n"
    "  }\n"
    "  if (u_pass == 1 && semi_px) discard;\n"
    "  if (u_pass == 2 && !semi_px) discard;\n"
    "  o_color = vec4(col / 255.0, 1.0);\n"
    "}\n";

int hdgl_init(void) {
    gl_prog = gl_program(gl_vs, gl_fs);
    if (!gl_prog) return 0;
    u_size = p_glGetUniformLocation(gl_prog, "u_size");
    u_pass = p_glGetUniformLocation(gl_prog, "u_pass");
    u_semi = p_glGetUniformLocation(gl_prog, "u_semi");
    u_vram = p_glGetUniformLocation(gl_prog, "u_vram");
    u_rep = p_glGetUniformLocation(gl_prog, "u_rep");
    p_glGenVertexArrays(1, &gl_vao);
    p_glGenBuffers(1, &gl_vbo);
    p_glBindVertexArray(gl_vao);
    p_glBindBuffer(GL_ARRAY_BUFFER, gl_vbo);
    p_glGenTextures(1, &gl_vram_tex);
    p_glBindTexture(GL_TEXTURE_2D, gl_vram_tex);
    p_glTexImage2D(GL_TEXTURE_2D, 0, GL_R16UI, 1024, 512, 0, GL_RED_INTEGER, GL_UNSIGNED_SHORT, NULL);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    memset(gl_vram_shadow, 0xFF, sizeof(gl_vram_shadow));
    g_hd_gl = 1;
    return 1;
}

/* attribute locations are bound before linking (see hdgl_link) */
static void hdgl_attribs(void) {
    GLsizei st = sizeof(GlVtx);
    p_glBindVertexArray(gl_vao);
    p_glBindBuffer(GL_ARRAY_BUFFER, gl_vbo);
    p_glEnableVertexAttribArray(0);
    p_glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, st, (void*)offsetof(GlVtx, x));
    p_glEnableVertexAttribArray(1);
    p_glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, st, (void*)offsetof(GlVtx, u));
    p_glEnableVertexAttribArray(2);
    p_glVertexAttribPointer(2, 4, GL_UNSIGNED_BYTE, GL_FALSE, st, (void*)offsetof(GlVtx, r));
    p_glEnableVertexAttribArray(3);
    p_glVertexAttribIPointer(3, 4, GL_UNSIGNED_INT, st, (void*)offsetof(GlVtx, p0));
    p_glEnableVertexAttribArray(4);
    p_glVertexAttribIPointer(4, 2, GL_UNSIGNED_INT, st, (void*)offsetof(GlVtx, c0));
}

static void hdgl_upload_vram(void) {
    /* upload only the rows that changed since the last upload */
    int y0 = -1, y1 = -1;
    for (int y = 0; y < 512; y++)
        if (memcmp(&gl_vram_shadow[y * 1024], &g_vram[y * 1024], 2048)) { if (y0 < 0) y0 = y; y1 = y; }
    if (y0 < 0) return;
    memcpy(&gl_vram_shadow[y0 * 1024], &g_vram[y0 * 1024], (size_t)(y1 - y0 + 1) * 2048);
    p_glActiveTexture(GL_TEXTURE0);
    p_glBindTexture(GL_TEXTURE_2D, gl_vram_tex);
    p_glPixelStorei(GL_UNPACK_ALIGNMENT, 2);
    p_glTexSubImage2D(GL_TEXTURE_2D, 0, 0, y0, 1024, y1 - y0 + 1, GL_RED_INTEGER, GL_UNSIGNED_SHORT, &g_vram[y0 * 1024]);
}

static void hdgl_create_surface(HdSurf* s) {
    p_glGenTextures(1, &s->gl_tex);
    p_glBindTexture(GL_TEXTURE_2D, s->gl_tex);
    p_glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, s->W, s->H, 0, GL_RGBA, GL_UNSIGNED_BYTE, NULL);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    p_glGenFramebuffers(1, &s->gl_fbo);
    p_glBindFramebuffer(GL_FRAMEBUFFER, s->gl_fbo);
    p_glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, s->gl_tex, 0);
    p_glClearColor(0, 0, 0, 1);
    p_glDisable(GL_SCISSOR_TEST);
    p_glClear(GL_COLOR_BUFFER_BIT);
}

static void hdgl_free_surface(HdSurf* s) {
    if (s->gl_fbo) p_glDeleteFramebuffers(1, &s->gl_fbo);
    if (s->gl_tex) p_glDeleteTextures(1, &s->gl_tex);
    s->gl_fbo = s->gl_tex = 0;
}

/* ---------------- batching ---------------- */

static GlVtx* gv;
static int gvn, gvcap;

static GlVtx* gv_push(int n) {
    if (gvn + n > gvcap) {
        gvcap = (gvn + n) * 2;
        gv = realloc(gv, sizeof(GlVtx) * (size_t)gvcap);
    }
    GlVtx* v = &gv[gvn];
    gvn += n;
    return v;
}

static u32 cmd_tex_params(const HdCmd* c, u32* p0, u32* p1, u32* p3) {
    *p0 = (u32)c->t.bx | ((u32)c->t.by << 16);
    *p1 = (u32)c->t.cx | ((u32)c->t.cy << 16);
    *p3 = (u32)c->t.twmx | ((u32)c->t.twmy << 8) | ((u32)c->t.twox << 16) | ((u32)c->t.twoy << 24);
    u32 f = (u32)c->t.depth;
    if (c->tex) f |= GF_TEX;
    if (c->raw) f |= GF_RAW;
    if (c->bilinear) f |= GF_BILINEAR;
    if (c->persp) f |= GF_PERSP;
    if (c->rep >= 0) f |= GF_REP;
    return f;
}

static void emit_vtx(GlVtx* o, const HV* h, const HdCmd* c, u32 p0, u32 p1, u32 p2, u32 p3) {
    o->x = h->x;
    o->y = h->y;
    o->u = h->u;
    o->v = h->v;
    o->q = h->q;
    o->r = (u8)(h->r < 0 ? 0 : h->r > 255 ? 255 : h->r);
    o->g = (u8)(h->g < 0 ? 0 : h->g > 255 ? 255 : h->g);
    o->b = (u8)(h->b < 0 ? 0 : h->b > 255 ? 255 : h->b);
    o->a = 255;
    o->p0 = p0; o->p1 = p1; o->p2 = p2; o->p3 = p3;
    o->c0 = (u32)(u8)c->umin | ((u32)(u8)c->vmin << 8) | ((u32)(u8)c->umax << 16) | ((u32)(u8)c->vmax << 24);
    o->c1 = c->rep >= 0 ? hd_rep_rect(c->rep) : 0;
}

/* append the vertices of one command; returns the vertex count */
static int cmd_vertices(const HdCmd* c) {
    u32 p0 = 0, p1 = 0, p3 = 0, p2 = 0;
    if (c->tex || c->type == HC_RECT) p2 = cmd_tex_params(c, &p0, &p1, &p3);
    if (c->type == HC_TRI) {
        GlVtx* o = gv_push(3);
        emit_vtx(&o[0], &c->tri.a, c, p0, p1, p2, p3);
        emit_vtx(&o[1], &c->tri.b, c, p0, p1, p2, p3);
        emit_vtx(&o[2], &c->tri.c, c, p0, p1, p2, p3);
        return 3;
    }
    /* sprite: corners of the clipped rectangle with texel coordinates at those corners */
    float x0 = (float)c->x0, x1 = (float)c->x1, y0 = (float)c->y0, y1 = (float)c->y1;
    float kx = c->rect.kx, ky = (float)hd_scale;
    HV h[4];
    float xs[4] = {x0, x1, x0, x1}, ys[4] = {y0, y0, y1, y1};
    for (int i = 0; i < 4; i++) {
        h[i].x = xs[i];
        h[i].y = ys[i];
        float du = (xs[i] - c->rect.X0) / kx, dv = (ys[i] - c->rect.Y0) / ky;
        h[i].u = c->flipx ? c->rect.u0 + 1 - du : c->rect.u0 + du;
        h[i].v = c->flipy ? c->rect.v0 + 1 - dv : c->rect.v0 + dv;
        h[i].q = 1;
        h[i].r = (float)(c->rect.color & 255);
        h[i].g = (float)((c->rect.color >> 8) & 255);
        h[i].b = (float)((c->rect.color >> 16) & 255);
    }
    HdCmd cc = *c;
    cc.umin = 0; cc.vmin = 0; cc.umax = 255; cc.vmax = 255;
    if (!c->tex) p2 = 0;
    GlVtx* o = gv_push(6);
    static const int idx[6] = {0, 1, 2, 1, 3, 2};
    for (int i = 0; i < 6; i++) emit_vtx(&o[i], &h[idx[i]], &cc, p0, p1, p2, p3);
    return 6;
}

static void gl_blend_mode(int mode) {
    p_glEnable(GL_BLEND);
    switch (mode) {
    case 0: p_glBlendColor(0, 0, 0, 0.5f); p_glBlendEquation(GL_FUNC_ADD); p_glBlendFunc(GL_CONSTANT_ALPHA, GL_CONSTANT_ALPHA); break;
    case 1: p_glBlendEquation(GL_FUNC_ADD); p_glBlendFunc(GL_ONE, GL_ONE); break;
    case 2: p_glBlendEquation(GL_FUNC_REVERSE_SUBTRACT); p_glBlendFunc(GL_ONE, GL_ONE); break;
    default: p_glBlendColor(0, 0, 0, 0.25f); p_glBlendEquation(GL_FUNC_ADD); p_glBlendFunc(GL_CONSTANT_ALPHA, GL_ONE); break;
    }
}

static void gl_bind_surface(const HdSurf* s) {
    p_glBindFramebuffer(GL_FRAMEBUFFER, s->gl_fbo);
    p_glViewport(0, 0, s->W, s->H);
    p_glUniform2f(u_size, (float)s->W, (float)s->H);
}

static void hdgl_execute(void) {
    hdgl_upload_vram();
    gvn = 0;
    /* build all vertices first, remembering each command's range */
    static int* first;
    static int first_cap;
    if (qn > first_cap) { first_cap = qn * 2; first = realloc(first, sizeof(int) * (size_t)first_cap); }
    for (int i = 0; i < qn; i++) {
        first[i] = gvn;
        if (q[i].type != HC_FILL) cmd_vertices(&q[i]);
    }
    p_glUseProgram(gl_prog);
    hdgl_attribs();
    p_glBufferData(GL_ARRAY_BUFFER, (GLsizeiptr)(sizeof(GlVtx) * (size_t)gvn), gv, GL_STREAM_DRAW);
    p_glActiveTexture(GL_TEXTURE0);
    p_glBindTexture(GL_TEXTURE_2D, gl_vram_tex);
    p_glUniform1i(u_vram, 0);
    p_glUniform1i(u_rep, 1);
    p_glEnable(GL_SCISSOR_TEST);

    int cur_surf = -1, cur_rep = -2;
    int i = 0;
    while (i < qn) {
        const HdCmd* c = &q[i];
        const HdSurf* s = &hd_surf[c->surf];
        if (c->surf != cur_surf) { gl_bind_surface(s); cur_surf = c->surf; }
        if (c->type == HC_FILL) {
            p_glScissor(c->x0, c->y0, c->x1 - c->x0, c->y1 - c->y0);
            u32 col = c->fill.color;
            p_glClearColor(((col >> 16) & 255) / 255.0f, ((col >> 8) & 255) / 255.0f, (col & 255) / 255.0f, 1.0f);
            p_glClear(GL_COLOR_BUFFER_BIT);
            i++;
            continue;
        }
        if (c->rep != cur_rep) {
            p_glActiveTexture(GL_TEXTURE1);
            p_glBindTexture(GL_TEXTURE_2D, c->rep >= 0 ? hd_rep_texture(c->rep) : 0);
            p_glActiveTexture(GL_TEXTURE0);
            cur_rep = c->rep;
        }
        /* merge following commands that share surface, clip, blend state and replacement */
        int j = i + 1;
        int semi_tex = c->semi && c->tex;
        if (!semi_tex)
            while (j < qn && q[j].type != HC_FILL && q[j].surf == c->surf && q[j].x0 == c->x0 && q[j].x1 == c->x1 &&
                   q[j].y0 == c->y0 && q[j].y1 == c->y1 && q[j].semi == c->semi && q[j].mode == c->mode &&
                   q[j].rep == c->rep && !(q[j].semi && q[j].tex))
                j++;
        int start = first[i], count = (j < qn ? first[j] : gvn) - start;
        p_glScissor(c->x0, c->y0, c->x1 - c->x0, c->y1 - c->y0);
        if (semi_tex) {
            p_glDisable(GL_BLEND);
            p_glUniform1i(u_semi, 1);
            p_glUniform1i(u_pass, 1);
            p_glDrawArrays(GL_TRIANGLES, start, count);
            gl_blend_mode(c->mode);
            p_glUniform1i(u_pass, 2);
            p_glDrawArrays(GL_TRIANGLES, start, count);
        } else {
            if (c->semi) gl_blend_mode(c->mode); else p_glDisable(GL_BLEND);
            p_glUniform1i(u_semi, c->semi);
            p_glUniform1i(u_pass, 0);
            p_glDrawArrays(GL_TRIANGLES, start, count);
        }
        i = j;
    }
    p_glDisable(GL_BLEND);
    p_glDisable(GL_SCISSOR_TEST);
    p_glBindFramebuffer(GL_FRAMEBUFFER, 0);
}

/* native VRAM rectangle -> surface region (quad whose texel coordinates are VRAM coordinates) */
static void hdgl_upload_region(HdSurf* s, int x0, int y0, int x1, int y1, int wide) {
    hdgl_upload_vram();
    float X0 = map_x(s, (float)x0, wide), X1 = map_x(s, (float)x1, wide);
    float Y0 = map_y(s, (float)y0), Y1 = map_y(s, (float)y1);
    GlVtx v[6];
    float xs[4] = {X0, X1, X0, X1}, ys[4] = {Y0, Y0, Y1, Y1};
    float us[4] = {(float)x0, (float)x1, (float)x0, (float)x1}, vs[4] = {(float)y0, (float)y0, (float)y1, (float)y1};
    static const int idx[6] = {0, 1, 2, 1, 3, 2};
    for (int i = 0; i < 6; i++) {
        memset(&v[i], 0, sizeof(GlVtx));
        v[i].x = xs[idx[i]];
        v[i].y = ys[idx[i]];
        v[i].u = us[idx[i]];
        v[i].v = vs[idx[i]];
        v[i].q = 1;
        v[i].p2 = GF_VRAMCOPY;
    }
    p_glUseProgram(gl_prog);
    hdgl_attribs();
    p_glBufferData(GL_ARRAY_BUFFER, sizeof(v), v, GL_STREAM_DRAW);
    p_glActiveTexture(GL_TEXTURE0);
    p_glBindTexture(GL_TEXTURE_2D, gl_vram_tex);
    p_glUniform1i(u_vram, 0);
    gl_bind_surface(s);
    p_glDisable(GL_BLEND);
    p_glDisable(GL_SCISSOR_TEST);
    p_glDrawArrays(GL_TRIANGLES, 0, 6);
    p_glBindFramebuffer(GL_FRAMEBUFFER, 0);
}

void hdgl_copy_surface(const HdSurf* src, const HdSurf* dst) {
    p_glBindFramebuffer(GL_READ_FRAMEBUFFER, src->gl_fbo);
    p_glBindFramebuffer(GL_DRAW_FRAMEBUFFER, dst->gl_fbo);
    p_glBlitFramebuffer(0, 0, src->W, src->H, 0, 0, dst->W, dst->H, GL_COLOR_BUFFER_BIT, GL_NEAREST);
    p_glBindFramebuffer(GL_FRAMEBUFFER, 0);
}

/* read a surface back (row 0 = top), for screenshots */
void hdgl_read(const HdSurf* s, u32* out) {
    p_glBindFramebuffer(GL_FRAMEBUFFER, s->gl_fbo);
    p_glPixelStorei(GL_PACK_ALIGNMENT, 4);
    p_glReadPixels(0, 0, s->W, s->H, GL_BGRA, GL_UNSIGNED_BYTE, out);
    p_glBindFramebuffer(GL_FRAMEBUFFER, 0);
}
