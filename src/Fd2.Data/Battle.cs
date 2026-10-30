namespace Fd2.Data;

/// <summary>원작 난수(sub_4EBE3) — 16비트 <c>seed = rol3(seed + 0x9014)</c>. 원작은 기다리는 동안에도 난수를 뽑아 사실상 시간에 따라 바뀌므로 시계로 씨를 준다.</summary>
public sealed class Fd2Random(int seed)
{
    private ushort _seed = (ushort)seed;

    /// <summary>0 ~ 65535.</summary>
    public int Next()
    {
        int v = (ushort)(_seed + 0x9014);
        _seed = (ushort)(v << 3 | v >> 13);
        return _seed;
    }

    /// <summary><c>난수 % n</c> — 원작처럼 나머지로(n 이 0 이하면 0).</summary>
    public int Next(int n) => n <= 0 ? 0 : Next() % n;
}

/// <summary>한 번 치기의 결과 — 맞았나, 필살(방어 반)인가, 피해, 독이 걸렸나(명중 판정 앞에 굴린다).</summary>
public sealed record StrikeResult(bool Hit, bool Critical, int Damage, bool Poisoned);

/// <summary>
/// 알림 한 줄 — Name 은 이름 표(FDTXT 0) 줄 번호(-1 없음)로, 원본 글꼴로 Text 앞에 그린다. Talk 는 보일 대사 묶음(-1 없음).
/// </summary>
public sealed record Notice(string Text, int Name = -1, int Talk = -1);

/// <summary>
/// 전투 규칙 — 분석-전투(FD2.EXE sub_1A30B · sub_1ECC7 · sub_2F7B6 · sub_1E292 · sub_1A866 · sub_205BE …)를 옮긴 것.
/// 표는 <see cref="GameTables"/>(FD2.EXE), 사람은 <see cref="Unit"/>, 지도는 <see cref="Scene"/>.
/// 화면 · 연출은 게임(GameWindow.Battle.cs)이 맡고, 여기는 수만 셈한다.
/// </summary>
public sealed partial class Battle
{
    public Scene Scene { get; }

    /// <summary>원작 표 — FD2.EXE 가 없으면 null(그때는 <see cref="SetupFallback"/> 의 기본값으로 돈다).</summary>
    public GameTables? Tables { get; }

    public Fd2Random Rng { get; }

    /// <summary>차례 수(`[0x53BEF]`, 전투 시작 때 1).</summary>
    public int Turn;

    /// <summary>돈(`dword_53BF3`) — 이 리메이크는 아직 저장이 없어 전투마다 0 부터.</summary>
    public int Gold;

    /// <summary>연 상자 번호(`dword_53AD5`) — 29장 승리 조건 · 도둑 AI 가 본다.</summary>
    public readonly HashSet<int> OpenedChests = new();

    public Battle(Scene scene, GameTables? tables, int? seed = null)
    {
        Scene = scene;
        Tables = tables;
        Rng = new Fd2Random(seed ?? Environment.TickCount);
    }

    // ── 사람 채우기 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 사람의 전투 칸을 원작 표로 채운다(sub_10C50 · sub_112A5). 동료 꼴(0~67)은 동료 표 + 레벨 × 성장표 최솟값,
    /// 몬스터(68~)는 레벨당 값 × 레벨. 장면 유닛 표에서 나왔으면 장비 · 물건 · 마법 · AI · 떨굼은 그 줄에서,
    /// 우리 편 출전자(줄 없음)는 동료 표의 합류 레벨 · 장비 · 가방 · 처음 마법.
    /// </summary>
    public void Setup(Unit u)
    {
        var t = Tables;
        if (t == null) { SetupFallback(u); return; }
        int f = u.FormId;
        var e = u.Entry;
        u.Bag.Clear();
        Array.Clear(u.Status);
        u.Acted = false;
        if (f < GameTables.Forms)
        {
            var h = t.Heroes[f];
            if (e == null) u.Level = Math.Max(1, h.Level);
            var st = t.HeroStats(f, u.Level);
            (u.MaxHp, u.MaxMp, u.BaseAtk, u.BaseDef, u.Speed) = st;
            (u.Race, u.Class, u.Move) = (h.Race, h.Class, h.Move);
            if (e == null)
            {
                if (h.Weapon != 255) u.Bag.Add((h.Weapon, true));
                if (h.Armor != 255) u.Bag.Add((h.Armor, true));
                foreach (int b in h.Bag.Where(b => b != 255)) u.Bag.Add((b, false));
                u.Spells = h.Spells;
            }
        }
        else if (f - GameTables.MonsterBase < t.Monsters.Count)
        {
            var m = t.Monsters[f - GameTables.MonsterBase];
            int l = u.Level;
            (u.MaxHp, u.MaxMp, u.BaseAtk, u.BaseDef, u.Speed) = (m.Hp * l, m.Mp * l, m.Str * l, m.Def * l, m.Spd * l);
            (u.Race, u.Class, u.Move) = (m.Race, m.Class, m.Move);
        }
        if (e != null)
        {
            if (e.Weapon != 255) u.Bag.Add((e.Weapon, true));
            if (e.Armor != 255) u.Bag.Add((e.Armor, true));
            foreach (int i in e.Items) if (u.Bag.Count < 8) u.Bag.Add((i, false));
            u.Spells = e.Spells;
            (u.AiByte, u.AiX, u.AiY, u.Chest) = (e.AiByte, e.AiX, e.AiY, e.Chest);
            (u.DropKind, u.DropValue) = (e.DropKind, e.DropValue);
        }
        u.Bag.RemoveAll(b => b.Item >= t.Items.Count);
        u.Exp = u.Team == 2 ? 0 : 255;
        u.Hp = u.MaxHp;
        u.Mp = u.MaxMp;
        Recompute(u);
    }

