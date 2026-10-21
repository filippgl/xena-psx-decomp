/* xena.ini: video options and input bindings. */
#include "config.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>

Config g_cfg = {
    .window_height = 720,
    .render_scale = 3,
    .aspect = ASPECT_16_9,
    .fullscreen = 0,
    .vsync = 0,
    .linear_filter = 0,
    .subpixel = 1,
    .mouse_enabled = 1,
    .mouse_turn = 1,
    .mouse_sensitivity = 1.0f,
    .rumble = 1,
    .stick_deadzone = 0.35f,
};

static const char* default_ini =
    "; Xena: Warrior Princess - PC port settings\n"
    "; Buttons: Cross Circle Square Triangle L1 R1 L2 R2 Start Select Up Down Left Right\n"
    "\n"
    "[video]\n"
    "; window height in pixels (width follows the aspect ratio)\n"
    "window_height = 720\n"
    "; internal resolution multiplier: 1 = original 320x240, 2..8 = sharper 3D\n"
    "render_scale = 3\n"
    "; aspect ratio: 4:3 (original), 16:9, 21:9\n"
    "aspect = 16:9\n"
    "fullscreen = 0\n"
    "vsync = 0\n"
    "; smooth final scaling (0 = sharp pixels)\n"
    "linear_filter = 0\n"
    "; sub-pixel precise 3D vertices (less polygon jitter)\n"
    "subpixel = 1\n"
    "\n"
    "[keyboard]\n"
    "; key names as SDL names them: W, Up, Space, Return, Escape, Left Shift, Keypad 8...\n"
    "Up = W, Up\n"
    "Down = S, Down\n"
    "Left = A, Left\n"
    "Right = D, Right\n"
    "Cross = Space, K\n"
    "Square = J\n"
    "Circle = L\n"
    "Triangle = I\n"
    "L1 = Q\n"
    "R1 = E\n"
    "L2 = Z\n"
    "R2 = C\n"
    "Start = Return, Escape\n"
    "Select = Backspace, Tab\n"
    "FastForward = `\n"
    "\n"
    "[mouse]\n"
    "enabled = 1\n"
    "Left = Square\n"
    "Right = Circle\n"
    "Middle = Triangle\n"
    "X1 = L2\n"
    "X2 = R2\n"
    "WheelUp = L1\n"
    "WheelDown = R1\n"
    "; horizontal mouse movement turns left/right (click the window to capture the mouse,\n"
    "; press Alt to release it)\n"
    "turn = 1\n"
    "sensitivity = 1.0\n"
    "\n"
    "[gamepad]\n"
    "; positions follow an Xbox-style pad: A = bottom, B = right, X = left, Y = top\n"
    "A = Cross\n"
    "B = Circle\n"
    "X = Square\n"
    "Y = Triangle\n"
    "LeftShoulder = L1\n"
    "RightShoulder = R1\n"
    "LeftTrigger = L2\n"
    "RightTrigger = R2\n"
    "Start = Start\n"
    "Back = Select\n"
    "LeftStick = \n"
    "RightStick = \n"
    "; right stick directions (empty = unused)\n"
    "RightStickLeft = Left\n"
    "RightStickRight = Right\n"
    "RightStickUp = \n"
    "RightStickDown = \n"
    "rumble = 1\n"
    "deadzone = 0.35\n";

static char* trim(char* s) {
    while (isspace((unsigned char)*s)) s++;
    char* e = s + strlen(s);
    while (e > s && isspace((unsigned char)e[-1])) *--e = 0;
    return s;
}

u16 config_button(const char* name) {
    static const struct { const char* n; u16 b; } names[] = {
        {"select", 0x0001}, {"l3", 0x0002}, {"r3", 0x0004}, {"start", 0x0008},
        {"up", 0x0010}, {"right", 0x0020}, {"down", 0x0040}, {"left", 0x0080},
        {"l2", 0x0100}, {"r2", 0x0200}, {"l1", 0x0400}, {"r1", 0x0800},
        {"triangle", 0x1000}, {"circle", 0x2000}, {"cross", 0x4000}, {"x", 0x4000}, {"square", 0x8000},
    };
    for (size_t i = 0; i < sizeof(names) / sizeof(names[0]); i++) {
        const char* a = names[i].n;
        const char* b = name;
        while (*a && tolower((unsigned char)*b) == *a) { a++; b++; }
        if (!*a && !*b) return names[i].b;
    }
    return 0;
}

static void add_key(const char* button, const char* keys) {
    u16 b = config_button(button);
    int ff = !strcmp(button, "FastForward");
    if (!b && !ff) return;
    char buf[256];
    snprintf(buf, sizeof(buf), "%s", keys);
    for (char* tok = strtok(buf, ","); tok; tok = strtok(NULL, ",")) {
        char* k = trim(tok);
        if (!*k || g_cfg.nkeys >= MAX_BINDINGS) continue;
        snprintf(g_cfg.keys[g_cfg.nkeys].name, sizeof(g_cfg.keys[0].name), "%s", k);
        g_cfg.keys[g_cfg.nkeys].buttons = b;
        g_cfg.keys[g_cfg.nkeys].fast_forward = ff;
        g_cfg.nkeys++;
    }
}

