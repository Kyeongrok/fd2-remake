using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 파티 정보 창(P 키, 폰은 모바일 메뉴) — 창세기전3 파트1 의 파티 창처럼 우리 편을 한 줄씩: 얼굴 · 이름(원본 글꼴) · 직업 · 레벨 · 경험,
/// HP · MP 막대, 셈한 공격 · 방어 · 명중 · 회피 · 속도 · 이동, 장비한 무기 · 옷, 상태. 전투 중이면 지도의 우리 편, 밖이면 동료 표.
/// 위아래(휠 · 끌기)로 고르고 P · Esc 로 닫는다.
/// </summary>
internal sealed partial class GameWindow
{
    private bool _partyOpen;
    private List<Unit> _party = new();
    private int _partyIndex, _partyTop, _partyVisible = 1, _partyCardH = 1;
    private (int X, int Y, int W, int H) _partyBox;
    private readonly List<(int X, int Y, int W, int H)> _partyRects = new();

    /// <summary>끌어 넘기는 중이면 누른 y 와 그때 맨 위 줄.</summary>
    private (int Y, int Top)? _partyDrag;

    /// <summary>파티 창을 연다 — 보일 사람이 없으면 안 연다.</summary>
    private void OpenParty()
    {
        _party = PartyMembers();
        if (_party.Count == 0) return;
        PlaySfx(Sound.UiBank, Sound.Menu);
        (_partyOpen, _partyIndex, _partyTop, _partyDrag) = (true, 0, 0, null);
    }

    private void CloseParty() => (_partyOpen, _partyDrag) = (false, null);

    /// <summary>우리 편 — 전투 중이면 지도의 사람들(우리가 움직이는 사람 먼저, 컴퓨터 아군 뒤), 밖이면 동료 표를 사람으로 세워서.</summary>
    private List<Unit> PartyMembers()
    {
        if (InBattle)
            return _scene.Units.Where(u => u.Side == Side.Player && u.MaxHp > 0)
                         .OrderBy(u => u.Ai).ThenBy(u => u.Slot < 0 ? int.MaxValue : u.Slot).ToList();
        if (_campaign is not { } c) return [];
        var list = new List<Unit>();
        foreach (var r in c.Party)
        {
            var u = new Unit { Icon = CharacterNames.SpriteOf(r[7], _icons.RoyalsMoved), Form = r[7], Id = r[8], Side = Side.Player };
            _battle.Setup(u);
            HeroRecord.Apply(r, u);
            _battle.Recompute(u);
            if (u.MaxHp > 0) list.Add(u);   // 쓰러진 사람도 보인다(HP 0)
        }
        return list;
    }

    /// <summary>파티 창이 연 동안 받는 키. 먹었으면 참.</summary>
    private bool PartyKey(int vk)
    {
        if (!_partyOpen) return false;
        var action = ActionOf(vk);
        if (action == KeyAction.MoveUp) vk = Win32.VK_UP;
        else if (action == KeyAction.MoveDown) vk = Win32.VK_DOWN;
        int n = _party.Count;
        switch (vk)
        {
            case Win32.VK_UP: PartySelect(_partyIndex == 0 ? n - 1 : _partyIndex - 1); break;
            case Win32.VK_DOWN: PartySelect(_partyIndex == n - 1 ? 0 : _partyIndex + 1); break;
            case Win32.VK_PRIOR: PartySelect(Math.Max(0, _partyIndex - _partyVisible)); break;
            case Win32.VK_NEXT: PartySelect(Math.Min(n - 1, _partyIndex + _partyVisible)); break;
            case Win32.VK_ESCAPE or Win32.VK_RETURN or Win32.VK_SPACE: CloseParty(); break;
            default: if (action == KeyAction.Party) CloseParty(); break;
        }
        return true;
    }

    /// <summary>고른 줄을 바꾸고 보이게 넘긴다.</summary>
    private void PartySelect(int i)
    {
        _partyIndex = Math.Clamp(i, 0, Math.Max(0, _party.Count - 1));
        if (_partyIndex < _partyTop) _partyTop = _partyIndex;
        if (_partyIndex >= _partyTop + _partyVisible) _partyTop = _partyIndex - _partyVisible + 1;
    }

