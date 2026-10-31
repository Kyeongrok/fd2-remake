using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 게임 로직의 공용 몸통 — 윈도우(GameWindow.cs · Win32 + D3D11)와 안드로이드(Fd2.Android)가 함께 쓴다.
/// </summary>
/// <remarks>
/// 화면은 모두 <c>_fb</c>(BGRA, <c>_fbW</c> x <c>_fbH</c>)에 찍는다. 윈도우는 지도를 GPU 텍스처로 따로 올리고 사람 층만 여기에,
/// 안드로이드는 <c>_cpuMap</c> 을 켜 지도까지 여기에 찍는다. 플랫폼마다 다른 일은 아래 갈고리(partial 메서드)로 맡긴다.
/// </remarks>
internal sealed partial class GameWindow
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

    /// <summary>원작 규칙 표(FD2.EXE) — 없으면 null(전투는 기본값으로 돈다).</summary>
    private readonly GameTables? _tables;
    private readonly int _sceneCount;
    private Scene _scene = null!;
    private Unit? _selected;
    private double _time;
    private int _camX, _camY;

    private readonly HashSet<int> _keys = new();
    private bool _running;

    /// <summary>지도까지 CPU 로 <c>_fb</c> 에 찍나 — 안드로이드는 켠다(윈도우는 GPU 텍스처).</summary>
    private bool _cpuMap;

    private readonly Settings _settings = Settings.Load();

    /// <summary>배율 「자동」이 맞추는 원본 화면 크기 — 이것이 창에 다 들어가는 가장 큰 정수 배율.</summary>
    private const int BaseW = 480, BaseH = 312;

    private int _clientW = 1920, _clientH = 1080;
    private int _zoom = 2, _offX, _offY;

    /// <summary>
    /// 화면 밖 시험(창세기전3 의 DUELDX_OFFSCREEN 과 같은 것) — FD2_OFFSCREEN=1 이면 창을 화면 밖에 포커스 없이 띄우고,
    /// 전체화면 · 패드를 끄고, 설정 파일을 저장하지 않는다. 사용하는 사람의 화면 · 마우스와 섞이지 않게 시험을 돌릴 때.
    /// </summary>
    internal static readonly bool Offscreen = Environment.GetEnvironmentVariable("FD2_OFFSCREEN") == "1";

    public GameWindow(string dir)
    {
        _dir = dir;
        _pal = GameFolder.Palette(dir);
        _icons = new IconBank(Path.Combine(dir, "FDICON.B24"), _pal);
        _sceneCount = Dat.Count(Path.Combine(dir, "FDFIELD.DAT")) / 3;
        TablesJson.EnsureExported(dir);   // 수치 표는 게임 폴더 tables/*.json 을 쓴다 — 처음이면 EXE 에서 뽑아 둔다
        _tables = GameTables.Load(dir);
        PlatformInit();
    }

    // ── 갈고리 — 플랫폼 파일이 채운다 ────────────────────────────────────────

    partial void PlatformInit();
    partial void PlatformTick();
    partial void OnSceneLoaded();
    partial void OnFbResized();
    partial void ToggleFullscreen();
    partial void RequestShot();
    partial void UpdateMenuChecks();

    /// <summary>배경음악 — 곡 번호(FDMUS.DAT)를 되풀이해 튼다. 같은 곡이 이미 돌면 그대로.</summary>
    partial void PlayMusic(int index);
    partial void StopMusic();

    /// <summary>효과음 — FDOTHER 소리 묶음 entry 의 index 번(<see cref="Sound"/>). 안드로이드는 아직 소리를 내지 않는다.</summary>
    partial void PlaySfx(int entry, int index);

    /// <summary>둘째 소리 갈래(원작 sub_25B45) — 첫 갈래(<see cref="PlaySfx"/>)를 끊지 않고 겹쳐 난다.</summary>
    partial void PlaySfx2(int entry, int index);
    partial void StopSfx();

    /// <summary>음악이 끝났으면 처음부터 다시(윈도우 MCI 는 스스로 되풀이하지 않는다).</summary>
    partial void MusicTick();

    /// <summary>고해상도 캐릭터 그림(96x96), 없으면 null — 안드로이드는 늘 null.</summary>
    private partial uint[]? HiIcon(int icon, int frame);

    /// <summary>편집기 띄우기 — 안드로이드는 없다(타이틀 메뉴에서 뺀다).</summary>
    private partial void OpenEditor();

    // ── 플랫폼이 부르는 입구 ─────────────────────────────────────────────────

    internal bool Running => _running;
    internal uint[] FrameBuffer => _fb;
    internal int FrameWidth => _fbW;
    internal int FrameHeight => _fbH;

    /// <summary>켤 때 버전 고르기부터 띄우나 — 윈도우에서 손으로 켰을 때(버전을 골라 다시 켠 것은 아니다).</summary>
    internal bool PickVersionFirst { get; init; }

    /// <summary>처음 — 1장 지도를 깔고 오프닝부터. 버전이 둘 넘으면 그 앞에 버전 고르기(그대로 두면 이 버전으로 오프닝).</summary>
    internal void Start()
    {
        _running = true;
        LoadScene(0);
        if (PickVersionFirst && !Offscreen && OpenVersions(StartOpening, min: 2)) return;
        StartOpening();
    }

    /// <summary>창(안드로이드는 그릴 판) 크기가 바뀌었다.</summary>
    internal void SetClientSize(int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        _clientW = w;
        _clientH = h;
        Layout();
    }

    internal void Step(double dt)
    {
        Tick(dt);
        Compose();
    }

    internal void KeyDown(int vk) => OnKeyDown(vk);
    internal void KeyUp(int vk) => _keys.Remove(vk);

    internal void PointerMove(int x, int y)
    {
        // 윈도우는 마우스가 그대로여도 이동 알림을 보낼 때가 있다 — 그때 커서를 숨겼다 보였다 하지 않게
        if ((x, y) == (_mouseX, _mouseY)) return;
        (_mouseX, _mouseY) = (x, y);
        _mouseMoved = true;
        if (PartyMove(x, y) || VolumeMove(x, y) || MenuMove(y)) return;
        if (DragMap(x, y)) return;
        if (_versionsOpen) VersionsHover(_mouseX, _mouseY);
        else if (_screen == Screen.Title && !_keysOpen) TitleHover(_mouseX, _mouseY);
        MouseToCursor();
    }

    internal void PointerDown(int x, int y)
    {
        (_mouseX, _mouseY) = (x, y);
        _mouseMoved = true;
        if (_keysOpen || _helpOpen) { KeysWindowKey(Win32.VK_ESCAPE); return; }
        if (VersionsClick(x, y)) return;
        if (_screen == Screen.Prologue) { PrologueNext(); return; }
        if (_screen == Screen.Ending) { EndingNext(); return; }
        if (_screen != Screen.Game) { TitleClick(_mouseX, _mouseY); return; }
        if (Talking) { TalkNext(); return; }
        if (InBattle && HudToggleClick(x, y)) return;
        MouseToCursor();
        OnClick((int)Math.Floor((_mouseX - _offX) / (double)_zoom), (int)Math.Floor((_mouseY - _offY) / (double)_zoom));
    }

    // ── 지도 끌기(창세기전3 처럼) — 우리 차례 전투에서 누른 채 끌면 지도가 따라 움직이고, 안 끌고 떼면 그 자리를 누른 것 ──

    private (int X, int Y)? _press;
    private (int X, int Y) _pressCam;
    private bool _dragging;

    /// <summary>이 몇 점 넘게 움직여야 끌기 — 손떨림 · 살짝 미끄러진 누르기는 누르기로.</summary>
    private const int DragSlop = 8;

    /// <summary>지도를 끌 수 있는 때 — 전투 우리 차례에 대사 · 컷인 · 메뉴 창이 없을 때.</summary>
    private bool CanDragMap => _screen == Screen.Game && InBattle && !_enemyPhase && !Talking && !_versionsOpen && !_keysOpen && !_helpOpen
                               && _bs is (BattleState.Select or BattleState.Target)
                               && (_scene.Width * T > _boardW || _scene.Height * T > _boardH);

    /// <summary>
    /// 왼쪽 단추(손가락)를 눌렀다 — 지도를 끌 수 있으면 뗄 때까지 기다리고, 아니면 바로 누르기.
    /// defer 면 늘 뗄 때 누른다(안드로이드 — 손가락은 뗄 때 누르기이고 길게 누르기는 물리기다).
    /// </summary>
    internal void PointerPress(int x, int y, bool defer = false)
    {
        if (PartyPress(x, y) || VolumePress(x, y) || MenuPress(x, y)) return;
        if (!defer && (!CanDragMap || InBattle && HudHit(x, y))) { PointerDown(x, y); return; }
        (_press, _pressCam, _dragging) = ((x, y), (_camX, _camY), false);
    }

    /// <summary>누르기를 버린다(안드로이드 길게 누르기로 물렸을 때).</summary>
    internal void PointerAbort() => (_press, _dragging, _partyDrag, _volumeDrag, _menuPress, _menuDragging) = (null, false, null, -1, null, false);

    /// <summary>뗐다 — 끌지 않았으면 그 자리를 누른 것으로.</summary>
    internal void PointerRelease(int x, int y)
    {
        if (PartyRelease() || VolumeRelease()) { _press = null; return; }
        if (MenuRelease(x, y)) return;
        if (_press == null) return;
        bool dragged = _dragging;
        (_press, _dragging) = (null, false);
        if (!dragged) PointerDown(x, y);
    }

    /// <summary>누른 채 움직이면 지도를 민다. 끄는 중이면 true(커서는 따라가지 않는다).</summary>
    private bool DragMap(int x, int y)
    {
        if (_press is not { } p || !CanDragMap) return false;
        if (!_dragging && Math.Abs(x - p.X) + Math.Abs(y - p.Y) < DragSlop) return false;
        _dragging = true;
        int mapW = _scene.Width * T, mapH = _scene.Height * T;
        if (mapW > _boardW) _camX = Math.Clamp(_pressCam.X - (x - p.X) / Math.Max(1, _zoom), 0, mapW - _boardW);
        if (mapH > _boardH) _camY = Math.Clamp(_pressCam.Y - (y - p.Y) / Math.Max(1, _zoom), 0, mapH - _boardH);
        return true;
    }

    /// <summary>오른쪽 단추(안드로이드는 길게 누르기) — 전투에서 한 단계 물리기.</summary>
    internal void PointerCancel()
    {
        if (_partyOpen) { CloseParty(); return; }
        if (_volumeOpen) { CloseVolume(); return; }
        if (InBattle) BattleCancel();
    }

    /// <summary>글 · 창(HUD) 크기를 정하는 배율 — 지도 배율과 따로. 폰은 두 손가락으로 지도만 키우고 글은 그대로 둔다.</summary>
    private int HudZoom => TouchUi ? Math.Max(3, _clientH / 180) : _zoom;   // 폰은 메뉴 · 글을 크게 — 화면 높이를 따라(1080 줄 폰이면 6배, 손가락으로 누르기 쉽게, 사용자 요청)

    private const int MinTouchZoom = 1, MaxTouchZoom = 4;

    /// <summary>
    /// 두 손가락 벌리기(step +1) · 오므리기(−1) — 지도 배율을 한 단계 바꾸고, 보던 곳 가운데를 그대로 둔다.
    /// </summary>
    internal void PinchZoom(int step)
    {
        int z = Math.Clamp(_zoom + step, MinTouchZoom, MaxTouchZoom);
        if (!TouchUi || z == _zoom) return;
        PointerAbort();
        int cx = _camX + _boardW / 2, cy = _camY + _boardH / 2;
        _settings.TouchZoom = z;
        _settings.Save();
        Layout();
        if (_scene == null) return;
        int mw = _scene.Width * T, mh = _scene.Height * T;
        _camX = mw <= _boardW ? (mw - _boardW) / 2 : Math.Clamp(cx - _boardW / 2, 0, mw - _boardW);
        _camY = mh <= _boardH ? (mh - _boardH) / 2 : Math.Clamp(cy - _boardH / 2, 0, mh - _boardH);
    }

    /// <summary>배율과 보이는 판 크기를 창 크기에서 셈한다. 판이 바뀌면 화면 버퍼와 텍스처를 다시 만든다.</summary>
    private void Layout()
    {
        int auto = Math.Max(1, Math.Min(_clientW / BaseW, _clientH / BaseH));
        _zoom = _settings.Zoom > 0 ? _settings.Zoom : auto;
        // 폰은 화면이 낮아 1배면 지도가 작다 — 기본 2배로 키워 사람 둘레를 보이고 카메라가 따라간다. 두 손가락으로 1~4배(PinchZoom)
        if (TouchUi) _zoom = Math.Clamp(_settings.TouchZoom, MinTouchZoom, MaxTouchZoom);
        int bw = Math.Max(1, _clientW / _zoom), bh = Math.Max(1, _clientH / _zoom);
        _offX = (_clientW - bw * _zoom) / 2;
        _offY = (_clientH - bh * _zoom) / 2;
        _boardW = bw;
        _boardH = bh;
        if (_fbW != _clientW || _fbH != _clientH)
        {
            _fbW = Math.Max(1, _clientW);
            _fbH = Math.Max(1, _clientH);
            _fb = new uint[_fbW * _fbH];
            OnFbResized();
        }
        if (_scene != null) Follow();
    }

    /// <summary>이번 키가 누른 채로 들어온 되풀이인가 — 대사는 되풀이로 넘기지 않는다(꾹 누르면 대사가 다 지나가므로).</summary>
    private bool _keyRepeat;

    private void OnKeyDown(int vk)
    {
        _keyRepeat = !_keys.Add(vk);
        if (vk == Win32.VK_F12) { RequestShot(); return; }
        if (vk == Win32.VK_F11) { ToggleFullscreen(); return; }
        if (KeysWindowKey(vk)) return;
        if (PartyKey(vk) || VolumeKey(vk)) return;
        if (VersionsKey(vk)) return;
        if (EndingKey(vk)) return;
        if (_screen == Screen.Prologue)
        {
            if (TalkAdvanceKey(vk)) PrologueNext();                        // Enter · Space · 방향키 · WASD 다음
            else if (vk == Win32.VK_ESCAPE) SkipPrologue();                // Esc 프롤로그 건너뛰기
            return;
        }
        if (_screen != Screen.Game)
        {
            // 패드 · 바꾼 키로도 메뉴를 고를 수 있게 커서 키는 방향키로
            vk = ActionOf(vk) switch { KeyAction.MoveUp => Win32.VK_UP, KeyAction.MoveDown => Win32.VK_DOWN, _ => vk };
            TitleKey(vk);
            return;
        }
        if (TownSecretKey(vk)) return;   // 마을 비밀상점 열쇠(Shift · Ctrl · Alt + F) — 도움말 같은 단축키보다 먼저

        // 바꿀 수 있는 키 — 커서 키는 방향키로 바꿔 넘기고, 목록 · 체력바는 어디서나
        switch (ActionOf(vk))
        {
            case KeyAction.MoveUp: vk = Win32.VK_UP; break;
            case KeyAction.MoveDown: vk = Win32.VK_DOWN; break;
            case KeyAction.MoveLeft: vk = Win32.VK_LEFT; break;
            case KeyAction.MoveRight: vk = Win32.VK_RIGHT; break;
            case KeyAction.KeyHelp: _helpOpen = true; return;
            case KeyAction.Gauges: _showGauges = !_showGauges; return;
            case KeyAction.Minimap: _settings.ShowMinimap = !_settings.ShowMinimap; _settings.Save(); return;
            case KeyAction.Chests: _settings.ShowChests = !_settings.ShowChests; _settings.Save(); return;
            case KeyAction.Party: OpenParty(); return;
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

    /// <param name="party">이야기의 동료 표 — 있으면 그 사람들을 세우고 세이브 값으로 덮는다(<see cref="HeroRecord.Apply"/>).</param>
    private void LoadScene(int index, IReadOnlyList<byte[]>? party = null)
    {
        _bs = BattleState.None;
        ClearSelection();
        MotionFiles.Reload();   // 편집기에서 캐릭터 모션 버전을 바꿨으면 장마다 새로
        _motions.Clear();
        _stands.Clear();
        _scene = new Scene(_dir, index, _pal, _icons, party);
        _backdrop = Backdrop.For(_dir, index, _pal);
        _battle = new Battle(_scene, _tables);
        foreach (var u in _scene.Units)
        {
            _battle.Setup(u);
            if (u.Record == null) continue;
            HeroRecord.Apply(u.Record, u);
            _battle.Recompute(u);
        }
        _freeReachFor = default;
        _selected = _scene.Units.Find(u => u.Side == Side.Player);
        _keys.Clear();
        OnSceneLoaded();
        Follow();
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
        PlatformTick();
        MusicTick();
        if (_screen == Screen.Prologue) { PrologueTick(dt); return; }
        if (_screen == Screen.Ending) { EndingTick(dt); return; }
        if (_screen != Screen.Game)
        {
            TitleTick(dt);
            return;
        }

        if (InBattle) BattleTick(dt);
        else if (!_partyOpen && !_volumeOpen && _selected is { Moving: false } sel && sel.Path.Count == 0)
        {
            bool Held(int arrow, KeyAction action) => _keys.Contains(arrow) || _keys.Contains(_keyMap[action]);
            if (Held(Win32.VK_UP, KeyAction.MoveUp)) TryStep(sel, 0, -1, Dir.Up);
            else if (Held(Win32.VK_DOWN, KeyAction.MoveDown)) TryStep(sel, 0, 1, Dir.Down);
            else if (Held(Win32.VK_LEFT, KeyAction.MoveLeft)) TryStep(sel, -1, 0, Dir.Left);
            else if (Held(Win32.VK_RIGHT, KeyAction.MoveRight)) TryStep(sel, 1, 0, Dir.Right);
        }

        MoveUnits(dt);
        Follow();
    }

    /// <summary>걷는 사람들을 한 걸음씩 — 칸 사이를 나아가고, 다 오면 다음 칸으로.</summary>
    private void MoveUnits(double dt)
    {
        Unit? lead = null;
        foreach (var u in _scene.Units)
        {
            if (u.Moving)
            {
                int before = (int)(u.Step * 6);
                u.Step = Math.Min(1, u.Step + dt * StepsPerSecond);
                if ((lead ??= u) == u)
                    for (int k = before; k < (int)(u.Step * 6); k++) StepSound(u);
                if (u.Moving) continue;
                (u.FromX, u.FromY) = (u.X, u.Y);
            }
            if (u.Path.TryDequeue(out var next))
            {
                int dx = next.X - u.X, dy = next.Y - u.Y;
                // 같은 편 칸은 지나가기만 한다(이동 범위가 그렇게 셈) — 멈출 칸이 아니면 사람이 있어도 걷는다
                if (u.Path.Count > 0 && _scene.Walkable(next.X, next.Y) && _scene.Units.Any(o => o != u && o.Alive && (o.X, o.Y) == next && (o.Team == 0) == (u.Team == 0)))
                {
                    u.Facing = DirOf(dx, dy);
                    (u.FromX, u.FromY) = (u.X, u.Y);
                    (u.X, u.Y) = next;
                    u.Step = 0;
                }
                else if (!TryStep(u, dx, dy, DirOf(dx, dy))) u.Path.Clear();
            }
        }
    }

    /// <summary>발소리 표(EXE 0x52725, 직업 − 1) — 0 은 6토막, 1 은 4토막마다 9번, 그 밖(무거운 · 나는)은 9토막마다 11번.</summary>
    private static readonly byte[] StepSoundKind = [1, 1, 2, 1, 0, 0, 1, 1, 1, 1, 2, 1, 0, 0, 3, 1, 1, 1, 3, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1];

    private int _stepTick;

    /// <summary>
    /// 발소리(sub_32230) — 한 칸을 여섯 토막으로 걸을 때 토막마다 셈을 하나 올리고, 나는 유닛은 6토막마다 묶음 31 의 10번,
    /// 그 밖은 직업 표로 9번(6 · 4토막) · 11번(9토막). 함께 걷는 무리는 첫 사람 것만.
    /// </summary>
    private void StepSound(Unit u)
    {
        var (every, index) = Battle.IsFlyer(u) ? (6, 10)
            : (u.Class is >= 1 and <= 29 ? StepSoundKind[u.Class - 1] : 0) switch { 0 => (6, 9), 1 => (4, 9), _ => (9, 11) };
        if (_stepTick % every == 0) PlaySfx(Sound.UiBank, index);
        _stepTick = (_stepTick + 1) & 0xFF;   // 원작 셈은 한 바이트(byte_54132)
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
        double cx = _lookAt is { } at ? at.X * T + T / 2 : _selected != null ? _selected.DrawX * T + T / 2 : mapW / 2;
        double cy = _lookAt is { } at2 ? at2.Y * T + T / 2 : _selected != null ? _selected.DrawY * T + T / 2 : mapH / 2;
        int want(double c, int map, int view) =>
            map <= view ? (map - view) / 2 : (int)Math.Clamp(c - view / 2, 0, map - view);
        _camX = want(cx, mapW, _boardW);
        if (_viewRows is ({ } top, { } bottom))
            _camY = top * T + want(cy - top * T, (bottom - top) * T, _boardH);   // 프롤로그 — 그 막의 줄들 안에서만
        else _camY = want(cy, mapH, _boardH);
    }

    /// <summary>사람 층을 창 해상도로 찍는다 — 고해상도 그림이 있으면 그것을, 없으면 원본을 점 그대로 배율만큼 키워.</summary>
    private void Compose()
    {
        ComposeScreen();
        DrawParty();
        DrawVolume();
        DrawVersionLabel();
    }

    private void ComposeScreen()
    {
        Array.Clear(_fb);
        if (_screen == Screen.Prologue)
        {
            DrawPrologue();
            DrawKeysWindow();
            return;
        }
        if (_screen == Screen.Ending)
        {
            DrawEnding();
            DrawKeysWindow();
            return;
        }
        if (_screen != Screen.Game)
        {
            DrawTitle();
            DrawVersions();
            DrawKeysWindow();
            return;
        }
        if (_cpuMap || PhaseFxOn) DrawMapCpu();   // 차례 넘김 모자이크는 지도까지 CPU 로
        DrawPatches();
        DrawRanges();
        DrawUnits(outline: true);
        DrawCovers();
        DrawTileFx();
        DrawBattleHud();
        DrawKeysWindow();
    }

    /// <summary>지도 위 사람들 — outline 이면 고른 사람에 테두리.</summary>
    private void DrawUnits(bool outline)
    {
        int cell = T * _zoom;
        // 아래쪽 사람이 위쪽 사람을 가리도록 y 순서로. 쓰러진 사람은 안 그린다.
        foreach (var u in _scene.Units.Where(u => u.Alive || DeathShown(u)).OrderBy(u => u.DrawY))
        {
            int sx = _offX + (int)Math.Round((u.DrawX * T - _camX) * _zoom);
            int sy = _offY + (int)Math.Round((u.DrawY * T - _camY) * _zoom);
            if (sx >= _fbW || sy >= _fbH || sx + cell <= 0 || sy + cell <= 0) continue;
            bool walking = u.Moving || u.Path.Count > 0;
            int f = walking ? WalkCycle[(int)(_time * WalkFps) % WalkCycle.Length]
                            : WalkCycle[(int)(_time * IdleFps) % WalkCycle.Length];
            int frame = (int)u.Facing * 3 + f;
            if (u.Lying) frame = (int)Dir.Left * 3 + 1;   // 누운 사람 — 왼옆 모습을 눕힌다
            var hi = HiIcon(u.Icon, frame);
            bool dim = InBattle && u.Side == Side.Player && u.Acted;   // 행동을 마친 아군은 어둡게
            if (u.Lying) Blit(Rotate(hi ?? _icons.Frame(u.Icon, frame), hi != null ? T * 4 : T), hi != null ? T * 4 : T, sx, sy, cell, dim);
            else if (hi != null) Blit(hi, T * 4, sx, sy, cell, dim);
            else Blit(_icons.Frame(u.Icon, frame), T, sx, sy, cell, dim);
            if (outline && u == (InBattle ? _actor : _selected)) Frame(sx, sy, cell, cell, 2 * Math.Max(1, _zoom / 2), CursorColor);
        }
    }

    /// <summary>정사각 그림을 시계 반대 방향으로 90도 — 왼쪽을 보던 머리가 위 왼쪽으로 가 누운 꼴.</summary>
    private static uint[] Rotate(uint[] px, int size)
    {
        var r = new uint[px.Length];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                r[(size - 1 - x) * size + y] = px[y * size + x];
        return r;
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

    /// <summary>지도를 <c>_fb</c> 에 — 안드로이드처럼 GPU 지도 텍스처가 없을 때. 보이는 칸만 배율만큼 키워.</summary>
    /// <summary>이 장면의 지도 밑 배경 — 있으면 지도를 CPU 로 그리며 타일의 빈 점으로 보이게 한다.</summary>
    private Backdrop? _backdrop;
    private int[]? _wave;

    /// <summary>
    /// 밑그림 — 원작 화면(312x192) 크기를 지도 판 크기로 늘린다. 물결은 줄마다 옆으로, 넓은 그림은 화면 왼쪽 위 칸 × (3점, 2줄)만큼 밀린다.
    /// </summary>
    private void DrawBackdrop(Backdrop b)
    {
        int step = (int)(_time * 18.2) % 16;
        _wave ??= _tables?.WaveTable() ?? new int[16];
        int ox = b.Parallax ? _camX / T * 3 : 0, oy = b.Parallax ? _camY / T * 2 : 0;
        int y1 = Math.Min(_fbH, _offY + _boardH * _zoom), x1 = Math.Min(_fbW, _offX + _boardW * _zoom);
        for (int yy = Math.Max(0, _offY); yy < y1; yy++)
        {
            int sy = (yy - _offY) * 192 / Math.Max(1, _boardH * _zoom);
            int dx = b.Wave ? _wave[(step + sy) % 16] : 0;
            int row = Math.Min(b.H - 1, sy + oy) * b.W, drow = yy * _fbW;
            for (int xx = Math.Max(0, _offX); xx < x1; xx++)
            {
                int sx = (xx - _offX) * 312 / Math.Max(1, _boardW * _zoom) + dx + ox;
                _fb[drow + xx] = b.Px[row + Math.Min(b.W - 1, sx)] | 0xFF000000;
            }
        }
    }

    private void DrawMapCpu()
    {
        Array.Fill(_fb, BgColor);
        var back = _backdrop;
        if (back != null) DrawBackdrop(back);
        int cell = T * _zoom;
        int x0 = Math.Max(0, (int)Math.Floor(_camX / (double)T)), y0 = Math.Max(0, (int)Math.Floor(_camY / (double)T));
        int x1 = Math.Min(_scene.Width, (_camX + _boardW) / T + 1), y1 = Math.Min(_scene.Height, (_camY + _boardH) / T + 1);
        for (int ty = y0; ty < y1; ty++)
            for (int tx = x0; tx < x1; tx++)
            {
                var tile = _scene.TileAt(tx, ty);
                int sx = _offX + (tx * T - _camX) * _zoom, sy = _offY + (ty * T - _camY) * _zoom;
                int yy0 = Math.Max(0, sy), yy1 = Math.Min(_fbH, sy + cell), xx0 = Math.Max(0, sx), xx1 = Math.Min(_fbW, sx + cell);
                for (int yy = yy0; yy < yy1; yy++)
                {
                    int srow = (yy - sy) / _zoom * T, drow = yy * _fbW;
                    for (int xx = xx0; xx < xx1; xx++)
                    {
                        uint c = tile[srow + (xx - sx) / _zoom];
                        if (c != 0 || back == null) _fb[drow + xx] = c | 0xFF000000;   // 밑그림이 있으면 빈 점은 비워 둔다
                    }
                }
            }
    }
}
