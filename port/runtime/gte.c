/* Geometry Transformation Engine (COP2), following the psx-spx specification. */
#include "recomp.h"

extern int g_gte_ws_num, g_gte_ws_den;
void gte_tag_vertex(int x, int y, float fx, float fy, float z);

static s32 D[32]; /* data registers */
static s32 C[32]; /* control registers */
static u32 FLAG;

/* register aliases */
#define VX(n) ((s16)D[(n) * 2])
#define VY(n) ((s16)(D[(n) * 2] >> 16))
#define VZ(n) ((s16)D[(n) * 2 + 1])
#define RGBC D[6]
#define OTZ D[7]
#define IR0 D[8]
#define IR1 D[9]
#define IR2 D[10]
#define IR3 D[11]
#define SXY0 D[12]
#define SXY1 D[13]
#define SXY2 D[14]
#define SZ0 D[16]
#define SZ1 D[17]
#define SZ2 D[18]
#define SZ3 D[19]
#define RGB0 D[20]
#define RGB1 D[21]
#define RGB2 D[22]
#define MAC0 D[24]
#define MAC1 D[25]
#define MAC2 D[26]
#define MAC3 D[27]
#define LZCS D[30]
#define LZCR D[31]

#define TRX C[5]
#define TRY C[6]
#define TRZ C[7]
#define RBK C[13]
#define GBK C[14]
#define BBK C[15]
#define RFC C[21]
#define GFC C[22]
#define BFC C[23]
#define OFX C[24]
#define OFY C[25]
#define H ((u16)C[26])
#define DQA ((s16)C[27])
#define DQB C[28]
#define ZSF3 ((s16)C[29])
#define ZSF4 ((s16)C[30])

/* matrix element access: m = 0 rotation, 1 light, 2 color */
static inline s16 M(int m, int i, int j) {
    int idx = i * 3 + j;
    s32 w = C[m * 8 + (idx >> 1)];
    return (idx & 1) ? (s16)(w >> 16) : (s16)w;
}

/* ---------------- flags / saturation ---------------- */

static inline s64 mac_check(int n, s64 v) {
    if (v > 0x7FFFFFFFFFFLL) FLAG |= 1u << (31 - n);      /* MACn positive overflow (bits 30..28) */
    else if (v < -0x80000000000LL) FLAG |= 1u << (28 - n); /* negative (bits 27..25) */
    return ((s64)(v << 20)) >> 20;                         /* wrap to 44 bits */
}

static inline s32 set_mac(int n, s64 v, int shift) {
    v = mac_check(n, v);
    s32 r = (s32)(v >> shift);
    D[24 + n] = r;
    return r;
}

static inline void set_mac0(s64 v) {
    if (v > 0x7FFFFFFFLL) FLAG |= 1u << 16;
    else if (v < -0x80000000LL) FLAG |= 1u << 15;
    MAC0 = (s32)v;
}

static inline s32 sat_ir(int n, s32 v, int lm) {
    s32 lo = lm ? 0 : -0x8000;
    if (v < lo) { FLAG |= 1u << (25 - n); return lo; }
    if (v > 0x7FFF) { FLAG |= 1u << (25 - n); return 0x7FFF; }
    return v;
}

static inline void set_ir(int n, s32 v, int lm) { D[8 + n] = sat_ir(n, v, lm); }

static inline void set_ir0(s64 v) {
    if (v < 0) { FLAG |= 1u << 12; v = 0; }
    else if (v > 0x1000) { FLAG |= 1u << 12; v = 0x1000; }
    IR0 = (s32)v;
}

static inline s32 sat_color(int n, s32 v) {
    if (v < 0) { FLAG |= 1u << (22 - n); return 0; }
    if (v > 255) { FLAG |= 1u << (22 - n); return 255; }
    return v;
}

static inline void push_sz(s64 v) {
    SZ0 = SZ1; SZ1 = SZ2; SZ2 = SZ3;
    if (v < 0) { FLAG |= 1u << 18; v = 0; }
    else if (v > 0xFFFF) { FLAG |= 1u << 18; v = 0xFFFF; }
    SZ3 = (s32)v;
}

