using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 설정 &gt; 소리 크기 창 — 배경음 · 효과음 크기(0~100%)를 막대로. ←→ 5% 씩, 막대를 누르거나 끌면 그 자리로, 바로 들린다.
/// 값은 <see cref="Settings.MusicVolume"/> · <see cref="Settings.SoundVolume"/> 에 둔다.
/// </summary>
internal sealed partial class GameWindow
{
    private bool _volumeOpen;

    /// <summary>줄: 0 배경음 · 1 효과음 · 2 닫기.</summary>
    private int _volumeRow;

    /// <summary>끄는 중인 막대 줄(−1 이면 아님).</summary>
    private int _volumeDrag = -1;

    private (int X, int Y, int W, int H) _volumeBox;
    private readonly (int X, int Y, int W, int H)[] _volumeTracks = new (int, int, int, int)[2];
    private readonly (int X, int Y, int W, int H)[] _volumeRowRects = new (int, int, int, int)[3];

    /// <summary>배경음 크기가 바뀌었다 — 플랫폼이 트는 곡에 바로 준다(안드로이드: MediaPlayer.SetVolume, 윈도우는 MusicTick 이 곡을 다시 연다).</summary>
    partial void ApplyVolume();

    private void OpenVolume() => (_volumeOpen, _volumeRow, _volumeDrag) = (true, 0, -1);

    private void CloseVolume()
    {
        _volumeOpen = false;
        _volumeDrag = -1;
        _settings.Save();
    }

    private int VolumeOf(int row) => row == 0 ? _settings.MusicVolume : _settings.SoundVolume;

    private void SetVolume(int row, int v)
    {
        v = Math.Clamp(v, 0, 100);
        if (v == VolumeOf(row)) return;
        if (row == 0) { _settings.MusicVolume = v; ApplyVolume(); }
        else _settings.SoundVolume = v;
    }

    /// <summary>고른 막대를 step% 만큼 — 효과음이면 그 크기로 한 번 들려준다.</summary>
    private void VolumeStep(int step)
    {
        if (_volumeRow > 1) return;
        SetVolume(_volumeRow, (VolumeOf(_volumeRow) + step) / 5 * 5 + (step < 0 && VolumeOf(_volumeRow) % 5 != 0 ? 5 : 0));
        if (_volumeRow == 1) PlaySfx(Sound.UiBank, Sound.Cursor);
        _settings.Save();
    }

    private bool VolumeKey(int vk)
    {
        if (!_volumeOpen) return false;
        vk = ActionOf(vk) switch
        {
            KeyAction.MoveUp => Win32.VK_UP, KeyAction.MoveDown => Win32.VK_DOWN,
            KeyAction.MoveLeft => Win32.VK_LEFT, KeyAction.MoveRight => Win32.VK_RIGHT, _ => vk,
        };
        switch (vk)
        {
            case Win32.VK_UP: _volumeRow = (_volumeRow + 2) % 3; break;
            case Win32.VK_DOWN: _volumeRow = (_volumeRow + 1) % 3; break;
            case Win32.VK_LEFT: VolumeStep(-5); break;
            case Win32.VK_RIGHT: VolumeStep(5); break;
            case Win32.VK_ESCAPE: CloseVolume(); break;
            case Win32.VK_RETURN or Win32.VK_SPACE: if (_volumeRow == 2) CloseVolume(); break;
        }
        return true;
    }

    /// <summary>막대 위 x 자리의 크기.</summary>
    private int VolumeAt(int row, int x)
    {
        var t = _volumeTracks[row];
        return t.W <= 0 ? VolumeOf(row) : (int)Math.Round(Math.Clamp((x - t.X) / (double)t.W, 0, 1) * 100);
    }