    /// <summary>
    /// FD2.EXE 가 없을 때(안드로이드에서 예전에 가져온 폴더 같은)만 쓰는 기본값 — 원작 규칙이 아니다.
    /// 몬스터 · 동료 모두 레벨에 비례한 흔한 값, 단검 하나.
    /// </summary>
    private static void SetupFallback(Unit u)
    {
        int l = Math.Max(1, u.Level);
        u.Bag.Clear();
        Array.Clear(u.Status);
        (u.MaxHp, u.MaxMp, u.BaseAtk, u.BaseDef, u.Speed, u.Move) = (30 + 8 * l, 0, 8 + 6 * l, 3 + 4 * l, 2 * l, 4);
        u.Hp = u.MaxHp;
        u.Atk = u.BaseAtk + 10;
        u.Def = u.BaseDef;
        u.Hit = u.Speed + 95;
        u.Evade = u.Speed;
        u.RangeMin = u.RangeMax = 1;
        u.Class = 1;
        u.Exp = u.Team == 2 ? 0 : 255;
        u.Acted = false;
    }

    /// <summary>
    /// 셈한 능력(sub_1B750) — 공격 = 역량 + 장비 공격(공격↑ 이면 ×1.15), 방어 = 내력 + 장비 방어(방어↑ 면 ×1.15),
    /// 명중 = 속도 + 장비 명중, 회피 = 속도 + 장비 회피(속도↑ 면 둘 다 +15). 사거리는 무기에서.
    /// </summary>
    public void Recompute(Unit u)
    {
        if (Tables == null) return;
        int atk = u.BaseAtk, def = u.BaseDef, hit = u.Speed, eva = u.Speed;
        foreach (var (id, on) in u.Bag)
        {
            if (!on) continue;
            var it = Tables.Items[id];
            atk += it.Atk;
            def += it.Def;
            hit += it.Hit;
            eva += it.Evade;
        }
        if (u.Status[0] > 0) atk = (int)(atk * 1.15);   // dbl_5018D
        if (u.Status[1] > 0) def = (int)(def * 1.15);
        if (u.Status[2] > 0) { hit += 15; eva += 15; }
        (u.Atk, u.Def, u.Hit, u.Evade) = (atk, def, hit, eva);
        var w = Weapon(u);
        (u.RangeMin, u.RangeMax) = w == null ? (0, 0) : (w.RangeMin, w.RangeMax);
    }

    /// <summary>무기 — 가방에서 장비 중인 첫 번호 &lt; 128(sub_1B83D(u, 0)). 없으면 null(공격 · 반격을 못 한다).</summary>
    public ItemInfo? Weapon(Unit u)
    {
        if (Tables == null) return u.RangeMax > 0 ? FallbackDagger : null;
        foreach (var (id, on) in u.Bag) if (on && id < 128) return Tables.Items[id];
        return null;
    }

    private static readonly ItemInfo FallbackDagger = new(1, 10, 95, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 50);

    public ClassInfo? ClassOf(Unit u) => Tables != null && u.Class >= 0 && u.Class < Tables.Classes.Count ? Tables.Classes[u.Class] : null;

