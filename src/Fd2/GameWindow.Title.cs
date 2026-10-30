using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 켜면 — 오프닝(sub_1F894) → 타이틀 메뉴(제목 그림 FDOTHER 7 의 0번 · 팔레트 8 위에, 맨 아래 팬메이드 알림) → 게임.
/// 오프닝: 漢堂 로고(74, 팔레트 76) → ANI 편 3 → 긴 두루마리(69 ~ 73 을 320x735 로 붙여 535 → 0 줄, 30ms씩, 팔레트 101).
/// 두루마리 도중 450 · 10 에 그림 100 · 75 를 잠깐, 330 · 210 · 110 · 25 에 ANI 편 4 · 5 / 6 · 7 / 8 / 0,
/// 표 0x5204E 의 14 자리에서 번개(소리 77 의 0, 팔레트 102 를 11 줄 동안). 끝나거나 키를 누르면(sub_10620)
/// 빨갛게 바래고 → ANI 편 1(첫 컷에 소리 78) → 소리 77 의 3 과 함께 희푸른 빛에서 제목 그림이 드러난다. 그 사이 키는 바로 타이틀로.
/// </summary>
internal sealed partial class GameWindow
{
    private enum Screen { Opening, Title, Prologue, Game, Ending }

    private const string FanMadeNotice = "이 게임은 팬메이드 게임입니다. 원작 및 모드의 저작권은 각 저작권자에게 있습니다.";

    /// <summary>오프닝 한 컷 — Len 초 동안 Draw(t)(320x200 _of 에 그리고 밝기 _oLight), 들어설 때 Enter.</summary>
    private sealed record OCut(double Len, Action<double> Draw, Action? Enter = null);

    /// <summary>두루마리 한 줄(delay 30) · 빨강 · 희푸른 빛 바래기 한 단계(delay 8).</summary>
    private const double ScrollStep = 0.030, TintStep = 0.008;

    /// <summary>번개 자리(표 0x5204E, 끝 1000 은 막이) — 두루마리가 이 줄에 오면 소리 77 의 0 · 팔레트 102, 11 줄 뒤 101.</summary>
    private static readonly int[] Thunder = [520, 430, 410, 340, 310, 300, 240, 180, 150, 130, 110, 87, 64, 22];

    private Screen _screen = Screen.Opening;
    private List<OCut>? _oCuts;
    private int _oCut, _oTitleIntro;
    private double _oT, _oLight;
    private readonly uint[] _of = new uint[AfmPlayer.Width * AfmPlayer.Height];
    private uint[]? _oHeld, _titleBack;