static inline void push_sxy(s64 x, s64 y) {
    if (x < -0x400) { FLAG |= 1u << 14; x = -0x400; }
    else if (x > 0x3FF) { FLAG |= 1u << 14; x = 0x3FF; }
    if (y < -0x400) { FLAG |= 1u << 13; y = -0x400; }
    else if (y > 0x3FF) { FLAG |= 1u << 13; y = 0x3FF; }
    SXY0 = SXY1; SXY1 = SXY2;
    SXY2 = (s32)(((u32)(u16)(s16)y << 16) | (u16)(s16)x);
}

static inline void push_color(void) {
    s32 r = sat_color(1, MAC1 >> 4), g = sat_color(2, MAC2 >> 4), b = sat_color(3, MAC3 >> 4);
    RGB0 = RGB1; RGB1 = RGB2;
    RGB2 = (s32)((u32)r | ((u32)g << 8) | ((u32)b << 16) | ((u32)RGBC & 0xFF000000u));
}

/* ---------------- UNR division ---------------- */

static const u8 unr_table[0x101] = {
    0xFF,0xFD,0xFB,0xF9,0xF7,0xF5,0xF3,0xF1,0xEF,0xEE,0xEC,0xEA,0xE8,0xE6,0xE4,0xE3,
    0xE1,0xDF,0xDD,0xDC,0xDA,0xD8,0xD6,0xD5,0xD3,0xD1,0xD0,0xCE,0xCD,0xCB,0xC9,0xC8,
    0xC6,0xC5,0xC3,0xC1,0xC0,0xBE,0xBD,0xBB,0xBA,0xB8,0xB7,0xB5,0xB4,0xB2,0xB1,0xB0,
    0xAE,0xAD,0xAB,0xAA,0xA9,0xA7,0xA6,0xA4,0xA3,0xA2,0xA0,0x9F,0x9E,0x9C,0x9B,0x9A,
    0x99,0x97,0x96,0x95,0x94,0x92,0x91,0x90,0x8F,0x8D,0x8C,0x8B,0x8A,0x89,0x87,0x86,
    0x85,0x84,0x83,0x82,0x81,0x7F,0x7E,0x7D,0x7C,0x7B,0x7A,0x79,0x78,0x77,0x75,0x74,
    0x73,0x72,0x71,0x70,0x6F,0x6E,0x6D,0x6C,0x6B,0x6A,0x69,0x68,0x67,0x66,0x65,0x64,
    0x63,0x62,0x61,0x60,0x5F,0x5E,0x5D,0x5D,0x5C,0x5B,0x5A,0x59,0x58,0x57,0x56,0x55,
    0x54,0x53,0x53,0x52,0x51,0x50,0x4F,0x4E,0x4D,0x4D,0x4C,0x4B,0x4A,0x49,0x48,0x48,
    0x47,0x46,0x45,0x44,0x43,0x43,0x42,0x41,0x40,0x3F,0x3F,0x3E,0x3D,0x3C,0x3C,0x3B,
    0x3A,0x39,0x39,0x38,0x37,0x36,0x36,0x35,0x34,0x33,0x33,0x32,0x31,0x31,0x30,0x2F,
    0x2E,0x2E,0x2D,0x2C,0x2C,0x2B,0x2A,0x2A,0x29,0x28,0x28,0x27,0x26,0x26,0x25,0x24,
    0x24,0x23,0x22,0x22,0x21,0x20,0x20,0x1F,0x1E,0x1E,0x1D,0x1D,0x1C,0x1B,0x1B,0x1A,
    0x19,0x19,0x18,0x18,0x17,0x16,0x16,0x15,0x15,0x14,0x14,0x13,0x12,0x12,0x11,0x11,
    0x10,0x0F,0x0F,0x0E,0x0E,0x0D,0x0D,0x0C,0x0C,0x0B,0x0A,0x0A,0x09,0x09,0x08,0x08,
    0x07,0x07,0x06,0x06,0x05,0x05,0x04,0x04,0x03,0x03,0x02,0x02,0x01,0x01,0x00,0x00,
    0x00};

