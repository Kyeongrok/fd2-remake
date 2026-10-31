using Fd2.Native;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Fd2;

/// <summary>
/// 창 위 제목 줄 — 대항해시대3 리메이크(cds-helper 의 ChromeTitleBar)처럼 우리가 직접 그린다.
/// 왼쪽 햄버거(누르면 게임 · 설정 · 모드 · 도구 메뉴), 제목, 오른쪽 내리기 · 최대화 · 닫기. 윈도우에만 있다.
/// </summary>
/// <remarks>
/// 창 안쪽 전체(<c>_winW</c> x <c>_winH</c>)가 스왑 체인이고, 맨 위 <c>_barH</c> 줄이 제목 줄, 그 밑이 게임 판(<c>_clientW</c> x <c>_clientH</c>)이다.
/// 게임 쪽 그림 · 마우스 좌표는 모두 판 기준이라 제목 줄 몫만 빼서 넘긴다. 전체화면이면 제목 줄이 없다(<c>_barH</c> = 0).
/// </remarks>
internal sealed unsafe partial class GameWindow
{
    // cds ChromeTitleBar 의 치수(96 DPI 기준) · 색
    private const int BarBaseH = 32, BarMenuBaseW = 40, BarButtonBaseW = 46;
    private const uint BarFrame = 0xFF2A1A15, BarFrameIdle = 0xFF221612;
    private const uint BarTitle = 0xFFF2EAD6, BarTitleIdle = 0xFF9A8A78;
    private const uint BarHover = 0x1AFFFFFF, BarPress = 0x33FFFFFF;
    private const uint BarCloseHover = 0xFFE81123, BarClosePress = 0xFFF1707A;

    /// <summary>단추 번호 — 0 햄버거, 1 내리기, 2 최대화, 3 닫기.</summary>
    private const int BarMenu = 0, BarMin = 1, BarMax = 2, BarClose = 3;

    /// <summary>창 안쪽 전체 크기(제목 줄 포함) — 스왑 체인 크기.</summary>
    private int _winW = 1920, _winH = 1080;

    /// <summary>제목 줄 높이(점) — 전체화면이면 0.</summary>
    private int _barH;
    private float _barScale = 1;
    private bool _focused = true;
    private int _barPress = -1;
    private string _caption = "용의기사2";

    private uint[] _barFb = [];
    private ID3D11Texture2D? _barTex;
    private ID3D11ShaderResourceView? _barSrv;

    private int BarMenuW => (int)Math.Round(BarMenuBaseW * _barScale);
    private int BarButtonW => (int)Math.Round(BarButtonBaseW * _barScale);

    /// <summary>모니터 배율과 전체화면에 맞춰 제목 줄 높이를 다시 셈한다.</summary>
    private void UpdateBarMetrics()
    {
        uint dpi = _hwnd != IntPtr.Zero ? Win32.GetDpiForWindow(_hwnd) : 96;
        _barScale = Math.Max(1, dpi) / 96f;
        _barH = _settings.Fullscreen && !Offscreen ? 0 : (int)Math.Round(BarBaseH * _barScale);
    }

    /// <summary>제목 줄 글 — 창 제목(작업 표시줄)도 같이.</summary>
    private void SetCaption(string text)
    {
        if (text == _caption) return;
        _caption = text;
        if (_hwnd != IntPtr.Zero) Win32.SetWindowTextW(_hwnd, text);
    }

    /// <summary>창 안쪽 (x, y) 의 제목 줄 단추 — 없으면 -1.</summary>
    private int BarButtonAt(int x, int y)
    {
        if (_barH <= 0 || y < 0 || y >= _barH || x < 0 || x >= _winW) return -1;
        if (x < BarMenuW) return BarMenu;
        int left = _winW - 3 * BarButtonW;
        return x >= left ? 1 + (x - left) / BarButtonW : -1;
    }

    // ── 창 메시지 ─────────────────────────────────────────────────────────────

