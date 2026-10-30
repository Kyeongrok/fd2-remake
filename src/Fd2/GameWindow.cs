using System.Diagnostics;
using Fd2.Data;
using Fd2.Native;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Fd2;

/// <summary>
/// 장면 지도 위에서 우리 편을 걸려 보는 창. 순수 Win32 + D3D11.
/// </summary>
/// <remarks>
/// 지도는 장면마다 한 장의 텍스처(<c>_mapTex</c>, 원본 또는 고해상도 4배 — <see cref="HiRes"/> 가 다 된 조각부터 갈아 끼운다)로 올리고,
/// 사람은 매 틱 CPU 에서 창 해상도의 BGRA 한 장(<c>_fb</c>)에 찍는다. 셰이더가 지도 위에 사람 층을 얹어 화면 가득 삼각형 한 장으로 찍는다.
/// 보이는 판(게임 픽셀) = 창 ÷ 배율. 해상도·배율·전체화면·고해상도는 메뉴(GameWindow.Menu.cs).
/// <para>방향키: 고른 사람 한 칸씩 · 마우스 왼쪽: 그 칸까지 걸어감 · Tab: 다음 우리 편 ·
/// PageUp/PageDown: 앞뒤 장면 · F11: 전체화면 · ESC: 끝.</para>
/// </remarks>
internal sealed unsafe partial class GameWindow : IDisposable
{
    private const int T = Rle.Tile;

    private const double StepsPerSecond = 7;
    private const double WalkFps = 8, IdleFps = 3;
    private static readonly int[] WalkCycle = [0, 1, 2, 1];

    private const uint BgColor = 0xFF000000;
    private const uint CursorColor = 0xFFB4B4B4;   // 커서 · 고른 사람 테두리 — 회색

    /// <summary>보이는 판(게임 픽셀) — <see cref="Layout"/> 이 창 크기와 배율로 정한다.</summary>
    private int _boardW = 480, _boardH = 312;

    /// <summary>사람 층 — 창 해상도(<c>_fbW</c> x <c>_fbH</c>), 빈 곳은 투명.</summary>
    private uint[] _fb = [];
    private int _fbW, _fbH;

    private readonly string _dir;
    private readonly uint[] _pal;
    private readonly IconBank _icons;
    private readonly int _sceneCount;
    private Scene _scene = null!;
    private Unit? _selected;
    private double _time;
    private int _camX, _camY;

    private readonly HashSet<int> _keys = new();
    private bool _running;

    private IntPtr _hwnd;
    private static readonly Win32.WndProc StaticWndProcDelegate = StaticWndProcTrampoline;
    private static GameWindow? _active;
    private static ushort _classAtom;
    private const string ClassName = "Fd2Dx";

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID3D11RenderTargetView _backBufferRtv = null!;
    private ID3D11Texture2D _boardTex = null!;
    private ID3D11ShaderResourceView _boardSrv = null!;
    private ID3D11Texture2D? _mapTex;
    private ID3D11ShaderResourceView? _mapSrv;
    private ID3D11SamplerState _linear = null!;

    /// <summary>지도 텍스처가 원본의 몇 배인가 — 고해상도면 4, 아니면 1. <c>_mapHiScene</c> 은 그 텍스처의 장면.</summary>
    private int _mapScale = 1, _mapHiScene = -1;

    private readonly HiRes _hires;
    private ID3D11Buffer _viewCb = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;

    public GameWindow(string dir)
    {
        _dir = dir;
        _pal = GameFolder.Palette(dir);
        _icons = new IconBank(Path.Combine(dir, "FDICON.B24"), _pal);
        _sceneCount = Dat.Count(Path.Combine(dir, "FDFIELD.DAT")) / 3;
        _hires = new HiRes(dir, _pal, _sceneCount, _icons.Characters, _settings.HiRes);
    }

    // ── 창 열기 · 메시지 펌프 ─────────────────────────────────────────────────

