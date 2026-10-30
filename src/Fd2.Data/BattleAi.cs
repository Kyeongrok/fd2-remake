namespace Fd2.Data;

/// <summary>
/// 컴퓨터가 고른 한 사람의 할 일 — 지금 칸에서 마법 · 물건을 쓰거나, 길을 걸어 다 걸은 뒤 치기 · 상자 훔치기 · 사라지기, 또는 쉬기.
/// 아무것도 안 하는 계획도 있다(갈래 6 · 8, 점수가 같아 넘기는 때).
/// </summary>
public sealed class AiPlan
{
    /// <summary>지금 칸에서 걸 마법(-1 없음) · 쓸 물건의 가방 칸(-1 없음), 그 가운데 칸.</summary>
    public int Spell = -1, ItemSlot = -1;
    public (int X, int Y) Center;

    /// <summary>걸을 길(출발 칸 빼고).</summary>
    public List<(int X, int Y)> Path = new();

    /// <summary>다 걸은 뒤 무기로 칠 상대.</summary>
    public Unit? Target;

    public bool Rest;

    /// <summary>다 걸어 목표 칸(Goal)에 닿으면 — 그 상자를 훔친다(도둑, -1 없음) · 판에서 사라진다(달아남).</summary>
    public int StealChest = -1;
    public bool VanishAtGoal;
    public (int X, int Y) Goal;

    /// <summary>갈래 11 — 마법을 쓴 뒤 공격 점수를 다시 본다(<see cref="BattleAi.FollowUp"/>).</summary>
    public bool ThenAttack;
}

/// <summary>
/// 컴퓨터 아군 · 적의 생각(분석-AI, sub_13A9F · sub_14EF0 · sub_14237 · sub_1598A · sub_1567E · sub_14121 · sub_13E9C · sub_14B78).
/// 유닛 +52 아래 4비트가 갈래(0 덤빔 · 1 덤빔(직선 쫓기 없음) · 2 제자리 지킴 · 3 사람 쫓기 · 4 자리로 감 · 5 도둑 · 6 없음 · 7 달아남 ·
/// 8 꼼짝 안 함 · 9 사람 쫓기(첫 행동 없이) · 10 자리 지키며 감 · 11 마법 먼저), 0x40 은 같은 점수면 공격, 0x80 은 아끼는 유닛.
/// </summary>
public sealed class BattleAi(Battle battle)
{
    private Scene Scene => battle.Scene;
    private GameTables? Tables => battle.Tables;

    /// <summary>길 찾기의 걸음 끝(sub_14121 · sub_14B78 의 28).</summary>
    private const int MaxSteps = 28;

    private IEnumerable<Unit> Opponents(Unit u) => Scene.Units.Where(o => o.Hp > 0 && o.MaxHp > 0 && o.Team == 0 != (u.Team == 0));

    private static AiPlan Rest => new() { Rest = true };

    /// <summary>적 차례 첫 훑기(sub_1D8BA)에서 먼저 움직일 유닛인가를 가르는 점수 — 마법 · 물건 점수 가운데 큰 것(6 이상이면 먼저).</summary>
    public int PriorityScore(Unit u) => Math.Max(SpellScore(u).Score, ItemScore(u).Score);

    /// <summary>한 사람의 차례(sub_13A9F) — 갈래로 나눠 계획을 낸다.</summary>
    public AiPlan Decide(Unit u)
    {
        switch (u.AiByte & 15)
        {
            case 0: return Act(u) ?? Approach(u) ?? Straight(u) ?? Rest;
            case 1: return Act(u) ?? Approach(u) ?? Rest;
            case 2: return Act(u) ?? Rest;
            case 3: return Act(u) ?? Chase(u) ?? Approach(u) ?? Straight(u) ?? Rest;
            case 4: return WalkToward(u, (u.AiX, u.AiY)) ?? Rest;
            case 5: return Act(u) ?? Thief(u) ?? Approach(u) ?? Straight(u) ?? Rest;
            case 7: return Flee(u);
            case 9: return Chase(u) ?? Act(u) ?? Approach(u) ?? Straight(u) ?? Rest;
            case 10: return Act(u) ?? WalkToward(u, (u.AiX, u.AiY)) ?? Rest;
            case 11:
            {
                var m = SpellScore(u);
                if (m.Score >= 6) return new AiPlan { Spell = m.Spell, Center = m.Center, ThenAttack = true };
                return FollowUp(u);
            }
            default: return new AiPlan();   // 6 · 8 · 12~15 — 아무것도 안 함
        }
    }

