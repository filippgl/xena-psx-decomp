/* 2D drawing used for presenting frames and for the settings menu, with two backends:
 * the SDL renderer (software rendering) and OpenGL (hardware rendering).
 * Coordinates are logical pixels (window_width() x g_cfg.window_height), letterboxed
 * into the window. Included from platform.c. */
#include "glfuncs.h"

static SDL_GLContext glctx;
static int gl_mode;
static GLuint r2d_prog, r2d_vao, r2d_vbo, r2d_frame_tex, r2d_font_tex;
static GLint r2u_mode, r2u_color, r2u_tex, r2u_ssaa, r2u_texsize, r2u_xform;
static GLuint shown_tex;          /* texture last presented (GL) */
static int shown_w, shown_h, shown_ssaa;
int hdgl_init(void);

static const char* r2d_vs =
    "#version 330 core\n"
    "in vec2 a_pos; in vec3 a_uvq;\n"
    "uniform vec4 u_xform; /* scale x, scale y, offset x, offset y in NDC */\n"
    "out vec2 v_uv;\n"
    "void main() { gl_Position = vec4(a_pos * u_xform.xy + u_xform.zw, 0.0, 1.0); v_uv = a_uvq.xy; }\n";

static const char* r2d_fs =
    "#version 330 core\n"
    "uniform int u_mode; uniform vec4 u_color; uniform sampler2D u_tex; uniform int u_ssaa; uniform vec2 u_texsize;\n"
    "in vec2 v_uv; out vec4 o_color;\n"
    "void main() {\n"
    "  if (u_mode == 0) { o_color = u_color; return; }\n"
    "  if (u_mode == 1) { o_color = vec4(u_color.rgb, u_color.a * texture(u_tex, v_uv).r); return; }\n"
    "  if (u_ssaa <= 1) { o_color = vec4(texture(u_tex, v_uv).rgb, 1.0); return; }\n"
    "  ivec2 base = ivec2(floor(v_uv * u_texsize / float(u_ssaa))) * u_ssaa;\n"
    "  vec3 acc = vec3(0.0);\n"
    "  for (int j = 0; j < u_ssaa; j++) for (int i = 0; i < u_ssaa; i++) acc += texelFetch(u_tex, base + ivec2(i, j), 0).rgb;\n"
    "  o_color = vec4(acc / float(u_ssaa * u_ssaa), 1.0);\n"
    "}\n";

static int r2d_gl_init(void) {
    r2d_prog = gl_program(r2d_vs, r2d_fs);
    if (!r2d_prog) return 0;
    r2u_mode = p_glGetUniformLocation(r2d_prog, "u_mode");
    r2u_color = p_glGetUniformLocation(r2d_prog, "u_color");
    r2u_tex = p_glGetUniformLocation(r2d_prog, "u_tex");
    r2u_ssaa = p_glGetUniformLocation(r2d_prog, "u_ssaa");
    r2u_texsize = p_glGetUniformLocation(r2d_prog, "u_texsize");
    r2u_xform = p_glGetUniformLocation(r2d_prog, "u_xform");
    p_glGenVertexArrays(1, &r2d_vao);
    p_glGenBuffers(1, &r2d_vbo);
    p_glBindVertexArray(r2d_vao);
    p_glBindBuffer(GL_ARRAY_BUFFER, r2d_vbo);
    p_glEnableVertexAttribArray(0);
    p_glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float), (void*)0);
    p_glEnableVertexAttribArray(1);
    p_glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float), (void*)(2 * sizeof(float)));
    p_glGenTextures(1, &r2d_frame_tex);
    p_glBindTexture(GL_TEXTURE_2D, r2d_frame_tex);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    return 1;
}

/* map logical coordinates to NDC with letterboxing inside the drawable */
static void r2d_viewport(void) {
    int dw, dh;
    SDL_GL_GetDrawableSize(win, &dw, &dh);
    p_glBindFramebuffer(GL_FRAMEBUFFER, 0);
    p_glViewport(0, 0, dw, dh);
    float lw = (float)window_width(), lh = (float)g_cfg.window_height;
    float s = fminf(dw / lw, dh / lh);
    float ox = (dw - lw * s) * 0.5f, oy = (dh - lh * s) * 0.5f;
    p_glUseProgram(r2d_prog);
    /* ndc.x = (x*s + ox) / dw * 2 - 1 ; ndc.y = 1 - (y*s + oy) / dh * 2 */
    p_glUniform4f(r2u_xform, 2 * s / dw, -2 * s / dh, 2 * ox / dw - 1, 1 - 2 * oy / dh);
}

