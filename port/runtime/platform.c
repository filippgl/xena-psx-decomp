/* SDL2 window, input (keyboard, mouse, game controllers), timing and audio. */
#include "hw.h"
#include "config.h"
#include <SDL.h>
#include <math.h>

static SDL_Window* win;
static SDL_Renderer* ren;
static SDL_Texture* tex;
static int tex_w, tex_h, tex_linear;
int g_turbo;
static int headless;
static SDL_Rect last_dst;
static int settings_requested;

/* PS1 button bits (active high here) */
enum {
    B_SELECT = 0x0001, B_L3 = 0x0002, B_R3 = 0x0004, B_START = 0x0008,
    B_UP = 0x0010, B_RIGHT = 0x0020, B_DOWN = 0x0040, B_LEFT = 0x0080,
    B_L2 = 0x0100, B_R2 = 0x0200, B_L1 = 0x0400, B_R1 = 0x0800,
    B_TRIANGLE = 0x1000, B_CIRCLE = 0x2000, B_CROSS = 0x4000, B_SQUARE = 0x8000,
};

u16 g_script_buttons; /* buttons forced by the test script */

s64 platform_ticks_us(void) {
    static u64 freq;
    if (!freq) freq = SDL_GetPerformanceFrequency();
    return (s64)(SDL_GetPerformanceCounter() * 1000000ull / freq);
}

/* ---------------- input state ---------------- */

typedef struct {
    SDL_Keycode key;
    u16 buttons;
    int ff;
} KeyBind;
static KeyBind keybinds[MAX_BINDINGS];
static int nkeybinds;
static u16 keys_down;          /* buttons held through the keyboard */
static int ff_down;

static u16 mouse_buttons[6];   /* SDL button index -> PS1 buttons */
static u16 wheel_up, wheel_down;
static u16 wheel_pulse;        /* wheel presses are held for a few frames */
static int wheel_frames;
static float mouse_acc;        /* horizontal motion waiting to become turn presses */
static int mouse_captured;

#define MAX_PADS 4
static SDL_GameController* pads[MAX_PADS];
static u16 pad_map[SDL_CONTROLLER_BUTTON_MAX];
static u16 trig_l, trig_r;
static u16 rs_left, rs_right, rs_up, rs_down;
static float stick_x, stick_y; /* strongest left stick this frame, +y = down */
static u16 turn_bits;           /* right stick and mouse turning, this frame */

static u16 lookup_binding(const Binding* list, int n, const char* name) {
    for (int i = 0; i < n; i++)
        if (!SDL_strcasecmp(list[i].name, name)) return list[i].buttons;
    return 0;
}

