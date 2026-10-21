#include "common.h"

typedef struct {
    /* 0x00 */ u8 pad0;
    /* 0x01 */ u8 unk1;
    /* 0x02 */ u8 unk2;
    /* 0x03 */ u8 pad3[8];
    /* 0x0B */ u8 unkB;
    /* 0x0C */ u8 unkC;
    /* 0x0D */ u8 unkD;
    /* 0x0E */ u8 unkE;
    /* 0x0F */ u8 unkF;
    /* 0x10 */ u8 unk10;
} Unk800227B8Flags;

typedef struct {
    /* 0x00 */ u8 pad0[0xC];
    /* 0x0C */ Unk800227B8Flags* flags;
} Unk800227B8;

void* memset(void* dst, int c, int n);
void func_800B3250(void* arg0);

void* D_800F0B44;
extern u8 D_800F1898[0x240];

INCLUDE_ASM("asm/nonmatchings/game", func_80019224);

INCLUDE_ASM("asm/nonmatchings/game", func_80019820);

INCLUDE_ASM("asm/nonmatchings/game", func_80019950);

INCLUDE_ASM("asm/nonmatchings/game", func_80019FE0);

INCLUDE_ASM("asm/nonmatchings/game", func_8001A4DC);

INCLUDE_ASM("asm/nonmatchings/game", func_8001A634);

INCLUDE_ASM("asm/nonmatchings/game", func_8001ABCC);

INCLUDE_ASM("asm/nonmatchings/game", func_8001AC0C);

INCLUDE_ASM("asm/nonmatchings/game", func_8001AD10);

INCLUDE_ASM("asm/nonmatchings/game", func_8001B12C);

INCLUDE_ASM("asm/nonmatchings/game", func_8001B374);

INCLUDE_ASM("asm/nonmatchings/game", func_8001B460);

INCLUDE_ASM("asm/nonmatchings/game", func_8002259C);

void func_800227B8(Unk800227B8* arg0) {
    Unk800227B8Flags* f = arg0->flags;

    f->unk10 = 1;
    f->unkF = 1;
    f->unkE = 1;
    f->unkD = 1;
    f->unkC = 1;
    f->unkB = 1;
    f->unk2 = 1;
    f->unk1 = 1;
}

INCLUDE_ASM("asm/nonmatchings/game", func_800227E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8002281C);

INCLUDE_ASM("asm/nonmatchings/game", func_80022ED0);

INCLUDE_ASM("asm/nonmatchings/game", func_80023018);

INCLUDE_ASM("asm/nonmatchings/game", func_80023420);

INCLUDE_ASM("asm/nonmatchings/game", func_80023740);

INCLUDE_ASM("asm/nonmatchings/game", func_80023794);

INCLUDE_ASM("asm/nonmatchings/game", func_8002387C);

INCLUDE_ASM("asm/nonmatchings/game", func_80023A34);

INCLUDE_ASM("asm/nonmatchings/game", func_80023C84);

INCLUDE_ASM("asm/nonmatchings/game", func_80023D40);

INCLUDE_ASM("asm/nonmatchings/game", func_80023D9C);

INCLUDE_ASM("asm/nonmatchings/game", func_80023E88);

INCLUDE_ASM("asm/nonmatchings/game", func_80023EBC);

INCLUDE_ASM("asm/nonmatchings/game", func_800241B4);

INCLUDE_ASM("asm/nonmatchings/game", func_8002455C);

INCLUDE_ASM("asm/nonmatchings/game", func_800246D4);

INCLUDE_ASM("asm/nonmatchings/game", func_80025220);

INCLUDE_ASM("asm/nonmatchings/game", func_800255A8);

INCLUDE_ASM("asm/nonmatchings/game", func_800257F4);

INCLUDE_ASM("asm/nonmatchings/game", func_80025DE4);

INCLUDE_ASM("asm/nonmatchings/game", func_80025E44);

INCLUDE_ASM("asm/nonmatchings/game", func_80025EB4);

INCLUDE_ASM("asm/nonmatchings/game", func_80025F18);

INCLUDE_ASM("asm/nonmatchings/game", func_80026640);

INCLUDE_ASM("asm/nonmatchings/game", func_80026690);

INCLUDE_ASM("asm/nonmatchings/game", func_80026A24);

INCLUDE_ASM("asm/nonmatchings/game", func_80026BFC);

INCLUDE_ASM("asm/nonmatchings/game", func_80026CDC);

INCLUDE_ASM("asm/nonmatchings/game", func_80026E78);

INCLUDE_ASM("asm/nonmatchings/game", func_800271DC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002745C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002765C);

INCLUDE_ASM("asm/nonmatchings/game", func_800276B4);

INCLUDE_ASM("asm/nonmatchings/game", func_8002770C);

INCLUDE_ASM("asm/nonmatchings/game", func_80027790);

INCLUDE_ASM("asm/nonmatchings/game", func_800277E8);

void func_800279E4(void) {
    if (D_800F0B44 != NULL) {
        func_800B3250(D_800F0B44);
    }
    D_800F0B44 = NULL;
}

INCLUDE_ASM("asm/nonmatchings/game", func_80027A10);

