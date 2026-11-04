using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 타이틀 「Setup」 창(사용자 요청 w-102) — 게임 안 메뉴 창(폰 메뉴)과 같은 꼴: 남색 바탕 · 금줄 · 큰 제목, 줄마다 이름과 오른쪽 켜고 끄기 단추 · 막대, 아래 금색 「닫기」.
/// 줄: 0 오프닝 보기(켬/끔) · 1 시작 화면 그림(켬/끔, w-127) · 2 배경음악 크기 · 3 효과음 크기 · 4 닫기. ↑↓ 고르기, ←→ 막대 5% · 켜고 끄기, Enter 켜고 끄기 · 닫기, Esc · 창 밖 누르기 닫기.
/// </summary>
internal sealed partial class GameWindow
{
    private bool _setupOpen;
    private int _setupRow, _setupDrag = -1;
    private (int X, int Y, int W, int H) _setupBox;
    private readonly (int X, int Y, int W, int H)[] _setupRows = new (int, int, int, int)[5];
    private readonly (int X, int Y, int W, int H)[] _setupTracks = new (int, int, int, int)[4];

    private const int SetupRows = 5, SetupClose = 4, SetupMusic = 2, SetupSound = 3;

    /// <summary>켜고 끄기 줄인가(0 오프닝 · 1 시작 화면 그림).</summary>
    private static bool SetupToggleRow(int row) => row is 0 or 1;

    private bool SetupToggleOn(int row) => row == 0 ? _settings.ShowOpening : _settings.ShowTitleImage;

    private void OpenSetup()
    {
        PlaySfx(Sound.UiBank, Sound.Menu);
        (_setupOpen, _setupRow, _setupDrag) = (true, 0, -1);
    }

    private void CloseSetup()
    {
        (_setupOpen, _setupDrag) = (false, -1);
        _settings.Save();
    }

    private void SetupToggle(int row)
    {
        if (row == 0) _settings.ShowOpening = !_settings.ShowOpening;
        else _settings.ShowTitleImage = !_settings.ShowTitleImage;
        PlaySfx(Sound.UiBank, Sound.Cursor);
        _settings.Save();
    }

    /// <summary>막대 줄(2 배경음악 · 3 효과음)을 step% 만큼. 켜고 끄기 줄이면 뒤집는다.</summary>
    private void SetupStep(int row, int step)
    {
        if (SetupToggleRow(row)) { SetupToggle(row); return; }
        if (row is not (SetupMusic or SetupSound)) return;
        int now = row == SetupMusic ? _settings.MusicVolume : _settings.SoundVolume;
        SetVolume(row - SetupMusic, (now + step) / 5 * 5 + (step < 0 && now % 5 != 0 ? 5 : 0));
        if (row == SetupSound) PlaySfx(Sound.UiBank, Sound.Cursor);
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
        switch (vk)
        {
            case Win32.VK_UP: _setupRow = (_setupRow + SetupRows - 1) % SetupRows; PlaySfx(77, 2); break;
            case Win32.VK_DOWN: _setupRow = (_setupRow + 1) % SetupRows; PlaySfx(77, 2); break;
            case Win32.VK_LEFT: SetupStep(_setupRow, -5); break;
            case Win32.VK_RIGHT: SetupStep(_setupRow, 5); break;
            case Win32.VK_ESCAPE: CloseSetup(); break;
            case Win32.VK_RETURN or Win32.VK_SPACE:
                if (SetupToggleRow(_setupRow)) SetupToggle(_setupRow);
                else if (_setupRow == SetupClose) CloseSetup();
                break;
        }
        return true;
    }

    private int SetupAt(int row, int x)
    {
        var t = _setupTracks[row];
        int now = row == SetupMusic ? _settings.MusicVolume : _settings.SoundVolume;
        return t.W <= 0 ? now : (int)Math.Round(Math.Clamp((x - t.X) / (double)t.W, 0, 1) * 100);
    }

    private bool SetupPress(int x, int y)
    {
        if (!_setupOpen) return false;
        if (!InRect(_setupBox, x, y)) { CloseSetup(); return true; }
        for (int row = 0; row < SetupRows; row++)
        {
            if (!InRect(_setupRows[row], x, y)) continue;
            _setupRow = row;
            if (SetupToggleRow(row)) SetupToggle(row);
            else if (row == SetupClose) CloseSetup();
            else
            {
                _setupDrag = row;
                SetVolume(row - SetupMusic, SetupAt(row, x));
            }
        }
        return true;
    }

