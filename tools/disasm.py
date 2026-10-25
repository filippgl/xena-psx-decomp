import sys,struct,rabbitizer
d=open('xena.exe','rb').read()
BASE=0x80010000
def word(va): return struct.unpack_from('<I',d,va-BASE+0x800)[0]
def dis(start,end):
    for va in range(start,end,4):
        ins=rabbitizer.Instruction(word(va),va)
        print(f"{va:08X}: {ins.disassemble()}")
if __name__=='__main__':
    dis(int(sys.argv[1],16),int(sys.argv[2],16))
