/* CD-ROM controller emulation backed by a raw MODE2/2352 disc image. */
#include "hw.h"
#include <stdlib.h>
#include <ctype.h>

static FILE* img;
static u32 img_sectors;

int cd_open_image(const char* path) {
    img = fopen(path, "rb");
    if (!img) return 0;
    fseek(img, 0, SEEK_END);
    img_sectors = (u32)(ftell(img) / 2352);
    return 1;
}

int cd_read_sector_raw(u32 lba, u8* out) {
    if (lba >= img_sectors) { memset(out, 0, 2352); return 0; }
    fseek(img, (long)lba * 2352, SEEK_SET);
    return fread(out, 1, 2352, img) == 2352;
}

/* ---------------- ISO9660 lookup (used by the loader) ---------------- */

static int read_user(u32 lba, u32 size, u8* out) {
    u8 sec[2352];
    for (u32 off = 0; off < size; off += 2048, lba++) {
        if (!cd_read_sector_raw(lba, sec)) return 0;
        u32 n = size - off < 2048 ? size - off : 2048;
        memcpy(out + off, sec + 24, n);
    }
    return 1;
}

static int dir_find(u32 lba, u32 size, const char* name, u32* out_lba, u32* out_size, int* is_dir) {
    u8* buf = malloc(size);
    if (!read_user(lba, size, buf)) { free(buf); return 0; }
    for (u32 pos = 0; pos < size;) {
        u8 len = buf[pos];
        if (len == 0) { pos = (pos / 2048 + 1) * 2048; continue; }
        u8 nl = buf[pos + 32];
        char nm[64];
        int k = 0;
        for (int i = 0; i < nl && i < 63; i++) {
            char c = (char)buf[pos + 33 + i];
            if (c == ';') break;
            nm[k++] = c;
        }
        nm[k] = 0;
        int match = 1;
        for (int i = 0;; i++) {
            if (toupper((u8)nm[i]) != toupper((u8)name[i])) { match = 0; break; }
            if (!nm[i]) break;
        }
        if (match) {
            memcpy(out_lba, buf + pos + 2, 4);
            memcpy(out_size, buf + pos + 10, 4);
            *is_dir = buf[pos + 25] & 2;
            free(buf);
            return 1;
        }
        pos += len;
    }
    free(buf);
    return 0;
}

int cd_find_file(const char* path, u32* lba, u32* size) {
    u8 pvd[2048];
    if (!read_user(16, 2048, pvd)) return 0;
    u32 dl, ds;
    memcpy(&dl, pvd + 156 + 2, 4);
    memcpy(&ds, pvd + 156 + 10, 4);
    char part[64];
    const char* p = path;
    while (*p == '\\' || *p == '/') p++;
    while (*p) {
        int k = 0;
        while (*p && *p != '\\' && *p != '/' && k < 63) part[k++] = *p++;
        part[k] = 0;
        while (*p == '\\' || *p == '/') p++;
        int is_dir;
        if (!dir_find(dl, ds, part, &dl, &ds, &is_dir)) return 0;
    }
    *lba = dl;
    *size = ds;
    return 1;
}

/* ---------------- controller ---------------- */

#define STAT_ERROR 0x01
#define STAT_MOTOR 0x02
#define STAT_SEEKERROR 0x04
#define STAT_READING 0x20
#define STAT_SEEKING 0x40
#define STAT_PLAYING 0x80

static u8 index_reg;
static u8 params[16];
static int nparams;
static u8 resp[16];
static int nresp, resp_pos;
static u8 int_enable, int_flag;
static u8 stat = STAT_MOTOR;
static u8 mode;
static u8 filter_file, filter_chan;
static u32 seek_lba, cur_lba;
static int reading;
static int muted;
static u8 last_header[8];

/* sector buffer (filled by the drive) and data FIFO (read by the CPU) */
static u8 sector_buf[2352];
static int sector_ready;
static u8 data_fifo[2352];
static int data_len, data_pos;

/* pending interrupts waiting for the CPU to acknowledge the current one */
typedef struct {
    u8 type;
    u8 n;
    u8 bytes[12];
} Resp;
static Resp queue[16];
static int qn;

static u32 bcd2(u8 v) { return (v >> 4) * 10 + (v & 15); }
static u8 tobcd(u32 v) { return (u8)(((v / 10) << 4) | (v % 10)); }

static void deliver(const Resp* r) {
    if (r->type != 1) LOG("cd: INT%d (%d bytes, first %02X) enable=%02X", r->type, r->n, r->bytes[0], int_enable);
    int_flag = r->type;
    memcpy(resp, r->bytes, r->n);
    nresp = r->n;
    resp_pos = 0;
    if (int_enable & int_flag & 0x1F) irq_raise(IRQ_CDROM);
}

