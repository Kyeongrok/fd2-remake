using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 켜면 — 오프닝(ANI.DAT 아홉 편, 아무 키나 누르면 건너뜀) → 타이틀 메뉴(오프닝의 로고 장면 위에, 맨 아래 팬메이드 알림) → 게임.
/// </summary>
internal sealed partial class GameWindow
{
    private enum Screen { Opening, Title, Prologue, Game, Ending }

    private const double OpeningCut = 1.0 / 18;
    private const string FanMadeNotice = "이 게임은 팬메이드 게임입니다. 원작 및 모드의 저작권은 각 저작권자에게 있습니다.";

    private Screen _screen = Screen.Opening;
    private AfmPlayer? _opening;
    private uint[]? _openingFrame, _titleBack;
    private double _openingClock;
    private int _titleIndex;
    private readonly List<(int X, int Y, int W, int H)> _titleRects = new();

    private (string Label, Action Do)[] TitleItems => _continueOpen
        ? [.. _continueList.Select(s => (s.Label, (Action)(() => ContinueFrom(s.Campaign)))), ("돌아가기", () => { _continueOpen = false; _titleIndex = 0; })]
        : [
        ("Start — 게임 시작", StartGame),
        .. ((_hasSaves ??= ContinueSlots().Count > 0) ? new (string, Action)[] { ("이어하기", () => { OpenContinue(); _titleIndex = 0; }) } : []),
        ($"판: {VersionName}", OpenVersions),
        ("지도 둘러보기", () => { _screen = Screen.Game; LoadScene(_scene.Index); }),
        ("오프닝 다시 보기", StartOpening),
        ("단축키 · 패드 설정", () => { _keysOpen = true; _keysRow = 0; _keysCapture = -1; }),
        .. (HasEditor ? new (string, Action)[] { ("편집기 열기", OpenEditor) } : []),
        ("끝내기", () => _running = false),
    ];

    private void StartOpening()
    {
        try { _opening = new AfmPlayer(_dir); }
        catch (IOException) { _opening = null; }
        _openingFrame = null;
        _openingClock = 0;
        _screen = _opening == null ? Screen.Title : Screen.Opening;
    }

    private void ToTitle()
    {
        _opening = null;
        _openingFrame = null;
        if (InBattle) { _bs = BattleState.None; ClearSelection(); SetMusic(-1); }   // 전투 곡을 끈다
        _screen = Screen.Title;
        _titleIndex = 0;
        _continueOpen = false;
        _hasSaves = null;
        _titleBack ??= LogoFrame();
    }

    /// <summary>타이틀 배경 — 오프닝 둘째 편(「Flame Dragon 2」 로고)의 마지막 컷.</summary>
    private uint[]? LogoFrame()
    {
        try
        {
            var player = new AfmPlayer(_dir);
            uint[]? last = null;
            // 첫 편(96컷) · 둘째 편(51컷)을 끝까지 돌린다 — Next 는 편을 이어서 준다
            for (int cut = 0; cut < 96 + 51 && player.Next() is { } f; cut++) last = f;
            return last;
        }
        catch (IOException) { return null; }
    }

    private void TitleTick(double dt)
    {
        if (_screen != Screen.Opening || _opening == null) return;
        _openingClock += dt;
        while (_openingClock >= OpeningCut)
        {
            _openingClock -= OpeningCut;
            var f = _opening.Next();
            if (f == null) { ToTitle(); return; }
            _openingFrame = f;
        }
    }

    /// <summary>오프닝 · 타이틀에서 받은 키 — 오프닝은 아무 키나 건너뛰고, 타이틀은 위아래 · 결정 · Esc(끝내기).</summary>
    private void TitleKey(int vk)
    {
        if (_screen == Screen.Opening) { ToTitle(); return; }
        var items = TitleItems;
        switch (vk)
        {
            case Win32.VK_UP: _titleIndex = (_titleIndex + items.Length - 1) % items.Length; break;
            case Win32.VK_DOWN: _titleIndex = (_titleIndex + 1) % items.Length; break;
            case Win32.VK_RETURN or Win32.VK_SPACE: items[_titleIndex].Do(); break;
            case Win32.VK_ESCAPE when _continueOpen: _continueOpen = false; _titleIndex = 0; break;
            case Win32.VK_ESCAPE: _running = false; break;
        }
    }

