/* SPU: 24 ADPCM voices with ADSR, noise, sound RAM transfers, and CD-XA audio input.
 * Samples are generated on the main thread in step with the virtual clock (44.1 kHz). */
#include "hw.h"
#include <stdlib.h>

static u16 regs[0x200];  /* 0x1F801C00..0x1F801FFF as halfwords */
static u8 spu_ram[0x80000];
static u32 xfer_addr;    /* current transfer address in bytes */
static u32 endx;
static u16 spucnt, spustat;

#define REG(a) regs[((a) - 0x1F801C00) >> 1]

enum { ENV_OFF, ENV_ATTACK, ENV_DECAY, ENV_SUSTAIN, ENV_RELEASE };

typedef struct {
    u32 addr;          /* current ADPCM block address (bytes) */
    u32 repeat;        /* loop address (bytes) */
    u32 counter;       /* pitch counter: 12 fraction bits, upper bits = sample index in block */
    s16 hist[2];       /* ADPCM filter history */
    s16 samples[28];   /* decoded current block */
    s16 prev[3];       /* previous samples for interpolation */
    int env_state;
    s32 env_level;
    s32 env_wait;
    u8 block_flags;
    int repeat_set;    /* repeat address set by the game since key-on */
} Voice;

static Voice voices[24];
static s32 noise_level = 1, noise_timer;

void spu_init(void) {
    memset(regs, 0, sizeof(regs));
    memset(spu_ram, 0, sizeof(spu_ram));
    memset(voices, 0, sizeof(voices));
}

/* ---------------- ADPCM ---------------- */

static const s32 pos_table[5] = {0, 60, 115, 98, 122};
static const s32 neg_table[5] = {0, 0, -52, -55, -60};

static void decode_block(Voice* v) {
    const u8* b = &spu_ram[v->addr & 0x7FFF0];
    int shift = b[0] & 0xF, filter = (b[0] >> 4) & 7;
    if (shift > 12) shift = 9;
    if (filter > 4) filter = 4;
    v->block_flags = b[1];
    s32 f0 = pos_table[filter], f1 = neg_table[filter];
    for (int i = 0; i < 28; i++) {
        s32 nib = (b[2 + i / 2] >> ((i & 1) * 4)) & 0xF;
        s32 s = (s16)(nib << 12) >> shift;
        s += (v->hist[0] * f0 + v->hist[1] * f1 + 32) >> 6;
        if (s > 32767) s = 32767;
        if (s < -32768) s = -32768;
        v->hist[1] = v->hist[0];
        v->hist[0] = (s16)s;
        v->samples[i] = (s16)s;
    }
}

/* ---------------- ADSR ---------------- */

static void env_tick(Voice* v, int idx) {
    int vi = idx * 8;
    u16 lo = regs[vi + 4], hi = regs[vi + 5];
    int rate, exp, decrease;
    s32 target = -1;
    switch (v->env_state) {
    case ENV_ATTACK: rate = (lo >> 8) & 0x7F; exp = lo >> 15; decrease = 0; break;
    case ENV_DECAY: rate = ((lo >> 4) & 0xF) << 2; exp = 1; decrease = 1; target = ((lo & 0xF) + 1) * 0x800; break;
    case ENV_SUSTAIN: rate = (hi >> 6) & 0x7F; exp = hi >> 15; decrease = (hi >> 14) & 1; break;
    case ENV_RELEASE: rate = (hi & 0x1F) << 2; exp = (hi >> 5) & 1; decrease = 1; break;
    default: return;
    }
    if (v->env_wait > 0) { v->env_wait--; return; }
    int shift = rate >> 2;
    s32 step = decrease ? -8 + (rate & 3) : 7 - (rate & 3);
    s32 cycles = 1 << (shift > 11 ? shift - 11 : 0);
    step <<= (shift < 11 ? 11 - shift : 0);
    if (exp && !decrease && v->env_level > 0x6000) cycles *= 4;
    if (exp && decrease) step = step * v->env_level / 0x8000;
    v->env_wait = cycles - 1;
    v->env_level += step;
    if (v->env_level > 0x7FFF) v->env_level = 0x7FFF;
    if (v->env_level < 0) v->env_level = 0;
    if (v->env_state == ENV_ATTACK && v->env_level >= 0x7FFF) { v->env_state = ENV_DECAY; v->env_wait = 0; }
    else if (v->env_state == ENV_DECAY && v->env_level <= target) { v->env_state = ENV_SUSTAIN; v->env_wait = 0; }
    else if (v->env_state == ENV_RELEASE && v->env_level == 0) v->env_state = ENV_OFF;
}

