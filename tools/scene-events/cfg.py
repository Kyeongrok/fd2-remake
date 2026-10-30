import struct,sys,capstone,collections
sys.path.insert(0,__import__("os").path.dirname(__file__))
from fdis import d,CD,DD
from capstone import x86
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32); md.detail=True
def dd(a): return struct.unpack_from('<I',d,a+DD)[0]
def tables():
    ev=[dd(0x51B91+4*k)+0x10000 for k in range(90)]
    st=[dd(0x51D71+4*k)+0x10000 for k in range(30)]
    vi=[dd(0x51DE9+4*k)+0x10000 for k in range(30)]
    return ev,st,vi
def one(a):
    return next(md.disasm(d[a+CD:a+CD+16],a))
def func(entry):
    """return dict addr->ins for all reachable instrs"""
    ins={}; work=[entry]
    while work:
        a=work.pop()
        while a not in ins:
            i=one(a); ins[a]=i
            m=i.mnemonic
            if m=='ret': break
            if m.startswith('j'):
                op=i.operands[0]
                if op.type==x86.X86_OP_IMM:
                    work.append(op.imm)
                if m=='jmp': break
            a=i.address+i.size
    return ins
if __name__=='__main__':
    ev,st,vi=tables()
    calls=collections.Counter(); mems=collections.Counter()
    for e in ev+st+vi:
        for a,i in func(e).items():
            if i.mnemonic=='call': calls[i.op_str]+=1
            for op in i.operands:
                if op.type==x86.X86_OP_MEM and op.mem.base==0 and op.mem.index==0:
                    mems[hex(op.mem.disp+0x50000)]+=1
    print(sorted(calls.items(),key=lambda x:-x[1]))
    print(sorted(mems.items(),key=lambda x:-x[1]))