    /// <summary>제목 줄 · 테두리 몫의 창 메시지를 받는다 — 받았으면 true 와 돌려줄 값.</summary>
    private bool BarMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        switch (msg)
        {
            case Win32.WM_NCCALCSIZE when wParam != IntPtr.Zero:
                // 창 전체가 그리는 곳 — 캡션 · 테두리는 안 그린다. 최대화면 테두리 몫만큼 화면 밖으로 넘치므로 작업 영역에 맞춘다.
                if (Win32.IsZoomed(hWnd) && !_settings.Fullscreen)
                {
                    var info = new Win32.MonitorInfo { Size = sizeof(Win32.MonitorInfo) };
                    if (Win32.GetMonitorInfoW(Win32.MonitorFromWindow(hWnd, 2 /*NEAREST*/), ref info))
                        *(Win32.Rect*)lParam = info.Work;
                }
                return true;
            case Win32.WM_NCHITTEST:
            {
                Win32.GetWindowRect(hWnd, out var frame);
                int px = Win32.LowWord(lParam) - frame.Left, py = Win32.HighWord(lParam) - frame.Top;
                result = Win32.HTCLIENT;
                if (_barH <= 0) return true;
                if (!Win32.IsZoomed(hWnd))
                {
                    int edge = (int)Math.Round(6 * _barScale);
                    bool left = px < edge, right = px >= frame.Width - edge, top = py < edge, bottom = py >= frame.Height - edge;
                    int hit = top && left ? Win32.HTTOPLEFT : top && right ? Win32.HTTOPRIGHT
                            : bottom && left ? Win32.HTBOTTOMLEFT : bottom && right ? Win32.HTBOTTOMRIGHT
                            : left ? Win32.HTLEFT : right ? Win32.HTRIGHT : top ? Win32.HTTOP : bottom ? Win32.HTBOTTOM : 0;
                    if (hit != 0 && BarButtonAt(px, py) is not (BarMenu or BarClose)) { result = hit; return true; }
                }
                // 단추는 우리가 받고, 나머지 제목 줄은 잡고 끄는 자리(두 번 누르면 최대화)
                if (py < _barH && BarButtonAt(px, py) < 0) result = Win32.HTCAPTION;
                return true;
            }
            case Win32.WM_ACTIVATE:
                _focused = ((long)wParam & 0xFFFF) != 0;
                return false;
            case Win32.WM_NCACTIVATE:
                _focused = wParam != IntPtr.Zero;
                result = 1;               // 캡션이 없으니 윈도우가 그릴 것도 없다
                return true;
            case Win32.WM_DPICHANGED:
            {
                var r = *(Win32.Rect*)lParam;
                UpdateBarMetrics();
                Win32.SetWindowPos(hWnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                return true;
            }
            case 0x0112 when ((long)wParam & 0xFFF0) == 0xF100 && lParam != (IntPtr)' ':   // WM_SYSCOMMAND · SC_KEYMENU
                // 메뉴 막대가 없으니 Alt 만 눌렀다 떼면 햄버거 메뉴를 내리고, Alt+글자는 삑 소리 없이 넘긴다(Alt+Space 는 시스템 메뉴)
                if (lParam == IntPtr.Zero && _barH > 0) OpenBarMenu();
                return true;
            case Win32.WM_LBUTTONDOWN when _barH > 0 && Win32.HighWord(lParam) < _barH:
            {
                int b = BarButtonAt(Win32.LowWord(lParam), Win32.HighWord(lParam));
                if (b == BarMenu) OpenBarMenu();
                else if (b > 0) { _barPress = b; Win32.SetCapture(hWnd); }
                return true;
            }
            case Win32.WM_LBUTTONUP when _barPress >= 0:
            {
                int b = _barPress;
                _barPress = -1;
                Win32.ReleaseCapture();
                if (BarButtonAt(Win32.LowWord(lParam), Win32.HighWord(lParam)) == b) BarAction(b);
                return true;
            }
            case Win32.WM_MOUSEMOVE when _barPress >= 0 || (_barH > 0 && Win32.HighWord(lParam) < _barH):
            case Win32.WM_RBUTTONDOWN when _barH > 0 && Win32.HighWord(lParam) < _barH:
                return true;              // 제목 줄 위의 마우스는 게임에 안 넘긴다
        }
        return false;
    }

