import struct,sys
sys.path.insert(0,r"C:\Users\ocean\AppData\Local\Temp\claude\C--Users-ocean-git-fd2-fd2-remake\9632b279-0093-4280-b5a9-728dad66cc96\scratchpad\evw")
from fdis import d,DD
def dd(a): return struct.unpack_from('<I',d,a+DD)[0]
for name,base,n in [('ev',0x51B91,90),('st',0x51D71,30),('vi',0x51DE9,30)]:
    print(name,[hex(dd(base+4*k)+0x10000) for k in range(n)])
