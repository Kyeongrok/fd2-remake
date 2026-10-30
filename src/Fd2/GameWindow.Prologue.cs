using Fd2.Data;

namespace Fd2;

/// <summary>
/// 프롤로그 — 게임 시작을 누르면 1장 앞에 「왕 알현 → 아래스와 대화 → 쓰러진 유니」를 튼다.
/// 대사는 대사 파일(%LOCALAPPDATA%\Fd2\prologue.json, 없으면 이 PC 의 FD2Re 에서 만든다 — Prologue.cs)에서.
/// 대사 파일도 FD2Re 도 없으면 바로 1장.
/// 화면: 어두운 바탕에 막 제목, 그 막에 나오는 사람들의 얼굴(말하는 사람만 밝게), 아래에 대사 창. Enter · 클릭 다음, Esc 건너뛰기.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>쪽마다 그 쪽이 든 막 번호.</summary>
    private readonly List<int> _prologueActOf = new();
    private Prologue.Script? _prologue;

    /// <summary>이번에 켠 동안 FD2Re 를 찾아봤나 — 없으면 다시 뒤지지 않는다.</summary>
    private bool _prologueLooked;

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
        _talkPage = 0;
        _talkShown = 0;
        _afterTalk = ToChapter1;
        _screen = Screen.Prologue;
    }

    private void ToChapter1()
    {
        _screen = Screen.Game;
        StartBattle(0);
    }

    private void DrawPrologue()
    {
        // 바탕 — 위는 남색, 아래로 갈수록 검게
        for (int y = 0; y < _fbH; y++)
        {
            uint k = (uint)(40 - 34 * y / Math.Max(1, _fbH));
            uint c = 0xFF000000 | k / 2 << 16 | k / 2 << 8 | k;
            Array.Fill(_fb, c, y * _fbW, _fbW);
        }
        if (!Talking || _prologue == null) return;

        int fs = Math.Max(14, 7 * _zoom);
        int act = _prologueActOf[_talkPage];
        var title = Text.Render(_prologue.Acts[act].Title, fs * 2, 0xFFFFE070);
        DrawText(title, (_clientW - title.W) / 2, _clientH / 12);
        FillRect((_clientW - title.W) / 2, _clientH / 12 + title.H + 6, title.W, 2, 0x80FFE070);

        // 이 막에 나오는 사람들의 얼굴 — 나온 차례로, 말하는 사람만 밝게
        var cast = _prologue.Acts[act].Lines.Select(l => l.Face).Where(f => f >= 0).Distinct()
            .Select(f => (Face: f, Frames: Face(f))).Where(c => c.Frames.Count > 0).ToList();
        if (cast.Count > 0)
        {
            int s = Math.Max(2, _zoom * 2);
            int w = cast.Max(c => c.Frames[0].W) * s, h = cast.Max(c => c.Frames[0].H) * s, gap = w / 3;
            while (s > 1 && cast.Count * (w + gap) > _clientW - 40) { s--; w = cast.Max(c => c.Frames[0].W) * s; h = cast.Max(c => c.Frames[0].H) * s; gap = w / 3; }
            int x = (_clientW - cast.Count * (w + gap) + gap) / 2, y = _clientH / 12 + title.H + 40;
            var page = _talk[_talkPage];
            bool talking = _talkShown < PageChars(page);
            foreach (var (face, frames) in cast)
            {
                bool now = face == page.Speaker;
                var f = frames[now && talking ? (int)(_time * 8) % frames.Count : 0];
                BlitFrame(f.Px, f.W, f.H, x, y, s, false, 0, now ? 0 : 150, (0, 0, _clientW, _clientH));
                if (now) Frame(x - 4, y - 4, f.W * s + 8, f.H * s + 8, 2, 0xFFFFE070);
                x += w + gap;
            }
        }

        var skip = Text.Render(TouchUi ? "누르면 다음 · 뒤로 건너뛰기" : "Enter 다음 · Esc 프롤로그 건너뛰기", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
        DrawText(skip, _clientW - skip.W - 16, 12);
        DrawTalk();
    }
}
