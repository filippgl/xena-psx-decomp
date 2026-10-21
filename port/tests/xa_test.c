/* Decode the XA audio of a raw 2352-byte-sector STR/XA file through the SPU code. */
#include "hw.h"
#include <stdlib.h>

void xa_decode_sector(const u8* raw);
int g_verbose;
s64 g_cycles, g_next_event;
void irq_raise(int l) { (void)l; }
void sched_event(int id, s64 d, EventFn f) { (void)id; (void)d; (void)f; }
void log_msg(const char* f, ...) { (void)f; }

int main(int argc, char** argv) {
    FILE* in = fopen(argv[1], "rb");
    FILE* out = fopen(argv[2], "wb");
    spu_init();
    spu_write(0x1F801DAA, 0x8001);
    spu_write(0x1F801DB0, 0x7FFF);
    spu_write(0x1F801DB2, 0x7FFF);
    static u8 sec[2352];
    static s16 buf[44100 * 2];
    int channel = argc > 3 ? atoi(argv[3]) : -1;
    while (fread(sec, 1, 2352, in) == 2352) {
        if (sec[15] == 2 && (sec[18] & 0x04) && (channel < 0 || sec[17] == channel)) {
            xa_decode_sector(sec);
            /* 2016 samples/channel at 37.8k -> about 2352 at 44.1k */
            int n = (sec[19] & 4) ? 4704 : 2352;
            if (!(sec[19] & 1)) n *= 2;
            spu_render(buf, n);
            fwrite(buf, 4, n, out);
        }
    }
    fclose(out);
    return 0;
}