static void input_setup(void) {
    nkeybinds = 0;
    for (int i = 0; i < g_cfg.nkeys && nkeybinds < MAX_BINDINGS; i++) {
        SDL_Keycode k = SDL_GetKeyFromName(g_cfg.keys[i].name);
        if (k == SDLK_UNKNOWN && SDL_strlen(g_cfg.keys[i].name) == 1) k = (SDL_Keycode)SDL_tolower(g_cfg.keys[i].name[0]);
        if (k == SDLK_UNKNOWN) { log_msg("xena.ini: unknown key '%s'", g_cfg.keys[i].name); continue; }
        keybinds[nkeybinds].key = k;
        keybinds[nkeybinds].buttons = g_cfg.keys[i].buttons;
        keybinds[nkeybinds].ff = g_cfg.keys[i].fast_forward;
        nkeybinds++;
    }
    mouse_buttons[SDL_BUTTON_LEFT] = lookup_binding(g_cfg.mouse, g_cfg.nmouse, "Left");
    mouse_buttons[SDL_BUTTON_RIGHT] = lookup_binding(g_cfg.mouse, g_cfg.nmouse, "Right");
    mouse_buttons[SDL_BUTTON_MIDDLE] = lookup_binding(g_cfg.mouse, g_cfg.nmouse, "Middle");
    mouse_buttons[SDL_BUTTON_X1] = lookup_binding(g_cfg.mouse, g_cfg.nmouse, "X1");
    mouse_buttons[SDL_BUTTON_X2] = lookup_binding(g_cfg.mouse, g_cfg.nmouse, "X2");
    wheel_up = lookup_binding(g_cfg.mouse, g_cfg.nmouse, "WheelUp");
    wheel_down = lookup_binding(g_cfg.mouse, g_cfg.nmouse, "WheelDown");

    static const struct { const char* n; SDL_GameControllerButton b; } pb[] = {
        {"A", SDL_CONTROLLER_BUTTON_A}, {"B", SDL_CONTROLLER_BUTTON_B},
        {"X", SDL_CONTROLLER_BUTTON_X}, {"Y", SDL_CONTROLLER_BUTTON_Y},
        {"LeftShoulder", SDL_CONTROLLER_BUTTON_LEFTSHOULDER}, {"RightShoulder", SDL_CONTROLLER_BUTTON_RIGHTSHOULDER},
        {"Start", SDL_CONTROLLER_BUTTON_START}, {"Back", SDL_CONTROLLER_BUTTON_BACK},
        {"Guide", SDL_CONTROLLER_BUTTON_GUIDE}, {"Misc", SDL_CONTROLLER_BUTTON_MISC1},
        {"LeftStick", SDL_CONTROLLER_BUTTON_LEFTSTICK}, {"RightStick", SDL_CONTROLLER_BUTTON_RIGHTSTICK},
    };
    memset(pad_map, 0, sizeof(pad_map));
    for (size_t i = 0; i < sizeof(pb) / sizeof(pb[0]); i++) pad_map[pb[i].b] = lookup_binding(g_cfg.pad, g_cfg.npad, pb[i].n);
    pad_map[SDL_CONTROLLER_BUTTON_DPAD_UP] = B_UP;
    pad_map[SDL_CONTROLLER_BUTTON_DPAD_DOWN] = B_DOWN;
    pad_map[SDL_CONTROLLER_BUTTON_DPAD_LEFT] = B_LEFT;
    pad_map[SDL_CONTROLLER_BUTTON_DPAD_RIGHT] = B_RIGHT;
    trig_l = lookup_binding(g_cfg.pad, g_cfg.npad, "LeftTrigger");
    trig_r = lookup_binding(g_cfg.pad, g_cfg.npad, "RightTrigger");
    rs_left = lookup_binding(g_cfg.pad, g_cfg.npad, "RightStickLeft");
    rs_right = lookup_binding(g_cfg.pad, g_cfg.npad, "RightStickRight");
    rs_up = lookup_binding(g_cfg.pad, g_cfg.npad, "RightStickUp");
    rs_down = lookup_binding(g_cfg.pad, g_cfg.npad, "RightStickDown");
}

static void open_pad(int index) {
    if (!SDL_IsGameController(index)) return;
    for (int i = 0; i < MAX_PADS; i++)
        if (!pads[i]) {
            pads[i] = SDL_GameControllerOpen(index);
            if (pads[i]) log_msg("controller: %s", SDL_GameControllerName(pads[i]));
            return;
        }
}

static void close_pad(SDL_JoystickID id) {
    for (int i = 0; i < MAX_PADS; i++)
        if (pads[i] && SDL_JoystickInstanceID(SDL_GameControllerGetJoystick(pads[i])) == id) {
            SDL_GameControllerClose(pads[i]);
            pads[i] = NULL;
        }
}

static void set_mouse_capture(int on) {
    if (headless || !g_cfg.mouse_enabled) return;
    mouse_captured = on;
    mouse_acc = 0;
    SDL_SetRelativeMouseMode(on ? SDL_TRUE : SDL_FALSE);
}

/* ---------------- window ---------------- */

static int window_width(void) { return (int)(g_cfg.window_height * config_aspect_ratio() + 0.5f); }

static SDL_Texture* font_tex; /* settings menu font (SDL renderer backend) */
#include "render2d.inc"

