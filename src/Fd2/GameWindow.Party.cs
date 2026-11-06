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

    /// <summary>끌어 넘기는 중이면 누른 y 와 그때 넘긴 점 수.</summary>
    private (int Y, double Scroll)? _partyDrag;

    /// <summary>목록을 점 단위로 넘긴 만큼 · 손을 뗀 뒤 미끄러지는 빠르기(점/초) — 예전에는 카드 한 장씩 뚝뚝 넘어가 폰에서 불편했다(w-111).</summary>
    private double _partyScroll, _partyVel, _partyLastT;
    private (int Y, double T) _partyLastMove;
    private int _partyMaxScroll;

    /// <summary>그린 카드마다 몇 번째 사람인가(_partyRects 와 같은 차례).</summary>
    private readonly List<int> _partyRectIdx = new();

    /// <summary>파티 창을 연다 — 보일 사람이 없으면 안 연다.</summary>
    private void OpenParty()
    {
        _party = PartyMembers();
        if (_party.Count == 0) return;
        PlaySfx(Sound.UiBank, Sound.Menu);
        (_partyOpen, _partyIndex, _partyTop, _partyDrag) = (true, 0, 0, null);
        (_partyScroll, _partyVel) = (0, 0);
    }

    private void CloseParty() => (_partyOpen, _partyDrag, _partyDetail, _partyDetailOnly) = (false, null, false, false);

    /// <summary>지도에서 바로 연 자세히 창 — 닫으면 목록이 아니라 지도로(w-121).</summary>
    /// <summary>장비한 물건 이름 색 — 원작처럼 빨강(w-139).</summary>
    private const uint EquippedColor = 0xFFFF5050;

    private bool _partyDetailOnly;

    /// <summary>그 동료의 자세히 창(능력 · 상태 · 물건 · 마법)을 바로 연다 — 행동을 마친 아군을 한 번 더 눌렀을 때(w-121).</summary>
    private bool OpenPartyDetailOf(Unit u)
    {
        var party = PartyMembers();
        int i = party.IndexOf(u);
        if (i < 0) return false;
        _party = party;
        (_partyOpen, _partyTop, _partyDrag, _partyScroll, _partyVel) = (true, 0, null, 0, 0);
        PartyDetail(i);
        _partyDetailOnly = true;
        return true;
    }

    /// <summary>자세히 창을 닫는다 — 지도에서 바로 열었으면 파티 창째로.</summary>
    private void ClosePartyDetail() { if (_partyDetailOnly) CloseParty(); else _partyDetail = false; }

    /// <summary>자세히 창(원작 인물 정보 창처럼 — 능력 · 가방 물건 · 아는 마법, 제보 w-95)이 열려 있나.</summary>
    private bool _partyDetail;

    private void PartyDetail(int i)
    {
        if (i < 0 || i >= _party.Count) return;
        PlaySfx(Sound.UiBank, Sound.Menu);
        _partyIndex = i;
        _partyDetail = true;
    }

    /// <summary>파티 창에서 동료를 눌러 장비로 가나 — 건물 안, 폰이면 마을 첫 화면과 우리 차례 전투에서도(사용자 요청).</summary>
    private bool PartyGearOn => _town != null && _campaign != null && (!_townTop || TouchUi) || PartyBattleGear;

    /// <summary>폰 전투 — 우리 차례에 고르는 중이면 파티 창에서 장비를 바꾼다.</summary>
    private bool PartyBattleGear => TouchUi && InBattle && _town == null && !_enemyPhase && _bs == BattleState.Select;

    /// <summary>파티 창에서 온 전투 장비 창 — 물리면 파티 창으로.</summary>
    private bool _partyEquip;

    /// <summary>마을 첫 화면 파티 창에서 온 장비 목록 — 물리면 첫 화면으로.</summary>
    private bool _partyFromTop;

    /// <summary>누른 줄(끌면 취소) — 뗄 때 장비로 간다.</summary>
    private int _partyTap = -1;

    /// <summary>고른 동료의 장비 · 물건 목록을 연다.</summary>
    private void PartyGear(int i)
    {
        if (!PartyGearOn || i < 0 || i >= _party.Count) return;
        if (PartyBattleGear)
        {
            var u = _party[i];
            if (u.Team != 2 || u.Hp <= 0) { Toast("우리 편만 장비를 바꿀 수 있습니다"); return; }
            if (u.Bag.Count == 0 && SharedItems(u, ItemMode.Equip).Count == 0) { Toast("가방이 비었습니다"); return; }
            CloseParty();
            ClearSelection();
            _actor = u;
            _partyEquip = true;
            OpenItemPick(u, ItemMode.Equip);
            return;
        }
        _partyFromTop = _townTop;
        int id = _party[i].NameId, member = _campaign!.Party.FindIndex(r => HeroRecord.Id(r) == id);
        CloseParty();
        if (member >= 0) OpenGiveItem(0, member);
    }

    /// <summary>우리 편 — 전투 중이면 지도의 사람들(우리가 움직이는 사람 먼저, 컴퓨터 아군 뒤), 밖이나 마을이면 동료 표를 사람으로 세워서(마을에서 산 장비가 보이게).</summary>
    private List<Unit> PartyMembers()
    {
        if (InBattle && _town == null)
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
        if (_partyDetail)
        {
            switch (vk)
            {
                case Win32.VK_UP or Win32.VK_LEFT: PartySelect(_partyIndex == 0 ? n - 1 : _partyIndex - 1); PlaySfx(Sound.UiBank, Sound.Cursor); break;
                case Win32.VK_DOWN or Win32.VK_RIGHT: PartySelect(_partyIndex == n - 1 ? 0 : _partyIndex + 1); PlaySfx(Sound.UiBank, Sound.Cursor); break;
                case Win32.VK_ESCAPE or Win32.VK_RETURN or Win32.VK_SPACE: ClosePartyDetail(); break;
                default: if (action == KeyAction.Party) CloseParty(); break;
            }
            return true;
        }
        switch (vk)
        {
            case Win32.VK_UP: PartySelect(_partyIndex == 0 ? n - 1 : _partyIndex - 1); break;
            case Win32.VK_DOWN: PartySelect(_partyIndex == n - 1 ? 0 : _partyIndex + 1); break;
            case Win32.VK_PRIOR: PartySelect(Math.Max(0, _partyIndex - _partyVisible)); break;
            case Win32.VK_NEXT: PartySelect(Math.Min(n - 1, _partyIndex + _partyVisible)); break;
            case Win32.VK_RETURN or Win32.VK_SPACE when PartyGearOn: PartyGear(_partyIndex); break;
            case Win32.VK_RETURN or Win32.VK_SPACE: PartyDetail(_partyIndex); break;   // 자세히(물건 · 마법, 제보 w-95)
            case Win32.VK_ESCAPE: CloseParty(); break;
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
        // 고른 카드가 다 보이게 점 단위로 맞춘다
        _partyVel = 0;
        double top = _partyIndex * _partyCardH, bottom = top + _partyCardH, view = _partyVisible * (double)_partyCardH;
        if (top < _partyScroll) _partyScroll = top;
        else if (bottom > _partyScroll + view) _partyScroll = bottom - view;
    }

    private static bool InRect((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X && y >= r.Y && x < r.X + r.W && y < r.Y + r.H;

    /// <summary>누르기 — 줄을 누르면 고르고 끌기를 시작, 창 밖이면 닫는다.</summary>
    private bool PartyPress(int x, int y)
    {
        if (!_partyOpen) return false;
        if (_partyDetail) { ClosePartyDetail(); _partyTap = -1; return true; }   // 자세히 창은 누르면 목록으로
        if (!InRect(_partyBox, x, y)) { CloseParty(); return true; }
        int hit = _partyRects.FindIndex(r => InRect(r, x, y));
        bool flinging = Math.Abs(_partyVel) > 60;   // 미끄러지는 중에 누르면 멈추기만
        _partyVel = 0;
        if (hit >= 0 && !flinging) _partyIndex = _partyRectIdx[hit];
        _partyTap = hit >= 0 && !flinging ? _partyRectIdx[hit] : -1;
        _partyDrag = (y, _partyScroll);
        _partyLastMove = (y, _time);
        return true;
    }

    private bool PartyMove(int x, int y)
    {
        if (!_partyOpen) return false;
        if (_partyDrag is { } d)
        {
            if (Math.Abs(y - d.Y) > DragSlop * 2) _partyTap = -1;   // 끌면 누르기가 아니다
            _partyScroll = Math.Clamp(d.Scroll - (y - d.Y), 0, _partyMaxScroll);   // 손가락을 따라 점 단위로
            double dt = _time - _partyLastMove.T;
            if (dt > 0.001) _partyVel = _partyVel * 0.3 + 0.7 * -(y - _partyLastMove.Y) / dt;
            _partyLastMove = (y, _time);
        }
        return true;
    }

    private bool PartyRelease()
    {
        if (!_partyOpen) return false;
        if (_partyTap >= 0 || _time - _partyLastMove.T > 0.08) _partyVel = 0;   // 누르기였거나 멈췄다 뗐으면 미끄러지지 않는다
        _partyDrag = null;
        if (_partyTap >= 0) { if (PartyGearOn) PartyGear(_partyTap); else PartyDetail(_partyTap); }
        _partyTap = -1;
        return true;
    }

    /// <summary>마우스 휠(+ 위로 · 왼쪽) — 열린 창이 받고, 없으면 전투 지도를 민다(sideways 면 좌우, w-176).</summary>
    internal void PointerWheel(int notches, bool sideways = false, bool zoom = false)
    {
        if (sideways && !_notesOpen && !_partyOpen && !_volumeOpen && !_setupOpen) { WheelMap(notches, true); return; }
        if (_notesOpen) NotesScroll(-notches * _notesStep * 3);
        else if (_partyOpen) PartySelect(_partyIndex - notches);
        else if (_volumeOpen) VolumeStep(notches * 5);
        else if (_setupOpen) SetupStep(_setupRow, notches * 5);
        else if (MenuWheel(notches)) { }
        else if (LoadWheel(notches)) { }
        else if (zoom) { if (TouchUi && InBattle && _bs is BattleState.Select or BattleState.Target) PinchZoom(notches); }   // 모바일 화면(PC): Ctrl+휠은 두 손가락 벌리기 · 오므리기
        else WheelMap(notches, false);
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
        if (_partyDetail) { DrawPartyDetail(_party[Math.Clamp(_partyIndex, 0, _party.Count - 1)]); return; }
        int fs = Math.Max(14, 7 * HudZoom), small = Math.Max(12, fs * 5 / 6), g = Math.Max(1, fs / Font16.Size);
        int gold = InBattle ? _battle.Gold : _campaign?.Gold ?? 0;
        var title = Text.Render($"파티 정보 — {_party.Count}명 · 돈 {gold}", fs, 0xFFFFE070);
        bool narrow = _clientW < _clientH;   // 세로 화면 — 옆이 잘리지 않게 줄을 나눠 세로로 길게(사용자 요청)
        var hint = Text.Render((PartyGearOn ? "동료를 누르면 장비를 바꿉니다 · 창 밖을 누르면 닫기" : narrow ? "눌러 자세히 · 끌어 넘기기 · 창 밖을 누르면 닫기" : TouchUi ? "줄을 누르면 자세히(물건 · 마법) · 끌어 넘기기 · 창 밖을 누르면 닫기"
                                       : $"↑↓ · 휠 고르기 · 누르기 · Enter 자세히(물건 · 마법) · {KeyBindings.KeyName(_keyMap[KeyAction.Party])} · Esc 닫기") + " · 초록 숫자 = 파티 최고", small, 0xFF9098A8, bold: false);

        // 한 사람의 글 줄 — 넷째 줄까지(이름 · 막대 · 능력 · 장비)
        Text.Image Line(string s, uint c = 0xFFFFFFFF) => Text.Render(s, small, c, bold: false);
        int lineH = Line("가").H + 4;
        int barW = small * 6;
        var faceOf = _party.Select(u => Face(u.Icon) is { Count: > 0 } f ? f[0] : ((uint[] Px, int W, int H)?)null).ToList();
        int textH = lineH * (narrow ? 8 : 4);
        int faceS = Math.Max(1, textH / Math.Max(1, faceOf.Where(f => f != null).Select(f => f!.Value.H).DefaultIfEmpty(textH).Max()));
        if (narrow) faceS = Math.Max(1, Math.Min(faceS, _clientW / 4 / Math.Max(1, faceOf.Where(f => f != null).Select(f => f!.Value.W).DefaultIfEmpty(1).Max())));
        int faceCol = faceOf.Where(f => f != null).Select(f => f!.Value.W * faceS).DefaultIfEmpty(0).Max();
        int faceH = faceOf.Where(f => f != null).Select(f => f!.Value.H * faceS).DefaultIfEmpty(0).Max();
        _partyCardH = Math.Max(textH, faceH) + 12;

        // 능력마다 파티에서 가장 높은 값 — 그 숫자는 초록으로(한눈에 비교, w-140). 모두 같으면 안 칠한다
        int? Top(Func<Unit, int> f) { var v = _party.Select(f).ToList(); return v.Count > 1 && v.Max() != v.Min() ? v.Max() : null; }
        int? topAtk = Top(u => u.Atk), topDef = Top(u => u.Def), topHit = Top(u => u.Hit), topEva = Top(u => u.Evade), topSpd = Top(u => u.Speed), topMov = Top(u => u.Move);
        var cards = _party.Select(u =>
        {
            bool down = u.Hp <= 0;
            uint nameColor = down ? 0xFF808080 : u.Ai ? 0xFFA0F0A0 : 0xFF90E0FF;
            var name = NameImage(Battle.NameLine(u), g, nameColor) ?? Text.Render(CharacterNames.Of(u.Icon).Split('-')[0], fs, nameColor);
            string head = $"  {GameNames.Class(u.Class)}  Lv {u.Level}" + (u.Team == 2 ? $"  경험 {u.Exp}/{_battle.LevelUpExp}" : "  (컴퓨터 아군)");
            string weapon = u.Bag.FirstOrDefault(b => b.Equipped && b.Item < 128) is { Equipped: true } w ? GameNames.Item(w.Item) : "-";
            string armor = u.Bag.FirstOrDefault(b => b.Equipped && b.Item >= 128) is { Equipped: true } a ? GameNames.Item(a.Item) : "-";
            string status = down ? "쓰러짐" : string.Join(" ", Enumerable.Range(0, 6).Where(k => u.Status[k] > 0).Select(k => $"{Battle.StatusNames[k]}({u.Status[k]})"));
            string headShort = $"  {GameNames.Class(u.Class)}  Lv {u.Level}", exp = u.Team == 2 ? $"경험 {u.Exp}/{_battle.LevelUpExp}" : "(컴퓨터 아군)";
            // 원작 정보 창(sub_17FC0)처럼 오른 능력 숫자는 색을 바꾼다. 상태는 오른 것 노랑 · 독 · 마비 · 봉쇄 빨강
            const uint upC = 0xFFFFD040, badC = 0xFFFF9090;
            const uint topC = 0xFF60F080;
            Text.Image Stat(string label, int v, bool up, int? top = null) => HJoin(Line(label), Line($"{v}", up && !down ? upC : v == top ? topC : 0xFFFFFFFF));
            var spd = HJoin(Stat("  속도 ", u.Speed, false, topSpd), Stat("  이동 ", u.Move, false, topMov));
            Text.Image? StatusImg() => status.Length == 0 ? null : down ? Line($"   {status}", badC)
                : HJoin(Enumerable.Range(0, 6).Where(k => u.Status[k] > 0)
                    .Select(k => Line($"   {Battle.StatusNames[k]}({u.Status[k]})", k < 3 ? upC : badC)).ToArray());
            var atk = Stat("공격 ", u.Atk, u.Status[0] > 0, topAtk); var def = Stat("  방어 ", u.Def, u.Status[1] > 0, topDef);
            var hit = Stat("  명중 ", u.Hit, u.Status[2] > 0, topHit); var eva = Stat("  회피 ", u.Evade, u.Status[2] > 0, topEva);
            return (Head: HJoin(name, Line(narrow ? headShort : head)), Exp: Line(exp),
                    Stats1: HJoin(atk, def, hit), Stats2: HJoin(Stat("회피 ", u.Evade, u.Status[2] > 0, topEva), spd),
                    Weapon: Line($"무기 {weapon}"), Armor: HJoin(Line($"옷 {armor}"), StatusImg()),
                    Hp: Line($"{Math.Max(0, u.Hp)} / {u.MaxHp}"), Mp: Line($"{u.Mp} / {u.MaxMp}"), ExpNum: Line($"{u.Exp} / {_battle.LevelUpExp}"),
                    Stats: HJoin(atk, def, hit, eva, spd),
                    Gear: HJoin(Line($"무기 {weapon}   옷 {armor}"), StatusImg()));
        }).ToList();

        var hpL = Line("HP");
        int numW = cards.Max(c => Math.Max(Math.Max(c.Hp.W, c.Mp.W), narrow ? c.ExpNum.W : 0));
        if (narrow) barW = Math.Max(small * 2, _clientW - 20 - faceCol - 56 - hpL.W - 6 - 8 - numW);
        int barsW = narrow ? hpL.W + 6 + barW + 8 + numW : 2 * (hpL.W + 6 + barW + 8 + numW) + small;
        int textW = narrow ? barsW : Math.Max(barsW, cards.Max(c => Math.Max(c.Head.W, Math.Max(c.Stats.W, c.Gear.W))));
        int w = Math.Min(_clientW - 20, Math.Max(Math.Max(title.W, hint.W) + 40, faceCol + textW + 56));
        int headH = title.H + hint.H + 22;
        _partyVisible = Math.Max(1, Math.Min(_party.Count, (_clientH - 20 - headH - 10) / _partyCardH));
        // 목록 높이 — 다 들어가면 딱 맞게, 넘치면 화면 높이까지(카드가 반쯤 걸쳐도 된다)
        int listH = Math.Min(_party.Count * _partyCardH, Math.Max(_partyCardH, _clientH - 20 - headH - 10));
        _partyMaxScroll = Math.Max(0, _party.Count * _partyCardH - listH);
        // 손을 뗀 뒤 미끄러짐 — 점점 느려진다
        double now = _time, dts = Math.Clamp(now - _partyLastT, 0, 0.1);
        _partyLastT = now;
        if (_partyDrag == null && Math.Abs(_partyVel) > 20)
        {
            _partyScroll += _partyVel * dts;
            _partyVel *= Math.Exp(-3.5 * dts);
            if (_partyScroll <= 0 || _partyScroll >= _partyMaxScroll) _partyVel = 0;
        }
        else if (_partyDrag == null) _partyVel = 0;
        _partyScroll = Math.Clamp(_partyScroll, 0, _partyMaxScroll);
        _partyTop = Math.Clamp((int)(_partyScroll / _partyCardH), 0, Math.Max(0, _party.Count - 1));
        int h = headH + listH + 10;
        int x = (_clientW - w) / 2, y = Math.Max(10, (_clientH - h) / 2);
        _partyBox = (x, y, w, h);

        FillRect(0, 0, _clientW, _clientH, 0x90000000);
        FillRect(x, y, w, h, 0xF0181828);
        DrawText(title, x + 20, y + 8);
        DrawText(hint, x + 20, y + 12 + title.H);
        if (_partyScroll > 0) { var up = Text.Render("▲", small, 0xFFFFE070); DrawText(up, x + w - up.W - 16, y + 8); }
        bool more = _partyScroll < _partyMaxScroll;

        int listY = y + headH;
        var clip = (x, listY, x + w, listY + listH);
        (_clipTop, _clipBottom) = (listY, listY + listH);   // 반쯤 걸친 카드는 목록 칸 안에만
        _partyRectIdx.Clear();
        int first = (int)(_partyScroll / _partyCardH);
        int cy = listY - (int)_partyScroll + first * _partyCardH;
        for (int i = first; i < _party.Count && cy < listY + listH; i++)
        {
            var u = _party[i];
            var c = cards[i];
            int ry0 = Math.Max(cy, listY), ry1 = Math.Min(cy + _partyCardH - 4, listY + listH);
            if (ry1 > ry0) { _partyRects.Add((x + 8, ry0, w - 16, ry1 - ry0)); _partyRectIdx.Add(i); }
            FillRect(x + 8, cy, w - 16, _partyCardH - 4, i == _partyIndex ? 0xC0405090 : 0x60283048);
            if (faceOf[i] is { } f)
                BlitFrame(f.Px, f.W, f.H, x + 16 + (faceCol - f.W * faceS) / 2, cy + (_partyCardH - 4 - f.H * faceS) / 2, faceS, false, 0, 0, clip);
            int tx = x + 16 + faceCol + 16, ty = cy + (_partyCardH - 4 - textH) / 2 + 2;
            DrawText(c.Head, tx, ty);
            int bx = tx, barRow = narrow ? 2 : 1;
            void Bar(Text.Image label, Text.Image num, int now, int max, uint color, int row = -1)
            {
                int by = ty + lineH * (row >= 0 ? row : barRow);
                DrawText(label, bx, by);
                int barX = bx + hpL.W + 6, barY = by + (label.H - small / 2) / 2, bh = Math.Max(4, small / 2);
                FillRect(barX, barY, barW, bh, 0xFF3A4670);
                if (max > 0) FillRect(barX + 1, barY + 1, (barW - 2) * Math.Clamp(now, 0, max) / max, bh - 2, color);
                DrawText(num, barX + barW + 8, by);
                if (row >= 0) return;
                if (narrow) barRow++;
                else bx = barX + barW + 8 + numW + small;
            }
            Bar(hpL, c.Hp, u.Hp, u.MaxHp, 0xFFF0C030);
            if (u.MaxMp > 0) Bar(Line("MP"), c.Mp, u.Mp, u.MaxMp, MpColor);
            if (narrow)
            {
                // 이름 · 경험 · HP · MP · 능력 둘 · 무기 · 옷 — MP 가 없는 사람도 자리를 맞춘다
                // 경험도 막대로(사용자 요청 w-106) — 컴퓨터 아군은 글 그대로
                if (u.Team == 2) Bar(Line("EX"), c.ExpNum, u.Exp, _battle.LevelUpExp, 0xFF60A8F0, 1);
                else DrawText(c.Exp, tx, ty + lineH);
                DrawText(c.Stats1, tx, ty + lineH * 4);
                DrawText(c.Stats2, tx, ty + lineH * 5);
                DrawText(c.Weapon, tx, ty + lineH * 6);
                DrawText(c.Armor, tx, ty + lineH * 7);
            }
            else
            {
                DrawText(c.Stats, tx, ty + lineH * 2);
                DrawText(c.Gear, tx, ty + lineH * 3);
            }
            cy += _partyCardH;
        }
        (_clipTop, _clipBottom) = (0, int.MaxValue);
        if (more) { var dn = Text.Render("▼", small, 0xFFFFE070); DrawText(dn, x + w - dn.W - 16, y + h - dn.H - 4); }
    }

    /// <summary>
    /// 자세히 창 — 원작 인물 정보 창처럼 위에 얼굴 · 이름 · 직업 · LV EX · HP MP · 능력, 아래 가방 물건(장비 표시 · 효과)과 아는 마법(MP)(제보 w-95).
    /// ←→ · ↑↓ 다른 사람, Esc · Enter · 누르기는 목록으로.
    /// </summary>
    private void DrawPartyDetail(Unit u)
    {
        int g = Math.Max(1, Math.Min(HudZoom, (_clientH - 40) / (16 * 16)));
        int fs = Math.Max(14, 7 * g), small = Math.Max(12, fs * 5 / 6), lineH = 16 * g + 4 * g;
        int W = Math.Min(_clientW - 24, 16 * g * 30), x0 = (_clientW - W) / 2;
        Text.Image T(string s, uint c = 0xFFFFFFFF, int size = 0) => Text.Render(s, size > 0 ? size : fs, c, bold: size == 0);
        void Panel(int x, int y, int w, int h)
        {
            FillRect(x, y, w, h, 0xF028458A);
            FillRect(x, y, w, 2, 0xFF8090C0); FillRect(x, y + h - 2, w, 2, 0xFF8090C0);
            FillRect(x, y, 2, h, 0xFF8090C0); FillRect(x + w - 2, y, 2, h, 0xFF8090C0);
        }

        var items = u.Bag.Select(b => (b.Item, b.Equipped)).ToList();
        var spells = _battle.KnownSpells(u).ToList();
        bool narrow = _clientW < _clientH;
        int rowsLeft = Math.Max(1, items.Count), rowsRight = Math.Max(1, spells.Count);
        int listRows = narrow ? rowsLeft + rowsRight + 2 : Math.Max(rowsLeft, rowsRight) + 1;
        var faces = Face(u.Icon);
        int faceW = faces is { Count: > 0 } ? faces[0].W * g : 0, faceH = faces is { Count: > 0 } ? faces[0].H * g : 0;
        if (faceW > W / 3) { faceH = faceH * (W / 3) / Math.Max(1, faceW); faceW = W / 3; }
        int topH = Math.Max(faceH + 16, lineH * 5 + 16), listH = lineH * listRows + 24;
        var hint = T(TouchUi ? "누르면 목록으로" : "←→ 다른 사람 · Esc · Enter 목록으로", 0xFF9098A8, small);
        int y0 = Math.Max(8, (_clientH - topH - listH - hint.H - 16) / 2);
        FillRect(0, 0, _clientW, _clientH, 0x90000000);

        // 위 — 얼굴 · 이름 · 능력 · 막대
        int faceBoxW = faceW > 0 ? faceW + 16 : 0;
        if (faceBoxW > 0)
        {
            Panel(x0, y0, faceBoxW, topH);
            var f = faces![0];
            BlitFit(f.Px, f.W, f.H, x0 + 8, y0 + (topH - faceH) / 2, faceW, faceH);
        }
        int sx = x0 + faceBoxW + (faceBoxW > 0 ? 6 : 0), sw = W - (sx - x0);
        Panel(sx, y0, sw, topH);
        var name = NameImage(Battle.NameLine(u), g, 0xFFB8C8FF) ?? T(CharacterNames.Of(u.Icon).Split('-')[0], 0xFFB8C8FF);
        DrawText(name, sx + 14, y0 + 8);
        var cls = T(GameNames.Class(u.Class) + (u.Ai ? " (컴퓨터 아군)" : ""), 0xFFB8C8FF);
        DrawText(cls, sx + sw - cls.W - 16, y0 + 8);
        int cy = y0 + 8 + lineH;
        var stats = new[] { ($"LV {u.Level}", u.Team == 2 ? $"EX {u.Exp}" : ""), ($"공격 {u.Atk}", $"방어 {u.Def}"),
                            ($"명중 {u.Hit}", $"회피 {u.Evade}"), ($"속도 {u.Speed}", $"이동 {u.Move}") };
        int colW = T("명중 000  ").W;
        for (int r = 0; r < stats.Length; r++)
        {
            DrawText(T(stats[r].Item1, 0xFFC8F0B0), sx + 14, cy + r * lineH);
            if (stats[r].Item2.Length > 0) DrawText(T(stats[r].Item2, 0xFFC8F0B0), sx + 14 + colW, cy + r * lineH);
        }
        int bw = Math.Min(sx + sw - 16 - (sx + 14 + colW * 2 + 8), fs * 16), bx = sx + sw - 16 - bw;   // 막대는 오른쪽에 알맞은 길이로
        void Bar(string label, int now, int max, uint color, int y)
        {
            var l = T(label);
            DrawText(l, bx, y);
            int barX = bx + l.W + 6, barW = Math.Max(10, bw - l.W - 6), bh = Math.Max(6, g * 5);
            FillRect(barX, y + (l.H - bh) / 2, barW, bh, 0xFF203060);
            if (max > 0) FillRect(barX, y + (l.H - bh) / 2, barW * Math.Clamp(now, 0, max) / max, bh, color);
            var n = T($"{Math.Max(0, now)} / {max}");
            DrawText(n, sx + sw - n.W - 16, y + lineH);
        }
        if (bw > fs * 4)
        {
            Bar("HP", u.Hp, u.MaxHp, 0xFFF0D040, cy);
            Bar("MP", u.Mp, u.MaxMp, MpColor, cy + lineH * 2);
        }

        // 아래 — 물건 | 마법
        int ly = y0 + topH + 8;
        Panel(x0, ly, W, listH);
        int colX0 = x0 + 16, colX1 = narrow ? colX0 : x0 + W / 2 + 8, colW1 = narrow ? W - 32 : W / 2 - 24;
        int iy = ly + 12;
        DrawText(T($"물건 {items.Count}/8", 0xFFFFE070), colX0, iy);
        for (int i = 0; i < items.Count; i++)
        {
            var (id, on) = items[i];
            int ry = iy + (i + 1) * lineH;
            var mark = T(on ? "E" : " ", 0xFFFF9070);
            DrawText(mark, colX0, ry);
            var nm = NameImage(181 + id, g, on ? EquippedColor : 0xFFB8C8FF) ?? T(GameNames.Item(id), on ? EquippedColor : 0xFFB8C8FF);   // 장비한 것은 빨강(원작, w-139)
            int nx = colX0 + T("E ").W;
            DrawText(nm, nx, ry);
            string eff = ItemEffect(id);
            if (eff.Length > 0)
            {
                var e = T(eff, 0xFFC8F0B0, small);
                int ex = Math.Max(nx + nm.W + 10, colX0 + colW1 - e.W);
                DrawText(e, ex, ry + (nm.H - e.H) / 2);
            }
        }
        if (items.Count == 0) DrawText(T("없음", 0xFF8090B0), colX0, iy + lineH);

        int sy = narrow ? iy + (rowsLeft + 1) * lineH + lineH / 2 : iy;
        DrawText(T($"마법 {spells.Count}", 0xFFFFE070), colX1, sy);
        for (int i = 0; i < spells.Count; i++)
        {
            int m = spells[i], ry = sy + (i + 1) * lineH;
            bool can = _battle.SpellOf(m) is { } s && u.Mp >= s.Mp;
            DrawText(T(SpellNames.Of(m), can ? 0xFFB8C8FF : 0xFF6878A0), colX1, ry);
            if (_battle.SpellOf(m) is { } si)
            {
                var mp = T($"MP {si.Mp}", can ? 0xFFC8F0B0 : 0xFF607060, small);
                DrawText(mp, colX1 + colW1 - mp.W, ry);
            }
        }
        if (spells.Count == 0) DrawText(T("없음", 0xFF8090B0), colX1, sy + lineH);

        var who = T($"{_partyIndex + 1} / {_party.Count}", 0xFF9098A8, small);
        DrawText(who, x0 + W - who.W - 8, ly + listH + 6);
        DrawText(hint, x0 + 8, ly + listH + 6);
    }
}