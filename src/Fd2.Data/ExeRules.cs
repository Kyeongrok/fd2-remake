using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 버전마다 FD2.EXE 의 「코드 안 숫자」로 다른 규칙 — 표(<see cref="GameTables"/>)가 아니라 명령의 즉칫값이라, 그 자리의 명령 바이트가
/// 원본과 같은 꼴일 때만 값을 읽고(다르면 원본 값), 버전 이름으로 가르지 않는다. 코드 파일 자리 = VA + 0x26014(오브젝트 1).
/// 원본 · 곡우 슈퍼 에디션 · 파워 인핸스드(3.55) EXE 를 비교해 찾은 곳들.
/// </summary>
public sealed class ExeRules
{
    /// <summary>레벨업에 드는 경험(sub_1E292 `sub [esp+4], n` · `cmp [esp+4], n`) — 원본 · 파워 100, 곡우 60.</summary>
    public int LevelUpExp { get; private init; } = 100;

    /// <summary>
    /// 경험 나눔 레벨의 전직 덧값(sub_1ECC7 `add byte [esp+34h], n` — 직업 9~24 이거나 이름 28) — 원본 30, 곡우 · 파워 50.
    /// </summary>
    public int AttackExpPromoted { get; private init; } = 30;

    /// <summary>마법 경험을 나누는 쓰는 사람 레벨의 덧값(sub_18D8C `add ebx, n` — 직업 &gt; 8) — 원본 30, 곡우 · 파워 50.</summary>
    public int SpellExpPromoted { get; private init; } = 30;

    /// <summary>마법 경험의 대상 레벨′ 덧값(sub_2218A · sub_22721 … `add …, n` — 직업 9~24) — 원본 30, 곡우 · 파워 50.</summary>
    public int SpellTargetPromoted { get; private init; } = 30;

    /// <summary>회복 마법 경험 <c>채운 양 × HealMul × 대상 Lv′ / 최대 HP</c>(sub_1C916) — 원본 40 · Lv′ 덧값 30, 곡우 18 · 35.</summary>
    public int HealMul { get; private init; } = 40;
    public int HealPromoted { get; private init; } = 30;

    /// <summary>올리기 마법 경험 <c>RaiseMul × Lv′</c>(sub_22721 — 22866 · 22997 도 같다고 본다) — 원본 2.</summary>
    public int RaiseMul { get; private init; } = 2;

    /// <summary>풀기 마법 경험 <c>CureMul × Lv′</c>(sub_22AF6) — 원본 4 · 덧값 30, 곡우 2 · 50.</summary>
    public int CureMul { get; private init; } = 4;
    public int CurePromoted { get; private init; } = 30;

    /// <summary>행동술 경험 <c>ActMul × Lv′</c>(sub_22C04) — 원본 8 · 덧값 30, 곡우 5 · 50.</summary>
    public int ActMul { get; private init; } = 8;
    public int ActPromoted { get; private init; } = 30;

    /// <summary>걸기 마법 경험 <c>HexMul × 대상 Lv</c>(sub_22D1B) — 원본 8, 곡우 2.</summary>
    public int HexMul { get; private init; } = 8;

    /// <summary>전송술 경험 <c>TeleportMul × Lv′</c>(sub_2218A) — 원본 10 · 덧값 30, 곡우 10 · 50.</summary>
    public int TeleportMul { get; private init; } = 10;
    public int TeleportPromoted { get; private init; } = 30;

    /// <summary>
    /// MP 절반만 드는 사람의 이름 번호(기록 +8) — 곡우는 MP 빼기(sub_1CA89) 끝을 jmp 로 바꿔 <c>cmp byte [eax+8], 11 / jne / sar edx, 1 / add ebx, edx</c>
    /// (이름 11 이면 낸 MP 의 절반을 돌려준다). 없으면 −1(원본 · 파워).
    /// </summary>
    public int HalfMpId { get; private init; } = -1;

    /// <summary>걸기 마법 명중 %(sub_22D1B `cmp edx, n` 0x22DCB — 난수 % 100 &lt; n 이면 걸림) — 원본 50, 곡우 · 파워 70.</summary>
    public int HexHit { get; private init; } = 50;

