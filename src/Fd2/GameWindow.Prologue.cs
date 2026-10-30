using Fd2.Data;

namespace Fd2;

/// <summary>
/// 프롤로그 — 게임 시작을 누르면 1장 앞에 원작의 프롤로그(FD2.EXE sub_3231B)를 그 장면 지도 위에서 튼다.
/// 1막 장면 32 위쪽 알현실(사울이 입구에서 옥좌 앞까지) → 2막 장면 32 아래 풀밭(아래스가 졸던 사울을 찾아옴)
/// → 3막 장면 31 숲(쓰러진 유니와 카일을 만남). 대사의 말하는 사람은 그 장면 유닛 표 차례 번호(FFEC · FFED).
/// 걷는 길은 원작의 이동 대본 표(0x627D8, <see cref="GameTables.MoveScript"/>)를 그대로 돌린다 — 대본의 유닛 번호 = 그 장면에서 세운 차례
/// = 유닛 표 차례(프롤로그 장면은 우리 편 자리가 0 이다). 대사 중에는 말하는 사람을 화면 가운데로.
/// Enter · Space · 방향키 · 클릭 = 다음(걷는 중이면 대본 끝 자리로 바로), Esc = 프롤로그 건너뛰기.
/// </summary>
internal sealed partial class GameWindow
{
    private enum CueKind { Stage, Camera, Script, Dark, Up, Talk, Music, Spawn, Remove }

    /// <summary>
    /// 연출 한 마디(sub_3231B 의 부르는 차례 그대로) — Stage 장면 세우기, Camera 화면 왼쪽 위 칸(sub_135DD), Script · Dark 이동 대본(Dark 는 n64 = 1 로
    /// 어두워지며), Up 유닛 A 를 B 칸 위로(sub_13185, 화면이 따라감), Talk 대사 묶음, Music 곡(-1 끔), Spawn 등장 차례(sub_10B4E), Remove 유닛 지우기(sub_32975).
    /// </summary>
    private sealed record Cue(CueKind Kind, int A = 0, int B = 0, Action? Do = null);

    /// <summary>화면이 볼 칸 — 있으면 고른 사람 대신 여기를 가운데로(Follow).</summary>
    private (int X, int Y)? _lookAt;

    /// <summary>
    /// 이 막이 쓰는 지도 줄 [위, 아래) — 장면 32 는 위쪽(0 ~ 42)이 알현실, 아래(43 ~ 50)가 풀밭인 한 장이라,
    /// 원작의 좁은 화면(320x200)에서는 서로 보이지 않는다. 넓은 창에서도 그 막의 줄만 보이게 화면을 그 안에 두고 바깥은 가린다.
    /// </summary>
    private (int Top, int Bottom)? _viewRows;

    private const int ThroneScene = 32, ForestScene = 31;

    private List<Cue>? _cues;
    private int _cue = -1;

    /// <summary>프롤로그에 세운 사람 — 장면 유닛 표 차례 번호 → 사람.</summary>
    private readonly Dictionary<int, Unit> _cast = new();

    // 돌고 있는 이동 대본 — 걸음들, 지금 걸음, 그 걸음에서 남은 칸, 쉬는 남은 시간
    private List<MoveStep>? _script;
    private int _scriptStep, _scriptTiles;
    private double _scriptWait;

    /// <summary>화면 어둡기 0 ~ 1 — 어두워지는 대본(n64 = 1) 동안 차오르고, 다음 막을 세울 때 걷힌다.</summary>
    private double _fade;
    private bool _fading;

    /// <summary>지금 지도를 그리나 — 게임 화면과 프롤로그.</summary>
    private bool MapShown => _screen is Screen.Game or Screen.Prologue;

    /// <summary>틀고 싶은 곡(-1 없음) — 배경음악을 껐다 켜면 이 곡부터.</summary>
    private int _musicWanted = -1;

    /// <summary>
    /// 1막 곡 — 원작은 1막에서 곡을 바꾸지 않고(앞 화면의 곡이 이어진다) 2막 앞에서 끈 뒤 곡 11 을 튼다.
    /// 앞 화면 곡을 몰라 1막은 호른 · 피콜로의 궁정 곡 10 으로 둔다(짐작). 2막 · 3막의 11 은 원작 그대로다.
    /// </summary>
    private const int ThroneMusic = 10, FieldMusic = 11;

    /// <summary>대본 「쉬기」 한 그림의 시간 — 원작 화면의 한 틀을 1/18 초로 본 짐작.</summary>
    private const double ScriptFrame = 1 / 18.0;

