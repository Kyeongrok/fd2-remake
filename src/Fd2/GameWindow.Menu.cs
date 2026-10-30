using System.Diagnostics;
using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 창 위 메뉴 막대 — 장면 · 설정(해상도 · 배율 · 전체화면) · 도구(편집기).
/// </summary>
/// <remarks>
/// 해상도는 <b>창 안쪽 크기</b>, 배율은 <b>확대 그대로</b>다. 보이는 판 = 창 ÷ 배율이라
/// 해상도를 키우면 지도가 더 넓게 보이고, 배율을 키우면 크게 보인다. 고른 값은 <see cref="Settings"/> 에 저장한다.
/// </remarks>
internal sealed partial class GameWindow
{

    private const int MenuScenePrev = 1001, MenuSceneNext = 1002, MenuSceneBase = 1100;
    private const int MenuResBase = 1300, MenuZoomBase = 1400, MenuFullscreen = 1500, MenuResetView = 1501, MenuKeys = 1502;
    private const int MenuEditor = 1600, MenuExit = 1601, MenuHiRes = 1700, MenuShowRange = 1701;
    private const int MenuBattle = 1003, MenuBattleStop = 1004, MenuVersions = 1005;

    private static readonly (int W, int H)[] ResChoices =
        [(640, 480), (800, 600), (960, 624), (1024, 768), (1280, 720), (1280, 960), (1440, 936), (1600, 900), (1920, 1080), (2560, 1440)];

    /// <summary>0 = 자동.</summary>
    private static readonly int[] ZoomChoices = [0, 1, 2, 3, 4, 5, 6];


    private IntPtr _menuBar;


