import sys, os, struct, json, copy
sys.path.insert(0, os.path.dirname(__file__))
from cfg import one, func, tables, d, DD
from capstone import x86

def db(a): return d[a + DD]
def dw(a): return struct.unpack_from('<H', d, a + DD)[0]

class V:
    __slots__ = ('base', 'off')
    def __init__(s, base=None, off=0): s.base = base; s.off = off
    def const(s): return s.base is None
    def __repr__(s):
        if s.base is None: return str(s.off)
        if s.off == 0: return s.base
        return '%s%+d' % (s.base, s.off)

C = lambda v: V(None, v)
PTRS = ('units', 'info', 'flags')
def _p(b):
    if '*' in b and '+' not in b:
        x, k = b.rsplit('*', 1); return x, int(k)
    return b, 1
def _mk(x, k): return x if k == 1 else '%s*%d' % (x, k)
def vadd(x, y):
    if x.const(): return V(y.base, x.off + y.off)
    if y.const(): return V(x.base, x.off + y.off)
    if '+' in x.base or '+' in y.base or '?' in x.base or '?' in y.base: return U('?')
    (s1, k1), (s2, k2) = _p(x.base), _p(y.base)
    if s1 == s2: return V(_mk(s1, k1 + k2), x.off + y.off)
    if s1 in PTRS and k1 == 1 and s2 not in PTRS: return V('%s+%s' % (s1, y.base), x.off + y.off)
    if s2 in PTRS and k2 == 1 and s1 not in PTRS: return V('%s+%s' % (s2, x.base), x.off + y.off)
    return U('?')
def vmul(x, k):
    if x.const(): return C(x.off * k)
    if '+' in x.base or '?' in x.base: return U('?')
    s1, k1 = _p(x.base); return V(_mk(s1, k1 * k), x.off * k)
U = lambda t: V(t, 0)
GLOB = {0x53a45: 'units', 0x53a55: 'info', 0x53ad5: 'flags', 0x53bef: 'turn', 0x53c03: 'n17',
        0x53a79: 'dlg', 0x53beb: 'nunits', 0x53ecc: 'n2_0'}
REG32 = {}
for r32, subs in [('eax', 'ax al ah'), ('ebx', 'bx bl bh'), ('ecx', 'cx cl ch'), ('edx', 'dx dl dh'),
                  ('esi', 'si'), ('edi', 'di'), ('ebp', 'bp'), ('esp', 'sp')]:
    REG32[r32] = r32
    for s in subs.split(): REG32[s] = r32

INLINE = {0x35b78, 0x34d64}
ARITY = {0x24bde:1, 0x13512:1, 0x35f10:1, 0x1c220:1, 0x1b8e7:2, 0x233c6:11, 0x33f78:3, 0x344f2:3, 0x35b78:3, 0x10b4e:1, 0x32999:1, 0x1366a:1, 0x135dd:2, 0x13185:1, 0x15f84:9, 0x112a5:1, 0x32975:1, 0x25977:2, 0x1aa1d:3, 0x34894:1, 0x33499:1}
IGNORE = {0x11506, 0x3702f, 0x3790a, 0x37910, 0x11cac, 0x4e381, 0x134e4, 0x17aa9, 0x12d7b}

def cond_text(c, a, b):
    def t(v):
        if v.base and v.base.startswith('call:'):
            return v.base[5:] + ('%+d' % v.off if v.off else '')
        return repr(v)
    if b.const() and b.off == -1 and a.base and a.base.startswith('call:') and c in ('==', '!='):
        return ('!' if c == '==' else '') + t(a)
    if b.const() and b.off == 0 and a.base and a.base.startswith('call:') and c in ('==', '!='):
        return ('!' if c == '==' else '') + t(a)
    return '%s%s%s' % (t(a), c, t(b))

NEG = {'==': '!=', '!=': '==', '<': '>=', '>=': '<', '>': '<=', '<=': '>'}
JCC = {'je': '==', 'jz': '==', 'jne': '!=', 'jnz': '!=', 'jl': '<', 'jge': '>=', 'jg': '>', 'jle': '<=',
       'jb': '<', 'jae': '>=', 'ja': '>', 'jbe': '<='}

def evalc(c, a, b):
    x, y = a.off, b.off
    return {'==': x == y, '!=': x != y, '<': x < y, '>=': x >= y, '>': x > y, '<=': x <= y}[c]