INCLUDE_ASM("asm/nonmatchings/game", func_80027C60);

INCLUDE_ASM("asm/nonmatchings/game", func_80027DFC);

INCLUDE_ASM("asm/nonmatchings/game", func_80027FB0);

INCLUDE_ASM("asm/nonmatchings/game", func_8002845C);

INCLUDE_ASM("asm/nonmatchings/game", func_80028650);

INCLUDE_ASM("asm/nonmatchings/game", func_80028E84);

INCLUDE_ASM("asm/nonmatchings/game", func_80028EDC);

INCLUDE_ASM("asm/nonmatchings/game", func_80029030);

INCLUDE_ASM("asm/nonmatchings/game", func_800291A0);

void func_80029358(void) {
    memset(D_800F1898, 0, sizeof(D_800F1898));
}

INCLUDE_ASM("asm/nonmatchings/game", func_80029384);

INCLUDE_ASM("asm/nonmatchings/game", func_80029438);

INCLUDE_ASM("asm/nonmatchings/game", func_80029518);

INCLUDE_ASM("asm/nonmatchings/game", func_800295E8);

INCLUDE_ASM("asm/nonmatchings/game", func_80029DF0);

INCLUDE_ASM("asm/nonmatchings/game", func_80029E54);

INCLUDE_ASM("asm/nonmatchings/game", func_80029E70);

INCLUDE_ASM("asm/nonmatchings/game", func_8002A43C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002A534);

INCLUDE_ASM("asm/nonmatchings/game", func_8002A608);

INCLUDE_ASM("asm/nonmatchings/game", func_8002AD40);

INCLUDE_ASM("asm/nonmatchings/game", func_8002AFFC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B010);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B1FC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B330);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B364);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B3A8);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B3F8);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B4E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B5D8);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B634);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B6DC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002B920);

INCLUDE_ASM("asm/nonmatchings/game", func_8002BA78);

INCLUDE_ASM("asm/nonmatchings/game", func_8002BE44);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C01C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C0BC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C15C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C208);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C318);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C37C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C474);

INCLUDE_ASM("asm/nonmatchings/game", func_8002C8BC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002CA70);

INCLUDE_ASM("asm/nonmatchings/game", func_8002CCAC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002CDAC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002CF0C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002D28C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002D2A0);

INCLUDE_ASM("asm/nonmatchings/game", func_8002D508);

INCLUDE_ASM("asm/nonmatchings/game", func_8002D54C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002D57C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002E69C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002EA1C);

INCLUDE_ASM("asm/nonmatchings/game", func_8002F8A8);

INCLUDE_ASM("asm/nonmatchings/game", func_8002F9C0);

INCLUDE_ASM("asm/nonmatchings/game", func_8002FEFC);

INCLUDE_ASM("asm/nonmatchings/game", func_8002FFA8);

INCLUDE_ASM("asm/nonmatchings/game", func_80030324);

INCLUDE_ASM("asm/nonmatchings/game", func_80030354);

INCLUDE_ASM("asm/nonmatchings/game", func_800305E0);

INCLUDE_ASM("asm/nonmatchings/game", func_800326DC);

INCLUDE_ASM("asm/nonmatchings/game", func_80032748);

INCLUDE_ASM("asm/nonmatchings/game", func_80032868);

INCLUDE_ASM("asm/nonmatchings/game", func_80032904);

INCLUDE_ASM("asm/nonmatchings/game", func_80032B6C);

INCLUDE_ASM("asm/nonmatchings/game", func_80032C18);

INCLUDE_ASM("asm/nonmatchings/game", func_80033814);

INCLUDE_ASM("asm/nonmatchings/game", func_80033DEC);

INCLUDE_ASM("asm/nonmatchings/game", func_80033EBC);

INCLUDE_ASM("asm/nonmatchings/game", func_80033F7C);

INCLUDE_ASM("asm/nonmatchings/game", func_80033F88);

INCLUDE_ASM("asm/nonmatchings/game", func_800340B8);

INCLUDE_ASM("asm/nonmatchings/game", func_8003412C);

INCLUDE_ASM("asm/nonmatchings/game", func_8003423C);

INCLUDE_ASM("asm/nonmatchings/game", func_800342C4);

INCLUDE_ASM("asm/nonmatchings/game", func_80034AD0);

INCLUDE_ASM("asm/nonmatchings/game", func_80034BCC);

INCLUDE_ASM("asm/nonmatchings/game", func_80034FD0);

INCLUDE_ASM("asm/nonmatchings/game", func_8003500C);

INCLUDE_ASM("asm/nonmatchings/game", func_80035070);

INCLUDE_ASM("asm/nonmatchings/game", func_800353CC);

INCLUDE_ASM("asm/nonmatchings/game", func_80035524);

INCLUDE_ASM("asm/nonmatchings/game", func_800355A4);

INCLUDE_ASM("asm/nonmatchings/game", func_80035B54);

INCLUDE_ASM("asm/nonmatchings/game", func_80036040);

INCLUDE_ASM("asm/nonmatchings/game", func_8003606C);

INCLUDE_ASM("asm/nonmatchings/game", func_80036118);