    /// <summary>배경음악 바꾸기 — 설정에서 껐거나 화면 밖 시험이면 소리는 내지 않는다.</summary>
    private void SetMusic(int index)
    {
        _musicWanted = index;
        if (index < 0 || !_settings.Music || Offscreen) StopMusic();
        else PlayMusic(index);
    }

    /// <summary>원작 sub_3231B 의 차례 그대로(분석-장면 6.1).</summary>
    private List<Cue> Cues()
    {
        var c = new List<Cue>
        {
            // 1막 — 알현실: 화면 (3,34), 대본 99 = 사울 위로 6 → (8,36), 화면과 함께 15칸 위 (8,21) 대사 0, 13칸 더 (8,8) 옥좌 앞 대사 1
            new(CueKind.Music, ThroneMusic),
            new(CueKind.Stage, Do: StageThrone),
            new(CueKind.Camera, 3, 34),
            new(CueKind.Script, 99),
            new(CueKind.Up, 2, 15),
            new(CueKind.Talk, ThroneScene, 0),
            new(CueKind.Up, 2, 13),
            new(CueKind.Talk, ThroneScene, 1),
            new(CueKind.Music, -1),
            new(CueKind.Dark, 100),                               // 사울 아래로 10 — 어두워지며 퇴장
            // 2막 — 같은 장면 아래 풀밭: 아래스 왼쪽 3 → (10,47) 대사 2, 왼 2 · 위 1 · 왼 1 → (7,46) 대사 3, 서로 보며 대사 4 · 5
            new(CueKind.Stage, Do: StageField),
            new(CueKind.Camera, 0, 43),
            new(CueKind.Music, FieldMusic),
            new(CueKind.Script, 101),
            new(CueKind.Talk, ThroneScene, 2),
            new(CueKind.Script, 102),
            new(CueKind.Talk, ThroneScene, 3),
            new(CueKind.Script, 103),
            new(CueKind.Talk, ThroneScene, 4),
            new(CueKind.Script, 104),
            new(CueKind.Talk, ThroneScene, 5),
            new(CueKind.Dark, 105),                               // 오른쪽으로 퇴장
            // 3막 — 숲: 사울 · 아래스(등장 1)가 오른쪽 아래에서 들어와 이야기, 등장 3(누운 유니 · 카일), 대사 4 뒤 누운 유니를 지우고 선 유니(등장 5)
            new(CueKind.Stage, Do: StageForest),
            new(CueKind.Camera, 5, 42),
            new(CueKind.Spawn, 1),
            new(CueKind.Script, 90),
            new(CueKind.Talk, ForestScene, 0),
            new(CueKind.Script, 91),
            new(CueKind.Talk, ForestScene, 1),
            new(CueKind.Script, 92),
            new(CueKind.Talk, ForestScene, 2),
            new(CueKind.Spawn, 3),
            new(CueKind.Camera, 4, 41),
            new(CueKind.Talk, ForestScene, 3),
            new(CueKind.Script, 93),
            new(CueKind.Talk, ForestScene, 4),
            new(CueKind.Remove, 2),
            new(CueKind.Spawn, 5),
            new(CueKind.Talk, ForestScene, 5),
        };
        for (int k = 0; k < 4; k++)
        {
            c.Add(new(CueKind.Script, 94 + k));
            c.Add(new(CueKind.Talk, ForestScene, 6 + k));
        }
        c.Add(new(CueKind.Music, -1));
        c.Add(new(CueKind.Dark, 98));                             // 넷이 오른쪽 아래로 — 어두워지며 1장으로
        return c;
    }

    private void StartGame()
    {
        NewCampaign();
        // 프롤로그 장면이 없는 판(장면이 33개보다 적은)이나 이동 대본을 읽을 FD2.EXE 가 없으면 바로 1장
        if (_sceneCount <= ThroneScene || _tables == null) { ToChapter1(); return; }
        _cues = Cues();
        _cue = -1;
        _screen = Screen.Prologue;
        NextCue();
    }

