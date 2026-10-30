using Fd2.Data;
using static Fd2.Check.Program;

namespace Fd2.Check;

/// <summary>전투 규칙 — 1장(장면 0)을 세워 분석-전투의 식대로 나오는지 본다(값은 이 EXE 의 표로 손셈한 것).</summary>
internal static class Rules
{
    public static void Run(string dir, GameTables t)
    {
        var pal = GameFolder.Palette(dir);
        var icons = new IconBank(Path.Combine(dir, "FDICON.B24"), pal);
        var scene = new Scene(dir, 0, pal, icons);
        var b = new Battle(scene, t, seed: 1234);
        foreach (var u in scene.Units) b.Setup(u);

        Console.WriteLine("\n[1장 출전 — 분석-장면 2.4: 우리 자리 0 사울 · 1 유니 · 2 아래스 · 3 카일]");
        var party = scene.Units.Where(u => u.Team == 2).OrderBy(u => u.Slot).ToList();
        Seq("배열 0~3 의 캐릭터", party.Select(u => u.FormId), [0, 9, 4, 30]);
        var saul = party[0];
        Same("사울 Lv · HP · 공격 · 방어 · 명중 · 회피 · 이동 · 사거리", (saul.Level, saul.MaxHp, saul.Atk, saul.Def, saul.Hit, saul.Evade, saul.Move, saul.RangeMax),
             (1, 42, 16, 12, 97, 2, 4, 1));
        Same("유니 MP · 화염술 비트", (party[1].MaxMp, (int)(party[1].Spells & 1)), (8, 1));
        Same("아래스 자창 사거리 1~2 · 이동 7", (party[2].RangeMin, party[2].RangeMax, party[2].Move), (1, 2, 7));

        // 등장 차례 1 의 도적(몬스터 96, Lv 2) — 유닛 표 0 번은 배열 4 번
        var e0 = scene.Roster[0];
        var thief = scene.Spawn(e0)!;
        b.Setup(thief);
        Console.WriteLine("\n[도적 Lv2 — 몬스터 표 × 2 + 단검 · 포의]");
        Same("배열 번호(출전 4명 뒤)", thief.Slot, 4);
        Same("도적 HP · 공격 · 방어 · 명중 · 회피 · 떨굼", (thief.MaxHp, thief.Atk, thief.Def, thief.Hit, thief.Evade, thief.DropKind, thief.DropValue),
             (28, 24, 4, 97, 2, 0, 192));

        Console.WriteLine("\n[한 번 치기 — 분석-전투 5절 식, 평지(공격 +5%)]");
        // 사울 → 도적: 공 16 + 16×5/100 = 16, 방 4 → 9×12/10 = 10, 10/9 = 1 → 흔들기 % 1 = 0. 필살이면 방 2 → 12
        // 도적 → 사울: 공 24 + 1 = 25, 방 12 → 9×13/10 = 11
        Place(scene, saul, thief);
        var dmg = new HashSet<int>();
        int hits = 0, crits = 0, n = 4000;
        for (int i = 0; i < n; i++)
        {
            thief.Hp = thief.MaxHp;
            thief.Status[3] = 0;
            var r = b.Strike(saul, thief);
            if (r.Hit) { hits++; dmg.Add(r.Damage); if (r.Critical) crits++; }
        }
        Same("사울 → 도적 땅 종류(평지 0)", (scene.Ground(saul.X, saul.Y), scene.Ground(thief.X, thief.Y)), (0, 0));
        Same("명중 률 95% 언저리(97 − 2)", Math.Abs(hits * 100.0 / n - 95) < 2, true);
        Seq("피해 값들(보통 10 · 필살 12)", dmg.OrderBy(x => x), [10, 12]);
        Same("필살 률 5% 언저리(검사)", Math.Abs(crits * 100.0 / hits - 5) < 1.5, true);
        saul.Hp = saul.MaxHp;
        var back = b.Strike(thief, saul);
        Same("도적 → 사울 피해(맞았으면 11)", back.Hit ? back.Damage : 11, 11);

        Console.WriteLine("\n[되받아치기 · 두 번 치기]");
        Same("바로 옆 · 단검(최소 1) → 되받아침", b.CanCounter(thief, saul), true);
        thief.Status[4] = 3;
        Same("마비면 되받아치지 않음", b.CanCounter(thief, saul), false);
        thief.Status[4] = 0;
        int doubles = 0;
        for (int i = 0; i < 10000; i++) if (b.StrikeCount(saul) == 2) doubles++;
        Same("두 번 치기 3% 언저리", Math.Abs(doubles / 100.0 - 3) < 0.7, true);

        Console.WriteLine("\n[경험치 — 분석-전투 7절: 2 × 21 / 1 = 42, 살면 × 피해 / 최대 HP]");
        thief.Hp = 0;
        Same("죽이면", b.AttackExp(saul, thief, 10), 42);
        thief.Hp = 18;
        Same("10 피해만(10 × 42 / 28)", b.AttackExp(saul, thief, 10), 15);
        Same("적이 치면 0", b.AttackExp(thief, saul, 11), 0);

        Console.WriteLine("\n[레벨업 — 성장표 최소 + 난수 % (최대 − 최소), 지금 HP 는 그대로]");
        int hpBefore = saul.Hp, maxBefore = saul.MaxHp, atkBefore = saul.BaseAtk;
        var notes = b.GainExp(saul, 130);
        Same("Lv 2 · 경험 30", (saul.Level, saul.Exp), (2, 30));
        Same("역량 +6~7", saul.BaseAtk - atkBefore is >= 6 and <= 7, true);
        Same("최대 HP +8~11, 지금 HP 그대로", (saul.MaxHp - maxBefore is >= 8 and <= 11, saul.Hp == hpBefore), (true, true));
        Same("알림 줄 수(등급상승 · 오른 값)", notes.Count, 2);

        Console.WriteLine("\n[상태 — 독은 그 편 차례 시작에 최대 HP / 10]");
        saul.Status[3] = 2;
        saul.Hp = 30;
        var died = new List<Unit>();
        b.StatusTick(2, died);
        Same("HP 30 → 30 − 최대/10, 독 남은 차례 1", (saul.Hp, saul.Status[3]), (30 - saul.MaxHp / 10, 1));
        saul.Status[3] = 0;
        Same("쉬기 — 최대 HP / 5", b.Rest(saul), saul.MaxHp / 5);

        Console.WriteLine("\n[이동 — ZOC: 적 곁 칸에 들어가면 멈춤, 적 칸은 못 감]");
        var reach = b.Reach(saul);
        var foes = new HashSet<(int, int)> { (thief.X, thief.Y) };
        bool Beside((int X, int Y) c) => foes.Contains((c.X + 1, c.Y)) || foes.Contains((c.X - 1, c.Y)) || foes.Contains((c.X, c.Y + 1)) || foes.Contains((c.X, c.Y - 1));
        thief.Hp = thief.MaxHp;
        reach = b.Reach(saul);
        Same("적 칸은 범위 밖", reach.ContainsKey((thief.X, thief.Y)), false);
        Same("적 곁 칸을 지나서 간 칸이 없음", reach.Any(kv => kv.Value != kv.Key && kv.Value != (saul.X, saul.Y) && Beside(kv.Value)), false);

        Console.WriteLine("\n[이기고 짐 — 기본: 사울 쓰러지면 짐, 적이 없으면 이김]");
        Same("적이 살아 있으면 계속", b.Judge(), 0);
        thief.Hp = 0;
        Same("적이 다 쓰러지면 이김", b.Judge(), 2);
        saul.Hp = 0;
        Same("사울이 쓰러지면 짐", b.Judge(), 1);

        Console.WriteLine("\n[떨굼 — 우리 편이 쓰러뜨렸을 때만]");
        saul.Hp = 10;
        var drop = b.Drops([thief], saul);
        Same("약초(192) 를 받음", saul.Bag.Any(x => x.Item == 192 && !x.Equipped) && drop.Count == 1 && drop[0].Name == 181 + 192, true);
    }

    /// <summary>두 사람을 평지에 나란히 — 1장 지도에서 지형 0 이 둘 이어진 첫 자리.</summary>
    private static void Place(Scene s, Unit a, Unit d)
    {
        for (int y = 0; y < s.Height; y++)
            for (int x = 0; x + 1 < s.Width; x++)
                if (s.Ground(x, y) == 0 && s.Ground(x + 1, y) == 0 && s.Units.All(u => (u.X, u.Y) != (x, y) && (u.X, u.Y) != (x + 1, y) || u == a || u == d))
                {
                    (a.X, a.Y, d.X, d.Y) = (x, y, x + 1, y);
                    return;
                }
    }
}
