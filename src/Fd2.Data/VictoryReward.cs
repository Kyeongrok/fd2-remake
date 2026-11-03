using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 승리 뒤 보상 물건 하나 — 고친 EXE(곡우 · 파워 인핸스드 …)가 승리 뒤 함수(표 0x51DE9, 장면 Scene)의 한 명령을 jmp 로 바꿔 붙인 코드:
/// <code>
/// cmp dword [차례 53BEFh], TurnLimit ; jg(또는 jle) 건너뜀  — 차례 ≤ TurnLimit(jle 꼴이면 차례 &gt; TurnLimit) 일 때만
/// (mov eax, [53A45h] ; cmp byte [eax + CondField], CondValue ; jne/jl 건너뜀)   — 배열 0 번 사람 조건(있으면)
/// push Item ; call sub_1C220 ; add esp, 4  (되풀이)      — 우리 편(편 2) 배열 차례로 가방 빈칸이 있는 첫 사람에게
/// </code>
/// 물건 번호 · 차례 한도 · 조건 값은 그 명령의 즉칫값을 읽은 것이라 다른 개조판도 같은 꼴이면 읽힌다.
/// </summary>
/// <param name="Scene">장면 번호(0 = 1장).</param>
/// <param name="TurnLimit">차례 한도(부호 있는 비교).</param>
/// <param name="TurnAbove">0 = 차례 ≤ 한도일 때(jg 로 건너뜀, 빨리 이기면), 1 = 차례 &gt; 한도일 때(jle 로 건너뜀 — 6장 · 13장 · 23장).</param>
/// <param name="CondField">배열 0 번 사람 칸의 자리(20h 직업 · 21h 레벨), −1 조건 없음.</param>
/// <param name="CondOp">1 = 같음(jne), 2 = 이상(jl), 0 없음.</param>
/// <param name="Item1">물건 번호(−1 없음) — 차례대로 준다.</param>
/// <param name="Late">1 이면 승리 뒤 함수의 첫 대사 뒤에 붙은 것(리메이크는 승리 대본 끝에), 0 이면 대사 앞(대본 앞에).</param>
public sealed record VictoryReward(int Scene, int TurnLimit, int TurnAbove, int CondField, int CondOp, int CondValue, int Item1, int Item2, int Item3, int Item4, int Late)
{
    public IEnumerable<int> Items() => new[] { Item1, Item2, Item3, Item4 }.Where(i => i >= 0);

    /// <summary>이 보상의 조건이 맞나 — turn 지금 차례, lead 배열 0 번 사람(없으면 조건이 있을 때 거짓).</summary>
    public bool Applies(int turn, Unit? lead)
    {
        if (TurnAbove == 0 ? turn > TurnLimit : turn <= TurnLimit) return false;
        if (CondOp == 0 || CondField < 0) return true;
        if (lead == null) return false;
        int v = CondField switch { 0x20 => lead.Class, 0x21 => lead.Level, 0x1F => lead.Race, 0x07 => lead.FormId, 0x08 => lead.NameId, _ => int.MinValue };
        if (v == int.MinValue) v = HeroRecord.From(lead)[CondField];
        v &= 0xFF;   // 바이트 비교(cmp byte)
        return CondOp == 1 ? v == (CondValue & 0xFF) : (sbyte)v >= (sbyte)CondValue;
    }

    private const int CodeOffset = 0x26014;
    private const uint TableVa = 0x51DE9, SceneCount = 30, Code1 = 0x10000;
    private const uint StackCheck = 0x3702F, GiveItem = 0x1C220, Talk = 0x15F84;

