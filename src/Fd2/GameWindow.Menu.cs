using System.Diagnostics;
using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 메뉴 — 게임 · 설정(해상도 · 배율 · 전체화면) · 모드 · 도구(편집기). 제목 줄 햄버거가 내린다(GameWindow.TitleBar.cs).
/// </summary>
/// <remarks>
/// 해상도는 <b>창 안쪽 크기</b>, 배율은 <b>확대 그대로</b>다. 보이는 판 = 창 ÷ 배율이라
/// 해상도를 키우면 지도가 더 넓게 보이고, 배율을 키우면 크게 보인다. 고른 값은 <see cref="Settings"/> 에 저장한다.
/// </remarks>
internal sealed partial class GameWindow
{

    private const int MenuScenePrev = 1001, MenuSceneNext = 1002, MenuSceneBase = 1100;
    private const int MenuResBase = 1300, MenuZoomBase = 1400, MenuFullscreen = 1500, MenuResetView = 1501, MenuKeys = 1502, MenuVolume = 1503;
    private const int MenuEditor = 1600, MenuExit = 1601, MenuHiRes = 1700, MenuShowRange = 1701, MenuMusic = 1702, MenuSound = 1703, MenuShowHelp = 1704, MenuShowGoals = 1705, MenuControlAllies = 1706, MenuMobileView = 1707, MenuBugReport = 1708, MenuSettingsDialog = 1709, MenuModeDialog = 1710;
    private const int MenuBattle = 1003, MenuBattleStop = 1004, MenuVersions = 1005;

    private static readonly (int W, int H)[] ResChoices =
        [(640, 480), (800, 600), (960, 624), (1024, 768), (1280, 720), (1280, 960), (1440, 936), (1600, 900), (1920, 1080), (2560, 1440)];

    /// <summary>0 = 자동.</summary>
    private static readonly int[] ZoomChoices = [0, 1, 2, 3, 4, 5, 6];


    /// <summary>햄버거가 내리는 메뉴 — 창에 막대로 달지 않고 팝업으로 띄운다.</summary>
    private IntPtr _menuBar;


