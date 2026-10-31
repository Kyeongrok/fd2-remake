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
        ResolveSpeakers();
        _talkPage = 0;
        _talkShown = 0;
        _talkSkip = 0;
        _talkMore = false;
        _talkWindowEnd = int.MaxValue;
        _afterTalk = after;
        if (!Talking) EndTalk();
        else FocusSpeaker();
    }

    /// <summary>
    /// FFEC · FFED 쪽의 말하는 사람(장면 유닛 표 차례 번호)을 그림 번호 · 이름으로 — 동료 · 왕처럼 이름이 있으면 이름과 얼굴,
    /// 몬스터 표(해적 같은)면 얼굴만.
    /// </summary>
    private void ResolveSpeakers()
    {
        foreach (var p in _talk)
        {
            if (p.Roster < 0 || p.Roster >= _scene.Roster.Count) continue;
            int id = _scene.Roster[p.Roster].Icon;
            p.Speaker = CharacterNames.SpriteOf(id, _icons.RoyalsMoved);
            p.Name ??= id < CharacterNames.Heroes ? CharacterNames.Of(id).Split('-')[0] : "";
        }
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

    private int PageChars(Dialogue.Page p) => p.Text?.Length ?? p.Lines.Sum(l => l.Count(c => !IsQuoteGlyph(c)));

    /// <summary>원작 대사의 따옴표 글자(0x180 「"」 · 0x181 「"」) — 창에 이름이 나오니 그리지 않는다(사용자 요청).</summary>
    private static bool IsQuoteGlyph(int code) => code is 0x180 or 0x181;

    private readonly Dictionary<(string, int, int), List<string>> _wrapped = new();

    /// <summary>보통 글을 폭 width 에 맞게 줄로 — 띄어쓰기에서 끊고, 한 낱말이 넘치면 글자에서.</summary>
    private List<string> Wrap(string text, int size, int width)
    {
        if (_wrapped.TryGetValue((text, size, width), out var lines)) return lines;
        lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (Text.Render(next, size, 0xFFFFFFFF).W <= width) { line = next; continue; }
                if (line.Length > 0) lines.Add(line);
                line = word;
                while (line.Length > 1 && Text.Render(line, size, 0xFFFFFFFF).W > width)
                {
                    int n = line.Length - 1;
                    while (n > 1 && Text.Render(line[..n], size, 0xFFFFFFFF).W > width) n--;
                    lines.Add(line[..n]);
                    line = line[n..];
                }
            }
            lines.Add(line);
        }
        if (_wrapped.Count > 500) _wrapped.Clear();
        return _wrapped[(text, size, width)] = lines;
    }

    /// <summary>창에 다 안 들어가는 대사 — 앞에서 넘긴 줄 수, 지금 창 끝까지의 글자 수, 뒤에 줄이 더 있나, 창에 들어가는 줄 수(DrawTalk 이 정한다).</summary>
    private int _talkSkip, _talkWindowEnd = int.MaxValue, _talkFit = 3;
    private bool _talkMore;

    private void TalkTick(double dt)
    {
        if (!Talking) return;
        _talkShown += dt * TalkCharsPerSecond;
        if (_talkMore && _talkShown > _talkWindowEnd) _talkShown = _talkWindowEnd;   // 창 끝에서 ▼ 를 띄우고 기다린다
    }

    /// <summary>다음 — 글이 아직 나오는 중이면 다 보이고, 다 보였으면 다음 쪽.</summary>
    private void TalkNext()
    {
        if (!Talking) return;
        if (_talkShown < Math.Min(_talkWindowEnd, PageChars(_talk[_talkPage]))) { _talkShown = _talkMore ? _talkWindowEnd : 9999; return; }
        if (_talkMore) { _talkSkip += _talkFit; _talkShown = _talkWindowEnd; _talkMore = false; _talkWindowEnd = int.MaxValue; return; }   // 같은 쪽의 다음 줄들
        if (++_talkPage >= _talk.Count) { EndTalk(); return; }
        _talkShown = 0;
        _talkSkip = 0;
        _talkWindowEnd = int.MaxValue;
        if (_screen == Screen.Prologue) ProloguePage();
        FocusSpeaker();
    }

    /// <summary>대사를 넘기는 키 — Enter · Space · 방향키 · W/A/S/D(바꾼 커서 키도).</summary>
    private bool TalkAdvanceKey(int vk) => !_keyRepeat && (
        vk is Win32.VK_RETURN or Win32.VK_SPACE or Win32.VK_UP or Win32.VK_DOWN or Win32.VK_LEFT or Win32.VK_RIGHT
        || ActionOf(vk) is KeyAction.MoveUp or KeyAction.MoveDown or KeyAction.MoveLeft or KeyAction.MoveRight);

    private bool TalkKey(int vk)
    {
        if (!Talking) return false;
        if (TalkAdvanceKey(vk)) TalkNext();
        else if (vk == Win32.VK_ESCAPE) EndTalk();
        return true;
    }

    private void DrawTalk()
    {
        if (!Talking) return;
        var page = _talk[_talkPage];
        var faces = page.Speaker >= 0 ? Face(page.Speaker) : null;
        string name = page.Name ?? (page.Speaker >= 0 ? CharacterNames.Of(page.Speaker).Split('-')[0] : "");   // 「사울-검사」에서 직업을 떼고

        // 글자 · 얼굴 배율 — 큰 것부터 줄여 창이 화면 안에 들어가는 첫 배율(폰처럼 낮은 화면에서 글이 창 밖으로 넘치지 않게)
        int g = 0, glyph = 0, lineH = 0, faceS = 0, faceW = 0, faceH = 0, boxW = 0, boxH = 0, textSize = 0, textLineH = 0;
        List<string>? textLines = null;
        // 세로 화면 — 글은 원작 글꼴 작게(한 줄 스무 자 남짓), 창은 화면 폭에 맞추고 여백을 좁게(사용자 요청)
        bool narrow = _clientW < _clientH;
        int pad = narrow ? 12 : 24;
        for (g = narrow ? Math.Max(1, Math.Min(HudZoom, _clientW / (Font16.Size * 20))) : Math.Max(2, HudZoom); g >= 1; g--)
        {
            glyph = Font16.Size * g;
            lineH = glyph + g * 4;
            // 얼굴 옆에 글이 일곱 자는 들어가게 — 좁은 세로 화면이면 얼굴을 줄이고, 그래도 안 되면 얼굴을 뺀다(사용자 요청)
            for (faceS = g; faceS >= 0; faceS--)
            {
                faceW = faces is { Count: > 0 } && faceS > 0 ? faces[0].W * faceS : 0;
                boxW = narrow ? _clientW - 16 : Math.Min(_clientW - 40, Math.Max(faceW + glyph * 16 + 80, _clientW * 2 / 3));
                if (boxW - faceW - 72 >= glyph * 7) break;
            }
            faceS = Math.Max(0, faceS);
            faceW = faces is { Count: > 0 } && faceS > 0 ? faces[0].W * faceS : 0;
            faceH = faces is { Count: > 0 } && faceS > 0 ? faces[0].H * faceS : 0;
            boxW = narrow ? _clientW - 16 : Math.Min(_clientW - 40, Math.Max(faceW + glyph * 16 + 80, _clientW * 2 / 3));
            boxH = narrow ? Math.Max(faceH + 24, lineH * 3 + 40) : Math.Max(faceH + 40, lineH * 3 + 70);

            // 보통 글(프롤로그) — 얼굴 옆 남는 폭에 맞춰 줄을 나누고, 줄이 많으면 창을 키운다
            textSize = Math.Max(14, glyph * 5 / 8);
            textLineH = textSize * 3 / 2;
            textLines = null;
            if (page.Text != null)
            {
                textLines = Wrap(page.Text, textSize, boxW - faceW - 24 * 3);
                boxH = Math.Max(boxH, textLines.Count * textLineH + textSize * 2 + 50);
            }
            // PC 는 창이 화면 높이의 2/5 를 넘지 않게 — 큰 창에서 배율대로 키우면 지도를 반 넘게 가렸다(사용자 요청)
            int maxH = TouchUi ? _clientH - 32 : Math.Max(160, _clientH * 2 / 5);
            if (boxH <= maxH && boxW - faceW - 72 >= glyph * 7) break;
        }
        g = Math.Max(1, g);

        // 원작처럼 오른쪽에 얼굴이 나오는 사람(FFEF · FFED)은 창을 위에, 왼쪽(FFEE · FFEC)은 아래에
        int bx = (_clientW - boxW) / 2, by = page.Right ? (TouchUi ? 56 : 16) : _clientH - boxH - (narrow ? 8 : 16);   // 폰은 오른쪽 위 단추 밑으로
        // 바탕은 얼굴 그림(DATO)의 바탕색 #28458A 과 같게 — 얼굴이 창에 붙어 보이도록. 테두리는 네 변 모두
        FillRect(bx, by, boxW, boxH, 0xFF28458A);
        FillRect(bx, by, boxW, 3, 0xFF8090C0);
        FillRect(bx, by + boxH - 3, boxW, 3, 0xFF8090C0);
        FillRect(bx, by, 3, boxH, 0xFF8090C0);
        FillRect(bx + boxW - 3, by, 3, boxH, 0xFF8090C0);

        int textX = bx + pad, faceX = bx + pad - 4;
        if (faces is { Count: > 0 } && faceS > 0)
        {
            if (page.Right) faceX = bx + boxW - faceW - (pad - 4);
            else textX = faceX + faceW + pad;
            bool talking = _talkShown < PageChars(page);
            var f = faces[talking ? (int)(_time * 8) % faces.Count : 0];
            BlitFrame(f.Px, f.W, f.H, faceX, by + (boxH - faceH) / 2, faceS, false, 0, 0, (0, 0, _clientW, _clientH));
        }

        int ty = by + (narrow ? 10 : 16);
        if (name.Length > 0)
        {
            // 몬스터 · 장면 사람은 「몬스터 29」 대신 원작 이름 표(FDTXT 0 의 번호+1 줄)의 이름을 원본 글꼴로
            var n = page.Name == null && page.Speaker >= CharacterNames.Heroes && NameImage(page.Speaker + 1, Math.Max(1, (g * 3 + 2) / 4), 0xFFFFE070) is { W: > 1 } original
                ? original : Text.Render(name, Math.Max(14, glyph * 3 / 4), 0xFFFFE070);
            DrawText(n, textX, ty);
            ty += n.H + 8;
        }

        int left = (int)_talkShown;
        foreach (var line in textLines ?? [])
        {
            if (left <= 0) break;
            var img = Text.Render(left >= line.Length ? line : line[..left], textSize, 0xFFFFFFFF, bold: false);
            DrawText(img, textX, ty);
            left -= line.Length + 1;   // 줄 사이의 띄어쓰기 한 칸
            ty += textLineH;
        }
        if (textLines != null) left = 0;
        // 원작 글꼴 대사 — 창에 들어가는 줄만큼씩(원작처럼). 더 남았으면 ▼ 를 띄우고 누르면 이어지는 줄(_talkSkip)
        var lines = Reflow(page, Math.Max(8, (bx + boxW - pad - textX - (page.Right ? faceW + pad : 0)) / glyph));
        int fit = Math.Max(1, (by + boxH - 12 - ty) / lineH);
        _talkSkip = Math.Clamp(_talkSkip, 0, Math.Max(0, lines.Count - 1));
        int skipped = lines.Take(_talkSkip).Sum(l => l.Count);
        _talkWindowEnd = textLines != null ? PageChars(page) : skipped + lines.Skip(_talkSkip).Take(fit).Sum(l => l.Count);
        _talkMore = textLines == null && _talkSkip + fit < lines.Count;
        _talkFit = fit;
        if (textLines == null) left -= skipped;
        foreach (var line in lines.Skip(_talkSkip).Take(fit))
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

        if (_talkShown >= _talkWindowEnd && (int)(_time * 2) % 2 == 0)
        {
            var more = Text.Render("▼", Math.Max(12, glyph / 2), 0xFFFFFFFF);
            DrawText(more, bx + boxW - more.W - 16, by + boxH - more.H - 10);
        }
    }

    private const int GlyphSpace = 0x9E;
    private readonly Dictionary<(Dialogue.Page, int), List<List<int>>> _reflowed = new();

    /// <summary>
    /// 원작 대사를 창 폭에 맞게 다시 줄 나눔 — 원작은 320 폭 창에 맞춰 줄을 끊어 두어(낱말 가운데서도) 넓은 창에서는 오른쪽이 빈다.
    /// 줄을 이어(이어지는 줄 앞의 들여쓰기 빈칸은 뗀다) 한 줄 width 글자 안에서 빈칸에서 끊는다. 글자 수는 그대로라 한 자씩 보이기와 맞는다.
    /// </summary>
    private List<List<int>> Reflow(Dialogue.Page page, int width)
    {
        if (_reflowed.TryGetValue((page, width), out var lines)) return lines;
        var all = new List<int>();
        for (int i = 0; i < page.Lines.Count; i++)
        {
            var l = page.Lines[i];
            int skip = 0;
            while (skip < l.Count && l[skip] == GlyphSpace) skip++;   // 줄 앞 들여쓰기(첫 줄은 따옴표를 뺀 자리) — 다시 나눌 때 정한다
            all.AddRange(l.Skip(skip).Where(c => !IsQuoteGlyph(c)));
        }
        lines = new List<List<int>>();
        int start = 0;
        while (start < all.Count)
        {
            int end = Math.Min(all.Count, start + width);
            if (end < all.Count)
            {
                int cut = all.LastIndexOf(GlyphSpace, end - 1, end - start);
                if (cut > start) end = cut + 1;
            }
            var line = all.GetRange(start, end - start);
            while (line.Count > 0 && line[^1] == GlyphSpace) line.RemoveAt(line.Count - 1);
            if (lines.Count > 0) line.Insert(0, GlyphSpace);   // 원작처럼 이어지는 줄은 한 칸 들여서
            lines.Add(line);
            start = end;
        }
        if (_reflowed.Count > 500) _reflowed.Clear();
        return _reflowed[(page, width)] = lines;
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