    /// <summary>
    /// 나는 유닛(sub_1F183) — 꼴 28(다커스)이 아니고, 직업 19(용기사)이거나 종족 4 · 5. 이동비 표 19 를 쓰고, 지형 공 · 방 보정을 안 받고,
    /// 땅 마법(10~12)이 안 든다. 종족 4 · 5 의 이름(용인 · 마족)은 글 표에서 읽은 것.
    /// </summary>
    public static bool IsFlyer(Unit u) => u.FormId != 28 && (u.Class == 19 || u.Race is 4 or 5);

    /// <summary>이동비 줄 — 나는 유닛은 19, 꼴 28(다커스)은 16, 그 밖은 직업 줄(sub_18890 · sub_14B78).</summary>
    public int[] MoveCosts(Unit u)
    {
        if (Tables == null) return Scene.DefaultMoveCost;
        int row = IsFlyer(u) ? 19 : u.FormId == 28 ? 16 : u.Class;
        return row >= 0 && row < Tables.Classes.Count ? Tables.Classes[row].MoveCost : Scene.DefaultMoveCost;
    }

    /// <summary>이동 범위 — 직업 이동비 · 상대 편 곁 멈춤(ZOC) · 같은 편 지나가기(분석-전투 3절).</summary>
    public Dictionary<(int X, int Y), (int X, int Y)> Reach(Unit u) => Scene.Reach(u, MoveCosts(u), zoc: true);