    private bool VolumePress(int x, int y)
    {
        if (!_volumeOpen) return false;
        if (!InRect(_volumeBox, x, y)) { CloseVolume(); return true; }
        for (int row = 0; row < 3; row++)
        {
            if (!InRect(_volumeRowRects[row], x, y)) continue;
            _volumeRow = row;
            if (row == 2) { CloseVolume(); return true; }
            var t = _volumeTracks[row];
            if (x >= t.X - 12 && x < t.X + t.W + 12)
            {
                _volumeDrag = row;
                SetVolume(row, VolumeAt(row, x));
            }
        }
        return true;
    }

    private bool VolumeMove(int x, int y)
    {
        if (!_volumeOpen) return false;
        if (_volumeDrag >= 0) SetVolume(_volumeDrag, VolumeAt(_volumeDrag, x));
        return true;
    }

    private bool VolumeRelease()
    {
        if (!_volumeOpen) return false;
        if (_volumeDrag == 1) PlaySfx(Sound.UiBank, Sound.Cursor);
        if (_volumeDrag >= 0) _settings.Save();
        _volumeDrag = -1;
        return true;
    }

    private void DrawVolume()
    {
        if (!_volumeOpen) return;
        int fs = Math.Max(14, 7 * HudZoom);
        var title = Text.Render("소리 크기", fs, 0xFFFFE070);
        var hint = Text.Render(TouchUi ? "막대를 누르거나 끌어 조절 · 창 밖을 누르면 닫기" : "↑↓ 고르기 · ←→ 5% 씩 · 막대를 누르거나 끌기 · Esc 닫기",
                               Math.Max(12, fs * 5 / 6), 0xFF9098A8, bold: false);
        string[] labels = ["배경음", "효과음"];
        var imgs = labels.Select(l => Text.Render(l, fs, 0xFFFFFFFF)).ToArray();
        var pct = Text.Render("100%", fs, 0xFFFFE070);
        int labelW = imgs.Max(i => i.W), trackW = fs * 14, rowH = Math.Max(pct.H, fs) + fs;
        int w = Math.Min(_clientW - 20, Math.Max(Math.Max(title.W, hint.W) + 40, labelW + trackW + pct.W + 100));
        trackW = Math.Max(fs * 4, w - labelW - pct.W - 100);
        int h = title.H + hint.H + 24 + rowH * 3 + 10;
        int x = (_clientW - w) / 2, y = Math.Max(10, (_clientH - h) / 2);
        _volumeBox = (x, y, w, h);

        FillRect(0, 0, _clientW, _clientH, 0x90000000);
        FillRect(x, y, w, h, 0xF0181828);
        DrawText(title, x + 20, y + 8);
        DrawText(hint, x + 20, y + 12 + title.H);
        int cy = y + title.H + hint.H + 24;
        for (int row = 0; row < 3; row++)
        {
            _volumeRowRects[row] = (x + 8, cy, w - 16, rowH - 4);
            if (row == _volumeRow) FillRect(x + 8, cy, w - 16, rowH - 4, 0xC0405090);
            int my = cy + (rowH - 4) / 2;
            if (row == 2)
            {
                var close = Text.Render("닫기", fs, 0xFFFFFFFF);
                DrawText(close, x + (w - close.W) / 2, my - close.H / 2);
                break;
            }
            DrawText(imgs[row], x + 24, my - imgs[row].H / 2);
            int v = VolumeOf(row), tx = x + 24 + labelW + 24, th = Math.Max(6, fs / 3);
            _volumeTracks[row] = (tx, my - th / 2, trackW, th);
            FillRect(tx, my - th / 2, trackW, th, 0xFF3A4670);
            FillRect(tx, my - th / 2, trackW * v / 100, th, row == 0 ? 0xFF58A0E0 : 0xFFF0C030);
            int knob = Math.Max(8, fs * 2 / 3);
            FillRect(tx + trackW * v / 100 - knob / 2, my - knob, knob, knob * 2, 0xFFFFFFFF);
            var num = Text.Render($"{v}%", fs, 0xFFFFE070);
            DrawText(num, tx + trackW + 20 + pct.W - num.W, my - num.H / 2);
            cy += rowH;
        }
    }
}
