using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 전투 정보 패널의 접기 · 펴기(창세기전3 파트1 처럼) — 보물상자 목록 · 미니맵 패널의 오른쪽 위 [−] 를 누르면 제목 줄만 남기고 접고, [+] 로 편다(M · T 키와 같은 설정).
/// 폰은 오른쪽 위 ESC 단추(원작 Esc 그대로 — 물리기 · 다음 아군) 와 메뉴 단추(→ 모바일 메뉴).
/// </summary>
internal sealed partial class GameWindow
{
    private (int X, int Y, int W, int H) _miniFold, _chestFold, _escIcon, _menuIcon, _mapIcon, _partyIcon, _readyIcon, _escIcon2, _townPrev, _townNext, _townEnter;

    private static readonly string[] ScreenLockNames = ["끔(폰 설정 따름)", "세로 고정", "가로 고정"];

    /// <summary>화면 방향 설정을 폰에 적용 — 안드로이드만.</summary>
    partial void ApplyScreenLock();

    /// <summary>화면 방향을 잠시 바꾼다(0 폰 설정 · 1 세로 · 2 가로) — 안드로이드만.</summary>
    partial void LockOrientation(int kind);

    /// <summary>마을에서 「돌리기」로 화면을 돌렸나(w-133) — 마을을 나가면 설정대로 되돌린다.</summary>
    private bool _townRotated;
    private (int X, int Y, int W, int H) _rotIcon;

    /// <summary>마을 「돌리기」 — 세로면 가로로, 가로면 세로로.</summary>
    private void RotateTown()
    {
        _townRotated = true;
        LockOrientation(_clientH > _clientW ? 2 : 1);
    }

    private static string EscSpotName(int spot) => spot switch { 0 => "오른쪽 위", 1 => "오른쪽 아래", 3 => "왼쪽 아래", _ => "위 · 오른쪽 아래" };

    /// <summary>오른쪽 위 줄의 아래 끝 — 미니맵 패널을 그 밑에 놓는다(폰은 ESC 단추 밑).</summary>
    private int _hudIconBottom = 12;

    private void DrawHudToggles(int fs)
    {
        _miniFold = _chestFold = _escIcon = _menuIcon = _mapIcon = _partyIcon = _readyIcon = _escIcon2 = _townPrev = _townNext = _townEnter = _rotIcon = default;
        _hudIconBottom = 12;
        if (_townRotated && !InTownView) { _townRotated = false; ApplyScreenLock(); }   // 마을을 나가면 화면 방향을 설정대로(w-133)
        _chestRows.Clear();
        if (_bs is BattleState.CutIn or BattleState.Talk) return;
        if (TouchUi) DrawTouchButtons(fs);
    }

    /// <summary>패널 오른쪽 위 접기 단추 — 펴 있으면 「−」, 접혀 있으면 「+」. 단추 자리를 돌려준다.</summary>
    private (int X, int Y, int W, int H) FoldButton(int x, int y, int size, bool open)
    {
        FillRect(x, y, size, size, 0xC0384068);
        FillRect(x, y, size, 1, 0xFFA8B8E8); FillRect(x, y + size - 1, size, 1, 0xFFA8B8E8);
        FillRect(x, y, 1, size, 0xFFA8B8E8); FillRect(x + size - 1, y, 1, size, 0xFFA8B8E8);
        int t = Math.Max(2, size / 8), l = size * 5 / 8;
        FillRect(x + (size - l) / 2, y + (size - t) / 2, l, t, 0xFFFFFFFF);
        if (!open) FillRect(x + (size - t) / 2, y + (size - l) / 2, t, l, 0xFFFFFFFF);
        return (x, y, size, size);
    }

    /// <summary>그 자리가 접기 단추 · ESC 단추인가 — 단추 누르기는 지도 끌기로 받지 않는다.</summary>
    private bool HudHit(int mx, int my) => (_touchMiniBig && _touchMini.W > 0) ||
        _readyBelow.Select(c => c.Rect).Concat(_chestRows.Select(c => c.Rect)).Concat([_rotIcon, _townPrev, _townNext, _townEnter, _miniFold, _chestFold, _escIcon, _escIcon2, _menuIcon, _mapIcon, _partyIcon, _readyIcon, _touchMini]).Any(r => r.W > 0 && mx >= r.X - 4 && my >= r.Y - 4 && mx < r.X + r.W + 4 && my < r.Y + r.H + 4);

