/* Native PC runtime for the statically recompiled Xena: Warrior Princess. */
#include "hw.h"
#include "config.h"
#include <stdlib.h>
#include <SDL.h>

Cpu cpu;
extern u16 g_script_buttons;

static const char* disc_path;
static int opt_scale = 3;
static int opt_render_scale, opt_aspect = -1, opt_vsync;
static const char* opts[16];
static long menu_shot_frame = -1;
static const char* menu_shot_file;
static int nopts;
static long opt_frames = -1;      /* exit after N frames (testing) */
static long opt_shot_every = 0;   /* dump a screenshot every N frames */
static const char* opt_shot_dir = "shots";
long g_frame_no;
#define frame_no g_frame_no
static u32 watch_addr[16];
static int nwatch;
static void pace_frame(void);

/* scripted input: "frame:BUTTON[+BUTTON]:len,..." */
typedef struct { long frame, len; u16 buttons; } ScriptStep;
static ScriptStep script[256];
static int nscript;

static u16 button_from_name(const char* s, int n) {
    static const struct { const char* n; u16 b; } names[] = {
        {"SELECT", 0x0001}, {"START", 0x0008}, {"UP", 0x0010}, {"RIGHT", 0x0020},
        {"DOWN", 0x0040}, {"LEFT", 0x0080}, {"L2", 0x0100}, {"R2", 0x0200},
        {"L1", 0x0400}, {"R1", 0x0800}, {"TRIANGLE", 0x1000}, {"CIRCLE", 0x2000},
        {"CROSS", 0x4000}, {"SQUARE", 0x8000}};
    for (size_t i = 0; i < sizeof(names) / sizeof(names[0]); i++)
        if ((int)strlen(names[i].n) == n && !strncmp(names[i].n, s, n)) return names[i].b;
    return 0;
}

static void parse_script(const char* s) {
    while (*s && nscript < 256) {
        ScriptStep st = {0, 6, 0};
        st.frame = strtol(s, (char**)&s, 10);
        if (*s == ':') s++;
        while (*s && *s != ',' && *s != ':') {
            const char* b = s;
            while (*s && *s != '+' && *s != ',' && *s != ':') s++;
            st.buttons |= button_from_name(b, (int)(s - b));
            if (*s == '+') s++;
        }
        if (*s == ':') { s++; st.len = strtol(s, (char**)&s, 10); }
        script[nscript++] = st;
        if (*s == ',') s++;
    }
}

/* ---------------- control flow ---------------- */

int g_trace;
static u32 call_counts[0x100000 / 4];
void trace_func(u32 addr) {
    u32 i = (addr & 0xFFFFF) >> 2;
    if (call_counts[i]++ == 0) log_msg("trace: frame %ld first call %08X", frame_no, addr);
}

static void dump_call_counts(void) {
    if (!g_trace) return;
    for (int n = 0; n < 40; n++) {
        u32 best = 0, bi = 0;
        for (u32 i = 0; i < 0x100000 / 4; i++)
            if (call_counts[i] > best) { best = call_counts[i]; bi = i; }
        if (!best) break;
        log_msg("calls: %08X %u", 0x80000000u | (bi << 2), best);
        call_counts[bi] = 0;
    }
}

void call_indirect(u32 addr) {
    if (addr == 0xA0 || addr == 0xB0 || addr == 0xC0) {
        bios_call(addr, cpu.r[9]);
        return;
    }
    addr &= 0x9FFFFFFFu | 0x80000000u;
    if ((addr & 0xF0000000u) == 0xA0000000u) addr = (addr & 0x1FFFFFFF) | 0x80000000u;
    int lo = 0, hi = g_func_count - 1;
    while (lo <= hi) {
        int mid = (lo + hi) / 2;
        u32 a = g_func_table[mid].addr;
        if (a == addr) { g_func_table[mid].fn(); return; }
        if (a < addr) lo = mid + 1;
        else hi = mid - 1;
    }
    fatal("call to unknown address %08X (ra=%08X)", addr, cpu.r[31]);
}

void cpu_break(u32 pc, u32 code) {
    static int n;
    if (n++ < 16) log_msg("break %u at %08X", code, pc);
}

void cpu_bad_insn(u32 pc, u32 w) { fatal("unknown instruction %08X at %08X", w, pc); }

/* ---------------- frames ---------------- */

static u32 frame_px[640 * 480];

