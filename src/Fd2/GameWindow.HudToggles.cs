using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 전투 정보 패널의 접기 · 펴기(창세기전3 파트1 처럼) — 보물상자 목록 · 미니맵 패널의 오른쪽 위 [−] 를 누르면 제목 줄만 남기고 접고, [+] 로 편다(M · T 키와 같은 설정).
/// 폰은 오른쪽 위 ESC 단추(원작 Esc 그대로 — 물리기 · 다음 아군) 와 메뉴 단추(→ 모바일 메뉴).
/// </summary>
internal sealed partial class GameWindow
{
    private (int X, int Y, int W, int H) _miniFold, _chestFold, _escIcon, _menuIcon, _mapIcon, _miniClose;

    /// <summary>오른쪽 위 줄의 아래 끝 — 미니맵 패널을 그 밑에 놓는다(폰은 ESC 단추 밑).</summary>
    private int _hudIconBottom = 12;

    private void DrawHudToggles(int fs)
    {
        _miniFold = _chestFold = _escIcon = _menuIcon = _mapIcon = _miniClose = default;
        _hudIconBottom = 12;
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
        new[] { _miniFold, _chestFold, _escIcon, _menuIcon, _mapIcon, _miniClose, _touchMini }.Any(r => r.W > 0 && mx >= r.X - 4 && my >= r.Y - 4 && mx < r.X + r.W + 4 && my < r.Y + r.H + 4);

    /// <summary>단추를 눌렀으면 그 패널을 접고 펴고(ESC 는 원작 Esc, 메뉴는 모바일 메뉴) true.</summary>
    private bool HudToggleClick(int mx, int my)
    {
        static bool In((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X - 4 && y >= r.Y - 4 && x < r.X + r.W + 4 && y < r.Y + r.H + 4;
        if (In(_escIcon, mx, my)) { _mouseMoved = false; PlaySfx(Sound.UiBank, Sound.Cursor); BattleKey(Win32.VK_ESCAPE); return true; }
        if (In(_menuIcon, mx, my)) { OpenTouchMenu(); return true; }
        if (In(_mapIcon, mx, my)) { _touchMiniOpen = !_touchMiniOpen; PlaySfx(Sound.UiBank, Sound.Cursor); return true; }
        if (In(_miniClose, mx, my)) { _touchMiniOpen = false; PlaySfx(Sound.UiBank, Sound.Cursor); return true; }
        if (TouchMinimapClick(mx, my)) return true;
        if (In(_miniFold, mx, my)) _settings.ShowMinimap = !_settings.ShowMinimap;
        else if (In(_chestFold, mx, my)) _settings.ShowChests = !_settings.ShowChests;
        else return false;
        _settings.Save();
        return true;
    }

    // ── 폰: 오른쪽 위 ESC 단추(원작 Esc 키와 같다) · 메뉴 단추(→ 모바일 메뉴: 보일 것 켜고 끄기 · 시스템 메뉴) ─────────────

    /// <summary>모바일 메뉴를 열 수 있는 때 — 우리 차례에 고르는 중.</summary>
    private bool CanOpenTouchMenu => InBattle && !_enemyPhase && _bs == BattleState.Select;

    /// <summary>ESC 단추를 보이는 때 — 우리 차례에 고르기 · 겨누기 · 메뉴(원작 Esc 가 듣는 때).</summary>
    private bool CanTouchEsc => InBattle && !_enemyPhase && _bs is BattleState.Select or BattleState.Target or BattleState.Menu;

    private void DrawTouchButtons(int fs)
    {
        int right = _clientW - 14, y = 10, bottom = 0;
        if (CanTouchEsc)
        {
            _escIcon = DrawHudButton("ESC", fs, right, y);
            right = _escIcon.X - 10;
            bottom = _escIcon.Y + _escIcon.H;
        }
        if (CanOpenTouchMenu)
        {
            _menuIcon = DrawHudButton("메뉴", fs, right, y);
            bottom = Math.Max(bottom, _menuIcon.Y + _menuIcon.H);
            // 지도 — 누르면 미니맵이 뜨고, 미니맵 오른쪽 위 ✕ 로 닫는다(사용자 요청)
            _mapIcon = DrawHudButton("지도", fs, _menuIcon.X - 10, y);
        }
        if (bottom > 0) _hudIconBottom = bottom + 6;
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

    /// <summary>모바일 메뉴의 쪽 — 0 맨 위(최대 넷), 1 화면 표시(켜고 끄기), 2 설정(소리 · 단축키).</summary>
    private int _mobilePage;

    /// <summary>
    /// 모바일 메뉴 — 맨 위는 넷만: 화면 표시 · 파티 정보 · 설정 · 시스템 메뉴(사용자 요청). 닫기는 Esc · 메뉴 밖 누르기.
    /// 화면 표시 = 칸 정보 · 상대 정보 · 보물상자 · 승패 조건 · 조작법 켜고 끄기, 설정 = 소리 크기 · 단축키 · 패드 설정(시스템 십자 메뉴의 설정 단추도 여기로).
    /// </summary>
    private void OpenTouchMenu(int index = 0, int page = 0)
    {
        if (!CanOpenTouchMenu && _bs != BattleState.Menu) return;
        if (!(_bs == BattleState.Menu && _stage == MenuStage.Mobile)) PlaySfx(Sound.UiBank, Sound.Menu);   // 처음 뜰 때만(켜고 끄며 다시 그릴 때는 말고)
        _stage = MenuStage.Mobile;
        _mobilePage = page;
        _menu.Clear();
        _menuGlyphs.Clear();
        void Close() { _menu.Clear(); _bs = BattleState.Select; }
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
                _menu.Add(("소리 크기", () => { Close(); OpenVolume(); }, default));
                _menu.Add(("단축키 · 패드 설정", () => { Close(); _keysOpen = true; _keysRow = 0; _keysCapture = -1; }, default));
                break;
            default:
                _menu.Add(("화면 표시", () => OpenTouchMenu(0, 1), default));
                _menu.Add(("파티 정보", () => { Close(); OpenParty(); }, default));
                _menu.Add(("설정", () => OpenTouchMenu(0, 2), default));
                _menu.Add(("시스템 메뉴", OpenSystemMenu, default));
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
