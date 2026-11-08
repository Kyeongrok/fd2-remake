using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 타이틀 「Setup」 창(사용자 요청 w-102) — 게임 안 메뉴 창(폰 메뉴)과 같은 꼴: 남색 바탕 · 금줄 · 큰 제목, 줄마다 이름과 오른쪽 켜고 끄기 단추 · 막대, 아래 금색 「닫기」.
/// 위에 탭 줄 「기본 · 모드」(w-163): 기본 = 오프닝 보기 · 시작 화면 그림 · 배경음악 · 효과음, 모드 = 모드로 만든 것들 켜고 끄기(폰 · PC 공통 — 고해상도는 PC 모드 창에만).
/// 줄 번호: 0 탭 줄, 1 ~ n 이 탭의 줄, n+1 닫기. ↑↓ 고르기, ←→ 탭 바꾸기 · 막대 5% · 켜고 끄기, Enter 켜고 끄기 · 닫기, Esc · 창 밖 누르기 닫기.
/// </summary>
internal sealed partial class GameWindow
{
    private bool _setupOpen;
    private int _setupRow, _setupDrag = -1, _setupTab;
    private (int X, int Y, int W, int H) _setupBox;
    private readonly List<(int X, int Y, int W, int H)> _setupRows = new();
    private readonly Dictionary<int, (int X, int Y, int W, int H)> _setupTracks = new();
    private readonly (int X, int Y, int W, int H)[] _setupTabs = new (int, int, int, int)[2];

    private static readonly string[] SetupTabNames = ["기본", "모드"];

    /// <summary>탭의 한 줄 — 켜고 끄기(On · Flip) 또는 막대(Volume: 0 배경음악 · 1 효과음).</summary>
    private sealed record SetupItem(string Label, Func<bool>? On = null, Action? Flip = null, int Volume = -1, string? Note = null);

    private List<SetupItem> SetupItems() => _setupTab == 0
        ?
        [
            new("오프닝 보기", () => _settings.ShowOpening, () => _settings.ShowOpening = !_settings.ShowOpening),
            new("시작 화면 그림", () => _settings.ShowTitleImage, () => _settings.ShowTitleImage = !_settings.ShowTitleImage),
            new("배경음악", Volume: 0),
            new("효과음", Volume: 1),
        ]
        :
        [
            new("이동 · 공격 범위 보기", () => _settings.ShowRange, () => _settings.ShowRange = !_settings.ShowRange),
            new("캐릭터 정보 창", () => TouchUi ? _settings.TouchUnitInfo : _settings.ShowUnitInfo,
                () => { if (TouchUi) _settings.TouchUnitInfo = !_settings.TouchUnitInfo; else _settings.ShowUnitInfo = !_settings.ShowUnitInfo; }),
            new("공통 가방", () => _settings.SharedBag, ToggleSharedBag, Note: "장비 안 한 물건 · 소모품을 한 곳에"),
            new("미니맵 보물상자", () => _settings.MiniChests, () => _settings.MiniChests = !_settings.MiniChests),
            new("적 공격 장면 건너뛰기", () => _settings.SkipEnemyFights, () => _settings.SkipEnemyFights = !_settings.SkipEnemyFights),
            new("아군 법사 이동 후 마법", () => _settings.MoveThenCast, () => _settings.MoveThenCast = !_settings.MoveThenCast, Note: "끄면 제자리에서만 마법"),
            new("마법 피해 · 회복 %", () => _settings.SpellDamagePct, () => _settings.SpellDamagePct = !_settings.SpellDamagePct),
            new("필살기 몸짓 · 이펙트 따로", () => _settings.SplitSkillMotion, () => _settings.SplitSkillMotion = !_settings.SplitSkillMotion, Note: "끄면 원작 그대로"),
            new("컴퓨터 아군 직접 조종", () => _settings.ControlAllies, () => _settings.ControlAllies = !_settings.ControlAllies),
            new("지도 아래 남은 아군", () => _settings.ReadyBelowMap, () => _settings.ReadyBelowMap = !_settings.ReadyBelowMap, Note: "지도를 줄여 아래가 비면"),
        ];