    /// <summary>갈래 11 의 마법 뒤(또는 마법 점수가 낮을 때) — 공격 점수 6 이상이면 공격, 아니면 가장 가까운 상대 쪽으로, 못 가면 쉼.</summary>
    public AiPlan FollowUp(Unit u)
    {
        var a = AttackScore(u);
        if (a.Score >= 6 && a.Target != null) return new AiPlan { Path = a.Path, Target = a.Target };
        return Approach(u) ?? Rest;
    }

    // ── 행동 고르기(sub_14EF0) ─────────────────────────────────────────────

    /// <summary>
    /// 공격 · 마법 · 물건 점수를 따로 셈해 모두 6 미만이면 null(갈래가 다음 길로). 가장 큰 것을 하되,
    /// 공격 = 마법 &gt; 물건이면 마법 번호 &lt; 11 일 때 「마법 세기 ≥ 내 공격력 − 상대 방어력」 이면 마법, 11 이상이면 0x40 이면 공격;
    /// 공격 = 물건 &gt; 마법이면 0x40 이면 공격, 아니면 물건. 그 밖의 같음(마법 = 물건 &gt; 공격 …)은 아무것도 안 하고 차례를 넘긴다.
    /// </summary>
    private AiPlan? Act(Unit u)
    {
        var a = AttackScore(u);
        var m = SpellScore(u);
        var i = ItemScore(u);
        if (a.Score < 6 && m.Score < 6 && i.Score < 6) return null;
        AiPlan Attack() => new() { Path = a.Path, Target = a.Target };
        AiPlan Magic() => new() { Spell = m.Spell, Center = m.Center };
        AiPlan Item() => new() { ItemSlot = i.Slot, Center = i.Center };
        bool likesAttack = (u.AiByte & 0x40) != 0;
        if (a.Score > m.Score && a.Score > i.Score) return Attack();
        if (a.Score == m.Score && m.Score > i.Score)
        {
            if (m.Spell < 11) return m.Power >= u.Atk - (a.Target?.Def ?? 0) ? Magic() : Attack();
            return likesAttack ? Attack() : Magic();
        }
        if (a.Score == i.Score && i.Score > m.Score) return likesAttack ? Attack() : Item();
        if (m.Score > a.Score && m.Score >= i.Score) return Magic();
        if (i.Score > a.Score && i.Score > m.Score) return Item();
        return new AiPlan();
    }

    // ── 공격 점수(sub_14237) ─────────────────────────────────────────────────

    /// <summary>
    /// 움직일 수 있는 모든 칸 × 무기 사거리 안의 상대마다:
    /// <code>
    /// v = 내 공격 − 상대 방어;  점수 = v &gt; 2 ? 8 : 0
    /// v &gt; 상대 HP 면 v ×= 2, 점수 = 18
    /// 상대가 그 칸에서 되받아칠 수 있으면 v += 내 방어 − 상대 공격
    /// 상대가 사울(캐릭터 0)이면 v = v × 3 / 2
    /// </code>
    /// 점수가 크면, 같으면 v 가 크면 고른다(먼저 찾은 것이 남는다). 나는 유닛이면 그 칸 지형의 공 · 방 % 를 더한다(노트 그대로).
    /// </summary>
    public (int Score, int V, Unit? Target, List<(int X, int Y)> Path) AttackScore(Unit u)
    {
        var best = (Score: 0, V: int.MinValue, Target: (Unit?)null, Path: new List<(int X, int Y)>());
        if (battle.Weapon(u) == null) return best;
        var reach = battle.Reach(u);
        var foes = Opponents(u).ToList();
        bool flyer = Battle.IsFlyer(u);
        foreach (var cell in Scene.Stops(reach, u))
        {
            int a = u.Atk, d = u.Def;
            if (flyer && Tables != null && Scene.Ground(cell.X, cell.Y) is var g and >= 0 and < 6)
            {
                a += a * Tables.TerrainAttack[g] / 100;
                d += d * Tables.TerrainDefense[g] / 100;
            }
            foreach (var o in foes)
            {
                if (!Battle.InArea(o.X - cell.X, o.Y - cell.Y, u.RangeMax, u.RangeMin)) continue;
                int v = a - o.Def, score = v > 2 ? 8 : 0;
                if (v > o.Hp) { v *= 2; score = 18; }
                bool counter = !o.Paralyzed && Math.Abs(o.X - cell.X) + Math.Abs(o.Y - cell.Y) == 1 && battle.Weapon(o) is { RangeMin: 1 };
                if (counter) v += d - o.Atk;
                if (o.NameId == 0) v = v * 3 / 2;
                if (best.Target == null || score > best.Score || score == best.Score && v > best.V)
                    best = (score, v, o, Scene.PathTo(reach, cell));
            }
        }
        return best;
    }

