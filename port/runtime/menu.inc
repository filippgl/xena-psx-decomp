/* In-game settings menu (F1, Guide, or Back+Start). Included from platform.c. */
#include "font_data.h"

int platform_settings_requested(void) { return settings_requested; }

static void font_init(void) {
    if (gl_mode || font_tex) return;
    static u32 rgba[FONT_ATLAS_W * FONT_ATLAS_H];
    for (int i = 0; i < FONT_ATLAS_W * FONT_ATLAS_H; i++) rgba[i] = ((u32)font_atlas[i] << 24) | 0xFFFFFF;
    font_tex = SDL_CreateTexture(ren, SDL_PIXELFORMAT_ARGB8888, SDL_TEXTUREACCESS_STATIC, FONT_ATLAS_W, FONT_ATLAS_H);
    SDL_UpdateTexture(font_tex, NULL, rgba, FONT_ATLAS_W * 4);
    SDL_SetTextureBlendMode(font_tex, SDL_BLENDMODE_BLEND);
    SDL_SetTextureScaleMode(font_tex, SDL_ScaleModeLinear);
}

static int glyph_index(u32 cp) {
    for (int i = 0; i < FONT_NGLYPHS; i++)
        if (font_glyphs[i].cp == cp) return i;
    return -1;
}

/* draw UTF-8 text and return its width (scale is relative to the baked font size) */
static int draw_text(const char* s, int x, int y, float scale, u32 rgb, int measure_only) {
    int pen = x;
    const unsigned char* p = (const unsigned char*)s;
    while (*p) {
        u32 cp = *p++;
        if (cp >= 0xC0 && (*p & 0xC0) == 0x80) {
            if (cp >= 0xE0) { cp = ((cp & 0x0F) << 12) | ((u32)(p[0] & 0x3F) << 6) | (p[1] & 0x3F); p += 2; }
            else { cp = ((cp & 0x1F) << 6) | (p[0] & 0x3F); p++; }
        }
        int gi = glyph_index(cp);
        if (gi < 0) gi = glyph_index('?');
        if (!measure_only) {
            SDL_Rect src = {font_glyphs[gi].x, font_glyphs[gi].y, font_glyphs[gi].w, FONT_CELL_H};
            SDL_Rect dst = {pen, y, (int)(font_glyphs[gi].w * scale + 0.5f), (int)(FONT_CELL_H * scale + 0.5f)};
            r2d_glyph(src, dst, rgb, font_atlas, FONT_ATLAS_W, FONT_ATLAS_H);
        }
        pen += (int)(font_glyphs[gi].adv * scale + 0.5f);
    }
    return pen - x;
}

enum {
    MI_RENDERER, MI_SCALE, MI_ASPECT, MI_SSAA, MI_TEXPACK, MI_TEXFILTER, MI_PERSP, MI_SUBPIXEL, MI_UPSCALE,
    MI_FULLSCREEN, MI_VSYNC, MI_WINDOW, MI_RUMBLE, MI_MOUSETURN, MI_MOUSESENS, MI_FASTCD, MI_DUMP, MI_BACK, MI_COUNT
};

static const char* menu_label(int i) {
    switch (i) {
    case MI_RENDERER: return "Рендер";
    case MI_TEXPACK: return "Перерисованные текстуры";
    case MI_SCALE: return "Разрешение рендера";
    case MI_ASPECT: return "Соотношение сторон";
    case MI_SSAA: return "Сглаживание (SSAA)";
    case MI_TEXFILTER: return "Фильтрация текстур";
    case MI_PERSP: return "Коррекция перспективы";
    case MI_SUBPIXEL: return "Точные вершины 3D";
    case MI_UPSCALE: return "Масштабирование кадра";
    case MI_FULLSCREEN: return "Полный экран";
    case MI_VSYNC: return "Вертикальная синхронизация";
    case MI_WINDOW: return "Высота окна";
    case MI_RUMBLE: return "Вибрация геймпада";
    case MI_MOUSETURN: return "Поворот мышью";
    case MI_MOUSESENS: return "Чувствительность мыши";
    case MI_FASTCD: return "Быстрая загрузка";
    case MI_DUMP: return "Сбор текстур";
    default: return "Вернуться в игру";
    }
}