    /// <summary>적 레벨 올리기(sub_10C50 몬스터 길에 붙인 코드) — 없으면 null(원본: 장면 유닛 표 레벨 그대로).</summary>
    public EnemyLevelRule? EnemyLevel { get; private init; }

    /// <summary>
    /// 최대 레벨 자리의 EXE 값(sub_1E292, −1 = 견주지 않음) — 지금 리메이크는 쓰지 않는다(110 / 99 그대로, 사용자 요청).
    /// Gain* 은 경험을 받기 전 「이 레벨이면 안 받는다」, Reset* 은 레벨이 오른 뒤 「이 레벨이면 남은 경험 0」. Kyle = 꼴 30 · 31.
    /// 원본(런처) 99 · 40 / 99 · 30, 곡우 −1 · −1 / 110 · −1, 파워 110 · 50 / 110 · 50.
    /// </summary>
    public int MaxLevelGainKyle { get; private init; } = 99;
    public int MaxLevelGainOther { get; private init; } = 40;
    public int MaxLevelResetKyle { get; private init; } = 99;
    public int MaxLevelResetOther { get; private init; } = 30;

    /// <summary>코드 토막을 돌릴 EXE 바이트(<see cref="X86Cpu"/>) — 원본 값만 쓸 때는 null.</summary>
    public byte[]? Exe { get; private init; }

    /// <summary>
    /// 전투 화면 치기(sub_2F7B6 의 0x2F925 ~ 0x2FA4D: 필살 · 독 · 명중 · 피해)가 원본과 다른 코드인가 — 참이면 그 토막을 <see cref="X86Cpu"/> 로 돌린다.
    /// </summary>
    public bool StrikeFromCode { get; private init; }

    /// <summary>무기 특수(0~15)마다 첫 치기 뒤 더 치는 횟수(sub_2F7B6 결과 +10h → sub_2EBE1) — 원본 특수 3 만 1, 곡우 · 파워 특수 5 는 2.</summary>
    public IReadOnlyList<int> ExtraStrikes { get; private init; } = Enumerable.Range(0, 16).Select(s => s == 3 ? 1 : 0).ToArray();

    /// <summary>공격↑ · 방어↑ 셈(sub_1B750)이 원본과 다른 코드면 그 꼴 — null 이면 원본(× 1.15).</summary>
    public BuffCode? Buff { get; private init; }

    /// <summary>회복량(sub_1C916)이 원본과 다른 코드인가 — 참이면 0x1C947 ~ 0x1C990 를 돌린다.</summary>
    public bool HealFromCode { get; private init; }

    /// <summary>
    /// 승리 뒤 보상 물건(<see cref="VictoryReward"/>) — 승리 뒤 함수 표(0x51DE9)의 함수들 안에서 붙인 코드를 찾아 읽는다. 원본은 없다.
    /// </summary>
    public IReadOnlyList<VictoryReward> VictoryRewards { get; private init; } = [];

    public static readonly ExeRules Original = new();

    private const int CodeOffset = 0x26014;

