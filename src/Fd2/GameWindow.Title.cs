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
        ? [.. _continueList.Select(s => (s.Label, (Action)(() => ContinueFrom(s.Campaign)))), ("세이브 연동(구글 · 개발중)", OpenCloudDialog), ("돌아가기", () => { _continueOpen = false; _titleIndex = 0; })]
        : [
        ("Start", StartGame),
        ("Load", () => { OpenContinue(); _titleIndex = 0; }),
        ("Setup", OpenSetup),   // 오프닝 켜고 끄기 · 배경음악 · 효과음 크기 창(w-102, GameWindow.Setup.cs)
        ("Exit", () => _running = false),
        ($"버전: {VersionName}  ▼", OpenVersions),   // 맨 끝 줄이지만 화면 맨 위(제목 그림의 파란 띠)에 그린다 — Start 에서 ↑ 하면 여기로
        ("릴리즈 노트", () => OpenNotes()),          // 버전 단추 옆(w-101)
    ];

    /// <summary>타이틀의 버전 줄 번호 — 메뉴 넷 뒤(이어하기 목록에는 없다).</summary>
    private int TitleVersionRow => _continueOpen ? -1 : 4;

    /// <summary>타이틀의 릴리즈 노트 단추 — 버전 단추 옆.</summary>
    private int TitleNotesRow => _continueOpen ? -1 : 5;

    /// <summary>켤 때 — Setup 에서 오프닝을 껐으면 바로 타이틀로(w-102).</summary>
    private void StartOpeningOrTitle()
    {
        if (_settings.ShowOpening) StartOpening();
        else ToTitle();
    }

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
        PlayEnd("quit");
        if (InBattle) { _bs = BattleState.None; ClearSelection(); SetMusic(-1); }   // 전투 곡을 끈다
        _screen = Screen.Title;
        _titleIndex = 0;
        _continueOpen = _setupOpen = false;   // Setup 창은 GameWindow.Setup.cs
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
            case Win32.VK_UP: _titleIndex = (_titleIndex + items.Length - 1) % items.Length; _loadFollow = true; PlaySfx(77, 2); break;
            case Win32.VK_DOWN: _titleIndex = (_titleIndex + 1) % items.Length; _loadFollow = true; PlaySfx(77, 2); break;
            case Win32.VK_RETURN or Win32.VK_SPACE: PlaySfx(77, 1); items[_titleIndex].Do(); break;
            case Win32.VK_ESCAPE when _continueOpen: _continueOpen = false; _titleIndex = 0; break;
            case Win32.VK_ESCAPE: _running = false; break;
        }
    }

    private void TitleClick(int mx, int my)
    {
        if (_screen == Screen.Opening) { SkipOpening(); return; }
        if (_continueOpen && LoadScrollClick(mx, my)) return;
        for (int i = 0; i < _titleRects.Count; i++)
        {
            var r = _titleRects[i];
            if (mx >= r.X && my >= r.Y && mx < r.X + r.W && my < r.Y + r.H) { _titleIndex = i; TitleItems[i].Do(); return; }
        }
    }

    // ── 불러오기 화면(사용자 요청 — 제목 그림 위 한 줄 글은 읽기 어려웠다) ─────────────────────────────
    // 그림을 어둡게 덮고, 기록마다 카드 한 장: 왼쪽 색 띠(전황 파랑 · 자동 청록 · 기록 금 · 원작 보라), 위 줄 이름 · 시각, 아래 줄 장 · 동료 · 돈.
    // 넘치면 고른 카드가 보이게 밀고, 위 · 아래 끝의 「▲ · ▼」 띠를 누르면 한 쪽씩 넘긴다. 세이브 연동 · 돌아가기는 맨 아래 단추 줄.

    private int _loadTop;
    private (int X, int Y, int W, int H) _loadUp, _loadDown;
    private int _loadPage = 1;

    /// <summary>고른 카드가 보이게 목록을 따라 밀까 — 끌거나 휠로 넘기면 끈다(키로 고르면 다시 켠다).</summary>
    private bool _loadFollow = true;

    /// <summary>카드 한 장 높이(사이 포함) · 끌기 시작할 때의 맨 위 카드.</summary>
    private int _loadStep = 1;
    private int? _loadDragTop;

    /// <summary>누른 채 끌면 목록을 넘긴다(폰, 사용자 요청). 처리했으면 참 — 끄는 동안은 고른 줄도 안 바꾼다.</summary>
    private bool LoadDrag(int y)
    {
        if (_screen != Screen.Title || !_continueOpen || _press is not { } p) { _loadDragTop = null; return false; }
        if (_loadDragTop == null && Math.Abs(y - p.Y) <= DragSlop * 2) return true;
        _loadDragTop ??= _loadTop;
        _loadFollow = false;
        _loadTop = Math.Clamp(_loadDragTop.Value - (y - p.Y) / Math.Max(1, _loadStep), 0, Math.Max(0, _continueList.Count - _loadPage));
        return true;
    }

    /// <summary>휠로 목록 넘기기.</summary>
    private bool LoadWheel(int notches)
    {
        if (_screen != Screen.Title || !_continueOpen) return false;
        _loadFollow = false;
        _loadTop = Math.Clamp(_loadTop - notches, 0, Math.Max(0, _continueList.Count - _loadPage));
        return true;
    }

    private bool LoadScrollClick(int mx, int my)
    {
        static bool In((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X && y >= r.Y && x < r.X + r.W && y < r.Y + r.H;
        int n = _continueList.Count;
        if (In(_loadUp, mx, my)) { _loadTop = Math.Max(0, _loadTop - _loadPage); _titleIndex = _loadTop; PlaySfx(77, 2); return true; }
        if (In(_loadDown, mx, my)) { _loadTop = Math.Min(Math.Max(0, n - _loadPage), _loadTop + _loadPage); _titleIndex = _loadTop; PlaySfx(77, 2); return true; }
        return false;
    }

    // 원작 창 색(컷인 정보 창 · 묻는 창) — 파란 바탕 · 연보라 테두리, 막대의 노랑(HP) · 초록(MP) · 빨강(깎인 몫)
    private const uint FdBlue = 0xFF28458A, FdBlueLit = 0xFF3A5CA8, FdEdge = 0xFF8090C0, FdEdgeLit = 0xFFA8B8E8,
                       FdYellow = 0xFFF0C030, FdGreen = 0xFF58A030, FdRed = 0xFFC02020, FdShade = 0xFF1A2C5C;

    /// <summary>사울 망토의 붉은빛(지도 그림 0 의 팔레트 색).</summary>
    private const uint SaulCape = 0xFFA60000;

    /// <summary>카드 왼쪽 띠 — 원작 색으로: 전황 사울 망토 빨강 · 자동 초록(MP) · 기록 연보라 · 원작 노랑(HP)(사용자 요청).</summary>
    private static uint LoadKindColor(string head) =>
        head.StartsWith("원작") ? FdYellow : head.StartsWith("자동") ? FdGreen : head.StartsWith("전황") ? SaulCape : FdEdgeLit;

    private void DrawLoadScreen(int fs)
    {
        var items = TitleItems;
        int n = _continueList.Count;
        FillRect(0, 0, _clientW, _clientH, 0xD8080A14);
        _titleRects.Clear();
        for (int i = 0; i < items.Length; i++) _titleRects.Add(default);
        (_loadUp, _loadDown) = (default, default);

        int pad = Math.Max(8, fs / 2), gap = Math.Max(6, fs / 3);
        int cw = Math.Min(_clientW - 2 * pad, fs * 30), cx = (_clientW - cw) / 2;

        // 머리 — 제목과 도움말
        var head = Text.Render("불러오기", fs * 3 / 2, 0xFFFFE070);
        var hint = Text.Render(n == 0 ? "이어 할 기록이 없습니다" : $"기록 {n}개 — 고르면 그 자리에서 이어 합니다", Math.Max(12, fs * 3 / 4), FdEdgeLit, bold: false);
        int y = pad + 4;
        DrawText(head, cx, y);
        DrawText(hint, cx + cw - hint.W, y + head.H - hint.H);
        y += head.H + gap;
        FillRect(cx, y, cw, 3, FdEdge);
        y += gap + 2;

        // 아래 단추 줄 — 세이브 연동 · 돌아가기
        int bfs = fs, bh = Text.Render("가", bfs, 0xFFFFFFFF).H + pad * 2, by = _clientH - bh - pad;
        int bw = (cw - gap) / 2;
        for (int k = 0; k < 2; k++)
        {
            int i = n + k, bx = cx + k * (bw + gap);
            bool sel = i == _titleIndex;
            var label = Text.Render(items[i].Label, bfs, sel ? 0xFFFFE070 : 0xFFFFFFFF);
            if (label.W > bw - pad) label = Text.Render(items[i].Label, Math.Max(12, bfs * (bw - pad) / Math.Max(1, label.W)), sel ? 0xFFFFE070 : 0xFFFFFFFF);
            FillRect(bx, by, bw, bh, sel ? FdBlueLit : FdBlue);
            Frame(bx, by, bw, bh, sel ? FdYellow : FdEdge);
            DrawText(label, bx + (bw - label.W) / 2, by + (bh - label.H) / 2);
            _titleRects[i] = (bx, by, bw, bh);
        }

        // 카드들
        int mainFs = fs, subFs = Math.Max(12, fs * 4 / 5);
        int cardH = Text.Render("가", mainFs, 0xFFFFFFFF).H + Text.Render("가", subFs, 0xFFFFFFFF).H + pad * 2 + 4;
        int listTop = y, listBottom = by - gap;
        int arrowH = Math.Max(fs, cardH / 2);
        int fit = Math.Max(1, (listBottom - listTop + gap) / (cardH + gap));
        if (n > fit) fit = Math.Max(1, (listBottom - listTop - 2 * (arrowH + gap) + gap) / (cardH + gap));   // ▲ ▼ 띠 자리
        _loadPage = fit;
        _loadStep = cardH + gap;
        if (_loadFollow && _titleIndex < n)
        {
            if (_titleIndex < _loadTop) _loadTop = _titleIndex;
            if (_titleIndex >= _loadTop + fit) _loadTop = _titleIndex - fit + 1;
        }
        _loadTop = Math.Clamp(_loadTop, 0, Math.Max(0, n - fit));
        int cy = listTop;
        if (n > fit)
        {
            _loadUp = Strip(cy, "▲", _loadTop > 0);
            cy += arrowH + gap;
        }
        for (int i = _loadTop; i < Math.Min(n, _loadTop + fit); i++)
        {
            string label = items[i].Label;
            int dash = label.IndexOf(" — ", StringComparison.Ordinal);
            string name = dash >= 0 ? label[..dash] : label, detail = dash >= 0 ? label[(dash + 3)..] : "", when = "";
            // 전황 · 자동의 끝 「· 11-02 17:58」은 위 줄 오른쪽으로
            int at = detail.LastIndexOf(" · ", StringComparison.Ordinal);
            if (at >= 0 && System.Text.RegularExpressions.Regex.IsMatch(detail[(at + 3)..], @"^\d\d-\d\d \d\d:\d\d$")) (detail, when) = (detail[..at], detail[(at + 3)..]);
            bool sel = i == _titleIndex;
            uint kind = LoadKindColor(name);
            FillRect(cx, cy, cw, cardH, sel ? FdBlueLit : FdBlue);
            Frame(cx, cy, cw, cardH, sel ? FdYellow : FdEdge);
            FillRect(cx + 3, cy + 3, Math.Max(4, pad / 2), cardH - 6, kind);
            int tx = cx + Math.Max(4, pad / 2) + pad;
            var nameImg = Text.Render(name, mainFs, sel ? 0xFFFFE070 : 0xFFFFFFFF);
            DrawText(nameImg, tx, cy + pad);
            if (when.Length > 0)
            {
                var whenImg = Text.Render(when, subFs, FdEdgeLit, bold: false);
                DrawText(whenImg, cx + cw - whenImg.W - pad, cy + pad + (nameImg.H - whenImg.H) / 2);
            }
            if (detail.Length > 0)
            {
                var d = Text.Render(detail, subFs, 0xFFFFFFFF, bold: false);
                int room = cx + cw - pad - tx;
                if (d.W > room) d = Text.Render(detail, Math.Max(10, subFs * room / Math.Max(1, d.W)), 0xFFFFFFFF, bold: false);
                DrawText(d, tx, cy + pad + nameImg.H + 4);
            }
            _titleRects[i] = (cx, cy, cw, cardH);
            cy += cardH + gap;
        }
        if (n > fit) _loadDown = Strip(listBottom - arrowH, "▼", _loadTop + fit < n);

        (int X, int Y, int W, int H) Strip(int sy, string mark, bool on)
        {
            FillRect(cx, sy, cw, arrowH, on ? FdBlue : FdShade);
            var m = Text.Render(mark, Math.Max(12, arrowH * 2 / 3), on ? 0xFFFFE070 : FdBlueLit);   // 넘길 곳이 없으면 바탕에 묻히게(글자 그림은 반투명이 안 먹는다)
            DrawText(m, cx + (cw - m.W) / 2, sy + (arrowH - m.H) / 2);
            return on ? (cx, sy, cw, arrowH) : default;
        }

        void Frame(int fx, int fy, int fw, int fh, uint c)   // 원작 창처럼 굵은 테두리
        {
            int t = 3;
            FillRect(fx, fy, fw, t, c); FillRect(fx, fy + fh - t, fw, t, c);
            FillRect(fx, fy, t, fh, c); FillRect(fx + fw - t, fy, t, fh, c);
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

    /// <summary>그림을 (dx, dy) 에서 dw x dh 로 늘려 찍는다(가까운 점 — 정수 배가 아니어도).</summary>
    private void BlitFit(uint[] px, int w, int h, int dx, int dy, int dw, int dh)
    {
        for (int y = Math.Max(0, dy); y < Math.Min(_fbH, dy + dh); y++)
        {
            int sy = (y - dy) * h / dh, row = y * _fbW;
            for (int x = Math.Max(0, dx); x < Math.Min(_fbW, dx + dw); x++)
            {
                uint c = px[sy * w + (x - dx) * w / dw];
                if (c >> 24 != 0) _fb[row + x] = c;
            }
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
        int fs = Math.Max(14, 7 * HudZoom);
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
            if (_versionsOpen) return;   // 켤 때 버전 고르기 — 오프닝은 고른 뒤에
            var skip = Text.Render(TouchUi ? "누르면 건너뜁니다" : "아무 키나 누르면 건너뜁니다", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
            DrawText(skip, _clientW - skip.W - 16, _clientH - skip.H - 12);
            return;
        }

        // 제목 그림은 화면에 꽉 차게(비율은 그대로, 정수 배가 아니어도) — 메뉴는 그림 아래쪽 불길(원작 메뉴 글씨 164 ~ 190줄) 위에, 버전은 맨 위 파란 띠에
        double ls = Math.Min(_clientW / (double)AfmPlayer.Width, _clientH / (double)AfmPlayer.Height);
        int lw = (int)(AfmPlayer.Width * ls), lh = (int)(AfmPlayer.Height * ls);
        int lx = (_clientW - lw) / 2, ly = (_clientH - lh) / 2;
        if (_titleBack != null) BlitFit(_titleBack, AfmPlayer.Width, AfmPlayer.Height, lx, ly, lw, lh);
        MaybeOpenTutorial();   // 폰 — 완전 처음 시작이면 조작법 안내(w-91 · fb-85)
        MaybeOpenNotes();      // 게임 버전이 바뀌었으면 릴리즈 노트(w-101)
        if (_continueOpen) { DrawLoadScreen(fs); return; }
        if (TouchUi)
        {
            // 폰은 창 제목 줄이 없어 게임 버전이 안 보였다 — 오른쪽 위에 작게(사용자 요청)
            var ver = Text.Render(TitleVersion, Math.Max(12, fs * 3 / 4), 0xC0FFFFFF, bold: false);
            DrawText(ver, _clientW - ver.W - 12, 8);
        }

        var items = TitleItems;
        int vrow = TitleVersionRow, nrow = TitleNotesRow;
        var menuRows = Enumerable.Range(0, items.Length).Where(i => i != vrow && i != nrow).ToList();
        // 긴 줄(전황 불러오기의 「원작 기록 1 — 27장 앞 · 동료 31명 · 돈 …」)이 화면 폭에 안 들면 글을 줄인다 — 고른 줄의 ▶ 가 첫 글자를 덮었다(사용자 요청)
        int big = fs * 3 / 2;
        var arrow = Text.Render("▶", big, 0xFFFFE070);
        int widest = menuRows.Max(i => Text.Render(items[i].Label, big, 0xFFFFFFFF).W), need = widest + 2 * (arrow.W + 24);
        if (need > _clientW - 8)
        {
            big = Math.Max(12, big * (_clientW - 8) / need);
            arrow = Text.Render("▶", big, 0xFFFFE070);
        }
        var imgs = items.Select((it, i) => Text.Render(it.Label, i == vrow || i == nrow ? fs : big, i == _titleIndex ? 0xFFFFE070 : 0xFFFFFFFF)).ToList();
        int rowH = menuRows.Max(i => imgs[i].H) + 14, w = Math.Min(_clientW - 8, menuRows.Max(i => imgs[i].W) + 2 * (arrow.W + 24));
        // 팬메이드 알림 — 화면 폭에 안 들면(휴대폰 세로) 문장마다 줄을 나눈다.
        int noticeSize = Math.Max(12, fs * 3 / 4);
        var notice = new[] { Text.Render(FanMadeNotice, noticeSize, 0xE0FFFFFF, bold: false) }.ToList();
        if (notice[0].W > _clientW - 16 && FanMadeNotice.Split(". ") is { Length: > 1 } sentences)
            notice = sentences.Select((line, k) => k < sentences.Length - 1 ? line + "." : line)
                .Select(line => Text.Render(line, noticeSize, 0xE0FFFFFF, bold: false)).ToList();
        int noticeH = notice.Sum(n => n.H + 4) - 4;
        int bottom = _clientH - noticeH - 24;
        int x = (_clientW - w) / 2, y = Math.Max(8, Math.Min(ly + (int)(162 * ls), bottom - rowH * menuRows.Count - 10));
        _titleRects.Clear();
        for (int i = 0; i < items.Length; i++) _titleRects.Add(default);
        FillRect(x, y - 10, w, rowH * menuRows.Count + 20, 0xA0101020);
        for (int k = 0; k < menuRows.Count; k++)
        {
            int i = menuRows[k], ry = y + k * rowH;
            int tx = _continueOpen ? x + arrow.W + 24 : x + (w - imgs[i].W) / 2;   // 불러오기 목록은 글 시작을 왼쪽으로 맞춘다(사용자 요청)
            _titleRects[i] = (x, ry, w, rowH);
            if (i == _titleIndex)
            {
                FillRect(x + 6, ry, w - 12, rowH - 4, 0xC0405090);
                DrawText(arrow, Math.Max(x + 8, tx - arrow.W - 8), ry + 5);   // 글 바로 왼쪽에
            }
            DrawText(imgs[i], tx, ry + 5);
        }
        if (vrow >= 0)
        {
            // 버전 고르기 — 맨 위 가운데 작은 단추
            // 버전 고르기 · 릴리즈 노트 — 맨 위 가운데 작은 단추 둘(w-101)
            var v = imgs[vrow];
            var nimg = nrow >= 0 ? imgs[nrow] : null;
            int vw = v.W + 40, vh = v.H + 12, nw = nimg != null ? nimg.W + 32 : 0, sep = nimg != null ? 8 : 0;
            if (vw + sep + nw > _clientW - 8) { nw = nimg != null ? Math.Min(nw, (_clientW - 8) / 3) : 0; vw = Math.Min(vw, _clientW - 8 - sep - nw); }
            int vx = (_clientW - vw - sep - nw) / 2, vy = Math.Max(4, ly + (int)(2 * ls));
            _titleRects[vrow] = (vx, vy, vw, vh);
            FillRect(vx, vy, vw, vh, vrow == _titleIndex ? 0xD0405090 : 0xA0101020);
            DrawText(v, vx + 20, vy + 6);
            if (nimg != null)
            {
                int nx = vx + vw + sep;
                _titleRects[nrow] = (nx, vy, nw, vh);
                FillRect(nx, vy, nw, vh, nrow == _titleIndex ? 0xD0405090 : 0xA0101020);
                DrawText(nimg, nx + (nw - nimg.W) / 2, vy + 6);
            }
        }

        // 맨 아래 — 팬메이드 알림
        FillRect(0, _clientH - noticeH - 20, _clientW, noticeH + 20, 0xA0000000);
        for (int k = 0, ny = _clientH - noticeH - 10; k < notice.Count; ny += notice[k].H + 4, k++)
            DrawText(notice[k], (_clientW - notice[k].W) / 2, ny);
    }
}