static u32 gte_divide(u32 h, u32 sz3) {
    if (h >= sz3 * 2) {
        FLAG |= 1u << 17;
        return 0x1FFFF;
    }
    int z = 0;
    for (u32 t = sz3 & 0xFFFF; z < 16 && !(t & 0x8000); t <<= 1) z++;
    u32 n = h << z;
    u32 d = (sz3 << z) & 0xFFFF;
    u32 u = unr_table[(d - 0x7FC0) >> 7] + 0x101;
    d = (0x2000080 - (d * u)) >> 8;
    d = (0x0000080 + (d * u)) >> 8;
    u32 r = (u32)(((u64)n * d + 0x8000) >> 16);
    return r > 0x1FFFF ? 0x1FFFF : r;
}

/* ---------------- commands ---------------- */

static void rtp(int v, int sf, int lm, int last) {
    s64 x = (s64)TRX * 0x1000 + M(0, 0, 0) * VX(v) + M(0, 0, 1) * VY(v) + M(0, 0, 2) * VZ(v);
    s64 y = (s64)TRY * 0x1000 + M(0, 1, 0) * VX(v) + M(0, 1, 1) * VY(v) + M(0, 1, 2) * VZ(v);
    s64 z = (s64)TRZ * 0x1000 + M(0, 2, 0) * VX(v) + M(0, 2, 1) * VY(v) + M(0, 2, 2) * VZ(v);
    int shift = sf ? 12 : 0;
    set_mac(1, x, shift);
    set_mac(2, y, shift);
    set_mac(3, z, shift);
    set_ir(1, MAC1, lm);
    set_ir(2, MAC2, lm);
    /* IR3 saturation flag uses the unshifted z>>12 regardless of sf */
    {
        s32 z12 = (s32)(mac_check(3, z) >> 12);
        s32 lo = lm ? 0 : -0x8000;
        IR3 = MAC3 < lo ? lo : (MAC3 > 0x7FFF ? 0x7FFF : MAC3);
        if (z12 < -0x8000 || z12 > 0x7FFF) FLAG |= 1u << 22;
    }
    push_sz(mac_check(3, z) >> 12);
    u32 div = gte_divide(H, (u32)SZ3);
    /* widescreen: squeeze X towards the centre; the HD renderer stretches it back */
    s64 sx = (s64)div * (s16)IR1 * g_gte_ws_num / g_gte_ws_den + OFX;
    s64 sy = (s64)div * (s16)IR2 + OFY;
    set_mac0(sx);
    set_mac0(sy);
    push_sxy(sx >> 16, sy >> 16);
    gte_tag_vertex((s16)SXY2, (s16)(SXY2 >> 16), (float)((double)sx / 65536.0), (float)((double)sy / 65536.0), (float)SZ3);
    if (last) {
        s64 dq = (s64)DQA * div + DQB;
        set_mac0(dq);
        set_ir0(dq >> 12);
    }
}