static void r2d_quad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1) {
    float v[24] = {x0, y0, u0, v0, x1, y0, u1, v0, x0, y1, u0, v1, x1, y0, u1, v0, x1, y1, u1, v1, x0, y1, u0, v1};
    p_glBindVertexArray(r2d_vao);
    p_glBindBuffer(GL_ARRAY_BUFFER, r2d_vbo);
    p_glBufferData(GL_ARRAY_BUFFER, sizeof(v), v, GL_STREAM_DRAW);
    p_glDrawArrays(GL_TRIANGLES, 0, 6);
}

static void r2d_clear(void) {
    if (gl_mode) {
        int dw, dh;
        SDL_GL_GetDrawableSize(win, &dw, &dh);
        p_glBindFramebuffer(GL_FRAMEBUFFER, 0);
        p_glDisable(GL_SCISSOR_TEST);
        p_glViewport(0, 0, dw, dh);
        p_glClearColor(0, 0, 0, 1);
        p_glClear(GL_COLOR_BUFFER_BIT);
        r2d_viewport();
    } else {
        SDL_SetRenderDrawColor(ren, 0, 0, 0, 255);
        SDL_RenderClear(ren);
    }
}

/* draw the current frame into its content rectangle */
static void r2d_frame(void) {
    if (gl_mode) {
        if (!shown_tex) return;
        r2d_viewport();
        p_glDisable(GL_BLEND);
        p_glActiveTexture(GL_TEXTURE0);
        p_glBindTexture(GL_TEXTURE_2D, shown_tex);
        GLint filt = g_cfg.linear_filter ? GL_LINEAR : GL_NEAREST;
        p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filt);
        p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filt);
        p_glUniform1i(r2u_tex, 0);
        p_glUniform1i(r2u_mode, 2);
        p_glUniform1i(r2u_ssaa, shown_ssaa);
        p_glUniform2f(r2u_texsize, (float)shown_w, (float)shown_h);
        r2d_quad((float)last_dst.x, (float)last_dst.y, (float)(last_dst.x + last_dst.w), (float)(last_dst.y + last_dst.h), 0, 0, 1, 1);
    } else if (tex) {
        SDL_RenderCopy(ren, tex, NULL, &last_dst);
    }
}

static void r2d_fill(SDL_Rect r, u32 rgb, int a) {
    if (gl_mode) {
        r2d_viewport();
        p_glEnable(GL_BLEND);
        p_glBlendEquation(GL_FUNC_ADD);
        p_glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        p_glUniform1i(r2u_mode, 0);
        p_glUniform4f(r2u_color, ((rgb >> 16) & 255) / 255.0f, ((rgb >> 8) & 255) / 255.0f, (rgb & 255) / 255.0f, a / 255.0f);
        r2d_quad((float)r.x, (float)r.y, (float)(r.x + r.w), (float)(r.y + r.h), 0, 0, 0, 0);
    } else {
        SDL_SetRenderDrawBlendMode(ren, SDL_BLENDMODE_BLEND);
        SDL_SetRenderDrawColor(ren, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255, (Uint8)a);
        SDL_RenderFillRect(ren, &r);
    }
}

static void r2d_outline(SDL_Rect r, u32 rgb) {
    SDL_Rect t = {r.x, r.y, r.w, 1}, b = {r.x, r.y + r.h - 1, r.w, 1}, l = {r.x, r.y, 1, r.h}, rr = {r.x + r.w - 1, r.y, 1, r.h};
    r2d_fill(t, rgb, 255);
    r2d_fill(b, rgb, 255);
    r2d_fill(l, rgb, 255);
    r2d_fill(rr, rgb, 255);
}