    // 오프닝 자료 — ANI.DAT, 두루마리(색 번호), 팔레트 101 · 102, 그림 74 · 75 · 100
    private byte[]? _ani, _scroll;
    private uint[]? _pal101, _pal102, _img74, _img75, _img100;
    private AfmPlayer? _film;
    private uint[]? _filmFrame;
    private int _filmShown, _thunder, _flashAt;
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
        _titleBack ??= TitleImage();
        try { LoadOpening(); }
        catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { _oCuts = null; }
        if (_oCuts == null) { ToTitle(); return; }
        _screen = Screen.Opening;
        _oCut = -1;
        _oT = 0;
        _thunder = 0;
        _flashAt = int.MinValue / 2;
        NextOCut();
    }

    private void ToTitle()
    {
        _oCuts = null;
        _ani = _scroll = null;
        _film = null;
        _filmFrame = _oHeld = _img74 = _img75 = _img100 = null;
        if (InBattle) { _bs = BattleState.None; ClearSelection(); SetMusic(-1); }   // 전투 곡을 끈다
        _screen = Screen.Title;
        _titleIndex = 0;
        _continueOpen = false;
        _hasSaves = null;
        _titleBack ??= TitleImage();
    }

    /// <summary>제목 그림 — FDOTHER 7(LLLLLL 7: 0 = 320x200, 1 ~ 6 = 원작 메뉴 글씨) 의 0번을 팔레트 8 로.</summary>
    private uint[]? TitleImage()
    {
        try
        {
            string other = Path.Combine(_dir, "FDOTHER.DAT");
            var e = Dat.Load(other, 7);
            int o = Dat.U32(e, 6);
            if (Dat.U16(e, o) != AfmPlayer.Width || Dat.U16(e, o + 2) != AfmPlayer.Height) return null;
            return Rle.Decode(e, o + 4, AfmPlayer.Width, AfmPlayer.Height, Data.Palette.FromVga(Dat.Load(other, 8)));
        }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    private void LoadOpening()
    {
        string other = Path.Combine(_dir, "FDOTHER.DAT");
        uint[] Pal(int n) => Data.Palette.FromVga(Dat.Load(other, n));
        uint[] Image(int n, uint[] pal, int h = AfmPlayer.Height)
        {
            var e = Dat.Load(other, n);
            if (Dat.U16(e, 0) != AfmPlayer.Width || Dat.U16(e, 2) != h) throw new IOException($"FDOTHER {n}");
            return Rle.Decode(e, 4, AfmPlayer.Width, h, pal);
        }
        _ani = AfmPlayer.Load(_dir);
        var p76 = Pal(76);
        _img74 = Image(74, p76);
        _img75 = Image(75, p76);
        _img100 = Image(100, Pal(99));
        _pal101 = Pal(101);
        _pal102 = Pal(102);
        // 두루마리 다섯 조각(320x147)을 색 번호 그대로 — 번호표 0 ~ 255 로 풀면 번호가 나온다(빈 칸은 0)
        var ids = Enumerable.Range(0, 256).Select(i => (uint)i).ToArray();
        _scroll = new byte[AfmPlayer.Width * 147 * 5];
        for (int k = 0; k < 5; k++)
        {
            var piece = Image(69 + k, ids, 147);
            for (int i = 0; i < piece.Length; i++) _scroll[AfmPlayer.Width * 147 * k + i] = (byte)piece[i];
        }
        _oCuts = OpeningCuts();
    }

    // ── 컷 목록(sub_1F894 차례 그대로) ──────────────────────────────────────

    private List<OCut> OpeningCuts()
    {
        var c = new List<OCut>();
        var player = new AfmPlayer(_ani!);
        // 그림 한 장 — 밝아지고(sub_1F525) hold 초, 어두워진다(sub_1F882)
        void Still(uint[]? img, double hold)
        {
            c.Add(new OCut(EFade, t => { Put320(img); _oLight = t / EFade - 1; }));
            c.Add(new OCut(hold, _ => Put320(img)));
            c.Add(new OCut(EFade, t => { Put320(img); _oLight = -t / EFade; }));
        }
        // ANI 편 하나(sub_20421 — 컷마다 delay ms), 뒤에 어둡게(sub_1F81E 의 sub_1F882)
        void Film(int film, int ms, bool fadeOut = true, Action? first = null)
        {
            double cut = ms / 1000.0;
            int cuts = player.CutsOf(film);
            c.Add(new OCut(cuts * cut, t => FilmAt(Math.Min(cuts - 1, (int)(t / cut))), () =>
            {
                _film = new AfmPlayer(_ani!, film);
                _filmShown = -1;
                first?.Invoke();
            }));
            if (fadeOut) c.Add(new OCut(EFade, t => { FilmAt(_filmShown); _oLight = -t / EFade; }));
        }
        void Run(int from, int to) => c.Add(new OCut((from - to + 1) * ScrollStep, t => ScrollAt(from - Math.Min(from - to, (int)(t / ScrollStep)))));
        void Out(int n) => c.Add(new OCut(EFade, t => { ScrollAt(n, before: true); _oLight = -t / EFade; }));
        // 끼운 것 뒤 — 팔레트 101 로 되돌려 밝아진다(그 줄의 번개는 그 뒤)
        void In(int n) => c.Add(new OCut(EFade, t => { ScrollAt(n); _oLight = t / EFade - 1; }, () => _flashAt = int.MinValue / 2));

        Still(_img74, 31 * ETick);                      // 漢堂 로고 — sub_17AA9(1) · (30)
        Film(3, 90);
        In(535);
        Run(535, 451);
        Out(450); Still(_img100, 7 * ETick); In(450);   // sub_1F73F(100, 99) — 틱 1 + 6
        Run(450, 331);
        Out(330); Film(4, 90); Film(5, 50); In(330);
        Run(330, 211);
        Out(210); Film(6, 90); Film(7, 50); In(210);
        Run(210, 111);
        Out(110); Film(8, 90); In(110);
        Run(110, 26);
        Film(0, 15); In(25);                            // 25 는 어둡게 하지 않고 바로 편 0
        Run(25, 11);
        Out(10); Still(_img75, 7 * ETick); In(10);      // sub_1F73F(75, 76)
        Run(10, 0);
        c.Add(new OCut(1.0, _ => ScrollAt(0)));         // 0 줄에서 delay 1000

        // 여기부터 제목 앞(LABEL_32) — 오프닝 중 키를 누르면 이리로
        _oTitleIntro = c.Count;
        c.Add(new OCut(41 * TintStep, t => { Held(); Toward(0xFFFF0000, (40 - Math.Min(40, (int)(t / TintStep))) / 40.0); },
            () => _oHeld = (uint[])_of.Clone()));       // sub_2DF01(…, 63, 0, 0) — 빨강 하나로
        c.Add(new OCut(0.1, _ => Array.Fill(_of, 0xFFFF0000)));
        Film(1, 15, fadeOut: false, () => PlaySfx(78, 0));
        c.Add(new OCut(41 * TintStep, t => { Put320(_titleBack); Toward(0xFFE3F3FF, Math.Min(40, (int)(t / TintStep)) / 40.0); },
            () => PlaySfx(77, 3)));                     // sub_2DF01(…, 56, 60, 63) — 희푸른 빛에서 팔레트 8 로
        return c;
    }

    private void NextOCut()
    {
        if (_oCuts == null) return;
        if (++_oCut >= _oCuts.Count) { ToTitle(); return; }
        _oCuts[_oCut].Enter?.Invoke();
    }

    private void Put320(uint[]? img)
    {
        if (img != null) Array.Copy(img, _of, _of.Length);
        else Array.Fill(_of, 0xFF000000);
    }

    private void Held() => Put320(_oHeld);

    /// <summary>편의 cut 번째 컷까지 — Next 는 앞으로만 간다.</summary>
    private void FilmAt(int cut)
    {
        while (_film != null && _filmShown < cut && _film.Next() is { } f) { _filmFrame = f; _filmShown++; }
        Put320(_filmFrame);
    }

    /// <summary>
    /// 두루마리 n 줄부터 200 줄(sub_11EB0). 번개는 원작처럼 줄을 셀 때 — 끼운 것 앞(before)이면 그 줄의 번개는 아직.
    /// </summary>
    private void ScrollAt(int n, bool before = false)
    {
        while (_thunder < Thunder.Length && (Thunder[_thunder] > n || Thunder[_thunder] == n && !before))
        {
            if (Thunder[_thunder] - n < 11) { PlaySfx(77, 0); _flashAt = Thunder[_thunder]; }   // 늦어 지나친 것은 건너뛴다
            _thunder++;
        }
        if (_scroll == null || _pal101 == null || _pal102 == null) { Array.Fill(_of, 0xFF000000); return; }
        var pal = n <= _flashAt && n > _flashAt - 11 ? _pal102 : _pal101;
        int o = AfmPlayer.Width * Math.Clamp(n, 0, 535);
        for (int i = 0; i < _of.Length; i++) _of[i] = pal[_scroll[o + i]];
    }

    /// <summary>팔레트를 한 빛깔 쪽으로(sub_2DF01) — k = 1 이면 그대로, 0 이면 그 빛깔 하나.</summary>
    private void Toward(uint rgb, double k)
    {
        int tr = (int)(rgb >> 16 & 255), tg = (int)(rgb >> 8 & 255), tb = (int)(rgb & 255);
        for (int i = 0; i < _of.Length; i++)
        {
            uint c = _of[i];
            int r = tr + (int)(((int)(c >> 16 & 255) - tr) * k), g = tg + (int)(((int)(c >> 8 & 255) - tg) * k), b = tb + (int)(((int)(c & 255) - tb) * k);
            _of[i] = 0xFF000000 | (uint)r << 16 | (uint)g << 8 | (uint)b;
        }
    }

    /// <summary>팔레트 6비트 값에 빼고 더하기(sub_11D40 · sub_11DF2) — light −1 검게 · 0 그대로 · +1 희게. 8비트로는 4배.</summary>
    private static void Lighten(uint[] px, double light)
    {
        if (light == 0) return;
        int d = (int)Math.Round(light * 63) * 4;
        for (int p = 0; p < px.Length; p++)
        {
            uint c = px[p];
            int r = Math.Clamp((int)(c >> 16 & 255) + d, 0, 255), g = Math.Clamp((int)(c >> 8 & 255) + d, 0, 255), b = Math.Clamp((int)(c & 255) + d, 0, 255);
            px[p] = 0xFF000000 | (uint)r << 16 | (uint)g << 8 | (uint)b;
        }
    }

    private void TitleTick(double dt)
    {
        if (_screen != Screen.Opening || _oCuts == null) return;
        _oT += dt;
        while (_oCuts != null && _oCut < _oCuts.Count && _oT >= _oCuts[_oCut].Len)
        {
            _oT -= _oCuts[_oCut].Len;
            // 지나는 컷도 끝 모습을 한 번 그린다 — 편 컷 · 번개 셈 · 붙잡은 그림이 앞으로 가게
            _oLight = 0;
            _oCuts[_oCut].Draw(_oCuts[_oCut].Len);
            NextOCut();
        }
    }

    /// <summary>오프닝에서 키 · 클릭 — 두루마리까지는 제목 앞 연출로(원작 LABEL_32), 그 뒤는 바로 타이틀로.</summary>
    private void SkipOpening()
    {
        if (_oCuts == null || _oCut >= _oTitleIntro) { ToTitle(); return; }
        _oCut = _oTitleIntro - 1;
        _oT = 0;
        NextOCut();
    }

    /// <summary>오프닝 · 타이틀에서 받은 키 — 오프닝은 아무 키나 건너뛰고, 타이틀은 위아래 · 결정 · Esc(끝내기).</summary>
    private void TitleKey(int vk)
    {
        if (_screen == Screen.Opening) { SkipOpening(); return; }
        var items = TitleItems;
        switch (vk)
        {
            // 원작 제목 메뉴 소리 — 소리 77 의 2(커서) · 1(결정)
            case Win32.VK_UP: _titleIndex = (_titleIndex + items.Length - 1) % items.Length; PlaySfx(77, 2); break;
            case Win32.VK_DOWN: _titleIndex = (_titleIndex + 1) % items.Length; PlaySfx(77, 2); break;
            case Win32.VK_RETURN or Win32.VK_SPACE: PlaySfx(77, 1); items[_titleIndex].Do(); break;
            case Win32.VK_ESCAPE when _continueOpen: _continueOpen = false; _titleIndex = 0; break;
            case Win32.VK_ESCAPE: _running = false; break;
        }
    }

    private void TitleClick(int mx, int my)
    {
        if (_screen == Screen.Opening) { SkipOpening(); return; }
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
            if (_oCuts != null && _oCut >= 0 && _oCut < _oCuts.Count)
            {
                var cut = _oCuts[_oCut];
                _oLight = 0;
                cut.Draw(Math.Min(_oT, cut.Len));
                Lighten(_of, _oLight);
                DrawScreen320(_of);
            }
            var skip = Text.Render(TouchUi ? "누르면 건너뜁니다" : "아무 키나 누르면 건너뜁니다", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
            DrawText(skip, _clientW - skip.W - 16, _clientH - skip.H - 12);
            return;
        }

        // 제목 그림은 위쪽 3/4 안에(「LEGEND OF GOLDEN CASTLE」 띠가 160줄까지), 메뉴는 그 밑 — 원작 메뉴 글씨(164 ~ 190줄) 자리
        int ls = Math.Max(1, Math.Min(_clientW / AfmPlayer.Width, _clientH * 3 / 4 / AfmPlayer.Height));
        int lx = (_clientW - AfmPlayer.Width * ls) / 2, ly = Math.Max(0, _clientH / 60);
        if (_titleBack != null) BlitScaled(_titleBack, AfmPlayer.Width, AfmPlayer.Height, lx, ly, ls, 0, 0, _clientW, _clientH);

        var items = TitleItems;
        var imgs = items.Select((it, i) => Text.Render(it.Label, fs * 5 / 4, i == _titleIndex ? 0xFFFFE070 : 0xFFFFFFFF)).ToList();
        int rowH = imgs.Max(i => i.H) + 12, w = imgs.Max(i => i.W) + 80;
        int x = (_clientW - w) / 2, y = Math.Min(ly + 160 * ls, _clientH - rowH * items.Length - 80);
        if (ly + 160 * ls + rowH * items.Length + 40 > _clientH && _titleBack != null)
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