/* ---------------- voices ---------------- */

static void key_on(int i) {
    Voice* v = &voices[i];
    v->addr = (u32)regs[i * 8 + 3] << 3;
    if (!v->repeat_set) v->repeat = v->addr;
    v->repeat_set = 0;
    v->counter = 0;
    v->hist[0] = v->hist[1] = 0;
    v->prev[0] = v->prev[1] = v->prev[2] = 0;
    v->env_state = ENV_ATTACK;
    v->env_level = 0;
    v->env_wait = 0;
    decode_block(v);
    endx &= ~(1u << i);
}

static void key_off(int i) {
    if (voices[i].env_state != ENV_OFF) {
        voices[i].env_state = ENV_RELEASE;
        voices[i].env_wait = 0;
    }
}

static s32 vol_reg(u16 r) {
    if (r & 0x8000) return 0x3FFF; /* sweep mode: approximated as full */
    return (s32)((s16)(r << 1));   /* 15-bit signed, *2 */
}

static s16 voice_sample(int i, s32* prev_out) {
    Voice* v = &voices[i];
    if (v->env_state == ENV_OFF) { *prev_out = 0; return 0; }
    int idx = v->counter >> 12;
    s32 frac = v->counter & 0xFFF;
    s32 cur = v->samples[idx];
    s32 last = idx > 0 ? v->samples[idx - 1] : v->prev[0];
    s32 s = last + ((cur - last) * frac >> 12); /* linear interpolation */

    u32 step = regs[i * 8 + 2];
    if (step > 0x3FFF) step = 0x4000;
    if (i > 0 && (REG(0x1F801D90) >> i & 1)) { /* pitch modulation by previous voice */
        s32 f = *prev_out + 0x8000;
        step = (u32)((s32)step * f >> 15) & 0xFFFF;
    }
    v->counter += step;
    while ((v->counter >> 12) >= 28) {
        v->counter -= 28 << 12;
        v->prev[0] = v->samples[27];
        u8 fl = v->block_flags;
        if (fl & 4) v->repeat = v->addr;
        if (fl & 1) {
            endx |= 1u << i;
            if (fl & 2) v->addr = v->repeat;
            else { v->env_state = ENV_OFF; v->env_level = 0; v->addr += 16; }
        } else {
            v->addr += 16;
        }
        v->addr &= 0x7FFFF;
        decode_block(v);
        if (v->block_flags & 4) v->repeat = v->addr;
    }
    if ((REG(0x1F801D94) >> i) & 1) s = noise_level; /* noise mode */
    env_tick(v, i);
    s = s * v->env_level >> 15;
    *prev_out = s;
    return (s16)s;
}

/* ---------------- CD-XA audio ---------------- */

#define XA_RING 65536
static s16 xa_ring[XA_RING * 2];
static u32 xa_w, xa_r;          /* in stereo frames */
static u32 xa_rate = 37800;
static u32 xa_frac;             /* resampling position (16.16) */
static s32 xa_hist[2][2];

static void xa_put(s16 l, s16 r) {
    if (xa_w - xa_r >= XA_RING) return; /* overflow: drop */
    xa_ring[(xa_w % XA_RING) * 2] = l;
    xa_ring[(xa_w % XA_RING) * 2 + 1] = r;
    xa_w++;
}

void xa_decode_sector(const u8* raw) {
    static const s32 k0[4] = {0, 60, 115, 98}, k1[4] = {0, 0, -52, -55};
    u8 coding = raw[19];
    int stereo = coding & 1;
    xa_rate = (coding & 4) ? 18900 : 37800;
    if (coding & 0x10) return; /* 8-bit XA: unused here */
    const u8* data = raw + 24;
    static s16 out_l[28 * 8 * 18], out_r[28 * 8 * 18];
    int nl = 0, nr = 0;
    for (int g = 0; g < 18; g++) {
        const u8* grp = data + g * 128;
        for (int unit = 0; unit < 8; unit++) {
            u8 param = grp[4 + unit];
            int shift = param & 0xF;
            int filter = (param >> 4) & 3;
            if (shift > 12) shift = 9;
            int ch = stereo ? (unit & 1) : 0;
            s32* h = xa_hist[ch];
            for (int i = 0; i < 28; i++) {
                u8 byte = grp[16 + i * 4 + unit / 2];
                s32 nib = (unit & 1) ? (byte >> 4) : (byte & 0xF);
                s32 s = (s16)(nib << 12);
                s >>= shift;
                s += (h[0] * k0[filter] + h[1] * k1[filter] + 32) >> 6;
                if (s > 32767) s = 32767;
                if (s < -32768) s = -32768;
                h[1] = h[0];
                h[0] = s;
                if (!stereo || ch == 0) out_l[nl++] = (s16)s;
                else out_r[nr++] = (s16)s;
            }
        }
    }
    for (int i = 0; i < nl; i++) xa_put(out_l[i], stereo ? out_r[i < nr ? i : nr - 1] : out_l[i]);
}