INCLUDE_ASM("asm/nonmatchings/game", func_800366EC);

INCLUDE_ASM("asm/nonmatchings/game", func_80036770);

INCLUDE_ASM("asm/nonmatchings/game", func_800367BC);

INCLUDE_ASM("asm/nonmatchings/game", func_8003D160);

INCLUDE_ASM("asm/nonmatchings/game", func_8003D2BC);

INCLUDE_ASM("asm/nonmatchings/game", func_8003D490);

INCLUDE_ASM("asm/nonmatchings/game", func_8003DCF8);

INCLUDE_ASM("asm/nonmatchings/game", func_8003DD78);

INCLUDE_ASM("asm/nonmatchings/game", func_8003DDEC);

INCLUDE_ASM("asm/nonmatchings/game", func_8003F3F0);

INCLUDE_ASM("asm/nonmatchings/game", func_8003F52C);

INCLUDE_ASM("asm/nonmatchings/game", func_8003F6AC);

INCLUDE_ASM("asm/nonmatchings/game", func_8003F9C8);

INCLUDE_ASM("asm/nonmatchings/game", func_8003FBA0);

INCLUDE_ASM("asm/nonmatchings/game", func_800406C8);

INCLUDE_ASM("asm/nonmatchings/game", func_8004079C);

INCLUDE_ASM("asm/nonmatchings/game", func_80040844);

INCLUDE_ASM("asm/nonmatchings/game", func_80040E34);

INCLUDE_ASM("asm/nonmatchings/game", func_800410C0);

INCLUDE_ASM("asm/nonmatchings/game", func_80041228);

INCLUDE_ASM("asm/nonmatchings/game", func_80041538);

INCLUDE_ASM("asm/nonmatchings/game", func_80041568);

INCLUDE_ASM("asm/nonmatchings/game", func_80041640);

INCLUDE_ASM("asm/nonmatchings/game", func_800416E8);

INCLUDE_ASM("asm/nonmatchings/game", func_8004180C);

INCLUDE_ASM("asm/nonmatchings/game", func_80042210);

INCLUDE_ASM("asm/nonmatchings/game", func_8004240C);

INCLUDE_ASM("asm/nonmatchings/game", func_800427C4);

INCLUDE_ASM("asm/nonmatchings/game", func_800429D4);

INCLUDE_ASM("asm/nonmatchings/game", func_80042C30);

INCLUDE_ASM("asm/nonmatchings/game", func_800432D0);

INCLUDE_ASM("asm/nonmatchings/game", func_8004339C);

INCLUDE_ASM("asm/nonmatchings/game", func_8004352C);

INCLUDE_ASM("asm/nonmatchings/game", func_800435AC);

INCLUDE_ASM("asm/nonmatchings/game", func_80043B5C);

INCLUDE_ASM("asm/nonmatchings/game", func_800441B0);

INCLUDE_ASM("asm/nonmatchings/game", func_80044550);

INCLUDE_ASM("asm/nonmatchings/game", func_80044D8C);

INCLUDE_ASM("asm/nonmatchings/game", func_80044DCC);

INCLUDE_ASM("asm/nonmatchings/game", func_800454F4);

INCLUDE_ASM("asm/nonmatchings/game", func_80045534);

INCLUDE_RODATA("asm/nonmatchings/game", D_800177A4);

INCLUDE_ASM("asm/nonmatchings/game", func_80045C1C);

INCLUDE_ASM("asm/nonmatchings/game", func_800460AC);

INCLUDE_ASM("asm/nonmatchings/game", func_80046284);

INCLUDE_ASM("asm/nonmatchings/game", func_800462E4);

INCLUDE_ASM("asm/nonmatchings/game", func_80046598);

INCLUDE_ASM("asm/nonmatchings/game", func_8004692C);

INCLUDE_ASM("asm/nonmatchings/game", func_80046F38);

INCLUDE_ASM("asm/nonmatchings/game", func_80047050);

INCLUDE_ASM("asm/nonmatchings/game", func_800478E0);

INCLUDE_ASM("asm/nonmatchings/game", func_800479D8);

INCLUDE_ASM("asm/nonmatchings/game", func_80047AA0);

INCLUDE_ASM("asm/nonmatchings/game", func_80047C00);

INCLUDE_ASM("asm/nonmatchings/game", func_80047CCC);

INCLUDE_ASM("asm/nonmatchings/game", func_80048D64);

INCLUDE_ASM("asm/nonmatchings/game", func_80048DF4);

INCLUDE_ASM("asm/nonmatchings/game", func_80049440);

INCLUDE_ASM("asm/nonmatchings/game", func_800494B0);

INCLUDE_ASM("asm/nonmatchings/game", func_8004A2A8);

INCLUDE_ASM("asm/nonmatchings/game", func_8004A690);

INCLUDE_ASM("asm/nonmatchings/game", func_8004A70C);

INCLUDE_ASM("asm/nonmatchings/game", func_8004A74C);

INCLUDE_ASM("asm/nonmatchings/game", func_8004A91C);

INCLUDE_ASM("asm/nonmatchings/game", func_8004A9AC);

INCLUDE_ASM("asm/nonmatchings/game", func_8004A9F8);

