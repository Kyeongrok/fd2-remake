namespace Fd2;

/// <summary>
/// 전투 오른쪽 위 접기 단추 — 배경 없이 아이콘만: 미니맵(지도) · 보물상자(상자). 누르면 그 창을 접고 편다(M · T 키와 같은 설정).
/// 접힌 창의 아이콘은 흐리게 보인다. 미니맵은 아이콘 밑에 펼친다.
/// </summary>
internal sealed partial class GameWindow
{
    private (int X, int Y, int W, int H) _miniIcon, _chestIcon, _escIcon;

    /// <summary>아이콘 줄 아래 끝 — 미니맵을 그 밑에 놓는다.</summary>
    private int _hudIconBottom = 12;

    private void DrawHudToggles(int fs)
    {
        _miniIcon = _chestIcon = _escIcon = default;
        _hudIconBottom = 12;
        if (_bs is BattleState.CutIn or BattleState.Talk) return;
        if (TouchUi) { DrawEscButton(fs); return; }
        int u = Math.Max(2, fs / 7), w = 12 * u, h = 10 * u, x = _clientW - 14 - w, y = 10;
        _miniIcon = (x, y, w, h);
        DrawMapIcon(x, y, u, _settings.ShowMinimap);
        if (_scene.Index < 30)
        {
            x -= w + 3 * u;
            _chestIcon = (x, y, w, h);
            DrawChestIcon(x, y, u, _settings.ShowChests);
        }
        _hudIconBottom = y + h + 6;
    }

    /// <summary>그 자리가 오른쪽 위 아이콘인가 — 아이콘 누르기는 지도 끌기로 받지 않는다.</summary>
    private bool HudHit(int mx, int my) =>
        new[] { _miniIcon, _chestIcon, _escIcon }.Any(r => r.W > 0 && mx >= r.X - 4 && my >= r.Y - 4 && mx < r.X + r.W + 4 && my < r.Y + r.H + 4);

    /// <summary>아이콘을 눌렀으면 그 창을 접고 펴고 true.</summary>
    private bool HudToggleClick(int mx, int my)
    {
        static bool In((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X - 4 && y >= r.Y - 4 && x < r.X + r.W + 4 && y < r.Y + r.H + 4;
        if (In(_escIcon, mx, my)) { OpenTouchMenu(); return true; }
        if (In(_miniIcon, mx, my)) _settings.ShowMinimap = !_settings.ShowMinimap;
        else if (In(_chestIcon, mx, my)) _settings.ShowChests = !_settings.ShowChests;
        else return false;
        _settings.Save();
        return true;
    }

    /// <summary>접힌 지도 — 양피지 세 폭, 초록 땅 · 파란 물, 어두운 테두리(지도 위에서도 보이게).</summary>
    private void DrawMapIcon(int x, int y, int u, bool on)
    {
        uint a = on ? 0xFF000000 : 0x90000000;
        void R(int px, int py, int pw, int ph, uint rgb) => FillRect(x + px * u, y + py * u, pw * u, ph * u, a | rgb & 0xFFFFFF);
        R(0, 0, 12, 10, 0x101820);
        R(1, 1, 10, 8, 0xE8D8A0);
        R(4, 1, 1, 8, 0xB8A070); R(8, 1, 1, 8, 0xB8A070);   // 접힌 금
        R(2, 2, 2, 3, 0x50A040); R(5, 4, 3, 3, 0x50A040);
        R(9, 2, 2, 2, 0x4080D0); R(2, 6, 2, 2, 0x4080D0);
        R(6, 2, 1, 1, 0xD03030);   // 지금 자리
    }

    /// <summary>보물상자 — 갈색 몸통 · 뚜껑, 금띠와 자물쇠.</summary>
    private void DrawChestIcon(int x, int y, int u, bool on)
    {
        uint a = on ? 0xFF000000 : 0x90000000;
        void R(int px, int py, int pw, int ph, uint rgb) => FillRect(x + px * u, y + py * u, pw * u, ph * u, a | rgb & 0xFFFFFF);
        R(0, 0, 12, 10, 0x101820);
        R(1, 1, 10, 3, 0x6B3F1B);   // 뚜껑
        R(1, 4, 10, 5, 0x8B5A2B);   // 몸통
        R(1, 4, 10, 1, 0xF0C040);   // 금띠
        R(3, 1, 1, 8, 0xF0C040); R(8, 1, 1, 8, 0xF0C040);
        R(5, 4, 2, 3, 0xF0E080); R(5, 6, 2, 1, 0x101820);   // 자물쇠
    }

    // ── 폰: 오른쪽 위 ESC 단추 하나 → 모바일 메뉴(보일 것 켜고 끄기 · 시스템 메뉴) ─────────────

    /// <summary>모바일 메뉴를 열 수 있는 때 — 우리 차례에 고르는 중.</summary>
    private bool CanOpenTouchMenu => InBattle && !_enemyPhase && _bs == BattleState.Select;

    private void DrawEscButton(int fs)
    {
        if (!CanOpenTouchMenu) return;
        var label = Text.Render("ESC", fs, 0xFFFFFFFF);
        int pad = fs / 2, w = label.W + pad * 2, h = label.H + pad, x = _clientW - 14 - w, y = 10;
        _escIcon = (x, y, w, h);
        FillRect(x, y, w, h, 0x90101020);
        FillRect(x, y, w, 1, 0xC0FFFFFF); FillRect(x, y + h - 1, w, 1, 0xC0FFFFFF);
        FillRect(x, y, 1, h, 0xC0FFFFFF); FillRect(x + w - 1, y, 1, h, 0xC0FFFFFF);
        DrawText(label, x + pad, y + pad / 2);
        _hudIconBottom = y + h + 6;
    }

    /// <summary>모바일 메뉴 — 칸 정보 · 상대 정보 · 보물상자 · 미니맵 · 승패 조건 · 조작법 켜고 끄기, 시스템 메뉴(턴 끝 · 기록 · 타이틀).</summary>
    private void OpenTouchMenu(int index = 0)
    {
        if (!CanOpenTouchMenu && _bs != BattleState.Menu) return;
        _stage = MenuStage.System;
        _menu.Clear();
        void Toggle(string label, bool on, Action flip) =>
            _menu.Add(($"{label}  [{(on ? "보임" : "숨김")}]", () => { flip(); _settings.Save(); OpenTouchMenu(_menuIndex); }, default));
        Toggle("칸 정보", _settings.TouchTileInfo, () => _settings.TouchTileInfo = !_settings.TouchTileInfo);
        Toggle("상대 정보", _settings.TouchUnitInfo, () => _settings.TouchUnitInfo = !_settings.TouchUnitInfo);
        Toggle("보물상자", _settings.TouchChests, () => _settings.TouchChests = !_settings.TouchChests);
        Toggle("미니맵", _settings.ShowMinimap, () => _settings.ShowMinimap = !_settings.ShowMinimap);
        Toggle("승패 조건", _settings.TouchGoals, () => _settings.TouchGoals = !_settings.TouchGoals);
        Toggle("조작법", _settings.TouchHelp, () => _settings.TouchHelp = !_settings.TouchHelp);
        _menu.Add(("시스템 메뉴 (턴 끝 · 기록 · 타이틀)", OpenSystemMenu, default));
        _menu.Add(("닫기", () => { _menu.Clear(); _bs = BattleState.Select; }, default));
        _menuIndex = Math.Clamp(index, 0, _menu.Count - 1);
        _bs = BattleState.Menu;
    }
}