void spu_xa_push(const s16* s, int n, int st, int rate) { (void)s; (void)n; (void)st; (void)rate; }

/* fetch the next XA sample resampled to 44.1 kHz */
static void xa_next(s32* l, s32* r) {
    if (xa_w - xa_r < 2) { *l = *r = 0; return; }
    u32 i0 = xa_r % XA_RING, i1 = (xa_r + 1) % XA_RING;
    s32 f = xa_frac >> 4; /* 12-bit fraction */
    *l = xa_ring[i0 * 2] + ((xa_ring[i1 * 2] - xa_ring[i0 * 2]) * f >> 12);
    *r = xa_ring[i0 * 2 + 1] + ((xa_ring[i1 * 2 + 1] - xa_ring[i0 * 2 + 1]) * f >> 12);
    xa_frac += (u32)((u64)xa_rate * 65536 / 44100);
    xa_r += xa_frac >> 16;
    xa_frac &= 0xFFFF;
}

/* ---------------- reverb (psx-spx algorithm, runs at 22.05 kHz) ---------------- */

static u32 rev_addr; /* current buffer address in bytes */

static inline s16 rev_reg(u32 a) { return (s16)REG(a); }
static inline u32 rev_off(u32 a) { return (u32)REG(a) * 8; }

static inline u32 rev_wrap(u32 off) {
    u32 base = (u32)REG(0x1F801DA2) * 8;
    u32 size = 0x80000 - base;
    if (!size) return 0;
    u32 a = rev_addr + off;
    if (a >= 0x80000) a = base + (a - 0x80000) % size;
    return a & 0x7FFFE;
}

static inline s32 rev_read(u32 off) {
    u32 a = rev_wrap(off);
    return (s16)(spu_ram[a] | (spu_ram[a + 1] << 8));
}

static inline void rev_write(u32 off, s32 v) {
    if (!(spucnt & 0x80)) return; /* reverb master enable gates buffer writes */
    if (v > 32767) v = 32767;
    if (v < -32768) v = -32768;
    u32 a = rev_wrap(off);
    spu_ram[a] = (u8)v;
    spu_ram[a + 1] = (u8)(v >> 8);
}

#define MUL(a, b) (((s32)(a) * (s32)(b)) >> 15)