    public static int Dist(Unit a, Unit b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>
    /// 사거리 꼴(sub_14818) — 최대 &lt; 16 이면 걸음 수(마름모) 최소 ~ 최대, 16 이상이면 같은 줄 · 같은 칸줄의 (최대 − 16) 칸 십자.
    /// </summary>
    public static bool InArea(int dx, int dy, int max, int min)
    {
        int d = Math.Abs(dx) + Math.Abs(dy);
        if (max < 16) return d >= min && d <= max;
        return (dx == 0 || dy == 0) && d >= min && d <= max - 16;
    }

    /// <summary>a 가 from 칸에서 d 를 무기로 칠 수 있나.</summary>
    public bool CanStrike(Unit a, (int X, int Y) from, Unit d) =>
        Weapon(a) != null && InArea(d.X - from.X, d.Y - from.Y, a.RangeMax, a.RangeMin);

    /// <summary>
    /// 되받아치기(sub_1F0DC) — 맞은 쪽 d 가 살았고, 마비가 아니고, 바로 옆(거리 1)이고, 무기의 최소 사거리가 1 이면 한 번(그 안에서 두 번 치기 판정).
    /// </summary>
    public bool CanCounter(Unit d, Unit a) =>
        d.Hp > 0 && a.Hp > 0 && !d.Paralyzed && Dist(d, a) == 1 && Weapon(d) is { RangeMin: 1 };

    /// <summary>
    /// 칠 횟수 — 1, 난수 % 100 &lt; 3 이면 2, 무기 특수 3 이면 하나 더(전투 화면 sub_2F7B6 식 — 겹치면 3번). 상대가 쓰러지면 그 자리에서 멈춘다.
    /// </summary>
    public int StrikeCount(Unit a)
    {
        int n = Rng.Next(100) < 3 ? 2 : 1;
        if (Weapon(a) is { Special: 3 }) n++;
        return n;
    }

    /// <summary>지형 종류(0~5) 의 공격 · 방어 %. 표 밖이면 0.</summary>
    private (int Atk, int Def) TerrainPercent(Unit u)
    {
        if (Tables == null) return (0, 0);
        int g = Scene.Ground(u.X, u.Y);
        return g >= 0 && g < 6 ? (Tables.TerrainAttack[g], Tables.TerrainDefense[g]) : (0, 0);
    }

    /// <summary>
    /// 한 번 치기(sub_1ECC7 · sub_2F7B6) — d 의 HP 를 깎는다.
    /// <code>
    /// 공 = a 공격(+72) + 공 × a 칸 지형 공격 % / 100 (나는 유닛 빼고), 방 = d 방어(+74) + 방 × d 칸 지형 방어 %
    /// 무기 특수 2: 난수 % 100 &lt; 확률 이면 d 독 = 난수 % 4 + 2   ← 명중 판정 앞(빗나가도 걸림)
    /// 난수 % 100 &lt; a 명중 − d 회피 이면 맞음:
    ///     난수 % 100 &lt; 직업 필살(+ 무기 특수 4 값) 이면 방 /= 2
    ///     피해 = 9 × (공 − 방) / 10 (0 아래면 0), 피해 += 난수 % (피해 / 9)
    /// </code>
    /// 최소 피해 같은 것은 없다 — 방어가 높으면 0.
    /// </summary>
    public StrikeResult Strike(Unit a, Unit d)
    {
        int atk = a.Atk, def = d.Def;
        if (!IsFlyer(a)) atk += atk * TerrainPercent(a).Atk / 100;
        if (!IsFlyer(d)) def += def * TerrainPercent(d).Def / 100;
        var w = Weapon(a);
        int crit = ClassOf(a)?.Crit ?? 0;
        if (w is { Special: 4 }) crit += w.SpecialValue;
        bool poisoned = false;
        if (w is { Special: 2 } && Rng.Next(100) < w.SpecialValue)
        {
            d.Status[3] = Rng.Next(4) + 2;
            poisoned = true;
        }
        if (Rng.Next(100) >= a.Hit - d.Evade) return new StrikeResult(false, false, 0, poisoned);
        bool critical = Rng.Next(100) < crit;
        if (critical) def /= 2;
        int dmg = 9 * (atk - def) / 10;
        if (dmg < 0) dmg = 0;
        if (dmg / 9 > 0) dmg += Rng.Next(dmg / 9);
        d.Hp = Math.Max(0, d.Hp - dmg);
        return new StrikeResult(true, critical, dmg, poisoned);
    }

    // ── 경험치 · 레벨업 ──────────────────────────────────────────────────────

    /// <summary>나눔 레벨 — 내 레벨 + (전직 직업 9~24 이거나 이름 28(다커스)이면 30).</summary>
    public static int ExpLevel(Unit u) => u.Level + (u.Class is >= 9 and <= 24 || u.NameId == 28 ? 30 : 0);

    /// <summary>
    /// 한 번 쳐서 얻는 경험(sub_1ECC7 끝) — 우리 편(편 2)이 몬스터 표 캐릭터(68~)를 칠 때만.
    /// <c>E = 대상 Lv × 몬스터 경험 / 나눔 레벨</c>, 대상이 살았으면 <c>E × 피해 / 대상 최대 HP</c>. 원작은 대입이라 마지막 한 대 몫만 남는다.
    /// </summary>
    public int AttackExp(Unit a, Unit d, int damage)
    {
        if (Tables == null || a.Team != 2 || d.FormId < GameTables.MonsterBase || d.FormId - GameTables.MonsterBase >= Tables.Monsters.Count) return 0;
        int e = d.Level * Tables.Monsters[d.FormId - GameTables.MonsterBase].Exp / Math.Max(1, ExpLevel(a));
        if (d.Hp > 0) e = damage * e / Math.Max(1, d.MaxHp);
        return e;
    }

    /// <summary>최대 레벨 — 꼴 30 · 31(카일 · 워터)은 110, 나머지 50.</summary>
    public static int MaxLevel(Unit u) => u.FormId is 30 or 31 ? 110 : 50;

    /// <summary>
    /// 경험 e 를 받는다(sub_1E292) — 합이 100 이 넘는 동안 레벨 +1 · 성장 · 100 빼기(최대 레벨에 닿으면 합 0).
    /// 쓰러졌거나 이미 최대 레벨이거나 e 가 0 이면 받지 않는다. 오른 것을 알림으로.
    /// </summary>
    public List<Notice> GainExp(Unit u, int e)
    {
        var notes = new List<Notice>();
        if (Tables == null || e <= 0 || u.Hp <= 0 || u.Team != 2 || u.Level >= MaxLevel(u)) return notes;
        int sum = u.Exp + e;
        while (sum >= 100)
        {
            u.Level++;
            notes.Add(new Notice($" 등급상승! Lv {u.Level}", NameLine(u)));
            notes.AddRange(LevelUp(u));
            sum -= 100;
            if (u.Level >= MaxLevel(u)) { sum = 0; break; }
        }
        u.Exp = sum;
        return notes;
    }

    /// <summary>
    /// 레벨이 하나 오를 때(sub_1E529) — 역량 · 내력 · 속도 · 최대 HP · 최대 MP 에 각각 <c>최소 + 난수 % (최대 − 최소)</c>
    /// (최대 = 최소면 그 값). 지금 HP · MP 는 안 오른다. 성장표 [10] 의 배움 표에 새 레벨이 있으면 그 마법을 배운다.
    /// </summary>
    private List<Notice> LevelUp(Unit u)
    {
        var notes = new List<Notice>();
        if (Tables == null || u.FormId >= GameTables.Forms) return notes;
        var g = Tables.Growths[u.FormId];
        int Roll(int min, int max) => max > min ? min + Rng.Next(max - min) : min;
        int a = Roll(g.StrMin, g.StrMax), d = Roll(g.DefMin, g.DefMax), s = Roll(g.SpdMin, g.SpdMax);
        int hp = Roll(g.HpMin, g.HpMax), mp = Roll(g.MpMin, g.MpMax);
        u.BaseAtk += a;
        u.BaseDef += d;
        u.Speed += s;
        u.MaxHp += hp;
        u.MaxMp += mp;
        var parts = new List<string>();
        if (a > 0) parts.Add($"역량 +{a}");
        if (d > 0) parts.Add($"내력 +{d}");
        if (s > 0) parts.Add($"속도 +{s}");
        if (hp > 0) parts.Add($"MHP +{hp}");
        if (mp > 0) parts.Add($"MMP +{mp}");
        if (parts.Count > 0) notes.Add(new Notice(string.Join("  ", parts)));
        if (g.SpellList < Tables.SpellLearn.Count)
            foreach (var (level, spell) in Tables.SpellLearn[g.SpellList])
                if (level == u.Level && spell < 40)
                {
                    u.Spells |= 1UL << spell;
                    notes.Add(new Notice($" {SpellNames.Of(spell)} 습득", NameLine(u)));
                }
        Recompute(u);
        return notes;
    }

    // ── 쉬기 · 상태 ─────────────────────────────────────────────────────────

    /// <summary>쉬기(sub_13FD4) — HP 가 덜 찼고 독 · 마비가 아니면 최대 HP 의 1/5 를 채운다. 채운 양.</summary>
    public int Rest(Unit u)
    {
        if (u.Hp <= 0 || u.Hp >= u.MaxHp || u.Poisoned || u.Paralyzed) return 0;
        int before = u.Hp;
        u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp / 5);
        return u.Hp - before;
    }