static const char* menu_hint(int i) {
    switch (i) {
    case MI_RENDERER: return "Видеокарта (OpenGL) или процессор. Применяется после перезапуска.";
    case MI_TEXPACK: return "Текстуры в высоком разрешении из папки textures (только видеокарта).";
    case MI_SCALE: return "Чем выше, тем чётче 3D.";
    case MI_ASPECT: return "Широкий экран расширяет обзор; меню и ролики остаются 4:3.";
    case MI_SSAA: return "Убирает лесенки на краях. Тяжело при высоком разрешении.";
    case MI_TEXFILTER: return "Сглаживает текстуры 3D-мира. Интерфейс остаётся чётким.";
    case MI_PERSP: return "Убирает плавание и изломы текстур на больших полигонах.";
    case MI_SUBPIXEL: return "Меньше дрожания полигонов при движении камеры.";
    case MI_UPSCALE: return "Как растягивать кадр до размера окна.";
    case MI_FULLSCREEN: return "Также F11 или Alt+Enter.";
    case MI_VSYNC: return "Убирает разрывы кадра. На мониторах ниже 60 Гц игра замедлится.";
    case MI_WINDOW: return "Размер окна в оконном режиме.";
    case MI_RUMBLE: return "Вибрация DualShock на современных геймпадах.";
    case MI_MOUSETURN: return "Движение мыши влево-вправо поворачивает героиню.";
    case MI_MOUSESENS: return "Скорость поворота мышью.";
    case MI_FASTCD: return "Уровни грузятся в несколько раз быстрее; музыка и ролики не ускоряются.";
    case MI_DUMP: return "Сохранять текстуры уровней в texture_dump для перерисовки.";
    default: return "Esc / F1 / B: закрыть. Настройки сохраняются в xena.ini.";
    }
}

static void menu_value(int i, char* buf, size_t n) {
    static const char* asp[] = {"4:3", "16:9", "21:9"};
    switch (i) {
    case MI_SCALE: {
        int h = 240 * g_cfg.render_scale;
        SDL_snprintf(buf, n, "%dx  (%dx%d)", g_cfg.render_scale, (int)(h * config_aspect_ratio() + 0.5f), h);
        break;
    }
    case MI_ASPECT: SDL_snprintf(buf, n, "%s", asp[g_cfg.aspect]); break;
    case MI_RENDERER: SDL_snprintf(buf, n, "%s%s", g_cfg.renderer ? "Видеокарта" : "Процессор", g_cfg.renderer != gl_mode ? " (после перезапуска)" : ""); break;
    case MI_TEXPACK: SDL_snprintf(buf, n, "%s", g_cfg.texture_pack ? "Вкл" : "Выкл"); break;
    case MI_SSAA: SDL_snprintf(buf, n, "%s", g_cfg.ssaa == 1 ? "Выкл" : g_cfg.ssaa == 2 ? "4x" : "9x"); break;
    case MI_TEXFILTER: SDL_snprintf(buf, n, "%s", g_cfg.texture_filter ? "Билинейная" : "Выкл (как на PS1)"); break;
    case MI_PERSP: SDL_snprintf(buf, n, "%s", g_cfg.perspective ? "Вкл" : "Выкл"); break;
    case MI_SUBPIXEL: SDL_snprintf(buf, n, "%s", g_cfg.subpixel ? "Вкл" : "Выкл"); break;
    case MI_UPSCALE: SDL_snprintf(buf, n, "%s", g_cfg.linear_filter ? "Сглаженное" : "Чёткое"); break;
    case MI_FULLSCREEN: SDL_snprintf(buf, n, "%s", (SDL_GetWindowFlags(win) & SDL_WINDOW_FULLSCREEN_DESKTOP) ? "Вкл" : "Выкл"); break;
    case MI_VSYNC: SDL_snprintf(buf, n, "%s", g_cfg.vsync ? "Вкл" : "Выкл"); break;
    case MI_WINDOW: SDL_snprintf(buf, n, "%d", g_cfg.window_height); break;
    case MI_RUMBLE: SDL_snprintf(buf, n, "%s", g_cfg.rumble ? "Вкл" : "Выкл"); break;
    case MI_MOUSETURN: SDL_snprintf(buf, n, "%s", g_cfg.mouse_turn ? "Вкл" : "Выкл"); break;
    case MI_MOUSESENS: SDL_snprintf(buf, n, "%.2f", g_cfg.mouse_sensitivity); break;
    case MI_DUMP: SDL_snprintf(buf, n, "%s", g_cfg.dump_textures ? "Вкл" : "Выкл"); break;
    case MI_FASTCD: SDL_snprintf(buf, n, "%s", g_cfg.fast_cd ? "Вкл" : "Выкл"); break;
    default: buf[0] = 0; break;
    }
}