    public static ExeRules Read(byte[] exe)
    {
        bool At(int va, ReadOnlySpan<byte> pattern, int wild = -1)
        {
            int o = va + CodeOffset;
            if (o < 0 || o + pattern.Length > exe.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
                if (i != wild && exe[o + i] != pattern[i]) return false;
            return true;
        }
        byte Imm(int va) => exe[va + CodeOffset];
        int? Imm8(int va, ReadOnlySpan<byte> op) => At(va, op) ? Imm(va + op.Length) : null;

        // 곱하기 한 명령 — shl r, n(C1 Ex n) = 2ⁿ · imul r, r, n(6B xx n) = n · add eax, eax(01 C0) = 2
        int? Mul(int va)
        {
            int o = va + CodeOffset;
            if (o < 0 || o + 3 > exe.Length) return null;
            if (exe[o] == 0xC1 && (exe[o + 1] & 0xF8) == 0xE0 && exe[o + 2] < 8) return 1 << exe[o + 2];
            if (exe[o] == 0x6B && (exe[o + 1] & 0xC0) == 0xC0) return exe[o + 2];
            if (exe[o] == 0x01 && exe[o + 1] == 0xC0) return 2;
            return null;
        }
        // 회복: mov edx, eax; shl edx, a; add edx, eax; shl edx, b → ((1 << a) + 1) << b
        int? heal = At(0x1C9BA, [0x89, 0xC2, 0xC1, 0xE2, 0, 0x01, 0xC2, 0xC1, 0xE2], 4) && Imm(0x1C9BE) < 8 && Imm(0x1C9C3) < 8
            ? ((1 << Imm(0x1C9BE)) + 1) << Imm(0x1C9C3) : null;

        // MP 절반(곡우) — 0x1CAC1 의 jmp 가 가리키는 곳이 「cmp byte [eax+8], n / jne +4 / sar edx, 1 / add ebx, edx」 꼴일 때만
        int halfMp = -1;
        if (At(0x1CABF, [0x29, 0xD3, 0xE9]))
        {
            int to = 0x1CAC6 + BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x1CAC2 + CodeOffset));
            if (At(to, [0x80, 0x78, 0x08, 0, 0x75, 0x04, 0xD1, 0xFA, 0x01, 0xD3], 3)) halfMp = Imm(to + 3);
        }