static void write_bmp(const char* path, const u32* px, int w, int h) {
    FILE* f = fopen(path, "wb");
    if (!f) return;
    u32 row = (u32)w * 3, pad = (4 - row % 4) % 4, size = (row + pad) * h;
    u8 hdr[54] = {'B', 'M'};
    u32 v = 54 + size; memcpy(hdr + 2, &v, 4);
    v = 54; memcpy(hdr + 10, &v, 4);
    v = 40; memcpy(hdr + 14, &v, 4);
    memcpy(hdr + 18, &w, 4); memcpy(hdr + 22, &h, 4);
    hdr[26] = 1; hdr[28] = 24;
    memcpy(hdr + 34, &size, 4);
    fwrite(hdr, 1, 54, f);
    for (int y = h - 1; y >= 0; y--) {
        for (int x = 0; x < w; x++) {
            u32 p = px[y * w + x];
            u8 bgr[3] = {(u8)p, (u8)(p >> 8), (u8)(p >> 16)};
            fwrite(bgr, 1, 3, f);
        }
        static const u8 z[3];
        fwrite(z, 1, pad, f);
    }
    fclose(f);
}

static FILE* wav_file;
static u32 wav_frames;

static void wav_finish(void) {
    if (!wav_file) return;
    u32 data = wav_frames * 4, riff = 36 + data;
    fseek(wav_file, 4, SEEK_SET); fwrite(&riff, 4, 1, wav_file);
    fseek(wav_file, 40, SEEK_SET); fwrite(&data, 4, 1, wav_file);
    fclose(wav_file);
    wav_file = NULL;
}

static void wav_open(const char* path) {
    wav_file = fopen(path, "wb");
    if (!wav_file) return;
    u8 h[44] = {'R','I','F','F',0,0,0,0,'W','A','V','E','f','m','t',' ',16,0,0,0,1,0,2,0,
                0x44,0xAC,0,0,0x10,0xB1,2,0,4,0,16,0,'d','a','t','a',0,0,0,0};
    fwrite(h, 1, 44, wav_file);
    atexit(wav_finish);
}

static void audio_frame(void) {
    static s64 last_cycles;
    static s64 frac;
    s64 elapsed = g_cycles - last_cycles;
    last_cycles = g_cycles;
    s64 num = elapsed * 44100 + frac;
    int n = (int)(num / CPU_HZ);
    frac = num % CPU_HZ;
    if (n <= 0) return;
    if (n > 4096) n = 4096;
    static s16 buf[4096 * 2];
    spu_render(buf, n);
    platform_audio_push(buf, n);
    if (wav_file) { fwrite(buf, 4, n, wav_file); wav_frames += n; }
}

void host_frame(void) {
    frame_no++;
    audio_frame();
    if (nwatch && frame_no % 30 == 0) {
        char line[512];
        int n = SDL_snprintf(line, sizeof(line), "watch f%ld:", frame_no);
        for (int i = 0; i < nwatch; i++)
            n += SDL_snprintf(line + n, sizeof(line) - n, " %08X=%08X", watch_addr[i], mem_r32(watch_addr[i]));
        log_msg("%s", line);
    }
    if (g_verbose && frame_no % 30 == 0) {
        extern u32 g_stat_gp0, g_stat_mdec, g_stat_sectors;
        log_msg("frame %ld: gp0=%u mdec=%u sectors=%u", frame_no, g_stat_gp0, g_stat_mdec, g_stat_sectors);
        g_stat_gp0 = g_stat_mdec = g_stat_sectors = 0;
    }
    if (g_verbose && frame_no % 30 == 0)
        log_msg("frame %ld: SR=%08X istat=%03X imask=%03X etc_init=%04X etc_mask=%04X vbcb=%08X", frame_no, cop0_read(12), g_istat, g_imask, mem_r16(0x800D34F0), mem_r16(0x800D3520), mem_r32(0x800D34F4));
    int w, h;
    extern const u32* gpu_hd_frame(int* w, int* h);
    const u32* shown = gpu_hd_frame(&w, &h);
    if (shown) {
        platform_present(shown, w, h, config_aspect_ratio());
    } else {
        gpu_get_display(frame_px, &w, &h, 640, 480);
        platform_present(frame_px, w, h, 4.0f / 3.0f);
        shown = frame_px;
    }
    if (!platform_pump()) exit(0);
    {
        extern int platform_settings_requested(void);
        extern void platform_settings_menu(void);
        extern const char* g_menu_shot;
        if (frame_no == menu_shot_frame) { g_menu_shot = menu_shot_file; platform_settings_menu(); }
        if (platform_settings_requested()) platform_settings_menu();
    }
    pace_frame();

    g_script_buttons = 0;
    for (int i = 0; i < nscript; i++)
        if (frame_no >= script[i].frame && frame_no < script[i].frame + script[i].len)
            g_script_buttons |= script[i].buttons;

    if (opt_shot_every && frame_no % opt_shot_every == 0) {
        char path[300];
        SDL_snprintf(path, sizeof(path), "%s/frame_%06ld.bmp", opt_shot_dir, frame_no);
        write_bmp(path, shown, w, h);
        if (g_verbose) { /* full VRAM for debugging */
            static u32 vram_px[1024 * 512];
            for (int i = 0; i < 1024 * 512; i++) {
                u16 p = g_vram[i];
                vram_px[i] = ((p & 31) << 19) | (((p >> 5) & 31) << 11) | (((p >> 10) & 31) << 3);
            }
            SDL_snprintf(path, sizeof(path), "%s/vram_%06ld.bmp", opt_shot_dir, frame_no);
            write_bmp(path, vram_px, 1024, 512);
        }
    }
    if (opt_frames >= 0 && frame_no >= opt_frames) {
        log_msg("reached frame %ld, exiting", frame_no);
        dump_call_counts();
        exit(0);
    }
}

