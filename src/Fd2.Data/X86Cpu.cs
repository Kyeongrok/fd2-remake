using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// FD2.EXE 코드 토막을 그대로 돌리는 작은 x86(32비트) 해석기 — 버전마다 고친 계산식(곡우 · 파워 인핸스드의 피해 식 · 직업 상성 같은)을
/// 손으로 옮기지 않고 그 EXE 의 기계어로 셈한다. 정수 명령 일부만 알고, 모르는 명령 · 다른 함수 부르기를 만나면 <see cref="X86Fault"/>.
/// </summary>
/// <remarks>
/// 메모리: 코드는 VA + 0x26014(오브젝트 1). 명령 안의 자료 주소(작은 수)는 오브젝트 2 바탕 0x50000 에서 센 자리라 VA 0x50000 + 값으로 읽고,
/// 쓰기는 덧칠(<see cref="_data"/>)에만 남긴다. 사람 칸 · 스택 · 결과 칸 같은 것은 <see cref="Map"/> 으로 큰 주소에 붙인다.
/// 부르기는 난수(sub_4EBE3)만 — <see cref="Rand"/> 가 eax 를 준다.
/// </remarks>
public sealed class X86Cpu
{
    public const uint RandVa = 0x4EBE3;
    private const int CodeOffset = 0x26014;

    private readonly byte[] _exe;
    private readonly List<(uint Base, byte[] Buf)> _maps = new();
    private readonly Dictionary<uint, byte> _data = new();

    public readonly uint[] R = new uint[8];   // eax ecx edx ebx esp ebp esi edi
    public uint Eip;
    private bool _zf, _sf, _cf, _of;

    public Func<uint>? Rand;

    /// <summary>자료 읽기 덮기(VA → 값) — 편집기 표 값(필살 % 같은)을 EXE 바이트 대신 줄 때. null 이면 EXE.</summary>
    public Func<uint, byte?>? DataOverride;

    public X86Cpu(byte[] exe) => _exe = exe;

    public void Map(uint at, byte[] buf) => _maps.Add((at, buf));

    public static int FileOffset(uint va) => va >= 0x60000 ? (int)va + 0x19014 : (int)va + 0x25014;