static void reverb_step(s32 in_l, s32 in_r, s32* out_l, s32* out_r) {
    s32 vIIR = rev_reg(0x1F801DC4), vWALL = rev_reg(0x1F801DCE);
    s32 vAPF1 = rev_reg(0x1F801DD0), vAPF2 = rev_reg(0x1F801DD2);
    u32 dAPF1 = rev_off(0x1F801DC0), dAPF2 = rev_off(0x1F801DC2);
    u32 mLSAME = rev_off(0x1F801DD4), mRSAME = rev_off(0x1F801DD6);
    u32 mLCOMB1 = rev_off(0x1F801DD8), mRCOMB1 = rev_off(0x1F801DDA);
    u32 mLCOMB2 = rev_off(0x1F801DDC), mRCOMB2 = rev_off(0x1F801DDE);
    u32 dLSAME = rev_off(0x1F801DE0), dRSAME = rev_off(0x1F801DE2);
    u32 mLDIFF = rev_off(0x1F801DE4), mRDIFF = rev_off(0x1F801DE6);
    u32 mLCOMB3 = rev_off(0x1F801DE8), mRCOMB3 = rev_off(0x1F801DEA);
    u32 mLCOMB4 = rev_off(0x1F801DEC), mRCOMB4 = rev_off(0x1F801DEE);
    u32 dLDIFF = rev_off(0x1F801DF0), dRDIFF = rev_off(0x1F801DF2);
    u32 mLAPF1 = rev_off(0x1F801DF4), mRAPF1 = rev_off(0x1F801DF6);
    u32 mLAPF2 = rev_off(0x1F801DF8), mRAPF2 = rev_off(0x1F801DFA);
    s32 Lin = MUL(rev_reg(0x1F801DFC), in_l), Rin = MUL(rev_reg(0x1F801DFE), in_r);

    /* same side and different side reflections */
    rev_write(mLSAME, MUL(Lin + MUL(rev_read(dLSAME), vWALL) - rev_read(mLSAME - 2), vIIR) + rev_read(mLSAME - 2));
    rev_write(mRSAME, MUL(Rin + MUL(rev_read(dRSAME), vWALL) - rev_read(mRSAME - 2), vIIR) + rev_read(mRSAME - 2));
    rev_write(mLDIFF, MUL(Lin + MUL(rev_read(dRDIFF), vWALL) - rev_read(mLDIFF - 2), vIIR) + rev_read(mLDIFF - 2));
    rev_write(mRDIFF, MUL(Rin + MUL(rev_read(dLDIFF), vWALL) - rev_read(mRDIFF - 2), vIIR) + rev_read(mRDIFF - 2));

    /* early echo */
    s32 c1 = rev_reg(0x1F801DC6), c2 = rev_reg(0x1F801DC8), c3 = rev_reg(0x1F801DCA), c4 = rev_reg(0x1F801DCC);
    s32 L = MUL(c1, rev_read(mLCOMB1)) + MUL(c2, rev_read(mLCOMB2)) + MUL(c3, rev_read(mLCOMB3)) + MUL(c4, rev_read(mLCOMB4));
    s32 R = MUL(c1, rev_read(mRCOMB1)) + MUL(c2, rev_read(mRCOMB2)) + MUL(c3, rev_read(mRCOMB3)) + MUL(c4, rev_read(mRCOMB4));

    /* late reverb all-pass filters */
    L = L - MUL(vAPF1, rev_read(mLAPF1 - dAPF1)); rev_write(mLAPF1, L); L = MUL(L, vAPF1) + rev_read(mLAPF1 - dAPF1);
    R = R - MUL(vAPF1, rev_read(mRAPF1 - dAPF1)); rev_write(mRAPF1, R); R = MUL(R, vAPF1) + rev_read(mRAPF1 - dAPF1);
    L = L - MUL(vAPF2, rev_read(mLAPF2 - dAPF2)); rev_write(mLAPF2, L); L = MUL(L, vAPF2) + rev_read(mLAPF2 - dAPF2);
    R = R - MUL(vAPF2, rev_read(mRAPF2 - dAPF2)); rev_write(mRAPF2, R); R = MUL(R, vAPF2) + rev_read(mRAPF2 - dAPF2);

    *out_l = MUL(L, rev_reg(0x1F801D84));
    *out_r = MUL(R, rev_reg(0x1F801D86));

    u32 base = (u32)REG(0x1F801DA2) * 8;
    rev_addr = (rev_addr + 2) & 0x7FFFE;
    if (rev_addr < base) rev_addr = base;
}

/* ---------------- mixing ---------------- */

static void noise_tick(void) {
    int shift = (spucnt >> 10) & 0xF, step = ((spucnt >> 8) & 3) + 4;
    noise_timer -= step;
    while (noise_timer < 0) {
        int parity = ((noise_level >> 15) ^ (noise_level >> 12) ^ (noise_level >> 11) ^ (noise_level >> 10) ^ 1) & 1;
        noise_level = (s16)((noise_level << 1) | parity);
        noise_timer += 0x20000 >> shift;
    }
}

