/* Native replacements for selected game/library functions (see recomp/hle.txt). */
#include "hw.h"

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
    if (!g_steer_active) return;
    steer_step(obj, STEER_MAX);
}