void irq_poll(void) {
    hw_run_events();
    irq_deliver();
}

/* keep virtual time from running ahead of real time (called every frame) */
static s64 pace_base_us = -1, pace_base_cycles;

static void pace_frame(void) {
    s64 now = platform_ticks_us();
    if (pace_base_us < 0 || g_turbo || opt_scale <= 0) {
        pace_base_us = now;
        pace_base_cycles = g_cycles;
        return;
    }
    s64 target = pace_base_us + (g_cycles - pace_base_cycles) * 1000000 / CPU_HZ;
    if (target - now > 100000 || now - target > 100000) { /* way off: resync */
        pace_base_us = now;
        pace_base_cycles = g_cycles;
        return;
    }
    while (now < target) {
        if (target - now > 2000) SDL_Delay((Uint32)((target - now) / 1000 - 1));
        now = platform_ticks_us();
    }
}

/* ---------------- loading ---------------- */

static int load_game_exe(u32* pc, u32* gp) {
    u32 lba, size;
    if (!cd_find_file("MEDIA\\XA2.XA", &lba, &size)) {
        log_msg("game executable MEDIA\\XA2.XA not found on disc");
        return 0;
    }
    u8* buf = malloc(size + 2048);
    u8 sec[2352];
    for (u32 off = 0; off < size; off += 2048) {
        cd_read_sector_raw(lba + off / 2048, sec);
        memcpy(buf + off, sec + 24, 2048);
    }
    if (memcmp(buf, "PS-X EXE", 8) != 0) { free(buf); return 0; }
    u32 t_addr, t_size;
    memcpy(pc, buf + 0x10, 4);
    memcpy(gp, buf + 0x14, 4);
    memcpy(&t_addr, buf + 0x18, 4);
    memcpy(&t_size, buf + 0x1C, 4);
    memcpy(&g_ram[t_addr & 0x1FFFFF], buf + 0x800, t_size);
    free(buf);
    log_msg("loaded game executable: %u bytes at %08X, entry %08X", t_size, t_addr, *pc);
    return 1;
}

/* the disc image: next to the program, one folder up, or any .bin there */
static const char* find_disc(void) {
    static char found[1024];
    static const char* dirs[] = {"", "../", "../../"};
    char* base = SDL_GetBasePath();
    for (int pass = 0; pass < 2; pass++) {
        const char* root = pass == 0 ? "" : (base ? base : "");
        for (size_t d = 0; d < 3; d++) {
            SDL_snprintf(found, sizeof(found), "%s%sXena Warrior Princess.bin", root, dirs[d]);
            FILE* f = fopen(found, "rb");
            if (f) { fclose(f); return found; }
        }
    }
    return NULL;
}

/* accept a .cue sheet: use the .bin with the same name */
static const char* resolve_disc_path(const char* p) {
    static char bin[1024];
    size_t n = strlen(p);
    if (n > 4 && !SDL_strcasecmp(p + n - 4, ".cue")) {
        SDL_snprintf(bin, sizeof(bin), "%.*s.bin", (int)(n - 4), p);
        return bin;
    }
    return p;
}

static void usage(void) {
    log_msg("usage: xena [options] [disc.bin]\n"
            "  --scale N        window scale (default 3)\n"
            "  --headless       no window (for automated tests)\n"
            "  --frames N       exit after N frames\n"
            "  --shots N DIR    save a screenshot every N frames into DIR\n"
            "  --script S       scripted input, e.g. 300:START,420:CROSS:10\n"
            "  --verbose        log hardware/BIOS activity\n"
            "keys: arrows, Z/Space=X, X=O, A=Square, S=Triangle, Enter=Start, Backspace=Select,\n"
            "      Q/W=L1/R1, E/R=L2/R2, Tab=fast forward, Esc=quit");
}