    public void Run()
    {
        RegisterClassOnce();
        CreateDevice();
        CreateNativeWindow();
        if (!Offscreen) _pad = new GamePad(_hwnd, _keyMap, PadPress);
        CreateSwapChain();
        LoadScene(0);
        StartOpening();
        ApplyWindow();

        Win32.ShowWindow(_hwnd, Offscreen ? 4 : 5);   // 화면 밖 시험이면 포커스를 안 뺏는다(SW_SHOWNOACTIVATE)
        Win32.UpdateWindow(_hwnd);

        _running = true;
        var clock = Stopwatch.StartNew();
        double last = 0;

        while (_running)
        {
            while (Win32.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.Message == Win32.WM_QUIT) { _running = false; break; }
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
            }
            if (!_running) break;

            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - last, 0.25);
            last = now;

            Tick(dt);
            Render();
        }
    }

    private static void RegisterClassOnce()
    {
        if (_classAtom != 0) return;
        var wc = new Win32.WndClassEx
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.WndClassEx>(),
            Style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
            WindowProc = StaticWndProcDelegate,
            Instance = Win32.GetModuleHandleW(null),
            Cursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)Win32.IDC_ARROW),
            Icon = Win32.LoadIconW(Win32.GetModuleHandleW(null), (IntPtr)Win32.IDI_APPLICATION),
            ClassName = ClassName,
        };
        _classAtom = Win32.RegisterClassExW(ref wc);
    }

    private static IntPtr StaticWndProcTrampoline(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        _active != null ? _active.WndProc(hWnd, msg, wParam, lParam)
                        : Win32.DefWindowProcW(hWnd, msg, wParam, lParam);

    private void CreateNativeWindow()
    {
        _menuBar = CreateMenuBar();
        var rect = new Win32.Rect { Left = 0, Top = 0, Right = _settings.ViewW, Bottom = _settings.ViewH };
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, true);

        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, "용의기사2",
            Win32.WS_OVERLAPPEDWINDOW, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
            rect.Width, rect.Height,
            IntPtr.Zero, _menuBar, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("창을 만들지 못했습니다.");
        if (Win32.GetClientRect(_hwnd, out var client)) (_clientW, _clientH) = (client.Width, client.Height);
        UpdateMenuChecks();
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
            case Win32.WM_ERASEBKGND:
                return (IntPtr)1;
            case Win32.WM_SIZE:
                Resize(Win32.LowWord(lParam) & 0xFFFF, Win32.HighWord(lParam) & 0xFFFF);
                return IntPtr.Zero;
            case Win32.WM_COMMAND:
                OnMenuCommand((int)((long)wParam & 0xFFFF));
                return IntPtr.Zero;
            case Win32.WM_KEYDOWN:
                OnKeyDown((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_KEYUP:
                _keys.Remove((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_KILLFOCUS:
                _keys.Clear();
                break;
            case Win32.WM_MOUSEMOVE:
                (_mouseX, _mouseY) = (Win32.LowWord(lParam), Win32.HighWord(lParam));
                _mouseMoved = true;
                if (_screen == Screen.Title && !_keysOpen) TitleHover(_mouseX, _mouseY);
                MouseToCursor();
                return IntPtr.Zero;
            case Win32.WM_RBUTTONDOWN:
                if (InBattle) BattleCancel();
                return IntPtr.Zero;
            case Win32.WM_LBUTTONDOWN:
                (_mouseX, _mouseY) = (Win32.LowWord(lParam), Win32.HighWord(lParam));
                _mouseMoved = true;
                if (_keysOpen || _helpOpen) { KeysWindowKey(Win32.VK_ESCAPE); return IntPtr.Zero; }
                if (_screen != Screen.Game) { TitleClick(_mouseX, _mouseY); return IntPtr.Zero; }
                if (Talking) { TalkNext(); return IntPtr.Zero; }
                MouseToCursor();
                OnClick((int)Math.Floor((_mouseX - _offX) / (double)_zoom),
                        (int)Math.Floor((_mouseY - _offY) / (double)_zoom));
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnKeyDown(int vk)
    {
        _keys.Add(vk);
        if (vk == Win32.VK_F12) { _shotPending = true; return; }
        if (vk == Win32.VK_F11) { OnMenuCommand(MenuFullscreen); return; }
        if (KeysWindowKey(vk)) return;
        if (_screen != Screen.Game)
        {
            // 패드 · 바꾼 키로도 메뉴를 고를 수 있게 커서 키는 방향키로
            vk = ActionOf(vk) switch { KeyAction.MoveUp => Win32.VK_UP, KeyAction.MoveDown => Win32.VK_DOWN, _ => vk };
            TitleKey(vk);
            return;
        }

        // 바꿀 수 있는 키 — 커서 키는 방향키로 바꿔 넘기고, 목록 · 체력바는 어디서나
        switch (ActionOf(vk))
        {
            case KeyAction.MoveUp: vk = Win32.VK_UP; break;
            case KeyAction.MoveDown: vk = Win32.VK_DOWN; break;
            case KeyAction.MoveLeft: vk = Win32.VK_LEFT; break;
            case KeyAction.MoveRight: vk = Win32.VK_RIGHT; break;
            case KeyAction.KeyHelp: _helpOpen = true; return;
            case KeyAction.Gauges: _showGauges = !_showGauges; return;
        }
        if (!InBattle && vk is Win32.VK_UP or Win32.VK_DOWN or Win32.VK_LEFT or Win32.VK_RIGHT && _selected is { Moving: false } walker && walker.Path.Count == 0)
        {
            // 누르자마자 한 칸 — 패드처럼 눌렀다 바로 떼는 입력도 걷게
            var (dx, dy, d) = vk switch
            {
                Win32.VK_UP => (0, -1, Dir.Up), Win32.VK_DOWN => (0, 1, Dir.Down), Win32.VK_LEFT => (-1, 0, Dir.Left), _ => (1, 0, Dir.Right),
            };
            TryStep(walker, dx, dy, d);
            return;
        }
        if (TalkKey(vk)) return;
        if (InBattle)
        {
            _mouseMoved = false;   // 키로 고르는 중에는 마우스 자리로 메뉴 줄을 바꾸지 않는다
            if (BattleKey(vk)) return;
            if (vk is Win32.VK_TAB or Win32.VK_PRIOR or Win32.VK_NEXT) return;   // 전투 중에는 장면 · 사람 바꾸기를 막는다
        }
        switch (vk)
        {
            case Win32.VK_ESCAPE:
                ToTitle();   // 게임 중 Esc — 타이틀로(타이틀에서 Esc 는 끝내기)
                break;
            case Win32.VK_F11: OnMenuCommand(MenuFullscreen); break;
            case Win32.VK_TAB: SelectNext(); break;
            case Win32.VK_PRIOR: LoadScene((_scene.Index + _sceneCount - 1) % _sceneCount); break;
            case Win32.VK_NEXT: LoadScene((_scene.Index + 1) % _sceneCount); break;
        }
    }

    /// <summary>우리 편을 누르면 고르고, 빈 칸을 누르면 고른 사람이 그리로 걷는다.</summary>
    private void OnClick(int sx, int sy)
    {
        int tx = (int)Math.Floor((sx + _camX) / (double)T), ty = (int)Math.Floor((sy + _camY) / (double)T);
        if (InBattle) { BattleClick(tx, ty, _mouseX, _mouseY); return; }
        if (sx < 0 || sy < 0 || sx >= _boardW || sy >= _boardH) return;
        if (!_scene.Inside(tx, ty)) return;
        var hit = _scene.UnitAt(tx, ty);
        if (hit is { Side: Side.Player }) { _selected = hit; return; }
        if (_selected == null || _selected.Moving) return;
        var path = _scene.FindPath(_selected.X, _selected.Y, tx, ty);
        if (path == null) return;
        _selected.Path.Clear();
        foreach (var p in path) _selected.Path.Enqueue(p);
    }

    // ── 장면 ─────────────────────────────────────────────────────────────────

    private void LoadScene(int index)
    {
        _bs = BattleState.None;
        ClearSelection();
        _scene = new Scene(_dir, index, _pal, _icons);
        foreach (var u in _scene.Units) Equip(u, battle: false);
        _freeReachFor = default;
        _selected = _scene.Units.Find(u => u.Side == Side.Player);
        _keys.Clear();
        BuildMapTexture();
        if (_hires.Enabled) _hires.Want(index, _scene.Units.Select(u => u.Icon));
        Follow();
        Win32.SetWindowTextW(_hwnd, $"용의기사2 — 장면 {index + 1}/{_sceneCount}  " +
                                    "(방향키·클릭 이동, Tab 다음 사람, PgUp/PgDn 장면, F11 전체화면)");
        _titleStatus = "";
        UpdateMenuChecks();
    }

    /// <summary>
    /// 지도 텍스처를 새로 — 고해상도면 4배 크기로, 만들어 둔 것이 있으면 그것을, 없으면 원본을 점 그대로 네 배로 늘려 깔아 두고
    /// 다 된 조각이 올 때마다(<see cref="PumpHiRes"/>) 그 자리만 갈아 끼운다.
    /// </summary>
    private void BuildMapTexture()
    {
        int lw = _scene.Width * T, lh = _scene.Height * T;
        _mapScale = _hires.Enabled ? HiRes.Scale : 1;
        _mapHiScene = _scene.Index;
        int w = lw * _mapScale, h = lh * _mapScale;
        var px = _mapScale > 1 ? HiRes.LoadMap(_scene.Index, lw, lh) : null;
        if (px == null)
        {
            var low = _scene.RenderMap();
            if (_mapScale == 1) px = low;
            else
            {
                px = new uint[w * h];
                for (int y = 0; y < h; y++)
                {
                    int ly = y / _mapScale;
                    for (int x = 0; x < w; x++) px[y * w + x] = low[ly * lw + x / _mapScale];
                }
            }
        }
        _mapSrv?.Dispose();
        _mapTex?.Dispose();
        _mapTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
        });
        UploadRegion(px, 0, 0, w, h);
        _mapSrv = _device.CreateShaderResourceView(_mapTex);
    }

    private void UploadRegion(uint[] px, int x, int y, int w, int h)
    {
        fixed (uint* p = px)
            _ctx.UpdateSubresource(_mapTex!, 0, new Box(x, y, 0, x + w, y + h, 1), (IntPtr)p, (uint)(w * 4), 0);
    }

    /// <summary>뒤에서 다 만든 지도 조각을 지금 장면이면 텍스처에 올린다 — 「시간이 지나면 또렷해지는」 자리.</summary>
    private void PumpHiRes()
    {
        while (_hires.MapChunks.TryDequeue(out var c))
        {
            if (c.Scene != _mapHiScene || _mapScale != HiRes.Scale || _mapTex == null) continue;
            UploadRegion(c.Px, c.X, c.Y, c.W, c.H);
        }
    }

    private void SelectNext()
    {
        var players = _scene.Units.Where(u => u.Side == Side.Player).ToList();
        if (players.Count == 0) return;
        int i = _selected == null ? -1 : players.IndexOf(_selected);
        _selected = players[(i + 1) % players.Count];
    }

    // ── 한 틱 · 합성 ─────────────────────────────────────────────────────────

    private void Tick(double dt)
    {
        _time += dt;
        PollPad(_time);
        if (_screen != Screen.Game)
        {
            TitleTick(dt);
            PumpHiRes();
            UpdateTitleStatus();
            return;
        }
        PumpHiRes();
        UpdateTitleStatus();

        if (InBattle) BattleTick(dt);
        else if (_selected is { Moving: false } sel && sel.Path.Count == 0)
        {
            bool Held(int arrow, KeyAction action) => _keys.Contains(arrow) || _keys.Contains(_keyMap[action]);
            if (Held(Win32.VK_UP, KeyAction.MoveUp)) TryStep(sel, 0, -1, Dir.Up);
            else if (Held(Win32.VK_DOWN, KeyAction.MoveDown)) TryStep(sel, 0, 1, Dir.Down);
            else if (Held(Win32.VK_LEFT, KeyAction.MoveLeft)) TryStep(sel, -1, 0, Dir.Left);
            else if (Held(Win32.VK_RIGHT, KeyAction.MoveRight)) TryStep(sel, 1, 0, Dir.Right);
        }

        foreach (var u in _scene.Units)
        {
            if (u.Moving)
            {
                u.Step = Math.Min(1, u.Step + dt * StepsPerSecond);
                if (u.Moving) continue;
                (u.FromX, u.FromY) = (u.X, u.Y);
            }
            if (u.Path.TryDequeue(out var next))
            {
                int dx = next.X - u.X, dy = next.Y - u.Y;
                if (!TryStep(u, dx, dy, DirOf(dx, dy))) u.Path.Clear();
            }
        }

        Follow();
    }

    private static Dir DirOf(int dx, int dy) =>
        dx < 0 ? Dir.Left : dx > 0 ? Dir.Right : dy < 0 ? Dir.Up : Dir.Down;

    private bool TryStep(Unit u, int dx, int dy, Dir facing)
    {
        u.Facing = facing;
        if (!_scene.Free(u.X + dx, u.Y + dy)) return false;
        (u.FromX, u.FromY) = (u.X, u.Y);
        u.X += dx;
        u.Y += dy;
        u.Step = 0;
        return true;
    }

    /// <summary>고른 사람을 화면 가운데에. 지도가 화면보다 작으면 지도를 가운데에.</summary>
    private void Follow()
    {
        if (InBattle && !_enemyPhase)
        {
            if (_centerCursor)
            {
                _centerCursor = false;
                int mw = _scene.Width * T, mh = _scene.Height * T;
                _camX = mw <= _boardW ? (mw - _boardW) / 2 : Math.Clamp(_cursor.X * T + T / 2 - _boardW / 2, 0, mw - _boardW);
                _camY = mh <= _boardH ? (mh - _boardH) / 2 : Math.Clamp(_cursor.Y * T + T / 2 - _boardH / 2, 0, mh - _boardH);
            }
            else if (_scene.Width * T <= _boardW || _scene.Height * T <= _boardH) KeepCursorInView();
            return;
        }
        int mapW = _scene.Width * T, mapH = _scene.Height * T;
        double cx = _selected != null ? _selected.DrawX * T + T / 2 : mapW / 2;
        double cy = _selected != null ? _selected.DrawY * T + T / 2 : mapH / 2;
        int want(double c, int map, int view) =>
            map <= view ? (map - view) / 2 : (int)Math.Clamp(c - view / 2, 0, map - view);
        _camX = want(cx, mapW, _boardW);
        _camY = want(cy, mapH, _boardH);
    }

    private void Render()
    {
        Compose();
        Upload();
        Draw();
    }

    /// <summary>사람 층을 창 해상도로 찍는다 — 고해상도 그림이 있으면 그것을, 없으면 원본을 점 그대로 배율만큼 키워.</summary>
    private void Compose()
    {
        Array.Clear(_fb);
        if (_screen != Screen.Game)
        {
            DrawTitle();
            DrawKeysWindow();
            return;
        }
        int cell = T * _zoom;
        DrawRanges();

        // 아래쪽 사람이 위쪽 사람을 가리도록 y 순서로. 쓰러진 사람은 안 그린다.
        foreach (var u in _scene.Units.Where(u => u.Alive).OrderBy(u => u.DrawY))
        {
            int sx = _offX + (int)Math.Round((u.DrawX * T - _camX) * _zoom);
            int sy = _offY + (int)Math.Round((u.DrawY * T - _camY) * _zoom);
            if (sx >= _fbW || sy >= _fbH || sx + cell <= 0 || sy + cell <= 0) continue;
            bool walking = u.Moving || u.Path.Count > 0;
            int f = walking ? WalkCycle[(int)(_time * WalkFps) % WalkCycle.Length]
                            : WalkCycle[(int)(_time * IdleFps) % WalkCycle.Length];
            int frame = (int)u.Facing * 3 + f;
            var hi = _hires.Enabled ? _hires.Icon(u.Icon, frame) : null;
            bool dim = InBattle && u.Side == Side.Player && u.Acted;   // 행동을 마친 아군은 어둡게
            if (hi != null) Blit(hi, T * HiRes.Scale, sx, sy, cell, dim);
            else Blit(_icons.Frame(u.Icon, frame), T, sx, sy, cell, dim);
            if (u == (InBattle ? _actor : _selected)) Frame(sx, sy, cell, cell, 2 * Math.Max(1, _zoom / 2), CursorColor);
        }
        DrawBattleHud();
        DrawKeysWindow();
    }

    /// <summary>size x size 그림을 화면 (x, y) 에 cell x cell 로 — 가까운 점으로 늘리거나 줄인다. 투명(0)은 건너뛴다.</summary>
    private void Blit(uint[] px, int size, int x, int y, int cell, bool dim = false)
    {
        int y0 = Math.Max(0, y), y1 = Math.Min(_fbH, y + cell);
        int x0 = Math.Max(0, x), x1 = Math.Min(_fbW, x + cell);
        for (int yy = y0; yy < y1; yy++)
        {
            int srow = (yy - y) * size / cell * size;
            int drow = yy * _fbW;
            for (int xx = x0; xx < x1; xx++)
            {
                uint c = px[srow + (xx - x) * size / cell];
                if (c == 0) continue;
                _fb[drow + xx] = dim ? (c >> 1 & 0x7F7F7F) | 0xFF000000 : c | 0xFF000000;
            }
        }
    }

    private void Frame(int x, int y, int w, int h, int t, uint color)
    {
        for (int k = 0; k < t; k++)
        {
            for (int i = 0; i < w; i++) { Dot(x + i, y + k, color); Dot(x + i, y + h - 1 - k, color); }
            for (int i = 0; i < h; i++) { Dot(x + k, y + i, color); Dot(x + w - 1 - k, y + i, color); }
        }
    }

    private void Dot(int x, int y, uint color)
    {
        if ((uint)x < (uint)_fbW && (uint)y < (uint)_fbH) _fb[y * _fbW + x] = color;
    }

    // ── D3D11 / DXGI ─────────────────────────────────────────────────────────

    private void CreateDevice()
    {
        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels,
                                out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        _device = dev;
        _ctx = ctx;

        // view = (배율, 왼쪽 여백, 위 여백, 지도 텍스처 배수), board = (판 너비, 판 높이, 카메라 x, 카메라 y),
        // map = (지도 텍스처 너비, 높이, 부드럽게 1/0, 0). t0 = 사람 층(창 해상도), t1 = 지도.
        // 셰이더 글에는 한글을 넣지 않는다 — 컴파일러가 바이트 수로 읽어 끝이 잘린다.
        const string shader = """
            Texture2D<float4> Sprites : register(t0);
            Texture2D<float4> Map : register(t1);
            SamplerState Lin : register(s0);
            cbuffer View : register(b0) { float4 view; float4 board; float4 map; };
            struct VSOut { float4 pos : SV_Position; };
            VSOut VS(uint id : SV_VertexID)
            {
                VSOut o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                return o;
            }
            float4 PS(VSOut i) : SV_Target
            {
                float2 g = (i.pos.xy - view.yz) / view.x;
                if (map.w == 0 && (g.x < 0 || g.y < 0 || g.x >= board.x || g.y >= board.y)) return float4(0, 0, 0, 1);
                float2 m = (g + board.zw) * view.w;
                float4 c = float4(0, 0, 0, 1);
                if (map.w == 0 && m.x >= 0 && m.y >= 0 && m.x < map.x && m.y < map.y)
                    c = map.z > 0 ? Map.SampleLevel(Lin, m / map.xy, 0) : Map.Load(int3(int2(floor(m)), 0));
                float4 s = Sprites.Load(int3(int2(i.pos.xy), 0));
                return float4(lerp(c.rgb, s.rgb, s.a), 1);
            }
            """;
        var vsBlob = Compiler.Compile(shader, "VS", "board.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(shader, "PS", "board.hlsl", "ps_4_0");
        _vs = _device.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);
        _viewCb = _device.CreateBuffer(new BufferDescription(48, BindFlags.ConstantBuffer,
                                                             ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _linear = _device.CreateSamplerState(SamplerDescription.LinearClamp);
    }

    /// <summary>사람 층 버퍼와 텍스처를 창 크기로 다시 만든다.</summary>
    private void CreateBoardTexture()
    {
        _fbW = Math.Max(1, _clientW);
        _fbH = Math.Max(1, _clientH);
        _fb = new uint[_fbW * _fbH];
        _boardSrv?.Dispose();
        _boardTex?.Dispose();
        _boardTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_fbW,
            Height = (uint)_fbH,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        _boardSrv = _device.CreateShaderResourceView(_boardTex);
    }

    private void CreateSwapChain()
    {
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        var desc = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, _clientW),
            Height = (uint)Math.Max(1, _clientH),
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipDiscard,
            Scaling = Scaling.None,
        };
        _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferRtv = _device.CreateRenderTargetView(back);
        Layout();
    }

    /// <summary>창 크기가 바뀌면 백버퍼를 맞추고 배율·판 크기를 다시 셈한다.</summary>
    private void Resize(int w, int h)
    {
        if (w <= 0 || h <= 0 || _swapChain == null) return;
        if (w == _clientW && h == _clientH) return;
        _clientW = w;
        _clientH = h;
        _ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        _backBufferRtv.Dispose();
        _swapChain.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferRtv = _device.CreateRenderTargetView(back);
        Layout();
    }

    private void Upload()
    {
        var cb = _ctx.Map(_viewCb, 0, MapMode.WriteDiscard);
        var v = (float*)cb.DataPointer;
        v[0] = _zoom; v[1] = _offX; v[2] = _offY; v[3] = _mapScale;
        v[4] = _boardW; v[5] = _boardH; v[6] = _camX; v[7] = _camY;
        v[8] = _scene.Width * T * _mapScale; v[9] = _scene.Height * T * _mapScale; v[10] = _mapScale > 1 ? 1 : 0; v[11] = _screen == Screen.Game ? 0 : 1;
        _ctx.Unmap(_viewCb, 0);

        var map = _ctx.Map(_boardTex, 0, MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < _fbH; y++)
            {
                var dst = new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), _fbW);
                _fb.AsSpan(y * _fbW, _fbW).CopyTo(dst);
            }
        }
        finally { _ctx.Unmap(_boardTex, 0); }
    }

    private void Draw()
    {
        _ctx.OMSetRenderTargets(_backBufferRtv);
        _ctx.RSSetViewport(0, 0, _clientW, _clientH);
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetShaderResources(0, [_boardSrv, _mapSrv!]);
        _ctx.PSSetSamplers(0, [_linear]);
        _ctx.PSSetConstantBuffers(0, [_viewCb]);
        _ctx.Draw(3, 0);
        if (_shotPending) { _shotPending = false; SaveShot(); }
        _swapChain.Present(1, PresentFlags.None);
    }

    private bool _shotPending;

    /// <summary>F12 — 지금 화면을 %LOCALAPPDATA%\Fd2\shots 에 BMP 로(시각.bmp 와 last.bmp).</summary>
    private void SaveShot()
    {
        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        var desc = back.Description;
        desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        desc.MiscFlags = ResourceOptionFlags.None;
        using var staging = _device.CreateTexture2D(desc);
        _ctx.CopyResource(staging, back);
        var map = _ctx.Map(staging, 0, MapMode.Read);
        try
        {
            int w = (int)desc.Width, h = (int)desc.Height;
            var bytes = new byte[54 + w * h * 4];
            var span = bytes.AsSpan();
            span[0] = (byte)'B'; span[1] = (byte)'M';
            BitConverter.TryWriteBytes(span[2..], bytes.Length);
            BitConverter.TryWriteBytes(span[10..], 54);
            BitConverter.TryWriteBytes(span[14..], 40);
            BitConverter.TryWriteBytes(span[18..], w);
            BitConverter.TryWriteBytes(span[22..], -h);   // 위에서 아래로
            BitConverter.TryWriteBytes(span[26..], (short)1);
            BitConverter.TryWriteBytes(span[28..], (short)32);
            for (int y = 0; y < h; y++)
                new ReadOnlySpan<byte>((void*)(map.DataPointer + y * map.RowPitch), w * 4).CopyTo(span[(54 + y * w * 4)..]);
            for (int i = 54 + 3; i < bytes.Length; i += 4) bytes[i] = 255;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}.bmp"), bytes);
            File.WriteAllBytes(Path.Combine(dir, "last.bmp"), bytes);
        }
        finally { _ctx.Unmap(staging, 0); }
    }

    public void Dispose()
    {
        if (_active == this) _active = null;
        _backBufferRtv?.Dispose();
        _swapChain?.Dispose();
        _boardSrv?.Dispose();
        _boardTex?.Dispose();
        _mapSrv?.Dispose();
        _mapTex?.Dispose();
        _linear?.Dispose();
        _viewCb?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
        if (_hwnd != IntPtr.Zero) { Win32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
