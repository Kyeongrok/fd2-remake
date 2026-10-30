import sys,os,re,glob,collections
sys.path.insert(0,os.path.dirname(__file__))
from cfg import one
from capstone import x86
m=collections.defaultdict(collections.Counter)
for f in glob.glob(r"C:\Users\ocean\git\fd2\fd2_freebuff\export-for-ai\asm\*.asm"):
    for line in open(f,encoding='utf-8',errors='replace'):
        mm=re.match(r'^([0-9a-f]{5})\s+(\w+)\s+(.*)',line)
        if not mm: continue
        names=re.findall(r'\b((?:n|dword_|byte_|word_|qword_)[0-9A-Za-z_]+)\b',mm.group(3))
        if not names: continue
        try: i=one(int(mm.group(1),16))
        except StopIteration: continue
        for op in i.operands:
            if op.type==x86.X86_OP_MEM and op.mem.base==0 and op.mem.index==0:
                for n in names: m[n][hex(op.mem.disp+0x50000)]+=1
            if op.type==x86.X86_OP_IMM and 0x50000<=op.imm+0x50000<0x56000 and 'offset' in mm.group(3):
                for n in names: m[n][hex(op.imm+0x50000)]+=1
for n,c in sorted(m.items()):
    print(n,c.most_common(1)[0][0])
