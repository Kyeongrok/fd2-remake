namespace Fd2.Data;

/// <summary>마법 한 대상에 든 결과 — 맞았나, 피해(Heal 이면 채운 양), 상태를 걸었나 · 풀었나의 글.</summary>
public sealed record SpellResult(Unit Target, bool Hit, int Amount, bool Heal, string? Note = null);

/// <summary>
/// 마법(분석-마법) — 표 0x619FD(세기 · 명중 · 사거리 · 범위 · MP · 대상 편), 아는 마법 비트, 공격 · 회복 · 올리기 · 걸기 · 풀기 · 기술.
/// 공격 마법 피해는 쓰는 사람 능력과 상관없이 <c>세기 × 직업 배율[맞는 쪽] / 10</c> 의 90 ~ 99.9%.
/// </summary>
public sealed partial class Battle
{
    public const int SpellCount = GameTables.SpellCount;

    /// <summary>원작이 쓰는 사람 쪽에서 안 다루는 마법 — 23 전송술(대상과 갈 칸을 두 번 고른다)은 아직 고르기 화면이 없어 뺐다.</summary>
    public static bool Unsupported(int spell) => spell == 23;

    /// <summary>아는 마법(sub_1C269) — 비트 차례대로.</summary>
    public IEnumerable<int> KnownSpells(Unit u)
    {
        for (int m = 0; m < SpellCount; m++)
            if ((u.Spells >> m & 1) != 0) yield return m;
    }

    public SpellInfo? SpellOf(int spell) => Tables != null && spell >= 0 && spell < Tables.Spells.Count ? Tables.Spells[spell] : null;

    /// <summary>지금 쓸 수 있나 — 봉쇄가 아니고 MP 가 남았나.</summary>
    public bool CanCast(Unit u, int spell) => !u.Sealed && SpellOf(spell) is { } s && u.Mp >= s.Mp && !Unsupported(spell);