static void push_resp(u8 type, const u8* bytes, int n) {
    Resp r;
    r.type = type;
    r.n = (u8)n;
    memcpy(r.bytes, bytes, n);
    if (int_flag == 0 && qn == 0) { deliver(&r); return; }
    if (type == 1) {
        /* a newer data-ready replaces an undelivered older one (the drive overwrites its buffer) */
        for (int i = 0; i < qn; i++)
            if (queue[i].type == 1) { queue[i] = r; return; }
    }
    if (qn < 16) queue[qn++] = r;
}

static void pump_queue(void) {
    if (int_flag == 0 && qn > 0) {
        Resp r = queue[0];
        memmove(queue, queue + 1, sizeof(Resp) * (--qn));
        deliver(&r);
    }
}

static void resp_stat(u8 type) {
    u8 s = stat;
    push_resp(type, &s, 1);
}

/* ---------------- reading ---------------- */

static s64 sector_period(void) { return (mode & 0x80) ? CPU_HZ / 150 : CPU_HZ / 75; }

static void read_event(void);

u32 g_stat_sectors;
static void deliver_sector(void) {
    g_stat_sectors++;
    u8 raw[2352];
    cd_read_sector_raw(cur_lba, raw);
    cur_lba++;
    memcpy(last_header, raw + 12, 8);
    u8 submode = raw[18];
    int is_form2 = raw[15] == 2 && (submode & 0x20);

    if ((mode & 0x40) && raw[15] == 2 && (submode & 0x04)) {
        /* XA-ADPCM audio sector: goes to the SPU, not to the CPU */
        if (!(mode & 0x08) || (raw[16] == filter_file && raw[17] == filter_chan)) {
            extern void xa_decode_sector(const u8* raw);
            if (!muted) xa_decode_sector(raw);
        }
        return;
    }
    if ((mode & 0x08) && raw[15] == 2 && (raw[16] != filter_file || raw[17] != filter_chan) && is_form2) {
        return;
    }
    memcpy(sector_buf, raw, 2352);
    sector_ready = 1;
    u8 s = stat;
    push_resp(1, &s, 1);
}

static void read_event(void) {
    if (!reading) return;
    sched_event(EV_CD2, sector_period(), read_event);
    deliver_sector();
}

static void start_reading(void) {
    cur_lba = seek_lba;
    reading = 1;
    stat = (stat & ~(STAT_SEEKING | STAT_PLAYING)) | STAT_READING | STAT_MOTOR;
    sched_event(EV_CD2, sector_period() + 20000, read_event);
}

static void stop_reading(void) {
    reading = 0;
    sched_cancel(EV_CD2);
    stat &= ~(STAT_READING | STAT_SEEKING | STAT_PLAYING);
}

/* ---------------- commands ---------------- */

static u8 cur_cmd;
static int second_pending;
static void second_response(void);

static void ack_event(void) {
    u8 cmd = cur_cmd;
    switch (cmd) {
    case 0x01: resp_stat(3); break;                         /* Getstat */
    case 0x02:                                              /* Setloc */
        seek_lba = (bcd2(params[0]) * 60 + bcd2(params[1])) * 75 + bcd2(params[2]);
        seek_lba = seek_lba >= 150 ? seek_lba - 150 : 0;
        LOG("cd: Setloc %u", seek_lba);
        resp_stat(3);
        break;
    case 0x03: resp_stat(3); stat |= STAT_PLAYING; break;   /* Play (no audio tracks) */
    case 0x06: case 0x1B:                                   /* ReadN / ReadS */
        resp_stat(3);
        start_reading();
        break;
    case 0x07: stat |= STAT_MOTOR; resp_stat(3); second_pending = 1; break; /* MotorOn */
    case 0x08: stop_reading(); resp_stat(3); stat &= ~STAT_MOTOR; second_pending = 1; break;
    case 0x09: resp_stat(3); stop_reading(); second_pending = 1; break;    /* Pause */
    case 0x0A:                                              /* Init */
        stop_reading();
        mode = 0;
        stat = STAT_MOTOR;
        resp_stat(3);
        second_pending = 1;
        break;
    case 0x0B: muted = 1; resp_stat(3); break;
    case 0x0C: muted = 0; resp_stat(3); break;
    case 0x0D: filter_file = params[0]; filter_chan = params[1]; resp_stat(3); break;
    case 0x0E: mode = params[0]; LOG("cd: Setmode %02X", mode); resp_stat(3); break;
    case 0x0F: { u8 r[5] = {stat, mode, 0, filter_file, filter_chan}; push_resp(3, r, 5); break; }
    case 0x10: push_resp(3, last_header, 8); break;         /* GetlocL */
    case 0x11: {                                            /* GetlocP */
        u32 l = cur_lba + 150, rel = cur_lba;
        u8 r[8] = {1, 1, tobcd(rel / 4500), tobcd((rel / 75) % 60), tobcd(rel % 75),
                   tobcd(l / 4500), tobcd((l / 75) % 60), tobcd(l % 75)};
        push_resp(3, r, 8);
        break;
    }
    case 0x13: { u8 r[3] = {stat, 1, 1}; push_resp(3, r, 3); break; }       /* GetTN */
    case 0x14: {                                                            /* GetTD */
        u32 l = params[0] == 0 ? img_sectors + 150 : 150;
        u8 r[3] = {stat, tobcd(l / 4500), tobcd((l / 75) % 60)};
        push_resp(3, r, 3);
        break;
    }
    case 0x15: case 0x16:                                   /* SeekL / SeekP */
        stop_reading();
        cur_lba = seek_lba;
        stat |= STAT_SEEKING;
        resp_stat(3);
        second_pending = 1;
        break;
    case 0x19:                                              /* Test */
        if (params[0] == 0x20) { u8 r[4] = {0x94, 0x09, 0x19, 0xC0}; push_resp(3, r, 4); }
        else resp_stat(3);
        break;
    case 0x1A: resp_stat(3); second_pending = 1; break;     /* GetID */
    case 0x1E: resp_stat(3); second_pending = 1; break;     /* ReadTOC */
    default:
        LOG("cd: unknown command %02X", cmd);
        { u8 r[2] = {stat | STAT_ERROR, 0x40}; push_resp(5, r, 2); }
        break;
    }
    nparams = 0;
    if (second_pending) sched_event(EV_CD, cmd == 0x0A ? 120000 : 60000, second_response);
}

