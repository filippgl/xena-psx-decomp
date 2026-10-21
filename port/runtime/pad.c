/* SIO0: controller port serial interface, with a digital pad on port 1. */
#include "hw.h"

static u16 ctrl, mode, baud;
static u8 rx;
static int rx_full;
static int ack_level;   /* /ACK line asserted */
static int irq_flag;
static int seq;         /* byte index within the current transaction */
static int device;      /* 0 = none, 1 = pad */

static void ack_event(void) {
    ack_level = 1;
    if (ctrl & 0x1000) {
        irq_flag = 1;
        irq_raise(IRQ_PAD);
    }
}

/* DualShock (SCPH-1200) protocol: digital mode by default, config mode, analog mode */
static int analog_mode, config_mode, analog_locked;
static u8 cur_cmd;
static u8 resp_buf[8];
static int resp_len;
static u8 params_rx[8];
static u8 vib_map[6] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};

u8 g_pad_axes[4] = {0x80, 0x80, 0x80, 0x80}; /* RX, RY, LX, LY (set by platform) */

static u8 pad_id(void) {
    if (config_mode) return 0xF3;
    return analog_mode ? 0x73 : 0x41;
}

static void prepare_response(u8 cmd, int port) {
    u16 b = pad_buttons(port);
    memset(resp_buf, 0, sizeof(resp_buf));
    resp_len = (pad_id() & 0xF) * 2;
    switch (cmd) {
    case 0x42: case 0x43:
        if (config_mode && cmd == 0x43) break; /* zeros */
        resp_buf[0] = (u8)~b;
        resp_buf[1] = (u8)(~b >> 8);
        if (analog_mode) memcpy(resp_buf + 2, g_pad_axes, 4);
        break;
    case 0x45: { static const u8 r[6] = {0x01, 0x02, 0x00, 0x02, 0x01, 0x00}; memcpy(resp_buf, r, 6); resp_buf[2] = (u8)analog_mode; break; }
    case 0x46: case 0x4C: case 0x47: break; /* filled once the parameter byte arrives */
    case 0x4D: memcpy(resp_buf, vib_map, 6); break;
    default: break;
    }
}

static void param_byte(int idx, u8 v) {
    params_rx[idx] = v;
    if (idx == 0) {
        switch (cur_cmd) {
        case 0x46: {
            static const u8 a[6] = {0x00, 0x00, 0x01, 0x02, 0x00, 0x0A}, c[6] = {0x00, 0x00, 0x01, 0x01, 0x01, 0x14};
            memcpy(resp_buf, v == 0 ? a : c, 6);
            break;
        }
        case 0x47: { static const u8 a[6] = {0x00, 0x00, 0x02, 0x00, 0x01, 0x00}; memcpy(resp_buf, a, 6); break; }
        case 0x4C: { static const u8 a[6] = {0x00, 0x00, 0x00, 0x04, 0x00, 0x00}, c[6] = {0x00, 0x00, 0x00, 0x07, 0x00, 0x00};
            memcpy(resp_buf, v == 0 ? a : c, 6);
            break; }
        }
    }
    if (cur_cmd == 0x4D && idx < 6) vib_map[idx] = v;
}

void platform_rumble(int small, int large);

static void finish_command(void) {
    switch (cur_cmd) {
    case 0x42: { /* motor bytes travel in the poll, routed by the 0x4D mapping */
        int small = 0, large = 0;
        for (int i = 0; i < 6; i++) {
            if (vib_map[i] == 0x00) small = params_rx[i] & 1;
            else if (vib_map[i] == 0x01) large = params_rx[i];
        }
        platform_rumble(small, large);
        break;
    }
    case 0x43:
        config_mode = params_rx[0] == 1;
        break;
    case 0x44:
        if (config_mode) {
            analog_mode = params_rx[0] == 1;
            analog_locked = params_rx[1] == 3;
        }
        break;
    }
}

static u8 pad_exchange(u8 cmd, int* ack) {
    int port = (ctrl >> 13) & 1;
    *ack = 0;
    if (seq == 0) {
        device = (cmd == 0x01 && port == 0) ? 1 : 0;
        if (!device) return 0xFF;
        *ack = 1;
        seq++;
        return 0xFF;
    }
    if (!device) return 0xFF;
    u8 r = 0xFF;
    if (seq == 1) {
        cur_cmd = cmd;
        r = pad_id();
        memset(params_rx, 0, sizeof(params_rx));
        prepare_response(cmd, port);
        *ack = 1;
    } else if (seq == 2) {
        r = 0x5A;
        *ack = 1;
    } else {
        int idx = seq - 3;
        param_byte(idx, cmd);
        r = idx < resp_len ? resp_buf[idx] : 0xFF;
        if (idx < resp_len - 1) *ack = 1;
        else { finish_command(); device = 0; }
    }
    seq++;
    return r;
}

void sio_write(u32 addr, u32 v, int size) {
    switch (addr & 0xF) {
    case 0x0: { /* JOY_DATA */
        if (!(ctrl & 0x2)) { rx = 0xFF; rx_full = 1; return; } /* not selected */
        int ack;
        int sq = seq;
        rx = pad_exchange((u8)v, &ack);
        LOG("sio: port%d seq%d tx=%02X rx=%02X ack=%d", (ctrl >> 13) & 1, sq, v & 0xFF, rx, ack);
        rx_full = 1;
        ack_level = 0;
        if (ack) sched_event(EV_SIO, 600, ack_event);
        break;
    }
    case 0x8: /* JOY_MODE (+ JOY_CTRL in the upper half for 32-bit writes) */
        mode = (u16)v;
        if (size == 4) goto write_ctrl;
        break;
    case 0xA:
    write_ctrl:
        if (size == 4) v >>= 16;
        if (v & 0x40) { /* reset */
            ctrl = 0; mode = 0; rx_full = 0; irq_flag = 0; seq = 0; ack_level = 0;
            sched_cancel(EV_SIO);
            break;
        }
        if (v & 0x10) irq_flag = 0;                  /* acknowledge */
        if (!(v & 0x2)) { seq = 0; device = 0; }     /* deselect: end of transaction */
        if ((v & 0x2) && !(ctrl & 0x2)) seq = 0;     /* new selection */
        ctrl = (u16)(v & ~0x50u);
        break;
    case 0xE:
        baud = (u16)v;
        break;
    }
}

u32 sio_read(u32 addr, int size) {
    switch (addr & 0xF) {
    case 0x0: {
        u8 r = rx_full ? rx : 0xFF;
        rx_full = 0;
        return size == 1 ? r : (u32)r | 0xFFFFFF00u;
    }
    case 0x4: { /* JOY_STAT */
        u32 s = 0x5;                      /* TX ready, TX finished */
        if (rx_full) s |= 0x2;
        if (ack_level) s |= 0x80;
        if (irq_flag) s |= 0x200;
        return s;
    }
    case 0x8: return size == 4 ? (u32)mode | ((u32)ctrl << 16) : mode;
    case 0xA: return ctrl;
    case 0xE: return baud;
    }
    return 0;
}