    /// <summary>승리 뒤 함수 표의 함수마다 몸통(다음 함수 앞, 많아야 0x800)에서 보상 코드로 가는 jmp 를 찾는다.</summary>
    internal static IReadOnlyList<VictoryReward> ReadAll(byte[] exe)
    {
        var list = new List<VictoryReward>();
        int t = X86Cpu.FileOffset(TableVa);
        if (t < 0 || t + 4 * (int)SceneCount > exe.Length) return list;
        var entries = Enumerable.Range(0, (int)SceneCount).Select(k => BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(t + 4 * k)) + Code1).ToArray();
        var sorted = entries.Distinct().OrderBy(v => v).ToArray();
        for (int scene = 0; scene < entries.Length; scene++)
        {
            uint entry = entries[scene];
            uint next = sorted.FirstOrDefault(v => v > entry);
            uint end = next == 0 ? entry + 0x800 : Math.Min(next, entry + 0x800);
            for (uint va = entry; va + 5 <= end; va++)
            {
                if (Byte(exe, va) != 0xE9) continue;
                uint target = Rel(exe, va + 1, va + 5);
                if (Parse(exe, target) is not { } r) continue;
                bool late = FindCall(exe, entry, va, Talk);
                var items = r.Items.Concat(Enumerable.Repeat(-1, 4)).Take(4).ToArray();
                list.Add(new VictoryReward(scene, r.TurnLimit, r.TurnAbove, r.CondField, r.CondOp, r.CondValue, items[0], items[1], items[2], items[3], late ? 1 : 0));
            }
        }
        return list;
    }

    private static byte Byte(byte[] exe, uint va) => (int)va + CodeOffset is var o && o >= 0 && o < exe.Length ? exe[o] : (byte)0;
    private static uint Rel(byte[] exe, uint at, uint next) =>
        (int)at + CodeOffset + 4 <= exe.Length ? (uint)(next + BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan((int)at + CodeOffset))) : 0;

    /// <summary>[from, to) 안에 call target 이 있나(바이트로 찾는다).</summary>
    private static bool FindCall(byte[] exe, uint from, uint to, uint target)
    {
        for (uint va = from; va + 5 <= to; va++)
            if (Byte(exe, va) == 0xE8 && Rel(exe, va + 1, va + 5) == target) return true;
        return false;
    }

    private static (int TurnLimit, int TurnAbove, int CondField, int CondOp, int CondValue, List<int> Items)? Parse(byte[] exe, uint p)
    {
        bool At(uint va, params byte[] b)
        {
            for (int i = 0; i < b.Length; i++) if (Byte(exe, va + (uint)i) != b[i]) return false;
            return true;
        }
        if (Byte(exe, p) == 0xE8 && Rel(exe, p + 1, p + 5) == StackCheck) p += 5;   // 바꾼 자리의 원래 명령(스택 검사)을 먼저 하는 꼴
        // cmp dword [53BEFh], imm8 ; jg rel8(차례 ≤ 한도에 준다) · jle rel8(차례 > 한도에 준다)
        if (!At(p, 0x83, 0x3D, 0xEF, 0x3B, 0x00, 0x00) || Byte(exe, p + 7) is not (0x7F or 0x7E)) return null;
        int turnLimit = (sbyte)Byte(exe, p + 6), turnAbove = Byte(exe, p + 7) == 0x7E ? 1 : 0;
        uint skip = (uint)(p + 9 + (sbyte)Byte(exe, p + 8));
        p += 9;
        int field = -1, op = 0, value = 0;
        // mov eax, [53A45h] ; cmp byte [eax + d8], imm8 ; jne/jl rel8
        if (At(p, 0xA1, 0x45, 0x3A, 0x00, 0x00, 0x80, 0x78))
        {
            byte j = Byte(exe, p + 9);
            if (j is not (0x75 or 0x7C) || (uint)(p + 11 + (sbyte)Byte(exe, p + 10)) != skip) return null;
            (field, value, op) = (Byte(exe, p + 7), (sbyte)Byte(exe, p + 8), j == 0x75 ? 1 : 2);
            p += 11;
        }
        var items = new List<int>();
        while (true)
        {
            int item, len;
            if (Byte(exe, p) == 0x6A) (item, len) = (Byte(exe, p + 1), 2);
            else if (Byte(exe, p) == 0x68) (item, len) = ((int)BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan((int)p + CodeOffset + 1)), 5);
            else break;
            uint q = p + (uint)len;
            // push 다음이 sub_1C220 부르기가 아니면 보상 줄이 끝난 것(건너뛸 자리의 다른 명령 — 23장은 대사 인자 push)
            if (Byte(exe, q) != 0xE8 || Rel(exe, q + 1, q + 5) != GiveItem || !At(q + 5, 0x83, 0xC4, 0x04)) break;
            items.Add(item & 0xFF);   // sub_1C220 은 char 로 받는다
            p = q + 8;
        }
        if (items.Count is 0 or > 4 || p != skip) return null;
        return (turnLimit, turnAbove, field, op, value, items);
    }
}
