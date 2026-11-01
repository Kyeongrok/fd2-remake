using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 전투 정보 패널의 접기 · 펴기(창세기전3 파트1 처럼) — 보물상자 목록 · 미니맵 패널의 오른쪽 위 [−] 를 누르면 제목 줄만 남기고 접고, [+] 로 편다(M · T 키와 같은 설정).
/// 폰은 오른쪽 위 ESC 단추(원작 Esc 그대로 — 물리기 · 다음 아군) 와 메뉴 단추(→ 모바일 메뉴).
/// </summary>
internal sealed partial class GameWindow
{
    private (int X, int Y, int W, int H) _miniFold, _chestFold, _escIcon, _menuIcon, _mapIcon, _partyIcon, _readyIcon, _escIcon2, _townPrev, _townNext, _townEnter;

    private static readonly string[] ScreenLockNames = ["끔(폰 설정 따름)", "세로 고정", "가로 고정"];

    /// <summary>화면 방향 설정을 폰에 적용 — 안드로이드만.</summary>
    partial void ApplyScreenLock();

    private static string EscSpotName(int spot) => spot switch { 0 => "오른쪽 위", 1 => "오른쪽 아래", 3 => "왼쪽 아래", _ => "위 · 오른쪽 아래" };

    /// <summary>오른쪽 위 줄의 아래 끝 — 미니맵 패널을 그 밑에 놓는다(폰은 ESC 단추 밑).</summary>
    private int _hudIconBottom = 12;

    private void DrawHudToggles(int fs)
    {
        _miniFold = _chestFold = _escIcon = _menuIcon = _mapIcon = _partyIcon = _readyIcon = _escIcon2 = _townPrev = _townNext = _townEnter = default;
        _hudIconBottom = 12;
        _chestRows.Clear();
        if (_bs is BattleState.CutIn or BattleState.Talk) return;
        if (TouchUi) DrawTouchButtons(fs);
    }

    /// <summary>패널 오른쪽 위 접기 단추 — 펴 있으면 「−」, 접혀 있으면 「+」. 단추 자리를 돌려준다.</summary>
    private (int X, int Y, int W, int H) FoldButton(int x, int y, int size, bool open)
    {
        FillRect(x, y, size, size, 0xC0384068);
        FillRect(x, y, size, 1, 0xFFA8B8E8); FillRect(x, y + size - 1, size, 1, 0xFFA8B8E8);
        FillRect(x, y, 1, size, 0xFFA8B8E8); FillRect(x + size - 1, y, 1, size, 0xFFA8B8E8);
        int t = Math.Max(2, size / 8), l = size * 5 / 8;
        FillRect(x + (size - l) / 2, y + (size - t) / 2, l, t, 0xFFFFFFFF);
        if (!open) FillRect(x + (size - t) / 2, y + (size - l) / 2, t, l, 0xFFFFFFFF);
        return (x, y, size, size);
    }

    /// <summary>그 자리가 접기 단추 · ESC 단추인가 — 단추 누르기는 지도 끌기로 받지 않는다.</summary>
    private bool HudHit(int mx, int my) =>
        _chestRows.Select(c => c.Rect).Concat([_townPrev, _townNext, _townEnter, _miniFold, _chestFold, _escIcon, _escIcon2, _menuIcon, _mapIcon, _partyIcon, _readyIcon, _touchMini]).Any(r => r.W > 0 && mx >= r.X - 4 && my >= r.Y - 4 && mx < r.X + r.W + 4 && my < r.Y + r.H + 4);