    /// <summary>
    /// 마법을 걸 가운데 칸 — 쓰는 사람 칸에서 걸음 수(지형 안 따짐) ≤ 사거리. 사거리 0 이면 자기 칸, 16 이상이면 십자 한 줄(사거리 − 16).
    /// </summary>
    public List<(int X, int Y)> SpellCenters(Unit u, int spell, (int X, int Y)? from = null)
    {
        var (fx, fy) = from ?? (u.X, u.Y);
        var list = new List<(int X, int Y)>();
        if (SpellOf(spell) is not { } s) return list;
        int r = s.Range < 16 ? s.Range : s.Range - 16;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (s.Range >= 16 ? dx != 0 && dy != 0 : Math.Abs(dx) + Math.Abs(dy) > r) continue;
                if (Scene.Inside(fx + dx, fy + dy)) list.Add((fx + dx, fy + dy));
            }
        return list;
    }

    /// <summary>
    /// 마법이 드는 사람(sub_14818 · sub_149F8) — 가운데 칸 둘레 범위(마름모, 16 이상은 십자)에서 대상 편(표 +6)의 산 사람.
    /// 편 0 은 쓰는 사람의 상대, 1 은 쓰는 사람 편, 3 은 우리 편(편 2)만. 30 암속인은 쓰는 사람에서 고른 쪽으로 한 줄 4칸.
    /// </summary>
    public List<Unit> SpellTargets(Unit caster, int spell, (int X, int Y) center)
    {
        if (SpellOf(spell) is not { } s) return [];
        bool Side(Unit o) => s.Side switch
        {
            0 => o.Team == 0 != (caster.Team == 0),
            3 => o.Team == 2,
            _ => o.Team == 0 == (caster.Team == 0),
        };
        var living = Scene.Units.Where(o => o.Hp > 0 && o.MaxHp > 0 && Side(o));
        if (spell == 30)
        {
            int sx = Math.Sign(center.X - caster.X), sy = Math.Sign(center.Y - caster.Y);
            if (sx != 0 && sy != 0 || sx == 0 && sy == 0) return [];
            var line = Enumerable.Range(1, s.Range - 16).Select(k => (caster.X + sx * k, caster.Y + sy * k)).ToHashSet();
            return living.Where(o => line.Contains((o.X, o.Y))).ToList();
        }
        return living.Where(o => InArea(o.X - center.X, o.Y - center.Y, s.Area, 0)).ToList();
    }

    /// <summary>공격 마법 피해 · 회복량의 굴림(sub_1C81F · sub_1C916) — 0.9a + a × (난수 % 100) / 1000, 곧 a 의 90 ~ 99.9%.</summary>
    private int MagicRoll(int a) => a <= 0 ? 0 : 9 * a / 10 + a * Rng.Next(100) / 1000;

    /// <summary>
    /// 마법 하나를 한 사람에게(MP 는 <see cref="PayMp"/> 로 따로) — 마법 번호마다:
    /// 0~12 · 32 공격(명중 굴림, 10~12 는 나는 유닛 빗나감, 피해 = 세기 × 배율 / 10 의 90~99.9%), 9 주술도 같은 길,
    /// 13~16 회복 · 33 풍요정(950), 17~19 올리기 · 34 셋 다(이미 걸렸으면 빗나감, 2~5 차례),
    /// 20 · 21 독 · 마비 풀기(+ HP 10), 22 · 26 · 27 · 35 걸기(이미 걸렸거나 직업 25 · 26 이면 빗나감, 늘 50% · 피해 10 굴림),
    /// 24 · 28~31 기술(배수 × 공격력 / 10 − 방어력), 25 행동술(이미 움직인 사람을 다시).
    /// </summary>
    public SpellResult CastOn(Unit caster, int spell, Unit target)
    {
        if (SpellOf(spell) is not { } s) return new SpellResult(target, false, 0, false);
        switch (spell)
        {
            case <= 12 or 32:
            {
                if (spell is >= 10 and <= 12 && IsFlyer(target)) return new SpellResult(target, false, 0, false);
                if (Rng.Next(100) >= s.Hit) return new SpellResult(target, false, 0, false);
                int mul = ClassOf(target) is { } c && target.Class > 0 ? c.MagicMul : 10;
                int dmg = MagicRoll(mul * s.Power / 10);
                target.Hp = Math.Max(0, target.Hp - dmg);
                return new SpellResult(target, true, dmg, false);
            }
            case >= 13 and <= 16 or 33:
                return HealBy(target, spell == 33 ? 950 : s.Power);
            case >= 17 and <= 19:
                return Raise(target, spell - 17);
            case 34:
            {
                var r = new[] { Raise(target, 0), Raise(target, 1), Raise(target, 2) };
                return new SpellResult(target, r.Any(x => x.Hit), 0, false, r.Any(x => x.Hit) ? " 공격 · 방어 · 속도 증가" : null);
            }
            case 20 or 21:
            {
                int k = spell == 20 ? 3 : 4;
                if (target.Status[k] == 0) return new SpellResult(target, false, 0, false);
                target.Status[k] = 0;
                int before = target.Hp;
                target.Hp = Math.Min(target.MaxHp, target.Hp + 10);
                return new SpellResult(target, true, target.Hp - before, true, $" {StatusNames[k]} 풀림");
            }
            case 22: return Inflict(target, 5);
            case 26: return Inflict(target, 3);
            case 27: return Inflict(target, 4);
            case 35:
            {
                var r = new[] { Inflict(target, 3), Inflict(target, 5), Inflict(target, 4) };
                return new SpellResult(target, r.Any(x => x.Hit), r.Sum(x => x.Amount), false,
                                       string.Concat(r.Where(x => x.Note != null).Select(x => x.Note)));
            }
            case 24 or >= 28 and <= 31:
            {
                int mult = spell switch { 24 => 15, 28 => 20, 29 => 13, _ => 18 };
                // 배수 × 공격력 / 10 − 방어력 → sub_1C81F. 0 아래는 0 으로 둔다(원작이 음수를 어떻게 다루는지는 안 봤다)
                int dmg = MagicRoll(Math.Max(0, mult * caster.Atk / 10 - target.Def));
                target.Hp = Math.Max(0, target.Hp - dmg);
                return new SpellResult(target, true, dmg, false);
            }
            case 25:
                if (!target.Acted) return new SpellResult(target, false, 0, false);
                target.Acted = false;
                return new SpellResult(target, true, 0, false, " 다시 움직일 수 있음");
            default:
                return new SpellResult(target, false, 0, false);
        }
    }

    /// <summary>MP 를 뺀다(sub_1CA89). 공격술(17)은 원작이 방어술(18) 값을 빼는 버그가 있지만 둘 다 5 라 같다.</summary>
    public void PayMp(Unit caster, int spell)
    {
        if (SpellOf(spell == 17 ? 18 : spell) is { } s) caster.Mp = Math.Max(0, caster.Mp - s.Mp);
    }

    /// <summary>HP 를 p 의 90 ~ 99.9% 채운다(sub_1C916, 늘 듦). 물건 회복도 이 길.</summary>
    public SpellResult HealBy(Unit target, int p)
    {
        int before = target.Hp;
        target.Hp = Math.Min(target.MaxHp, target.Hp + MagicRoll(p));
        return new SpellResult(target, true, target.Hp - before, true);
    }

    /// <summary>올리기(sub_22721 · sub_22866 · sub_22997) — 이미 걸렸으면 빗나감, 아니면 2~5 차례, 능력 다시 셈.</summary>
    private SpellResult Raise(Unit target, int k)
    {
        if (target.Status[k] > 0) return new SpellResult(target, false, 0, false);
        target.Status[k] = Rng.Next(4) + 2;
        Recompute(target);
        return new SpellResult(target, true, 0, false, $" {StatusNames[k]}");
    }

    /// <summary>걸기(sub_22D1B) — 이미 걸렸거나 직업 25 · 26 이면 빗나감, 아니면 50% 로 맞아 피해 10 굴림 · 2~5 차례. 표의 명중은 이 길에서 안 쓴다.</summary>
    private SpellResult Inflict(Unit target, int k)
    {
        if (target.Status[k] > 0 || target.Class is 25 or 26 || Rng.Next(100) >= 50) return new SpellResult(target, false, 0, false);
        int dmg = MagicRoll(10);
        target.Hp = Math.Max(0, target.Hp - dmg);
        target.Status[k] = Rng.Next(4) + 2;
        return new SpellResult(target, true, dmg, false, $" {StatusNames[k]}에 걸림");
    }

    /// <summary>
    /// 마법 경험(sub_18D8C 마법 길) — 우리 편(편 2)이 쓸 때만. 대상마다 더한 뒤 <c>쓰는 사람 Lv + (직업 &gt; 8 이면 30)</c> 으로 나눈다.
    /// 공격 · 기술: 몬스터(68~)만 <c>대상 Lv × 몬스터 경험</c>(살았으면 × 피해 / 최대 HP), 회복: <c>채운 양 × 40 × 대상 Lv′ / 최대 HP</c>(대상 꼴 &lt; 75),
    /// 올리기 2 × Lv′, 걸기 8 × Lv, 풀기 4 × Lv′, 행동술 8 × Lv′. Lv′ = 레벨 + (직업 9~24 면 30).
    /// </summary>
    public int SpellExp(Unit caster, int spell, IEnumerable<SpellResult> results)
    {
        if (caster.Team != 2 || Tables == null) return 0;
        int sum = 0;
        foreach (var r in results.Where(r => r.Hit))
        {
            var t = r.Target;
            int lv = t.Level + (t.Class is >= 9 and <= 24 ? 30 : 0);
            sum += spell switch
            {
                <= 12 or 32 or 24 or >= 28 and <= 31 when t.FormId >= GameTables.MonsterBase && t.FormId - GameTables.MonsterBase < Tables.Monsters.Count
                    => t.Hp > 0 ? r.Amount * (t.Level * Tables.Monsters[t.FormId - GameTables.MonsterBase].Exp) / Math.Max(1, t.MaxHp)
                                : t.Level * Tables.Monsters[t.FormId - GameTables.MonsterBase].Exp,
                >= 13 and <= 16 or 33 when t.FormId < 75 => r.Amount * 40 * lv / Math.Max(1, t.MaxHp),
                >= 17 and <= 19 or 34 => 2 * lv,
                22 or 26 or 27 or 35 => 8 * t.Level,
                20 or 21 => 4 * lv,
                25 => 8 * lv,
                _ => 0,
            };
        }
        return sum / Math.Max(1, caster.Level + (caster.Class > 8 ? 30 : 0));
    }
}
