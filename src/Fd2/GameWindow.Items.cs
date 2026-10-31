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

    private static bool UsableItem(ItemInfo it) => it.Use is >= 5 and <= 24;

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

    // ── 원작 물건 메뉴(sub_1BBDC) — 물건 단추를 누르면 그 사람 둘레에 단추 넷(8 · 9 · 10 · 11):
    //    위 사용(sub_20C6F, 쓰면 끝냄) · 왼쪽 건네주기(옆 칸 우리 편이 없으면 빨강) · 오른쪽 장비(sub_1BFFE) · 아래 버리기.
    //    고르면 아이템 창 — 위에 얼굴 · 능력, 아래 가방 목록(그림 · 이름 · +AP/+DP/+HP), 할 수 없는 줄은 어둡게.

    private enum ItemMode { Use, Give, Equip, Drop }
    private ItemMode _itemMode;
    private int _pendingGive = -1;

    private void OpenItemCross(Unit u)
    {
        PlaySfx(Sound.UiBank, Sound.Menu);
        _actor = u;
        _stage = MenuStage.ItemCross;
        _menu.Clear();
        _menuGlyphs.Clear();
        _attackTiles.Clear();
        _sysButtons = [8, 9, 10, 11];
        _menu.Add(("사용", () => OpenItemPick(u, ItemMode.Use), default));
        if (GiveCells(u).Count > 0) _menu.Add(("건네주기", () => OpenItemPick(u, ItemMode.Give), default));
        _menu.Add(("장비", () => OpenItemPick(u, ItemMode.Equip), default));
        _menu.Add(("버리기", () => OpenItemPick(u, ItemMode.Drop), default));
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    /// <summary>건네줄 수 있는 곳 — 위아래 · 좌우 옆 칸의 살아 있는 우리 편(가방에 자리가 있는 사람).</summary>
    private List<(int X, int Y)> GiveCells(Unit u) =>
        new[] { (0, -1), (-1, 0), (1, 0), (0, 1) }.Select(d => (X: u.X + d.Item1, Y: u.Y + d.Item2))
            .Where(c => _scene.UnitAt(c.X, c.Y) is { Team: 2, Hp: > 0 } o && o != u && o.Bag.Count < 8).ToList();

    /// <summary>이 줄로 지금 할 수 있나 — 할 수 없는 줄은 어둡게 두고 눌러도 아무 일 없다.</summary>
    private bool ItemRowEnabled(Unit u, int slot, ItemMode mode)
    {
        if (_tables == null || slot >= u.Bag.Count) return false;
        var (id, equipped) = u.Bag[slot];
        var it = _tables.Items[id];
        return mode switch
        {
            ItemMode.Use => UsableItem(it) && ItemCenters(u, it).Count > 0,
            ItemMode.Equip => it.Kind < 32 && !equipped && CanEquip(u, it),
            _ => !equipped,
        };
    }

    private void OpenItemPick(Unit u, ItemMode mode)
    {
        if (u.Bag.Count == 0) { OpenItemCross(u); return; }
        PlaySfx(Sound.UiBank, Sound.Menu);
        _itemMode = mode;
        _stage = MenuStage.ItemPick;
        _menu.Clear();
        _menuGlyphs.Clear();
        for (int i = 0; i < u.Bag.Count; i++)
        {
            int slot = i;
            _menu.Add((GameNames.Item(u.Bag[i].Item), () =>
            {
                if (!ItemRowEnabled(u, slot, mode)) return;
                switch (mode)
                {
                    case ItemMode.Use: BeginItemTarget(u, slot, _itemFrom); break;
                    case ItemMode.Equip: Equip(u, slot); OpenItemPick(u, mode); break;
                    case ItemMode.Drop:
                        AskBox(-1, u.Icon, () => { u.Bag.RemoveAt(slot); _battle.Recompute(u); OpenItemPick(u, mode); }, () => OpenItemPick(u, mode));
                        _askText = $"{GameNames.Item(u.Bag[slot].Item)} 을(를) 버립니까?";
                        break;
                    case ItemMode.Give:
                        _pendingGive = slot;
                        _menu.Clear();
                        _attackTiles = GiveCells(u).ToHashSet();
                        _cursor = _attackTiles.First();
                        _tapAim = _cursor;
                        _bs = BattleState.Target;
                        break;
                }
            }, default));
        }
        _menuIndex = Math.Max(0, Enumerable.Range(0, u.Bag.Count).FirstOrDefault(i => ItemRowEnabled(u, i, mode), 0));
        _bs = BattleState.Menu;
    }

    /// <summary>건네주기(sub_1BB8C) — 고른 옆 칸 우리 편 가방에 넣는다. 차례는 끝나지 않고 물건 메뉴로 돌아간다.</summary>
    private void GiveItem(Unit u, int slot, (int X, int Y) at)
    {
        _pendingGive = -1;
        _attackTiles.Clear();
        if (_scene.UnitAt(at.X, at.Y) is { Team: 2 } to && to != u && to.Bag.Count < 8 && slot < u.Bag.Count && !u.Bag[slot].Equipped)
        {
            to.Bag.Add((u.Bag[slot].Item, false));
            u.Bag.RemoveAt(slot);
            _battle.Recompute(u);
            _battle.Recompute(to);
            PlaySfx(Sound.UiBank, Sound.Menu);
        }
        if (u.Bag.Count == 0) { _menu.Clear(); OpenMenu(); return; }
        OpenItemCross(u);
    }

    /// <summary>아이템 창 — 원작처럼 위 왼쪽 얼굴, 위 오른쪽 이름 · 직업 · LV · EX · DX · MV · HIT · AP · EV · DP · HP/MP 막대, 아래 가방 목록.</summary>
    private void DrawItemWindow()
    {
        if (_actor is not { } u || _tables == null) return;
        int g = Math.Max(1, Math.Min(HudZoom, (_clientH - 40) / (16 * 14)));
        int fs = Math.Max(14, 7 * g), lineH = 16 * g + 4 * g;
        int W = Math.Min(_clientW - 24, 16 * g * 26), x0 = (_clientW - W) / 2;
        var faces = Face(u.Icon);
        int faceW = faces is { Count: > 0 } ? faces[0].W * g : 0, faceH = faces is { Count: > 0 } ? faces[0].H * g : 0;
        int topH = Math.Max(faceH + 16, lineH * 5 + 16), listH = lineH * Math.Max(4, u.Bag.Count) + 24;
        int y0 = Math.Max(8, (_clientH - topH - listH - 8) / 2);

        void Panel(int x, int y, int w, int h)
        {
            FillRect(x, y, w, h, 0xF028458A);
            FillRect(x, y, w, 2, 0xFF8090C0); FillRect(x, y + h - 2, w, 2, 0xFF8090C0);
            FillRect(x, y, 2, h, 0xFF8090C0); FillRect(x + w - 2, y, 2, h, 0xFF8090C0);
        }
        int faceBoxW = faceW > 0 ? faceW + 16 : 0;
        if (faceBoxW > 0)
        {
            Panel(x0, y0, faceBoxW, topH);
            var f = faces![0];
            BlitFrame(f.Px, f.W, f.H, x0 + 8, y0 + (topH - faceH) / 2, g, false, 0, 0, (0, 0, _clientW, _clientH));
        }
        int sx = x0 + faceBoxW + (faceBoxW > 0 ? 6 : 0), sw = W - (sx - x0);
        Panel(sx, y0, sw, topH);
        Text.Image T(string s, uint c = 0xFFFFFFFF) => Text.Render(s, fs, c);
        var name = NameImage(Battle.NameLine(u), g, 0xFFB8C8FF) ?? T(CharacterNames.Of(u.Icon).Split('-')[0], 0xFFB8C8FF);
        DrawText(name, sx + 14, y0 + 8);
        var cls = T(GameNames.Class(u.Class), 0xFFB8C8FF);
        DrawText(cls, sx + sw - cls.W - 16, y0 + 8);
        int cy = y0 + 8 + lineH;
        string N(int v, int d) => Math.Clamp(v, 0, d == 2 ? 99 : 999).ToString(d == 2 ? "00" : "000");
        var stats = new[] { ($"LV·{N(u.Level, 2)}", $"EX·{N(u.Exp, 2)}"), ($"DX·{N(u.Speed, 3)}", $"MV·{N(u.Move, 2)}"),
                            ($"HIT·{N(u.Hit, 3)}", $"AP·{N(u.Atk, 3)}"), ($"EV·{N(u.Evade, 3)}", $"DP·{N(u.Def, 3)}") };
        int colW = T("HIT·000 ").W;
        for (int r = 0; r < stats.Length; r++)
        {
            DrawText(T(stats[r].Item1, 0xFFC8F0B0), sx + 14, cy + r * lineH);
            DrawText(T(stats[r].Item2, 0xFFC8F0B0), sx + 14 + colW, cy + r * lineH);
        }
        int bx = sx + 14 + colW * 2 + 8, bw = sx + sw - 16 - bx;
        void Bar(string label, int now, int max, uint color, int y)
        {
            var l = T(label);
            DrawText(l, bx, y);
            int barX = bx + l.W + 6, barW = Math.Max(10, bw - l.W - 6), bh = Math.Max(6, g * 5);
            FillRect(barX, y + (l.H - bh) / 2, barW, bh, 0xFF203060);
            if (max > 0) FillRect(barX, y + (l.H - bh) / 2, barW * Math.Clamp(now, 0, max) / max, bh, color);
            var n = T($"{N(now, 3)}/{N(max, 3)}");
            DrawText(n, sx + sw - n.W - 16, y + lineH);
        }
        Bar("HP·", u.Hp, u.MaxHp, 0xFFF0D040, cy);
        Bar("MP·", u.Mp, u.MaxMp, 0xFFA01818, cy + lineH * 2);

        // 아래 — 가방 목록
        int ly = y0 + topH + 8;
        Panel(x0, ly, W, listH);
        if (!_buttonsTried)
        {
            _buttonsTried = true;
            try { _buttons = new Buttons(_dir, _pal); }
            catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { _buttons = null; }
        }
        for (int i = 0; i < u.Bag.Count && i < _menu.Count; i++)
        {
            var (id, equipped) = u.Bag[i];
            var it = _tables.Items[id];
            int ry = ly + 12 + i * lineH;
            var rect = (X: x0 + 8, Y: ry - 2, W: W - 16, H: lineH);
            bool on = ItemRowEnabled(u, i, _itemMode);
            if (_mouseMoved && _mouseX >= rect.X && _mouseY >= rect.Y && _mouseX < rect.X + rect.W && _mouseY < rect.Y + rect.H) _menuIndex = i;
            if (i == _menuIndex) FillRect(rect.X, rect.Y, rect.W, rect.H, 0x804870C0);
            // 그림 — 무기는 칼, 옷은 갑옷, 그 밖은 주머니 단추(장비 중이면 빨강, 원작처럼)
            int icon = id < 128 && it.Kind < 32 && it.Atk > 0 ? 0 : it.Kind < 32 && id >= 128 ? 10 : 2;
            int ix = x0 + 16;
            if (_buttons is { Count: > 3 * 10 + 2 } bank)
            {
                var (px, w, h) = bank.Frame(icon, red: equipped, 0);
                int s = Math.Max(1, (lineH - 4) / h);
                BlitScaled(px, w, h, ix, ry + (lineH - 4 - h * s) / 2, s, 0, 0, _fbW, _fbH);
                ix += w * s + 12;
            }
            var nm = NameImage(181 + id, g, on ? 0xFFB8C8FF : 0xFF6878A0) ?? T(GameNames.Item(id), on ? 0xFFB8C8FF : 0xFF6878A0);
            DrawText(nm, ix, ry + (lineH - 4 - nm.H) / 2);
            string eff = id < 128 && it.Atk > 0 ? $"+AP {it.Atk:000}" : id >= 128 && it.Def > 0 ? $"+DP {it.Def:000}"
                       : it.Use is 5 or 13 ? $"+HP {it.UseValue:000}" : it.Use == 11 ? $"+MP {it.UseValue:000}" : "";
            if (eff.Length > 0) { var e = T(eff, on ? 0xFFC8F0B0 : 0xFF607060); DrawText(e, x0 + W * 2 / 5, ry + (lineH - 4 - e.H) / 2); }
            _menu[i] = (_menu[i].Label, _menu[i].Do, rect);
        }
        var mode = T(_itemMode switch { ItemMode.Use => "사용", ItemMode.Give => "건네주기", ItemMode.Equip => "장비", _ => "버리기" }, 0xFFFFE070);
        DrawText(mode, x0 + W - mode.W - 16, ly + listH - mode.H - 8);
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

    /// <summary>전송법장(물건 쓰임 23)으로 옮기는 중 — MP 를 내지 않는다.</summary>
    private bool _teleportByItem;

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
        if (!_teleportByItem) _battle.PayMp(caster, 23);
        _teleportByItem = false;
        PlaySpellFx(23, [(who.X, who.Y)], () =>
        {
            (who.X, who.Y, who.FromX, who.FromY) = (to.X, to.Y, to.X, to.Y);
            var notes = new List<Notice> { new(" 전송", Battle.NameLine(who)) };
            notes.AddRange(_battle.GainExp(caster, _battle.SpellExp(caster, 23, [new SpellResult(who, true, 0, false)])));
            ShowNotices(notes, after);
        });
    }
}