int platform_init(int scale) {
    headless = scale <= 0;
    SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_PS4_RUMBLE, "1");
    SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_PS5_RUMBLE, "1");
    SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMECONTROLLER | SDL_INIT_AUDIO) != 0 &&
        SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMECONTROLLER) != 0) {
        log_msg("SDL_Init: %s", SDL_GetError());
        return 0;
    }
    input_setup();
    if (headless && !g_cfg.renderer) return 1;
    if (scale > 0 && scale != 3) g_cfg.window_height = 240 * scale;
    if (g_cfg.renderer && gl_create(headless)) {
        for (int i = 0; i < SDL_NumJoysticks(); i++) open_pad(i);
        return 1;
    }
    g_cfg.renderer = 0;
    if (headless) return 1;
    if (scale > 0 && scale != 3) g_cfg.window_height = 240 * scale; /* --scale overrides xena.ini */
    win = SDL_CreateWindow("Xena: Warrior Princess", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
                           window_width(), g_cfg.window_height,
                           SDL_WINDOW_RESIZABLE | (g_cfg.fullscreen ? SDL_WINDOW_FULLSCREEN_DESKTOP : 0));
    if (!win) return 0;
    /* frame pacing is done by the runtime; vsync is optional because a display slower than
     * 60 Hz (e.g. virtual machines) would otherwise slow the whole game down */
    ren = SDL_CreateRenderer(win, -1, SDL_RENDERER_ACCELERATED | (g_cfg.vsync ? SDL_RENDERER_PRESENTVSYNC : 0));
    if (!ren) ren = SDL_CreateRenderer(win, -1, 0);
    SDL_RenderSetLogicalSize(ren, window_width(), g_cfg.window_height);
    for (int i = 0; i < SDL_NumJoysticks(); i++) open_pad(i);
    return 1;
}

static void apply_aspect(void) {
    if (!win) return;
    if (!(SDL_GetWindowFlags(win) & SDL_WINDOW_FULLSCREEN_DESKTOP)) SDL_SetWindowSize(win, window_width(), g_cfg.window_height);
    if (!gl_mode) SDL_RenderSetLogicalSize(ren, window_width(), g_cfg.window_height);
}

static void update_title(void) {
    static int frames;
    static s64 last_us;
    s64 now = platform_ticks_us();
    frames++;
    if (now - last_us >= 1000000) {
        static const char* asp[] = {"4:3", "16:9", "21:9"};
        char title[160];
        SDL_snprintf(title, sizeof(title), "Xena: Warrior Princess  [%d fps | %s %dx | %s]  F1: settings%s",
                     (int)(frames * 1000000LL / (now - last_us)), gl_mode ? "GL" : "CPU", g_cfg.render_scale,
                     asp[g_cfg.aspect], mouse_captured ? "  Alt: release mouse" : "");
        SDL_SetWindowTitle(win, title);
        frames = 0;
        last_us = now;
    }
}

static void content_rect(float content_aspect) {
    int lw = window_width(), lh = g_cfg.window_height;
    int cw = (int)(lh * content_aspect + 0.5f);
    if (cw > lw) cw = lw;
    last_dst.x = (lw - cw) / 2;
    last_dst.y = 0;
    last_dst.w = cw;
    last_dst.h = lh;
}

/* content_aspect: aspect ratio the picture occupies inside the output (4:3 for FMVs) */
void platform_present(const u32* px, int w, int h, float content_aspect) {
    if (headless) return;
    content_rect(content_aspect);
    if (gl_mode) {
        p_glActiveTexture(GL_TEXTURE0);
        p_glBindTexture(GL_TEXTURE_2D, r2d_frame_tex);
        p_glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
        p_glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_BGRA, GL_UNSIGNED_BYTE, px);
        shown_tex = r2d_frame_tex;
        shown_w = w;
        shown_h = h;
        shown_ssaa = 1;
        r2d_clear();
        r2d_frame();
        r2d_present();
        update_title();
        return;
    }
    if (!tex || w != tex_w || h != tex_h || tex_linear != g_cfg.linear_filter) {
        if (tex) SDL_DestroyTexture(tex);
        SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, g_cfg.linear_filter ? "linear" : "nearest");
        tex = SDL_CreateTexture(ren, SDL_PIXELFORMAT_XRGB8888, SDL_TEXTUREACCESS_STREAMING, w, h);
        tex_w = w;
        tex_h = h;
        tex_linear = g_cfg.linear_filter;
    }
    SDL_UpdateTexture(tex, NULL, px, w * 4);
    r2d_clear();
    r2d_frame();
    r2d_present();
    update_title();
}

