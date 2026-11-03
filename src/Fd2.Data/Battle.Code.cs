namespace Fd2.Data;

/// <summary>
/// 버전마다 고친 계산식을 그 EXE 의 기계어로 셈하는 곳(<see cref="X86Cpu"/>) — 고친 코드가 있는 EXE 에서만 쓰고(<see cref="ExeRules"/> 가 바이트 꼴로 가린다),
/// 원본 EXE 이거나 토막을 못 돌리면 원래 C# 식(원본 규칙)으로 간다. 사람은 80바이트 칸(<see cref="HeroRecord.From"/>)으로 넘긴다.
/// </summary>
public sealed partial class Battle
{
    private const uint AttBase = 0x1000000, DefBase = 0x2000000, StackBase = 0x3000000, ResBase = 0x4000000, Esp = StackBase + 0x100;

    private X86Cpu NewCpu(byte[] exe)
    {
        var cpu = new X86Cpu(exe) { Rand = () => (uint)Rng.Next() };
        // 필살 % 표(0x524A8, 직업 − 1)는 편집기 표 값을 준다
        if (Tables != null)
            cpu.DataOverride = va => va >= 0x524A8 && va - 0x524A8 + 1 < Tables.Classes.Count ? (byte)Tables.Classes[(int)(va - 0x524A8) + 1].Crit : null;
        return cpu;
    }

    /// <summary>
    /// 전투 화면 한 번 치기(sub_2F7B6 0x2F925 ~ 0x2FA4D)를 EXE 코드로 — ebp 치는 쪽 칸, edi 맞는 쪽 칸, esi 결과(+0 빗나감 · +4 필살 · +8 독 · +14h 피해),
    /// [esp+24h] 공(지형 뒤) · [2Ch] 방 · [28h] HP · [8] 최대 HP · [14h] 명중 · [0Ch] 회피 · [10h] 직업 − 1 · ebx 무기 특수 · [1Ch] 특수 값.
    /// 못 돌리면 null(난수를 몇 번 썼을 수 있다).
    /// </summary>
    private StrikeResult? StrikeByCode(Unit a, Unit d, int atk, int def, ItemInfo? w)
    {
        if (!Rules.StrikeFromCode || Rules.Exe is not { } exe) return null;
        var cpu = NewCpu(exe);
        byte[] ra = HeroRecord.From(a), rd = HeroRecord.From(d), stack = new byte[0x200], res = new byte[0x18];
        cpu.Map(AttBase, ra); cpu.Map(DefBase, rd); cpu.Map(StackBase, stack); cpu.Map(ResBase, res);
        (cpu.R[4], cpu.R[5], cpu.R[7], cpu.R[6], cpu.R[3]) = (Esp, AttBase, DefBase, ResBase, (uint)(w?.Special ?? 0));
        void L(uint off, int v) => cpu.Write(Esp + off, (uint)v, 4);
        L(0x08, d.MaxHp); L(0x0C, d.Evade); L(0x10, a.Class - 1); L(0x14, a.Hit); L(0x1C, w?.SpecialValue ?? 0);
        L(0x24, atk); L(0x28, d.Hp); L(0x2C, def); L(0x30, 0);
        stack[0x100 + 0x34] = (byte)d.Level;
        stack[0x100 + 0x38] = (byte)a.Level;
        cpu.Write(ResBase, 1, 4);
        try { cpu.Run(0x2F925, [0x2FA4D]); }
        catch (X86Fault) { return null; }
        bool miss = cpu.Read(ResBase, 4) != 0, crit = cpu.Read(ResBase + 4, 4) != 0, poisoned = cpu.Read(ResBase + 8, 4) != 0;
        int dmg = (int)cpu.Read(Esp + 0x30, 4), hp = (int)cpu.Read(Esp + 0x28, 4);
        if (poisoned) d.Status[3] = rd[0x25];
        d.Hp = Math.Max(0, Math.Min(d.Hp, hp));
        return new StrikeResult(!miss, crit, dmg, poisoned);
    }