INCLUDE_ASM("asm/nonmatchings/game", func_8004AB5C);

INCLUDE_ASM("asm/nonmatchings/game", func_8004C7A8);

INCLUDE_ASM("asm/nonmatchings/game", func_8004C814);

INCLUDE_ASM("asm/nonmatchings/game", func_8004CC84);

INCLUDE_ASM("asm/nonmatchings/game", func_8004CDF0);

INCLUDE_ASM("asm/nonmatchings/game", func_8004D1F8);

void func_8004D2E0(void) {
}

INCLUDE_ASM("asm/nonmatchings/game", func_8004D2E8);

INCLUDE_ASM("asm/nonmatchings/game", func_8004D528);

INCLUDE_ASM("asm/nonmatchings/game", func_8004D558);

INCLUDE_ASM("asm/nonmatchings/game", func_8004D5E0);

INCLUDE_ASM("asm/nonmatchings/game", func_8004D7A4);

INCLUDE_ASM("asm/nonmatchings/game", func_8004D808);

INCLUDE_ASM("asm/nonmatchings/game", func_8004E188);

INCLUDE_ASM("asm/nonmatchings/game", func_8004E1B0);

INCLUDE_ASM("asm/nonmatchings/game", func_8004E1D8);

INCLUDE_ASM("asm/nonmatchings/game", func_8004E4A4);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F02C);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F088);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F294);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F3D4);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F61C);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F6E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F778);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F858);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F920);

INCLUDE_ASM("asm/nonmatchings/game", func_8004F9BC);

INCLUDE_ASM("asm/nonmatchings/game", func_8004FCBC);

INCLUDE_ASM("asm/nonmatchings/game", func_800500A4);

INCLUDE_ASM("asm/nonmatchings/game", func_800501A8);

INCLUDE_ASM("asm/nonmatchings/game", func_8005022C);

INCLUDE_ASM("asm/nonmatchings/game", func_800502B8);

INCLUDE_ASM("asm/nonmatchings/game", func_80050478);

INCLUDE_ASM("asm/nonmatchings/game", func_80050994);

INCLUDE_ASM("asm/nonmatchings/game", func_80050E20);

INCLUDE_ASM("asm/nonmatchings/game", func_8005116C);

INCLUDE_ASM("asm/nonmatchings/game", func_80051528);

INCLUDE_ASM("asm/nonmatchings/game", func_80051554);

INCLUDE_ASM("asm/nonmatchings/game", func_800516A4);

INCLUDE_ASM("asm/nonmatchings/game", func_80051894);

INCLUDE_ASM("asm/nonmatchings/game", func_80052A38);

INCLUDE_ASM("asm/nonmatchings/game", func_80052D10);

INCLUDE_ASM("asm/nonmatchings/game", func_80053380);

INCLUDE_ASM("asm/nonmatchings/game", func_8005361C);

INCLUDE_ASM("asm/nonmatchings/game", func_80053808);

INCLUDE_ASM("asm/nonmatchings/game", func_80053AFC);

INCLUDE_ASM("asm/nonmatchings/game", func_80053E60);

INCLUDE_ASM("asm/nonmatchings/game", func_80053F58);

INCLUDE_ASM("asm/nonmatchings/game", func_80053FF4);

INCLUDE_ASM("asm/nonmatchings/game", func_800542CC);

INCLUDE_ASM("asm/nonmatchings/game", func_80054644);

INCLUDE_ASM("asm/nonmatchings/game", func_80055108);

INCLUDE_ASM("asm/nonmatchings/game", func_80055600);

INCLUDE_ASM("asm/nonmatchings/game", func_80057FC8);

INCLUDE_ASM("asm/nonmatchings/game", func_800588CC);

INCLUDE_ASM("asm/nonmatchings/game", func_80058988);

INCLUDE_ASM("asm/nonmatchings/game", func_8005A364);

INCLUDE_ASM("asm/nonmatchings/game", func_8005A54C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005ABE0);

INCLUDE_ASM("asm/nonmatchings/game", func_8005AC20);

INCLUDE_ASM("asm/nonmatchings/game", func_8005AC94);

INCLUDE_ASM("asm/nonmatchings/game", func_8005ADE8);

INCLUDE_ASM("asm/nonmatchings/game", func_8005AE80);

INCLUDE_ASM("asm/nonmatchings/game", func_8005AEB0);

INCLUDE_ASM("asm/nonmatchings/game", func_8005B128);

INCLUDE_ASM("asm/nonmatchings/game", func_8005B1D0);

INCLUDE_ASM("asm/nonmatchings/game", func_8005B22C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005B6F8);

INCLUDE_ASM("asm/nonmatchings/game", func_8005BF10);

INCLUDE_ASM("asm/nonmatchings/game", func_8005BF50);

INCLUDE_ASM("asm/nonmatchings/game", func_8005C498);

INCLUDE_ASM("asm/nonmatchings/game", func_8005C66C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005CF20);

INCLUDE_ASM("asm/nonmatchings/game", func_8005D1B4);

INCLUDE_ASM("asm/nonmatchings/game", func_8005D32C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005D628);

INCLUDE_ASM("asm/nonmatchings/game", func_8005D638);