static void mvmva(int sf, int lm, int mx, int vsel, int cv) {
    s16 vx, vy, vz;
    if (vsel < 3) { vx = VX(vsel); vy = VY(vsel); vz = VZ(vsel); }
    else { vx = (s16)IR1; vy = (s16)IR2; vz = (s16)IR3; }
    s64 tx = 0, ty = 0, tz = 0;
    if (cv == 0) { tx = TRX; ty = TRY; tz = TRZ; }
    else if (cv == 1) { tx = RBK; ty = GBK; tz = BBK; }
    else if (cv == 2) { tx = RFC; ty = GFC; tz = BFC; }
    int shift = sf ? 12 : 0;
    if (mx == 3) {
        /* garbage matrix: reproduce the documented hardware result */
        s16 r = (s16)(RGBC & 0xFF) << 4;
        s64 m[3][3] = {{-r, r, IR0}, {M(0, 0, 2), M(0, 0, 2), M(0, 0, 2)}, {M(0, 1, 1), M(0, 1, 1), M(0, 1, 1)}};
        set_mac(1, tx * 0x1000 + m[0][0] * vx + m[0][1] * vy + m[0][2] * vz, shift);
        set_mac(2, ty * 0x1000 + m[1][0] * vx + m[1][1] * vy + m[1][2] * vz, shift);
        set_mac(3, tz * 0x1000 + m[2][0] * vx + m[2][1] * vy + m[2][2] * vz, shift);
    } else if (cv == 2) {
        /* far-color bug: the translation part only affects flags */
        for (int i = 0; i < 3; i++) {
            s64 t = (i == 0 ? tx : i == 1 ? ty : tz) * 0x1000 + M(mx, i, 0) * vx;
            sat_ir(i + 1, (s32)(mac_check(i + 1, t) >> shift), 0);
            set_mac(i + 1, (s64)M(mx, i, 1) * vy + (s64)M(mx, i, 2) * vz, shift);
        }
    } else {
        set_mac(1, tx * 0x1000 + M(mx, 0, 0) * vx + M(mx, 0, 1) * vy + M(mx, 0, 2) * vz, shift);
        set_mac(2, ty * 0x1000 + M(mx, 1, 0) * vx + M(mx, 1, 1) * vy + M(mx, 1, 2) * vz, shift);
        set_mac(3, tz * 0x1000 + M(mx, 2, 0) * vx + M(mx, 2, 1) * vy + M(mx, 2, 2) * vz, shift);
    }
    set_ir(1, MAC1, lm);
    set_ir(2, MAC2, lm);
    set_ir(3, MAC3, lm);
}

/* light matrix * vector -> IR, then color matrix with background color */
static void ncx_light(int v, int sf, int lm) {
    int shift = sf ? 12 : 0;
    s16 vx = VX(v), vy = VY(v), vz = VZ(v);
    set_mac(1, (s64)M(1, 0, 0) * vx + M(1, 0, 1) * vy + M(1, 0, 2) * vz, shift);
    set_mac(2, (s64)M(1, 1, 0) * vx + M(1, 1, 1) * vy + M(1, 1, 2) * vz, shift);
    set_mac(3, (s64)M(1, 2, 0) * vx + M(1, 2, 1) * vy + M(1, 2, 2) * vz, shift);
    set_ir(1, MAC1, lm);
    set_ir(2, MAC2, lm);
    set_ir(3, MAC3, lm);
    s64 r = (s64)RBK * 0x1000 + M(2, 0, 0) * (s16)IR1 + M(2, 0, 1) * (s16)IR2 + M(2, 0, 2) * (s16)IR3;
    s64 g = (s64)GBK * 0x1000 + M(2, 1, 0) * (s16)IR1 + M(2, 1, 1) * (s16)IR2 + M(2, 1, 2) * (s16)IR3;
    s64 b = (s64)BBK * 0x1000 + M(2, 2, 0) * (s16)IR1 + M(2, 2, 1) * (s16)IR2 + M(2, 2, 2) * (s16)IR3;
    set_mac(1, r, shift);
    set_mac(2, g, shift);
    set_mac(3, b, shift);
    set_ir(1, MAC1, lm);
    set_ir(2, MAC2, lm);
    set_ir(3, MAC3, lm);
}

static void color_mul(int sf, int lm) { /* MAC = RGB * IR << 4 */
    int shift = sf ? 12 : 0;
    s32 r = RGBC & 0xFF, g = (RGBC >> 8) & 0xFF, b = (RGBC >> 16) & 0xFF;
    set_mac(1, ((s64)r * (s16)IR1) << 4, shift);
    set_mac(2, ((s64)g * (s16)IR2) << 4, shift);
    set_mac(3, ((s64)b * (s16)IR3) << 4, shift);
    set_ir(1, MAC1, lm);
    set_ir(2, MAC2, lm);
    set_ir(3, MAC3, lm);
}