    /// <summary>단추를 눌렀으면 그 패널을 접고 펴고(ESC 는 원작 Esc, 메뉴는 모바일 메뉴) true.</summary>
    private bool HudToggleClick(int mx, int my)
    {
        static bool In((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X - 4 && y >= r.Y - 4 && x < r.X + r.W + 4 && y < r.Y + r.H + 4;
        if (In(_escIcon, mx, my) || In(_escIcon2, mx, my)) { _mouseMoved = false; PlaySfx(Sound.UiBank, Sound.Cursor); BattleKey(Win32.VK_ESCAPE); return true; }
        if (In(_menuIcon, mx, my)) { OpenTouchMenu(); return true; }
        if (In(_townEnter, mx, my)) { _mouseMoved = false; PlaySfx(Sound.UiBank, Sound.Cursor); BattleKey(Win32.VK_RETURN); return true; }
        if (In(_townPrev, mx, my) || In(_townNext, mx, my))
        {
            _mouseMoved = false;
            PlaySfx(Sound.UiBank, Sound.Cursor);
            BattleKey(In(_townPrev, mx, my) ? Win32.VK_LEFT : Win32.VK_RIGHT);
            return true;
        }
        if (In(_readyIcon, mx, my)) { CursorToNextReady(); PlaySfx(Sound.UiBank, Sound.Cursor); return true; }
        if (In(_partyIcon, mx, my)) { PlaySfx(Sound.UiBank, Sound.Menu); OpenParty(); return true; }
        if (In(_mapIcon, mx, my)) { _touchMiniOpen = !_touchMiniOpen; PlaySfx(Sound.UiBank, Sound.Cursor); return true; }
        if (TouchMinimapClick(mx, my)) return true;
        if (_bs is BattleState.Select or BattleState.Target && _chestRows.FindIndex(c => In(c.Rect, mx, my)) is var row and >= 0)
        {
            // 보물상자 줄 — 커서만 그 칸으로(지도가 따라간다). 마우스 자리가 커서를 도로 가져가지 않게
            _mouseMoved = false;
            _cursor = (_chestRows[row].X, _chestRows[row].Y);
            _centerCursor = true;
            PlaySfx(Sound.UiBank, Sound.Cursor);
            return true;
        }
        if (In(_miniFold, mx, my)) _settings.ShowMinimap = !_settings.ShowMinimap;
        else if (In(_chestFold, mx, my)) _settings.ShowChests = !_settings.ShowChests;
        else return false;
        _settings.Save();
        return true;
    }

    // ── 폰: 오른쪽 위 ESC 단추(원작 Esc 키와 같다) · 메뉴 단추(→ 모바일 메뉴: 보일 것 켜고 끄기 · 시스템 메뉴) ─────────────

    /// <summary>모바일 메뉴를 열 수 있는 때 — 우리 차례에 고르는 중.</summary>
    private bool CanOpenTouchMenu => InBattle && !_enemyPhase && (_bs == BattleState.Select || TownTop);

    /// <summary>마을 첫 화면(건물 고르기)인가.</summary>
    private bool TownTop => _townTop && _stage == MenuStage.Town && _bs == BattleState.Menu && InTownView;

    /// <summary>모바일 메뉴를 마을에서 열었나 — 닫으면 마을로, 에디트는 동료 표 · 기록의 돈을 고친다.</summary>
    private bool _touchFromTown;

    /// <summary>모바일 메뉴를 닫는다 — 싸움이면 고르기로, 마을이면 마을 첫 화면으로.</summary>
    private void CloseTouchMenu()
    {
        _menu.Clear();
        if (_touchFromTown) { _touchFromTown = false; OpenTownMenu(_townSpot); }
        else _bs = BattleState.Select;
    }

    /// <summary>에디트 돈 — 마을에서는 기록의 돈까지(상점 · 저장이 그것을 본다).</summary>
    private void EditGold(int add)
    {
        _battle.Gold += add;
        if (_touchFromTown && _campaign is { } c) c.Gold = _battle.Gold;
    }

    /// <summary>에디트로 고친 사람을 동료 표에 — 마을의 사람은 동료 표로 세운 사본이다.</summary>
    private void EditCommit(Unit u)
    {
        if (!_touchFromTown || _campaign is not { } c) return;
        int i = c.Party.FindIndex(r => HeroRecord.Id(r) == u.NameId);
        if (i >= 0) c.Party[i] = HeroRecord.From(u);
    }

    /// <summary>ESC 단추를 보이는 때 — 우리 차례에 고르기 · 겨누기 · 메뉴(원작 Esc 가 듣는 때).</summary>
    private bool CanTouchEsc => InBattle && !_enemyPhase && _bs is BattleState.Select or BattleState.Target or BattleState.Menu;

    private void DrawTouchButtons(int fs)
    {
        int right = _clientW - 14, y = 10, bottom = 0;
        int spot = _settings.EscSpot;
        if (CanTouchEsc && spot is 0 or 2)
        {
            _escIcon = DrawHudButton("ESC", fs, right, y);
            right = _escIcon.X - 10;
            bottom = _escIcon.Y + _escIcon.H;
        }
        if (CanTouchEsc && spot is 1 or 2 or 3)
        {
            // 아래 ESC — 엄지가 닿는 자리에 크게(자주 쓰는 단추, 사용자 요청)
            int big = fs * 3 / 2;
            var label = Text.Render("ESC", big, 0xFFFFFFFF);
            int w = label.W + big / 2 * 2, h = label.H + big / 2;
            _escIcon2 = DrawHudButton("ESC", big, spot == 3 ? 14 + w : _clientW - 14, _clientH - h - 14);   // 3 은 왼쪽 아래
        }
        if (CanOpenTouchMenu)
        {
            _menuIcon = DrawHudButton("메뉴", fs, right, y);
            bottom = Math.Max(bottom, _menuIcon.Y + _menuIcon.H);
            // 지도 — 누르면 미니맵이 뜨고 한 번 더 누르면 닫힌다(사용자 요청)
            if (!TownTop) _mapIcon = DrawHudButton("지도", fs, _menuIcon.X - 10, y);
            _partyIcon = DrawHudButton("파티", fs, (TownTop ? _menuIcon.X : _mapIcon.X) - 10, y);   // 파티 정보 — 메뉴에서 빼 단추로(사용자 요청)
        }
        if (bottom > 0) _hudIconBottom = bottom + 6;
        if (TownTop)
        {
            // 마을 — 아래 ESC 와 같은 줄 · 같은 크기로 ◀ ▶ · 들어가기(그림을 가리지 않게, 사용자 요청). ESC 가 왼쪽 아래면 그 옆부터
            int big = fs * 3 / 2, gap = 10, by = _clientH - Text.Render("ESC", big, 0xFFFFFFFF).H - big / 2 - 14;
            int Width(string t) => Text.Render(t, big, 0xFFFFFFFF).W + big / 2 * 2;
            int bx = 14 + (spot == 3 ? Width("ESC") + gap : 0);
            (int X, int Y, int W, int H) Next(string t) { bx += Width(t); var r = DrawHudButton(t, big, bx, by); bx += gap; return r; }
            _townPrev = Next("◀");
            _townNext = Next("▶");
            _townEnter = Next(_townSpot == 2 ? "나가기" : "들어가기");   // 고른 건물에 들어가기, 출구면 나가기
        }
        DrawReadyCount(fs);
    }

    /// <summary>
    /// 폰 — 아래 가운데에 아직 안 움직인 우리 편 수(「남은 아군 3 / 5」). 누르면 Esc 처럼 다음 안 움직인 아군으로 커서가 간다(사용자 요청).
    /// </summary>
    private static readonly string[] ReadySpotNames = ["아래 가운데", "아래 왼쪽", "아래 오른쪽", "위 가운데", "숨김"];

    private void DrawReadyCount(int fs)
    {
        if (!InBattle || _enemyPhase || _bs is not (BattleState.Select or BattleState.Target)) return;   // 메뉴 · 묻는 창이 떠 있으면 숨긴다(누르기가 겹치지 않게)
        var mine = Controlled.Where(u => u.Hp > 0).ToList();
        if (mine.Count == 0) return;
        int left = mine.Count(Ready);
        // NPC 아군도 내가 움직이면 따로 센다 — 색은 지도의 체력 막대와 같게 노랑(사용자 요청)
        var heroes = mine.Where(u => !u.Ai).ToList();
        var npcs = mine.Where(u => u.Ai).ToList();
        int heroLeft = heroes.Count(Ready), npcLeft = npcs.Count(Ready);
        var label = npcs.Count == 0
            ? Text.Render($"남은 아군 {left} / {mine.Count}", fs, left > 0 ? 0xFFFFFFFF : 0xFFB0B0B0)
            : HJoin(Text.Render($"아군 {heroLeft} / {heroes.Count}", fs, heroLeft > 0 ? 0xFFFFFFFF : 0xFFB0B0B0),
                    Text.Render($"   NPC {npcLeft} / {npcs.Count}", fs, npcLeft > 0 ? 0xFFF0D040 : 0xFFB0B0B0));
        if (_settings.ReadySpot == 4) return;
        int pad = fs / 2, w = label.W + pad * 2, h = label.H + pad, x = (_clientW - w) / 2, y = _clientH - h - 8;
        switch (_settings.ReadySpot)
        {
            case 1: x = 14; break;
            case 2: (x, y) = (_clientW - w - 14, _clientH - h * 3 - 24); break;   // ESC 단추 위
            case 3: y = _hudIconBottom; break;
        }
        FillRect(x, y, w, h, 0xA0101020);
        FillRect(x, y, w, 1, 0x80FFFFFF); FillRect(x, y + h - 1, w, 1, 0x80FFFFFF);
        FillRect(x, y, 1, h, 0x80FFFFFF); FillRect(x + w - 1, y, 1, h, 0x80FFFFFF);
        DrawText(label, x + pad, y + pad / 2);
        _readyIcon = (x, y, w, h);
    }

    /// <summary>오른쪽 끝이 right 인 글자 단추를 그리고 자리를 돌려준다.</summary>
    private (int X, int Y, int W, int H) DrawHudButton(string text, int fs, int right, int y)
    {
        var label = Text.Render(text, fs, 0xFFFFFFFF);
        int pad = fs / 2, w = label.W + pad * 2, h = label.H + pad, x = right - w;
        FillRect(x, y, w, h, 0x90101020);
        FillRect(x, y, w, 1, 0xC0FFFFFF); FillRect(x, y + h - 1, w, 1, 0xC0FFFFFF);
        FillRect(x, y, 1, h, 0xC0FFFFFF); FillRect(x + w - 1, y, 1, h, 0xC0FFFFFF);
        DrawText(label, x + pad, y + pad / 2);
        return (x, y, w, h);
    }

    /// <summary>맨 위 줄의 이름 · 설명 — 시안(메뉴).</summary>
    private static readonly Dictionary<string, (string Name, string Sub)> MobileRows = new()
    {
        ["화면 표시"] = ("화면 표시", "칸 정보 · 상대 정보 · 보물상자 · 승패 조건"),
        ["설정"] = ("설정", "속도 · 회전 고정 · 소리 · 조작"),
        ["시스템 메뉴"] = ("시스템 메뉴", "턴 끝 · 행군 · 전황 기록"),
        ["에디트 — 돈 · 경험치"] = ("에디트", "돈 · 경험치"),
        ["세이브 연동(구글)"] = ("세이브 연동", "구글 계정에 기록 올리고 받기"),
        ["버그 리포트"] = ("버그 리포트", "문제를 적어 보내기"),
    };

    /// <summary>
    /// 폰 세로 모바일 메뉴 — 엄지가 닿는 아래 시트(사용자 요청, 시안). 위에 제목 · 지금 상황, 줄마다 이름과 값(켬 · 끔은 스위치), 맨 아래 금색 닫기.
    /// 맨 끝 줄이 늘 닫기다(<see cref="OpenTouchMenu"/>). 줄이 넘치면 끌어 넘기고, 닫기는 시트 아래에 고정.
    /// </summary>
    private void DrawMobileSheet()
    {
        double ui = _clientW / 390.0;
        int U(double v) => (int)Math.Round(v * ui);
        int large = U(20), body = U(16), small = U(12), m = U(20);
        FillRect(0, 0, _clientW, _clientH, 0xA0060916);
        int headH = U(68), rowH = U(60), footH = U(84), last = _menu.Count - 1;
        int fit = Math.Max(1, (_clientH - U(48) - headH - footH) / rowH), shown = Math.Min(fit, last);
        (_menuRowH, _menuFit) = (rowH, last > fit ? fit + 1 : _menu.Count);   // 끌기는 닫기 줄을 뺀 줄 수로 재므로 닫기 몫 하나를 더한다
        var sig = (_stage, _menu.Count, _menu[0].Label);
        if (sig != _menuSig) (_menuSig, _menuScroll, _menuScrolledByDrag) = (sig, 0, false);
        if (last <= fit) _menuScroll = 0;
        else
        {
            if (!_menuScrolledByDrag && _menuIndex >= 0 && _menuIndex < last)
            {
                if (_menuIndex < _menuScroll) _menuScroll = _menuIndex;
                else if (_menuIndex >= _menuScroll + fit) _menuScroll = _menuIndex - fit + 1;
            }
            _menuScroll = Math.Clamp(_menuScroll, 0, last - fit);
        }
        int h = headH + shown * rowH + footH, y = _clientH - h;
        _menuBox = (0, y + headH, _clientW, shown * rowH);
        FillRect(0, y, _clientW, h, 0xFF1C2850);
        FillRect(0, y, _clientW, Math.Max(2, U(2)), 0xFFC8A040);

        string title = _mobilePage switch { 0 => "메뉴", 1 => "화면 표시", 2 => "설정", _ => "에디트" };
        string now = _touchFromTown ? "마을" : $"{ChapterName(_scene.Index)} · {Turn}턴 · 아군 차례";
        var head = Text.Render(title, large, 0xFFEEF0FF);
        var state = Text.Render(now, small, 0xFFC4CAE6, bold: false);
        var gold = Text.Render($"{_battle.Gold:N0}원", body, 0xFFE8C870);
        DrawText(head, m, y + U(12));
        DrawText(state, m, y + U(12) + head.H);
        DrawText(gold, _clientW - m - gold.W, y + (headH - gold.H) / 2);
        FillRect(0, y + headH - 1, _clientW, 1, 0xFF3A4678);

        for (int i = 0; i < last; i++)
        {
            int row = i - _menuScroll;
            if (row < 0 || row >= shown) { _menu[i] = (_menu[i].Label, _menu[i].Do, default); continue; }
            var rect = (X: 0, Y: y + headH + row * rowH, W: _clientW, H: rowH);
            if (_mouseX >= 0 && _mouseY >= rect.Y && _mouseY < rect.Y + rowH && _mouseMoved && !_menuDragging) _menuIndex = i;
            if (i == _menuIndex) FillRect(0, rect.Y, _clientW, rowH - 1, 0xFF34447E);
            FillRect(0, rect.Y + rowH - 1, _clientW, 1, 0xFF2C3768);
            string label = _menu[i].Label;
            if (_menuGlyphs.ContainsKey(i))
            {
                var img = MenuRow(label, i, body);
                DrawText(img, m, rect.Y + (rowH - img.H) / 2);
            }
            else if (MobileRows.TryGetValue(label, out var r) && _mobilePage == 0)
            {
                var name = Text.Render(r.Name, body, 0xFFEEF0FF);
                var note = Text.Render(r.Sub, small, 0xFFC4CAE6, bold: false);
                DrawText(name, m, rect.Y + (rowH - name.H - note.H) / 2);
                DrawText(note, m, rect.Y + (rowH - name.H - note.H) / 2 + name.H);
            }
            else
            {
                int at = label.LastIndexOf("  [", StringComparison.Ordinal);
                var name = Text.Render(at >= 0 ? label[..at] : label, body, 0xFFEEF0FF);
                DrawText(name, m, rect.Y + (rowH - name.H) / 2);
                if (at >= 0)
                {
                    string value = label[(at + 3)..].TrimEnd(']');
                    int dash = value.IndexOf(" — ", StringComparison.Ordinal);
                    if (dash > 0) value = value[..dash];
                    if (value is "켬" or "보임" or "끔" or "숨김")
                    {
                        bool on = value is "켬" or "보임";
                        int tw = U(52), th = U(30), tx = _clientW - m - tw, ty = rect.Y + (rowH - th) / 2, k = U(22);
                        FillRect(tx, ty, tw, th, on ? 0xFFC8A040 : 0xFF3A4678);
                        FillRect(on ? tx + tw - k - U(4) : tx + U(4), ty + U(4), k, k, 0xFFEEF0FF);
                    }
                    else
                    {
                        var v = Text.Render(value, U(14), 0xFFE8C870);
                        DrawText(v, _clientW - m - v.W, rect.Y + (rowH - v.H) / 2);
                    }
                }
            }
            _menu[i] = (label, _menu[i].Do, rect);
        }
        if (last > shown)
        {
            int barH = rowH * shown, thumb = Math.Max(U(24), barH * shown / last), bar = (barH - thumb) * _menuScroll / Math.Max(1, last - shown);
            FillRect(_clientW - U(4), y + headH + bar, U(4), thumb, 0xE0C8A040);
        }
        var close = (X: U(16), Y: _clientH - footH + U(12), W: _clientW - 2 * U(16), H: U(52));
        FillRect(close.X, close.Y, close.W, close.H, 0xFFC8A040);
        var word = Text.Render(_menu[last].Label, body, 0xFF141A33);
        DrawText(word, close.X + (close.W - word.W) / 2, close.Y + (close.H - word.H) / 2);
        _menu[last] = (_menu[last].Label, _menu[last].Do, close);
    }

    /// <summary>모바일 메뉴의 쪽 — 0 맨 위(최대 넷), 1 화면 표시(켜고 끄기), 2 설정(소리 · 단축키), 3 에디트(돈 · 사람 고르기), 4 에디트 — 고른 사람 경험치.</summary>
    private int _mobilePage;

    /// <summary>에디트에서 고른 사람.</summary>
    private Unit? _editUnit;

    /// <summary>
    /// 모바일 메뉴 — 맨 위는 화면 표시 · 설정 · 시스템 메뉴 · 닫기(사용자 요청, 파티 정보는 오른쪽 위 「파티」 단추로). Esc · 메뉴 밖 누르기로도 닫힌다.
    /// 화면 표시 = 칸 정보 · 상대 정보 · 보물상자 · 승패 조건 · 조작법 켜고 끄기, 설정 = 소리 크기 · 단축키 · 패드 설정(시스템 십자 메뉴의 설정 단추도 여기로).
    /// </summary>
    private void OpenTouchMenu(int index = 0, int page = 0)
    {
        if (!CanOpenTouchMenu && _bs != BattleState.Menu) return;
        if (!(_bs == BattleState.Menu && _stage == MenuStage.Mobile)) PlaySfx(Sound.UiBank, Sound.Menu);   // 처음 뜰 때만(켜고 끄며 다시 그릴 때는 말고)
        if (TownTop) _touchFromTown = true;
        _stage = MenuStage.Mobile;
        _mobilePage = page;
        _menu.Clear();
        _menuGlyphs.Clear(); _menuIcons.Clear();
        void Close() => CloseTouchMenu();
        void Toggle(string label, bool on, Action flip) =>
            _menu.Add(($"{label}  [{(on ? "보임" : "숨김")}]", () => { flip(); _settings.Save(); OpenTouchMenu(_menuIndex, 1); }, default));
        switch (page)
        {
            case 1:
                Toggle("칸 정보", _settings.TouchTileInfo, () => _settings.TouchTileInfo = !_settings.TouchTileInfo);
                Toggle("상대 정보", _settings.TouchUnitInfo, () => _settings.TouchUnitInfo = !_settings.TouchUnitInfo);
                Toggle("보물상자", _settings.TouchChests, () => _settings.TouchChests = !_settings.TouchChests);
                Toggle("승패 조건", _settings.TouchGoals, () => _settings.TouchGoals = !_settings.TouchGoals);
                Toggle("조작법", _settings.TouchHelp, () => _settings.TouchHelp = !_settings.TouchHelp);
                break;
            case 2:
                _menu.Add(($"고를 때 확대  [{(_settings.TouchFocusZoom ? "켬" : "끔")}]", () =>
                {
                    _settings.TouchFocusZoom = !_settings.TouchFocusZoom;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"공통 가방  [{(_settings.SharedBag ? "켬" : "끔")}]", () =>
                {
                    _settings.SharedBag = !_settings.SharedBag;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"NPC 아군 내가 움직이기  [{(_settings.ControlAllies ? "켬" : "끔")}]", () =>
                {
                    _settings.ControlAllies = !_settings.ControlAllies;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"남은 아군 표시  [{ReadySpotNames[Math.Clamp(_settings.ReadySpot, 0, 4)]}]", () =>
                {
                    _settings.ReadySpot = (_settings.ReadySpot + 1) % ReadySpotNames.Length;   // 누를 때마다 다음 자리
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"미니맵 보물상자  [{(_settings.MiniChests ? "켬" : "끔")}]", () =>
                {
                    _settings.MiniChests = !_settings.MiniChests;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"적 공격 장면  [{(_settings.SkipEnemyFights ? "건너뛰기" : "보기")}]", () =>
                {
                    _settings.SkipEnemyFights = !_settings.SkipEnemyFights;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"ESC 단추 자리  [{EscSpotName(_settings.EscSpot)}]", () =>
                {
                    _settings.EscSpot = (_settings.EscSpot + 1) % 4;   // 누를 때마다 다음 자리
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"전투 모션 속도  [{SpellSpeedName(_settings.StrikeSpeed)}]", () =>
                {
                    int at = Array.IndexOf(SpellSpeeds, _settings.StrikeSpeed);
                    _settings.StrikeSpeed = SpellSpeeds[(at + 1) % SpellSpeeds.Length];
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"마법 속도  [{SpellSpeedName(_settings.SpellSpeed)}]", () =>
                {
                    int at = Array.IndexOf(SpellSpeeds, _settings.SpellSpeed);
                    _settings.SpellSpeed = SpellSpeeds[(at + 1) % SpellSpeeds.Length];   // 누를 때마다 다음 배율
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"적 이동 속도  [{SpellSpeedName(_settings.EnemySpeed)}]", () =>
                {
                    int at = Array.IndexOf(EnemySpeeds, _settings.EnemySpeed);
                    _settings.EnemySpeed = EnemySpeeds[(at + 1) % EnemySpeeds.Length];
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                if (OperatingSystem.IsAndroid())
                    _menu.Add(($"화면 회전 고정  [{ScreenLockNames[Math.Clamp(_settings.ScreenLock, 0, 2)]}]", () =>
                    {
                        _settings.ScreenLock = (_settings.ScreenLock + 1) % ScreenLockNames.Length;
                        _settings.Save();
                        ApplyScreenLock();
                        OpenTouchMenu(_menuIndex, 2);
                    }, default));
                _menu.Add(("소리 크기", () => { Close(); OpenVolume(); }, default));
                _menu.Add(("단축키 · 패드 설정", () => { Close(); _keysOpen = true; _keysRow = 0; _keysCapture = -1; }, default));
                break;
            case 3:
                // 에디트(사용자 요청) — 돈 올리기, 우리 편마다 경험치. 경험치는 원작 경험 함수(GainExp)로 넣어 100 이 넘으면 바로 레벨이 오른다
                _menu.Add(($"돈 +1,000  (지금 {_battle.Gold:N0})", () => { EditGold(1000); OpenTouchMenu(_menuIndex, 3); }, default));
                _menu.Add(("돈 +10,000", () => { EditGold(10000); OpenTouchMenu(_menuIndex, 3); }, default));
                foreach (var u in _touchFromTown ? PartyMembers().Where(u => u.Hp > 0) : _scene.Units.Where(u => u.Team == 2 && u.Hp > 0).OrderBy(u => u.Slot))
                {
                    var who = u;
                    _menuGlyphs[_menu.Count] = Battle.NameLine(u);
                    _menu.Add(($"Lv {u.Level}  EX {u.Exp:00}  ", () => { _editUnit = who; OpenTouchMenu(0, 4); }, default));
                }
                break;
            case 4 when _editUnit is { } eu:
                _menuGlyphs[_menu.Count] = Battle.NameLine(eu);
                _menu.Add(($"Lv {eu.Level}  EX {eu.Exp:00} — ", () => { }, default));
                foreach (int add in new[] { 10, 50, 100 })
                {
                    int e = add;
                    _menu.Add(($"경험치 +{e}{(e == 100 ? "  (레벨 업)" : "")}", () =>
                    {
                        var notes = _battle.GainExp(eu, e);
                        EditCommit(eu);
                        if (notes.Count == 0) { OpenTouchMenu(_menuIndex, 4); return; }   // 최대 레벨 등 — 오르지 않음
                        int row = _menuIndex;
                        _menu.Clear();
                        ShowNotices(notes, () => { _bs = BattleState.Menu; OpenTouchMenu(row, 4); });
                    }, default));
                }
                _menu.Add(("돌아가기", () => OpenTouchMenu(0, 3), default));
                break;
            default:
                _menu.Add(("화면 표시", () => OpenTouchMenu(0, 1), default));
                _menu.Add(("설정", () => OpenTouchMenu(0, 2), default));
                if (!_touchFromTown) _menu.Add(("시스템 메뉴", OpenSystemMenu, default));   // 턴 끝 · 전황 기록은 싸움 것
                _menu.Add(("에디트 — 돈 · 경험치", () => OpenTouchMenu(0, 3), default));
                _menu.Add(("세이브 연동(구글)", () => { Close(); OpenCloudDialog(); }, default));
                _menu.Add(("버그 리포트", () => { Close(); OpenBugReport(); }, default));
                break;
        }
        _menu.Add(("닫기", Close, default));   // 맨 아래 닫기(사용자 요청) — 밖을 누르거나 Esc 로도 닫힌다
        _menuIndex = Math.Clamp(index, 0, _menu.Count - 1);
        _bs = BattleState.Menu;
    }

    // ── 폰: 대화 · 프롤로그 중에도 오른쪽 위 「메뉴」 단추 — 대사 건너뛰기 · 타이틀로 · 게임 끝내기(사용자 요청) ─────────────

    private bool _talkMenuOpen;
    private (int X, int Y, int W, int H) _talkMenuIcon;
    private readonly List<(int X, int Y, int W, int H)> _talkMenuRows = new();

    /// <summary>대화 메뉴 단추를 보이는 때 — 폰에서 대사(전투 · 마을)나 프롤로그가 나오는 중.</summary>
    private bool TalkMenuShown => TouchUi && (_screen == Screen.Prologue || _screen == Screen.Game && Talking) && !_partyOpen && !_volumeOpen && !_keysOpen;

    private (string Label, Action Do)[] TalkMenuItems =>
    [
        ("대사 건너뛰기", () => { if (_screen == Screen.Prologue) SkipPrologue(); else EndTalk(); }),
        ("타이틀로", ToTitle),
        ("게임 끝내기", () => _running = false),
        ("닫기", () => { }),
    ];

    private void DrawTalkMenu()
    {
        _talkMenuIcon = default;
        _talkMenuRows.Clear();
        if (!TalkMenuShown) { _talkMenuOpen = false; return; }
        int fs = Math.Max(14, 7 * HudZoom);
        _talkMenuIcon = DrawHudButton("메뉴", fs, _clientW - 14, 10);
        if (!_talkMenuOpen) return;
        var items = TalkMenuItems;
        int big = fs * 4 / 3;
        var imgs = items.Select(i => Text.Render(i.Label, big, 0xFFFFFFFF)).ToList();
        int rowH = imgs.Max(i => i.H) + big / 2, w = imgs.Max(i => i.W) + big * 2, h = rowH * items.Length + 16;
        int x = (_clientW - w) / 2, y = (_clientH - h) / 2;
        FillRect(0, 0, _clientW, _clientH, 0x80000000);
        FillRect(x, y, w, h, 0xF0182040);
        FillRect(x, y, w, 2, 0xFF8090C0); FillRect(x, y + h - 2, w, 2, 0xFF8090C0);
        FillRect(x, y, 2, h, 0xFF8090C0); FillRect(x + w - 2, y, 2, h, 0xFF8090C0);
        for (int i = 0; i < items.Length; i++)
        {
            var r = (X: x + 8, Y: y + 8 + i * rowH, W: w - 16, H: rowH - 4);
            FillRect(r.X, r.Y, r.W, r.H, 0xC0304070);
            DrawText(imgs[i], x + (w - imgs[i].W) / 2, r.Y + (r.H - imgs[i].H) / 2);
            _talkMenuRows.Add(r);
        }
    }

    /// <summary>대화 메뉴 단추 · 줄을 눌렀으면 처리하고 true. 메뉴가 떠 있을 때 밖을 누르면 닫는다(대사는 넘기지 않는다).</summary>
    private bool TalkMenuClick(int mx, int my)
    {
        if (!TalkMenuShown) return false;
        static bool In((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X - 4 && y >= r.Y - 4 && x < r.X + r.W + 4 && y < r.Y + r.H + 4;
        if (!_talkMenuOpen)
        {
            if (!In(_talkMenuIcon, mx, my)) return false;
            _talkMenuOpen = true;
            PlaySfx(Sound.UiBank, Sound.Menu);
            return true;
        }
        _talkMenuOpen = false;
        for (int i = 0; i < _talkMenuRows.Count; i++)
            if (In(_talkMenuRows[i], mx, my)) { TalkMenuItems[i].Do(); break; }
        return true;
    }
}
