import os,struct,sys,capstone
d=open(os.environ["FD2_EXE"],"rb").read()  # 원작 FD2.EXE 자리(환경 변수)
CD=0x26014; DD=0x25014
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32)
def dis(a,n=0x400):
    out=[]
    for ins in md.disasm(d[a+CD:a+CD+n],a):
        out.append(ins)
    return out
if __name__=='__main__':
    a=int(sys.argv[1],16); n=int(sys.argv[2],16) if len(sys.argv)>2 else 0x200
    for i in dis(a,n): print(hex(i.address),i.mnemonic,i.op_str)