    /// <summary>상태 이름 — 풀릴 때 글 481 + 칸.</summary>
    public static readonly string[] StatusNames = ["공격력 증가", "방어력 증가", "속도 증가", "독", "마비", "봉쇄"];

    /// <summary>
    /// 그 편 차례가 시작될 때(sub_1A866) — 독이면 최대 HP / 10 을 깎고(0 까지, 쓰러질 수 있다), 상태 여섯 칸을 1 씩 줄여
    /// 0 이 되면 풀림 글 · 능력 다시 셈. died 에 이번에 쓰러진 사람.
    /// </summary>
    public List<Notice> StatusTick(int team, List<Unit> died)
    {
        var notes = new List<Notice>();
        var mine = Scene.Units.Where(u => u.Team == team && u.MaxHp > 0 && u.Hp > 0).ToList();
        foreach (var u in mine.Where(u => u.Poisoned))
        {
            int n = Math.Min(u.Hp, u.MaxHp / 10);
            u.Hp -= n;
            notes.Add(new Notice($" 독성발작, HP {n} 감소", NameLine(u)));
            if (u.Hp <= 0) died.Add(u);
        }
        foreach (var u in mine.Where(u => u.Hp > 0))
        {
            bool changed = false;
            for (int k = 0; k < 6; k++)
                if (u.Status[k] > 0 && --u.Status[k] == 0)
                {
                    notes.Add(new Notice($" {StatusNames[k]} 풀림", NameLine(u)));
                    changed = true;
                }
            if (changed) Recompute(u);
        }
        return notes;
    }

    // ── 쓰러짐 · 떨굼 ────────────────────────────────────────────────────────

    /// <summary>
    /// 이번에 쓰러진 사람들의 떨굼(sub_1B6B7 → sub_1AA1D) — 0 물건 · 1 돈은 죽인 사람이 우리 편(편 2)일 때만 받고,
    /// 그렇지 않으면 그 자리에서 나머지 처리를 통째로 멈춘다(원작 그대로). 3 은 대사 묶음. 2(사건 함수)는 원작 코드라 옮기지 못했다.
    /// 짐이 가득이면 원작은 버릴 물건을 고르게 하지만 여기서는 받지 못한 것으로 둔다.
    /// </summary>
    public List<Notice> Drops(IEnumerable<Unit> died, Unit? killer)
    {
        var notes = new List<Notice>();
        foreach (var u in died)
        {
            int kind = u.DropKind, value = u.DropValue;
            if (kind == 255) continue;
            if (kind is 0 or 1 && killer?.Team != 2) break;
            u.DropKind = 255;
            switch (kind)
            {
                case 0 when killer!.Bag.Count < 8:
                    killer.Bag.Add((value, false));
                    notes.Add(new Notice(" 획득", 181 + value));
                    break;
                case 0:
                    notes.Add(new Notice(" — 짐이 가득해 받지 못함", 181 + value));
                    break;
                case 1:
                    Gold += value;
                    notes.Add(new Notice($"돈 {value} 획득"));
                    break;
                case 3:
                    notes.Add(new Notice("", -1, value));
                    break;
            }
        }
        return notes;
    }