#ifdef _WIN32
#include <windows.h>
#endif

int main(int argc, char** argv) {
#ifdef _WIN32
    /* GUI build: without a console, keep the log in xena.log next to the program */
    HANDLE herr = GetStdHandle(STD_ERROR_HANDLE);
    if (herr == NULL || herr == INVALID_HANDLE_VALUE) {
        char* base = SDL_GetBasePath();
        char path[1024];
        SDL_snprintf(path, sizeof(path), "%sxena.log", base ? base : "");
        freopen(path, "w", stderr);
        SDL_snprintf(path, sizeof(path), "%sxena_stdout.log", base ? base : "");
        freopen(path, "w", stdout);
    }
#endif
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--scale") && i + 1 < argc) opt_scale = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--headless")) opt_scale = 0;
        else if (!strcmp(argv[i], "--vsync")) opt_vsync = 1;
        else if (!strcmp(argv[i], "--menu-shot") && i + 2 < argc) { menu_shot_frame = atol(argv[++i]); menu_shot_file = argv[++i]; }
        else if (!strcmp(argv[i], "--opt") && i + 1 < argc && nopts < 16) opts[nopts++] = argv[++i];
        else if (!strcmp(argv[i], "--render-scale") && i + 1 < argc) opt_render_scale = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--aspect") && i + 1 < argc) {
            const char* a = argv[++i];
            opt_aspect = !strcmp(a, "21:9") ? ASPECT_21_9 : !strcmp(a, "16:9") ? ASPECT_16_9 : ASPECT_4_3;
        }
        else if (!strcmp(argv[i], "--frames") && i + 1 < argc) opt_frames = atol(argv[++i]);
        else if (!strcmp(argv[i], "--shots") && i + 2 < argc) { opt_shot_every = atol(argv[++i]); opt_shot_dir = argv[++i]; }
        else if (!strcmp(argv[i], "--script") && i + 1 < argc) parse_script(argv[++i]);
        else if (!strcmp(argv[i], "--verbose")) g_verbose = 1;
        else if (!strcmp(argv[i], "--trace")) g_trace = 1;
        else if (!strcmp(argv[i], "--wav") && i + 1 < argc) wav_open(argv[++i]);
        else if (!strcmp(argv[i], "--watch") && i + 1 < argc) {
            char* p = argv[++i];
            while (*p && nwatch < 16) { watch_addr[nwatch++] = (u32)strtoul(p, &p, 16); if (*p == ',') p++; }
        }
        else if (!strcmp(argv[i], "--help")) { usage(); return 0; }
        else disc_path = argv[i];
    }
    if (disc_path) disc_path = resolve_disc_path(disc_path);
    else disc_path = find_disc();
    if (!disc_path || !cd_open_image(disc_path)) {
        log_msg("cannot open disc image (pass the path to your Xena .bin file)");
        usage();
        return 1;
    }
    {
        char* base = SDL_GetBasePath();
        char ini[1024];
        SDL_snprintf(ini, sizeof(ini), "%sxena.ini", base ? base : "");
        config_load(ini);
        if (opt_render_scale) g_cfg.render_scale = opt_render_scale;
        if (opt_aspect >= 0) g_cfg.aspect = opt_aspect;
        if (opt_vsync) g_cfg.vsync = 1;
        for (int k = 0; k < nopts; k++) {
            char kv[128];
            SDL_snprintf(kv, sizeof(kv), "%s", opts[k]);
            char* eq = SDL_strchr(kv, '=');
            if (eq) { *eq = 0; config_set_video(kv, eq + 1); }
        }
    }
    if (!platform_init(opt_scale)) return 1;
    extern void gpu_hd_configure(void);
    gpu_hd_configure();
    {
        extern void bios_set_card_dir(const char* dir);
        char* base = SDL_GetBasePath();
        char dir[1024];
        SDL_snprintf(dir, sizeof(dir), "%smemcard", base ? base : "");
        bios_set_card_dir(dir);
    }

    gpu_init();
    spu_init();
    mdec_init();
    cd_init();
    bios_init();
    hw_init();
    platform_audio_init();

    u32 pc, gp;
    if (!load_game_exe(&pc, &gp)) return 1;
    cpu.r[29] = 0x801FFF00u;
    cpu.r[30] = cpu.r[29];
    cpu.r[28] = gp;
    call_indirect(pc);
    log_msg("game returned from its entry point");
    return 0;
}