INCLUDE_ASM("asm/nonmatchings/game", func_8005DA34);

INCLUDE_ASM("asm/nonmatchings/game", func_8005DD40);

INCLUDE_ASM("asm/nonmatchings/game", func_8005E1B8);

INCLUDE_ASM("asm/nonmatchings/game", func_8005E1F4);

INCLUDE_ASM("asm/nonmatchings/game", func_8005E430);

INCLUDE_ASM("asm/nonmatchings/game", func_8005E48C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005E540);

INCLUDE_ASM("asm/nonmatchings/game", func_8005EA18);

INCLUDE_ASM("asm/nonmatchings/game", func_8005EBD4);

INCLUDE_ASM("asm/nonmatchings/game", func_8005EF4C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005F1B0);

INCLUDE_ASM("asm/nonmatchings/game", func_8005F94C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005FB1C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005FC1C);

INCLUDE_ASM("asm/nonmatchings/game", func_8005FE50);

INCLUDE_ASM("asm/nonmatchings/game", func_8005FEB8);

INCLUDE_ASM("asm/nonmatchings/game", func_8005FFA4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006135C);

INCLUDE_ASM("asm/nonmatchings/game", func_800613A4);

INCLUDE_ASM("asm/nonmatchings/game", func_80061488);

INCLUDE_ASM("asm/nonmatchings/game", func_800615A8);

INCLUDE_ASM("asm/nonmatchings/game", func_800617AC);

INCLUDE_ASM("asm/nonmatchings/game", func_80061E50);

INCLUDE_ASM("asm/nonmatchings/game", func_8006214C);

INCLUDE_ASM("asm/nonmatchings/game", func_80062158);

INCLUDE_ASM("asm/nonmatchings/game", func_800622E8);

INCLUDE_ASM("asm/nonmatchings/game", func_800626E0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006276C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006287C);

INCLUDE_ASM("asm/nonmatchings/game", func_800645C4);

INCLUDE_ASM("asm/nonmatchings/game", func_80064680);

INCLUDE_ASM("asm/nonmatchings/game", func_80064750);

INCLUDE_ASM("asm/nonmatchings/game", func_8006486C);

INCLUDE_ASM("asm/nonmatchings/game", func_80064960);

INCLUDE_ASM("asm/nonmatchings/game", func_80064D0C);

INCLUDE_ASM("asm/nonmatchings/game", func_80065120);

INCLUDE_ASM("asm/nonmatchings/game", func_80065704);

INCLUDE_ASM("asm/nonmatchings/game", func_800659EC);

INCLUDE_ASM("asm/nonmatchings/game", main);

INCLUDE_ASM("asm/nonmatchings/game", func_80066BB0);

INCLUDE_ASM("asm/nonmatchings/game", func_80066BD4);

INCLUDE_ASM("asm/nonmatchings/game", func_80066CDC);

INCLUDE_ASM("asm/nonmatchings/game", func_800672F8);

INCLUDE_ASM("asm/nonmatchings/game", func_80067360);

INCLUDE_ASM("asm/nonmatchings/game", func_800673E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006747C);

INCLUDE_ASM("asm/nonmatchings/game", func_80067510);

void func_800675E8(void) {
}

INCLUDE_ASM("asm/nonmatchings/game", func_800675F0);

INCLUDE_ASM("asm/nonmatchings/game", func_80067640);

INCLUDE_ASM("asm/nonmatchings/game", func_800676A0);

INCLUDE_ASM("asm/nonmatchings/game", func_80067950);

INCLUDE_ASM("asm/nonmatchings/game", func_80067AAC);

INCLUDE_ASM("asm/nonmatchings/game", func_80067F30);

INCLUDE_ASM("asm/nonmatchings/game", func_80067FB8);

INCLUDE_ASM("asm/nonmatchings/game", func_80068044);

INCLUDE_ASM("asm/nonmatchings/game", func_80068450);

INCLUDE_ASM("asm/nonmatchings/game", func_8006854C);

INCLUDE_ASM("asm/nonmatchings/game", func_80068584);

INCLUDE_ASM("asm/nonmatchings/game", func_800685B4);

INCLUDE_ASM("asm/nonmatchings/game", func_800685FC);

INCLUDE_ASM("asm/nonmatchings/game", func_80068680);

INCLUDE_ASM("asm/nonmatchings/game", func_800686DC);

INCLUDE_ASM("asm/nonmatchings/game", func_80068748);

INCLUDE_ASM("asm/nonmatchings/game", func_80068794);

INCLUDE_ASM("asm/nonmatchings/game", func_800688E8);

INCLUDE_ASM("asm/nonmatchings/game", func_80068980);

INCLUDE_ASM("asm/nonmatchings/game", func_80068A30);

INCLUDE_ASM("asm/nonmatchings/game", func_80068ADC);

INCLUDE_ASM("asm/nonmatchings/game", func_80068BA4);

INCLUDE_ASM("asm/nonmatchings/game", func_80068BB0);

INCLUDE_ASM("asm/nonmatchings/game", func_80068BBC);

INCLUDE_ASM("asm/nonmatchings/game", func_80068DD8);