/* hardware renderer: present a GL texture (the high-resolution surface) */
void platform_present_gl(unsigned gltex, int w, int h, float content_aspect, int ssaa) {
    if (headless) return;
    content_rect(content_aspect);
    shown_tex = gltex;
    shown_w = w;
    shown_h = h;
    shown_ssaa = ssaa;
    r2d_clear();
    r2d_frame();
    r2d_present();
    update_title();
}

int platform_gl(void) { return gl_mode; }

void gpu_hd_configure(void);

static void key_event(SDL_Keycode k, int down) {
    for (int i = 0; i < nkeybinds; i++) {
        if (keybinds[i].key != k) continue;
        if (keybinds[i].ff) ff_down = down;
        else if (down) keys_down |= keybinds[i].buttons;
    }
    if (!down) { /* rebuild from the keys still held: several keys may share a button */
        keys_down = 0;
        const Uint8* st = SDL_GetKeyboardState(NULL);
        for (int j = 0; j < nkeybinds; j++)
            if (!keybinds[j].ff && st[SDL_GetScancodeFromKey(keybinds[j].key)]) keys_down |= keybinds[j].buttons;
    }
}

int platform_pump(void) {
    SDL_Event e;
    if (wheel_frames > 0 && --wheel_frames == 0) wheel_pulse = 0;
    while (SDL_PollEvent(&e)) {
        switch (e.type) {
        case SDL_QUIT: return 0;
        case SDL_KEYDOWN:
            if (e.key.repeat) break;
            if (e.key.keysym.sym == SDLK_F11 ||
                (e.key.keysym.sym == SDLK_RETURN && (e.key.keysym.mod & KMOD_ALT))) {
                int fs = SDL_GetWindowFlags(win) & SDL_WINDOW_FULLSCREEN_DESKTOP;
                SDL_SetWindowFullscreen(win, fs ? 0 : SDL_WINDOW_FULLSCREEN_DESKTOP);
                break;
            }
            if (e.key.keysym.sym == SDLK_F1) { settings_requested = 1; break; }
            if (e.key.keysym.sym == SDLK_F5) { g_cfg.aspect = (g_cfg.aspect + 1) % 3; apply_aspect(); gpu_hd_configure(); break; }
            if (e.key.keysym.sym == SDLK_F6) { g_cfg.render_scale = g_cfg.render_scale % 8 + 1; gpu_hd_configure(); break; }
            if (e.key.keysym.sym == SDLK_LALT || e.key.keysym.sym == SDLK_RALT) set_mouse_capture(0);
            key_event(e.key.keysym.sym, 1);
            break;
        case SDL_KEYUP:
            key_event(e.key.keysym.sym, 0);
            break;
        case SDL_MOUSEBUTTONDOWN:
            /* the mouse is only captured when it is used for turning */
            if (g_cfg.mouse_enabled && g_cfg.mouse_turn && !mouse_captured) set_mouse_capture(1);
            break;
        case SDL_MOUSEWHEEL:
            if (!mouse_captured) break;
            if (e.wheel.y > 0) { wheel_pulse = wheel_up; wheel_frames = 6; }
            else if (e.wheel.y < 0) { wheel_pulse = wheel_down; wheel_frames = 6; }
            break;
        case SDL_MOUSEMOTION:
            if (mouse_captured && g_cfg.mouse_turn) mouse_acc += e.motion.xrel * g_cfg.mouse_sensitivity;
            break;
        case SDL_WINDOWEVENT:
            if (e.window.event == SDL_WINDOWEVENT_FOCUS_LOST) { set_mouse_capture(0); keys_down = 0; ff_down = 0; }
            break;
        case SDL_CONTROLLERDEVICEADDED: open_pad(e.cdevice.which); break;
        case SDL_CONTROLLERDEVICEREMOVED: close_pad(e.cdevice.which); break;
        case SDL_CONTROLLERBUTTONDOWN: {
            /* Back+Start or the Guide button opens the settings */
            SDL_GameController* p = SDL_GameControllerFromInstanceID(e.cbutton.which);
            if (e.cbutton.button == SDL_CONTROLLER_BUTTON_GUIDE ||
                (p && SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_BACK) &&
                 SDL_GameControllerGetButton(p, SDL_CONTROLLER_BUTTON_START)))
                settings_requested = 1;
            break;
        }
        }
    }
    g_turbo = ff_down;
    return 1;
}