    private void BarAction(int button)
    {
        switch (button)
        {
            case BarMin: Win32.ShowWindow(_hwnd, Win32.SW_MINIMIZE); break;
            case BarMax: Win32.ShowWindow(_hwnd, Win32.IsZoomed(_hwnd) ? Win32.SW_RESTORE : Win32.SW_MAXIMIZE); break;
            case BarClose: Win32.PostMessageW(_hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
        }
    }

    /// <summary>햄버거 — 메뉴(게임 · 설정 · 모드 · 도구)를 단추 왼쪽 아래에 내린다. 고르면 WM_COMMAND 로 온다.</summary>
    private void OpenBarMenu()
    {
        _barPress = BarMenu;
        Render();                         // 메뉴가 떠 있는 동안 눌린 모양으로
        var at = new Win32.Point { X = 0, Y = _barH };
        Win32.ClientToScreen(_hwnd, ref at);
        UpdateMenuChecks();
        Win32.TrackPopupMenuEx(_menuBar, 0, at.X, at.Y, _hwnd, IntPtr.Zero);
        _barPress = -1;
    }

    // ── 그리기 ────────────────────────────────────────────────────────────────

    /// <summary>제목 줄 텍스처를 창 너비 x 제목 줄 높이로 다시 만든다(제목 줄이 없으면 1 x 1).</summary>
    private void ResizeBar()
    {
        int w = Math.Max(1, _winW), h = Math.Max(1, _barH);
        if (_barTex != null && _barFb.Length == w * h && _barTex.Description.Width == w) return;
        _barSrv?.Dispose();
        _barTex?.Dispose();
        _barFb = new uint[w * h];
        _barTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        _barSrv = _device.CreateShaderResourceView(_barTex);
    }

    private void DrawTitleBar()
    {
        if (_barH <= 0 || _barFb.Length != _winW * _barH) return;
        bool on = _focused;
        uint frame = on ? BarFrame : BarFrameIdle, ink = on ? BarTitle : BarTitleIdle;
        Array.Fill(_barFb, frame);

        // 마우스가 올라간 단추 — 매 틱 커서 자리를 본다(제목 줄의 빈 곳은 캡션이라 이동 알림이 안 온다)
        int hover = -1;
        if (Win32.GetCursorPos(out var cur) && (Win32.WindowFromPoint(cur) == _hwnd || _barPress >= 0))
        {
            Win32.ScreenToClient(_hwnd, ref cur);
            hover = BarButtonAt(cur.X, cur.Y);
        }

        int s = Math.Max(1, (int)Math.Round(_barScale)), bw = BarButtonW, mw = BarMenuW;
        for (int b = 0; b < 4; b++)
        {
            int x = b == BarMenu ? 0 : _winW - (4 - b) * bw, w = b == BarMenu ? mw : bw;
            bool down = _barPress == b && (hover == b || b == BarMenu), over = hover == b;
            if (down || over)
                BarFill(x, 0, w, _barH, b == BarClose ? (down ? BarClosePress : BarCloseHover) : down ? BarPress : BarHover);
            int g = (int)Math.Round(10 * _barScale), gx = x + (w - g) / 2, gy = (_barH - g) / 2;
            uint gc = b == BarClose && (down || over) ? 0xFFFFFFFF : ink;
            int u(double v) => (int)Math.Round(v * _barScale);
            switch (b)
            {
                case BarMenu:
                    gx = x + (w - u(12)) / 2;
                    foreach (int r in new[] { 1, 5, 9 }) BarFill(gx, gy + u(r), u(12), s, gc);
                    break;
                case BarMin: BarFill(gx, gy + u(5), g, s, gc); break;
                case BarMax when Win32.IsZoomed(_hwnd):
                    BarBox(gx, gy + u(2), u(8), u(8), s, gc);
                    BarFill(gx + u(2), gy, s, u(2), gc);
                    BarFill(gx + u(2), gy, u(8), s, gc);
                    BarFill(gx + u(9), gy, s, u(8), gc);
                    BarFill(gx + u(7), gy + u(7), u(2), s, gc);
                    break;
                case BarMax: BarBox(gx, gy, g, g, s, gc); break;
                case BarClose:
                    for (int i = 0; i < g; i++)
                    {
                        BarFill(gx + i, gy + i, s, s, gc);
                        BarFill(gx + g - s - i, gy + i, s, s, gc);
                    }
                    break;
            }
        }

        // 제목 — 햄버거 오른쪽에 12 띄워서, 단추 앞에서 자른다
        var img = Text.Render(_caption, Math.Max(10, (int)Math.Round(12 * _barScale)), ink, bold: false);
        int tx = mw + (int)Math.Round(12 * _barScale), ty = (_barH - img.H) / 2, tr = _winW - 3 * bw - (int)Math.Round(12 * _barScale);
        for (int yy = 0; yy < img.H; yy++)
        {
            int sy = ty + yy;
            if ((uint)sy >= (uint)_barH) continue;
            for (int xx = 0; xx < img.W && tx + xx < tr; xx++)
            {
                uint c = img.Px[yy * img.W + xx];
                if (c >> 24 != 0) Over(ref _barFb[sy * _winW + tx + xx], c);
            }
        }
    }

    private void BarFill(int x, int y, int w, int h, uint argb)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(_winW, x + w), y1 = Math.Min(_barH, y + h);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++) Over(ref _barFb[yy * _winW + xx], argb);
    }

    private void BarBox(int x, int y, int w, int h, int t, uint argb)
    {
        BarFill(x, y, w, t, argb);
        BarFill(x, y + h - t, w, t, argb);
        BarFill(x, y + t, t, h - 2 * t, argb);
        BarFill(x + w - t, y + t, t, h - 2 * t, argb);
    }

    private void UploadBar()
    {
        if (_barTex == null) return;
        int w = (int)_barTex.Description.Width, h = (int)_barTex.Description.Height;
        if (_barFb.Length != w * h) return;
        var map = _ctx.Map(_barTex, 0, MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < h; y++)
                _barFb.AsSpan(y * w, w).CopyTo(new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), w));
        }
        finally { _ctx.Unmap(_barTex, 0); }
    }
}
