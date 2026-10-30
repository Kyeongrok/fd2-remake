using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 대사 — 지도 위 아래쪽에 대사 창: 왼쪽(오른쪽 화자는 오른쪽)에 얼굴(DATO, 말하는 동안 네 장을 돌린다), 이름, 원본 글꼴 글자를 한 자씩.
/// Enter · Space · 클릭 = 다음(글이 나오는 중이면 다 보이기), Esc = 대사 건너뛰기.
/// </summary>
internal sealed partial class GameWindow
{
    private const double TalkCharsPerSecond = 40;

    private Font16? _font;
    private readonly List<Dialogue.Page> _talk = new();
    private int _talkPage;
    private double _talkShown;
    private Action? _afterTalk;
    private readonly Dictionary<int, List<(uint[] Px, int W, int H)>> _faces = new();

    private bool Talking => _talk.Count > 0;

    /// <summary>장면 scene 의 대사 묶음 messages 를 차례로 보이고, 다 보면 after.</summary>
    private void StartTalk(int scene, IEnumerable<int> messages, Action after)
    {
        _talk.Clear();
        try
        {
            _font ??= new Font16(_dir);
            var all = Dialogue.Load(_dir, scene);
            foreach (int m in messages)
                if (m < all.Count) _talk.AddRange(all[m]);
        }
        catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { _talk.Clear(); }
        _talkPage = 0;
        _talkShown = 0;
        _afterTalk = after;
        if (!Talking) EndTalk();
        else FocusSpeaker();
    }

    /// <summary>말하는 사람이 지도에 있으면 커서를 그리로 — 화면이 그 사람을 비춘다.</summary>
    private void FocusSpeaker()
    {
        if (!Talking || _talk[_talkPage].Speaker < 0) return;
        if (_scene.Units.FirstOrDefault(u => u.Icon == _talk[_talkPage].Speaker && u.Alive) is { } who)
        {
            _cursor = (who.X, who.Y);
            _centerCursor = true;
        }
    }

    private void EndTalk()
    {
        _talk.Clear();
        var after = _afterTalk;
        _afterTalk = null;
        after?.Invoke();
    }

    private int PageChars(Dialogue.Page p) => p.Lines.Sum(l => l.Count);

    private void TalkTick(double dt)
    {
        if (Talking) _talkShown += dt * TalkCharsPerSecond;
    }

    /// <summary>다음 — 글이 아직 나오는 중이면 다 보이고, 다 보였으면 다음 쪽.</summary>
    private void TalkNext()
    {
        if (!Talking) return;
        if (_talkShown < PageChars(_talk[_talkPage])) { _talkShown = 9999; return; }
        if (++_talkPage >= _talk.Count) { EndTalk(); return; }
        _talkShown = 0;
        FocusSpeaker();
    }

    private bool TalkKey(int vk)
    {
        if (!Talking) return false;
        if (vk is Win32.VK_RETURN or Win32.VK_SPACE) TalkNext();
        else if (vk == Win32.VK_ESCAPE) EndTalk();
        return true;
    }

    private void DrawTalk()
    {
        if (!Talking) return;
        var page = _talk[_talkPage];
        int g = Math.Max(2, _zoom);                     // 글자 한 점의 크기(화면 점)
        int glyph = Font16.Size * g, lineH = glyph + g * 4;
        int faceS = Math.Max(2, _zoom);
        var faces = page.Speaker >= 0 ? Face(page.Speaker) : null;
        int faceW = faces is { Count: > 0 } ? faces[0].W * faceS : 0, faceH = faces is { Count: > 0 } ? faces[0].H * faceS : 0;

        int boxW = Math.Min(_clientW - 40, Math.Max(faceW + glyph * 16 + 80, _clientW * 2 / 3));
        int boxH = Math.Max(faceH + 40, lineH * 3 + 70);
        // 일행이 화면 아래쪽에 있으면 창을 위로 — 말하는 사람들을 가리지 않게
        var party = _scene.Units.Where(u => u.Side == Side.Player && u.Alive).ToList();
        double partyY = party.Count == 0 ? 0 : party.Average(u => _offY + (u.DrawY * T - _camY) * _zoom);
        int bx = (_clientW - boxW) / 2, by = partyY > _clientH / 2 ? 64 : _clientH - boxH - 24;
        FillRect(bx, by, boxW, boxH, 0xE0101828);
        FillRect(bx, by, boxW, 3, 0xFF8090C0);
        FillRect(bx, by + boxH - 3, boxW, 3, 0xFF8090C0);

        int textX = bx + 24, faceX = bx + 20;
        if (faces is { Count: > 0 })
        {
            if (page.Right) faceX = bx + boxW - faceW - 20;
            else textX = faceX + faceW + 24;
            bool talking = _talkShown < PageChars(page);
            var f = faces[talking ? (int)(_time * 8) % faces.Count : 0];
            BlitFrame(f.Px, f.W, f.H, faceX, by + (boxH - faceH) / 2, faceS, false, 0, 0, (0, 0, _clientW, _clientH));
        }

        // 이름 — 「사울-검사」에서 직업을 떼고
        string name = page.Speaker >= 0 ? CharacterNames.Of(page.Speaker).Split('-')[0] : "";
        int ty = by + 16;
        if (name.Length > 0)
        {
            var n = Text.Render(name, Math.Max(14, glyph * 3 / 4), 0xFFFFE070);
            DrawText(n, textX, ty);
            ty += n.H + 8;
        }

        int left = (int)_talkShown;
        foreach (var line in page.Lines)
        {
            int x = textX;
            foreach (int c in line)
            {
                if (left-- <= 0) break;
                DrawGlyph(c, x, ty, g);
                x += glyph;
            }
            ty += lineH;
        }

        if (_talkShown >= PageChars(page) && (int)(_time * 2) % 2 == 0)
        {
            var more = Text.Render("▼", Math.Max(12, glyph / 2), 0xFFFFFFFF);
            DrawText(more, bx + boxW - more.W - 16, by + boxH - more.H - 10);
        }
    }

    /// <summary>원본 글꼴 글자 하나 — 그림자를 한 점 아래오른쪽에 깔고 흰 글자.</summary>
    private void DrawGlyph(int code, int x, int y, int g)
    {
        if (_font == null || !_font.Has(code)) return;
        for (int yy = 0; yy < Font16.Size; yy++)
            for (int xx = 0; xx < Font16.Size; xx++)
            {
                if (!_font.Pixel(code, xx, yy)) continue;
                FillRect(x + xx * g + g / 2, y + yy * g + g / 2, g, g, 0xFF000000);
                FillRect(x + xx * g, y + yy * g, g, g, 0xFFFFFFFF);
            }
    }

    private List<(uint[] Px, int W, int H)> Face(int icon)
    {
        if (_faces.TryGetValue(icon, out var f)) return f;
        try { f = Portrait.Load(_dir, icon, _pal); }
        catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { f = []; }
        return _faces[icon] = f;
    }
}
