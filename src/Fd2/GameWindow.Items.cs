using Fd2.Data;

namespace Fd2;

/// <summary>
/// 우리 편 물건(sub_1BBDC · sub_20C6F, 분석-아이템 §3) — 걸은 뒤 명령 단추의 오른쪽 「물건」: 가방에서 골라 쓰기 · 장비 · 버리기.
/// 쓰기는 물건의 쓰는 거리 · 편 · 범위로 가운데 칸을 고른다. 회복(5 · 13) · 마법 물건(14 · 20 · 21 · 22 · 24)은 마법 · 회복과 같은 길로,
/// 해독 · 마비 풀기 · 능력 영구 오름 · MP · 올리기 술은 그 자리에서. 5 · 6 · 7 · 8 ~ 11 · 17 ~ 19 는 쓰면 없어진다. 전송(23)은 아직.
/// </summary>
internal sealed partial class GameWindow
{
    private int _pendingItem = -1;
    private MenuStage _itemFrom = MenuStage.AfterMove;

    private static bool UsableItem(ItemInfo it) => it.Use is >= 5 and <= 22 and not 23 or 24;

    private static bool ConsumedItem(int use) => use is 5 or 6 or 7 or (>= 8 and <= 11) or (>= 17 and <= 19);

    /// <summary>가방 목록 — 줄마다 원본 글꼴 이름, 장비 중이면 표시.</summary>
    private void OpenItemMenu(Unit u, MenuStage from)
    {
        _stage = MenuStage.Item;
        _menu.Clear();
        _menuGlyphs.Clear();
        for (int i = 0; i < u.Bag.Count; i++)
        {
            int slot = i;
            _menuGlyphs[_menu.Count] = 181 + u.Bag[i].Item;
            _menu.Add((u.Bag[i].Equipped ? "(장비) " : "", () => OpenItemActions(u, slot, from), default));
        }
        _menu.Add(("돌아가기", () => { _menuGlyphs.Clear(); BackTo(from); }, default));
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    private void OpenItemActions(Unit u, int slot, MenuStage from)
    {
        if (_tables == null || slot >= u.Bag.Count) return;
        var (id, equipped) = u.Bag[slot];
        var it = _tables.Items[id];
        _menu.Clear();
        _menuGlyphs.Clear();
        _menuGlyphs[0] = 181 + id;
        _menu.Add(("", () => { }, default));
        if (UsableItem(it) && ItemCenters(u, it).Count > 0) _menu.Add(("쓰기", () => BeginItemTarget(u, slot, from), default));
        if (it.Kind < 32 && !equipped && CanEquip(u, it))
            _menu.Add(("장비", () => { Equip(u, slot); OpenItemMenu(u, from); }, default));
        if (!equipped) _menu.Add(("버리기", () => { u.Bag.RemoveAt(slot); _battle.Recompute(u); OpenItemMenu(u, from); }, default));
        _menu.Add(("돌아가기", () => OpenItemMenu(u, from), default));
        _menuIndex = Math.Min(1, _menu.Count - 1);
        _bs = BattleState.Menu;
    }

    private bool CanEquip(Unit u, ItemInfo it) => _battle.ClassOf(u) is not { } c || c.EquipKinds.Contains(it.Kind);

    /// <summary>장비 — 같은 쪽(무기 &lt; 128 · 옷 128~)에 끼던 것을 풀고 낀다.</summary>
    private void Equip(Unit u, int slot)
    {
        bool weapon = u.Bag[slot].Item < 128;
        for (int i = 0; i < u.Bag.Count; i++)
            if (u.Bag[i].Equipped && u.Bag[i].Item < 128 == weapon) u.Bag[i] = (u.Bag[i].Item, false);
        u.Bag[slot] = (u.Bag[slot].Item, true);
        _battle.Recompute(u);
    }

    /// <summary>쓸 수 있는 가운데 칸 — 범위 안에 대상이 하나라도 있는 칸.</summary>
    private List<(int X, int Y)> ItemCenters(Unit u, ItemInfo it)
    {
        var ai = new BattleAi(_battle);
        return ai.ItemCenters(u, it).Where(c => ai.ItemTargets(u, it, c).Count > 0).ToList();
    }

    private void BeginItemTarget(Unit u, int slot, MenuStage from)
    {
        if (_tables == null) return;
        _menuGlyphs.Clear();
        var cells = ItemCenters(u, _tables.Items[u.Bag[slot].Item]);
        BeginTarget(u, cells, -1, from);
        _pendingItem = slot;
    }

    /// <summary>
    /// 우리 편이 물건을 쓴다 — 회복 · 마법 물건은 컴퓨터와 같은 <see cref="UseItem"/>, 그 밖은 그 자리에서 대상마다 효과와 알림.
    /// </summary>
    private void UsePlayerItem(Unit u, int slot, (int X, int Y) center, Action after)
    {
        if (_tables == null || slot >= u.Bag.Count) { after(); return; }
        var it = _tables.Items[u.Bag[slot].Item];
        switch (it.Use)
        {
            case 5 or 13 or 20 or 21 or 24:
                _ai = new BattleAi(_battle);
                UseItem(u, slot, center, after);
                return;
            case 14 or 22:
                CastSpell(u, it.Use == 14 ? 27 : 22, center, after, fromItem: true);
                return;
        }
        var targets = new BattleAi(_battle).ItemTargets(u, it, center);
        var notes = new List<Notice>();
        int v = it.UseValue;
        foreach (var t in targets)
        {
            string? text = it.Use switch
            {
                6 => Cure(t, 3, " 독이 풀림"),
                7 => Cure(t, 4, " 마비가 풀림"),
                8 => Raise(() => t.BaseAtk += v, $" 역량 +{v}"),
                9 => Raise(() => t.BaseDef += v, $" 내력 +{v}"),
                10 => Raise(() => t.Speed += v, $" 속도 +{v}"),
                11 => Raise(() => t.Mp = Math.Min(t.MaxMp, t.Mp + v), $" MP {v} 회복"),
                12 => Raise(() => t.Status[2] = 2 + _battle.Rng.Next(4), " 속도 증가"),
                15 => Raise(() => t.Status[1] = 2 + _battle.Rng.Next(4), " 방어력 증가"),
                16 => Raise(() => t.Status[0] = 2 + _battle.Rng.Next(4), " 공격력 증가"),
                17 => Raise(() => { t.MaxHp += v; t.Hp += v; }, $" 최대 HP +{v}"),
                18 => Raise(() => { t.MaxMp += v; t.Mp += v; }, $" 최대 MP +{v}"),
                19 => Raise(() => t.Move += v, $" 이동력 +{v}"),
                _ => null,
            };
            _battle.Recompute(t);
            if (text != null) notes.Add(new Notice(text, Battle.NameLine(t)));
        }
        if (ConsumedItem(it.Use) && slot < u.Bag.Count) u.Bag.RemoveAt(slot);
        _battle.Recompute(u);
        ShowNotices(notes, after);

        static string Raise(Action a, string text) { a(); return text; }
        static string? Cure(Unit t, int k, string text) { if (t.Status[k] == 0) return null; t.Status[k] = 0; return text; }
    }
}

/// <summary>
/// 전송술(마법 23, sub_2218A · sub_22253) — 1단계 사거리 안 우리 편 한 명, 2단계 그 사람이 갈 빈 칸. MP 를 내고 옮긴 뒤 경험(대상 레벨' × 10).
/// </summary>
internal sealed partial class GameWindow
{
    private Unit? _teleportWho;

    private void BeginTeleportDest(Unit caster, Unit who)
    {
        var cells = new List<(int X, int Y)>();
        for (int y = 0; y < _scene.Height; y++)
            for (int x = 0; x < _scene.Width; x++)
                if (_scene.Free(x, y)) cells.Add((x, y));
        BeginTarget(caster, cells, 23, _targetFrom);
        _teleportWho = who;
    }

    private void Teleport(Unit caster, Unit who, (int X, int Y) to, Action after)
    {
        _teleportWho = null;
        _battle.PayMp(caster, 23);
        PlaySpellFx(23, [(who.X, who.Y)], () =>
        {
            (who.X, who.Y, who.FromX, who.FromY) = (to.X, to.Y, to.X, to.Y);
            var notes = new List<Notice> { new(" 전송", Battle.NameLine(who)) };
            notes.AddRange(_battle.GainExp(caster, _battle.SpellExp(caster, 23, [new SpellResult(who, true, 0, false)])));
            ShowNotices(notes, after);
        });
    }
}
