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

    /// <summary>고해상도 캐릭터 그림(96x96), 없으면 null — 안드로이드는 늘 null.</summary>
    private partial uint[]? HiIcon(int icon, int frame);

    /// <summary>편집기 띄우기 — 안드로이드는 없다(타이틀 메뉴에서 뺀다).</summary>
    private partial void OpenEditor();

    // ── 플랫폼이 부르는 입구 ─────────────────────────────────────────────────

    internal bool Running => _running;
    internal uint[] FrameBuffer => _fb;
    internal int FrameWidth => _fbW;
    internal int FrameHeight => _fbH;

    /// <summary>처음 — 1장 지도를 깔고 오프닝부터.</summary>
    internal void Start()
    {
        _running = true;
        LoadScene(0);
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
        (_mouseX, _mouseY) = (x, y);
        _mouseMoved = true;
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
        if (_screen == Screen.Prologue) { TalkNext(); return; }
        if (_screen != Screen.Game) { TitleClick(_mouseX, _mouseY); return; }
        if (Talking) { TalkNext(); return; }
        MouseToCursor();
        OnClick((int)Math.Floor((_mouseX - _offX) / (double)_zoom), (int)Math.Floor((_mouseY - _offY) / (double)_zoom));
    }

    /// <summary>오른쪽 단추(안드로이드는 길게 누르기) — 전투에서 한 단계 물리기.</summary>
    internal void PointerCancel()
    {
        if (InBattle) BattleCancel();
    }

    /// <summary>배율과 보이는 판 크기를 창 크기에서 셈한다. 판이 바뀌면 화면 버퍼와 텍스처를 다시 만든다.</summary>
    private void Layout()
    {
        int auto = Math.Max(1, Math.Min(_clientW / BaseW, _clientH / BaseH));
        _zoom = _settings.Zoom > 0 ? _settings.Zoom : auto;
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

    private void OnKeyDown(int vk)
    {
        _keys.Add(vk);
        if (vk == Win32.VK_F12) { RequestShot(); return; }
        if (vk == Win32.VK_F11) { ToggleFullscreen(); return; }
        if (KeysWindowKey(vk)) return;
        if (VersionsKey(vk)) return;
        if (_screen == Screen.Prologue)
        {
            TalkKey(vk);   // Enter · Space 다음, Esc 프롤로그 건너뛰기
            return;
        }
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
        if (_screen == Screen.Prologue) { TalkTick(dt); return; }
        if (_screen != Screen.Game)
        {
            TitleTick(dt);
            return;
        }

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

    /// <summary>사람 층을 창 해상도로 찍는다 — 고해상도 그림이 있으면 그것을, 없으면 원본을 점 그대로 배율만큼 키워.</summary>
    private void Compose()
    {
        Array.Clear(_fb);
        if (_screen == Screen.Prologue)
        {
            DrawPrologue();
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
        int cell = T * _zoom;
        if (_cpuMap) DrawMapCpu();
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
            var hi = HiIcon(u.Icon, frame);
            bool dim = InBattle && u.Side == Side.Player && u.Acted;   // 행동을 마친 아군은 어둡게
            if (hi != null) Blit(hi, T * 4, sx, sy, cell, dim);
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

    /// <summary>지도를 <c>_fb</c> 에 — 안드로이드처럼 GPU 지도 텍스처가 없을 때. 보이는 칸만 배율만큼 키워.</summary>
    private void DrawMapCpu()
    {
        Array.Fill(_fb, BgColor);
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
                    for (int xx = xx0; xx < xx1; xx++) _fb[drow + xx] = tile[srow + (xx - sx) / _zoom] | 0xFF000000;
                }
            }
    }
}