    /// <summary>속도↑ 의 명중 · 회피 덧값(sub_1B750 0x1B796 ~ 0x1B79B, [esp+4] 에 더한다) — 원본 15. 곡우 · 파워: 10, 속도 100 · 200 이상 +5 씩, 직업 18 +15.</summary>
    private int SpeedBuff(Unit u)
    {
        if (Rules.Exe is not { } exe || !X86Cpu.CodeAt(exe, 0x1B79B, "8B 44 24 04")) return 15;
        var cpu = NewCpu(exe);
        byte[] r = HeroRecord.From(u), stack = new byte[0x200];
        cpu.Map(AttBase, r); cpu.Map(StackBase, stack);
        (cpu.R[4], cpu.R[7]) = (Esp, AttBase);
        try { cpu.Run(0x1B796, [0x1B79B], 200); }
        catch (X86Fault) { return 15; }
        return (int)cpu.Read(Esp + 4, 4);
    }

    /// <summary>공격↑ · 방어↑ 를 붙인 값(sub_1B750) — 원본 <c>(int)(v × 1.15)</c>, 고친 꼴이면 <c>x = (int)(v × 배율)</c> 뒤 EXE 꼬리 코드(<see cref="BuffCode"/>).</summary>
    private int BuffValue(Unit u, int v, bool defense)
    {
        if (Rules.Buff is not { } b || Rules.Exe is not { } exe) return (int)(v * 1.15);   // dbl_5018D
        double mul = defense ? b.DefMul : b.AtkMul;
        uint tail = defense ? b.DefTail : b.AtkTail, exit = defense ? 0x1B81Du : 0x1B802u, slot = defense ? 0x0Cu : 0x08u;
        int x = (int)(v * mul);
        var cpu = NewCpu(exe);
        byte[] r = HeroRecord.From(u), stack = new byte[0x200];
        cpu.Map(AttBase, r); cpu.Map(StackBase, stack);
        (cpu.R[4], cpu.R[7], cpu.R[2]) = (Esp, AttBase, (uint)v);
        cpu.Write(Esp + slot, (uint)x, 4);
        try { cpu.Run(tail, [exit], 200); }
        catch (X86Fault) { return (int)(v * 1.15); }
        return (int)cpu.Read(Esp + slot, 4);
    }

    /// <summary>
    /// 승리 뒤 보상(<see cref="ExeRules.VictoryRewards"/>) 가운데 이 장면 · 이 때(late: 승리 대본 뒤)의 것을 준다 — sub_1C220 처럼
    /// 배열 차례(Slot)로 우리 편(편 2) 가운데 가방에 빈칸이 있는 첫 사람에게(쓰러진 사람도 배열에 남아 있으면 받는다). 다 차 있으면 그 물건은 없어진다.
    /// 준 (물건, 받은 사람) 들. 원작은 알림을 띄우지 않는다.
    /// </summary>
    public List<(int Item, Unit? To)> GiveVictoryRewards(bool late)
    {
        var given = new List<(int, Unit?)>();
        if (Tables == null) return given;
        var lead = Scene.Units.Find(u => u.Slot == 0) ?? Scene.Hidden.Find(u => u.Slot == 0);
        foreach (var r in Rules.VictoryRewards.Where(r => r.Scene == Scene.Index && (r.Late != 0) == late))
        {
            if (!r.Applies(Turn, lead)) continue;
            foreach (int item in r.Items())
            {
                var to = Scene.Units.Where(u => u.Team == 2 && u.Slot >= 0).OrderBy(u => u.Slot).FirstOrDefault(u => u.Bag.Count < 8);
                if (to != null && item < Tables.Items.Count) to.Bag.Add((item, false));
                given.Add((item, to));
            }
        }
        return given;
    }

