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
    int perspective;     /* perspective-correct textures in the HD renderer */
    int texture_filter;  /* 0 nearest, 1 bilinear (3D only) */
    int ssaa;            /* supersampling factor per axis: 1..3 */
    int renderer;        /* 1 = OpenGL, 0 = software */
    int texture_pack;    /* use textures/ replacements (OpenGL) */
    int dump_textures;   /* collect textures into texture_dump/ */
    int fast_cd;         /* faster loading */
    int mouse_enabled;
    int mouse_turn;
    int modern_controls; /* directions are relative to the camera */
    int force_hd;        /* testing: HD renderer even at 1x without enhancements (not saved) */
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
void config_set_video(const char* key, const char* val);
void config_save(void);
float config_aspect_ratio(void);
u16 config_button(const char* name);