    /// <summary>이름 표(FDTXT 0) 의 그 사람 이름 줄 — 캐릭터 번호 + 1.</summary>
    public static int NameLine(Unit u) => u.NameId + 1;

    // ── 이기고 짐 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 이기고 짐(`[0x53ECC]`: 0 계속 · 1 짐 · 2 이김). 기본 sub_205BE: 살아 있는 편 0 이 없으면 이김, 배열 0 번(사울)이 쓰러지면 짐.
    /// 판마다 함수 표 0x51B19[판](30칸)이 지킬 사람 · 이길 표적을 더한다 — 번호는 전투 배열 번호(<see cref="Unit.Slot"/>).
    /// </summary>
    public int Judge()
    {
        bool Dead(int slot) => Scene.Units.Any(u => u.Slot == slot && u.MaxHp > 0 && u.Hp <= 0);
        bool AllDead(int from, int to) => Enumerable.Range(from, to - from + 1).All(Dead);
        bool AnyDead(params int[] slots) => slots.Any(Dead);
        int Base()
        {
            int r = Scene.Units.Any(u => u.Team == 0 && u.MaxHp > 0 && u.Hp > 0) ? 0 : 2;
            return Dead(0) ? 1 : r;
        }
        int r;
        switch (Scene.Index)
        {
            case 1: r = Base(); if (AllDead(5, 10)) r = 1; break;                                   // 마을 사람 여섯
            case 9: r = Base(); if (AnyDead(50, 51)) r = 1; break;                                   // 소피아 · 카나은 3세
            case 11: r = Base(); if (Dead(14)) r = 1; break;                                         // 미아스튜터
            case 12: r = Base(); if (AllDead(15, 26) || Turn > 5 && Dead(59)) r = 1; break;          // 요정 열둘
            case 14: r = Base(); if (Dead(64)) r = 1; break;                                         // 사이클린
            case 15: r = Base(); if (Dead(65)) r = 1; break;                                         // 미티
            case 16: r = Base(); if (!Scene.Units.Any(u => u.Team == 2 && u.NameId == 18) && Dead(52)) r = 1; break;
            case 17: r = Dead(52) ? 2 : 0; if (AnyDead(0, 16, 17)) r = 1; break;                     // 암흑기사를 쓰러뜨리면 이김
            case 18: r = Base(); if (Turn > 6 && Dead(64)) r = 1; break;                             // 파나로시아
            case 19:
                r = Base();
                if (AllDead(36, 51) && AllDead(61, 82)) r = 2;                                       // 늪 괴물 빼고 다
                if (AllDead(53, 60) || AnyDead(0, 52)) r = 1;
                break;
            case 20: r = Base(); if (AnyDead(16, 17)) r = 1; break;                                  // 롤렌 · 히올프
            case 21 or 26 or 27: r = Base(); if (Dead(1)) r = 1; break;
            case 22: r = Dead(18) ? 2 : 0; if (AnyDead(0, 1, 16, 17)) r = 1; break;                  // 기갑대장
            case 24: r = Base(); if (Dead(16)) r = 1; break;                                         // 샌디라스
            case 25: r = Base(); if (AnyDead(1, 2)) r = 1; break;                                    // 유니 · 아치메치
            case 28: r = OpenedChests.IsSupersetOf([18, 19, 20]) ? 2 : 0; if (AnyDead(0, 1)) r = 1; break;   // 방어 장치 셋(사건이 켬)
            case 29: r = Dead(20) ? 2 : 0; if (AnyDead(0, 1)) r = 1; break;                          // 공마신
            // 0(1장)은 표 칸이 비어 있다 — 원작은 사건이 끝을 정하는 듯하나 못 따라가 기본 판정을 쓴다(짐작)
            default: r = Base(); break;
        }
        return r;
    }
}