/* depth cue interpolation of MAC towards far color, input MAC1-3 already scaled */
static void depth_cue(s64 m1, s64 m2, s64 m3, int sf, int lm) {
    int shift = sf ? 12 : 0;
    s32 t1 = sat_ir(1, (s32)(mac_check(1, ((s64)RFC << 12) - m1) >> shift), 0);
    s32 t2 = sat_ir(2, (s32)(mac_check(2, ((s64)GFC << 12) - m2) >> shift), 0);
    s32 t3 = sat_ir(3, (s32)(mac_check(3, ((s64)BFC << 12) - m3) >> shift), 0);
    set_mac(1, m1 + (s64)t1 * (s16)IR0, shift);
    set_mac(2, m2 + (s64)t2 * (s16)IR0, shift);
    set_mac(3, m3 + (s64)t3 * (s16)IR0, shift);
    set_ir(1, MAC1, lm);
    set_ir(2, MAC2, lm);
    set_ir(3, MAC3, lm);
}

static void nc(int v, int sf, int lm, int mode) { /* mode 0 NCS, 1 NCCS, 2 NCDS */
    ncx_light(v, sf, lm);
    if (mode == 0) {
        /* MAC already holds the color */
    } else {
        s32 r = RGBC & 0xFF, g = (RGBC >> 8) & 0xFF, b = (RGBC >> 16) & 0xFF;
        s64 m1 = ((s64)r * (s16)IR1) << 4, m2 = ((s64)g * (s16)IR2) << 4, m3 = ((s64)b * (s16)IR3) << 4;
        if (mode == 1) {
            int shift = sf ? 12 : 0;
            set_mac(1, m1, shift); set_mac(2, m2, shift); set_mac(3, m3, shift);
            set_ir(1, MAC1, lm); set_ir(2, MAC2, lm); set_ir(3, MAC3, lm);
        } else {
            depth_cue(m1, m2, m3, sf, lm);
        }
    }
    push_color();
}