    private void TitleClick(int mx, int my)
    {
        if (_screen == Screen.Opening) { ToTitle(); return; }
        for (int i = 0; i < _titleRects.Count; i++)
        {
            var r = _titleRects[i];
            if (mx >= r.X && my >= r.Y && mx < r.X + r.W && my < r.Y + r.H) { _titleIndex = i; TitleItems[i].Do(); return; }
        }
    }

    private void TitleHover(int mx, int my)
    {
        for (int i = 0; i < _titleRects.Count; i++)
        {
            var r = _titleRects[i];
            if (mx >= r.X && my >= r.Y && mx < r.X + r.W && my < r.Y + r.H) _titleIndex = i;
        }
    }

    /// <summary>320x200 그림을 창에 들어가는 가장 큰 정수 배로 가운데에.</summary>
    private (int X, int Y, int S) DrawScreen320(uint[] px)
    {
        int s = Math.Max(1, Math.Min(_clientW / AfmPlayer.Width, _clientH / AfmPlayer.Height));
        int x = (_clientW - AfmPlayer.Width * s) / 2, y = (_clientH - AfmPlayer.Height * s) / 2;
        BlitScaled(px, AfmPlayer.Width, AfmPlayer.Height, x, y, s, 0, 0, _clientW, _clientH);
        return (x, y, s);
    }

    private void DrawTitle()
    {
        int fs = Math.Max(14, 7 * _zoom);
        if (_screen == Screen.Opening)
        {
            if (_openingFrame != null) DrawScreen320(_openingFrame);
            var skip = Text.Render(TouchUi ? "누르면 건너뜁니다" : "아무 키나 누르면 건너뜁니다", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
            DrawText(skip, _clientW - skip.W - 16, _clientH - skip.H - 12);
            return;
        }

        // 로고는 위쪽 3/4 안에(로고 글자는 그림의 150줄까지), 메뉴는 그 밑에
        int ls = Math.Max(1, Math.Min(_clientW / AfmPlayer.Width, _clientH * 3 / 4 / AfmPlayer.Height));
        int lx = (_clientW - AfmPlayer.Width * ls) / 2, ly = Math.Max(0, _clientH / 60);
        if (_titleBack != null) BlitScaled(_titleBack, AfmPlayer.Width, AfmPlayer.Height, lx, ly, ls, 0, 0, _clientW, _clientH);

        var items = TitleItems;
        var imgs = items.Select((it, i) => Text.Render(it.Label, fs * 5 / 4, i == _titleIndex ? 0xFFFFE070 : 0xFFFFFFFF)).ToList();
        int rowH = imgs.Max(i => i.H) + 12, w = imgs.Max(i => i.W) + 80;
        int x = (_clientW - w) / 2, y = Math.Min(ly + 155 * ls, _clientH - rowH * items.Length - 80);
        if (ly + 155 * ls + rowH * items.Length + 40 > _clientH && _titleBack != null)
        {
            // 낮은 화면(폰) — 로고는 왼쪽 절반, 메뉴는 오른쪽 절반 가운데
            FillRect(0, 0, _clientW, _clientH, 0xFF000000);
            int sideLx = Math.Max(0, (_clientW / 2 - AfmPlayer.Width * ls) / 2);
            BlitScaled(_titleBack, AfmPlayer.Width, AfmPlayer.Height, sideLx, (_clientH - AfmPlayer.Height * ls) / 2, ls, 0, 0, _clientW / 2 + AfmPlayer.Width * ls / 2, _clientH);
            x = Math.Max(_clientW / 2, _clientW * 3 / 4 - w / 2);
            y = Math.Max(8, (_clientH - rowH * items.Length) / 2 - 16);
        }
        FillRect(x, y - 10, w, rowH * items.Length + 20, 0xB0101020);
        _titleRects.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            int ry = y + i * rowH;
            _titleRects.Add((x, ry, w, rowH));
            if (i == _titleIndex)
            {
                FillRect(x + 6, ry, w - 12, rowH - 4, 0xC0405090);
                DrawText(Text.Render("▶", fs * 5 / 4, 0xFFFFE070), x + 16, ry + 5);
            }
            DrawText(imgs[i], x + (w - imgs[i].W) / 2, ry + 5);
        }

        // 맨 아래 — 팬메이드 알림
        var notice = Text.Render(FanMadeNotice, Math.Max(12, fs * 3 / 4), 0xE0FFFFFF, bold: false);
        FillRect(0, _clientH - notice.H - 20, _clientW, notice.H + 20, 0xA0000000);
        DrawText(notice, (_clientW - notice.W) / 2, _clientH - notice.H - 10);
    }
}