    /// <summary>다음 마디들 — 바로 끝나는 것(세우기 · 화면 · 곡 · 등장 · 지우기)은 이어서, 걷기 · 대사는 시작하고 끝날 때까지 기다린다.</summary>
    private void NextCue()
    {
        while (_cues != null)
        {
            if (++_cue >= _cues.Count) { ToChapter1(); return; }
            var c = _cues[_cue];
            switch (c.Kind)
            {
                case CueKind.Stage:
                    c.Do?.Invoke();
                    _fade = 0;
                    _fading = false;
                    break;
                case CueKind.Camera:
                    _lookAt = (c.A + 6, c.B + 4);   // 원작 화면(13 x 8 칸)의 왼쪽 위 → 그 가운데
                    _selected = null;
                    Follow();
                    break;
                case CueKind.Music:
                    SetMusic(c.A);
                    break;
                case CueKind.Spawn:
                    for (int r = 0; r < _scene.Roster.Count; r++)
                        if (_scene.Roster[r].Phase == c.A) PutCast(r);
                    break;
                case CueKind.Remove:
                    if (_cast.Remove(c.A, out var gone)) _scene.Units.Remove(gone);
                    break;
                case CueKind.Script or CueKind.Dark:
                    _fading = c.Kind == CueKind.Dark;
                    StartScript(_tables!.MoveScript(c.A));
                    return;
                case CueKind.Up:
                    if (_cast.TryGetValue(c.A, out var who)) { _selected = who; _lookAt = null; }   // 화면이 그 사람을 따라 올라간다
                    StartScript([new MoveStep(c.B, false, [(c.A, Dir.Up)])]);
                    return;
                case CueKind.Talk:
                    StartTalk(c.A, [c.B], NextCue);
                    ProloguePage();
                    return;
            }
        }
    }

    private void StartScript(List<MoveStep> steps)
    {
        _script = steps;
        _scriptStep = -1;
        _scriptTiles = 0;
        _scriptWait = 0;
        NextScriptStep();
    }

    /// <summary>대본의 다음 걸음 — 걷기면 모두 그 쪽을 보고 칸 수를 잡고, 쉬기면 그 쪽을 보고 시간을 잡는다. 다 끝나면 다음 마디.</summary>
    private void NextScriptStep()
    {
        if (_script == null) return;
        if (++_scriptStep >= _script.Count) { _script = null; NextCue(); return; }
        var step = _script[_scriptStep];
        foreach (var (unit, dir) in step.Units)
            if (_cast.TryGetValue(unit, out var u)) u.Facing = dir;
        if (step.Wait) { _scriptTiles = 0; _scriptWait = step.Count * ScriptFrame; }
        else { _scriptTiles = step.Count; _scriptWait = 0; }
    }

    /// <summary>
    /// 대본을 한 틀 나아간다 — 걷기는 모두 함께 한 칸씩(다른 사람 · 지형에 막히지 않는다, 원작 대본처럼), 다 걸으면 다음 걸음.
    /// </summary>
    private void ScriptTick(double dt)
    {
        if (_script == null || _scriptStep >= _script.Count) return;
        var step = _script[_scriptStep];
        if (_fading) _fade = Math.Min(1, _fade + dt * 1.2);
        if (step.Wait)
        {
            if ((_scriptWait -= dt) <= 0) NextScriptStep();
            return;
        }
        var walkers = step.Units.Select(p => _cast.GetValueOrDefault(p.Unit)).OfType<Unit>().ToList();
        if (walkers.Any(u => u.Moving)) return;
        if (_scriptTiles <= 0) { NextScriptStep(); return; }
        _scriptTiles--;
        foreach (var (unit, dir) in step.Units)
        {
            if (!_cast.TryGetValue(unit, out var u)) continue;
            var (dx, dy) = Delta(dir);
            (u.FromX, u.FromY) = (u.X, u.Y);
            u.X += dx;
            u.Y += dy;
            u.Facing = dir;
            u.Step = 0;
        }
    }

    private static (int Dx, int Dy) Delta(Dir d) => d switch { Dir.Down => (0, 1), Dir.Left => (-1, 0), Dir.Up => (0, -1), _ => (1, 0) };

    /// <summary>걷는 중인 대본을 끝 자리로 바로 — 남은 칸 · 걸음을 모두 그 자리에 놓는다.</summary>
    private void FinishScript()
    {
        if (_script == null) return;
        for (int s = Math.Max(0, _scriptStep); s < _script.Count; s++)
        {
            var step = _script[s];
            int tiles = s == _scriptStep ? _scriptTiles : step.Wait ? 0 : step.Count;
            foreach (var (unit, dir) in step.Units)
            {
                if (!_cast.TryGetValue(unit, out var u)) continue;
                var (dx, dy) = Delta(dir);
                u.X += dx * tiles;
                u.Y += dy * tiles;
                u.Facing = dir;
            }
        }
        foreach (var u in _cast.Values) { (u.FromX, u.FromY) = (u.X, u.Y); u.Step = 1; }
        if (_fading) _fade = 1;
        _script = null;
        NextCue();
    }

    private bool CastWalking => _script != null;

