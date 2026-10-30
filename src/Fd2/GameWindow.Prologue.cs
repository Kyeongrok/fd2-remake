using Fd2.Data;

namespace Fd2;

/// <summary>
/// 프롤로그 — 게임 시작을 누르면 1장 앞에 「왕 알현 → 아래스와 대화 → 쓰러진 유니」를 튼다.
/// 대사는 대사 파일(%LOCALAPPDATA%\Fd2\prologue.json, 없으면 이 PC 의 FD2Re 에서 만든다 — Prologue.cs)에서.
/// 대사 파일도 FD2Re 도 없으면 바로 1장.
/// 막마다 배경 지도(1막은 장면 33 알현실 — 사울이 입구에서 옥좌 앞까지 걸어간 뒤 대사)와 배경음악.
/// 지도가 없는 막은 어두운 바탕에 막 제목과 나오는 사람들의 얼굴(말하는 사람만 밝게). Enter · 클릭 다음, Esc 건너뛰기.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>쪽마다 그 쪽이 든 막 번호.</summary>
    private readonly List<int> _prologueActOf = new();
    private Prologue.Script? _prologue;
    private int _prologueAct = -1;

    /// <summary>이 막은 배경 지도 위에서 하나.</summary>
    private bool _prologueMap;

    /// <summary>사울이 옥좌 앞으로 걸어가는 중 — 다 걸으면 대사.</summary>
    private Unit? _prologueWalker;

    /// <summary>이번에 켠 동안 FD2Re 를 찾아봤나 — 없으면 다시 뒤지지 않는다.</summary>
    private bool _prologueLooked;

    /// <summary>지금 지도를 그리나 — 게임 화면, 또는 배경 지도가 있는 프롤로그 막.</summary>
    private bool MapShown => _screen == Screen.Game || _screen == Screen.Prologue && _prologueMap;

    /// <summary>틀고 싶은 곡(-1 없음) — 배경음악을 껐다 켜면 이 곡부터.</summary>
    private int _musicWanted = -1;

    /// <summary>배경음악 바꾸기 — 설정에서 껐거나 화면 밖 시험이면 소리는 내지 않는다.</summary>
    private void SetMusic(int index)
    {
        _musicWanted = index;
        if (index < 0 || !_settings.Music || Offscreen) StopMusic();
        else PlayMusic(index);
    }

    private void StartGame()
    {
        if (!_prologueLooked)
        {
            _prologueLooked = true;
            try { _prologue = Prologue.LoadOrBuild([_dir, AppContext.BaseDirectory]); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _prologue = null; }
        }
        if (_prologue is not { Acts.Count: > 0 } script) { ToChapter1(); return; }

        _talk.Clear();
        _prologueActOf.Clear();
        for (int a = 0; a < script.Acts.Count; a++)
            foreach (var line in script.Acts[a].Lines)
            {
                // 사울은 왼쪽, 상대는 오른쪽
                _talk.Add(new Dialogue.Page { Speaker = line.Face, Right = line.Face != 0, Name = line.Speaker, Text = line.Text });
                _prologueActOf.Add(a);
            }
        if (_talk.Count == 0) { ToChapter1(); return; }
        _talkPage = 0;
        _talkShown = 0;
        _afterTalk = ToChapter1;
        _prologueAct = -1;
        _screen = Screen.Prologue;
        ProloguePage();
    }

    /// <summary>쪽이 바뀔 때 — 막이 바뀌었으면 배경 · 음악을 그 막의 것으로.</summary>
    private void ProloguePage()
    {
        if (_prologue == null || _talkPage >= _prologueActOf.Count) return;
        int a = _prologueActOf[_talkPage];
        if (a == _prologueAct) return;
        _prologueAct = a;
        var act = _prologue.Acts[a];
        SetMusic(act.Music ?? -1);

        _prologueMap = false;
        _prologueWalker = null;
        int scene = act.Scene ?? -1;
        if (scene < 0 || scene >= _sceneCount) return;
        LoadScene(scene);
        _prologueMap = true;
        // 장면 정보에는 나중에 나오는 사람(입구의 사울 · 아래스 둘째 자리 같은)도 들어 있다 — 처음 자리의 우리 편만 둔다
        _scene.Units.RemoveAll(u => u.Side != Side.Player && u.Icon < 16);
        var saul = _scene.Units.Find(u => u.Icon == 0);
        _selected = saul;
        if (saul != null && act.WalkTo is [int tx, int ty])
        {
            // 위아래 먼저, 그다음 옆으로 — 옥좌 앞에 서면 위(왕)를 본다
            int x = saul.X, y = saul.Y;
            while (y != ty) { y += Math.Sign(ty - y); saul.Path.Enqueue((x, y)); }
            while (x != tx) { x += Math.Sign(tx - x); saul.Path.Enqueue((x, y)); }
            _prologueWalker = saul;
        }
        Follow();
    }

    private bool PrologueWalking => _prologueWalker is { } w && (w.Moving || w.Path.Count > 0);

    private void PrologueTick(double dt)
    {
        if (_prologueMap)
        {
            MoveUnits(dt);
            if (_prologueWalker != null && !PrologueWalking)
            {
                _prologueWalker.Facing = Dir.Up;
                _prologueWalker = null;
            }
            Follow();
        }
        if (!PrologueWalking) TalkTick(dt);
    }

    /// <summary>프롤로그에서 Enter · 클릭 — 걷는 중이면 옥좌 앞으로 바로 옮기고, 아니면 대사 다음.</summary>
    private void PrologueNext()
    {
        if (PrologueWalking && _prologueWalker is { } w)
        {
            var last = w.Path.LastOrDefault((w.X, w.Y));
            w.Path.Clear();
            (w.X, w.Y) = last;
            (w.FromX, w.FromY) = last;
            w.Step = 1;
            return;
        }
        TalkNext();
    }

    private void ToChapter1()
    {
        SetMusic(-1);
        _prologueMap = false;
        _prologueWalker = null;
        _screen = Screen.Game;
        StartBattle(0);
    }

    private void DrawPrologue()
    {
        if (_prologue == null || _prologueAct < 0) return;
        int fs = Math.Max(14, 7 * _zoom);
        var act = _prologue.Acts[_prologueAct];

        if (_prologueMap)
        {
            if (_cpuMap) DrawMapCpu();
            DrawUnits(outline: false);
            var t = Text.Render(act.Title, fs * 3 / 2, 0xFFFFE070);
            FillRect((_clientW - t.W) / 2 - 16, 8, t.W + 32, t.H + 8, 0xA0000000);
            DrawText(t, (_clientW - t.W) / 2, 12);
        }
        else DrawPrologueCast(act, fs);

        var skip = Text.Render(TouchUi ? "누르면 다음 · 뒤로 건너뛰기" : "Enter 다음 · Esc 프롤로그 건너뛰기", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
        FillRect(_clientW - skip.W - 24, 6, skip.W + 16, skip.H + 8, 0x80000000);
        DrawText(skip, _clientW - skip.W - 16, 10);
        if (!PrologueWalking) DrawTalk();
    }

    /// <summary>지도 없는 막 — 어두운 바탕, 막 제목, 그 막에 나오는 사람들의 얼굴(나온 차례로, 말하는 사람만 밝게).</summary>
    private void DrawPrologueCast(Prologue.Act act, int fs)
    {
        for (int y = 0; y < _fbH; y++)
        {
            uint k = (uint)(40 - 34 * y / Math.Max(1, _fbH));
            uint c = 0xFF000000 | k / 2 << 16 | k / 2 << 8 | k;
            Array.Fill(_fb, c, y * _fbW, _fbW);
        }
        if (!Talking) return;

        var title = Text.Render(act.Title, fs * 2, 0xFFFFE070);
        DrawText(title, (_clientW - title.W) / 2, _clientH / 12);
        FillRect((_clientW - title.W) / 2, _clientH / 12 + title.H + 6, title.W, 2, 0x80FFE070);

        var cast = act.Lines.Select(l => l.Face).Where(f => f >= 0).Distinct()
            .Select(f => (Face: f, Frames: Face(f))).Where(c => c.Frames.Count > 0).ToList();
        if (cast.Count == 0) return;
        int s = Math.Max(2, _zoom * 2);
        int w = cast.Max(c => c.Frames[0].W) * s, gap = w / 3;
        while (s > 1 && cast.Count * (w + gap) > _clientW - 40) { s--; w = cast.Max(c => c.Frames[0].W) * s; gap = w / 3; }
        int x = (_clientW - cast.Count * (w + gap) + gap) / 2, y0 = _clientH / 12 + title.H + 40;
        var page = _talk[_talkPage];
        bool talking = _talkShown < PageChars(page);
        foreach (var (face, frames) in cast)
        {
            bool now = face == page.Speaker;
            var f = frames[now && talking ? (int)(_time * 8) % frames.Count : 0];
            BlitFrame(f.Px, f.W, f.H, x, y0, s, false, 0, now ? 0 : 150, (0, 0, _clientW, _clientH));
            if (now) Frame(x - 4, y0 - 4, f.W * s + 8, f.H * s + 8, 2, 0xFFFFE070);
            x += w + gap;
        }
    }
}