    private int SetupCloseRow => SetupItems().Count + 1;

    private void OpenSetup()
    {
        PlaySfx(Sound.UiBank, Sound.Menu);
        (_setupOpen, _setupRow, _setupDrag) = (true, 1, -1);
    }

    private void CloseSetup()
    {
        (_setupOpen, _setupDrag) = (false, -1);
        _settings.Save();
    }

    private void SetupTabTo(int tab)
    {
        if (tab == _setupTab) return;
        _setupTab = tab;
        _setupTracks.Clear();
        PlaySfx(77, 2);
    }

    private SetupItem? ItemAt(int row) => row >= 1 && SetupItems() is var items && row - 1 < items.Count ? items[row - 1] : null;

    private void SetupToggle(SetupItem it)
    {
        it.Flip?.Invoke();
        PlaySfx(Sound.UiBank, Sound.Cursor);
        _settings.Save();
    }

    private int VolumeNow(int v) => v == 0 ? _settings.MusicVolume : _settings.SoundVolume;

    /// <summary>줄을 step% 만큼 — 탭 줄이면 탭을 바꾸고, 켜고 끄기 줄이면 뒤집는다.</summary>
    private void SetupStep(int row, int step)
    {
        if (row == 0) { SetupTabTo(step < 0 ? 0 : 1); return; }
        if (ItemAt(row) is not { } it) return;
        if (it.Flip != null) { SetupToggle(it); return; }
        if (it.Volume < 0) return;
        int now = VolumeNow(it.Volume);
        SetVolume(it.Volume, (now + step) / 5 * 5 + (step < 0 && now % 5 != 0 ? 5 : 0));
        if (it.Volume == 1) PlaySfx(Sound.UiBank, Sound.Cursor);
        _settings.Save();
    }

    private bool SetupKey(int vk)
    {
        if (!_setupOpen) return false;
        vk = ActionOf(vk) switch
        {
            KeyAction.MoveUp => Win32.VK_UP, KeyAction.MoveDown => Win32.VK_DOWN,
            KeyAction.MoveLeft => Win32.VK_LEFT, KeyAction.MoveRight => Win32.VK_RIGHT, _ => vk,
        };
        int rows = SetupCloseRow + 1;
        switch (vk)
        {
            case Win32.VK_UP: _setupRow = (_setupRow + rows - 1) % rows; PlaySfx(77, 2); break;
            case Win32.VK_DOWN: _setupRow = (_setupRow + 1) % rows; PlaySfx(77, 2); break;
            case Win32.VK_LEFT: SetupStep(_setupRow, -5); break;
            case Win32.VK_RIGHT: SetupStep(_setupRow, 5); break;
            case Win32.VK_ESCAPE: CloseSetup(); break;
            case Win32.VK_RETURN or Win32.VK_SPACE:
                if (_setupRow == 0) SetupTabTo(1 - _setupTab);
                else if (_setupRow == SetupCloseRow) CloseSetup();
                else if (ItemAt(_setupRow) is { Flip: not null } it) SetupToggle(it);
                break;
        }
        _setupRow = Math.Min(_setupRow, SetupCloseRow);
        return true;
    }

    private int SetupAt(int row, int x)
    {
        int now = ItemAt(row) is { Volume: >= 0 } it ? VolumeNow(it.Volume) : 0;
        return !_setupTracks.TryGetValue(row, out var t) || t.W <= 0 ? now : (int)Math.Round(Math.Clamp((x - t.X) / (double)t.W, 0, 1) * 100);
    }