class Fn:
    def __init__(s, entry, kind):
        s.entry = entry; s.kind = kind
        s.ins = func(entry)
        s.succ = {}
        for a, i in s.ins.items():
            m = i.mnemonic; nx = a + i.size
            if m == 'ret': s.succ[a] = ['X']
            elif m == 'jmp':
                op = i.operands[0]
                s.succ[a] = [op.imm] if op.type == x86.X86_OP_IMM else ['X']
            elif m.startswith('j'):
                s.succ[a] = [i.operands[0].imm, nx]
            else: s.succ[a] = [nx]
        nodes = list(s.ins) + ['X']
        allset = set(nodes)
        pd = {n: set(allset) for n in nodes}; pd['X'] = {'X'}
        ch = True
        while ch:
            ch = False
            for n in s.ins:
                new = set.intersection(*[pd[x] for x in s.succ[n] if x in pd]) | {n}
                if new != pd[n]: pd[n] = new; ch = True
        s.pd = pd
        s.budget = 20000

    def ipdom(s, n):
        c = [p for p in s.pd[n] if p != n]
        return max(c, key=lambda p: len(s.pd[p]))

    def run(s, at=None):
        st = {'r': {r: U(r + '0') for r in ['eax', 'ebx', 'ecx', 'edx', 'esi', 'edi', 'ebp']}, 'sp': 0, 'sd': {4: U('arg0'), 0: U('ret')}, 'sb': {}, 'fl': None, 'esi_src': None}
        return s.emit(at or s.entry, None, st, 0)

    def get(s, st, op, size=4):
        if op.type == x86.X86_OP_IMM: return C(op.imm)
        if op.type == x86.X86_OP_REG:
            n = s.ins_reg(op.reg)
            if n == 'esp': return V('sp', st['sp'])
            return st['r'].get(REG32.get(n, n), U(n))
        # mem
        base, disp = s.addr(st, op)
        if base is None:
            a = disp + 0x50000
            if a in GLOB: return U(GLOB[a])
            return U('g%x' % a)
        if base == 'sp': return s.sread(st, disp, op.size)
        if base == 'units': return U('unit(%d).%d' % divmod(disp, 80))
        if base and base.startswith('units+') and base.endswith('*80'): return U('unit(%s).%d' % (base[6:-3], disp))
        if base == 'flags': return U('flag(%d)' % disp)
        return U('%s[%d]' % (base, disp))

    def sread(s, st, A, size):
        if size == 4 and A in st['sd']: return st['sd'][A]
        bs = [st['sb'].get(A + k) for k in range(size)]
        if None not in bs: return C(int.from_bytes(bytes(bs), 'little'))
        if A in st['sd']: return st['sd'][A]
        return U('local')
    def swrite(s, st, A, size, v):
        for k in range(size): st['sb'].pop(A + k, None)
        for k in range(A - 3, A + size):
            if k in st['sd'] and k != A: del st['sd'][k]
        st['sd'].pop(A, None)
        if v.const():
            for k in range(size): st['sb'][A + k] = (v.off >> (8 * k)) & 0xff
        else: st['sd'][A] = v
    def push(s, st, v):
        st['sp'] -= 4; s.swrite(st, st['sp'], 4, v)
    def ins_reg(s, r):
        return s.cur.reg_name(r)

    def addr(s, st, op):
        m = op.mem
        disp = m.disp
        b = None
        parts = []
        for r, sc in ((m.base, 1), (m.index, m.scale)):
            if r == 0: continue
            n = REG32[s.ins_reg(r)]
            if n == 'esp': parts.append(V('sp', st['sp'])); continue
            v = st['r'][n]
            if sc != 1:
                v = vmul(v, sc)
            parts.append(v)
        acc = C(disp)
        for v in parts: acc = vadd(acc, v)
        return acc.base, acc.off

    def setreg(s, st, op, v):
        n = REG32.get(s.ins_reg(op.reg))
        if n and n != 'esp': st['r'][n] = v

    def write(s, st, op, val, out):
        base, disp = s.addr(st, op)
        if base == 'sp': s.swrite(st, disp, op.size, val); return
        if base is None:
            a = disp + 0x50000
            if a == 0x51a83: return
            if a == 0x53afb:
                if val.const() and val.off == 1: out.append(['dark'])
                return
            if a == 0x53afa:
                if val.const() and val.off == 1: out.append(['stack'])
                return
            if a == 0x53ecc and val.const():
                out.append({2: ['win'], 1: ['lose']}.get(val.off, ['raw', 'n2_0=%d' % val.off])); return
            if a in (0x53c03, 0x53bf3): return
            out.append(['raw', 'g%x=%r' % (a, val)]); return
        if base == 'units':
            i, f = divmod(disp, 80)
            if val.const():
                if f == 6 and val.off == 1: out.append(['ally', i])
                else: out.append(['setunit', i, f, val.off & (0xff if s.cur_size == 1 else 0xffff if s.cur_size == 2 else 0xffffffff)])
            else: out.append(['raw', 'unit[%d]+%d=%r' % (i, f, val)])
            return
        if base == 'info':
            if 3 <= disp < 51 and (disp - 3) % 3 == 0:
                k = (disp - 3) // 3
                if val.base == 'turn':
                    out.append(['arm', k] if val.off == 1 else ['arm', k, val.off])
                elif val.const(): out.append(['armset', k, val.off & 0xff])
                else: out.append(['raw', 'info[%d]=%r' % (disp, val)])
            else: out.append(['raw', 'info[%d]=%r' % (disp, val)])
            return
        if base == 'flags':
            if val.const() and val.off == 1: out.append(['flag', disp])
            else: out.append(['raw', 'flag[%d]=%r' % (disp, val)])
            return
        out.append(['raw', '[%s+%d]=%r' % (base, disp, val)])

    def call(s, st, tgt, nargs, out):
        args = [s.sread(st, st['sp'] + 4 * k, 4) for k in range(nargs)]
        av = [a.off if a.const() else repr(a) for a in args]
        allc = all(a.const() for a in args)
        r = None
        if tgt == 0x3702f: st['sp'] += 4
        elif tgt in IGNORE: pass
        elif tgt == 0x10b4e and allc: out.append(['spawn', av[0]])
        elif tgt == 0x10b4e: out.append(['raw', 'spawn(%s)' % av[0]])
        elif tgt == 0x32999 and allc: out.append(['spawnfx', av[0]])
        elif tgt == 0x1366a and allc: out.append(['script', av[0]])
        elif tgt == 0x135dd and allc: out.append(['camera', av[0], av[1]])
        elif tgt == 0x13185 and allc: out.append(['stepup', av[0]])
        elif tgt == 0x15f84 and args[0].base == 'dlg' and args[1].const(): out.append(['talk', av[1]])
        elif tgt == 0x112a5 and allc: out.append(['join', av[0]])
        elif tgt == 0x32975 and allc: out.append(['remove', av[0]])
        elif tgt == 0x25977 and args[0].const():
            out.append(['music', av[0]] if av[0] != 0xffffffff else ['music', -1])
        elif tgt == 0x1aa1d and args[2].base == 'sp':
            k = s.sread(st, args[2].off, 1); v = s.sread(st, args[2].off + 1, 2)
            out.append(['reward', k.off if k.const() else repr(k), v.off if v.const() else repr(v)])
        elif tgt in INLINE:
            s.push(st, U('ret'))
            sub = Fn(tgt, s.kind)
            out.extend(sub.emit(tgt, None, st, 1))
            st['sp'] += 4
        elif tgt == 0x361b0: out.append(['raw', 'flash'])
        elif tgt == 0x15f84 and args[0].base == 'dlg': out.append(['raw', 'talk(%s)' % av[1]])
        elif tgt == 0x13512: out.append(['raw', 'unit(%s).5|=0x80' % av[0]])
        elif tgt == 0x35f10: out.append(['raw', 'killfrom(%s) (unit %s.. HP=0)' % (av[0], av[0])])
        elif tgt == 0x12263: out.append(['raw', 'refresh chest tiles'])
        elif tgt == 0x1c220: out.append(['raw', 'giveitem(%s)' % av[0]])
        elif tgt == 0x1b8e7: out.append(['raw', 'dropitem(%s,%s)' % tuple(av)])
        elif tgt == 0x33f78: out.append(['raw', 'warp(%s,%s,%s)' % tuple(av)])
        elif tgt == 0x344f2: out.append(['raw', 'ai(units %s..%s, %s)' % tuple(av)])
        elif tgt == 0x233c6:
            def arr(p, i):
                if p.const(): return p.off
                if p.base == 'sp':
                    b = s.sread(st, p.off + i, 1); return b.off if b.const() else '?'
                return '?'
            a4, a5 = args[3].off, args[4].off
            pl = ['%d:(%s,%s,%s)' % (i, arr(args[0], i), arr(args[1], i), arr(args[2], i) if not args[2].const() or args[2].off >= 4 else args[2].off) for i in range(a4, a5 + 1)]
            extra = ' u%s:(%s,%s,%s)' % tuple(av[5:9]) if av[5] else ''
            out.append(['raw', 'place %s;%s cam(%s,%s)' % (' '.join(pl), extra, av[9], av[10] if len(av) > 10 else '?')])
        elif tgt == 0x205da:
            out.append(['raw', 'sub_205DA load scene'])
        elif tgt == 0x34894: r = 'dead(%s)' % av[0]
        elif tgt == 0x33499: r = 'inparty(%s)' % av[0]
        elif tgt == 0x24b14: r = 'hasitem(%s)' % av[0]
        elif tgt == 0x2aedb: r = 'itemslot(%s,%s)' % tuple(av)
        elif tgt == 0x1b8a6: r = 'nitems(%s)' % av[0]
        elif tgt == 0x4ebe3: r = 'rand()'
        elif tgt == 0x24bde: r = 'inroster(%s)' % av[0]
        else:
            if tgt not in (0x11df2, 0x11d40):
                out.append(['raw', 'sub_%X(%s)' % (tgt, ','.join(hex(x) if isinstance(x, int) and x > 0xffff else str(x) for x in av))])
            r = 'sub_%X(%s)' % (tgt, ','.join(map(str, av)))
        st['r']['eax'] = U('call:' + (r or 'sub_%X' % tgt))

    def emit(s, a, stop, st, depth):
        out = []
        while a != stop and a != 'X':
            s.budget -= 1
            if s.budget < 0 or depth > 12:
                out.append(['raw', 'unresolved@%x' % a]); return out
            if a in IGNORE and a != s.entry: return out
            i = s.ins[a]; s.cur = i
            m = i.mnemonic; ops = i.operands; nx = a + i.size
            s.cur_size = ops[0].size if ops else 4
            r = st['r']
            if m == 'ret': st['term'] = True; return out
            if m == 'jmp':
                if ops[0].imm == a: out.append(['raw', 'halt']); st['term'] = True; return out
                a = ops[0].imm; continue
            if m in JCC:
                fl = st['fl']
                c = JCC[m]
                if fl and fl[0].const() and fl[1].const():
                    a = ops[0].imm if evalc(c, fl[0], fl[1]) else nx; continue
                tgt = ops[0].imm
                if fl and any(v.base and 'nunits' in v.base for v in fl) and any(v.const() for v in fl):
                    body = tgt if tgt < a else nx
                    exitp = nx if tgt < a else tgt
                    stb = copy.deepcopy(st)
                    bops = s.emit(body, a, stb, depth + 1)
                    out.append(['raw', 'units loop from %r while <nunits: %s' % ([v for v in fl if v.const()][0], json.dumps(bops))])
                    a = exitp; continue
                merge = s.ipdom(a)
                if merge == 'X': merge = None
                txt = cond_text(NEG[c], *fl) if fl else '?' + m
                st['term'] = False
                st1 = copy.deepcopy(st); st2 = copy.deepcopy(st)
                if fl and fl[1].const() and c in ('==', '!='):
                    eqst = st2 if c == '==' else st1
                    for rr, vv in list(eqst['r'].items()):
                        if repr(vv) == repr(fl[0]): eqst['r'][rr] = C(fl[1].off)
                sv = copy.deepcopy(st1), copy.deepcopy(st2)
                then = s.emit(nx, merge, st1, depth + 1)
                els = s.emit(ops[0].imm, merge, st2, depth + 1)
                if merge is not None and st1['sp'] == st2['sp'] and st1['sp'] < sv[0]['sp'] and any(
                        repr(s.sread(st1, A, 4)) != repr(s.sread(st2, A, 4)) for A in range(st1['sp'], sv[0]['sp'], 4)):
                    then = s.emit(nx, None, sv[0], depth + 1)
                    els = s.emit(ops[0].imm, None, sv[1], depth + 1)
                    out.append(['if', txt, then, els]); st['term'] = True; return out
                if then or els: out.append(['if', txt, then, els])
                if merge is None or (st1.get('term') and st2.get('term')): st['term'] = True; return out
                if st1.get('term'): st1 = st2
                elif st2.get('term'): st2 = st1
                for rr in st1['r']:
                    if repr(st1['r'][rr]) != repr(st2['r'][rr]): st1['r'][rr] = U('?m')
                for A in set(st1['sb']) | set(st2['sb']):
                    if st1['sb'].get(A) != st2['sb'].get(A): st1['sb'].pop(A, None); st1['sd'][A & ~3] = U('?m')
                st.update(st1)
                a = merge; continue
            if m.startswith('j'):
                out.append(['raw', 'jcc %s' % m]); a = nx; continue
            if m == 'push':
                s.push(st, s.get(st, ops[0])); a = nx; continue
            if m == 'pop':
                v = s.sread(st, st['sp'], 4); st['sp'] += 4
                if ops[0].type == x86.X86_OP_REG: s.setreg(st, ops[0], v)
                a = nx; continue
            if m == 'call':
                tgt = ops[0].imm if ops[0].type == x86.X86_OP_IMM else None
                n2 = s.ins.get(nx)
                nargs = 0
                if n2 and n2.mnemonic == 'add' and n2.op_str.startswith('esp,'):
                    nargs = n2.operands[1].imm // 4
                if tgt in ARITY: nargs = ARITY[tgt]
                if tgt is None: out.append(['raw', 'call ' + i.op_str])
                else: s.call(st, tgt, nargs, out)
                a = nx; continue
            if m in ('add', 'sub') and ops[0].type == x86.X86_OP_REG and s.ins_reg(ops[0].reg) == 'esp':
                if m == 'add': st['sp'] += ops[1].imm
                else: st['sp'] -= ops[1].imm
                a = nx; continue
            if m in ('mov', 'movzx', 'movsx'):
                if ops[1].type == x86.X86_OP_IMM and ops[0].type == x86.X86_OP_REG and s.ins_reg(ops[0].reg) == 'esi':
                    pass
                v = s.get(st, ops[1])
                if ops[0].type == x86.X86_OP_MEM: s.write(st, ops[0], v, out)
                else: s.setreg(st, ops[0], v)
                a = nx; continue
            if m == 'lea':
                b, disp = s.addr(st, ops[1])
                s.setreg(st, ops[0], V(b, disp)); a = nx; continue
            if m in ('add', 'sub', 'inc', 'dec', 'shl', 'sal', 'sar', 'shr', 'imul', 'xor', 'and', 'or', 'neg', 'not'):
                x = s.get(st, ops[0])
                y = s.get(st, ops[1]) if len(ops) > 1 else None
                if len(ops) == 3: x = y; y = s.get(st, ops[2])
                if m == 'add': v = vadd(x, y)
                elif m in ('shl', 'sal') and y.const() and not x.const(): v = vmul(x, 1 << y.off)
                elif m == 'imul' and y.const() and not x.const(): v = vmul(x, y.off)
                elif m == 'sub': v = V(x.base, x.off - y.off) if y.const() else (C(0) if repr(x) == repr(y) else vadd(x, vmul(y, -1)))
                elif m == 'sar' and y.const() and not x.const(): v = C(0) if y.off == 31 else U('(%r>>%d)' % (x, y.off))
                elif m == 'inc': v = V(x.base, x.off + 1)
                elif m == 'dec': v = V(x.base, x.off - 1)
                elif m == 'xor' and ops[1].type == x86.X86_OP_REG and ops[0].reg == ops[1].reg: v = C(0)
                elif x.const() and (y is None or y.const()):
                    yo = y.off if y else 0
                    v = C({'shl': x.off << yo, 'sal': x.off << yo, 'sar': x.off >> yo, 'shr': x.off >> yo, 'imul': x.off * yo,
                           'xor': x.off ^ yo, 'and': x.off & yo, 'or': x.off | yo, 'neg': -x.off, 'not': ~x.off}[m])
                elif m == 'and' and y.const():
                    v = U('(%r&%d)' % (x, y.off))
                elif m == 'or' and y.const() and x.base and x.base.startswith('units['):
                    v = U('(%r|%d)' % (x, y.off))
                else: v = U('?')
                if ops[0].type == x86.X86_OP_MEM:
                    if m in ('inc', 'dec', 'add', 'sub', 'or', 'and', 'xor') :
                        b, disp = s.addr(st, ops[0])
                        if b is None and disp + 0x50000 == 0x53c03 and s.kind == 'victory': pass
                        elif b == 'units' and m in ('or', 'and') and y.const():
                            i2, f = divmod(disp, 80)
                            out.append(['raw', 'unit[%d]+%d %s= %d' % (i2, f, '|' if m == 'or' else '&', y.off)])
                        else: s.write(st, ops[0], v, out)
                else: s.setreg(st, ops[0], v)
                st['fl'] = (v, C(0))
                a = nx; continue
            if m == 'cmp':
                st['fl'] = (s.get(st, ops[0]), s.get(st, ops[1])); a = nx; continue
            if m == 'test':
                x = s.get(st, ops[0]); y = s.get(st, ops[1])
                if ops[1].type == x86.X86_OP_REG and ops[0].type == x86.X86_OP_REG and ops[0].reg == ops[1].reg:
                    st['fl'] = (x, C(0))
                elif x.const() and y.const(): st['fl'] = (C(x.off & y.off), C(0))
                else: st['fl'] = (U('(%r&%r)' % (x, y)), C(0))
                a = nx; continue
            if m in ('movsw', 'movsb', 'movsd', 'rep movsd', 'rep movsb', 'rep movsw'):
                n = {'b': 1, 'w': 2, 'd': 4}[m[-1]]
                cnt = 1
                if m.startswith('rep'):
                    cnt = r['ecx'].off if r['ecx'].const() else 0
                    r['ecx'] = C(0)
                src, dst = r['esi'], r['edi']
                tot = n * cnt
                for k in range(tot):
                    if src.const(): b = C(db(src.off + k + 0x50000))
                    elif src.base == 'sp': b = s.sread(st, src.off + k, 1)
                    else: b = U('?')
                    if dst.base == 'sp': s.swrite(st, dst.off + k, 1, b)
                r['esi'] = V(src.base, src.off + tot); r['edi'] = V(dst.base, dst.off + tot)
                a = nx; continue
            if m in ('idiv', 'div', 'mul'):
                r['eax'] = U('?q'); r['edx'] = U('?rem'); a = nx; continue
            if m in ('cdq', 'nop', 'cld'):
                a = nx; continue
            if m == 'setne' or m == 'sete' or m.startswith('set'):
                s.setreg(st, ops[0], U('?')); a = nx; continue
            out.append(['raw', 'asm %s %s' % (m, i.op_str)])
            a = nx
        return out

