using Fd2.Data;

namespace Fd2;

/// <summary>
/// 프롤로그 — 게임 시작을 누르면 1장 앞에 원작의 프롤로그 대사(FDTXT 의 장면 33 · 32 몫)를 그 장면 지도 위에서 튼다.
/// 1막 장면 33 위쪽 알현실(왕 · 왕비, 사울이 입구에서 옥좌 앞까지 걸어감) → 2막 장면 33 아래 풀밭(아래스가 사울을 찾아옴)
/// → 3막 장면 32 숲(검술 연습 길에 쓰러진 유니와 카일을 만남). 대사의 말하는 사람은 그 장면 유닛 표 차례 번호(FFEC · FFED).
/// 사람들이 어디서 어디로 걷는지는 원작 실행 파일 안의 연출이라 자료에 없어, 대사에 맞춰 정한 것이다.
/// Enter · Space · 방향키 · 클릭 = 다음(걷는 중이면 바로 그 자리로), Esc = 프롤로그 건너뛰기.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>한 토막 — 그 장면의 대사 묶음 하나. 앞서 할 일(사람 세우기 · 방향) 뒤에 걸을 사람들이 다 걸으면 대사.</summary>
    private sealed record Beat(int Scene, int Msg, int Music, Action? Setup = null, (int Roster, int X, int Y)[]? Moves = null);

    private const int ThroneScene = 32, ForestScene = 31;

    private Beat[]? _beats;
    private int _beat = -1;

    /// <summary>프롤로그에 세운 사람 — 장면 유닛 표 차례 번호 → 사람.</summary>
    private readonly Dictionary<int, Unit> _cast = new();

    private bool _beatWaiting;

    /// <summary>지금 지도를 그리나 — 게임 화면과 프롤로그.</summary>
    private bool MapShown => _screen is Screen.Game or Screen.Prologue;

    /// <summary>틀고 싶은 곡(-1 없음) — 배경음악을 껐다 켜면 이 곡부터.</summary>
    private int _musicWanted = -1;

    /// <summary>
    /// 막마다 배경음악(FDMUS 곡 번호) — 원작이 장면마다 어느 곡을 쓰는지는 자료에서 찾지 못해 곡의 악기 · 분위기로 고른 것이다
    /// (10 호른 · 피콜로, 11 피아노 위주의 잔잔한 곡, 4 리코더 · 하모니카).
    /// </summary>
    private const int ThroneMusic = 10, FieldMusic = 11, ForestMusic = 4;

    /// <summary>배경음악 바꾸기 — 설정에서 껐거나 화면 밖 시험이면 소리는 내지 않는다.</summary>
    private void SetMusic(int index)
    {
        _musicWanted = index;
        if (index < 0 || !_settings.Music || Offscreen) StopMusic();
        else PlayMusic(index);
    }

    private Beat[] Beats() =>
    [
        // 1막 — 알현실. 사울이 입구(8,42)에서 옥좌 앞까지 걸어가 왕을 본다
        new(ThroneScene, 0, ThroneMusic, StageThrone, [(2, 8, 8)]),
        new(ThroneScene, 1, ThroneMusic),
        // 2막 — 같은 장면 아래 풀밭. 졸던 사울(4,46)에게 아래스가 걸어온다
        new(ThroneScene, 2, FieldMusic, StageField, [(4, 6, 46)]),
        new(ThroneScene, 3, FieldMusic, () => FaceCast(3, Dir.Right)),
        new(ThroneScene, 4, FieldMusic),
        new(ThroneScene, 5, FieldMusic),
        // 3막 — 숲. 사울 · 아래스가 오른쪽 끝에서 이야기하다, 왼쪽의 쓰러진 유니와 카일을 본다
        new(ForestScene, 0, ForestMusic, StageForest),
        new(ForestScene, 1, ForestMusic),
        new(ForestScene, 2, ForestMusic),
        new(ForestScene, 3, ForestMusic),
        new(ForestScene, 4, ForestMusic, null, [(0, 8, 44), (1, 8, 45)]),
        new(ForestScene, 5, ForestMusic),
        new(ForestScene, 6, ForestMusic, null, [(0, 6, 44)]),
        new(ForestScene, 7, ForestMusic, StandYuni),
        new(ForestScene, 8, ForestMusic),
        new(ForestScene, 9, ForestMusic),
        new(ForestScene, 10, ForestMusic),
    ];

    private void StartGame()
    {
        // 프롤로그 장면이 없는 판(장면이 33개보다 적은)은 바로 1장
        if (_sceneCount <= ThroneScene) { ToChapter1(); return; }
        _beats = Beats();
        _beat = -1;
        _screen = Screen.Prologue;
        NextBeat();
    }

    private void NextBeat()
    {
        if (_beats == null || ++_beat >= _beats.Length) { ToChapter1(); return; }
        var b = _beats[_beat];
        SetMusic(b.Music);
        b.Setup?.Invoke();
        foreach (var (r, x, y) in b.Moves ?? [])
            if (_cast.TryGetValue(r, out var u)) WalkTo(u, x, y);
        _beatWaiting = true;
    }

    /// <summary>지나갈 수 있는 칸으로 가장 짧게(나무 · 사람을 돌아서) — 길이 없으면 곧게 가다 막히면 선다.</summary>
    private void WalkTo(Unit u, int tx, int ty)
    {
        u.Path.Clear();
        var from = new Dictionary<(int, int), (int, int)> { [(u.X, u.Y)] = (u.X, u.Y) };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((u.X, u.Y));
        while (queue.Count > 0 && !from.ContainsKey((tx, ty)))
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            {
                var n = (x + dx, y + dy);
                if (from.ContainsKey(n) || !_scene.Free(n.Item1, n.Item2)) continue;
                from[n] = (x, y);
                queue.Enqueue(n);
            }
        }
        if (!from.ContainsKey((tx, ty)))
        {
            int x = u.X, y = u.Y;
            while (y != ty) { y += Math.Sign(ty - y); u.Path.Enqueue((x, y)); }
            while (x != tx) { x += Math.Sign(tx - x); u.Path.Enqueue((x, y)); }
            return;
        }
        var steps = new List<(int, int)>();
        for (var c = (tx, ty); c != (u.X, u.Y); c = from[c]) steps.Add(c);
        for (int i = steps.Count - 1; i >= 0; i--) u.Path.Enqueue(steps[i]);
    }

    private bool CastWalking => _cast.Values.Any(u => u.Moving || u.Path.Count > 0);

    private void PrologueTick(double dt)
    {
        MoveUnits(dt);
        Follow();
        if (_beatWaiting && !CastWalking && _beats != null)
        {
            _beatWaiting = false;
            var b = _beats[_beat];
            StartTalk(b.Scene, [b.Msg], NextBeat);
        }
        TalkTick(dt);
    }

    /// <summary>Enter · 클릭 — 걷는 중이면 다들 바로 그 자리로, 아니면 대사 다음.</summary>
    private void PrologueNext()
    {
        if (CastWalking)
        {
            foreach (var u in _cast.Values.Where(u => u.Moving || u.Path.Count > 0))
            {
                var steps = u.Path.ToList();
                var last = steps.Count > 0 ? steps[^1] : (u.X, u.Y);
                var before = steps.Count > 1 ? steps[^2] : (u.X, u.Y);
                if (last != before) u.Facing = DirOf(last.Item1 - before.Item1, last.Item2 - before.Item2);
                u.Path.Clear();
                (u.X, u.Y) = last;
                (u.FromX, u.FromY) = last;
                u.Step = 1;
            }
            return;
        }
        TalkNext();
    }

    private void SkipPrologue()
    {
        _talk.Clear();
        _afterTalk = null;
        ToChapter1();
    }

    private void ToChapter1()
    {
        SetMusic(-1);
        _beats = null;
        _cast.Clear();
        _beatWaiting = false;
        _screen = Screen.Game;
        StartBattle(0);
    }

    // ── 세우기 ───────────────────────────────────────────────────────────────

    /// <summary>프롤로그 장면을 띄우고 사람은 비운다 — 세울 사람만 Put 으로.</summary>
    private void StageScene(int scene)
    {
        if (_scene.Index != scene) LoadScene(scene);
        _scene.Units.Clear();
        _cast.Clear();
    }

    /// <summary>장면 유닛 표 r 번째 사람을 그 자리에 — sprite 를 주면 그 그림으로.</summary>
    private Unit? Put(int r, Dir facing, int? sprite = null, Side side = Side.Player)
    {
        if (r < 0 || r >= _scene.Roster.Count) return null;
        var e = _scene.Roster[r];
        int icon = sprite ?? CharacterNames.SpriteOf(e.Icon);
        if (icon >= _icons.Characters) return null;
        var u = new Unit { Icon = icon, Side = side, Facing = facing, X = e.X, Y = e.Y };
        (u.FromX, u.FromY) = (u.X, u.Y);
        _scene.Units.Add(u);
        _cast[r] = u;
        return u;
    }

    private void FaceCast(int r, Dir d)
    {
        if (_cast.TryGetValue(r, out var u)) u.Facing = d;
    }

    /// <summary>1막 — 왕(0) · 왕비(1)는 옥좌, 병사들(5 ~ 20)은 붉은 길 양쪽에서 길을 보고, 사울(2)은 입구에서 위를 본다.</summary>
    private void StageThrone()
    {
        StageScene(ThroneScene);
        for (int r = 5; r < _scene.Roster.Count; r++)
        {
            if (_scene.Roster[r].Phase != 0 || Put(r, Dir.Down, side: Side.Other) is not { } guard) continue;
            guard.Facing = guard.X < _scene.Width / 2 ? Dir.Right : Dir.Left;
            _cast.Remove(r);   // 병사는 걷지 않는다 — 걷는 사람 셈에서 뺀다
        }
        Put(0, Dir.Down);
        Put(1, Dir.Down);
        _selected = Put(2, Dir.Up);
        Follow();
    }

    /// <summary>2막 — 풀밭에서 졸던 사울(3), 오른쪽에서 걸어오는 아래스(4).</summary>
    private void StageField()
    {
        StageScene(ThroneScene);
        _selected = Put(3, Dir.Down);
        Put(4, Dir.Left);
        Follow();
    }

    /// <summary>3막 — 사울(0) · 아래스(1)는 오른쪽 끝, 왼쪽에 카일(3, 가이아)과 쓰러진 유니(2 — 선 유니 그림을 눕혀서).</summary>
    private void StageForest()
    {
        StageScene(ForestScene);
        _selected = Put(0, Dir.Left);
        Put(1, Dir.Left);
        Put(3, Dir.Down);
        if (Put(2, Dir.Left, sprite: 9) is { } yuni) yuni.Lying = true;
        Follow();
    }

    /// <summary>유니가 일어난다 — 누운 그림(2)을 선 유니(4)로.</summary>
    private void StandYuni()
    {
        if (_cast.Remove(2, out var lying)) _scene.Units.Remove(lying);
        Put(4, Dir.Right);
    }

    private void DrawPrologue()
    {
        if (_cpuMap) DrawMapCpu();
        DrawUnits(outline: false);
        int fs = Math.Max(14, 7 * _zoom);
        var skip = Text.Render(TouchUi ? "누르면 다음 · 뒤로 건너뛰기" : "Enter 다음 · Esc 프롤로그 건너뛰기", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
        FillRect(_clientW - skip.W - 24, 6, skip.W + 16, skip.H + 8, 0x80000000);
        DrawText(skip, _clientW - skip.W - 16, 10);
        DrawTalk();
    }
}