/* glyph from the font atlas (alpha in the atlas) */
static void r2d_glyph(SDL_Rect src, SDL_Rect dst, u32 rgb, const u8* atlas, int aw, int ah) {
    if (gl_mode) {
        if (!r2d_font_tex) {
            p_glGenTextures(1, &r2d_font_tex);
            p_glBindTexture(GL_TEXTURE_2D, r2d_font_tex);
            p_glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
            p_glTexImage2D(GL_TEXTURE_2D, 0, GL_R8, aw, ah, 0, GL_RED, GL_UNSIGNED_BYTE, atlas);
            p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            p_glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        }
        r2d_viewport();
        p_glEnable(GL_BLEND);
        p_glBlendEquation(GL_FUNC_ADD);
        p_glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        p_glActiveTexture(GL_TEXTURE0);
        p_glBindTexture(GL_TEXTURE_2D, r2d_font_tex);
        p_glUniform1i(r2u_tex, 0);
        p_glUniform1i(r2u_mode, 1);
        p_glUniform4f(r2u_color, ((rgb >> 16) & 255) / 255.0f, ((rgb >> 8) & 255) / 255.0f, (rgb & 255) / 255.0f, 1.0f);
        r2d_quad((float)dst.x, (float)dst.y, (float)(dst.x + dst.w), (float)(dst.y + dst.h),
                 (float)src.x / aw, (float)src.y / ah, (float)(src.x + src.w) / aw, (float)(src.y + src.h) / ah);
    } else {
        SDL_SetTextureColorMod(font_tex, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        SDL_RenderCopy(ren, font_tex, &src, &dst);
    }
}

static void r2d_present(void) {
    if (gl_mode) SDL_GL_SwapWindow(win);
    else SDL_RenderPresent(ren);
}

static void r2d_window_to_logical(int x, int y, float* fx, float* fy) {
    if (gl_mode) {
        int ww, wh;
        SDL_GetWindowSize(win, &ww, &wh);
        float lw = (float)window_width(), lh = (float)g_cfg.window_height;
        float s = fminf(ww / lw, wh / lh);
        *fx = (x - (ww - lw * s) * 0.5f) / s;
        *fy = (y - (wh - lh * s) * 0.5f) / s;
    } else {
        SDL_RenderWindowToLogical(ren, x, y, fx, fy);
    }
}

static void r2d_screenshot(const char* path) {
    int w, h;
    if (gl_mode) SDL_GL_GetDrawableSize(win, &w, &h);
    else SDL_GetRendererOutputSize(ren, &w, &h);
    SDL_Surface* shot = SDL_CreateRGBSurfaceWithFormat(0, w, h, 32, SDL_PIXELFORMAT_ARGB8888);
    if (gl_mode) {
        u32* tmp = malloc((size_t)w * h * 4);
        p_glBindFramebuffer(GL_FRAMEBUFFER, 0);
        p_glPixelStorei(GL_PACK_ALIGNMENT, 4);
        p_glReadPixels(0, 0, w, h, GL_BGRA, GL_UNSIGNED_BYTE, tmp);
        for (int y = 0; y < h; y++) memcpy((u8*)shot->pixels + (size_t)y * shot->pitch, tmp + (size_t)(h - 1 - y) * w, (size_t)w * 4);
        free(tmp);
    } else {
        SDL_RenderReadPixels(ren, NULL, SDL_PIXELFORMAT_ARGB8888, shot->pixels, shot->pitch);
    }
    SDL_SaveBMP(shot, path);
    SDL_FreeSurface(shot);
}

static void r2d_set_vsync(int on) {
    if (gl_mode) SDL_GL_SetSwapInterval(on);
    else SDL_RenderSetVSync(ren, on);
}

/* try to create the OpenGL window/context; returns 0 to fall back to the SDL renderer */
static int gl_create(int hidden) {
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_PROFILE_MASK, SDL_GL_CONTEXT_PROFILE_CORE);
    SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);
    Uint32 flags = SDL_WINDOW_OPENGL | SDL_WINDOW_RESIZABLE;
    if (hidden) flags |= SDL_WINDOW_HIDDEN;
    else if (g_cfg.fullscreen) flags |= SDL_WINDOW_FULLSCREEN_DESKTOP;
    win = SDL_CreateWindow("Xena: Warrior Princess", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
                           window_width(), g_cfg.window_height, flags);
    if (!win) return 0;
    glctx = SDL_GL_CreateContext(win);
    if (!glctx || !gl_load() || !r2d_gl_init() || !hdgl_init()) {
        log_msg("OpenGL 3.3 unavailable (%s), using the software renderer", SDL_GetError());
        if (glctx) SDL_GL_DeleteContext(glctx);
        SDL_DestroyWindow(win);
        win = NULL;
        glctx = NULL;
        return 0;
    }
    SDL_GL_SetSwapInterval(g_cfg.vsync);
    gl_mode = 1;
    log_msg("renderer: OpenGL (%s)", (const char*)p_glGetString(GL_RENDERER));
    return 1;
}