static void set_binding(Binding* list, int* n, const char* src, const char* value) {
    if (*n >= MAX_BINDINGS) return;
    u16 b = 0;
    char buf[128];
    snprintf(buf, sizeof(buf), "%s", value);
    for (char* tok = strtok(buf, ",+"); tok; tok = strtok(NULL, ",+")) b |= config_button(trim(tok));
    snprintf(list[*n].name, sizeof(list[0].name), "%s", src);
    list[*n].buttons = b;
    (*n)++;
}

static void parse(char* text) {
    char section[32] = "";
    for (char* line = strtok(text, "\n"); line; line = strtok(NULL, "\n")) {
        char* l = trim(line);
        if (!*l || *l == ';' || *l == '#') continue;
        if (*l == '[') {
            char* e = strchr(l, ']');
            if (e) *e = 0;
            snprintf(section, sizeof(section), "%s", l + 1);
            continue;
        }
        char* eq = strchr(l, '=');
        if (!eq) continue;
        *eq = 0;
        char* key = trim(l);
        char* val = trim(eq + 1);
        if (!strcmp(section, "video")) {
            if (!strcmp(key, "window_height")) g_cfg.window_height = atoi(val);
            else if (!strcmp(key, "render_scale")) g_cfg.render_scale = atoi(val);
            else if (!strcmp(key, "aspect")) g_cfg.aspect = !strcmp(val, "21:9") ? ASPECT_21_9 : !strcmp(val, "16:9") ? ASPECT_16_9 : ASPECT_4_3;
            else if (!strcmp(key, "fullscreen")) g_cfg.fullscreen = atoi(val);
            else if (!strcmp(key, "vsync")) g_cfg.vsync = atoi(val);
            else if (!strcmp(key, "linear_filter")) g_cfg.linear_filter = atoi(val);
            else if (!strcmp(key, "subpixel")) g_cfg.subpixel = atoi(val);
        } else if (!strcmp(section, "keyboard")) {
            /* rescanned in a second pass (strtok is not reentrant) */
        } else if (!strcmp(section, "mouse")) {
            if (!strcmp(key, "enabled")) g_cfg.mouse_enabled = atoi(val);
            else if (!strcmp(key, "turn")) g_cfg.mouse_turn = atoi(val);
            else if (!strcmp(key, "sensitivity")) g_cfg.mouse_sensitivity = (float)atof(val);
        } else if (!strcmp(section, "gamepad")) {
            if (!strcmp(key, "rumble")) g_cfg.rumble = atoi(val);
            else if (!strcmp(key, "deadzone")) g_cfg.stick_deadzone = (float)atof(val);
        }
    }
}

/* bindings need nested tokenizing, so they are parsed line by line separately */
static void parse_bindings(const char* text) {
    char section[32] = "";
    const char* p = text;
    while (*p) {
        const char* e = strchr(p, '\n');
        size_t n = e ? (size_t)(e - p) : strlen(p);
        char line[512];
        if (n >= sizeof(line)) n = sizeof(line) - 1;
        memcpy(line, p, n);
        line[n] = 0;
        p += n + (e ? 1 : 0);
        char* l = trim(line);
        if (!*l || *l == ';' || *l == '#') continue;
        if (*l == '[') {
            char* b = strchr(l, ']');
            if (b) *b = 0;
            snprintf(section, sizeof(section), "%s", l + 1);
            continue;
        }
        char* eq = strchr(l, '=');
        if (!eq) continue;
        *eq = 0;
        char* key = trim(l);
        char* val = trim(eq + 1);
        if (!strcmp(section, "keyboard")) add_key(key, val);
        else if (!strcmp(section, "mouse") && strcmp(key, "enabled") && strcmp(key, "turn") && strcmp(key, "sensitivity"))
            set_binding(g_cfg.mouse, &g_cfg.nmouse, key, val);
        else if (!strcmp(section, "gamepad") && strcmp(key, "rumble") && strcmp(key, "deadzone"))
            set_binding(g_cfg.pad, &g_cfg.npad, key, val);
    }
}

void config_load(const char* path) {
    FILE* f = fopen(path, "rb");
    if (!f) {
        f = fopen(path, "wb");
        if (f) { fputs(default_ini, f); fclose(f); }
        f = fopen(path, "rb");
    }
    char* text;
    if (f) {
        fseek(f, 0, SEEK_END);
        long n = ftell(f);
        fseek(f, 0, SEEK_SET);
        text = malloc((size_t)n + 1);
        n = (long)fread(text, 1, (size_t)n, f);
        text[n] = 0;
        fclose(f);
    } else {
        text = strdup(default_ini);
    }
    for (char* c = text; *c; c++) if (*c == '\r') *c = '\n';
    char* copy = strdup(text);
    parse(copy);
    free(copy);
    parse_bindings(text);
    free(text);
    if (g_cfg.render_scale < 1) g_cfg.render_scale = 1;
    if (g_cfg.render_scale > 8) g_cfg.render_scale = 8;
    if (g_cfg.window_height < 240) g_cfg.window_height = 240;
}

float config_aspect_ratio(void) {
    switch (g_cfg.aspect) {
    case ASPECT_16_9: return 16.0f / 9.0f;
    case ASPECT_21_9: return 21.0f / 9.0f;
    default: return 4.0f / 3.0f;
    }
}
