/* Native replacements for selected game/library functions (see recomp/hle.txt). */
#include "hw.h"
#include "config.h"

void hle_nop(void) { cpu.r[2] = 0; }

/* Xena's object, captured every frame by the probe on her control update */
extern long g_frame_no;
u32 g_player_obj;
void probe_player_update(void) {
    if (g_player_obj != cpu.r[4]) log_msg("player object %08X", cpu.r[4]);
    g_player_obj = cpu.r[4];
}

/* Camera-relative controls (see modern_controls in platform.c). Her control update keeps
 * this tick's turn in obj+0x10E: when standing it decides from it whether to start running
 * (turn below the threshold in $s4) or to pivot on the spot, and later applies it to her
 * rotation. Both places are hooked so that, while a heading is requested, the turn is a
 * step toward that heading instead of the game's own steering along the level. */
#define STEER_MAX 80
int g_steer_active, g_steer_target; /* target rotY, 12-bit */
long g_steer_frame = -100;          /* last frame the steering code ran */
u32 g_player_state;                 /* her state then (obj+0xD0) */
/* diagnostic counters: per state, frames reaching the turn hook, frames left unsteered */
u32 g_steer_frames, g_hook_frames[256], g_no_steer_frames[256], g_no_steer_turns[256];

static void steer_step(u32 obj, int limit) {
    u32 tr = mem_r32(mem_r32(obj + 0xA8) + 0x18);
    int d = ((g_steer_target - (int)mem_r16(tr + 0xE) + 2048) & 4095) - 2048;
    int step = abs(d) <= 24 ? d : d / 2;
    if (step > limit) step = limit;
    if (step < -limit) step = -limit;
    mem_w16(obj + 0x10E, (u32)step);
}

/* 8003BF14: run or pivot? */
void probe_player_start(void) {
    u32 obj = cpu.r[17];
    if (obj != g_player_obj || !g_steer_active) return;
    int thr = (s32)cpu.r[20] - 1;
    steer_step(obj, thr < STEER_MAX ? (thr > 0 ? thr : 0) : STEER_MAX);
}

/* 8003C6B4: the turn about to be applied */
void probe_player_steer(void) {
    u32 obj = cpu.r[17];
    if (obj != g_player_obj) return;
    g_steer_frame = g_frame_no;
    g_player_state = mem_r32(obj + 0xD0);
    g_hook_frames[g_player_state & 0xFF]++;
    if (!g_steer_active) {
        /* diagnostic: a direction is held but this state keeps the original buttons, so
         * the game turns her along the level instead of toward the pressed direction */
        extern int g_dir_pressed;
        if (g_cfg.modern_controls && g_dir_pressed) {
            u32 st = g_player_state & 0xFF;
            g_no_steer_frames[st]++;
            if ((s16)mem_r16(obj + 0x10E)) g_no_steer_turns[st]++;
        }
        return;
    }
    g_steer_frames++;
    steer_step(obj, STEER_MAX);
}

/* Only report the states that lose the requested heading; while every held direction was
 * followed this logs nothing, so a normal play session stays quiet. */
void dump_steer_diag(void) {
    int any = 0;
    for (int st = 0; st < 256 && !any; st++) any = g_no_steer_frames[st] != 0;
    if (!any) return;
    log_msg("modern controls: %u frames steered, heading lost in these states:", g_steer_frames);
    for (int st = 0; st < 256; st++)
        if (g_no_steer_frames[st])
            log_msg("  state %02X: turn hook %u frames, heading not followed %u"
                    " (the game turned her in %u)",
                    st, g_hook_frames[st], g_no_steer_frames[st], g_no_steer_turns[st]);
}

/* --verbose: every file the game looks up in XENA.UDA (func_800A4B1C, its CdSearchFile) */
void probe_file_lookup(void) {
    char name[64];
    int i = 0;
    for (u32 a = cpu.r[4]; i < 63 && (name[i] = (char)mem_r8(a)) != 0; a++, i++) {}
    name[i] = 0;
    LOG("file: %s", name);
}

/* Steady 30 fps in play. The game draws a frame, swaps buffers (func_800A5AC8) and starts
 * the next one without waiting for vblank, moving everything a fixed step per frame; on a
 * PlayStation a frame's work fills about two vblanks, here it takes much less emulated
 * time, so pictures came out 1 or 2 vblanks apart: too fast and uneven. In play, the swap
 * now waits (in emulated time, interrupts delivered as usual) until two vblanks have passed
 * since the previous one. */
void probe_frame_swap(void) {
    extern long g_frame_no;
    static long last;
    if (g_cfg.frame_lock && mem_r32(0x800F2648) == 0x0C && g_frame_no - last < 2 && g_frame_no >= last) {
        long target = last + 2;
        while (g_frame_no < target) {
            if (g_next_event > g_cycles) g_cycles = g_next_event;
            irq_poll();
        }
    }
    last = g_frame_no;
}