static void second_response(void) {
    second_pending = 0;
    switch (cur_cmd) {
    case 0x15: case 0x16:
        stat &= ~STAT_SEEKING;
        resp_stat(2);
        break;
    case 0x1A: {
        u8 r[8] = {stat, 0x00, 0x20, 0x00, 'S', 'C', 'E', 'A'};
        push_resp(2, r, 8);
        break;
    }
    default:
        resp_stat(2);
        break;
    }
}

static void command(u8 cmd) {
    LOG("cd: command %02X params=%d", cmd, nparams);
    cur_cmd = cmd;
    second_pending = 0;
    sched_cancel(EV_CD);
    sched_event(EV_CD, cmd == 0x01 ? 3000 : 6000, ack_event);
}

void cd_init(void) {
    stat = STAT_MOTOR;
    int_enable = 0x1F; /* left enabled by the BIOS boot sequence */
    int_flag = 0;
}

u8 cd_read_reg(u32 addr) {
    switch (addr & 3) {
    case 0: {
        u8 s = index_reg & 3;
        if (nparams == 0) s |= 0x08;
        if (nparams < 16) s |= 0x10;
        if (resp_pos < nresp) s |= 0x20;
        if (data_pos < data_len) s |= 0x40;
        return s;
    }
    case 1: return resp_pos < nresp ? resp[resp_pos++] : 0;
    case 2: return data_pos < data_len ? data_fifo[data_pos++] : 0;
    default:
        if (index_reg & 1) return int_flag | 0xE0;
        return int_enable | 0xE0;
    }
}

static void load_data_fifo(void) {
    LOG("cd: data request, sector %u submode %02X", cur_lba - 1, sector_buf[18]);
    if (!sector_ready) { data_len = data_pos = 0; return; }
    if (mode & 0x20) {
        memcpy(data_fifo, sector_buf + 12, 2340);
        data_len = 2340;
    } else {
        memcpy(data_fifo, sector_buf + 24, 2048);
        data_len = 2048;
    }
    data_pos = 0;
}

void cd_write_reg(u32 addr, u8 v) {
    int r = addr & 3;
    if (r) LOG("cd: write reg%d.%d = %02X", r, index_reg, v);
    if (r == 0) { index_reg = v & 3; return; }
    switch (index_reg) {
    case 0:
        if (r == 1) command(v);
        else if (r == 2) { if (nparams < 16) params[nparams++] = v; }
        else if (r == 3) {
            if (v & 0x80) load_data_fifo();
            else data_len = data_pos = 0;
        }
        break;
    case 1:
        if (r == 2) int_enable = v & 0x1F;
        else if (r == 3) {
            int_flag &= ~(v & 0x1F);
            if (v & 0x40) nparams = 0;
            if (int_flag == 0) {
                nresp = resp_pos = 0;
                pump_queue();
            }
        }
        break;
    default:
        break; /* audio volume registers */
    }
}

u32 cd_dma_read_word(void) {
    u32 w = 0;
    for (int i = 0; i < 4; i++) {
        u8 b = data_pos < data_len ? data_fifo[data_pos++] : 0;
        w |= (u32)b << (i * 8);
    }
    return w;
}