    private static bool InRect((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X && y >= r.Y && x < r.X + r.W && y < r.Y + r.H;

    /// <summary>누르기 — 줄을 누르면 고르고 끌기를 시작, 창 밖이면 닫는다.</summary>
    private bool PartyPress(int x, int y)
    {
        if (!_partyOpen) return false;
        if (!InRect(_partyBox, x, y)) { CloseParty(); return true; }
        int hit = _partyRects.FindIndex(r => InRect(r, x, y));
        if (hit >= 0) _partyIndex = _partyTop + hit;
        _partyDrag = (y, _partyTop);
        return true;
    }

    private bool PartyMove(int x, int y)
    {
        if (!_partyOpen) return false;
        if (_partyDrag is { } d)
            _partyTop = Math.Clamp(d.Top - (y - d.Y) / Math.Max(1, _partyCardH), 0, Math.Max(0, _party.Count - _partyVisible));
        return true;
    }

    private bool PartyRelease()
    {
        if (!_partyOpen) return false;
        _partyDrag = null;
        return true;
    }

    /// <summary>마우스 휠(+ 위로) — 열린 창이 받는다.</summary>
    internal void PointerWheel(int notches)
    {
        if (_partyOpen) PartySelect(_partyIndex - notches);
        else if (_volumeOpen) VolumeStep(notches * 5);
        else if (MenuWheel(notches)) { }
        else if (TouchUi && InBattle && _bs is BattleState.Select or BattleState.Target) PinchZoom(notches);   // 모바일 화면(PC): 휠은 두 손가락 벌리기 · 오므리기
    }

    private static Text.Image HJoin(params Text.Image?[] parts)
    {
        var ps = parts.Where(p => p != null).Select(p => p!).ToList();
        int w = Math.Max(1, ps.Sum(p => p.W)), h = Math.Max(1, ps.Max(p => p.H));
        var px = new uint[w * h];
        int ox = 0;
        foreach (var p in ps)
        {
            int oy = (h - p.H) / 2;
            for (int r = 0; r < p.H; r++) Array.Copy(p.Px, r * p.W, px, (r + oy) * w + ox, p.W);
            ox += p.W;
        }
        return new Text.Image(px, w, h);
    }

    private void DrawParty()
    {
        _partyRects.Clear();
        if (!_partyOpen) return;
        if (_party.Count == 0) { CloseParty(); return; }
        int fs = Math.Max(14, 7 * HudZoom), small = Math.Max(12, fs * 5 / 6), g = Math.Max(1, fs / Font16.Size);
        int gold = InBattle ? _battle.Gold : _campaign?.Gold ?? 0;
        var title = Text.Render($"파티 정보 — {_party.Count}명 · 돈 {gold}", fs, 0xFFFFE070);
        var hint = Text.Render(TouchUi ? "줄을 눌러 고르기 · 끌어 넘기기 · 창 밖을 누르면 닫기"
                                       : $"↑↓ · 휠 고르기 · {KeyBindings.KeyName(_keyMap[KeyAction.Party])} · Esc 닫기", small, 0xFF9098A8, bold: false);

        // 한 사람의 글 줄 — 넷째 줄까지(이름 · 막대 · 능력 · 장비)
        Text.Image Line(string s, uint c = 0xFFFFFFFF) => Text.Render(s, small, c, bold: false);
        int lineH = Line("가").H + 4;
        int barW = small * 6;
        var faceOf = _party.Select(u => Face(u.Icon) is { Count: > 0 } f ? f[0] : ((uint[] Px, int W, int H)?)null).ToList();
        int textH = lineH * 4;
        int faceS = Math.Max(1, textH / Math.Max(1, faceOf.Where(f => f != null).Select(f => f!.Value.H).DefaultIfEmpty(textH).Max()));
        int faceCol = faceOf.Where(f => f != null).Select(f => f!.Value.W * faceS).DefaultIfEmpty(0).Max();
        int faceH = faceOf.Where(f => f != null).Select(f => f!.Value.H * faceS).DefaultIfEmpty(0).Max();
        _partyCardH = Math.Max(textH, faceH) + 12;

        var cards = _party.Select(u =>
        {
            bool down = u.Hp <= 0;
            uint nameColor = down ? 0xFF808080 : u.Ai ? 0xFFA0F0A0 : 0xFF90E0FF;
            var name = NameImage(Battle.NameLine(u), g, nameColor) ?? Text.Render(CharacterNames.Of(u.Icon).Split('-')[0], fs, nameColor);
            string head = $"  {GameNames.Class(u.Class)}  Lv {u.Level}" + (u.Team == 2 ? $"  경험 {u.Exp}/100" : "  (컴퓨터 아군)");
            string weapon = u.Bag.FirstOrDefault(b => b.Equipped && b.Item < 128) is { Equipped: true } w ? GameNames.Item(w.Item) : "-";
            string armor = u.Bag.FirstOrDefault(b => b.Equipped && b.Item >= 128) is { Equipped: true } a ? GameNames.Item(a.Item) : "-";
            string status = down ? "쓰러짐" : string.Join(" ", Enumerable.Range(0, 6).Where(k => u.Status[k] > 0).Select(k => $"{Battle.StatusNames[k]}({u.Status[k]})"));
            return (Head: HJoin(name, Line(head)),
                    Hp: Line($"{Math.Max(0, u.Hp)} / {u.MaxHp}"), Mp: Line($"{u.Mp} / {u.MaxMp}"),
                    Stats: Line($"공격 {u.Atk}  방어 {u.Def}  명중 {u.Hit}  회피 {u.Evade}  속도 {u.Speed}  이동 {u.Move}"),
                    Gear: HJoin(Line($"무기 {weapon}   옷 {armor}"), status.Length > 0 ? Line($"   {status}", down ? 0xFFFF9090 : 0xFFFFC060) : null));
        }).ToList();

        var hpL = Line("HP");
        int numW = cards.Max(c => Math.Max(c.Hp.W, c.Mp.W));
        int barsW = 2 * (hpL.W + 6 + barW + 8 + numW) + small;
        int textW = Math.Max(barsW, cards.Max(c => Math.Max(c.Head.W, Math.Max(c.Stats.W, c.Gear.W))));
        int w = Math.Min(_clientW - 20, Math.Max(Math.Max(title.W, hint.W) + 40, faceCol + textW + 56));
        int headH = title.H + hint.H + 22;
        _partyVisible = Math.Max(1, Math.Min(_party.Count, (_clientH - 20 - headH - 10) / _partyCardH));
        _partyTop = Math.Clamp(_partyTop, 0, Math.Max(0, _party.Count - _partyVisible));
        int h = headH + _partyVisible * _partyCardH + 10;
        int x = (_clientW - w) / 2, y = Math.Max(10, (_clientH - h) / 2);
        _partyBox = (x, y, w, h);

        FillRect(0, 0, _clientW, _clientH, 0x90000000);
        FillRect(x, y, w, h, 0xF0181828);
        DrawText(title, x + 20, y + 8);
        DrawText(hint, x + 20, y + 12 + title.H);
        if (_partyTop > 0) { var up = Text.Render("▲", small, 0xFFFFE070); DrawText(up, x + w - up.W - 16, y + 8); }
        if (_partyTop + _partyVisible < _party.Count) { var dn = Text.Render("▼", small, 0xFFFFE070); DrawText(dn, x + w - dn.W - 16, y + h - dn.H - 4); }

        var clip = (x, y, x + w, y + h);
        int cy = y + headH;
        for (int i = _partyTop; i < Math.Min(_party.Count, _partyTop + _partyVisible); i++)
        {
            var u = _party[i];
            var c = cards[i];
            _partyRects.Add((x + 8, cy, w - 16, _partyCardH - 4));
            FillRect(x + 8, cy, w - 16, _partyCardH - 4, i == _partyIndex ? 0xC0405090 : 0x60283048);
            if (faceOf[i] is { } f)
                BlitFrame(f.Px, f.W, f.H, x + 16 + (faceCol - f.W * faceS) / 2, cy + (_partyCardH - 4 - f.H * faceS) / 2, faceS, false, 0, 0, clip);
            int tx = x + 16 + faceCol + 16, ty = cy + (_partyCardH - 4 - textH) / 2 + 2;
            DrawText(c.Head, tx, ty);
            int bx = tx;
            void Bar(Text.Image label, Text.Image num, int now, int max, uint color)
            {
                DrawText(label, bx, ty + lineH);
                int barX = bx + hpL.W + 6, barY = ty + lineH + (label.H - small / 2) / 2, bh = Math.Max(4, small / 2);
                FillRect(barX, barY, barW, bh, 0xFF3A4670);
                if (max > 0) FillRect(barX + 1, barY + 1, (barW - 2) * Math.Clamp(now, 0, max) / max, bh - 2, color);
                DrawText(num, barX + barW + 8, ty + lineH);
                bx = barX + barW + 8 + numW + small;
            }
            Bar(hpL, c.Hp, u.Hp, u.MaxHp, 0xFFF0C030);
            if (u.MaxMp > 0) Bar(Line("MP"), c.Mp, u.Mp, u.MaxMp, 0xFF58A030);
            DrawText(c.Stats, tx, ty + lineH * 2);
            DrawText(c.Gear, tx, ty + lineH * 3);
            cy += _partyCardH;
        }
    }
}
