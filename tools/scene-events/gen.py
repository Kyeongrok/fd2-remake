import sys,os,json,collections
sys.path.insert(0,os.path.dirname(__file__))
from dec import Fn, collapse
from cfg import tables
ev,st,vi=tables()
res={'events':{},'start':{},'victory':{}}
for k,a in enumerate(ev): res['events'][str(k)]=collapse(Fn(a,'events').run())
for k,a in enumerate(st): res['start'][str(k)]=collapse(Fn(a,'start').run(0x327eb if k==0 else None))
for k,a in enumerate(vi): res['victory'][str(k)]=collapse(Fn(a,'victory').run())
json.dump(res,open('out.json','w',encoding='utf-8'),ensure_ascii=False)
raws=collections.Counter()
def walk(ops,where):
    for o in ops:
        if o[0]=='raw': raws[o[1].split('(')[0] if o[1].startswith('sub_') else o[1]]+=1; print(where,o[1])
        if o[0]=='if': walk(o[2],where); walk(o[3],where)
for sec in res:
    for k,ops in res[sec].items(): walk(ops,sec+k)
print(sorted(raws.items(),key=lambda x:-x[1]))