/* mouse turning: accumulated motion becomes left/right presses, one per frame */
static u16 mouse_turn_buttons(void) {
    const float step = 6.0f; /* mouse counts per frame of turning */
    u16 b = 0;
    if (mouse_acc > step) { b = B_RIGHT; mouse_acc -= step; }
    else if (mouse_acc < -step) { b = B_LEFT; mouse_acc += step; }
    else mouse_acc *= 0.8f; /* small leftovers fade instead of turning later */
    if (mouse_acc > step * 10) mouse_acc = step * 10;
    if (mouse_acc < -step * 10) mouse_acc = -step * 10;
    return b;
}

static u16 pad_state(SDL_GameController* p) {
    u16 b = 0;
    for (int i = 0; i < SDL_CONTROLLER_BUTTON_MAX; i++)
        if (pad_map[i] && SDL_GameControllerGetButton(p, (SDL_GameControllerButton)i)) b |= pad_map[i];
    if (SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_TRIGGERLEFT) > 12000) b |= trig_l;
    if (SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_TRIGGERRIGHT) > 12000) b |= trig_r;
    /* left stick: 8-way digital directions with a radial deadzone */
    float lx = SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_LEFTX) / 32767.0f;
    float ly = SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_LEFTY) / 32767.0f;
    if (sqrtf(lx * lx + ly * ly) > g_cfg.stick_deadzone) {
        if (lx * lx + ly * ly > stick_x * stick_x + stick_y * stick_y) { stick_x = lx; stick_y = ly; }
        float a = atan2f(ly, lx); /* -pi..pi, 0 = right, +y = down */
        int sector = ((int)floorf(a / (3.14159265f / 4) + 0.5f) + 8) & 7;
        static const u16 dirs[8] = {B_RIGHT, B_RIGHT | B_DOWN, B_DOWN, B_DOWN | B_LEFT,
                                    B_LEFT, B_LEFT | B_UP, B_UP, B_UP | B_RIGHT};
        b |= dirs[sector];
    }
    float rx = SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_RIGHTX) / 32767.0f;
    float ry = SDL_GameControllerGetAxis(p, SDL_CONTROLLER_AXIS_RIGHTY) / 32767.0f;
    /* right stick: turning in the original controls (see pad_buttons) */
    if (rx < -0.5f) turn_bits |= rs_left;
    if (rx > 0.5f) turn_bits |= rs_right;
    if (ry < -0.5f) turn_bits |= rs_up;
    if (ry > 0.5f) turn_bits |= rs_down;
    return b;
}

static u16 frame_buttons;
static long frame_buttons_stamp = -1;
extern long g_frame_no;

/* ---------------- modern (camera-relative) controls ----------------
 * In the original, Up runs along the camera's direction, Down turns her round and Left/
 * Right run in a circle. Here the direction pressed is a direction on screen: it is turned
 * into a heading from the camera's yaw (0x800F0F28), the game is told "run" (Up), and a hook
 * in her control update (probe_player_steer) turns her toward that heading. Angles are
 * 12-bit, increasing to the right; she moves along rotY + 2048 and a camera behind her has
 * yaw = rotY + 2048. Outside her normal movement (menus, cutscenes, attacks, jumps...)
 * the buttons pass through unchanged. */
#define CAM_YAW 0x800F0F28u

static int wrap12(int a) { return ((a + 2048) & 4095) - 2048; }

static int modern_active; /* directions were translated this frame */

