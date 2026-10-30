using Fd2.Native;

namespace Fd2;

/// <summary>
/// 단축키 · 패드 — 설정 &gt; 단축키 설정 창(줄을 고르고 키나 패드 단추를 누르면 묶인다), J 단축키 목록, 동작 나눠 주기.
/// </summary>
internal sealed partial class GameWindow
{
    private readonly KeyBindings _keyMap = KeyBindings.Load();
    private GamePad? _pad;

    /// <summary>단축키 설정 창 — 열려 있나, 고른 줄, 키를 기다리는 줄(−1 이면 아님).</summary>
    private bool _keysOpen;
    private int _keysRow, _keysCapture = -1;

    private bool _helpOpen;
    private bool _showGauges = true;

    /// <summary>줄: 키 동작들 다음에 패드 동작들, 맨 끝은 「처음 값으로」.</summary>
    private int KeyRows => KeyBindings.All.Length + GamePad.All.Length + 1;

    /// <summary>키 하나를 먼저 받아 본다 — 설정 창 · 목록이 열려 있으면 그것이 먹는다. 먹었으면 참.</summary>
    private bool KeysWindowKey(int vk)
    {
        if (_helpOpen) { _helpOpen = false; return true; }
        if (!_keysOpen) return false;
        if (_keysCapture >= 0)
        {
            if (vk != Win32.VK_ESCAPE && _keysCapture < KeyBindings.All.Length)
            {
                _keyMap.Assign(KeyBindings.All[_keysCapture].Action, vk);
                _keyMap.Save();
            }
            _keysCapture = -1;
            if (_pad != null) _pad.Capture = null;
            return true;
        }
        switch (vk)
        {
            case Win32.VK_UP: _keysRow = (_keysRow + KeyRows - 1) % KeyRows; break;
            case Win32.VK_DOWN: _keysRow = (_keysRow + 1) % KeyRows; break;
            case Win32.VK_ESCAPE: _keysOpen = false; break;
            case Win32.VK_RETURN or Win32.VK_SPACE: PickKeyRow(_keysRow); break;
        }
        return true;
    }

    private void PickKeyRow(int row)
    {
        if (row == KeyRows - 1)
        {
            _keyMap.ResetDefaults();
            _keyMap.Save();
            _pad?.ResetDefaults();
            return;
        }
        _keysCapture = row;
        if (row >= KeyBindings.All.Length && _pad != null) _pad.Capture = GamePad.All[row - KeyBindings.All.Length].Action;
    }

    /// <summary>패드 단추가 묶였으면(설정 창이 기다리던 것) 기다림을 푼다.</summary>
    private void PollPad(double now)
    {
        if (_pad == null) return;
        bool waiting = _pad.Capture != null;
        _pad.Poll(now);
        if (waiting && _pad.Capture == null) _keysCapture = -1;
    }

    /// <summary>패드가 키를 누른 것처럼 — 손으로 누른 키와 같은 길로 보낸다.</summary>
    private void PadPress(int vk)
    {
        OnKeyDown(vk);
        _keys.Remove(vk);
    }

    private void DrawKeysWindow()
    {
        if (!_keysOpen && !_helpOpen) return;
        int fs = Math.Max(14, 7 * _zoom);
        var rows = new List<(string Label, string Key, bool Header)>();
        rows.Add(("키보드", "", true));
        foreach (var (a, label, _) in KeyBindings.All) rows.Add((label, KeyBindings.KeyName(_keyMap[a]), false));
        rows.Add(("패드", _pad?.Connected == true ? "(연결됨)" : "(없음)", true));
        foreach (var (a, label, _) in GamePad.All) rows.Add((label, GamePad.ButtonName(_pad?.ButtonOf(a) ?? 0), false));
        if (_keysOpen) rows.Add(("처음 값으로 되돌리기", "", false));

        string title = _keysOpen ? "단축키 설정 — 위아래로 고르고 Enter, 그다음 키(패드 줄은 패드 단추)를 누르세요 · Esc 닫기"
                                 : "단축키 목록 — 아무 키나 누르면 닫힙니다 · 방향키 커서 · Enter/Space 결정 · Esc 취소 · F11 전체화면 · F12 스크린샷";
        var t = Text.Render(title, fs, 0xFFFFE070);
        var imgs = rows.Select(r => (L: Text.Render(r.Label, fs, r.Header ? 0xFF90E0FF : 0xFFFFFFFF), K: Text.Render(r.Key, fs, 0xFFFFE070))).ToList();
        int rowH = imgs.Max(i => i.L.H) + 6;
        int w = Math.Max(t.W + 40, imgs.Max(i => i.L.W) + 260), h = t.H + 30 + rowH * rows.Count;
        int x = (_clientW - w) / 2, y = Math.Max(8, (_clientH - h) / 2);
        FillRect(0, 0, _clientW, _clientH, 0x90000000);
        FillRect(x, y, w, h, 0xF0181828);
        DrawText(t, x + 20, y + 10);

        int cy = y + t.H + 20, selectable = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            int index = rows[i].Header ? -1 : selectable++;
            bool hot = _keysOpen && index == _keysRow;
            if (hot) FillRect(x + 10, cy - 2, w - 20, rowH, 0xC0405090);
            DrawText(imgs[i].L, x + (rows[i].Header ? 20 : 40), cy);
            var key = hot && _keysCapture == index ? Text.Render("키를 누르세요…", fs, 0xFFFF9090) : imgs[i].K;
            DrawText(key, x + w - key.W - 30, cy);
            cy += rowH;
        }
    }

    /// <summary>동작 키 — 전투 밖에서는 커서 키를 걷기로(방향키와 같이), 체력바 · 목록은 어디서나.</summary>
    private KeyAction? ActionOf(int vk) => _keyMap.ActionFor(vk);
}