    // ── 마법 점수(sub_1598A · sub_15B77) ────────────────────────────────────

    /// <summary>
    /// 지금 선 칸에서만(움직인 뒤 마법은 안 쓴다), 봉쇄가 아니고 MP 가 되는 마법마다 사거리 안 칸 × 범위 안 대상의 점수를 더한다.
    /// 공격(0~12): HP ≥ 세기 8 · 아래면 24(사울이면 × 1.5, 10~12 는 나는 유닛 빼고), 회복(13~16): HP &lt; 최대/3 이면 8 · &lt; 최대/2 면 3(대상 +52 &amp; 1 이면 × 2),
    /// 올리기(17~19) 안 걸린 대상 3, 풀기(20 · 21) 걸린 대상 6, 봉쇄(22) 마법이 있는 안 걸린 대상 6, 시독 · 마비(26 · 27) 안 걸린 대상 4, 그 밖 0.
    /// 같은 점수면 세기가 큰 마법.
    /// </summary>
    public (int Score, int Spell, (int X, int Y) Center, int Power) SpellScore(Unit u)
    {
        var best = (Score: 0, Spell: -1, Center: (u.X, u.Y), Power: 0);
        if (u.Sealed || Tables == null) return best;
        foreach (int m in battle.KnownSpells(u))
        {
            var s = Tables.Spells[m];
            if (u.Mp < s.Mp) continue;
            foreach (var c in battle.SpellCenters(u, m))
            {
                int sum = 0;
                foreach (var o in battle.SpellTargets(u, m, c)) sum += SpellTargetScore(m, s, o);
                if (sum > best.Score || sum == best.Score && sum > 0 && s.Power > best.Power)
                    best = (sum, m, c, s.Power);
            }
        }
        return best;
    }

    private static int SpellTargetScore(int m, SpellInfo s, Unit o)
    {
        switch (m)
        {
            case <= 12:
            {
                if (m >= 10 && Battle.IsFlyer(o)) return 0;
                int v = o.Hp >= s.Power ? 8 : 24;
                return o.NameId == 0 ? v * 3 / 2 : v;   // dbl_50144
            }
            case <= 16: return HealScore(o) * ((o.AiByte & 1) != 0 ? 2 : 1);
            case <= 19: return o.Status[m - 17] == 0 ? 3 : 0;
            case 20: return o.Poisoned ? 6 : 0;
            case 21: return o.Paralyzed ? 6 : 0;
            case 22: return !o.Sealed && o.Spells != 0 ? 6 : 0;
            case 26: return o.Poisoned ? 0 : 4;
            case 27: return o.Paralyzed ? 0 : 4;
            default: return 0;   // 전송 · 기술 · 행동술 · 소환은 컴퓨터가 안 쓴다
        }
    }

    /// <summary>HP 가 최대의 1/3 아래면 8, 1/2 아래면 3.</summary>
    private static int HealScore(Unit o) => o.Hp < o.MaxHp / 3 ? 8 : o.Hp < o.MaxHp / 2 ? 3 : 0;

    // ── 물건 점수(sub_1567E · sub_15880) ────────────────────────────────────

    /// <summary>
    /// 가방의 쓰는 물건마다 지금 칸에서 쓰는 거리(16 넘으면 한 줄) 안 칸 × 범위 안 대상:
    /// 회복(쓰임 5 · 13)은 회복 마법과 같은 HP 비율 점수(대상 +52 &amp; 0x80 이면 × 3), 공격(20 · 21 · 24)은 HP ≤ 세기면 18 · 아니면 8.
    /// </summary>
    public (int Score, int Slot, (int X, int Y) Center) ItemScore(Unit u)
    {
        var best = (Score: 0, Slot: -1, Center: (u.X, u.Y));
        if (Tables == null) return best;
        for (int slot = 0; slot < u.Bag.Count; slot++)
        {
            var it = Tables.Items[u.Bag[slot].Item];
            if (it.Use is not (5 or 13 or 20 or 21 or 24)) continue;
            int power = it.Use switch
            {
                20 or 21 when it.UseValue < Tables.Spells.Count => Tables.Spells[it.UseValue].Power,
                24 => it.UseValue,
                _ => 0,
            };
            foreach (var c in ItemCenters(u, it))
            {
                int sum = 0;
                foreach (var o in ItemTargets(u, it, c))
                    sum += it.Use is 5 or 13 ? HealScore(o) * ((o.AiByte & 0x80) != 0 ? 3 : 1) : o.Hp <= power ? 18 : 8;
                if (sum > best.Score) best = (sum, slot, c);
            }
        }
        return best;
    }