static u16 modern_controls(u16 b) {
    static int latched, ref_cam, last_in;
    extern int g_steer_active, g_steer_target;
    extern long g_steer_frame;
    extern u32 g_player_state;
    g_steer_active = modern_active = 0;
    /* standing, running, pivoting; attacks, jumps, climbing... keep the original buttons */
    int moving = g_player_state == 0 || (g_player_state >= 0x14 && g_player_state <= 0x17) ||
                 g_player_state == 0x2B || g_player_state == 0x2C;
    if (!g_cfg.modern_controls || g_frame_no - g_steer_frame > 3 || !moving) { latched = 0; return b; }
    modern_active = 1;
    /* screen-space direction: right = +x, forward (away from the camera) = +y */
    float ix, iy;
    if (stick_x * stick_x + stick_y * stick_y > 0) { ix = stick_x; iy = -stick_y; }
    else {
        ix = (b & B_RIGHT ? 1.f : 0.f) - (b & B_LEFT ? 1.f : 0.f);
        iy = (b & B_UP ? 1.f : 0.f) - (b & B_DOWN ? 1.f : 0.f);
    }
    if (ix == 0 && iy == 0) { latched = 0; return b; }
    int cam = mem_r16(CAM_YAW) & 4095;
    int in = (int)lrintf(atan2f(ix, iy) * (2048.0f / 3.14159265f));
    /* the camera swings round after her: keep the yaw it had when this direction was
     * chosen, otherwise holding Right would run in circles */
    if (!latched || abs(wrap12(in - last_in)) > 400) { ref_cam = cam; latched = 1; }
    last_in = in;
    /* running forward follows the camera (scripted camera turns, corners) */
    if (abs(in) < 512) ref_cam = (ref_cam + wrap12(cam - ref_cam) / 32) & 4095;
    g_steer_target = (ref_cam + in + 2048) & 4095; /* rotY that runs along ref_cam + in */
    g_steer_active = 1;
    return (u16)((b & ~(B_UP | B_DOWN | B_LEFT | B_RIGHT)) | B_UP);
}

u16 pad_buttons(int port) {
    if (port != 0) return 0;
    /* sampled once per frame so turn pulses and wheel presses stay consistent */
    if (frame_buttons_stamp != g_frame_no) {
        frame_buttons_stamp = g_frame_no;
        u16 b = keys_down | g_script_buttons | wheel_pulse;
        if (mouse_captured) {
            Uint32 mb = SDL_GetMouseState(NULL, NULL);
            for (int i = 1; i <= 5; i++)
                if (mb & SDL_BUTTON(i)) b |= mouse_buttons[i];
        }
        stick_x = stick_y = 0;
        turn_bits = 0;
        if (mouse_captured && g_cfg.mouse_turn) turn_bits |= mouse_turn_buttons();
        for (int i = 0; i < MAX_PADS; i++)
            if (pads[i]) b |= pad_state(pads[i]);
        /* tank-style turning only makes sense with the original controls */
        frame_buttons = modern_controls(b);
        if (!modern_active) frame_buttons |= turn_bits;
    }
    return frame_buttons;
}

void pad_poll_host(void) {}

/* DualShock motors: small = on/off, large = 0..255 */
void platform_rumble(int small, int large) {
    if (!g_cfg.rumble) return;
    static int last_small = -1, last_large = -1;
    static s64 last_us;
    s64 now = platform_ticks_us();
    if (small == last_small && large == last_large && now - last_us < 150000) return;
    last_small = small;
    last_large = large;
    last_us = now;
    for (int i = 0; i < MAX_PADS; i++)
        if (pads[i]) SDL_GameControllerRumble(pads[i], (Uint16)(large * 257), small ? 0xFFFF : 0, 250);
}

/* ---------------- audio ---------------- */

static SDL_AudioDeviceID audio_dev;

void platform_audio_init(void) {
    if (headless) return;
    SDL_AudioSpec want = {0}, have;
    want.freq = 44100;
    want.format = AUDIO_S16SYS;
    want.channels = 2;
    want.samples = 512;
    audio_dev = SDL_OpenAudioDevice(NULL, 0, &want, &have, 0);
    if (audio_dev) SDL_PauseAudioDevice(audio_dev, 0);
}

/* queue generated samples; keeps latency bounded (~3 frames) */
void platform_audio_push(const s16* samples, int frames) {
    if (!audio_dev) return;
    u32 queued = SDL_GetQueuedAudioSize(audio_dev) / 4;
    if (queued > 44100 / 60 * 6) return; /* too far ahead (e.g. fast-forward): drop */
    SDL_QueueAudio(audio_dev, samples, (Uint32)frames * 4);
}

#include "menu.inc"