/* change item i by dir (-1/+1); returns 1 when the menu should close */
static int menu_change(int i, int dir) {
    static const int heights[] = {480, 720, 900, 1080, 1440, 2160};
    switch (i) {
    case MI_SCALE: g_cfg.render_scale = (g_cfg.render_scale - 1 + dir + 8) % 8 + 1; gpu_hd_configure(); break;
    case MI_ASPECT: g_cfg.aspect = (g_cfg.aspect + dir + 3) % 3; apply_aspect(); gpu_hd_configure(); break;
    case MI_SSAA: g_cfg.ssaa = (g_cfg.ssaa - 1 + dir + 3) % 3 + 1; gpu_hd_configure(); break;
    case MI_TEXFILTER: g_cfg.texture_filter ^= 1; gpu_hd_configure(); break;
    case MI_RENDERER: g_cfg.renderer ^= 1; break;
    case MI_TEXPACK: g_cfg.texture_pack ^= 1; break;
    case MI_PERSP: g_cfg.perspective ^= 1; gpu_hd_configure(); break;
    case MI_SUBPIXEL: g_cfg.subpixel ^= 1; break;
    case MI_UPSCALE: g_cfg.linear_filter ^= 1; break;
    case MI_FULLSCREEN: {
        int fs = SDL_GetWindowFlags(win) & SDL_WINDOW_FULLSCREEN_DESKTOP;
        SDL_SetWindowFullscreen(win, fs ? 0 : SDL_WINDOW_FULLSCREEN_DESKTOP);
        g_cfg.fullscreen = !fs;
        break;
    }
    case MI_VSYNC: g_cfg.vsync ^= 1; r2d_set_vsync(g_cfg.vsync); break;
    case MI_WINDOW: {
        int k = 0;
        for (int j = 0; j < 6; j++)
            if (heights[j] == g_cfg.window_height) k = j;
        k = (k + dir + 6) % 6;
        g_cfg.window_height = heights[k];
        apply_aspect();
        break;
    }
    case MI_RUMBLE: g_cfg.rumble ^= 1; if (g_cfg.rumble) platform_rumble(1, 160); break;
    case MI_MOUSETURN: g_cfg.mouse_turn ^= 1; break;
    case MI_FASTCD: g_cfg.fast_cd ^= 1; break;
    case MI_DUMP: { extern void texdump_set(int on); g_cfg.dump_textures ^= 1; texdump_set(g_cfg.dump_textures); break; }
    case MI_MOUSESENS: {
        float s = g_cfg.mouse_sensitivity + dir * 0.25f;
        g_cfg.mouse_sensitivity = s < 0.25f ? 0.25f : s > 4.0f ? 4.0f : s;
        break;
    }
    default: return 1;
    }
    config_save();
    return 0;
}

static void menu_draw(int sel, SDL_Rect* rows) {
    int lw = window_width(), lh = g_cfg.window_height;
    r2d_clear();
    r2d_frame();
    SDL_Rect all = {0, 0, lw, lh};
    r2d_fill(all, 0x000000, 170);

    float sc = lh / 1080.0f * 1.25f;
    float fit = lh * 0.96f / (FONT_CELL_H * 1.35f * (MI_COUNT + 4));
    if (sc > fit) sc = fit;
    int row_h = (int)(FONT_CELL_H * sc * 1.35f);
    int pw = (int)(1150 * sc), ph = row_h * (MI_COUNT + 4) + row_h / 2;
    if (pw > lw - 20) pw = lw - 20;
    int px = (lw - pw) / 2, py = (lh - ph) / 2;
    SDL_Rect panel = {px, py, pw, ph};
    r2d_fill(panel, 0x12161E, 230);
    r2d_outline(panel, 0xBE9646);

    draw_text("Настройки", px + row_h, py + row_h / 3, sc * 1.25f, 0xF0D080, 0);
    int y = py + row_h * 2;
    for (int i = 0; i < MI_COUNT; i++) {
        if (i == MI_RUMBLE || i == MI_FASTCD || i == MI_BACK) y += row_h / 2;
        SDL_Rect r = {px + row_h / 2, y - row_h / 8, pw - row_h, row_h};
        rows[i] = r;
        if (i == sel) r2d_fill(r, 0xBE9646, 70);
        draw_text(menu_label(i), px + row_h, y, sc, i == sel ? 0xFFFFFF : 0xC8C8C8, 0);
        char val[64];
        menu_value(i, val, sizeof(val));
        if (val[0]) {
            char show[96];
            SDL_snprintf(show, sizeof(show), i == sel ? "\xe2\x86\x90 %s \xe2\x86\x92" : "%s", val);
            int w = draw_text(show, 0, 0, sc, 0, 1);
            draw_text(show, px + pw - row_h - w, y, sc, i == sel ? 0xF0D080 : 0xA0B8D0, 0);
        }
        y += row_h;
    }
    draw_text(menu_hint(sel), px + row_h, py + ph - row_h, sc * 0.8f, 0x9AA0A8, 0);
    r2d_present();
}