    /// <summary>단추를 눌렀으면 그 패널을 접고 펴고(ESC 는 원작 Esc, 메뉴는 모바일 메뉴) true.</summary>
    private bool HudToggleClick(int mx, int my)
    {
        static bool In((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X - 4 && y >= r.Y - 4 && x < r.X + r.W + 4 && y < r.Y + r.H + 4;
        if (_touchMiniBig && _touchMini.W > 0) return TouchMinimapClick(mx, my);   // 큰 미니맵은 어디를 눌러도 닫는다(w-122)
        if (In(_escIcon, mx, my) || In(_escIcon2, mx, my)) { _mouseMoved = false; PlaySfx(Sound.UiBank, Sound.Cursor); BattleKey(Win32.VK_ESCAPE); return true; }
        if (In(_menuIcon, mx, my)) { OpenTouchMenu(); return true; }
        // 지도 아래 안 움직인 아군 줄 — 누르기 · 끌기는 ReadyPress · ReadyRelease 가 받는다(fb-92)
        if (InRect(_readyArea, mx, my)) return true;
        if (In(_rotIcon, mx, my)) { PlaySfx(Sound.UiBank, Sound.Cursor); RotateTown(); return true; }
        if (In(_townEnter, mx, my)) { _mouseMoved = false; PlaySfx(Sound.UiBank, Sound.Cursor); BattleKey(Win32.VK_RETURN); return true; }
        if (In(_townPrev, mx, my) || In(_townNext, mx, my))
        {
            _mouseMoved = false;
            PlaySfx(Sound.UiBank, Sound.Cursor);
            BattleKey(In(_townPrev, mx, my) ? Win32.VK_LEFT : Win32.VK_RIGHT);
            return true;
        }
        if (In(_readyIcon, mx, my)) { CursorToNextReady(); PlaySfx(Sound.UiBank, Sound.Cursor); return true; }
        if (In(_partyIcon, mx, my)) { PlaySfx(Sound.UiBank, Sound.Menu); OpenParty(); return true; }
        if (In(_mapIcon, mx, my)) { _touchMiniOpen = !_touchMiniOpen; _touchMiniBig = _touchMiniOpen;   // 바로 화면 가득(w-129)
            PlaySfx(Sound.UiBank, Sound.Cursor); return true; }
        if (TouchMinimapClick(mx, my)) return true;
        if (_bs is BattleState.Select or BattleState.Target && _chestRows.FindIndex(c => In(c.Rect, mx, my)) is var row and >= 0)
        {
            // 보물상자 줄 — 커서만 그 칸으로(지도가 따라간다). 마우스 자리가 커서를 도로 가져가지 않게
            _mouseMoved = false;
            _cursor = (_chestRows[row].X, _chestRows[row].Y);
            _centerCursor = true;
            PlaySfx(Sound.UiBank, Sound.Cursor);
            return true;
        }
        if (In(_miniFold, mx, my)) _settings.ShowMinimap = !_settings.ShowMinimap;
        else if (In(_chestFold, mx, my)) _settings.ShowChests = !_settings.ShowChests;
        else return false;
        _settings.Save();
        return true;
    }

    // ── 폰: 오른쪽 위 ESC 단추(원작 Esc 키와 같다) · 메뉴 단추(→ 모바일 메뉴: 보일 것 켜고 끄기 · 시스템 메뉴) ─────────────

    /// <summary>모바일 메뉴를 열 수 있는 때 — 우리 차례에 고르는 중.</summary>
    private bool CanOpenTouchMenu => InBattle && !_enemyPhase && (_bs == BattleState.Select || TownTop);

    /// <summary>마을 첫 화면(건물 고르기)인가.</summary>
    private bool TownTop => _townTop && _stage == MenuStage.Town && _bs == BattleState.Menu && InTownView;

    /// <summary>모바일 메뉴를 마을에서 열었나 — 닫으면 마을로, 에디트는 동료 표 · 기록의 돈을 고친다.</summary>
    private bool _touchFromTown;

    /// <summary>모바일 메뉴를 닫는다 — 싸움이면 고르기로, 마을이면 마을 첫 화면으로.</summary>
    private void CloseTouchMenu()
    {
        _menu.Clear();
        if (_touchFromTown) { _touchFromTown = false; OpenTownMenu(_townSpot); }
        else _bs = BattleState.Select;
    }

    /// <summary>에디트 돈 — 마을에서는 기록의 돈까지(상점 · 저장이 그것을 본다).</summary>
    private void EditGold(int add)
    {
        _battle.Gold += add;
        if (_touchFromTown && _campaign is { } c) c.Gold = _battle.Gold;
    }

    /// <summary>에디트 — 그 사람 가방에 물건 하나(w-147). 가방이 차면 공통 가방(켜져 있고 담을 수 있는 것)으로.</summary>
    private void EditAddItem(Unit u, int item)
    {
        if (u.Bag.Count < 8)
        {
            u.Bag.Add((item, false));
            EditCommit(u);
            PlaySfx(Sound.UiBank, 5);
            Toast($"{GameNames.Item(item)} 넣었습니다 (가방 {u.Bag.Count}/8)");
        }
        else if (_settings.SharedBag && PoolItem(item))
        {
            Pool.Add(item);
            PlaySfx(Sound.UiBank, 5);
            Toast($"가방이 가득 차 공통 가방에 넣었습니다 — {GameNames.Item(item)}");
        }
        else { PlaySfx(Sound.UiBank, 7); Toast("가방이 가득 찼습니다"); }
    }

    /// <summary>에디트로 고친 사람을 동료 표에 — 마을의 사람은 동료 표로 세운 사본이다.</summary>
    private void EditCommit(Unit u)
    {
        if (!_touchFromTown || _campaign is not { } c) return;
        int i = c.Party.FindIndex(r => HeroRecord.Id(r) == u.NameId);
        if (i >= 0) c.Party[i] = HeroRecord.From(u);
    }

    /// <summary>ESC 단추를 보이는 때 — 우리 차례에 고르기 · 겨누기 · 메뉴(원작 Esc 가 듣는 때).</summary>
    private bool CanTouchEsc => InBattle && !_enemyPhase && _bs is BattleState.Select or BattleState.Target or BattleState.Menu;

    private void DrawTouchButtons(int fs)
    {
        int right = _clientW - 14, y = 10, bottom = 0;
        int spot = _settings.EscSpot;
        if (CanTouchEsc && spot is 0 or 2)
        {
            _escIcon = DrawHudButton("ESC", fs, right, y, "esc");
            right = _hudDefault.X - 10;   // 옆 단추는 기본 자리를 따라 줄 선다(옮긴 단추 자리와 상관없이)
            bottom = _hudDefault.Y + _hudDefault.H;
        }
        if (CanTouchEsc && spot is 1 or 2 or 3)
        {
            // 아래 ESC — 엄지가 닿는 자리에 크게(자주 쓰는 단추, 사용자 요청)
            int big = fs * 3 / 2;
            var label = Text.Render("ESC", big, 0xFFFFFFFF);
            int w = label.W + big / 2 * 2, h = label.H + big / 2;
            _escIcon2 = DrawHudButton("ESC", big, spot == 3 ? 14 + w : _clientW - 14, _clientH - h - 14, "esc2");   // 3 은 왼쪽 아래
        }
        if (CanOpenTouchMenu)
        {
            _menuIcon = DrawHudButton("메뉴", fs, right, y, "menu");
            var menuAt = _hudDefault;
            bottom = Math.Max(bottom, menuAt.Y + menuAt.H);
            // 지도 — 누르면 미니맵이 뜨고 한 번 더 누르면 닫힌다(사용자 요청)
            int next = menuAt.X;
            if (!TownTop) { _mapIcon = DrawHudButton("지도", fs, next - 10, y, "map"); next = _hudDefault.X; }
            _partyIcon = DrawHudButton("파티", fs, next - 10, y, "party");   // 파티 정보 — 메뉴에서 빼 단추로(사용자 요청)
            if (TownTop) _rotIcon = DrawRotateButton(fs, _hudDefault.X - 10, y);   // 마을 — 화면을 돌려 넓게 보기(w-133), 그림 단추(w-136)
        }
        if (bottom > 0) _hudIconBottom = bottom + 6;
        if (TownTop)
        {
            // 마을 — 아래 ESC 와 같은 줄 · 같은 크기로 ◀ ▶ · 들어가기(그림을 가리지 않게, 사용자 요청). ESC 가 왼쪽 아래면 그 옆부터
            int big = fs * 3 / 2, gap = 10, by = _clientH - Text.Render("ESC", big, 0xFFFFFFFF).H - big / 2 - 14;
            int Width(string t) => Text.Render(t, big, 0xFFFFFFFF).W + big / 2 * 2;
            int bx = 14 + (spot == 3 ? Width("ESC") + gap : 0);
            (int X, int Y, int W, int H) Next(string t) { bx += Width(t); var r = DrawHudButton(t, big, bx, by); bx += gap; return r; }
            _townPrev = Next("◀");
            _townNext = Next("▶");
            _townEnter = Next(_townSpot == 2 ? "나가기" : "들어가기");   // 고른 건물에 들어가기, 출구면 나가기
        }
        DrawReadyCount(fs);
    }

    /// <summary>
    /// 폰 — 아래 가운데에 아직 안 움직인 우리 편 수(「남은 아군 3 / 5」). 누르면 Esc 처럼 다음 안 움직인 아군으로 커서가 간다(사용자 요청).
    /// </summary>
    private static readonly string[] ReadySpotNames = ["아래 가운데", "아래 왼쪽", "아래 오른쪽", "위 가운데", "숨김"];

    private void DrawReadyCount(int fs)
    {
        if (!InBattle || _enemyPhase || _bs is not (BattleState.Select or BattleState.Target)) return;   // 메뉴 · 묻는 창이 떠 있으면 숨긴다(누르기가 겹치지 않게)
        var mine = Controlled.Where(u => u.Hp > 0).ToList();
        if (mine.Count == 0) return;
        int left = mine.Count(Ready);
        // NPC 아군도 내가 움직이면 따로 센다 — 색은 지도의 체력 막대와 같게 노랑(사용자 요청)
        var heroes = mine.Where(u => !u.Ai).ToList();
        var npcs = mine.Where(u => u.Ai).ToList();
        int heroLeft = heroes.Count(Ready), npcLeft = npcs.Count(Ready);
        var label = npcs.Count == 0
            ? Text.Render($"남은 아군 {left} / {mine.Count}", fs, left > 0 ? 0xFFFFFFFF : 0xFFB0B0B0)
            : HJoin(Text.Render($"아군 {heroLeft} / {heroes.Count}", fs, heroLeft > 0 ? 0xFFFFFFFF : 0xFFB0B0B0),
                    Text.Render($"   NPC {npcLeft} / {npcs.Count}", fs, npcLeft > 0 ? 0xFFF0D040 : 0xFFB0B0B0));
        if (_settings.ReadySpot == 4) return;
        int pad = fs / 2, w = label.W + pad * 2, h = label.H + pad, x = (_clientW - w) / 2, y = _clientH - h - 8;
        switch (_settings.ReadySpot)
        {
            case 1: x = 14; break;
            case 2: (x, y) = (_clientW - w - 14, _clientH - h * 3 - 24); break;   // ESC 단추 위
            case 3: y = _hudIconBottom; break;
        }
        (x, y) = HudSpot("ready", x, y, w, h);
        uint edge = _hudMoving == "ready" ? 0xFFFFE070 : 0x80FFFFFF;
        FillRect(x, y, w, h, 0xA0101020);
        FillRect(x, y, w, 1, edge); FillRect(x, y + h - 1, w, 1, edge);
        FillRect(x, y, 1, h, edge); FillRect(x + w - 1, y, 1, h, edge);
        DrawText(label, x + pad, y + pad / 2);
        _readyIcon = (x, y, w, h);
    }

    /// <summary>
    /// 화면 돌리기 그림 단추(w-136) — 글자 단추와 같은 높이 · 테두리, 가운데 폰 모양과 그 둘레를 도는 화살표. 오른쪽 끝이 right.
    /// </summary>
    private (int X, int Y, int W, int H) DrawRotateButton(int fs, int right, int y)
    {
        int h = Text.Render("가", fs, 0xFFFFFFFF).H + fs / 2, w = h * 5 / 4, x = right - w;
        const uint edge = 0xC0FFFFFF, ink = 0xFFFFFFFF;
        FillRect(x, y, w, h, 0x90101020);
        FillRect(x, y, w, 1, edge); FillRect(x, y + h - 1, w, 1, edge);
        FillRect(x, y, 1, h, edge); FillRect(x + w - 1, y, 1, h, edge);
        double cx = x + w / 2.0, cy = y + h / 2.0, r = h * 0.36;
        int t = Math.Max(2, h / 14), t2 = Math.Max(1, h / 20);
        // 가운데 폰(세로 네모 테두리)
        int pw = (int)(r * 0.55), ph = (int)(r * 0.9), px = (int)(cx - pw / 2.0), py = (int)(cy - ph / 2.0);
        FillRect(px, py, pw, t2, ink); FillRect(px, py + ph - t2, pw, t2, ink);
        FillRect(px, py, t2, ph, ink); FillRect(px + pw - t2, py, t2, ph, ink);
        // 둘레 화살(위 오른쪽에서 시계 방향으로 거의 한 바퀴)
        double a0 = -55, a1 = 225;
        for (double a = a0; a <= a1; a += 3)
        {
            double rad = a * Math.PI / 180;
            FillRect((int)(cx + r * Math.Cos(rad) - t / 2.0), (int)(cy + r * Math.Sin(rad) - t / 2.0), t, t, ink);
        }
        // 화살촉 — 끝(a0)에서 반시계 쪽을 향해(그림이 시계 방향으로 돌게)
        double e = a0 * Math.PI / 180, ex = cx + r * Math.Cos(e), ey = cy + r * Math.Sin(e);
        double dx = Math.Sin(e), dy = -Math.Cos(e);   // a 가 줄어드는 쪽(반시계) 접선
        double sz = Math.Max(4, h * 0.18);
        (double X, double Y) tip = (ex + dx * sz, ey + dy * sz), l = (ex - dy * sz * 0.8, ey + dx * sz * 0.8), rr = (ex + dy * sz * 0.8, ey - dx * sz * 0.8);
        int bx0 = (int)Math.Floor(Math.Min(tip.X, Math.Min(l.X, rr.X))), bx1 = (int)Math.Ceiling(Math.Max(tip.X, Math.Max(l.X, rr.X)));
        int by0 = (int)Math.Floor(Math.Min(tip.Y, Math.Min(l.Y, rr.Y))), by1 = (int)Math.Ceiling(Math.Max(tip.Y, Math.Max(l.Y, rr.Y)));
        static double Side((double X, double Y) p, (double X, double Y) q, double ax, double ay) => (q.X - p.X) * (ay - p.Y) - (q.Y - p.Y) * (ax - p.X);
        for (int qy = by0; qy <= by1; qy++)
            for (int qx = bx0; qx <= bx1; qx++)
            {
                double s1 = Side(tip, l, qx + .5, qy + .5), s2 = Side(l, rr, qx + .5, qy + .5), s3 = Side(rr, tip, qx + .5, qy + .5);
                if (s1 >= 0 && s2 >= 0 && s3 >= 0 || s1 <= 0 && s2 <= 0 && s3 <= 0) FillRect(qx, qy, 1, 1, ink);
            }
        return (x, y, w, h);
    }

    /// <summary>
    /// 오른쪽 끝이 right 인 글자 단추를 그리고 자리를 돌려준다. key 가 있으면 3초 눌러 옮긴 자리에 그린다
    /// (기본 자리는 <see cref="_hudDefault"/> — 옆 단추를 그 옆에 줄 세울 때 쓴다).
    /// </summary>
    private (int X, int Y, int W, int H) DrawHudButton(string text, int fs, int right, int y, string? key = null)
    {
        var label = Text.Render(text, fs, 0xFFFFFFFF);
        int pad = fs / 2, w = label.W + pad * 2, h = label.H + pad, x = right - w;
        _hudDefault = (x, y, w, h);
        (x, y) = HudSpot(key, x, y, w, h);
        uint edge = key != null && key == _hudMoving ? 0xFFFFE070 : 0xC0FFFFFF;
        FillRect(x, y, w, h, 0x90101020);
        FillRect(x, y, w, 1, edge); FillRect(x, y + h - 1, w, 1, edge);
        FillRect(x, y, 1, h, edge); FillRect(x + w - 1, y, 1, h, edge);
        DrawText(label, x + pad, y + pad / 2);
        return (x, y, w, h);
    }

    // ── 폰 단추 옮기기(사용자 요청) — 겹쳐 보이는 단추를 3초 누르고 있으면 테두리가 노래지고, 끌어 놓은 자리에 남는다 ──

    private const double HudHoldSeconds = 3;

    /// <summary>마지막으로 그린 단추의 기본 자리(옮기기 전).</summary>
    private (int X, int Y, int W, int H) _hudDefault;

    /// <summary>누르고 있는 단추 · 누른 시간 · 누른 자리.</summary>
    private string? _hudHoldKey;
    private double _hudHoldTime;
    private (int X, int Y) _hudHoldFrom;

    /// <summary>옮기는 중인 단추 · 그 가운데 자리 · 손가락과 가운데의 차이.</summary>
    private string? _hudMoving;
    private (int X, int Y) _hudMoveAt, _hudGrab;

    /// <summary>단추를 누르고 있거나 옮기는 중 — 안드로이드의 길게 누르기(물리기)를 건너뛴다.</summary>
    internal bool HudHolding => _hudHoldKey != null || _hudMoving != null;

    private string HudSpotKey(string key) => key + (_clientH > _clientW ? ":세로" : ":가로");

    /// <summary>단추 key 를 그릴 자리 — 옮기는 중이면 손가락을 따라, 옮겨 둔 자리가 있으면 거기, 아니면 기본 자리. 화면 밖으로는 안 나간다.</summary>
    private (int X, int Y) HudSpot(string? key, int x, int y, int w, int h)
    {
        if (key == null || !TouchUi) return (x, y);
        (int X, int Y)? center = null;
        if (key == _hudMoving) center = _hudMoveAt;
        else if (_settings.HudSpots.TryGetValue(HudSpotKey(key), out var f) && f is [var fx, var fy])
            center = ((int)(fx * _clientW), (int)(fy * _clientH));
        if (center is not { } c) return (x, y);
        return (Math.Clamp(c.X - w / 2, 0, Math.Max(0, _clientW - w)), Math.Clamp(c.Y - h / 2, 0, Math.Max(0, _clientH - h)));
    }

    /// <summary>그 자리의 옮길 수 있는 단추.</summary>
    private (string Key, (int X, int Y, int W, int H) Rect)? HudButtonAt(int x, int y)
    {
        foreach (var (key, r) in new[] { ("esc", _escIcon), ("esc2", _escIcon2), ("menu", _menuIcon), ("map", _mapIcon), ("party", _partyIcon), ("ready", _readyIcon) })
            if (r.W > 0 && x >= r.X - 4 && y >= r.Y - 4 && x < r.X + r.W + 4 && y < r.Y + r.H + 4) return (key, r);
        return null;
    }

    /// <summary>단추 위를 눌렀다 — 뗄 때 누르기로 하고(3초 넘게 누르면 옮기기) true.</summary>
    private bool HudHoldPress(int x, int y)
    {
        if (!TouchUi || !InBattle || HudButtonAt(x, y) is not { } hit) return false;
        (_hudHoldKey, _hudHoldTime, _hudHoldFrom, _hudMoving) = (hit.Key, 0, (x, y), null);
        _press = null;
        return true;
    }

    private void HudHoldTick(double dt)
    {
        if (_hudHoldKey == null) return;
        _hudHoldTime += dt;
        if (_hudHoldTime < HudHoldSeconds) return;
        var r = HudButtonAt(_hudHoldFrom.X, _hudHoldFrom.Y) is { } hit && hit.Key == _hudHoldKey ? hit.Rect : default;
        if (r.W == 0) { _hudHoldKey = null; return; }   // 그새 단추가 사라졌다
        (int X, int Y) center = (r.X + r.W / 2, r.Y + r.H / 2);
        (_hudMoving, _hudMoveAt, _hudGrab, _hudHoldKey) = (_hudHoldKey, center, (_hudHoldFrom.X - center.X, _hudHoldFrom.Y - center.Y), null);
        PlaySfx(Sound.UiBank, Sound.Menu);
    }

    /// <summary>누른 채 움직였다 — 옮기는 중이면 단추가 따라오고, 3초 전에 많이 움직이면 누르기를 버린다.</summary>
    private bool HudHoldMove(int x, int y)
    {
        if (_hudMoving != null) { _hudMoveAt = (x - _hudGrab.X, y - _hudGrab.Y); return true; }
        if (_hudHoldKey == null) return false;
        if (Math.Abs(x - _hudHoldFrom.X) + Math.Abs(y - _hudHoldFrom.Y) > DragSlop * 3) _hudHoldKey = null;
        return true;
    }

    /// <summary>뗐다 — 옮기는 중이면 그 자리를 남기고, 3초 전이면 그냥 누르기.</summary>
    private bool HudHoldRelease()
    {
        if (_hudMoving is { } moved)
        {
            _hudMoving = null;
            _settings.HudSpots[HudSpotKey(moved)] = [Math.Clamp(_hudMoveAt.X / (double)Math.Max(1, _clientW), 0, 1), Math.Clamp(_hudMoveAt.Y / (double)Math.Max(1, _clientH), 0, 1)];
            _settings.Save();
            PlaySfx(Sound.UiBank, Sound.Cursor);
            return true;
        }
        if (_hudHoldKey == null) return false;
        _hudHoldKey = null;
        _press = null;
        PointerDown(_hudHoldFrom.X, _hudHoldFrom.Y);
        return true;
    }

    /// <summary>맨 위 줄의 이름 · 설명 — 시안(메뉴).</summary>
    private static readonly Dictionary<string, (string Name, string Sub)> MobileRows = new()
    {
        ["화면 표시"] = ("화면 표시", "칸 정보 · 상대 정보 · 보물상자 · 승패 조건"),
        ["설정"] = ("설정", "속도 · 회전 고정 · 소리 · 조작"),
        ["시스템 메뉴"] = ("시스템 메뉴", "턴 끝 · 행군 · 전황 기록"),
        ["에디트 — 돈 · 경험치 · 아이템"] = ("에디트", "돈 · 경험치 · 아이템"),
        ["세이브 연동(구글 · 개발중)"] = ("세이브 연동(개발중)", "구글 계정에 기록 올리고 받기"),
        ["버그 리포트"] = ("버그 리포트", "문제를 적어 보내기"),
        ["타이틀로"] = ("타이틀로", "처음 화면으로(기록하지 않은 진행은 사라짐)"),
    };

    /// <summary>
    /// 폰 세로 모바일 메뉴 — 엄지가 닿는 아래 시트(사용자 요청, 시안). 위에 제목 · 지금 상황, 줄마다 이름과 값(켬 · 끔은 스위치), 맨 아래 금색 닫기.
    /// 맨 끝 줄이 늘 닫기다(<see cref="OpenTouchMenu"/>). 줄이 넘치면 끌어 넘기고, 닫기는 시트 아래에 고정.
    /// </summary>
    private void DrawMobileSheet()
    {
        double ui = _clientW / 390.0;
        int U(double v) => (int)Math.Round(v * ui);
        int large = U(20), body = U(16), small = U(12), m = U(20);
        FillRect(0, 0, _clientW, _clientH, 0xA0060916);
        int headH = U(68), rowH = U(60), footH = U(84), last = _menu.Count - 1;
        int fit = Math.Max(1, (_clientH - U(48) - headH - footH) / rowH), shown = Math.Min(fit, last);
        (_menuRowH, _menuFit) = (rowH, last > fit ? fit + 1 : _menu.Count);   // 끌기는 닫기 줄을 뺀 줄 수로 재므로 닫기 몫 하나를 더한다
        var sig = (_stage, _menu.Count, _menu[0].Label);
        if (sig != _menuSig) (_menuSig, _menuScroll, _menuScrolledByDrag) = (sig, 0, false);
        if (last <= fit) _menuScroll = 0;
        else
        {
            if (!_menuScrolledByDrag && _menuIndex >= 0 && _menuIndex < last)
            {
                if (_menuIndex < _menuScroll) _menuScroll = _menuIndex;
                else if (_menuIndex >= _menuScroll + fit) _menuScroll = _menuIndex - fit + 1;
            }
            _menuScroll = Math.Clamp(_menuScroll, 0, last - fit);
        }
        int h = headH + shown * rowH + footH, y = _clientH - h;
        _menuBox = (0, y + headH, _clientW, shown * rowH);
        FillRect(0, y, _clientW, h, 0xFF1C2850);
        FillRect(0, y, _clientW, Math.Max(2, U(2)), 0xFFC8A040);

        string title = _mobilePage switch { 0 => "메뉴", 1 => "화면 표시", 2 => "설정", 5 => "타이틀로 갈까요?", _ => "에디트" };
        string now = _touchFromTown ? "마을" : $"{ChapterName(_scene.Index)} · {Turn}턴 · 아군 차례";
        var head = Text.Render(title, large, 0xFFEEF0FF);
        var state = Text.Render(now, small, 0xFFC4CAE6, bold: false);
        var gold = Text.Render($"{_battle.Gold:N0}원", body, 0xFFE8C870);
        DrawText(head, m, y + U(12));
        DrawText(state, m, y + U(12) + head.H);
        DrawText(gold, _clientW - m - gold.W, y + (headH - gold.H) / 2);
        FillRect(0, y + headH - 1, _clientW, 1, 0xFF3A4678);

        for (int i = 0; i < last; i++)
        {
            int row = i - _menuScroll;
            if (row < 0 || row >= shown) { _menu[i] = (_menu[i].Label, _menu[i].Do, default); continue; }
            var rect = (X: 0, Y: y + headH + row * rowH, W: _clientW, H: rowH);
            if (_mouseX >= 0 && _mouseY >= rect.Y && _mouseY < rect.Y + rowH && _mouseMoved && !_menuDragging) _menuIndex = i;
            if (i == _menuIndex) FillRect(0, rect.Y, _clientW, rowH - 1, 0xFF34447E);
            FillRect(0, rect.Y + rowH - 1, _clientW, 1, 0xFF2C3768);
            string label = _menu[i].Label;
            if (_menuGlyphs.ContainsKey(i))
            {
                var img = MenuRow(label, i, body);
                DrawText(img, m, rect.Y + (rowH - img.H) / 2);
            }
            else if (MobileRows.TryGetValue(label, out var r) && _mobilePage == 0)
            {
                var name = Text.Render(r.Name, body, 0xFFEEF0FF);
                var note = Text.Render(r.Sub, small, 0xFFC4CAE6, bold: false);
                DrawText(name, m, rect.Y + (rowH - name.H - note.H) / 2);
                DrawText(note, m, rect.Y + (rowH - name.H - note.H) / 2 + name.H);
            }
            else
            {
                int at = label.LastIndexOf("  [", StringComparison.Ordinal);
                var name = Text.Render(at >= 0 ? label[..at] : label, body, 0xFFEEF0FF);
                DrawText(name, m, rect.Y + (rowH - name.H) / 2);
                if (at >= 0)
                {
                    string value = label[(at + 3)..].TrimEnd(']');
                    int dash = value.IndexOf(" — ", StringComparison.Ordinal);
                    if (dash > 0) value = value[..dash];
                    if (value is "켬" or "보임" or "끔" or "숨김")
                    {
                        bool on = value is "켬" or "보임";
                        int tw = U(52), th = U(30), tx = _clientW - m - tw, ty = rect.Y + (rowH - th) / 2, k = U(22);
                        FillRect(tx, ty, tw, th, on ? 0xFFC8A040 : 0xFF3A4678);
                        FillRect(on ? tx + tw - k - U(4) : tx + U(4), ty + U(4), k, k, 0xFFEEF0FF);
                    }
                    else
                    {
                        var v = Text.Render(value, U(14), 0xFFE8C870);
                        DrawText(v, _clientW - m - v.W, rect.Y + (rowH - v.H) / 2);
                    }
                }
            }
            _menu[i] = (label, _menu[i].Do, rect);
        }
        if (last > shown)
        {
            int barH = rowH * shown, thumb = Math.Max(U(24), barH * shown / last), bar = (barH - thumb) * _menuScroll / Math.Max(1, last - shown);
            FillRect(_clientW - U(4), y + headH + bar, U(4), thumb, 0xE0C8A040);
        }
        var close = (X: U(16), Y: _clientH - footH + U(12), W: _clientW - 2 * U(16), H: U(52));
        FillRect(close.X, close.Y, close.W, close.H, 0xFFC8A040);
        var word = Text.Render(_menu[last].Label, body, 0xFF141A33);
        DrawText(word, close.X + (close.W - word.W) / 2, close.Y + (close.H - word.H) / 2);
        _menu[last] = (_menu[last].Label, _menu[last].Do, close);
    }

    /// <summary>모바일 메뉴의 쪽 — 0 맨 위(최대 넷), 1 화면 표시(켜고 끄기), 2 설정(소리 · 단축키), 3 에디트(돈 · 사람 고르기), 4 에디트 — 고른 사람 경험치.</summary>
    private int _mobilePage;

    /// <summary>에디트에서 고른 사람.</summary>
    private Unit? _editUnit;

    /// <summary>
    /// 모바일 메뉴 — 맨 위는 화면 표시 · 설정 · 시스템 메뉴 · 닫기(사용자 요청, 파티 정보는 오른쪽 위 「파티」 단추로). Esc · 메뉴 밖 누르기로도 닫힌다.
    /// 화면 표시 = 칸 정보 · 상대 정보 · 보물상자 · 승패 조건 · 조작법 켜고 끄기, 설정 = 소리 크기 · 단축키 · 패드 설정(시스템 십자 메뉴의 설정 단추도 여기로).
    /// </summary>
    private void OpenTouchMenu(int index = 0, int page = 0)
    {
        if (!CanOpenTouchMenu && _bs != BattleState.Menu) return;
        if (!(_bs == BattleState.Menu && _stage == MenuStage.Mobile)) PlaySfx(Sound.UiBank, Sound.Menu);   // 처음 뜰 때만(켜고 끄며 다시 그릴 때는 말고)
        if (TownTop) _touchFromTown = true;
        _stage = MenuStage.Mobile;
        _mobilePage = page;
        _menu.Clear();
        _menuGlyphs.Clear(); _menuIcons.Clear();
        void Close() => CloseTouchMenu();
        void Toggle(string label, bool on, Action flip) =>
            _menu.Add(($"{label}  [{(on ? "보임" : "숨김")}]", () => { flip(); _settings.Save(); OpenTouchMenu(_menuIndex, 1); }, default));
        switch (page)
        {
            case 1:
                Toggle("칸 정보", _settings.TouchTileInfo, () => _settings.TouchTileInfo = !_settings.TouchTileInfo);
                Toggle("상대 정보", _settings.TouchUnitInfo, () => _settings.TouchUnitInfo = !_settings.TouchUnitInfo);
                Toggle("보물상자", _settings.TouchChests, () => _settings.TouchChests = !_settings.TouchChests);
                Toggle("승패 조건", _settings.TouchGoals, () => _settings.TouchGoals = !_settings.TouchGoals);
                Toggle("조작법", _settings.TouchHelp, () => _settings.TouchHelp = !_settings.TouchHelp);
                break;
            case 2:
                _menu.Add(($"고를 때 확대  [{(_settings.TouchFocusZoom ? "켬" : "끔")}]", () =>
                {
                    _settings.TouchFocusZoom = !_settings.TouchFocusZoom;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"공통 가방  [{(_settings.SharedBag ? "켬" : "끔")}]", () =>
                {
                    ToggleSharedBag();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"NPC 아군 내가 움직이기  [{(_settings.ControlAllies ? "켬" : "끔")}]", () =>
                {
                    _settings.ControlAllies = !_settings.ControlAllies;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"남은 아군 표시  [{ReadySpotNames[Math.Clamp(_settings.ReadySpot, 0, 4)]}]", () =>
                {
                    _settings.ReadySpot = (_settings.ReadySpot + 1) % ReadySpotNames.Length;   // 누를 때마다 다음 자리
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"미니맵 보물상자  [{(_settings.MiniChests ? "켬" : "끔")}]", () =>
                {
                    _settings.MiniChests = !_settings.MiniChests;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"마법 피해 · 회복 %  [{(_settings.SpellDamagePct ? "켬" : "끔")}]", () =>
                {
                    _settings.SpellDamagePct = !_settings.SpellDamagePct;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"필살기 모션  [{(_settings.SplitSkillMotion ? "몸짓 · 이펙트 따로" : "원작 그대로")}]", () =>
                {
                    _settings.SplitSkillMotion = !_settings.SplitSkillMotion;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"지도 아래 남은 아군  [{(_settings.ReadyBelowMap ? "켬" : "끔")}]", () =>
                {
                    _settings.ReadyBelowMap = !_settings.ReadyBelowMap;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"적 공격 장면  [{(_settings.SkipEnemyFights ? "건너뛰기" : "보기")}]", () =>
                {
                    _settings.SkipEnemyFights = !_settings.SkipEnemyFights;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"ESC 단추 자리  [{EscSpotName(_settings.EscSpot)}]", () =>
                {
                    _settings.EscSpot = (_settings.EscSpot + 1) % 4;   // 누를 때마다 다음 자리
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"전투 모션 속도  [{SpellSpeedName(_settings.StrikeSpeed)}]", () =>
                {
                    int at = Array.IndexOf(SpellSpeeds, _settings.StrikeSpeed);
                    _settings.StrikeSpeed = SpellSpeeds[(at + 1) % SpellSpeeds.Length];
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"마법 속도  [{SpellSpeedName(_settings.SpellSpeed)}]", () =>
                {
                    int at = Array.IndexOf(SpellSpeeds, _settings.SpellSpeed);
                    _settings.SpellSpeed = SpellSpeeds[(at + 1) % SpellSpeeds.Length];   // 누를 때마다 다음 배율
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"알림 글 속도  [{SpellSpeedName(_settings.NoticeSpeed)}]", () =>
                {
                    int at = Array.IndexOf(EnemySpeeds, _settings.NoticeSpeed);
                    _settings.NoticeSpeed = EnemySpeeds[(at + 1) % EnemySpeeds.Length];
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"아군 이동 속도  [{SpellSpeedName(_settings.AllySpeed)}]", () =>
                {
                    int at = Array.IndexOf(EnemySpeeds, _settings.AllySpeed);
                    _settings.AllySpeed = EnemySpeeds[(at + 1) % EnemySpeeds.Length];
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(($"적 이동 속도  [{SpellSpeedName(_settings.EnemySpeed)}]", () =>
                {
                    int at = Array.IndexOf(EnemySpeeds, _settings.EnemySpeed);
                    _settings.EnemySpeed = EnemySpeeds[(at + 1) % EnemySpeeds.Length];
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                if (OperatingSystem.IsAndroid())
                    _menu.Add(($"화면 회전 고정  [{ScreenLockNames[Math.Clamp(_settings.ScreenLock, 0, 2)]}]", () =>
                    {
                        _settings.ScreenLock = (_settings.ScreenLock + 1) % ScreenLockNames.Length;
                        _settings.Save();
                        ApplyScreenLock();
                        OpenTouchMenu(_menuIndex, 2);
                    }, default));
                if (OperatingSystem.IsAndroid())
                {
                    string hi = $"고해상도(4배, 폰에서 차례로 만듦 · 느림)  [{(_settings.HiResPhone ? "켬" : "끔")}]";
                    HiResInfo(ref hi);
                    _menu.Add((hi, () => { HiResToggle(); OpenTouchMenu(_menuIndex, 2); }, default));
                }
                if (_settings.HudSpots.Count > 0)
                    _menu.Add(("단추 자리 처음대로(3초 눌러 옮긴 것)", () =>
                    {
                        _settings.HudSpots.Clear();
                        _settings.Save();
                        OpenTouchMenu(_menuIndex, 2);
                    }, default));
                _menu.Add(("조작법 보기", () => { Close(); OpenTutorial(); }, default));
                _menu.Add(($"익명 플레이 통계 보내기  [{(_settings.SendStats ? "켬" : "끔")}]", () =>
                {
                    _settings.SendStats = !_settings.SendStats;
                    if (!_settings.SendStats) _play = null;
                    _settings.Save();
                    OpenTouchMenu(_menuIndex, 2);
                }, default));
                _menu.Add(("소리 크기", () => { Close(); OpenVolume(); }, default));
                _menu.Add(("단축키 · 패드 설정", () => { Close(); _keysOpen = true; _keysRow = 0; _keysCapture = -1; }, default));
                break;
            case 3:
                // 에디트(사용자 요청) — 돈 올리기, 우리 편마다 경험치. 경험치는 원작 경험 함수(GainExp)로 넣어 100 이 넘으면 바로 레벨이 오른다
                _menu.Add(($"돈 +1,000  (지금 {_battle.Gold:N0})", () => { EditGold(1000); OpenTouchMenu(_menuIndex, 3); }, default));
                _menu.Add(("돈 +10,000", () => { EditGold(10000); OpenTouchMenu(_menuIndex, 3); }, default));
                foreach (var u in _touchFromTown ? PartyMembers().Where(u => u.Hp > 0) : _scene.Units.Where(u => u.Team == 2 && u.Hp > 0).OrderBy(u => u.Slot))
                {
                    var who = u;
                    _menuGlyphs[_menu.Count] = Battle.NameLine(u);
                    _menu.Add(($"Lv {u.Level}  EX {u.Exp:00}  ", () => { _editUnit = who; OpenTouchMenu(0, 4); }, default));
                }
                break;
            case 4 when _editUnit is { } eu:
                _menuGlyphs[_menu.Count] = Battle.NameLine(eu);
                _menu.Add(($"Lv {eu.Level}  EX {eu.Exp:00} — ", () => { }, default));
                foreach (int add in new[] { 10, 50, 100 })
                {
                    int e = add;
                    _menu.Add(($"경험치 +{e}{(e == 100 ? "  (레벨 업)" : "")}", () =>
                    {
                        var notes = _battle.GainExp(eu, e);
                        EditCommit(eu);
                        if (notes.Count == 0) { OpenTouchMenu(_menuIndex, 4); return; }   // 최대 레벨 등 — 오르지 않음
                        int row = _menuIndex;
                        _menu.Clear();
                        ShowNotices(notes, () => { _bs = BattleState.Menu; OpenTouchMenu(row, 4); });
                    }, default));
                }
                _menu.Add(($"아이템 넣기 — 회복약 · 장비  (가방 {eu.Bag.Count}/8)", () => OpenTouchMenu(0, 6), default));   // w-147
                _menu.Add(("돌아가기", () => OpenTouchMenu(0, 3), default));
                break;
            case 6 when _editUnit is { } iu && _tables != null:
                // 에디트 아이템 넣기(w-147) — 쓰는 물건(회복약 등) 먼저, 그다음 무기 · 옷. 가방이 차면 공통 가방으로(켜져 있으면)
                _menuGlyphs[_menu.Count] = Battle.NameLine(iu);
                _menu.Add(($"가방 {iu.Bag.Count}/8 — 넣을 물건", () => { }, default));
                foreach (int id in Enumerable.Range(0, _tables.Items.Count).Where(i => GameNames.Item(i).Length > 0 && !GameNames.Item(i).All(char.IsDigit)).OrderBy(i => _tables.Items[i].Kind < 32 ? 1 : 0).ThenBy(i => i))
                {
                    int item = id;
                    _menuGlyphs[_menu.Count] = 181 + item;
                    _menuIcons[_menu.Count] = ItemIcon(item);
                    _menu.Add(($"  {ItemEffect(item)}", () => { EditAddItem(iu, item); OpenTouchMenu(_menuIndex, 6); }, default));
                }
                _menu.Add(("돌아가기", () => OpenTouchMenu(0, 4), default));
                break;
            default:
                _menu.Add(("화면 표시", () => OpenTouchMenu(0, 1), default));
                _menu.Add(("설정", () => OpenTouchMenu(0, 2), default));
                if (!_touchFromTown) _menu.Add(("시스템 메뉴", OpenSystemMenu, default));   // 턴 끝 · 전황 기록은 싸움 것
                _menu.Add(("에디트 — 돈 · 경험치 · 아이템", () => OpenTouchMenu(0, 3), default));
                _menu.Add(("버그 리포트", () => { Close(); OpenBugReport(); }, default));
                _menu.Add(("타이틀로", () => OpenTouchMenu(1, 5), default));   // 메뉴에서 타이틀로 갈 길이 없었다(사용자 요청) — 한 번 묻는다
                break;
            case 5:
                // 타이틀로 — 기록하지 않은 진행은 사라진다고 알리고 묻는다
                _menu.Add(("기록하지 않은 진행은 사라집니다", () => { }, default));
                _menu.Add(("예 — 타이틀로", () =>
                {
                    _menu.Clear();
                    _touchFromTown = false;
                    _town = null;
                    ToTitle();
                }, default));
                _menu.Add(("아니오 — 메뉴로", () => OpenTouchMenu(0), default));
                break;
        }
        _menu.Add(("닫기", Close, default));   // 맨 아래 닫기(사용자 요청) — 밖을 누르거나 Esc 로도 닫힌다
        _menuIndex = Math.Clamp(index, 0, _menu.Count - 1);
        _bs = BattleState.Menu;
    }

    // ── 폰: 대화 · 프롤로그 중에도 오른쪽 위 「메뉴」 단추 — 대사 건너뛰기 · 타이틀로 · 게임 끝내기(사용자 요청) ─────────────

    private bool _talkMenuOpen;
    private (int X, int Y, int W, int H) _talkMenuIcon;
    private readonly List<(int X, int Y, int W, int H)> _talkMenuRows = new();

    /// <summary>대화 메뉴 단추를 보이는 때 — 폰에서 대사(전투 · 마을)나 프롤로그가 나오는 중.</summary>
    private bool TalkMenuShown => TouchUi && (_screen == Screen.Prologue || _screen == Screen.Game && Talking) && !_partyOpen && !_volumeOpen && !_keysOpen;

    private (string Label, Action Do)[] TalkMenuItems =>
    [
        ("대사 건너뛰기", () => { if (_screen == Screen.Prologue) SkipPrologue(); else EndTalk(); }),
        ("타이틀로", ToTitle),
        ("게임 끝내기", () => _running = false),
        ("닫기", () => { }),
    ];

    private void DrawTalkMenu()
    {
        _talkMenuIcon = default;
        _talkMenuRows.Clear();
        if (!TalkMenuShown) { _talkMenuOpen = false; return; }
        int fs = Math.Max(14, 7 * HudZoom);
        _talkMenuIcon = DrawHudButton("메뉴", fs, _clientW - 14, 10);
        if (!_talkMenuOpen) return;
        var items = TalkMenuItems;
        int big = fs * 4 / 3;
        var imgs = items.Select(i => Text.Render(i.Label, big, 0xFFFFFFFF)).ToList();
        int rowH = imgs.Max(i => i.H) + big / 2, w = imgs.Max(i => i.W) + big * 2, h = rowH * items.Length + 16;
        int x = (_clientW - w) / 2, y = (_clientH - h) / 2;
        FillRect(0, 0, _clientW, _clientH, 0x80000000);
        FillRect(x, y, w, h, 0xF0182040);
        FillRect(x, y, w, 2, 0xFF8090C0); FillRect(x, y + h - 2, w, 2, 0xFF8090C0);
        FillRect(x, y, 2, h, 0xFF8090C0); FillRect(x + w - 2, y, 2, h, 0xFF8090C0);
        for (int i = 0; i < items.Length; i++)
        {
            var r = (X: x + 8, Y: y + 8 + i * rowH, W: w - 16, H: rowH - 4);
            FillRect(r.X, r.Y, r.W, r.H, 0xC0304070);
            DrawText(imgs[i], x + (w - imgs[i].W) / 2, r.Y + (r.H - imgs[i].H) / 2);
            _talkMenuRows.Add(r);
        }
    }

    /// <summary>대화 메뉴 단추 · 줄을 눌렀으면 처리하고 true. 메뉴가 떠 있을 때 밖을 누르면 닫는다(대사는 넘기지 않는다).</summary>
    private bool TalkMenuClick(int mx, int my)
    {
        if (!TalkMenuShown) return false;
        static bool In((int X, int Y, int W, int H) r, int x, int y) => r.W > 0 && x >= r.X - 4 && y >= r.Y - 4 && x < r.X + r.W + 4 && y < r.Y + r.H + 4;
        if (!_talkMenuOpen)
        {
            if (!In(_talkMenuIcon, mx, my)) return false;
            _talkMenuOpen = true;
            PlaySfx(Sound.UiBank, Sound.Menu);
            return true;
        }
        _talkMenuOpen = false;
        for (int i = 0; i < _talkMenuRows.Count; i++)
            if (In(_talkMenuRows[i], mx, my)) { TalkMenuItems[i].Do(); break; }
        return true;
    }
}

/// <summary>
/// 지도 아래 빈 곳의 안 움직인 아군(모드 「지도 아래 남은 아군」, 기본 켬, w-166) — 지도를 줄여 아래가 까맣게 비면
/// 그 띠에 아직 안 움직인 우리 편을 작은 카드(지도 그림 · 원작 글꼴 이름 · 아래 얇은 체력 막대)로 한 줄에 넷까지 보인다.
/// 더 있으면 좌우로 끌어 넘긴다(fb-92 — 예전엔 큰 카드라 한 명만 보였다). 누르면 커서가 그 사람에게.
/// 띠가 카드 하나보다 낮으면 그리지 않는다. 우리 차례에 고르는 동안만.
/// </summary>
internal sealed partial class GameWindow
{
    private readonly List<((int X, int Y, int W, int H) Rect, Unit Who)> _readyBelow = new();

    /// <summary>한 번에 보이는 카드 수.</summary>
    private const int ReadyPage = 4;

    /// <summary>맨 왼쪽에 보이는 카드 번호 · 띠 자리(누르기 받는 곳) · 카드 한 칸 폭(사이 포함) · 남은 아군 수.</summary>
    private int _readyFirst, _readyStep = 1, _readyCount;
    private (int X, int Y, int W, int H) _readyArea;

    /// <summary>띠를 누른 자리와 그때의 맨 왼쪽 카드 · 끌었나.</summary>
    private (int X, int Y, int First)? _readyPress;
    private bool _readyDragged;

    /// <summary>
    /// 폰 — 지도 아래 자리가 없어 띠가 안 나올 때, 빨리 위로 쓸면 화면 아래에 덮어 띄운다(아래로 쓸면 닫기, w-178).
    /// _readyFits 는 이번 틀에 띠가 지도 아래 제자리에 들었나(그러면 쓸어올리기는 그냥 지도 밀기).
    /// </summary>
    private bool _readyPeek, _readyFits;

    private bool ReadyShowable => _settings.ReadyBelowMap && InBattle && !InTownView && !_enemyPhase && _bs is BattleState.Select or BattleState.Target;

    /// <summary>손을 뗐다 — 짧고 빠르게 세로로 쓸었으면 덮은 띠를 열거나(위로) 닫는다(아래로). 그랬으면 지도는 누르기 전 자리로.</summary>
    private bool ReadyFlick((int X, int Y) from, int x, int y)
    {
        if (!TouchUi || _screen != Screen.Game || !ReadyShowable || _time - _pressAt > 0.35) return false;
        int up = from.Y - y, side = Math.Abs(x - from.X);
        if (Math.Abs(up) < Math.Max(DragSlop * 4, _clientH / 12) || side * 2 > Math.Abs(up)) return false;
        bool open = up > 0;
        if (open == _readyPeek || open && _readyFits) return false;   // 이미 그렇다 · 띠가 제자리에 보인다 — 지도 밀기 그대로
        _readyPeek = open;
        (_camX, _camY) = _pressCam;
        PlaySfx(Sound.UiBank, Sound.Cursor);
        return true;
    }

    private void DrawReadyBelow(int fs)
    {
        _readyBelow.Clear();
        _readyArea = default;
        _readyFits = false;
        if (!ReadyShowable) { _readyPeek = false; return; }
        // 지도 아래 끝(화면 점) — 그 밑이 빈 띠
        // 폰에서 지도를 끝 너머로 더 밀 수 있는 몫(CamOver)은 빼고 잰다 — 밀거나 쓸어올리는 동안 띠가 나왔다 들어갔다 깜빡이지 않게(fb-99)
        int mapH = _scene.Height * T;
        int camY = mapH > _boardH ? Math.Min(_camY, mapH - _boardH) : _camY;
        int mapBottom = _offY + (mapH - camY) * _zoom;
        int top = Math.Max(0, mapBottom) + 4, band = _clientH - top - 4;
        var ready = Controlled.Where(u => u.Hp > 0 && Ready(u)).OrderBy(u => u.Slot).ToList();
        _readyCount = ready.Count;
        if (ready.Count == 0) return;
        int small = Math.Max(11, fs * 3 / 4), pad = Math.Max(3, fs / 4), gap = Math.Max(4, fs / 3);
        int left = 8, stripW = _clientW - 16;
        var title = Text.Render($"남은 아군 {ready.Count}", small, 0xFFA8C8FF);
        int cw = (stripW - gap * (ReadyPage - 1)) / ReadyPage, bar = Math.Max(3, fs / 5);
        int g = Math.Max(1, small / Font16.Size);
        int nameH = NameImage(Battle.NameLine(ready[0]), g, 0xFFFFFFFF)?.H ?? Text.Render("가", small, 0xFFFFFFFF).H;
        int cardsTop = top + title.H + 2;
        // 그림 한 변 — 카드 폭과 띠 높이 둘 다에 들게, 지도 칸 셋을 넘지 않게
        int icon = Math.Min(Math.Min(cw - 2 * pad, _clientH - 4 - cardsTop - (pad * 2 + 2 + nameH + 2 + bar)), T * 3 * Math.Max(1, _zoom) / 2);
        if (icon < T)   // 띠가 너무 낮다 — 쓸어올렸으면 화면 아래에 덮어서
        {
            if (!_readyPeek) return;
            icon = Math.Max(T, Math.Min(cw - 2 * pad, _clientH / 7));
            top = _clientH - 8 - (pad + icon + 2 + nameH + 2 + bar + pad) - title.H - 2;
            cardsTop = top + title.H + 2;
            // 띠 바탕은 깔지 않는다 — 밑의 지도가 보이게(fb-98). 글자만 얇은 그림자 상자로 읽히게
            var shut = Text.Render("아래로 쓸면 닫기", Math.Max(10, small * 4 / 5), 0xFFB8C4E8, bold: false);   // 띠 위 오른쪽 — 제목 줄의 「밀어서 넘기기」와 안 겹치게
            FillRect(_clientW - 12 - shut.W, top - 8 - shut.H, shut.W + 8, shut.H + 4, 0x90000000);
            DrawText(shut, _clientW - 8 - shut.W, top - 6 - shut.H);
        }
        else { _readyFits = true; _readyPeek = false; }
        bool overMap = !_readyFits;
        int ch = pad + icon + 2 + nameH + 2 + bar + pad;
        _readyStep = cw + gap;
        _readyFirst = Math.Clamp(_readyFirst, 0, Math.Max(0, ready.Count - ReadyPage));
        _readyArea = (left, top, stripW, cardsTop + ch - top);
        if (overMap) FillRect(left - 4, top - 2, title.W + 8, title.H + 4, 0x90000000);
        DrawText(title, left, top);
        // 더 있으면 제목 옆에 「◀ 1~4 / 6 ▶」 — 끌어서 넘긴다는 표시(단추 아님)
        if (ready.Count > ReadyPage)
        {
            var more = Text.Render($"{(_readyFirst > 0 ? "◀ " : "")}{_readyFirst + 1}~{Math.Min(ready.Count, _readyFirst + ReadyPage)} / {ready.Count}{(_readyFirst + ReadyPage < ready.Count ? " ▶" : "")}  밀어서 넘기기",
                                     Math.Max(10, small * 4 / 5), overMap ? 0xFFB8C4E8 : 0xFF8090C0, bold: false);
            int mx = left + title.W + gap * 2, my = top + (title.H - more.H) / 2;
            if (overMap) FillRect(mx - 4, my - 2, more.W + 8, more.H + 4, 0x90000000);
            DrawText(more, mx, my);
        }
        for (int i = 0; i < ReadyPage && _readyFirst + i < ready.Count; i++)
        {
            var u = ready[_readyFirst + i];
            int x = left + i * _readyStep, y = cardsTop;
            bool here = _cursor == (u.X, u.Y);
            FillRect(x, y, cw, ch, overMap ? (here ? 0x80344480u : 0x50101020u) : here ? 0xC0344480u : 0xA0101020u);   // 지도 위에 덮을 때는 더 옅게(fb-98)
            uint edge = here ? 0xFFFFE070 : 0x80FFFFFF;
            FillRect(x, y, cw, 1, edge); FillRect(x, y + ch - 1, cw, 1, edge);
            FillRect(x, y, 1, ch, edge); FillRect(x + cw - 1, y, 1, ch, edge);
            int ix = x + (cw - icon) / 2, iy = y + pad;
            var hi = HiIcon(u.Icon, (int)Dir.Down * 3);
            if (hi != null) BlitFit(hi, T * 4, T * 4, ix, iy, icon, icon);
            else BlitFit(_icons.Frame(u.Icon, (int)Dir.Down * 3), T, T, ix, iy, icon, icon);
            // 이름 — 카드 폭에 안 들면 작게
            var name = NameImage(Battle.NameLine(u), g, 0xFFFFFFFF);
            if (name == null || name.W > cw - 2 * pad)
            {
                string n = CharacterNames.Of(u.Form >= 0 ? u.Form : u.Icon);
                name = Text.Render(n, small, 0xFFFFFFFF);
                if (name.W > cw - 2 * pad) name = Text.Render(n, Math.Max(9, small * (cw - 2 * pad) / Math.Max(1, name.W)), 0xFFFFFFFF);
            }
            DrawText(name, x + (cw - name.W) / 2, iy + icon + 2 + (nameH - name.H) / 2);
            // 체력 막대 — 숫자 대신(많이 깎이면 노랑 · 빨강)
            int bx = x + pad, bw = cw - 2 * pad, by = y + ch - pad - bar;
            FillRect(bx, by, bw, bar, 0xFF401010);
            int fill = u.MaxHp > 0 ? Math.Max(1, bw * u.Hp / u.MaxHp) : 0;
            FillRect(bx, by, fill, bar, u.Hp * 3 < u.MaxHp ? 0xFFE04040 : u.Hp * 3 < u.MaxHp * 2 ? 0xFFF0C030 : 0xFF58C040);
            _readyBelow.Add(((x, y, cw, ch), u));
        }
    }

    /// <summary>띠를 눌렀다 — 끌면 넘기고, 안 끌고 떼면 그 카드를 고른다(<see cref="ReadyRelease"/>).</summary>
    private bool ReadyPress(int x, int y)
    {
        if (_screen != Screen.Game || !ReadyShowable || !InRect(_readyArea, x, y)) return false;
        (_readyPress, _readyDragged) = ((x, y, _readyFirst), false);
        return true;
    }

    private bool ReadyMove(int x, int y)
    {
        if (_readyPress is not { } p) return false;
        if (!_readyDragged && Math.Abs(x - p.X) + Math.Abs(y - p.Y) < DragSlop) return true;
        _readyDragged = true;
        _readyFirst = Math.Clamp(p.First - (int)Math.Round((x - p.X) / (double)Math.Max(1, _readyStep)), 0, Math.Max(0, _readyCount - ReadyPage));
        return true;
    }

    private bool ReadyRelease(int x, int y)
    {
        if (_readyPress is not { } p) return false;
        _readyPress = null;
        if (_readyPeek && y - p.Y > Math.Max(DragSlop * 4, _clientH / 12) && Math.Abs(x - p.X) * 2 < y - p.Y) { _readyPeek = false; _readyFirst = p.First; return true; }   // 띠를 아래로 쓸어 닫기
        if (_readyDragged) return true;
        foreach (var (r, who) in _readyBelow)
            if (InRect(r, p.X, p.Y) && who.Hp > 0)
            {
                // 커서가 이미 그 사람에게 있으면(한 번 더 누름) 지도에서 누른 것처럼 골라 이동 범위를 띄운다(w-180)
                if (_cursor == (who.X, who.Y) && _bs == BattleState.Select && who != _actor && Ready(who))
                {
                    _selected = who; BeginMove(who);
                    _readyPeek = false;   // 덮어 띄운 띠는 닫아 범위가 보이게
                    _centerCursor = true; _mouseMoved = false;
                    PlaySfx(Sound.UiBank, Sound.Cursor);
                    break;
                }
                _cursor = (who.X, who.Y); _centerCursor = true; _mouseMoved = false;
                PlaySfx(Sound.UiBank, Sound.Cursor);
                break;
            }
        return true;
    }
}