    private IntPtr CreateMenuBar()
    {
        IntPtr bar = Win32.CreateMenu();

        IntPtr game = Win32.CreatePopupMenu();
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuScenePrev, "이전 장면(&P)\tPgUp");
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuSceneNext, "다음 장면(&N)\tPgDn");
        IntPtr pick = Win32.CreatePopupMenu();
        for (int i = 0; i < _sceneCount; i++)
            Win32.AppendMenuW(pick, Win32.MF_STRING, (nuint)(MenuSceneBase + i), $"장면 {i + 1}");
        Win32.AppendMenuW(game, Win32.MF_POPUP, (nuint)pick, "장면 고르기(&S)");
        Win32.AppendMenuW(game, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuBattle, "1장 전투 시작(&B)");
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuBattleStop, "전투 그만두기(&Q)");
        Win32.AppendMenuW(game, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuVersions, "판 고르기(&V)...");
        Win32.AppendMenuW(game, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuExit, "끝내기(&X)\tEsc");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)game, "게임(&G)");

        IntPtr settings = Win32.CreatePopupMenu();
        IntPtr res = Win32.CreatePopupMenu();
        for (int i = 0; i < ResChoices.Length; i++)
            Win32.AppendMenuW(res, Win32.MF_STRING, (nuint)(MenuResBase + i), $"{ResChoices[i].W} × {ResChoices[i].H}");
        Win32.AppendMenuW(settings, Win32.MF_POPUP, (nuint)res, "해상도(창 크기)(&R)");
        IntPtr zoom = Win32.CreatePopupMenu();
        foreach (int z in ZoomChoices)
            Win32.AppendMenuW(zoom, Win32.MF_STRING, (nuint)(MenuZoomBase + z), z == 0 ? "자동(창을 채움)" : $"{z * 100}%");
        Win32.AppendMenuW(settings, Win32.MF_POPUP, (nuint)zoom, "배율(&Z)");
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuFullscreen, "전체화면(&F)\tF11");
        Win32.AppendMenuW(settings, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuResetView, "화면 처음 값으로(&D)");
        Win32.AppendMenuW(settings, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuKeys, "단축키 · 패드 설정(&K)...");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)settings, "설정(&S)");

        IntPtr mode = Win32.CreatePopupMenu();
        Win32.AppendMenuW(mode, Win32.MF_STRING, MenuHiRes, "고해상도(4배, 이 PC 에서 차례로 만듦)(&H)");
        Win32.AppendMenuW(mode, Win32.MF_STRING, MenuShowRange, "이동 · 공격 범위 보기(&R)");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)mode, "모드(&M)");

        IntPtr tools = Win32.CreatePopupMenu();
        Win32.AppendMenuW(tools, Win32.MF_STRING, MenuEditor, "편집기 열기(&E)...");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)tools, "도구(&T)");
        return bar;
    }

    private void OnMenuCommand(int id)
    {
        switch (id)
        {
            case MenuBattle: StartBattle(0); break;
            case MenuBattleStop: if (InBattle) StopBattle(); break;
            case MenuShowRange:
                _settings.ShowRange = !_settings.ShowRange;
                _settings.Save();
                break;
            case MenuScenePrev: LoadScene((_scene.Index + _sceneCount - 1) % _sceneCount); break;
            case MenuSceneNext: LoadScene((_scene.Index + 1) % _sceneCount); break;
            case >= MenuSceneBase and < MenuSceneBase + 200 when id - MenuSceneBase < _sceneCount:
                LoadScene(id - MenuSceneBase);
                break;
            case >= MenuResBase and < MenuResBase + 20 when id - MenuResBase < ResChoices.Length:
                (_settings.ViewW, _settings.ViewH) = ResChoices[id - MenuResBase];
                _settings.Save();
                ApplyWindow();
                break;
            case >= MenuZoomBase and < MenuZoomBase + 20:
                _settings.Zoom = id - MenuZoomBase;
                _settings.Save();
                Layout();
                break;
            case MenuFullscreen:
                _settings.Fullscreen = !_settings.Fullscreen;
                _settings.Save();
                ApplyWindow();
                break;
            case MenuResetView:
            {
                var first = new Settings();
                (_settings.ViewW, _settings.ViewH, _settings.Zoom, _settings.Fullscreen) = (first.ViewW, first.ViewH, first.Zoom, first.Fullscreen);
                _settings.Save();
                ApplyWindow();
                Layout();
                break;
            }
            case MenuHiRes:
                _settings.HiRes = _hires.Enabled = !_hires.Enabled;
                _settings.Save();
                BuildMapTexture();
                if (_hires.Enabled) _hires.Want(_scene.Index, _scene.Units.Select(u => u.Icon));
                break;
            case MenuKeys:
                _keysOpen = true;
                _keysRow = 0;
                _keysCapture = -1;
                break;
            case MenuVersions: PickVersion(); break;
            case MenuEditor: OpenEditor(); break;
            case MenuExit: _running = false; break;
        }
        UpdateMenuChecks();
    }

    partial void UpdateMenuChecks()
    {
        if (_menuBar == IntPtr.Zero) return;
        void check(int id, bool on) =>
            Win32.CheckMenuItem(_menuBar, (uint)id, Win32.MF_BYCOMMAND | (on ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
        for (int i = 0; i < ResChoices.Length; i++)
            check(MenuResBase + i, ResChoices[i] == (_settings.ViewW, _settings.ViewH));
        foreach (int z in ZoomChoices) check(MenuZoomBase + z, z == _settings.Zoom);
        check(MenuFullscreen, _settings.Fullscreen);
        check(MenuHiRes, _settings.HiRes);
        check(MenuShowRange, _settings.ShowRange);
        if (_scene != null)
            for (int i = 0; i < _sceneCount; i++) check(MenuSceneBase + i, i == _scene.Index);
    }

    /// <summary>창 꼴을 설정대로 — 전체화면이면 테두리 · 메뉴 없이 모니터 크기, 아니면 해상도만큼의 안쪽을 가진 창.</summary>
    private void ApplyWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_settings.Fullscreen && !Offscreen)
        {
            Win32.SetMenu(_hwnd, IntPtr.Zero);
            Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, Win32.WS_POPUP | Win32.WS_VISIBLE);
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0,
                               Win32.GetSystemMetrics(Win32.SM_CXSCREEN), Win32.GetSystemMetrics(Win32.SM_CYSCREEN),
                               Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED);
            return;
        }

        // 모니터 작업 영역보다 크면 들어가는 데까지만 — 제목 표시줄 · 메뉴 · 테두리 몫을 남긴다.
        int w = _settings.ViewW, h = _settings.ViewH;
        var work = new Win32.Rect();
        if (Win32.SystemParametersInfoW(Win32.SPI_GETWORKAREA, 0, ref work, 0))
        {
            w = Math.Min(w, work.Width - 32);
            h = Math.Min(h, work.Height - 100);
        }
        var rect = new Win32.Rect { Left = 0, Top = 0, Right = w, Bottom = h };
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, true);
        Win32.SetMenu(_hwnd, _menuBar);
        Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, Win32.WS_OVERLAPPEDWINDOW | Win32.WS_VISIBLE);
        if (Offscreen) { w = _settings.ViewW; h = _settings.ViewH; rect = new Win32.Rect { Right = w, Bottom = h }; Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, true); }
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, Offscreen ? -8000 : Math.Max(work.Left, work.Left + (work.Width - rect.Width) / 2),
                           Offscreen ? 0 : Math.Max(work.Top, work.Top + (work.Height - rect.Height) / 2),
                           rect.Width, rect.Height, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | (Offscreen ? Win32.SWP_NOACTIVATE : 0));
    }

    private string _titleStatus = "";

    /// <summary>고해상도를 만드는 동안 제목줄 끝에 지금 상태를 붙인다(바뀔 때만 고친다).</summary>
    private void UpdateTitleStatus()
    {
        string st = _hires.Enabled ? _hires.Status : "";
        if (st == _titleStatus || _scene == null) return;
        _titleStatus = st;
        Win32.SetWindowTextW(_hwnd, $"용의기사2 — 장면 {_scene.Index + 1}/{_sceneCount}" + (st.Length > 0 ? $"  · {st}" : "  (F11 전체화면)"));
    }

    /// <summary>
    /// 게임 > 판 고르기 — 이 PC 의 용의기사2 판들(바탕화면 · 다운로드 · 문서)을 찾아 띄우고, 고르면 그 판으로 다시 켠다.
    /// </summary>
    private void PickVersion()
    {
        var versions = GameVersions.Discover(_settings.GameDir.Length > 0 ? [_settings.GameDir] : null);
        IntPtr popup = Win32.CreatePopupMenu();
        for (int i = 0; i < versions.Count; i++)
        {
            bool now = string.Equals(Path.GetFullPath(versions[i].Dir), Path.GetFullPath(_dir), StringComparison.OrdinalIgnoreCase);
            Win32.AppendMenuW(popup, Win32.MF_STRING | (now ? Win32.MF_CHECKED : 0), (nuint)(i + 1), $"{versions[i].Name}   —   {versions[i].Dir}");
        }
        if (versions.Count == 0) Win32.AppendMenuW(popup, Win32.MF_STRING | Win32.MF_GRAYED, 0, "(찾은 판이 없습니다)");
        Win32.GetCursorPos(out var at);
        int pick = Win32.TrackPopupMenuEx(popup, Win32.TPM_RETURNCMD, at.X, at.Y, _hwnd, IntPtr.Zero);
        Win32.DestroyMenu(popup);
        if (pick <= 0) return;
        _settings.GameDir = versions[pick - 1].Dir;
        _settings.Save();
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { versions[pick - 1].Dir }, UseShellExecute = false });
        _running = false;
    }

    /// <summary>타이틀 메뉴에 「편집기 열기」를 보이나 — 윈도우는 편집기가 함께 온다.</summary>
    private const bool HasEditor = true;

    /// <summary>안내 글을 터치 기준으로 쓰나 — 윈도우는 키보드 · 마우스.</summary>
    private const bool TouchUi = false;

    /// <summary>도구 > 편집기 열기 — 같은 폴더의 Fd2.Editor.exe 를 게임 폴더를 주어 띄운다.</summary>
    private partial void OpenEditor()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Fd2.Editor.exe");
        if (!File.Exists(exe))
        {
            Win32.MessageBoxW(_hwnd, $"편집기({exe})가 없습니다.\n릴리즈에서 Fd2.Editor.exe 를 받아 Fd2.exe 옆에 두세요.", "FD2", 0x30);
            return;
        }
        Process.Start(new ProcessStartInfo(exe) { ArgumentList = { _dir }, UseShellExecute = false });
    }
}