        var d = Original;
        int? sub = Imm8(0x1E3EE, [0x83, 0x6C, 0x24, 0x04]), cmp = Imm8(0x1E414, [0x83, 0x7C, 0x24, 0x04]);
        return new ExeRules
        {
            HealMul = heal ?? d.HealMul,
            HealPromoted = Imm8(0x1C9AE, [0x83, 0xC0]) ?? d.HealPromoted,
            RaiseMul = At(0x2280A, [0x8B, 0x44, 0x24, 0x08]) ? Mul(0x2280E) ?? d.RaiseMul : d.RaiseMul,
            CureMul = Mul(0x22BAC) ?? d.CureMul,
            CurePromoted = Imm8(0x22B76, [0x83, 0xC5]) ?? d.CurePromoted,
            ActMul = Mul(0x22C98) ?? d.ActMul,
            ActPromoted = Imm8(0x22C95, [0x83, 0xC2]) ?? d.ActPromoted,
            HexMul = Mul(0x22E08) ?? d.HexMul,
            TeleportMul = Mul(0x221DD) ?? d.TeleportMul,
            TeleportPromoted = Imm8(0x221DA, [0x83, 0xC2]) ?? d.TeleportPromoted,
            LevelUpExp = sub is > 0 && sub == cmp ? sub.Value : d.LevelUpExp,
            AttackExpPromoted = Imm8(0x1F007, [0x80, 0x44, 0x24, 0x34]) ?? d.AttackExpPromoted,
            SpellExpPromoted = Imm8(0x19036, [0x83, 0xC3]) ?? d.SpellExpPromoted,
            SpellTargetPromoted = Imm8(0x227A9, [0x83, 0x44, 0x24, 0x08]) ?? d.SpellTargetPromoted,
            EnemyLevel = EnemyLevelRule.Read(exe, CodeOffset),
            MaxLevelGainKyle = (sbyte?)Imm8(0x1E2EA, [0x83, 0xF8]) ?? d.MaxLevelGainKyle,
            MaxLevelGainOther = (sbyte?)Imm8(0x1E2EF, [0x83, 0xF8]) ?? d.MaxLevelGainOther,
            MaxLevelResetKyle = (sbyte?)Imm8(0x1E403, [0x83, 0xF8]) ?? d.MaxLevelResetKyle,
            MaxLevelResetOther = (sbyte?)Imm8(0x1E4F6, [0x83, 0xF8]) ?? d.MaxLevelResetOther,
            HalfMpId = halfMp,
            HexHit = At(0x22DC9, [0xF7, 0xFB, 0x83, 0xFA]) ? Imm(0x22DCD) : d.HexHit,
            Exe = exe,
            StrikeFromCode = StrikeBlockOk(exe) && !StrikeIsOriginal(exe),
            ExtraStrikes = ReadExtraStrikes(exe) ?? d.ExtraStrikes,
            Buff = BuffCode.Read(exe),
            VictoryRewards = VictoryReward.ReadAll(exe),
            HealFromCode = X86Cpu.CodeAt(exe, 0x1C947, "8B 54 24 1C") && X86Cpu.CodeAt(exe, 0x1C990, "0F B7 46 40")
                           && !X86Cpu.CodeAt(exe, 0x1C94B, "89 D0 C1 E0 03 01 C2 BB 0A 00 00 00 89 D0 C1 FA 1F F7 FB 89 C5 E8"),
        };
    }

    /// <summary>치기 토막의 들머리 · 끝(세 버전 같음) — 이 꼴이 아니면 토막을 돌리지 않는다.</summary>
    private static bool StrikeBlockOk(byte[] exe) =>
        X86Cpu.CodeAt(exe, 0x2F925, "8B 44 24 10 0F B6 80 A8 24 00 00") && X86Cpu.CodeAt(exe, 0x2FA4D, "0F B6 45 06 83 F8 02");

    /// <summary>원본 치기 토막인가 — 고친 버전들이 jmp 로 바꾸는 네 자리(독 · 특수 3 · 빗나감 · 피해 식)가 원본 그대로면 참.</summary>
    private static bool StrikeIsOriginal(byte[] exe) =>
        X86Cpu.CodeAt(exe, 0x2F948, "E8 96 F2 01 00") && X86Cpu.CodeAt(exe, 0x2F97F, "83 FB 03 75 07 C7 46 10 01")
        && X86Cpu.CodeAt(exe, 0x2F9A6, "0F 8D A1 00 00 00")
        && X86Cpu.CodeAt(exe, 0x2F9E1, "8B 54 24 24 2B 54 24 2C 89 D0 C1 E0 03 01 C2 BB 0A 00 00 00");

    /// <summary>특수 고르기(0x2F934 ~ 0x2F98B)를 특수마다 돌려 결과 +10h 를 읽는다(난수는 0). 못 돌리면 null.</summary>
    private static int[]? ReadExtraStrikes(byte[] exe)
    {
        if (!X86Cpu.CodeAt(exe, 0x2F934, "83 FB 04") || !X86Cpu.CodeAt(exe, 0x2F98B, "E8")) return null;
        var list = new int[16];
        for (int s = 0; s < 16; s++)
        {
            var cpu = new X86Cpu(exe) { Rand = () => 0 };
            byte[] att = new byte[80], def = new byte[80], stack = new byte[0x200], res = new byte[0x18];
            cpu.Map(0x1000000, att); cpu.Map(0x2000000, def); cpu.Map(0x3000000, stack); cpu.Map(0x4000000, res);
            (cpu.R[4], cpu.R[5], cpu.R[7], cpu.R[6], cpu.R[3]) = (0x3000100, 0x1000000, 0x2000000, 0x4000000, (uint)s);
            try { cpu.Run(0x2F934, [0x2F98B], 500); }
            catch (X86Fault) { return null; }
            list[s] = (int)cpu.Read(0x4000010, 4);
        }
        return list;
    }
}