    private IntPtr CreateMenuBar()
    {
        IntPtr bar = Win32.CreatePopupMenu();

        // 맨 위는 버전 고르기, 맨 아래는 끝내기 — 장면 넘기기 · 1장 전투 시작 · 전투 그만두기는 뺐다(사용자 요청)
        Win32.AppendMenuW(bar, Win32.MF_STRING, MenuVersions, "버전 고르기(&V)...");
        Win32.AppendMenuW(bar, Win32.MF_SEPARATOR, 0, null);

        // 설정 · 모드는 창 하나로(사용자 요청 — 창세기전3 · 대항해시대3 처럼). GameWindow.SettingsDialog.cs
        Win32.AppendMenuW(bar, Win32.MF_STRING, MenuSettingsDialog, "설정(&S)...");
        Win32.AppendMenuW(bar, Win32.MF_STRING, MenuModeDialog, "모드(&M)...");

        IntPtr tools = Win32.CreatePopupMenu();
        Win32.AppendMenuW(tools, Win32.MF_STRING, MenuEditor, "편집기 열기(&E)...");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)tools, "도구(&T)");
        Win32.AppendMenuW(bar, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(bar, Win32.MF_STRING, MenuBugReport, "버그 리포트(&B)...");
        Win32.AppendMenuW(bar, Win32.MF_STRING, MenuExit, "끝내기(&X)");
        return bar;
    }

    private void OnMenuCommand(int id)
    {
        switch (id)
        {
            case MenuBattle: NewCampaign(); PlayCampaign(); break;
            case MenuBattleStop: if (InBattle) StopBattle(); break;
            case MenuShowRange:
                _settings.ShowRange = !_settings.ShowRange;
                _settings.Save();
                break;
            case MenuShowHelp:
                _settings.ShowHelp = !_settings.ShowHelp;
                _settings.Save();
                break;
            case MenuShowGoals:
                _settings.ShowGoals = !_settings.ShowGoals;
                _settings.Save();
                break;
            case MenuMobileView:
                _settings.MobileView = !_settings.MobileView;
                _settings.Save();
                if (_bs == BattleState.Menu && _stage == MenuStage.Mobile) { _menu.Clear(); _bs = BattleState.Select; }   // 열린 모바일 메뉴는 닫는다
                Layout();
                break;
            case MenuControlAllies:
                _settings.ControlAllies = !_settings.ControlAllies;
                _settings.Save();
                break;
            case MenuMusic:
                _settings.Music = !_settings.Music;
                _settings.Save();
                if (_settings.Music) PlayMusic(_musicWanted);
                else StopMusic();
                break;
            case MenuSound:
                _settings.Sound = !_settings.Sound;
                _settings.Save();
                if (!_settings.Sound) StopSfx();
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
            case MenuVolume: OpenVolume(); break;
            case MenuBugReport: OpenBugReport(); break;
            case MenuSettingsDialog: OpenSettingsDialog(); break;
            case MenuModeDialog: OpenModeDialog(); break;
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
        check(MenuShowHelp, _settings.ShowHelp);
        check(MenuShowGoals, _settings.ShowGoals);
        check(MenuMobileView, _settings.MobileView);
        check(MenuControlAllies, _settings.ControlAllies);
        check(MenuMusic, _settings.Music);
        check(MenuSound, _settings.Sound);
        if (_scene != null)
            for (int i = 0; i < _sceneCount; i++) check(MenuSceneBase + i, i == _scene.Index);
    }

    /// <summary>
    /// 창 꼴을 설정대로 — 전체화면이면 테두리 · 제목 줄 없이 모니터 크기, 아니면 제목 줄 밑에 해상도만큼의 판을 가진 창.
    /// 창은 캡션 · 테두리를 그리지 않으므로(WM_NCCALCSIZE) 창 크기 = 안쪽 크기 = 제목 줄 + 판.
    /// </summary>
    private void ApplyWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (Win32.IsZoomed(_hwnd)) Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
        UpdateBarMetrics();
        if (_settings.Fullscreen && !Offscreen)
        {
            Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, Win32.WS_POPUP | Win32.WS_VISIBLE);
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0,
                               Win32.GetSystemMetrics(Win32.SM_CXSCREEN), Win32.GetSystemMetrics(Win32.SM_CYSCREEN),
                               Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED);
        }
        else
        {
            // 모니터 작업 영역보다 크면 들어가는 데까지만 — 제목 줄 몫을 남긴다.
            int w = _settings.ViewW, h = _settings.ViewH;
            var work = new Win32.Rect();
            if (Win32.SystemParametersInfoW(Win32.SPI_GETWORKAREA, 0, ref work, 0) && !Offscreen)
            {
                w = Math.Min(w, work.Width);
                h = Math.Min(h, work.Height - _barH);
            }
            h += _barH;
            Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, Win32.WS_OWNTITLE | Win32.WS_VISIBLE);
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, Offscreen ? -8000 : Math.Max(work.Left, work.Left + (work.Width - w) / 2),
                               Offscreen ? 0 : Math.Max(work.Top, work.Top + (work.Height - h) / 2),
                               w, h, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | (Offscreen ? Win32.SWP_NOACTIVATE : 0));
        }
        // 크기가 그대로여도 제목 줄이 생기거나 없어졌으면 판을 다시 잡는다
        if (Win32.GetClientRect(_hwnd, out var client)) Resize(client.Width, client.Height);
    }

    private string _titleStatus = "";

    /// <summary>제목 줄 — 「용의기사2 — 지금 버전 이름」, 고해상도를 만드는 동안은 끝에 지금 상태를 붙인다(바뀔 때만 고친다).</summary>
    private void UpdateTitleStatus()
    {
        string st = _hires.Enabled ? _hires.Status : "";
        string text = $"용의기사2 — {VersionName}" + (st.Length > 0 ? $"  · {st}" : "");
        if (text == _titleStatus) return;
        _titleStatus = text;
        SetCaption(text);
    }

    /// <summary>버전 이름을 화면 왼쪽 위에도 그리나 — 윈도우는 제목 줄에 있으니, 제목 줄이 없는 전체화면일 때만.</summary>
    private bool VersionOnScreen => _barH <= 0;

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
        if (versions.Count == 0) Win32.AppendMenuW(popup, Win32.MF_STRING | Win32.MF_GRAYED, 0, "(찾은 버전이 없습니다)");
        Win32.GetCursorPos(out var at);
        int pick = Win32.TrackPopupMenuEx(popup, Win32.TPM_RETURNCMD, at.X, at.Y, _hwnd, IntPtr.Zero);
        Win32.DestroyMenu(popup);
        if (pick <= 0) return;
        SwitchVersion(versions[pick - 1].Dir);
    }

    private partial List<(string Name, string Dir)> FindVersions() =>
        GameVersions.Discover(_settings.GameDir.Length > 0 ? [_settings.GameDir, _dir] : [_dir]).Select(v => (v.Name, v.Dir)).ToList();

    /// <summary>판 바꾸기 — 고른 판을 설정에 적고(다음에 켤 때도 그 판), 그 판으로 다시 켠다.</summary>
    private partial void SwitchVersion(string dir)
    {
        _settings.GameDir = dir;
        _settings.Save();
        if (!Offscreen) Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { dir }, UseShellExecute = false });
        _running = false;
    }

    private partial bool CanAddVersion() => false;

    private partial void AddVersion() { }

    /// <summary>타이틀 메뉴에 「편집기 열기」를 보이나 — 윈도우는 편집기가 함께 온다.</summary>
    private static readonly bool HasEditor = true;

    /// <summary>안내 글 · 화면을 터치 기준으로 쓰나 — 윈도우는 키보드 · 마우스, 설정 「모바일 화면으로 보기」를 켜면 폰과 같은 화면.</summary>
    private bool TouchUi => _settings.MobileView;

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
