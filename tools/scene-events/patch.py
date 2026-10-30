import json,sys
r=json.load(open('out.json',encoding='utf-8'))
def recond(ops, old, new):
    n=0
    for o in ops:
        if o[0]=='if':
            if o[1]==old: o[1]=new; n+=1
            n+=recond(o[2],old,new)+recond(o[3],old,new)
    return n
assert recond(r['events']['19'],'?m==1','anyalive(7..36)')==1
assert recond(r['victory']['1'],'?m==0','!anydead(5..10)')==1
assert recond(r['victory']['15'],'?m!=1','deadcount(66..73)<=4')==1
v=r['victory']['20']
for o in v:
    if o[0]=='if' and o[1]=='?m==6':
        o[1]='hasall(209..214)'
        o[2]=[['raw','dropitem: remove one each of items 209..214 from party']]+[x for x in o[2] if not (x[0]=='if' and x[1].startswith('itemslot'))]
s=r['start']['14']
assert s[1]==['raw','talk(?q)']
r['start']['14']=[s[0],['if','inparty(12)',[['talk',0],['camera',24,17],['talk',1],['script',48],['talk',2]],[['talk',3],['camera',24,17],['talk',4],['script',48],['talk',5]]]]
assert r['victory']['14'][0]==['raw','talk(?+12)']
r['victory']['14'][0]=['if','inparty(12)',[['talk',12]],[['talk',13]]]
# compact JSON: one op list per line
out=['{']
for si,sec in enumerate(['events','start','victory']):
    out.append('  "%s": {'%sec)
    keys=list(r[sec])
    for ki,k in enumerate(keys):
        out.append('    "%s": %s%s'%(k,json.dumps(r[sec][k],ensure_ascii=False),',' if ki<len(keys)-1 else ''))
    out.append('  }%s'%(',' if si<2 else ''))
out.append('}')
txt='\n'.join(out)+'\n'
json.loads(txt)
open(sys.argv[1],'w',encoding='utf-8',newline='\n').write(txt)
print(len(txt))