    /// <summary>대사 쪽이 바뀔 때 — 말하는 사람이 지도에 있으면 그 사람을 가운데로.</summary>
    private void ProloguePage()
    {
        if (!Talking) return;
        int who = _talk[_talkPage].Speaker;
        if (_cast.Values.FirstOrDefault(u => u.Icon == who) is { } speaker) { _selected = speaker; _lookAt = null; }
    }

    private void PrologueTick(double dt)
    {
        MoveUnits(dt);
        ScriptTick(dt);
        Follow();
        if (!CastWalking) TalkTick(dt);
    }

    /// <summary>Enter · 클릭 — 걷는 중이면 대본 끝 자리로 바로, 아니면 대사 다음.</summary>
    private void PrologueNext()
    {
        if (CastWalking) { FinishScript(); return; }
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
        _cues = null;
        _script = null;
        _cast.Clear();
        _lookAt = null;
        _viewRows = null;
        _fade = 0;
        _fading = false;
        _screen = Screen.Game;
        _campaign ??= Campaign.NewGame(_battle);
        _campaign.NextScene = 0;
        PlayCampaign();
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
        int icon = sprite ?? CharacterNames.SpriteOf(e.Icon, _icons.RoyalsMoved);
        if (icon >= _icons.Characters) return null;
        var u = new Unit { Icon = icon, Side = side, Facing = facing, X = e.X, Y = e.Y };
        (u.FromX, u.FromY) = (u.X, u.Y);
        _scene.Units.Add(u);
        _cast[r] = u;
        return u;
    }

    /// <summary>
    /// 등장 차례로 나오는 사람 — 누운 유니(그림 75)는 그 그림이 없는 판(모션패치 — 75 자리를 왕비로 바꿨다)이면 선 유니(9)의 옆모습을 눕혀 그린다.
    /// </summary>
    private void PutCast(int r)
    {
        if (_scene.Roster[r].Icon == 75 && _icons.RoyalsMoved)
        {
            if (Put(r, Dir.Left, sprite: 9) is { } yuni) yuni.Lying = true;
            return;
        }
        Put(r, Dir.Down);
    }

    /// <summary>1막 — 왕(0) · 왕비(1)는 옥좌, 병사들(5 ~ 20)은 붉은 길 양쪽에서 길을 보고, 사울(2)은 입구에서 위를 본다.</summary>
    private void StageThrone()
    {
        StageScene(ThroneScene);
        _viewRows = (0, 43);
        for (int r = 5; r < _scene.Roster.Count; r++)
        {
            if (_scene.Roster[r].Phase != 0 || Put(r, Dir.Down, side: Side.Other) is not { } guard) continue;
            guard.Facing = guard.X < _scene.Width / 2 ? Dir.Right : Dir.Left;
            _cast.Remove(r);   // 병사는 대본에 안 나온다
        }
        Put(0, Dir.Down);
        Put(1, Dir.Down);
        Put(2, Dir.Up);
    }

    /// <summary>2막 — 풀밭에서 졸던 사울(3), 오른쪽에서 걸어오는 아래스(4).</summary>
    private void StageField()
    {
        StageScene(ThroneScene);
        _viewRows = (43, _scene.Height);
        Put(3, Dir.Down);
        Put(4, Dir.Left);
    }

    /// <summary>3막 — 숲. 사람들은 등장 차례(1 사울 · 아래스, 3 누운 유니 · 카일, 5 선 유니)로 나온다.</summary>
    private void StageForest()
    {
        StageScene(ForestScene);
        _viewRows = null;
    }

    private void DrawPrologue()
    {
        if (_cpuMap) DrawMapCpu();
        DrawUnits(outline: false);
        if (_viewRows is ({ } top, { } bottom))
        {
            // 그 막의 줄 바깥은 검게
            int y0 = _offY + (top * T - _camY) * _zoom, y1 = _offY + (bottom * T - _camY) * _zoom;
            if (y0 > 0) FillRect(0, 0, _clientW, y0, 0xFF000000);
            if (y1 < _clientH) FillRect(0, y1, _clientW, _clientH - y1, 0xFF000000);
        }
        int fs = Math.Max(14, 7 * _zoom);
        var skip = Text.Render(TouchUi ? "누르면 다음 · 뒤로 건너뛰기" : "Enter 다음 · Esc 프롤로그 건너뛰기", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
        FillRect(_clientW - skip.W - 24, 6, skip.W + 16, skip.H + 8, 0x80000000);
        DrawText(skip, _clientW - skip.W - 16, 10);
        if (_fade > 0) FillRect(0, 0, _clientW, _clientH, (uint)(_fade * 255) << 24);   // 어두워지는 대본(n64)
        if (!CastWalking) DrawTalk();   // 걷는 동안에는 대사 창을 숨긴다
    }
}