void gte_command(u32 cmd) {
    int sf = (cmd >> 19) & 1;
    int lm = (cmd >> 10) & 1;
    int shift = sf ? 12 : 0;
    FLAG = 0;
    switch (cmd & 0x3F) {
    case 0x01: rtp(0, sf, lm, 1); break; /* RTPS */
    case 0x30: rtp(0, sf, lm, 0); rtp(1, sf, lm, 0); rtp(2, sf, lm, 1); break; /* RTPT */
    case 0x06: { /* NCLIP */
        s32 x0 = (s16)SXY0, y0 = (s16)(SXY0 >> 16), x1 = (s16)SXY1, y1 = (s16)(SXY1 >> 16);
        s32 x2 = (s16)SXY2, y2 = (s16)(SXY2 >> 16);
        set_mac0((s64)x0 * y1 + (s64)x1 * y2 + (s64)x2 * y0 - (s64)x0 * y2 - (s64)x1 * y0 - (s64)x2 * y1);
        break;
    }
    case 0x0C: { /* OP */
        s32 d1 = M(0, 0, 0), d2 = M(0, 1, 1), d3 = M(0, 2, 2);
        set_mac(1, (s64)(s16)IR3 * d2 - (s64)(s16)IR2 * d3, shift);
        set_mac(2, (s64)(s16)IR1 * d3 - (s64)(s16)IR3 * d1, shift);
        set_mac(3, (s64)(s16)IR2 * d1 - (s64)(s16)IR1 * d2, shift);
        set_ir(1, MAC1, lm); set_ir(2, MAC2, lm); set_ir(3, MAC3, lm);
        break;
    }
    case 0x10: { /* DPCS */
        s32 r = RGBC & 0xFF, g = (RGBC >> 8) & 0xFF, b = (RGBC >> 16) & 0xFF;
        depth_cue((s64)r << 16, (s64)g << 16, (s64)b << 16, sf, lm);
        push_color();
        break;
    }
    case 0x2A: /* DPCT */
        for (int i = 0; i < 3; i++) {
            s32 c = RGB0;
            depth_cue((s64)(c & 0xFF) << 16, (s64)((c >> 8) & 0xFF) << 16, (s64)((c >> 16) & 0xFF) << 16, sf, lm);
            push_color();
        }
        break;
    case 0x11: /* INTPL */
        depth_cue((s64)(s16)IR1 << 12, (s64)(s16)IR2 << 12, (s64)(s16)IR3 << 12, sf, lm);
        push_color();
        break;
    case 0x12: /* MVMVA */
        mvmva(sf, lm, (cmd >> 17) & 3, (cmd >> 15) & 3, (cmd >> 13) & 3);
        break;
    case 0x13: nc(0, sf, lm, 2); break;                                 /* NCDS */
    case 0x16: nc(0, sf, lm, 2); nc(1, sf, lm, 2); nc(2, sf, lm, 2); break; /* NCDT */
    case 0x1B: nc(0, sf, lm, 1); break;                                 /* NCCS */
    case 0x3F: nc(0, sf, lm, 1); nc(1, sf, lm, 1); nc(2, sf, lm, 1); break; /* NCCT */
    case 0x1E: nc(0, sf, lm, 0); break;                                 /* NCS */
    case 0x20: nc(0, sf, lm, 0); nc(1, sf, lm, 0); nc(2, sf, lm, 0); break; /* NCT */
    case 0x14: { /* CDP */
        s64 r = (s64)RBK * 0x1000 + M(2, 0, 0) * (s16)IR1 + M(2, 0, 1) * (s16)IR2 + M(2, 0, 2) * (s16)IR3;
        s64 g = (s64)GBK * 0x1000 + M(2, 1, 0) * (s16)IR1 + M(2, 1, 1) * (s16)IR2 + M(2, 1, 2) * (s16)IR3;
        s64 b = (s64)BBK * 0x1000 + M(2, 2, 0) * (s16)IR1 + M(2, 2, 1) * (s16)IR2 + M(2, 2, 2) * (s16)IR3;
        set_mac(1, r, shift); set_mac(2, g, shift); set_mac(3, b, shift);
        set_ir(1, MAC1, lm); set_ir(2, MAC2, lm); set_ir(3, MAC3, lm);
        s32 cr = RGBC & 0xFF, cg = (RGBC >> 8) & 0xFF, cb = (RGBC >> 16) & 0xFF;
        depth_cue(((s64)cr * (s16)IR1) << 4, ((s64)cg * (s16)IR2) << 4, ((s64)cb * (s16)IR3) << 4, sf, lm);
        push_color();
        break;
    }
    case 0x1C: { /* CC */
        s64 r = (s64)RBK * 0x1000 + M(2, 0, 0) * (s16)IR1 + M(2, 0, 1) * (s16)IR2 + M(2, 0, 2) * (s16)IR3;
        s64 g = (s64)GBK * 0x1000 + M(2, 1, 0) * (s16)IR1 + M(2, 1, 1) * (s16)IR2 + M(2, 1, 2) * (s16)IR3;
        s64 b = (s64)BBK * 0x1000 + M(2, 2, 0) * (s16)IR1 + M(2, 2, 1) * (s16)IR2 + M(2, 2, 2) * (s16)IR3;
        set_mac(1, r, shift); set_mac(2, g, shift); set_mac(3, b, shift);
        set_ir(1, MAC1, lm); set_ir(2, MAC2, lm); set_ir(3, MAC3, lm);
        color_mul(sf, lm);
        push_color();
        break;
    }
    case 0x28: /* SQR */
        set_mac(1, (s64)(s16)IR1 * (s16)IR1, shift);
        set_mac(2, (s64)(s16)IR2 * (s16)IR2, shift);
        set_mac(3, (s64)(s16)IR3 * (s16)IR3, shift);
        set_ir(1, MAC1, lm); set_ir(2, MAC2, lm); set_ir(3, MAC3, lm);
        break;
    case 0x29: { /* DCPL */
        s32 r = RGBC & 0xFF, g = (RGBC >> 8) & 0xFF, b = (RGBC >> 16) & 0xFF;
        depth_cue(((s64)r * (s16)IR1) << 4, ((s64)g * (s16)IR2) << 4, ((s64)b * (s16)IR3) << 4, sf, lm);
        push_color();
        break;
    }
    case 0x2D: { /* AVSZ3 */
        s64 v = (s64)ZSF3 * (SZ1 + SZ2 + SZ3);
        set_mac0(v);
        s64 o = v >> 12;
        if (o < 0) { FLAG |= 1u << 18; o = 0; } else if (o > 0xFFFF) { FLAG |= 1u << 18; o = 0xFFFF; }
        OTZ = (s32)o;
        break;
    }
    case 0x2E: { /* AVSZ4 */
        s64 v = (s64)ZSF4 * (SZ0 + SZ1 + SZ2 + SZ3);
        set_mac0(v);
        s64 o = v >> 12;
        if (o < 0) { FLAG |= 1u << 18; o = 0; } else if (o > 0xFFFF) { FLAG |= 1u << 18; o = 0xFFFF; }
        OTZ = (s32)o;
        break;
    }
    case 0x3D: /* GPF */
        set_mac(1, (s64)(s16)IR0 * (s16)IR1, shift);
        set_mac(2, (s64)(s16)IR0 * (s16)IR2, shift);
        set_mac(3, (s64)(s16)IR0 * (s16)IR3, shift);
        set_ir(1, MAC1, lm); set_ir(2, MAC2, lm); set_ir(3, MAC3, lm);
        push_color();
        break;
    case 0x3E: /* GPL */
        set_mac(1, ((s64)MAC1 << shift) + (s64)(s16)IR0 * (s16)IR1, shift);
        set_mac(2, ((s64)MAC2 << shift) + (s64)(s16)IR0 * (s16)IR2, shift);
        set_mac(3, ((s64)MAC3 << shift) + (s64)(s16)IR0 * (s16)IR3, shift);
        set_ir(1, MAC1, lm); set_ir(2, MAC2, lm); set_ir(3, MAC3, lm);
        push_color();
        break;
    default:
        break;
    }
    if (FLAG & 0x7F87E000) FLAG |= 0x80000000u;
}