INCLUDE_ASM("asm/nonmatchings/game", func_80068DE4);

INCLUDE_ASM("asm/nonmatchings/game", func_80068E90);

INCLUDE_ASM("asm/nonmatchings/game", func_80068EDC);

INCLUDE_ASM("asm/nonmatchings/game", func_80068F0C);

INCLUDE_ASM("asm/nonmatchings/game", func_80068F3C);

INCLUDE_ASM("asm/nonmatchings/game", func_80068F6C);

INCLUDE_ASM("asm/nonmatchings/game", func_80068FE4);

INCLUDE_ASM("asm/nonmatchings/game", func_80069040);

INCLUDE_ASM("asm/nonmatchings/game", func_80069B80);

INCLUDE_ASM("asm/nonmatchings/game", func_80069C38);

INCLUDE_ASM("asm/nonmatchings/game", func_80069C5C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A498);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A528);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A684);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A6E8);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A8B8);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A8E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A924);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A988);

INCLUDE_ASM("asm/nonmatchings/game", func_8006A9B0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006AA04);

INCLUDE_ASM("asm/nonmatchings/game", func_8006AC00);

INCLUDE_ASM("asm/nonmatchings/game", func_8006AC88);

INCLUDE_ASM("asm/nonmatchings/game", func_8006B214);

INCLUDE_ASM("asm/nonmatchings/game", func_8006B678);

INCLUDE_ASM("asm/nonmatchings/game", func_8006B980);

INCLUDE_ASM("asm/nonmatchings/game", func_8006BB70);

INCLUDE_ASM("asm/nonmatchings/game", func_8006BFE0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C04C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C16C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C18C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C1B4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C20C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C264);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C2A4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C2D0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C2E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C3D8);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C424);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C464);

INCLUDE_ASM("asm/nonmatchings/game", func_8006C4D0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006CCF4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D208);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D258);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D3EC);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D5E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D604);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D734);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D7A8);

INCLUDE_ASM("asm/nonmatchings/game", func_8006D7F0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006DB5C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006DCA8);

INCLUDE_ASM("asm/nonmatchings/game", func_8006DE28);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E1EC);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E2BC);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E314);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E3FC);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E41C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E468);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E4CC);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E508);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E66C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006E78C);

INCLUDE_ASM("asm/nonmatchings/game", func_8006FCEC);

INCLUDE_ASM("asm/nonmatchings/game", func_8006FCF8);

INCLUDE_ASM("asm/nonmatchings/game", func_8006FDD0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006FE00);

INCLUDE_ASM("asm/nonmatchings/game", func_8006FE50);

INCLUDE_ASM("asm/nonmatchings/game", func_8006FEC0);

INCLUDE_ASM("asm/nonmatchings/game", func_8006FF38);

INCLUDE_ASM("asm/nonmatchings/game", func_80070654);

INCLUDE_ASM("asm/nonmatchings/game", func_800706D0);

INCLUDE_ASM("asm/nonmatchings/game", func_80070724);

INCLUDE_ASM("asm/nonmatchings/game", func_80070B04);

INCLUDE_ASM("asm/nonmatchings/game", func_80070B6C);

INCLUDE_ASM("asm/nonmatchings/game", func_80070BD4);

INCLUDE_ASM("asm/nonmatchings/game", func_80070D84);

INCLUDE_ASM("asm/nonmatchings/game", func_80070DFC);

INCLUDE_ASM("asm/nonmatchings/game", func_8007121C);

INCLUDE_ASM("asm/nonmatchings/game", func_800717F8);

INCLUDE_ASM("asm/nonmatchings/game", func_80071850);

INCLUDE_ASM("asm/nonmatchings/game", func_80071AB0);

INCLUDE_ASM("asm/nonmatchings/game", func_80071B24);

INCLUDE_ASM("asm/nonmatchings/game", func_80071C60);

INCLUDE_ASM("asm/nonmatchings/game", func_80071F9C);

INCLUDE_ASM("asm/nonmatchings/game", func_800720BC);

INCLUDE_ASM("asm/nonmatchings/game", func_80072DFC);

INCLUDE_ASM("asm/nonmatchings/game", func_80072E60);

INCLUDE_ASM("asm/nonmatchings/game", func_80072EA0);

INCLUDE_ASM("asm/nonmatchings/game", func_80072EE0);

INCLUDE_ASM("asm/nonmatchings/game", func_80073014);

INCLUDE_ASM("asm/nonmatchings/game", func_80073020);

INCLUDE_ASM("asm/nonmatchings/game", func_80073054);

INCLUDE_ASM("asm/nonmatchings/game", func_800730C4);

INCLUDE_ASM("asm/nonmatchings/game", func_80073138);

INCLUDE_ASM("asm/nonmatchings/game", func_800732B4);

INCLUDE_ASM("asm/nonmatchings/game", func_80073308);

INCLUDE_ASM("asm/nonmatchings/game", func_800733CC);

INCLUDE_ASM("asm/nonmatchings/game", func_800736D0);

INCLUDE_ASM("asm/nonmatchings/game", func_80073774);

INCLUDE_ASM("asm/nonmatchings/game", func_80074114);