/// <summary>
/// 공격↑ · 방어↑ 셈(sub_1B750)을 고친 꼴 — 원본은 <c>값 × 1.15</c>(fild · fmul [5018Dh] · fistp)이고, 고친 버전은
/// <c>x = 값 × 배율</c>(배율은 EXE 자료 8바이트)을 셈한 뒤 붙인 코드(정수 명령)로 마무리한다: 곡우 · 파워는 <c>(직업 23 · 15 면 x × 3 / 2) + 값 + 1</c>.
/// Tail* 은 fistp 다음 코드 자리 — <see cref="X86Cpu"/> 가 eax · edx(값) · [esp+8 · +0Ch](x) 로 끝(0x1B802 · 0x1B81D)까지 돌린다.
/// </summary>
public sealed record BuffCode(double AtkMul, uint AtkTail, double DefMul, uint DefTail)
{
    internal static BuffCode? Read(byte[] exe)
    {
        (double, uint)? One(uint va, string slot)
        {
            // mov edx, [esp+n]; fild [esp+n]; fmul qword [d32]; jmp cave → cave: call __CHP; fistp [esp+n]; (꼬리)
            if (!X86Cpu.CodeAt(exe, va, $"8B 54 24 {slot} DB 44 24 {slot} DC 0D ?? ?? ?? ?? E9")) return null;
            uint d = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan((int)va + 0x26014 + 10));
            uint cave = (uint)(va + 19 + BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan((int)va + 0x26014 + 15)));
            if (!X86Cpu.CodeAt(exe, cave, $"E8 ?? ?? ?? ?? DB 5C 24 {slot}")) return null;
            int f = X86Cpu.FileOffset(0x50000 + d);
            if (f < 0 || f + 8 > exe.Length) return null;
            double mul = BitConverter.ToDouble(exe, f);
            return double.IsFinite(mul) ? (mul, cave + 9) : null;
        }
        var a = One(0x1B7EF, "08");
        var b = One(0x1B80A, "0C");
        return a is { } x && b is { } y ? new BuffCode(x.Item1, x.Item2, y.Item1, y.Item2) : null;
    }
}