const char* g_menu_shot; /* test hook: draw the menu once, save it, close */

void platform_settings_menu(void) {
    settings_requested = 0;
    if (headless) return;
    font_init();
    int was_captured = mouse_captured;
    set_mouse_capture(0);
    keys_down = 0;
    int sel = 0, open = 1;
    SDL_Rect rows[MI_COUNT];
    memset(rows, 0, sizeof(rows));
    u32 pad_prev = ~0u; /* ignore buttons still held from opening the menu */
    while (open) {
        SDL_Event e;
        while (SDL_PollEvent(&e)) {
            switch (e.type) {
            case SDL_QUIT: exit(0);
            case SDL_KEYDOWN:
                switch (e.key.keysym.sym) {
                case SDLK_ESCAPE: case SDLK_F1: case SDLK_BACKSPACE: open = 0; break;
                case SDLK_UP: case SDLK_w: sel = (sel + MI_COUNT - 1) % MI_COUNT; break;
                case SDLK_DOWN: case SDLK_s: sel = (sel + 1) % MI_COUNT; break;
                case SDLK_LEFT: case SDLK_a: if (menu_change(sel, -1)) open = 0; break;
                case SDLK_RIGHT: case SDLK_d: case SDLK_RETURN: case SDLK_SPACE: if (menu_change(sel, 1)) open = 0; break;
                case SDLK_F11: menu_change(MI_FULLSCREEN, 1); break;
                }
                break;
            case SDL_MOUSEMOTION: {
                float fx, fy;
                r2d_window_to_logical(e.motion.x, e.motion.y, &fx, &fy);
                for (int i = 0; i < MI_COUNT; i++)
                    if (fx >= rows[i].x && fx < rows[i].x + rows[i].w && fy >= rows[i].y && fy < rows[i].y + rows[i].h) sel = i;
                break;
            }
            case SDL_MOUSEBUTTONDOWN:
                if (e.button.button == SDL_BUTTON_LEFT) { if (menu_change(sel, 1)) open = 0; }
                else if (e.button.button == SDL_BUTTON_RIGHT) { if (menu_change(sel, -1)) open = 0; }
                break;
            case SDL_MOUSEWHEEL:
                if (e.wheel.y) menu_change(sel, e.wheel.y > 0 ? 1 : -1);
                break;
            case SDL_CONTROLLERDEVICEADDED: open_pad(e.cdevice.which); break;
            case SDL_CONTROLLERDEVICEREMOVED: close_pad(e.cdevice.which); break;
            }
        }
        /* game controllers: edge-triggered navigation */
        u32 pb = 0;
        for (int i = 0; i < MAX_PADS; i++) {
            if (!pads[i]) continue;
            SDL_GameController* p = pads[i];
            if (SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_DPAD_UP) || SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_LEFTY) < -20000) pb |= 1;
            if (SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_DPAD_DOWN) || SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_LEFTY) > 20000) pb |= 2;
            if (SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_DPAD_LEFT) || SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_LEFTX) < -20000) pb |= 4;
            if (SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_DPAD_RIGHT) || SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_LEFTX) > 20000) pb |= 8;
            if (SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_A)) pb |= 16;
            if (SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_B) || SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_START)) pb |= 32;
        }
        u32 edge = pb & ~pad_prev;
        pad_prev = pb;
        if (edge & 1) sel = (sel + MI_COUNT - 1) % MI_COUNT;
        if (edge & 2) sel = (sel + 1) % MI_COUNT;
        if ((edge & 4) && menu_change(sel, -1)) open = 0;
        if ((edge & (8 | 16)) && menu_change(sel, 1)) open = 0;
        if (edge & 32) open = 0;
        menu_draw(sel, rows);
        if (g_menu_shot) {
            menu_draw(sel, rows);
            r2d_screenshot(g_menu_shot);
            g_menu_shot = NULL;
            open = 0;
        }
        SDL_Delay(16);
    }
    config_save();
    if (was_captured) set_mouse_capture(1);
    keys_down = 0;
}