void spu_render(s16* out, int frames) {
    s32 mvl = vol_reg(REG(0x1F801D80)), mvr = vol_reg(REG(0x1F801D82));
    s32 cdl = (s16)REG(0x1F801DB0), cdr = (s16)REG(0x1F801DB2);
    int enabled = spucnt & 0x8000;
    u32 eon = REG(0x1F801D98) | ((u32)REG(0x1F801D9A) << 16);
    static s32 rev_in_l, rev_in_r, rev_out_l, rev_out_r;
    static int rev_phase;
    static int no_reverb = -1;
    if (no_reverb < 0) no_reverb = getenv("XENA_NOREVERB") != NULL;
    for (int n = 0; n < frames; n++) {
        s32 L = 0, R = 0, prev = 0, RL = 0, RR = 0;
        noise_tick();
        if (enabled) {
            for (int i = 0; i < 24; i++) {
                s32 s = voice_sample(i, &prev);
                if (!s) continue;
                s32 l = s * vol_reg(regs[i * 8 + 0]) >> 15, r = s * vol_reg(regs[i * 8 + 1]) >> 15;
                L += l;
                R += r;
                if (eon >> i & 1) { RL += l; RR += r; }
            }
        }
        /* reverb runs at half rate on the averaged input; its output is held */
        rev_in_l += RL;
        rev_in_r += RR;
        if (++rev_phase == 2) {
            rev_phase = 0;
            reverb_step(rev_in_l / 2, rev_in_r / 2, &rev_out_l, &rev_out_r);
            rev_in_l = rev_in_r = 0;
        }
        if (!no_reverb) { L += rev_out_l; R += rev_out_r; }
        L = L * mvl >> 15;
        R = R * mvr >> 15;
        s32 xl, xr;
        xa_next(&xl, &xr);
        if (spucnt & 1) { L += xl * cdl >> 15; R += xr * cdr >> 15; }
        if (L > 32767) L = 32767; if (L < -32768) L = -32768;
        if (R > 32767) R = 32767; if (R < -32768) R = -32768;
        out[n * 2] = (s16)L;
        out[n * 2 + 1] = (s16)R;
    }
}

/* ---------------- registers ---------------- */

u16 spu_read(u32 a) {
    if (a < 0x1F801D80) {
        int i = (a - 0x1F801C00) >> 4;
        switch (a & 0xF) {
        case 0xC: return (u16)voices[i].env_level;
        case 0xE: return (u16)(voices[i].repeat >> 3);
        }
        return REG(a);
    }
    switch (a) {
    case 0x1F801D9C: return (u16)endx;
    case 0x1F801D9E: return (u16)(endx >> 16);
    case 0x1F801DA6: return (u16)(xfer_addr >> 3);
    case 0x1F801DAA: return spucnt;
    case 0x1F801DAE: return (spucnt & 0x3F) | (spustat & 0xFFC0);
    case 0x1F801DB8: return REG(0x1F801D80);
    case 0x1F801DBA: return REG(0x1F801D82);
    }
    return REG(a);
}

void spu_write(u32 a, u16 v) {
    REG(a) = v;
    if (a < 0x1F801D80) {
        int i = (a - 0x1F801C00) >> 4;
        if ((a & 0xF) == 0xE) { voices[i].repeat = (u32)v << 3; voices[i].repeat_set = 1; }
        return;
    }
    switch (a) {
    case 0x1F801D88: for (int i = 0; i < 16; i++) if (v >> i & 1) key_on(i); break;
    case 0x1F801D8A: for (int i = 0; i < 8; i++) if (v >> i & 1) key_on(16 + i); break;
    case 0x1F801D8C: for (int i = 0; i < 16; i++) if (v >> i & 1) key_off(i); break;
    case 0x1F801D8E: for (int i = 0; i < 8; i++) if (v >> i & 1) key_off(16 + i); break;
    case 0x1F801D9C: case 0x1F801D9E: break; /* ENDX is read-only */
    case 0x1F801DA6: xfer_addr = (u32)v << 3; break;
    case 0x1F801DA8: /* manual transfer FIFO */
        spu_ram[xfer_addr & 0x7FFFF] = (u8)v;
        spu_ram[(xfer_addr + 1) & 0x7FFFF] = (u8)(v >> 8);
        xfer_addr = (xfer_addr + 2) & 0x7FFFF;
        break;
    case 0x1F801DAA:
        spucnt = v;
        /* transfer mode bits are mirrored in SPUSTAT; DMA requests are always ready */
        spustat = (spustat & ~0x0380u) | ((v & 0x30) == 0x20 ? 0x100 : 0) | ((v & 0x30) == 0x30 ? 0x200 : 0);
        break;
    }
}

void spu_dma_write(u32 w) {
    for (int i = 0; i < 4; i++) spu_ram[(xfer_addr + i) & 0x7FFFF] = (u8)(w >> (i * 8));
    xfer_addr = (xfer_addr + 4) & 0x7FFFF;
}

u32 spu_dma_read(void) {
    u32 w = 0;
    for (int i = 0; i < 4; i++) w |= (u32)spu_ram[(xfer_addr + i) & 0x7FFFF] << (i * 8);
    xfer_addr = (xfer_addr + 4) & 0x7FFFF;
    return w;
}
