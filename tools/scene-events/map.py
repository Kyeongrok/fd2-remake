import os,struct
d=open(os.environ["FD2_EXE"],"rb").read()  # 원작 FD2.EXE 자리(환경 변수)
pat=bytes([0x68,0x20,0,0,0,0xe8])  # push 20h; call
# sub_10B4E starts with push 20h (68 20 00 00 00) call
i=-1;hits=[]
while True:
    i=d.find(pat,i+1)
    if i<0:break
    hits.append(i)
print(len(hits),[hex(h) for h in hits[:10]])
# data: 0x50000 -> 0x75014 ; check table 0x51B91
def dd(a): return struct.unpack_from('<I',d,a-0x50000+0x75014)[0]
print([hex(dd(0x51B91+4*k)) for k in range(5)])
rel=(0x3702F-0x10b58)&0xffffffff
p=bytes([0x68,0x20,0,0,0,0xe8])+struct.pack('<I',rel)
o=d.find(p); print('10b4e at file',hex(o), 'delta',hex(o-0x10b4e))