    public static bool CodeAt(byte[] exe, uint va, string hex)
    {
        var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int o = (int)va + CodeOffset;
        if (o < 0 || o + parts.Length > exe.Length) return false;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != "??" && exe[o + i] != Convert.ToByte(parts[i], 16)) return false;
        return true;
    }

    // ── 메모리 ──
    public byte Read8(uint a)
    {
        foreach (var (b, buf) in _maps)
            if (a >= b && a - b < buf.Length) return buf[a - b];
        if (a < 0x20000)
        {
            uint va = 0x50000 + a;
            if (_data.TryGetValue(va, out byte v)) return v;
            if (DataOverride?.Invoke(va) is { } o) return o;
            int f = FileOffset(va);
            if (f >= 0 && f < _exe.Length) return _exe[f];
        }
        throw new X86Fault($"읽기 {a:X}");
    }

    public void Write8(uint a, byte v)
    {
        foreach (var (b, buf) in _maps)
            if (a >= b && a - b < buf.Length) { buf[a - b] = v; return; }
        if (a < 0x20000) { _data[0x50000 + a] = v; return; }
        throw new X86Fault($"쓰기 {a:X}");
    }

    public uint Read(uint a, int size) => size switch
    {
        1 => Read8(a),
        2 => (uint)(Read8(a) | Read8(a + 1) << 8),
        _ => (uint)(Read8(a) | Read8(a + 1) << 8 | Read8(a + 2) << 16 | Read8(a + 3) << 24),
    };

    public void Write(uint a, uint v, int size)
    {
        for (int i = 0; i < size; i++) Write8(a + (uint)i, (byte)(v >> (8 * i)));
    }

    private byte Fetch() => _exe[(int)Eip++ + CodeOffset];
    private uint Fetch32() { uint v = (uint)(Fetch() | Fetch() << 8 | Fetch() << 16 | Fetch() << 24); return v; }
    private ushort Fetch16() => (ushort)(Fetch() | Fetch() << 8);

    // ── 레지스터 ──
    private uint GetReg(int r, int size) => size switch
    {
        1 => r < 4 ? R[r] & 0xFF : (R[r - 4] >> 8) & 0xFF,
        2 => R[r] & 0xFFFF,
        _ => R[r],
    };

    private void SetReg(int r, int size, uint v)
    {
        switch (size)
        {
            case 1:
                if (r < 4) R[r] = (R[r] & ~0xFFu) | (v & 0xFF);
                else R[r - 4] = (R[r - 4] & ~0xFF00u) | ((v & 0xFF) << 8);
                break;
            case 2: R[r] = (R[r] & ~0xFFFFu) | (v & 0xFFFF); break;
            default: R[r] = v; break;
        }
    }

    // ── ModRM ──
    private struct Operand
    {
        public bool IsReg;
        public int Reg;
        public uint Addr;
    }

    private (int Reg, Operand Rm) ModRm()
    {
        byte m = Fetch();
        int mod = m >> 6, reg = (m >> 3) & 7, rm = m & 7;
        if (mod == 3) return (reg, new Operand { IsReg = true, Reg = rm });
        uint addr;
        if (rm == 4)
        {
            byte sib = Fetch();
            int scale = sib >> 6, idx = (sib >> 3) & 7, bas = sib & 7;
            uint b = bas == 5 && mod == 0 ? Fetch32() : R[bas];
            uint i = idx == 4 ? 0 : R[idx] << scale;
            addr = b + i;
        }
        else if (rm == 5 && mod == 0) addr = Fetch32();
        else addr = R[rm];
        if (mod == 1) addr += (uint)(sbyte)Fetch();
        else if (mod == 2) addr += Fetch32();
        return (reg, new Operand { Addr = addr });
    }

    private uint Get(Operand o, int size) => o.IsReg ? GetReg(o.Reg, size) : Read(o.Addr, size);
    private void Set(Operand o, int size, uint v) { if (o.IsReg) SetReg(o.Reg, size, v); else Write(o.Addr, v, size); }

    private static uint Mask(int size) => size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : 0xFFFFFFFFu;
    private static uint SignBit(int size) => size == 1 ? 0x80u : size == 2 ? 0x8000u : 0x80000000u;
    private static int Sx(uint v, int size) => size == 1 ? (sbyte)v : size == 2 ? (short)v : (int)v;

    private void Logic(uint r, int size)
    {
        r &= Mask(size);
        _zf = r == 0; _sf = (r & SignBit(size)) != 0; _cf = false; _of = false;
    }

    /// <summary>산술 한 가지(0 add · 1 or · 4 and · 5 sub · 6 xor · 7 cmp) — 결과(cmp 면 쓰지 않을 값).</summary>
    private uint Alu(int op, uint a, uint b, int size)
    {
        uint m = Mask(size), s = SignBit(size);
        a &= m; b &= m;
        uint r;
        switch (op)
        {
            case 0:
                r = (a + b) & m;
                _cf = (ulong)a + b > m;
                _of = ((a ^ r) & (b ^ r) & s) != 0;
                _zf = r == 0; _sf = (r & s) != 0;
                return r;
            case 5 or 7:
                r = (a - b) & m;
                _cf = a < b;
                _of = ((a ^ b) & (a ^ r) & s) != 0;
                _zf = r == 0; _sf = (r & s) != 0;
                return r;
            case 1: r = a | b; Logic(r, size); return r;
            case 4: r = a & b; Logic(r, size); return r;
            case 6: r = a ^ b; Logic(r, size); return r;
            default: throw new X86Fault($"alu {op}");
        }
    }

    private bool Cond(int cc) => cc switch
    {
        0x0 => _of, 0x1 => !_of,
        0x2 => _cf, 0x3 => !_cf,
        0x4 => _zf, 0x5 => !_zf,
        0x6 => _cf || _zf, 0x7 => !_cf && !_zf,
        0x8 => _sf, 0x9 => !_sf,
        0xC => _sf != _of, 0xD => _sf == _of,
        0xE => _zf || _sf != _of, 0xF => !_zf && _sf == _of,
        _ => throw new X86Fault($"jcc {cc:X}"),
    };

    private uint Shift(int op, uint v, int count, int size)
    {
        count &= 31;
        if (count == 0) return v & Mask(size);
        uint m = Mask(size), s = SignBit(size);
        v &= m;
        uint r;
        switch (op)
        {
            case 4 or 6: r = (uint)((ulong)v << count) & m; _cf = ((v >> (Bits(size) - count)) & 1) != 0; break;   // shl
            case 5: r = v >> count; _cf = ((v >> (count - 1)) & 1) != 0; break;                                   // shr
            case 7: r = (uint)(Sx(v, size) >> count) & m; _cf = ((Sx(v, size) >> (count - 1)) & 1) != 0; break;  // sar
            default: throw new X86Fault($"shift {op}");
        }
        _zf = r == 0; _sf = (r & s) != 0; _of = false;
        return r;
    }

    private static int Bits(int size) => size * 8;

    /// <summary>start 에서 돌려 eip 가 exit 들 가운데 하나에 닿으면 그 주소. 걸음 수가 넘거나 모르는 명령이면 <see cref="X86Fault"/>.</summary>
    public uint Run(uint start, IReadOnlyCollection<uint> exits, int maxSteps = 20000)
    {
        Eip = start;
        for (int step = 0; step < maxSteps; step++)
        {
            if (exits.Contains(Eip)) return Eip;
            Step();
        }
        throw new X86Fault("걸음 수 넘침");
    }

    private void Push(uint v) { R[4] -= 4; Write(R[4], v, 4); }
    private uint Pop() { uint v = Read(R[4], 4); R[4] += 4; return v; }

    private void Step()
    {
        int size = 4;
        uint at = Eip;
        byte op = Fetch();
        if (op == 0x66) { size = 2; op = Fetch(); }
        switch (op)
        {
            case <= 0x3B when (op & 7) < 4 && ((op >> 3) & 7) is not (2 or 3):
            {
                int alu = (op >> 3) & 7, form = op & 7;
                int sz = (form & 1) == 0 ? 1 : size;
                var (reg, rm) = ModRm();
                uint a, b;
                if (form < 2) { a = Get(rm, sz); b = GetReg(reg, sz); }
                else { a = GetReg(reg, sz); b = Get(rm, sz); }
                uint r = Alu(alu, a, b, sz);
                if (alu != 7)
                {
                    if (form < 2) Set(rm, sz, r); else SetReg(reg, sz, r);
                }
                break;
            }
            case 0x04 or 0x05 or 0x0C or 0x0D or 0x24 or 0x25 or 0x2C or 0x2D or 0x34 or 0x35 or 0x3C or 0x3D:
            {
                int alu = (op >> 3) & 7, sz = (op & 1) == 0 ? 1 : size;
                uint imm = sz == 1 ? Fetch() : sz == 2 ? Fetch16() : Fetch32();
                uint r = Alu(alu, GetReg(0, sz), imm, sz);
                if (alu != 7) SetReg(0, sz, r);
                break;
            }
            case >= 0x40 and <= 0x47:
            {
                bool cf = _cf;
                SetReg(op - 0x40, size, Alu(0, GetReg(op - 0x40, size), 1, size));
                _cf = cf;
                break;
            }
            case >= 0x48 and <= 0x4F:
            {
                bool cf = _cf;
                SetReg(op - 0x48, size, Alu(5, GetReg(op - 0x48, size), 1, size));
                _cf = cf;
                break;
            }
            case >= 0x50 and <= 0x57: Push(R[op - 0x50]); break;
            case >= 0x58 and <= 0x5F: R[op - 0x58] = Pop(); break;
            case 0x68: Push(Fetch32()); break;
            case 0x6A: Push((uint)(sbyte)Fetch()); break;
            case 0x69 or 0x6B:
            {
                var (reg, rm) = ModRm();
                int imm = op == 0x6B ? (sbyte)Fetch() : size == 2 ? (short)Fetch16() : (int)Fetch32();
                long p = (long)Sx(Get(rm, size), size) * imm;
                uint r = (uint)p & Mask(size);
                _cf = _of = p != Sx(r, size);
                SetReg(reg, size, r);
                break;
            }
            case >= 0x70 and <= 0x7F:
            {
                int rel = (sbyte)Fetch();
                if (Cond(op - 0x70)) Eip = (uint)(Eip + rel);
                break;
            }
            case 0x80 or 0x81 or 0x83:
            {
                int sz = op == 0x80 ? 1 : size;
                var (reg, rm) = ModRm();
                uint imm = op == 0x81 ? (sz == 2 ? Fetch16() : Fetch32()) : (uint)(sbyte)Fetch();
                if (reg is 2 or 3) throw new X86Fault("adc/sbb");
                uint r = Alu(reg, Get(rm, sz), imm, sz);
                if (reg != 7) Set(rm, sz, r);
                break;
            }
            case 0x84 or 0x85:
            {
                int sz = op == 0x84 ? 1 : size;
                var (reg, rm) = ModRm();
                Logic(Get(rm, sz) & GetReg(reg, sz), sz);
                break;
            }
            case 0x88 or 0x89:
            {
                int sz = op == 0x88 ? 1 : size;
                var (reg, rm) = ModRm();
                Set(rm, sz, GetReg(reg, sz));
                break;
            }
            case 0x8A or 0x8B:
            {
                int sz = op == 0x8A ? 1 : size;
                var (reg, rm) = ModRm();
                SetReg(reg, sz, Get(rm, sz));
                break;
            }
            case 0x8D:
            {
                var (reg, rm) = ModRm();
                if (rm.IsReg) throw new X86Fault("lea reg");
                SetReg(reg, size, rm.Addr);
                break;
            }
            case 0x90: break;
            case 0x99: R[2] = (R[0] & 0x80000000) != 0 ? 0xFFFFFFFF : 0; break;
            case 0xA1: R[0] = Read(Fetch32(), size); break;
            case 0xA3: Write(Fetch32(), R[0], size); break;
            case >= 0xB0 and <= 0xB7: SetReg(op - 0xB0, 1, Fetch()); break;
            case >= 0xB8 and <= 0xBF: SetReg(op - 0xB8, size, size == 2 ? Fetch16() : Fetch32()); break;
            case 0xC1 or 0xD1 or 0xD3:
            {
                var (reg, rm) = ModRm();
                int count = op == 0xC1 ? Fetch() : op == 0xD1 ? 1 : (int)(R[1] & 0xFF);
                Set(rm, size, Shift(reg, Get(rm, size), count, size));
                break;
            }
            case 0xC6 or 0xC7:
            {
                int sz = op == 0xC6 ? 1 : size;
                var (reg, rm) = ModRm();
                if (reg != 0) throw new X86Fault("c6/c7");
                uint imm = sz == 1 ? Fetch() : sz == 2 ? Fetch16() : Fetch32();
                Set(rm, sz, imm);
                break;
            }
            case 0xE8:
            {
                uint target = (uint)(Eip + 4 + (int)Fetch32());
                if (target != RandVa || Rand == null) throw new X86Fault($"부르기 {target:X} ({at:X})");
                R[0] = Rand();
                break;
            }
            case 0xE9: { int rel = (int)Fetch32(); Eip = (uint)(Eip + rel); break; }
            case 0xEB: { int rel = (sbyte)Fetch(); Eip = (uint)(Eip + rel); break; }
            case 0xF6 or 0xF7:
            {
                int sz = op == 0xF6 ? 1 : size;
                var (reg, rm) = ModRm();
                switch (reg)
                {
                    case 0: Logic(Get(rm, sz) & (sz == 1 ? Fetch() : sz == 2 ? Fetch16() : Fetch32()), sz); break;
                    case 2: Set(rm, sz, ~Get(rm, sz)); break;
                    case 3: { uint v = Get(rm, sz); uint r = Alu(5, 0, v, sz); _cf = v != 0; Set(rm, sz, r); break; }
                    case 7 when sz == 4:
                    {
                        int d = (int)Get(rm, 4);
                        if (d == 0) throw new X86Fault("0 으로 나눔");
                        long n = (long)((ulong)R[2] << 32 | R[0]);
                        long q = n / d, m = n % d;
                        if (q is > int.MaxValue or < int.MinValue) throw new X86Fault("idiv 넘침");
                        (R[0], R[2]) = ((uint)(int)q, (uint)(int)m);
                        break;
                    }
                    default: throw new X86Fault($"f7 /{reg}");
                }
                break;
            }
            case 0x0F:
            {
                byte op2 = Fetch();
                switch (op2)
                {
                    case >= 0x80 and <= 0x8F:
                    {
                        int rel = (int)Fetch32();
                        if (Cond(op2 - 0x80)) Eip = (uint)(Eip + rel);
                        break;
                    }
                    case 0xAF:
                    {
                        var (reg, rm) = ModRm();
                        long p = (long)Sx(GetReg(reg, size), size) * Sx(Get(rm, size), size);
                        uint r = (uint)p & Mask(size);
                        _cf = _of = p != Sx(r, size);
                        SetReg(reg, size, r);
                        break;
                    }
                    case 0xB6 or 0xB7 or 0xBE or 0xBF:
                    {
                        int src = (op2 & 1) == 0 ? 1 : 2;
                        var (reg, rm) = ModRm();
                        uint v = Get(rm, src);
                        SetReg(reg, size, op2 >= 0xBE ? (uint)Sx(v, src) : v);
                        break;
                    }
                    default: throw new X86Fault($"0F {op2:X2} ({at:X})");
                }
                break;
            }
            default: throw new X86Fault($"명령 {op:X2} ({at:X})");
        }
    }
}

public sealed class X86Fault(string message) : Exception(message);
