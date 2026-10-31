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

    /// <summary>모바일 메뉴 — 칸 정보 · 상대 정보 · 보물상자 · 미니맵 · 승패 조건 · 조작법 켜고 끄기, 파티 정보 · 소리 크기, 시스템 메뉴(턴 끝 · 기록 · 타이틀).</summary>
    private void OpenTouchMenu(int index = 0)
    {
        if (!CanOpenTouchMenu && !(_bs == BattleState.Menu && _stage == MenuStage.Mobile)) return;
        if (_bs != BattleState.Menu) PlaySfx(Sound.UiBank, Sound.Menu);   // 처음 뜰 때만(켜고 끄며 다시 그릴 때는 말고)
        _stage = MenuStage.Mobile;
        _menu.Clear();
        void Toggle(string label, bool on, Action flip) =>
            _menu.Add(($"{label}  [{(on ? "보임" : "숨김")}]", () => { flip(); _settings.Save(); OpenTouchMenu(_menuIndex); }, default));
        Toggle("칸 정보", _settings.TouchTileInfo, () => _settings.TouchTileInfo = !_settings.TouchTileInfo);
        Toggle("상대 정보", _settings.TouchUnitInfo, () => _settings.TouchUnitInfo = !_settings.TouchUnitInfo);
        Toggle("보물상자", _settings.TouchChests, () => _settings.TouchChests = !_settings.TouchChests);
        Toggle("승패 조건", _settings.TouchGoals, () => _settings.TouchGoals = !_settings.TouchGoals);
        Toggle("조작법", _settings.TouchHelp, () => _settings.TouchHelp = !_settings.TouchHelp);
        _menu.Add(("파티 정보", () => { _menu.Clear(); _bs = BattleState.Select; OpenParty(); }, default));
        _menu.Add(("소리 크기", () => { _menu.Clear(); _bs = BattleState.Select; OpenVolume(); }, default));
        _menu.Add(("단축키 · 패드 설정", () => { _menu.Clear(); _bs = BattleState.Select; _keysOpen = true; _keysRow = 0; _keysCapture = -1; }, default));   // 타이틀에서 옮김
        _menu.Add(("시스템 메뉴 (턴 끝 · 기록 · 타이틀)", OpenSystemMenu, default));
        _menu.Add(("닫기", () => { _menu.Clear(); _bs = BattleState.Select; }, default));
        _menuIndex = Math.Clamp(index, 0, _menu.Count - 1);
        _bs = BattleState.Menu;
    }
}