    /// <summary>물건을 쓸 가운데 칸 — 쓰는 거리(16 이상은 십자 한 줄, 거리 − 16).</summary>
    public List<(int X, int Y)> ItemCenters(Unit u, ItemInfo it)
    {
        var list = new List<(int X, int Y)>();
        int r = it.UseRange < 16 ? it.UseRange : it.UseRange - 16;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (it.UseRange >= 16 ? dx != 0 && dy != 0 : Math.Abs(dx) + Math.Abs(dy) > r) continue;
                if (Scene.Inside(u.X + dx, u.Y + dy)) list.Add((u.X + dx, u.Y + dy));
            }
        return list;
    }

    /// <summary>물건이 드는 사람 — 범위(마름모 · 16 이상 십자) 안, 쓰는 편(1 쓰는 사람 편 · 0 상대).</summary>
    public List<Unit> ItemTargets(Unit u, ItemInfo it, (int X, int Y) c) =>
        Scene.Units.Where(o => o.Hp > 0 && o.MaxHp > 0 && (it.UseSide == 1 ? o.Team == 0 == (u.Team == 0) : o.Team == 0 != (u.Team == 0))
                               && Battle.InArea(o.X - c.X, o.Y - c.Y, it.UseArea, 0)).ToList();

    // ── 걷기(sub_14121 · sub_13E9C · sub_14B78) ─────────────────────────────

    /// <summary>
    /// 길 찾기 — 직업 이동비로 가장 싼 길(20 이상은 못 감). block 이면 상대가 선 칸은 못 지나간다(목표 칸은 된다).
    /// goal 에 닿는 첫 칸까지, 걸음 수가 maxSteps 를 넘으면 버린다. 없으면 null.
    /// </summary>
    private List<(int X, int Y)>? FindPath(Unit u, Func<(int X, int Y), bool> goal, bool block, int maxSteps)
    {
        var costs = battle.MoveCosts(u);
        var foes = block ? Opponents(u).Select(o => (o.X, o.Y)).ToHashSet() : [];
        var start = (u.X, u.Y);
        var dist = new Dictionary<(int X, int Y), (int Cost, int Steps)> { [start] = (0, 0) };
        var prev = new Dictionary<(int X, int Y), (int X, int Y)>();
        var q = new PriorityQueue<(int X, int Y), int>();
        q.Enqueue(start, 0);
        ReadOnlySpan<(int, int)> dirs = [(0, 1), (-1, 0), (0, -1), (1, 0)];
        while (q.TryDequeue(out var c, out int cost))
        {
            if (dist[c].Cost != cost) continue;
            if (c != start && goal(c))
            {
                var path = new List<(int X, int Y)>();
                for (var p = c; p != start; p = prev[p]) path.Add(p);
                path.Reverse();
                return path;
            }
            int steps = dist[c].Steps;
            if (steps >= maxSteps) continue;
            foreach (var (dx, dy) in dirs)
            {
                var n = (c.X + dx, c.Y + dy);
                int step = Scene.MoveCost(n.Item1, n.Item2, costs);
                if (step < 0 || step >= 20) continue;
                if (foes.Contains(n) && !goal(n)) continue;
                int nc = cost + step;
                if (dist.TryGetValue(n, out var had) && had.Cost <= nc) continue;
                dist[n] = (nc, steps + 1);
                prev[n] = c;
                q.Enqueue(n, nc);
            }
        }
        return null;
    }

    /// <summary>
    /// 목표 칸 쪽으로(sub_14B78) — 상대를 피한 길을 내고, 안 되면 피하지 않는 길(28걸음)을 낸 뒤
    /// 이번 이동력으로 멈출 수 있는 칸 가운데 그 길 위에서 가장 먼 칸까지 간다. 한 칸도 못 나아가면 null.
    /// </summary>
    private AiPlan? WalkToward(Unit u, (int X, int Y) target)
    {
        if ((u.X, u.Y) == target) return null;
        var path = FindPath(u, c => c == target, block: true, 99) ?? FindPath(u, c => c == target, block: false, MaxSteps);
        if (path == null) return null;
        var reach = battle.Reach(u);
        var stops = Scene.Stops(reach, u).ToHashSet();
        for (int i = path.Count - 1; i >= 0; i--)
            if (stops.Contains(path[i]) && path[i] != (u.X, u.Y))
                return new AiPlan { Path = Scene.PathTo(reach, path[i]), Goal = target };
        return null;
    }

    /// <summary>다가가기(sub_14121) — 상대를 피해 28걸음 안에서 가장 가까운 상대의 곁 칸을 찾아 그쪽으로.</summary>
    private AiPlan? Approach(Unit u)
    {
        var foes = Opponents(u).Select(o => (o.X, o.Y)).ToHashSet();
        if (foes.Count == 0) return null;
        bool Beside((int X, int Y) c) => !foes.Contains(c) && (foes.Contains((c.X + 1, c.Y)) || foes.Contains((c.X - 1, c.Y))
                                                              || foes.Contains((c.X, c.Y + 1)) || foes.Contains((c.X, c.Y - 1)));
        var path = FindPath(u, Beside, block: true, MaxSteps);
        return path == null ? null : WalkToward(u, path[^1]);
    }

    /// <summary>곧장 쫓기(sub_13E9C) — 걸음 수(맨해튼)로 가장 가까운 상대의 칸 쪽으로.</summary>
    private AiPlan? Straight(Unit u)
    {
        var o = Opponents(u).OrderBy(o => Math.Abs(o.X - u.X) + Math.Abs(o.Y - u.Y)).FirstOrDefault();
        return o == null ? null : WalkToward(u, (o.X, o.Y));
    }

    /// <summary>사람 쫓기(갈래 3 · 9) — 캐릭터 번호 +53 인 사람(sub_12C60)의 칸 쪽으로. 없으면 null.</summary>
    private AiPlan? Chase(Unit u)
    {
        var who = Scene.Units.FirstOrDefault(o => o != u && o.Hp > 0 && o.MaxHp > 0 && o.NameId == u.AiX);
        return who == null ? null : WalkToward(u, (who.X, who.Y));
    }

    /// <summary>
    /// 도둑(갈래 5) — +61 번 상자 칸(타일 깃발 &amp; 0x60 = 0x20 이고 칸 표시가 그 번호, sub_15DF3)으로 걸어가 닿으면 훔친다.
    /// 상자가 이미 열렸거나 없으면 null(갈래 0 으로).
    /// </summary>
    private AiPlan? Thief(Unit u)
    {
        int k = u.Chest;
        if (battle.OpenedChests.Contains(k)) return null;
        for (int y = 0; y < Scene.Height; y++)
            for (int x = 0; x < Scene.Width; x++)
            {
                if ((Scene.TileFlags(x, y) & 0x60) != 0x20 || Scene.Mark(x, y) != k) continue;
                if ((u.X, u.Y) == (x, y)) return new AiPlan { StealChest = k, Goal = (x, y) };
                var walk = WalkToward(u, (x, y));
                if (walk != null) { walk.StealChest = k; walk.Goal = (x, y); }
                return walk;
            }
        return null;
    }

    /// <summary>달아남(갈래 7) — (+53, +54) 로 걸어가 닿으면 판에서 사라진다(sub_32975).</summary>
    private AiPlan Flee(Unit u)
    {
        var goal = (u.AiX, u.AiY);
        if ((u.X, u.Y) == goal) return new AiPlan { VanishAtGoal = true, Goal = goal };
        var walk = WalkToward(u, goal) ?? new AiPlan();
        walk.VanishAtGoal = true;
        walk.Goal = goal;
        return walk;
    }

    /// <summary>
    /// 상자 훔치기 — 장면 정보의 상자 k 의 (종류, 값)을 들고(+49 · +50), 상자를 연 것으로 하고, 갈래 7(달아남)로 바꾼다.
    /// 알림 글을 돌려준다.
    /// </summary>
    public Notice Steal(Unit u, int k)
    {
        var (kind, value) = k < Scene.Chests.Count ? Scene.Chests[k] : (255, 0);
        (u.DropKind, u.DropValue) = (kind, value);
        if (kind == 0 && u.Bag.Count < 8) u.Bag.Add((value, false));
        battle.OpenedChests.Add(k);
        u.AiByte = u.AiByte & 0xF0 | 7;
        return new Notice(" 이(가) 상자를 털었다", Battle.NameLine(u));
    }
}