INCLUDE_ASM("asm/nonmatchings/game", func_80074D8C);

INCLUDE_ASM("asm/nonmatchings/game", func_80074FB8);

INCLUDE_ASM("asm/nonmatchings/game", func_8007515C);

INCLUDE_ASM("asm/nonmatchings/game", func_80075AA8);

INCLUDE_ASM("asm/nonmatchings/game", func_80075AE4);

INCLUDE_ASM("asm/nonmatchings/game", func_80075B44);

INCLUDE_ASM("asm/nonmatchings/game", func_80075B9C);

INCLUDE_ASM("asm/nonmatchings/game", func_80075C2C);

INCLUDE_ASM("asm/nonmatchings/game", func_80076258);

INCLUDE_ASM("asm/nonmatchings/game", func_8007674C);

INCLUDE_ASM("asm/nonmatchings/game", func_80076888);

INCLUDE_ASM("asm/nonmatchings/game", func_80076D9C);

INCLUDE_ASM("asm/nonmatchings/game", func_80076E5C);

INCLUDE_ASM("asm/nonmatchings/game", func_80076F94);

INCLUDE_ASM("asm/nonmatchings/game", func_800770E0);

INCLUDE_ASM("asm/nonmatchings/game", func_80077260);

INCLUDE_ASM("asm/nonmatchings/game", func_800774B4);

INCLUDE_ASM("asm/nonmatchings/game", func_800776C8);

INCLUDE_ASM("asm/nonmatchings/game", func_80077908);

INCLUDE_ASM("asm/nonmatchings/game", func_800779B4);

INCLUDE_ASM("asm/nonmatchings/game", func_80078B58);

INCLUDE_ASM("asm/nonmatchings/game", func_80078BE8);

INCLUDE_ASM("asm/nonmatchings/game", func_80079234);

INCLUDE_ASM("asm/nonmatchings/game", func_80079318);

INCLUDE_ASM("asm/nonmatchings/game", func_800793BC);

INCLUDE_ASM("asm/nonmatchings/game", func_8007945C);

INCLUDE_ASM("asm/nonmatchings/game", func_800794E0);

INCLUDE_ASM("asm/nonmatchings/game", func_80079564);

INCLUDE_ASM("asm/nonmatchings/game", func_80079614);

INCLUDE_ASM("asm/nonmatchings/game", func_800796CC);

INCLUDE_ASM("asm/nonmatchings/game", func_80079764);

INCLUDE_ASM("asm/nonmatchings/game", func_800797F0);

INCLUDE_ASM("asm/nonmatchings/game", func_80079848);

INCLUDE_ASM("asm/nonmatchings/game", func_800798A0);

INCLUDE_ASM("asm/nonmatchings/game", func_800798CC);

INCLUDE_ASM("asm/nonmatchings/game", func_800798FC);

INCLUDE_ASM("asm/nonmatchings/game", func_80079C40);

INCLUDE_ASM("asm/nonmatchings/game", func_80079D00);

INCLUDE_ASM("asm/nonmatchings/game", func_8007A2A0);

INCLUDE_ASM("asm/nonmatchings/game", func_8007A2E0);

INCLUDE_ASM("asm/nonmatchings/game", func_8007AF14);

INCLUDE_ASM("asm/nonmatchings/game", func_8007AF68);

INCLUDE_ASM("asm/nonmatchings/game", func_8007B034);

INCLUDE_ASM("asm/nonmatchings/game", func_8007B048);

INCLUDE_ASM("asm/nonmatchings/game", func_8007B084);

INCLUDE_ASM("asm/nonmatchings/game", func_8007B5E0);

INCLUDE_ASM("asm/nonmatchings/game", func_8007B644);

INCLUDE_ASM("asm/nonmatchings/game", func_8007BC1C);

INCLUDE_ASM("asm/nonmatchings/game", func_8007BC54);

INCLUDE_ASM("asm/nonmatchings/game", func_8007BC9C);

INCLUDE_ASM("asm/nonmatchings/game", func_8007BD64);

INCLUDE_ASM("asm/nonmatchings/game", func_8007BDD4);

INCLUDE_ASM("asm/nonmatchings/game", func_8007BE4C);

INCLUDE_ASM("asm/nonmatchings/game", func_8007E0B0);

INCLUDE_ASM("asm/nonmatchings/game", func_8007E134);

INCLUDE_ASM("asm/nonmatchings/game", func_8007E238);

INCLUDE_ASM("asm/nonmatchings/game", func_8007E258);

INCLUDE_ASM("asm/nonmatchings/game", func_8007E410);

INCLUDE_ASM("asm/nonmatchings/game", func_8007ED94);

INCLUDE_ASM("asm/nonmatchings/game", func_8007EED8);

INCLUDE_ASM("asm/nonmatchings/game", func_8007F2EC);

INCLUDE_ASM("asm/nonmatchings/game", func_8007F730);

INCLUDE_ASM("asm/nonmatchings/game", func_8007F8D4);

INCLUDE_ASM("asm/nonmatchings/game", func_8007FAE4);

INCLUDE_ASM("asm/nonmatchings/game", func_8007FC08);

INCLUDE_ASM("asm/nonmatchings/game", func_80083984);