    private bool SetupMove(int x, int y)
    {
        if (!_setupOpen) return false;
        if (_setupDrag >= SetupMusic) SetVolume(_setupDrag - SetupMusic, SetupAt(_setupDrag, x));
        else if (_mouseMoved) for (int row = 0; row < SetupRows; row++) if (InRect(_setupRows[row], x, y)) _setupRow = row;
        return true;
    }

    private bool SetupRelease()
    {
        if (!_setupOpen) return false;
        if (_setupDrag == SetupSound) PlaySfx(Sound.UiBank, Sound.Cursor);
        if (_setupDrag >= SetupMusic) _settings.Save();
        _setupDrag = -1;
        return true;
    }

    private void DrawSetup()
    {
        if (!_setupOpen) return;
        // 게임 안 메뉴 창(DrawTouchMenu)과 같은 색 · 꼴 — 화면 가운데 카드로
        int body = Math.Max(14, 7 * HudZoom), large = body * 3 / 2, small = Math.Max(12, body * 3 / 4);
        int U(double v) => (int)Math.Round(v * body / 14.0);
        int m = U(16), rowH = Math.Max(U(56), body * 2 + U(16)), w = Math.Min(_clientW - 24, U(520));
        var head = Text.Render("Setup", large, 0xFFEEF0FF);
        var sub = Text.Render(TouchUi ? "누르거나 막대를 끌어 고치기 · 창 밖을 누르면 닫기" : "↑↓ 고르기 · ←→ 고치기 · Enter · Esc", small, 0xFFC4CAE6, bold: false);
        int headH = U(12) + head.H + sub.H + U(12), footH = U(52) + U(24);
        int h = headH + SetupClose * rowH + footH;
        int x = (_clientW - w) / 2, y = Math.Max(8, (_clientH - h) / 2);
        _setupBox = (x, y, w, h);

        FillRect(0, 0, _clientW, _clientH, 0x90000000);
        FillRect(x, y, w, h, 0xFF1C2850);
        FillRect(x, y, w, Math.Max(2, U(2)), 0xFFC8A040);
        DrawText(head, x + m, y + U(12));
        DrawText(sub, x + m, y + U(12) + head.H);
        FillRect(x, y + headH - 1, w, 1, 0xFF3A4678);

        for (int row = 0; row < SetupClose; row++)
        {
            int ry = y + headH + row * rowH;
            _setupRows[row] = (x, ry, w, rowH);
            if (row == _setupRow) FillRect(x, ry, w, rowH - 1, 0xFF34447E);
            FillRect(x, ry + rowH - 1, w, 1, 0xFF2C3768);
            string label = row switch { 0 => "오프닝 보기", 1 => "시작 화면 그림", SetupMusic => "배경음악", _ => "효과음" };
            var name = Text.Render(label, body, 0xFFEEF0FF);
            DrawText(name, x + m, ry + (rowH - name.H) / 2);
            if (SetupToggleRow(row))
            {
                // 켜고 끄기 단추 — 폰 메뉴와 같은 금색 알약
                bool on = SetupToggleOn(row);
                int tw = U(52), th = U(30), tx = x + w - m - tw, ty = ry + (rowH - th) / 2, k = U(22);
                FillRect(tx, ty, tw, th, on ? 0xFFC8A040 : 0xFF3A4678);
                FillRect(on ? tx + tw - k - U(4) : tx + U(4), ty + U(4), k, k, 0xFFEEF0FF);
                var state = Text.Render(on ? "켬" : "끔", small, 0xFFE8C870);
                DrawText(state, tx - state.W - U(10), ry + (rowH - state.H) / 2);
                continue;
            }
            int v = row == SetupMusic ? _settings.MusicVolume : _settings.SoundVolume;
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
        _setupRows[SetupClose] = close;
        FillRect(close.X, close.Y, close.W, close.H, _setupRow == SetupClose ? 0xFFE8C870 : 0xFFC8A040);
        var word = Text.Render("닫기", body, 0xFF141A33);
        DrawText(word, close.X + (close.W - word.W) / 2, close.Y + (close.H - word.H) / 2);
    }
}