    private bool SetupPress(int x, int y)
    {
        if (!_setupOpen) return false;
        if (!InRect(_setupBox, x, y)) { CloseSetup(); return true; }
        for (int t = 0; t < _setupTabs.Length; t++)
            if (InRect(_setupTabs[t], x, y)) { _setupRow = 0; SetupTabTo(t); return true; }
        for (int row = 1; row < _setupRows.Count; row++)
        {
            if (!InRect(_setupRows[row], x, y)) continue;
            _setupRow = row;
            if (row == SetupCloseRow) CloseSetup();
            else if (ItemAt(row) is { } it)
            {
                if (it.Flip != null) SetupToggle(it);
                else if (it.Volume >= 0)
                {
                    _setupDrag = row;
                    SetVolume(it.Volume, SetupAt(row, x));
                }
            }
            break;
        }
        return true;
    }

    private bool SetupMove(int x, int y)
    {
        if (!_setupOpen) return false;
        if (_setupDrag >= 1 && ItemAt(_setupDrag) is { Volume: >= 0 } it) SetVolume(it.Volume, SetupAt(_setupDrag, x));
        else if (_mouseMoved) for (int row = 1; row < _setupRows.Count; row++) if (InRect(_setupRows[row], x, y)) _setupRow = row;
        return true;
    }

    private bool SetupRelease()
    {
        if (!_setupOpen) return false;
        if (_setupDrag >= 1 && ItemAt(_setupDrag) is { Volume: >= 0 } it)
        {
            if (it.Volume == 1) PlaySfx(Sound.UiBank, Sound.Cursor);
            _settings.Save();
        }
        _setupDrag = -1;
        return true;
    }

