/* User settings (xena.ini). */
#pragma once
#include "recomp.h"

enum { ASPECT_4_3, ASPECT_16_9, ASPECT_21_9 };

#define MAX_BINDINGS 64

typedef struct {
    char name[32];   /* key / mouse button / pad control name */
    u16 buttons;     /* PS1 buttons it presses */
    int fast_forward;
} Binding;

typedef struct {
    int window_height;
    int render_scale;
    int aspect;
    int fullscreen;
    int vsync;
    int linear_filter;
    int subpixel;
    int mouse_enabled;
    int mouse_turn;
    float mouse_sensitivity;
    int rumble;
    float stick_deadzone;
    Binding keys[MAX_BINDINGS];
    int nkeys;
    Binding mouse[MAX_BINDINGS];
    int nmouse;
    Binding pad[MAX_BINDINGS];
    int npad;
} Config;

extern Config g_cfg;
void config_load(const char* path);
float config_aspect_ratio(void);
u16 config_button(const char* name);
