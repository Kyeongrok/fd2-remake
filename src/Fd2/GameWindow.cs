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

    private HiRes _hires = null!;
    private ID3D11Buffer _viewCb = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;

    partial void PlatformInit() => _hires = new HiRes(_dir, _pal, _sceneCount, _icons.Characters, _settings.HiRes);

    // ── 창 열기 · 메시지 펌프 ─────────────────────────────────────────────────

    public void Run()
    {
        RegisterClassOnce();
        CreateDevice();
        CreateNativeWindow();
        if (!Offscreen) _pad = new GamePad(_hwnd, _keyMap, PadPress);
        CreateSwapChain();
        Start();
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

        // 캡션 없는 창 — 제목 줄은 우리가 그린다(GameWindow.TitleBar.cs). 메뉴 막대도 달지 않고 햄버거로 내린다.
        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, _caption,
            Win32.WS_OWNTITLE, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
            _settings.ViewW, _settings.ViewH + BarBaseH,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("창을 만들지 못했습니다.");
        UpdateBarMetrics();
        if (Win32.GetClientRect(_hwnd, out var client)) (_winW, _winH) = (client.Width, client.Height);
        (_clientW, _clientH) = (_winW, Math.Max(1, _winH - _barH));
        UpdateMenuChecks();
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (BarMessage(hWnd, msg, wParam, lParam, out var handled)) return handled;
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
                KeyDown((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_KEYUP:
                KeyUp((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_SYSKEYDOWN:
                // Alt 와 F10 은 시스템 키로 온다 — 마을 비밀상점 열쇠(Alt+F · Shift+F10)를 받게 넘기고, Alt+F4 · 메뉴는 윈도우에
                if ((int)wParam == Win32.VK_MENU) KeyDown(Win32.VK_MENU);
                else if ((int)wParam is >= 0x70 and <= 0x79 && (int)wParam != 0x73)
                {
                    KeyDown((int)wParam);
                    return IntPtr.Zero;
                }
                break;
            case Win32.WM_SYSKEYUP:
                KeyUp((int)wParam);
                break;
            case Win32.WM_KILLFOCUS:
                _keys.Clear();
                break;
            case Win32.WM_MOUSEMOVE:
                PointerMove(Win32.LowWord(lParam), Win32.HighWord(lParam) - _barH);
                return IntPtr.Zero;
            case Win32.WM_RBUTTONDOWN:
                PointerCancel();
                return IntPtr.Zero;
            case Win32.WM_LBUTTONDOWN:
                PointerDown(Win32.LowWord(lParam), Win32.HighWord(lParam) - _barH);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ── 갈고리(GameWindow.Core.cs 가 부르는 플랫폼 일) ───────────────────────────

    partial void OnSceneLoaded()
    {
        _cpuMap = _backdrop != null;   // 밑그림이 있는 장면은 지도를 CPU 로(타일의 빈 점으로 밑그림이 보이게)
        BuildMapTexture();
        if (_hires.Enabled) _hires.Want(_scene.Index, _scene.Units.Select(u => u.Icon));
        UpdateMenuChecks();
    }

    partial void PlatformTick()
    {
        PollPad(_time);
        PumpHiRes();
        UpdateTitleStatus();
    }

    partial void ToggleFullscreen() => OnMenuCommand(MenuFullscreen);

    partial void RequestShot() => _shotPending = true;

    private partial uint[]? HiIcon(int icon, int frame) => _hires.Enabled ? _hires.Icon(icon, frame) : null;

    partial void OnFbResized()
    {
        if (_device == null) return;
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

    /// <summary>
    /// 지도 텍스처를 새로 — 고해상도면 4배 크기로, 만들어 둔 것이 있으면 그것을, 없으면 원본을 점 그대로 네 배로 늘려 깔아 두고
    /// 다 된 조각이 올 때마다(<see cref="PumpHiRes"/>) 그 자리만 갈아 끼운다.
    /// </summary>
    private void BuildMapTexture()
    {
        int lw = _scene.Width * T, lh = _scene.Height * T;
        _mapScale = _hires.Enabled ? HiRes.Scale : 1;
        _mapHi = _mapScale > 1 && !_cpuMap;
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

    private void Render()
    {
        Compose();
        DrawTitleBar();
        Upload();
        Draw();
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
        // map = (지도 텍스처 너비, 높이, 부드럽게 1/0, 0), bar = (제목 줄 높이, 0, 0, 0).
        // t0 = 사람 층(판 해상도), t1 = 지도, t2 = 제목 줄. 판은 제목 줄 밑에서 시작한다.
        // 셰이더 글에는 한글을 넣지 않는다 — 컴파일러가 바이트 수로 읽어 끝이 잘린다.
        const string shader = """
            Texture2D<float4> Sprites : register(t0);
            Texture2D<float4> Map : register(t1);
            Texture2D<float4> Bar : register(t2);
            SamplerState Lin : register(s0);
            cbuffer View : register(b0) { float4 view; float4 board; float4 map; float4 bar; };
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
                if (i.pos.y < bar.x) return float4(Bar.Load(int3(int2(i.pos.xy), 0)).rgb, 1);
                float2 p = i.pos.xy - float2(0, bar.x);
                float2 g = (p - view.yz) / view.x;
                if (map.w == 0 && (g.x < 0 || g.y < 0 || g.x >= board.x || g.y >= board.y)) return float4(0, 0, 0, 1);
                float2 m = (g + board.zw) * view.w;
                float4 c = float4(0, 0, 0, 1);
                if (map.w == 0 && m.x >= 0 && m.y >= 0 && m.x < map.x && m.y < map.y)
                    c = map.z > 0 ? Map.SampleLevel(Lin, m / map.xy, 0) : Map.Load(int3(int2(floor(m)), 0));
                float4 s = Sprites.Load(int3(int2(p), 0));
                return float4(lerp(c.rgb, s.rgb, s.a), 1);
            }
            """;
        var vsBlob = Compiler.Compile(shader, "VS", "board.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(shader, "PS", "board.hlsl", "ps_4_0");
        _vs = _device.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);
        _viewCb = _device.CreateBuffer(new BufferDescription(64, BindFlags.ConstantBuffer,
                                                             ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _linear = _device.CreateSamplerState(SamplerDescription.LinearClamp);
    }

    private void CreateSwapChain()
    {
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        var desc = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, _winW),
            Height = (uint)Math.Max(1, _winH),
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
        ResizeBar();
        Layout();
    }

    /// <summary>
    /// 창 크기가 바뀌면 백버퍼를 맞추고 배율·판 크기를 다시 셈한다. w x h 는 창 안쪽 전체 — 판은 그중 제목 줄 밑.
    /// </summary>
    private void Resize(int w, int h)
    {
        if (w <= 0 || h <= 0 || _swapChain == null) return;
        if (w == _winW && h == _winH && h - _barH == _clientH) return;
        if (w != _winW || h != _winH)
        {
            _winW = w;
            _winH = h;
            _ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!);
            _backBufferRtv.Dispose();
            _swapChain.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _backBufferRtv = _device.CreateRenderTargetView(back);
        }
        _clientW = w;
        _clientH = Math.Max(1, h - _barH);
        ResizeBar();
        Layout();
    }

    private void Upload()
    {
        var cb = _ctx.Map(_viewCb, 0, MapMode.WriteDiscard);
        var v = (float*)cb.DataPointer;
        v[0] = _zoom; v[1] = _offX; v[2] = _offY; v[3] = _mapScale;
        v[4] = _boardW; v[5] = _boardH; v[6] = _camX; v[7] = _camY;
        v[8] = _scene.Width * T * _mapScale; v[9] = _scene.Height * T * _mapScale; v[10] = _mapScale > 1 ? 1 : 0; v[11] = MapShown && !_cpuMap ? 0 : 1;
        v[12] = _barH; v[13] = v[14] = v[15] = 0;
        _ctx.Unmap(_viewCb, 0);
        UploadBar();

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
        _ctx.RSSetViewport(0, 0, _winW, _winH);
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetShaderResources(0, [_boardSrv, _mapSrv!, _barSrv!]);
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
        catch (IOException) { }   // 다른 프로그램이 last.bmp 를 열고 있으면 이번 것은 건너뛴다 — 게임은 계속

        finally { _ctx.Unmap(staging, 0); }
    }

    public void Dispose()
    {
        StopMusic();
        StopSfx();
        if (_active == this) _active = null;
        _backBufferRtv?.Dispose();
        _swapChain?.Dispose();
        _boardSrv?.Dispose();
        _boardTex?.Dispose();
        _mapSrv?.Dispose();
        _mapTex?.Dispose();
        _barSrv?.Dispose();
        _barTex?.Dispose();
        _linear?.Dispose();
        _viewCb?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
        if (_hwnd != IntPtr.Zero) { Win32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