INCLUDE_ASM("asm/nonmatchings/game", func_8008449C);

INCLUDE_ASM("asm/nonmatchings/game", func_800844BC);

INCLUDE_ASM("asm/nonmatchings/game", func_800845C8);

INCLUDE_ASM("asm/nonmatchings/game", func_800847C0);

INCLUDE_ASM("asm/nonmatchings/game", func_80084A34);

INCLUDE_ASM("asm/nonmatchings/game", func_80084A70);

INCLUDE_ASM("asm/nonmatchings/game", func_80084AA0);

INCLUDE_ASM("asm/nonmatchings/game", func_80084AD0);

INCLUDE_ASM("asm/nonmatchings/game", func_80084AFC);

INCLUDE_ASM("asm/nonmatchings/game", func_80084CF4);

INCLUDE_ASM("asm/nonmatchings/game", func_80084E60);

INCLUDE_ASM("asm/nonmatchings/game", func_80084E80);

INCLUDE_ASM("asm/nonmatchings/game", func_80084FAC);

INCLUDE_ASM("asm/nonmatchings/game", func_80085830);

INCLUDE_ASM("asm/nonmatchings/game", func_80085E78);

INCLUDE_ASM("asm/nonmatchings/game", func_8008604C);

INCLUDE_ASM("asm/nonmatchings/game", func_8008626C);

INCLUDE_ASM("asm/nonmatchings/game", func_8008628C);

INCLUDE_ASM("asm/nonmatchings/game", func_80086464);

INCLUDE_ASM("asm/nonmatchings/game", func_800864A8);

INCLUDE_ASM("asm/nonmatchings/game", func_800865A8);

INCLUDE_ASM("asm/nonmatchings/game", func_800866C0);

INCLUDE_ASM("asm/nonmatchings/game", func_800866F0);

INCLUDE_ASM("asm/nonmatchings/game", func_80086764);

INCLUDE_ASM("asm/nonmatchings/game", func_80086808);

INCLUDE_ASM("asm/nonmatchings/game", func_80086888);

INCLUDE_ASM("asm/nonmatchings/game", func_800868E4);

INCLUDE_ASM("asm/nonmatchings/game", func_80086940);

INCLUDE_ASM("asm/nonmatchings/game", func_8008699C);

INCLUDE_ASM("asm/nonmatchings/game", func_80086A24);

INCLUDE_ASM("asm/nonmatchings/game", func_80086AAC);

INCLUDE_ASM("asm/nonmatchings/game", func_80086B68);

INCLUDE_ASM("asm/nonmatchings/game", func_80086C50);

INCLUDE_ASM("asm/nonmatchings/game", func_80086C9C);

INCLUDE_ASM("asm/nonmatchings/game", func_80086DCC);

INCLUDE_ASM("asm/nonmatchings/game", func_8008701C);

INCLUDE_ASM("asm/nonmatchings/game", func_80087058);

INCLUDE_ASM("asm/nonmatchings/game", func_80087064);

INCLUDE_ASM("asm/nonmatchings/game", func_800870A8);

INCLUDE_ASM("asm/nonmatchings/game", func_800870B4);

INCLUDE_ASM("asm/nonmatchings/game", func_800871AC);

INCLUDE_ASM("asm/nonmatchings/game", func_800871B8);

INCLUDE_ASM("asm/nonmatchings/game", func_80087238);

INCLUDE_ASM("asm/nonmatchings/game", func_8008726C);

INCLUDE_ASM("asm/nonmatchings/game", func_800872B8);

INCLUDE_ASM("asm/nonmatchings/game", func_80087480);

INCLUDE_ASM("asm/nonmatchings/game", func_8008751C);

INCLUDE_ASM("asm/nonmatchings/game", func_80087748);

INCLUDE_ASM("asm/nonmatchings/game", func_8008777C);

INCLUDE_ASM("asm/nonmatchings/game", func_800877EC);

INCLUDE_ASM("asm/nonmatchings/game", func_80087868);

INCLUDE_ASM("asm/nonmatchings/game", func_800878B8);

INCLUDE_ASM("asm/nonmatchings/game", func_800878E4);

INCLUDE_ASM("asm/nonmatchings/game", func_8008795C);

INCLUDE_ASM("asm/nonmatchings/game", func_8008799C);

INCLUDE_ASM("asm/nonmatchings/game", func_800879D4);

INCLUDE_ASM("asm/nonmatchings/game", func_80087A44);

INCLUDE_ASM("asm/nonmatchings/game", func_80087F70);

INCLUDE_ASM("asm/nonmatchings/game", func_80087FE0);

INCLUDE_ASM("asm/nonmatchings/game", func_800882A8);

INCLUDE_ASM("asm/nonmatchings/game", func_80088698);

INCLUDE_ASM("asm/nonmatchings/game", func_8008896C);

INCLUDE_ASM("asm/nonmatchings/game", func_8008897C);

INCLUDE_ASM("asm/nonmatchings/game", func_8008898C);

INCLUDE_ASM("asm/nonmatchings/game", func_8008899C);

INCLUDE_RODATA("asm/nonmatchings/game", D_80017F04);