    private void DrawSetup()
    {
        if (!_setupOpen) return;
        // 게임 안 메뉴 창(DrawTouchMenu)과 같은 색 · 꼴 — 화면 가운데 카드로
        var items = SetupItems();
        int body = Math.Max(14, 7 * HudZoom), large = body * 3 / 2, small = Math.Max(12, body * 3 / 4);
        int U(double v) => (int)Math.Round(v * body / 14.0);
        int m = U(16), w = Math.Min(_clientW - 24, U(560));
        var head = Text.Render("Setup", large, 0xFFEEF0FF);
        var sub = Text.Render(TouchUi ? "누르거나 막대를 끌어 고치기 · 창 밖을 누르면 닫기" : "↑↓ 고르기 · ←→ 고치기 · 탭 줄에서 ←→ 탭 바꾸기 · Enter · Esc", small, 0xFFC4CAE6, bold: false);
        int headH = U(12) + head.H + sub.H + U(12), tabH = Math.Max(U(44), body * 2 + U(4)), footH = U(52) + U(24);
        // 줄이 많으면(모드 탭) 화면 높이에 맞춰 줄 높이를 줄인다 — 줄 전체가 누름 영역이라 너무 작게는 안 한다
        int rowH = Math.Max(U(56), body * 2 + U(16));
        int room = _clientH - 16 - headH - tabH - footH;
        if (items.Count * rowH > room) rowH = Math.Max(body + U(20), room / Math.Max(1, items.Count));
        int h = headH + tabH + items.Count * rowH + footH;
        int x = (_clientW - w) / 2, y = Math.Max(8, (_clientH - h) / 2);
        _setupBox = (x, y, w, h);
        _setupRows.Clear();

        FillRect(0, 0, _clientW, _clientH, 0x90000000);
        FillRect(x, y, w, h, 0xFF1C2850);
        FillRect(x, y, w, Math.Max(2, U(2)), 0xFFC8A040);
        DrawText(head, x + m, y + U(12));
        DrawText(sub, x + m, y + U(12) + head.H);
        FillRect(x, y + headH - 1, w, 1, 0xFF3A4678);

        // 탭 줄 — 반씩 나눠 줄 전체가 누름 영역
        int ty0 = y + headH;
        _setupRows.Add((x, ty0, w, tabH));
        for (int t = 0; t < 2; t++)
        {
            int tx = x + t * w / 2, tw = (t + 1) * w / 2 - t * w / 2;
            _setupTabs[t] = (tx, ty0, tw, tabH);
            bool cur = t == _setupTab;
            FillRect(tx, ty0, tw, tabH, cur ? 0xFF34447E : 0xFF1C2850);
            if (cur) FillRect(tx, ty0 + tabH - Math.Max(2, U(3)), tw, Math.Max(2, U(3)), 0xFFC8A040);
            if (_setupRow == 0 && cur) FillRect(tx, ty0, tw, Math.Max(1, U(1)), 0xFFE8C870);
            var word = Text.Render(SetupTabNames[t], body, cur ? 0xFFE8C870 : 0xFFC4CAE6);
            DrawText(word, tx + (tw - word.W) / 2, ty0 + (tabH - word.H) / 2);
        }
        FillRect(x, ty0 + tabH - 1, w, 1, 0xFF3A4678);

        for (int i = 0; i < items.Count; i++)
        {
            int row = i + 1, ry = ty0 + tabH + i * rowH;
            var it = items[i];
            _setupRows.Add((x, ry, w, rowH));
            if (row == _setupRow) FillRect(x, ry, w, rowH - 1, 0xFF34447E);
            FillRect(x, ry + rowH - 1, w, 1, 0xFF2C3768);
            var name = Text.Render(it.Label, body, 0xFFEEF0FF);
            var note = it.Note != null && rowH >= body * 2 + U(8) ? Text.Render(it.Note, small, 0xFF9AA2C8, bold: false) : null;
            int nameY = note != null ? ry + (rowH - name.H - note.H) / 2 : ry + (rowH - name.H) / 2;
            DrawText(name, x + m, nameY);
            if (note != null) DrawText(note, x + m, nameY + name.H);
            if (it.Flip != null)
            {
                // 켜고 끄기 단추 — 폰 메뉴와 같은 금색 알약
                bool on = it.On?.Invoke() == true;
                int tw = U(52), th = Math.Min(U(30), rowH - U(8)), tx = x + w - m - tw, tyy = ry + (rowH - th) / 2, k = th - U(8);
                FillRect(tx, tyy, tw, th, on ? 0xFFC8A040 : 0xFF3A4678);
                FillRect(on ? tx + tw - k - U(4) : tx + U(4), tyy + U(4), k, k, 0xFFEEF0FF);
                var state = Text.Render(on ? "켬" : "끔", small, 0xFFE8C870);
                DrawText(state, tx - state.W - U(10), ry + (rowH - state.H) / 2);
                continue;
            }
            if (it.Volume < 0) continue;
            int v = VolumeNow(it.Volume);
            var pct = Text.Render($"{v}%", body, 0xFFE8C870);
            int pctW = Text.Render("100%", body, 0xFFE8C870).W;
            int tx0 = x + m + Text.Render("배경음악", body, 0xFFEEF0FF).W + U(20), tw0 = Math.Max(U(60), x + w - m - pctW - U(16) - tx0);
            int bh = Math.Max(U(6), 4), my = ry + rowH / 2;
            _setupTracks[row] = (tx0, my - bh / 2, tw0, bh);
            FillRect(tx0, my - bh / 2, tw0, bh, 0xFF3A4678);
            FillRect(tx0, my - bh / 2, tw0 * v / 100, bh, 0xFFC8A040);
            int knob = U(18);
            FillRect(tx0 + tw0 * v / 100 - knob / 2, my - knob / 2, knob, knob, 0xFFEEF0FF);
            DrawText(pct, x + w - m - pct.W, ry + (rowH - pct.H) / 2);
        }

        // 아래 — 금색 「닫기」(폰 메뉴와 같은 꼴)
        var close = (X: x + m, Y: y + h - footH + U(12), W: w - 2 * m, H: U(52));
        _setupRows.Add(close);
        FillRect(close.X, close.Y, close.W, close.H, _setupRow == SetupCloseRow ? 0xFFE8C870 : 0xFFC8A040);
        var word2 = Text.Render("닫기", body, 0xFF141A33);
        DrawText(word2, close.X + (close.W - word2.W) / 2, close.Y + (close.H - word2.H) / 2);
    }
}