    /// <summary>
    /// 올리기 차례(sub_22721 · 22866 · 22997) — 굴린 <c>난수 % 4 + 2</c>(edx)를 대상 칸(esi) +22h · +23h · +24h 에 적는 자리부터 FPU 앞까지 돌린다.
    /// 원본은 그대로 적고, 곡우는 이름 12 · 99(공격↑) · 3 · 79(방어↑) 이면 +2, 파워는 그대로. 못 돌리면 굴린 값.
    /// </summary>
    private int BuffTurns(Unit target, int k, int rolled)
    {
        if (Rules.Exe is not { } exe || k is < 0 or > 2) return rolled;
        (uint start, uint[] exits) = k switch
        {
            0 => (0x227D0u, new uint[] { 0x227D5, 0x227D7 }),
            1 => (0x22915u, new uint[] { 0x2291A, 0x2291C }),
            _ => (0x22A44u, new uint[] { 0x22A51, 0x22A52 }),
        };
        var cpu = NewCpu(exe);
        byte[] r = HeroRecord.From(target), stack = new byte[0x200];
        cpu.Map(AttBase, r); cpu.Map(StackBase, stack);
        (cpu.R[4], cpu.R[6], cpu.R[2]) = (Esp, AttBase, (uint)rolled);
        r[0x22 + k] = 0;
        try { cpu.Run(start, exits, 200); }
        catch (X86Fault) { return rolled; }
        return r[0x22 + k];
    }

    /// <summary>
    /// 걸기 차례(sub_22D1B 0x22DFC ~ 0x22E04) — eax 날 난수, ebx 4, edi 대상 칸, ebp 그 상태 칸(+22h + k), [esp+28h] 상태 자리(25h 독 · 26h · 27h).
    /// 원본 <c>난수 % 4 + 2</c>, 곡우는 이름마다 더하거나 1 · 0 으로(사울은 올리기가 걸려 있으면 0). 못 돌리면 원본 식.
    /// </summary>
    private int HexTurns(Unit target, int k, int raw)
    {
        int plain = raw % 4 + 2;
        if (Rules.Exe is not { } exe || k is < 0 or > 5) return plain;
        var cpu = NewCpu(exe);
        byte[] r = HeroRecord.From(target), stack = new byte[0x200];
        cpu.Map(AttBase, r); cpu.Map(StackBase, stack);
        (cpu.R[4], cpu.R[7], cpu.R[5], cpu.R[0], cpu.R[2], cpu.R[3]) = (Esp, AttBase, AttBase + 0x22 + (uint)k, (uint)raw, 0, 4);
        cpu.Write(Esp + 0x28, (uint)(0x22 + k), 4);
        try { cpu.Run(0x22DFC, [0x22E04], 200); }
        catch (X86Fault) { return plain; }
        return r[0x22 + k];
    }

    /// <summary>
    /// 회복(sub_1C916 0x1C947 ~ 0x1C990)을 EXE 코드로 — esi 대상 칸, edi 지금 HP, [esp] 최대 HP, [esp+1Ch] 회복 바탕 값. 끝의 edi 가 새 HP(최대 HP 로 자름).
    /// 곡우 · 파워는 그 앞에 편 · 직업마다 값을 바꾸고 상태(독 · 마비 · …)를 풀기도 한다. 못 돌리면 null.
    /// </summary>
    private SpellResult? HealByCode(Unit target, int p)
    {
        if (!Rules.HealFromCode || Rules.Exe is not { } exe) return null;
        var cpu = NewCpu(exe);
        byte[] r = HeroRecord.From(target), stack = new byte[0x200];
        cpu.Map(AttBase, r); cpu.Map(StackBase, stack);
        (cpu.R[4], cpu.R[6], cpu.R[7]) = (Esp, AttBase, (uint)Math.Max(0, target.Hp));
        cpu.Write(Esp, (uint)target.MaxHp, 4);
        cpu.Write(Esp + 0x1C, (uint)p, 4);
        try { cpu.Run(0x1C947, [0x1C990]); }
        catch (X86Fault) { return null; }
        int before = target.Hp;
        target.Hp = Math.Min(target.MaxHp, (int)cpu.R[7]);
        for (int k = 0; k < 6; k++) target.Status[k] = r[34 + k];
        return new SpellResult(target, true, target.Hp - before, true);
    }
}
