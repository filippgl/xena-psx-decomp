/* Native replacements for selected game/library functions (see recomp/hle.txt). */
#include "hw.h"

void hle_nop(void) { cpu.r[2] = 0; }
