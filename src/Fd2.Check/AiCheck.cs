using Fd2.Data;
using static Fd2.Check.Program;

namespace Fd2.Check;

/// <summary>컴퓨터의 생각 · 마법 — 분석-AI 4~8절 · 분석-마법 3~5절 식이 그대로 나오는지.</summary>
internal static class AiCheck
{
    public static void Run(string dir, GameTables t)
    {
        var pal = GameFolder.Palette(dir);
        var icons = new IconBank(Path.Combine(dir, "FDICON.B24"), pal);
        var scene = new Scene(dir, 0, pal, icons);
        var b = new Battle(scene, t, seed: 77);
        foreach (var u in scene.Units) b.Setup(u);
        var ai = new BattleAi(b);
        var saul = scene.Units.First(u => u.Slot == 0);
        var yuni = scene.Units.First(u => u.Slot == 1);
        // 다른 출전자는 멀리 치운다 — 사울 하나만 상대로
        foreach (var u in scene.Units.Where(u => u.Team == 2 && u != saul && u != yuni)) u.Hp = 0;

        Console.WriteLine("\n[AI 공격 점수 — 분석-AI 5절]");
        var thief = scene.Spawn(scene.Roster[0])!;
        b.Setup(thief);
        Same("도적 AI 갈래(유닛 표 +17)", thief.AiByte & 15, 0);
        (thief.X, thief.Y) = (saul.X + 1, saul.Y);
        yuni.Hp = 0;
        var a = ai.AttackScore(thief);
        // v = 24 − 12 = 12 > 2 → 8, 되받아침이 있어 v += 4 − 16, 사울이라 × 3 / 2 → 0
        Same("곁의 사울을 칠 점수 · v", (a.Score, a.V, a.Target == saul), (8, 0, true));
        var plan = ai.Decide(thief);
        Same("갈래 0 → 사울을 친다", plan.Target == saul, true);
        (thief.X, thief.Y) = FreeNear(scene, saul, 9);
        int before = Math.Abs(thief.X - saul.X) + Math.Abs(thief.Y - saul.Y);
        plan = ai.Decide(thief);
        var end = plan.Path.Count > 0 ? plan.Path[^1] : (thief.X, thief.Y);
        Same("멀면 사울 쪽으로 걷는다", plan.Target == null && Math.Abs(end.Item1 - saul.X) + Math.Abs(end.Item2 - saul.Y) < before, true);
        thief.AiByte = 2;
        Same("갈래 2(제자리 지킴)는 닿지 않으면 쉼", ai.Decide(thief).Rest, true);
        thief.AiByte = 8;
        var none = ai.Decide(thief);
        Same("갈래 8(꼼짝 안 함)은 아무것도 안 함", none.Path.Count == 0 && none.Target == null && !none.Rest && none.Spell < 0, true);
        thief.Hp = 0;

        Console.WriteLine("\n[AI 마법 점수 — 분석-AI 6절: HP < 세기면 24, 사울이면 × 1.5]");
        var mage = new Unit { Icon = 90, Form = 90, Side = Side.Other, Level = 2 };
        scene.Units.Add(mage);
        b.Setup(mage);
        mage.Spells = 1;               // 화염술
        mage.Mp = mage.MaxMp = 10;
        (mage.X, mage.Y) = FreeNear(scene, saul, 3);
        var power = t.Spells[0].Power;
        var m = ai.SpellScore(mage);
        Same("화염술 점수(사울 HP 42 · 세기 " + power + ")", (m.Spell, m.Score), (0, (saul.Hp < power ? 24 : 8) * 3 / 2));
        Same("마법 점수가 6 이상이면 먼저 움직이는 유닛", ai.PriorityScore(mage) >= 6, true);
        mage.Status[5] = 2;
        Same("봉쇄면 마법을 안 본다", ai.SpellScore(mage).Score, 0);
        mage.Status[5] = 0;

        Console.WriteLine("\n[마법 피해 · 회복 — 분석-마법 3절]");
        var dmg = new HashSet<int>();
        for (int i = 0; i < 300; i++)
        {
            saul.Hp = saul.MaxHp;
            var r = b.CastOn(mage, 0, saul);
            if (r.Hit) dmg.Add(r.Amount);
        }
        int a0 = 10 * power / 10;
        Same($"화염술 → 검사(배율 10): {9 * a0 / 10} ~ {9 * a0 / 10 + a0 * 99 / 1000}", dmg.Min() >= 9 * a0 / 10 && dmg.Max() <= 9 * a0 / 10 + a0 * 99 / 1000, true);
        saul.Hp = 1;
        saul.MaxHp = 200;
        var heal = b.HealBy(saul, 70);
        saul.MaxHp = saul.Hp = 42;
        Same("치료술 70 → 63 ~ 69 채움", heal.Amount is >= 63 and <= 69, true);
        int poisoned = 0;
        for (int i = 0; i < 2000; i++) { saul.Status[3] = 0; saul.Hp = saul.MaxHp; if (b.CastOn(mage, 26, saul).Hit) poisoned++; }
        Same("시독술 — 늘 50% 언저리(표 명중은 안 씀)", Math.Abs(poisoned / 20.0 - 50) < 4, true);
        saul.Status[3] = 2;
        Same("이미 걸렸으면 빗나감", b.CastOn(mage, 26, saul).Hit, false);
        saul.Status[3] = 0;

        Console.WriteLine("\n[마법 범위 — 분석-마법 4절: 범위 1 = 5칸, 2 = 13칸]");
        Same("범위 1 · 2 칸 수", (Cells(1), Cells(2)), (5, 13));
        Same("지진술(사거리 0) 가운데는 자기 칸뿐", b.SpellCenters(mage, 11).Count, 1);
        Same("화염술(사거리 5) 가운데 칸 수(지도 안)", b.SpellCenters(mage, 0).Count <= 61, true);

        Console.WriteLine("\n[마법 경험 — 대상 Lv × 몬스터 경험 / (시전자 Lv + 직업 > 8 ? 30)]");
        var victim = scene.Spawn(scene.Roster[1])!;
        b.Setup(victim);
        yuni.Hp = yuni.MaxHp;
        victim.Hp = 0;
        Same("유니가 도적 Lv2 를 쓰러뜨림 = 2 × 21 / 1", b.SpellExp(yuni, 0, [new SpellResult(victim, true, 28, false)]), 42);
    }

    private static int Cells(int area)
    {
        int n = 0;
        for (int dy = -area; dy <= area; dy++)
            for (int dx = -area; dx <= area; dx++)
                if (Battle.InArea(dx, dy, area, 0)) n++;
        return n;
    }

    private static (int, int) FreeNear(Scene s, Unit u, int d)
    {
        for (int dy = -d; dy <= d; dy++)
            for (int dx = -d; dx <= d; dx++)
                if (Math.Abs(dx) + Math.Abs(dy) == d && s.Free(u.X + dx, u.Y + dy) && s.Ground(u.X + dx, u.Y + dy) == 0) return (u.X + dx, u.Y + dy);
        return (u.X, u.Y);
    }
}
