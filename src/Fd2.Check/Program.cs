using Fd2.Data;

namespace Fd2.Check;

/// <summary>
/// 원작 표 · 규칙 점검 — 분석 노트(분석-캐릭터 · 아이템 · 마법 · 전투 · 지도 · 장면 · 자료형식)에 적힌 값과 맞춰 본다.
/// 노트의 값은 원본 판(용의기사2_original) 것이라, 모션패치 판으로 돌리면 몇 줄은 다르게 나온다.
/// </summary>
internal static class Program
{
    private static int _fail, _pass;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string dir = args.Length > 0 ? args[0]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "용의기사2 런처", "용의기사2_original");
        if (GameFolder.Missing(dir) is { } missing) { Console.WriteLine($"게임 폴더가 아닙니다({missing} 없음): {dir}"); return 2; }
        var t = GameTables.Load(dir);
        if (t == null) { Console.WriteLine($"FD2.EXE 가 없거나 크기가 {GameTables.ExeSize} 가 아닙니다: {dir}"); return 2; }
        Console.WriteLine($"게임 폴더: {dir}");

        Files(dir);
        Tables(t);
        Names(dir);
        Scripts(t);

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? $"모두 맞음 ({_pass})" : $"틀림 {_fail} / {_pass + _fail}");
        return _fail == 0 ? 0 : 1;
    }

    internal static void Same<T>(string what, T got, T want)
    {
        bool ok = EqualityComparer<T>.Default.Equals(got, want);
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"{(ok ? "  맞음" : "! 틀림")}  {what}: {got}{(ok ? "" : $"  (노트 {want})")}");
    }

    internal static void Seq(string what, IEnumerable<int> got, IEnumerable<int> want) =>
        Same(what, string.Join(",", got), string.Join(",", want));

    /// <summary>묶음 파일 항목 수 — 분석-자료형식 「항목 수」(끝 표시 칸을 뺀 수).</summary>
    private static void Files(string dir)
    {
        Console.WriteLine("\n[묶음 파일 항목 수]");
        foreach (var (file, n) in new[] { ("FDSHAP.DAT", 66), ("FIGANI.DAT", 408), ("DATO.DAT", 136), ("FDOTHER.DAT", 103), ("BG.DAT", 56),
                                          ("TAI.DAT", 56), ("FDTXT.DAT", 34), ("FDFIELD.DAT", 99), ("FDMUS.DAT", 20) })
            Same(file, Dat.Count(Path.Combine(dir, file)), n);
        Same("ANI.DAT 편 수", new AfmPlayer(dir).Films, 9);
        Same("FIGANI 캐릭터 수", Motion.Characters(dir), 136);
    }

    private static void Tables(GameTables t)
    {
        Console.WriteLine("\n[동료 표 0x61DA1 — 분석-캐릭터 8.1]");
        var saul = t.Heroes[0];
        Same("사울 종족 · 직업 · Lv", (saul.Race, saul.Class, saul.Level), (1, 1, 1));
        Same("사울 HP · MP · 이동", (saul.Hp, saul.Mp, saul.Move), (42, 0, 4));
        Same("사울 장비(무기 · 옷)", (saul.Weapon, saul.Armor), (0, 132));
        Seq("사울 가방", saul.Bag.Where(b => b != 255), [192]);
        var yuni = t.Heroes[9];
        Same("유니 법사 · HP 28 · MP 8 · 장곤 · 장포", (yuni.Class, yuni.Hp, yuni.Mp, yuni.Weapon, yuni.Armor), (5, 28, 8, 52, 164));
        Same("유니 처음 마법 비트(화염술)", (int)(yuni.Spells & 1), 1);
        Same("아래스 기사 · HP 48 · 이동 7 · 자창", (t.Heroes[4].Class, t.Heroes[4].Hp, t.Heroes[4].Move, t.Heroes[4].Weapon), (3, 48, 7, 20));
        Same("카일 기계 · 기병 · 위력팔뚝 · 전투갑옷", (t.Heroes[30].Race, t.Heroes[30].Class, t.Heroes[30].Weapon, t.Heroes[30].Armor), (6, 25, 72, 178));
        Same("란스로타 덧값", (t.Heroes[7].StrAdd, t.Heroes[7].DefAdd, t.Heroes[7].SpdAdd), (185, 110, 48));

        Console.WriteLine("\n[성장표 0x620A1 — 8.2 (표의 a~b 는 최소 a, 최대 b+1)]");
        var g = t.Growths[0];
        Same("사울 역량 · 내력 · 속도", (g.StrMin, g.StrMax, g.DefMin, g.DefMax, g.SpdMin, g.SpdMax), (6, 8, 4, 6, 2, 3));
        Same("사울 HP · MP · 마법표", (g.HpMin, g.HpMax, g.MpMin, g.MpMax, g.SpellList), (8, 12, 0, 0, 255));
        Same("유니 마법 배움 표", t.Growths[9].SpellList, 0);
        Seq("배움 표 0 (Lv5 공격술 · Lv9 열염술 · Lv15 시독술 · Lv21 염룡술 · Lv26 마비술)",
             t.SpellLearn[0].SelectMany(p => new[] { p.Level, p.Spell }), [5, 17, 9, 1, 15, 26, 21, 2, 26, 27]);

        Console.WriteLine("\n[합류 능력 — 2절 예: 사울 Lv1 → HP 42, 역량 6(+단검 10 = 공격 16), 내력 4(+피갑 8 = 방어 12), 속도 2]");
        var s = t.HeroStats(0, 1);
        Same("사울 Lv1 최대HP · 역량 · 내력 · 속도", (s.MaxHp, s.Str, s.Def, s.Spd), (42, 6, 4, 2));
        Same("사울 Lv1 공격 · 방어", (s.Str + t.Items[0].Atk, s.Def + t.Items[132].Def), (16, 12));

        Console.WriteLine("\n[몬스터 표 0x61AF9 — 8.6]");
        var thief = t.Monsters[96 - GameTables.MonsterBase];
        Same("96 도적 종족 · 직업 · HP/Lv · 역량 · 내력 · 속도 · 이동 · 경험",
             (thief.Race, thief.Class, thief.Hp, thief.Str, thief.Def, thief.Spd, thief.Move, thief.Exp), (1, 7, 14, 7, 1, 1, 4, 21));
        var soldier = t.Monsters[0];
        Same("68 사병", (soldier.Class, soldier.Hp, soldier.Str, soldier.Def, soldier.Spd, soldier.Move, soldier.Exp), (2, 18, 5, 2, 1, 4, 30));
        Same("104 화룡 직업 26 · 이동 0", (t.Monsters[104 - 68].Class, t.Monsters[104 - 68].Move), (26, 0));

        Console.WriteLine("\n[물건 표 0x602AD — 분석-아이템 7.1]");
        var dagger = t.Items[0];
        Same("0 단검 종류 · 공격 · 명중 · 사거리 · 값", (dagger.Kind, dagger.Atk, dagger.Hit, dagger.RangeMin, dagger.RangeMax, dagger.Price), (1, 10, 95, 1, 1, 50));
        Same("4 암흑검 독 10%", (t.Items[4].Special, t.Items[4].SpecialValue), (2, 10));
        Same("7 참철검 필살 + 30%", (t.Items[7].Special, t.Items[7].SpecialValue), (4, 30));
        Same("20 자창 사거리 1~2", (t.Items[20].RangeMin, t.Items[20].RangeMax), (1, 2));
        Same("44 단궁 사거리 2~3", (t.Items[44].RangeMin, t.Items[44].RangeMax), (2, 3));
        Same("71 마룡조 두 번 치기", t.Items[71].Special, 3);
        Same("132 피갑 방어 8", (t.Items[132].Kind, t.Items[132].Def), (22, 8));
        Same("192 약초 HP 회복 40 · 거리 1 · 우리 · 값 10", (t.Items[192].Use, t.Items[192].UseValue, t.Items[192].UseRange, t.Items[192].UseSide, t.Items[192].Price), (5, 40, 1, 1, 10));

        Console.WriteLine("\n[마법 표 0x619FD — 분석-마법 1 (세기는 노트가 다른 판 덤프를 본 듯해 여기서는 MP · 사거리 · 범위 · 명중 · 편만)]");
        var fire = t.Spells[0];
        Same("0 화염술 MP · 사거리 · 범위 · 명중 · 편", (fire.Mp, fire.Range, fire.Area, fire.Hit, fire.Side), (2, 5, 0, 90, 0));
        Same("8 성광탄 사거리 8 · 명중 100", (t.Spells[8].Range, t.Spells[8].Hit), (8, 100));
        Same("11 지진술 사거리 0 · 범위 7", (t.Spells[11].Range, t.Spells[11].Area), (0, 7));
        Same("13 치료술 MP 3 · 우리 편", (t.Spells[13].Mp, t.Spells[13].Side), (3, 1));
        Same("23 전송술 편 3", t.Spells[23].Side, 3);
        Same("30 암속인 사거리 20(한 줄)", t.Spells[30].Range, 20);
        Console.WriteLine($"  참고  세기 화염술 {fire.Power} · 열염술 {t.Spells[1].Power} · 치료술 {t.Spells[13].Power} · 치천사 {t.Spells[32].Power}");

        Console.WriteLine("\n[직업 표 — 분석-캐릭터 8.5 · 분석-전투]");
        Seq("필살 % 검사 · 무사 · 저격수 · 닌자 · 무성", new[] { 1, 8, 12, 23, 24 }.Select(c => t.Classes[c].Crit), [5, 18, 12, 30, 18]);
        // 성기사는 분석-캐릭터 8.5(9)를 따른다 — 분석-전투 · 분석-마법 표의 8 은 한 칸 밀려 적은 것으로 보인다
        Seq("마법 피해 ×/10 법사 · 대법사 · 영웅 · 성기사 · 26", new[] { 5, 13, 17, 11, 26 }.Select(c => t.Classes[c].MagicMul), [7, 5, 6, 9, 4]);
        Seq("부활값 검사 · 전사 · 소환사", new[] { 1, 2, 21 }.Select(c => t.Classes[c].Revive), [100, 150, 3000]);
        Seq("장비 종류 검사", t.Classes[1].EquipKinds, [1, 21, 22]);
        Seq("장비 종류 무사", t.Classes[8].EquipKinds, [7, 21, 26, 13]);
        Seq("이동비 보통(검사)", t.Classes[1].MoveCost, [1, 20, 1, 2, 2, 20]);
        Seq("이동비 기사", t.Classes[3].MoveCost, [1, 20, 2, 3, 3, 20]);
        Seq("이동비 용기사(나는 표)", t.Classes[19].MoveCost, [1, 1, 1, 1, 1, 20]);
        Seq("이동비 28(물에서만)", t.Classes[28].MoveCost, [20, 20, 20, 20, 1, 20]);
        Seq("이동비 0(지형 안 따짐)", t.Classes[0].MoveCost, [1, 1, 1, 1, 1, 1]);
        Seq("지형 공격 %", t.TerrainAttack, [5, 0, -5, -5, -5, 0]);
        Seq("지형 방어 %", t.TerrainDefense, [0, 0, 10, 10, -5, 0]);
        Same("전직: 사울 검성 +1 · 영웅 +2 · 용자훈장", (t.ClassChange[0], t.ClassChange[18], t.ClassChangeItem[0]), ((9, 1), (17, 2), 89));

        Console.WriteLine("\n[상점 0x6238D — 분석-아이템 7.2]");
        Seq("기록 1 무기점", t.Shops[0].Weapons, [128, 129, 132, 165]);
        Seq("기록 1 도구점", t.Shops[0].Tools, [192]);
        Seq("기록 1 비밀상점", t.Shops[0].Secret, [1, 22, 53, 192, 193, 132]);
        Same("기록 22 빈 기록", t.Shops[21].Weapons.Length, 0);

        Console.WriteLine("\n[장면 ↔ 곡 byte_51E63 · byte_51E81 — 분석-자료형식 11]");
        Seq("우리 차례 곡 0~29", Enumerable.Range(0, 30).Select(t.SceneMusic),
             [19, 19, 19, 19, 3, 19, 19, 19, 3, 4, 19, 19, 19, 19, 3, 19, 4, 19, 19, 3, 19, 3, 4, 19, 3, 19, 4, 19, 19, 8]);
        Seq("적 차례 곡 0~29", Enumerable.Range(0, 30).Select(t.EnemyMusic),
             [12, 12, 1, 12, 6, 12, 12, 1, 6, 4, 12, 1, 12, 1, 6, 12, 8, 12, 12, 6, 12, 6, 8, 12, 6, 1, 8, 1, 1, 8]);
    }

    private static void Names(string dir)
    {
        Console.WriteLine("\n[이름 표 FDTXT 0 — 분석-캐릭터 1]");
        var n = new NameTable(dir);
        Same("줄 수", n.Count, 661);
        Same("사울 이름 글자 수", n.Character(0).Length, 2);
        Same("화염술 이름 글자 수", n.Spell(0).Length, 3);
        Same("31 번 마법 이름은 빈 줄", n.Spell(31).Length, 0);
    }

    /// <summary>이동 대본 — 분석-장면 6.1 프롤로그.</summary>
    private static void Scripts(GameTables t)
    {
        Console.WriteLine("\n[이동 대본 0x627D8 — 분석-장면 6]");
        string Show(int k) => string.Join(" ", t.MoveScript(k).Select(s =>
            (s.Wait ? $"W{s.Count}" : $"x{s.Count}") + "[" + string.Join(",", s.Units.Select(u => $"{u.Unit}{"v<^>"[(int)u.Dir]}")) + "]"));
        Same("99 사울 위로 6", Show(99), "x6[2^]");
        Same("101 아래스 왼쪽 3 → (10,47)", Show(101), "x3[4<]");
        Same("102 왼 2 · 위 1 · 왼 1", Show(102), "x2[4<] x1[4^] x1[4<]");
        Same("90 숲 들어옴", Show(90), "x1[0<] x1[0<,1^] x4[0<,1<] W8[0<,1<] W8[0>,1<]");
        Same("0 1장 넷이 위로 6", Show(0), "x6[0^,1^,2^,3^] W8[0<] W8[0>] W8[0<] W4[0v]");
        Same("106 은 표 끝", t.MoveScript(106).Count, 0);
    }
}
