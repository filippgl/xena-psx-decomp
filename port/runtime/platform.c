/* SDL2 window, input, timing and audio. */
#include "hw.h"
#include <SDL.h>

static SDL_Window* win;
static SDL_Renderer* ren;
static SDL_Texture* tex;
static int tex_w, tex_h;
static SDL_GameController* pad;
static u16 keys_state;
int g_turbo;
static int headless;

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

int g_vsync;

int platform_init(int scale) {
    headless = scale <= 0;
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMECONTROLLER | SDL_INIT_AUDIO) != 0) {
        log_msg("SDL_Init: %s", SDL_GetError());
        return 0;
    }
    if (headless) return 1;
    win = SDL_CreateWindow("Xena: Warrior Princess", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
                           320 * scale, 240 * scale, SDL_WINDOW_RESIZABLE);
    if (!win) return 0;
    /* frame pacing is done by the runtime; vsync is optional because a display slower than
     * 60 Hz (e.g. virtual machines) would otherwise slow the whole game down */
    ren = SDL_CreateRenderer(win, -1, SDL_RENDERER_ACCELERATED | (g_vsync ? SDL_RENDERER_PRESENTVSYNC : 0));
    if (!ren) ren = SDL_CreateRenderer(win, -1, 0);
    SDL_RenderSetLogicalSize(ren, 320 * scale, 240 * scale);
    SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "nearest");
    for (int i = 0; i < SDL_NumJoysticks(); i++)
        if (SDL_IsGameController(i)) { pad = SDL_GameControllerOpen(i); break; }
    return 1;
}

void platform_present(const u32* px, int w, int h) {
    if (headless) return;
    if (!tex || w != tex_w || h != tex_h) {
        if (tex) SDL_DestroyTexture(tex);
        tex = SDL_CreateTexture(ren, SDL_PIXELFORMAT_XRGB8888, SDL_TEXTUREACCESS_STREAMING, w, h);
        tex_w = w;
        tex_h = h;
    }
    SDL_UpdateTexture(tex, NULL, px, w * 4);
    SDL_RenderClear(ren);
    SDL_RenderCopy(ren, tex, NULL, NULL);
    SDL_RenderPresent(ren);

    static int frames;
    static s64 last_us;
    s64 now = platform_ticks_us();
    frames++;
    if (now - last_us >= 1000000) {
        char title[64];
        SDL_snprintf(title, sizeof(title), "Xena: Warrior Princess  [%d fps]", (int)(frames * 1000000LL / (now - last_us)));
        SDL_SetWindowTitle(win, title);
        frames = 0;
        last_us = now;
    }
}

static u16 key_bit(SDL_Keycode k) {
    switch (k) {
    case SDLK_UP: return B_UP;
    case SDLK_DOWN: return B_DOWN;
    case SDLK_LEFT: return B_LEFT;
    case SDLK_RIGHT: return B_RIGHT;
    case SDLK_z: case SDLK_SPACE: return B_CROSS;
    case SDLK_x: return B_CIRCLE;
    case SDLK_a: return B_SQUARE;
    case SDLK_s: return B_TRIANGLE;
    case SDLK_RETURN: return B_START;
    case SDLK_BACKSPACE: case SDLK_RSHIFT: return B_SELECT;
    case SDLK_q: return B_L1;
    case SDLK_w: return B_R1;
    case SDLK_e: return B_L2;
    case SDLK_r: return B_R2;
    }
    return 0;
}

int platform_pump(void) {
    SDL_Event e;
    while (SDL_PollEvent(&e)) {
        switch (e.type) {
        case SDL_QUIT: return 0;
        case SDL_KEYDOWN:
            if (e.key.keysym.sym == SDLK_ESCAPE) return 0;
            if (e.key.keysym.sym == SDLK_F11 ||
                (e.key.keysym.sym == SDLK_RETURN && (e.key.keysym.mod & KMOD_ALT))) {
                int fs = SDL_GetWindowFlags(win) & SDL_WINDOW_FULLSCREEN_DESKTOP;
                SDL_SetWindowFullscreen(win, fs ? 0 : SDL_WINDOW_FULLSCREEN_DESKTOP);
                break;
            }
            if (e.key.keysym.sym == SDLK_TAB) g_turbo = 1;
            keys_state |= key_bit(e.key.keysym.sym);
            break;
        case SDL_KEYUP:
            if (e.key.keysym.sym == SDLK_TAB) g_turbo = 0;
            keys_state &= ~key_bit(e.key.keysym.sym);
            break;
        case SDL_CONTROLLERDEVICEADDED:
            if (!pad) pad = SDL_GameControllerOpen(e.cdevice.which);
            break;
        }
    }
    return 1;
}

u16 pad_buttons(int port) {
    if (port != 0) return 0;
    u16 b = keys_state | g_script_buttons;
    if (pad) {
        static const struct { SDL_GameControllerButton sb; u16 bit; } map[] = {
            {SDL_CONTROLLER_BUTTON_A, B_CROSS}, {SDL_CONTROLLER_BUTTON_B, B_CIRCLE},
            {SDL_CONTROLLER_BUTTON_X, B_SQUARE}, {SDL_CONTROLLER_BUTTON_Y, B_TRIANGLE},
            {SDL_CONTROLLER_BUTTON_START, B_START}, {SDL_CONTROLLER_BUTTON_BACK, B_SELECT},
            {SDL_CONTROLLER_BUTTON_LEFTSHOULDER, B_L1}, {SDL_CONTROLLER_BUTTON_RIGHTSHOULDER, B_R1},
            {SDL_CONTROLLER_BUTTON_DPAD_UP, B_UP}, {SDL_CONTROLLER_BUTTON_DPAD_DOWN, B_DOWN},
            {SDL_CONTROLLER_BUTTON_DPAD_LEFT, B_LEFT}, {SDL_CONTROLLER_BUTTON_DPAD_RIGHT, B_RIGHT},
            {SDL_CONTROLLER_BUTTON_LEFTSTICK, B_L3}, {SDL_CONTROLLER_BUTTON_RIGHTSTICK, B_R3},
        };
        for (size_t i = 0; i < sizeof(map) / sizeof(map[0]); i++)
            if (SDL_GameControllerGetButton(pad, map[i].sb)) b |= map[i].bit;
        if (SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_TRIGGERLEFT) > 16000) b |= B_L2;
        if (SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_TRIGGERRIGHT) > 16000) b |= B_R2;
        int lx = SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_LEFTX);
        int ly = SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_LEFTY);
        if (lx < -16000) b |= B_LEFT;
        if (lx > 16000) b |= B_RIGHT;
        if (ly < -16000) b |= B_UP;
        if (ly > 16000) b |= B_DOWN;
    }
    return b;
}

void pad_poll_host(void) {}

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