/// <summary>
/// 적 레벨 올리기 — 몬스터(꼴 68~)를 세울 때 우리 편 자리(배열 0 ~ P−1)의 힘으로 장면 유닛 표 레벨에 더한다. 원본에는 없고,
/// sub_10C50 의 `movzx eax, [esi+1]; sub eax, 44h`(VA 0x10DA4)를 jmp 로 바꾼 버전에만 있다. 세 토막을 따라가며 바이트 꼴을 맞춰 보고 상수를 읽는다.
/// <code>
/// 1단(곡우 꼴): 점수 = 18 × (역량 + 2 × 속도) + 내력 + 최대 HP, 평균 × 29 / 32 보다 큰 사람 k 명의 합 S →
///              v = (S − 56 × k × (P − k)) / 128, 사람 수 k
/// 1단(파워 꼴): v = (1 + Σ (역량 + 4 × 속도)) / 16, 사람 수 P
/// 2단: a = v / 사람 수, n = 장면 + 1 이 30(상한) 넘으면 그만, a ≥ 50 이면 a −= (a − 50) × 25 / 64, a −= 3n(음수면 그만)
/// 3단: 유닛 표 레벨 ≤ 2n 이면 a = (a + 1) × 5 / 8. 레벨 += a
/// </code>
/// </summary>
public sealed record EnemyLevelRule(bool Strongest, int Mul, int Thr, int Shift, int Pair, int Div, int SceneCap, int Knee, int KneeMul, int KneeShift,
                                    int LowMul, int LowShift)
{
    /// <summary>party: 배열 0 ~ P−1 자리의 (역량, 내력, 속도, 최대 HP) — 빈 자리는 0.</summary>
    public int Bonus(int level, int scene, IReadOnlyList<(int Str, int Def, int Spd, int MaxHp)> party)
    {
        int n = party.Count;
        if (n == 0) return 0;
        uint v;
        int count;
        if (Strongest)
        {
            var s = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                var p = party[i];
                // movzx ebx, [+3E]; shl ebx, 1; add bx, [+37] — 아래 16비트만 더한다
                int b = (p.Spd & 0xFFFF) << 1;
                b = (b & ~0xFFFF) | ((b + p.Str) & 0xFFFF);
                int c = ((p.Def & 0xFFFF) + p.MaxHp) & 0xFFFF;
                s[i] = unchecked(b * Mul + c);
                total = unchecked(total + s[i]);
            }
            int thr = (int)((uint)unchecked(total / n * Thr) >> Shift);
            int sum = 0, k = 0;
            foreach (int x in s) if (thr < x) { sum = unchecked(sum + x); k++; }
            v = (uint)unchecked(sum - (n - k) * k * Pair) >> Div;
            count = k;
        }
        else
        {
            int total = 1;
            foreach (var p in party) total = unchecked(total + (p.Str & 0xFFFF) + ((p.Spd & 0xFFFF) << Mul));
            v = (uint)total >> Div;
            count = n;
        }
        if (count == 0) return 0;
        uint a = (uint)((int)v / count);
        int sc = scene + 1;
        if (sc > SceneCap) return 0;
        if (a >= (uint)Knee) a -= (uint)((a - (uint)Knee) * (uint)KneeMul) >> KneeShift;
        if (a < (uint)(3 * sc)) return 0;
        a -= (uint)(3 * sc);
        if (level <= 2 * sc) a = (a + 1) * (uint)LowMul >> LowShift;
        return (int)a;
    }

    internal static EnemyLevelRule? Read(byte[] exe, int off)
    {
        // VA 0x10DA4: jmp rel32 + nop nop (원본은 0F B6 46 01 83 E8 44)
        int Jmp(int va) => exe[va + off] == 0xE9 ? va + 5 + BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(va + off + 1)) : -1;
        bool Match(int va, string hex)
        {
            var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int o = va + off;
            if (va < 0 || o + parts.Length > exe.Length) return false;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != "??" && exe[o + i] != Convert.ToByte(parts[i], 16)) return false;
            return true;
        }
        byte B(int va) => exe[va + off];
        if (!Match(0x10DA4, "E9 ?? ?? ?? ?? 90 90")) return null;
        int s1 = Jmp(0x10DA4), s2;
        bool strongest;
        int mul, thr = 0, shift = 0, pair = 0, div;
        // 곡우 꼴(0x1F1CC)
        const string strong =
            "31 FF 31 D2 8B 6C 24 1C EB 01 47 3B 3D E7 3B 00 00 7D 2D 89 F8 6B C0 50 03 05 45 3A 00 00 0F B7 58 3E D1 E3 66 03 58 37 " +
            "6B DB ?? 0F B7 48 39 66 03 48 42 6B DB ?? 01 CB 01 DA 89 5C BD 00 EB CA 85 FF 74 47 89 D0 C1 FA 1F F7 FF 6B C0 ?? C1 E8 ?? " +
            "31 D2 31 C9 31 DB EB 01 41 39 F9 7D 0D 3B 44 8D 00 7D 05 03 54 8D 00 43 EB EE 29 DF 0F AF FB 6B FF ?? 29 FA C1 EA ?? " +
            "89 DF 89 D3 6A 50 6A 00 55 E8 ?? ?? ?? ?? 83 C4 0C 89 DA E9";
        // 파워 꼴(0x19E71)
        const string sum =
            "31 FF BA 01 00 00 00 EB 01 47 3B 3D E7 3B 00 00 7D 23 89 F8 C1 E0 02 8D 1C 38 C1 E3 04 A1 45 3A 00 00 01 D8 " +
            "0F B7 58 37 01 DA 0F B7 58 3E C1 E3 ?? 01 DA EB D4 C1 EA ?? E9";
        if (Match(s1, strong))
        {
            strongest = true;
            mul = B(s1 + 42) * B(s1 + 53);
            thr = B(s1 + 77);
            shift = B(s1 + 80);
            pair = B(s1 + 114);
            div = B(s1 + 119);
            s2 = Jmp(s1 + 139);
        }
        else if (Match(s1, sum))
        {
            strongest = false;
            mul = B(s1 + 48);
            div = B(s1 + 55);
            s2 = Jmp(s1 + 56);
        }
        else return null;
        const string two =
            "85 FF 74 31 89 D0 C1 FA 1F F7 FF 8B 1D 03 3C 00 00 43 83 FB ?? 7F 1E 89 DF D1 E7 01 FB 89 C2 83 EA ?? 72 08 6B D2 ?? C1 EA ?? " +
            "29 D0 29 D8 72 05 E9";
        if (!Match(s2, two)) return null;
        int s3 = Jmp(s2 + 48);
        if (!Match(s3, "8B 54 24 18 39 FA 7F 07 40 6B C0 ?? C1 E8 ?? 01 44 24 18 E9")) return null;
        return new EnemyLevelRule(strongest, mul, thr, shift, pair, div, (sbyte)B(s2 + 20), B(s2 + 33), B(s2 + 38), B(s2 + 41),
                                  B(s3 + 11), B(s3 + 14));
    }
}