/* ---------------- register access ---------------- */

static s32 irgb_component(s32 ir) {
    s32 v = ir >> 7;
    return v < 0 ? 0 : (v > 0x1F ? 0x1F : v);
}

u32 gte_read_data(int r) {
    switch (r) {
    case 1: case 3: case 5: case 8: case 9: case 10: case 11:
        return (u32)(s32)(s16)D[r];
    case 7: case 16: case 17: case 18: case 19:
        return (u16)D[r];
    case 15:
        return (u32)SXY2;
    case 28: case 29:
        return (u32)(irgb_component(IR1) | (irgb_component(IR2) << 5) | (irgb_component(IR3) << 10));
    default:
        return (u32)D[r];
    }
}

void gte_write_data(int r, u32 v) {
    switch (r) {
    case 15:
        SXY0 = SXY1; SXY1 = SXY2; SXY2 = (s32)v;
        break;
    case 28:
        IR1 = (s32)((v & 0x1F) * 0x80);
        IR2 = (s32)(((v >> 5) & 0x1F) * 0x80);
        IR3 = (s32)(((v >> 10) & 0x1F) * 0x80);
        break;
    case 29: case 31:
        break;
    case 30: {
        LZCS = (s32)v;
        u32 x = (v & 0x80000000u) ? ~v : v;
        int n = 0;
        while (n < 32 && !(x & 0x80000000u)) { x <<= 1; n++; }
        LZCR = n;
        break;
    }
    default:
        D[r] = (s32)v;
        break;
    }
}

u32 gte_read_ctrl(int r) {
    switch (r) {
    case 4: case 12: case 20: case 26: case 27: case 29: case 30:
        return (u32)(s32)(s16)C[r];
    case 31:
        return FLAG;
    default:
        return (u32)C[r];
    }
}

void gte_write_ctrl(int r, u32 v) {
    if (r == 31) {
        FLAG = v & 0x7FFFF000;
        if (FLAG & 0x7F87E000) FLAG |= 0x80000000u;
        return;
    }
    C[r] = (s32)v;
}