import re
def collapse(ops):
    res = []
    for o in ops:
        if o[0] == 'if': o = ['if', o[1], collapse(o[2]), collapse(o[3])]
        if o[0] == 'raw' and res and res[-1][0] == 'raw':
            p = res[-1]
            m1 = re.split(r'(-?\d+)', p[1]); m2 = re.split(r'(-?\d+)', o[1])
            if len(m1) == len(m2):
                diff = [k for k in range(len(m1)) if m1[k] != m2[k]]
                if len(diff) == 1 and diff[0] % 2 == 1 and not m1[diff[0]-1].endswith('sub_'):
                    k = diff[0]
                    prev = m1[k]
                    if '..' in p[1] and p[1].endswith(']') is False and False: pass
                    res[-1] = ['raw', o[1]]; res[-1].append(p[2] if len(p) > 2 else p[1])
                    continue
        res.append(o)
    # turn merged pairs into range text
    out = []
    for o in res:
        if o[0] == 'raw' and len(o) == 3:
            a = re.split(r'(-?\d+)', o[2]); b = re.split(r'(-?\d+)', o[1])
            k = [i for i in range(len(a)) if a[i] != b[i]]
            if len(k) == 1:
                a[k[0]] = a[k[0]] + '..' + b[k[0]]
                out.append(['raw', ''.join(a)]); continue
            out.append(['raw', o[1]]); continue
        out.append(o)
    return out

def decode(entry, kind):
    return Fn(entry, kind).run()

if __name__ == '__main__':
    a = int(sys.argv[1], 16); kind = sys.argv[2] if len(sys.argv) > 2 else 'events'
    for o in decode(a, kind): print(json.dumps(o, ensure_ascii=False))
