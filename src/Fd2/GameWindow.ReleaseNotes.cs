using System.Reflection;
using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 릴리즈 노트(사용자 요청 w-101) — 타이틀 위 버전 단추 옆 「릴리즈 노트」, 그리고 게임 버전이 바뀐 뒤 처음 타이틀이 뜰 때 저절로(그 사이 버전들만).
/// 글은 src/Fd2/릴리즈노트.md 를 실행 파일에 넣은 것(「## fd2-v판」 대목 · 「- 」 줄). 끌기 · 휠 · ↑↓ 로 넘기고, 누르기 · Esc · Enter 로 닫는다.
/// </summary>
internal sealed partial class GameWindow
{
    private bool _notesOpen, _notesChecked;
    private int _notesScroll, _notesMax, _notesStep = 1;
    private string _notesHead = "";
    private List<(string Version, List<string> Lines)> _notesShown = new();
    private int? _notesDragFrom;

    /// <summary>버전이 아닌 대목(「## 도움을 주신 분」) — 노트 끝에 늘 붙인다.</summary>
    private static readonly List<(string Head, List<string> Lines)> _notesExtra = new();

    /// <summary>실행 파일에 든 릴리즈 노트 — 대목(새것부터)마다 버전과 줄들. 버전이 아닌 대목은 <see cref="_notesExtra"/> 로.</summary>
    private static List<(string Version, List<string> Lines)> ReadNotes()
    {
        var list = new List<(string, List<string>)>();
        _notesExtra.Clear();
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fd2.ReleaseNotes.md");
            if (s == null) return list;
            using var r = new StreamReader(s);
            List<string>? cur = null;
            for (string? line = r.ReadLine(); line != null; line = r.ReadLine())
            {
                if (line.StartsWith("## "))
                {
                    string v = line[3..].Trim();
                    if (!v.StartsWith("fd2-")) { cur = new List<string>(); _notesExtra.Add((v, cur)); continue; }
                    v = v[4..];
                    v = v.TrimStart('v');   // 「0.14.33」 — 그릴 때 v 를 붙인다
                    cur = new List<string>();
                    list.Add((v, cur));
                }
                else if (cur != null && line.Trim().Length > 0) cur.Add(line.Trim());
            }
        }
        catch (IOException) { }
        return list;
    }

    /// <summary>노트를 연다 — since 가 있으면 그 버전보다 새 대목만(업데이트 뒤), 없으면 모두.</summary>
    private void OpenNotes(string? since = null)
    {
        var all = ReadNotes();
        if (all.Count == 0) { Toast("릴리즈 노트가 없습니다"); return; }
        var shown = all;
        if (since != null)
        {
            int at = all.FindIndex(n => n.Version == since);
            shown = at > 0 ? all.Take(Math.Min(at, 10)).ToList() : all.Take(1).ToList();   // 처음 보는 사람(지난 버전 모름)은 이번 대목만
        }
        _notesShown = shown.Concat(_notesExtra.Select(e => ("#" + e.Head, e.Lines))).ToList();   // 「#」 = 버전이 아닌 대목
        _notesHead = since != null ? $"{TitleVersion} 로 업데이트했습니다" : "릴리즈 노트";
        (_notesOpen, _notesScroll, _notesDragFrom) = (true, 0, null);
        PlaySfx(Sound.UiBank, Sound.Menu);
    }

    private void CloseNotes() => (_notesOpen, _notesDragFrom) = (false, null);

    /// <summary>타이틀이 뜰 때 한 번 — 게임 버전이 바뀌었으면 노트를 띄운다. 완전 처음 실행이면 띄우지 않고 적어만 둔다.</summary>
    private void MaybeOpenNotes()
    {
        if (_notesChecked || Offscreen || _tutorOpen) return;
        _notesChecked = true;
        string now = TitleVersion;
        if (now == "dev" || _settings.NotesSeenVersion == now) return;
        string seen = _settings.NotesSeenVersion;
        _settings.NotesSeenVersion = now;
        try { _settings.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (_settings.FirstRun) return;
        OpenNotes(seen.Length > 0 ? seen.TrimStart('v') : "");
    }

    /// <summary>노트가 열려 있으면 키를 먹는다.</summary>
    private bool NotesKey(int vk)
    {
        if (!_notesOpen) return false;
        var action = ActionOf(vk);
        if (action == KeyAction.MoveUp) vk = Win32.VK_UP;
        else if (action == KeyAction.MoveDown) vk = Win32.VK_DOWN;
        switch (vk)
        {
            case Win32.VK_UP: NotesScroll(-_notesStep * 2); break;
            case Win32.VK_DOWN: NotesScroll(_notesStep * 2); break;
            case Win32.VK_PRIOR: NotesScroll(-_notesStep * 12); break;
            case Win32.VK_NEXT or Win32.VK_SPACE: NotesScroll(_notesStep * 12); break;
            case 0x24:   // Home
                _notesScroll = 0; break;
            case 0x23:   // End
                _notesScroll = _notesMax; break;
            case Win32.VK_ESCAPE or Win32.VK_RETURN: CloseNotes(); break;
        }
        return true;
    }

    private void NotesScroll(int dy) => _notesScroll = Math.Clamp(_notesScroll + dy, 0, _notesMax);

    /// <summary>누른 채 끌면 넘긴다 — 끄는 중이면 참(뗄 때 닫지 않는다).</summary>
    private bool NotesDrag(int y)
    {
        if (!_notesOpen || _press is not { } p) return false;
        if (!_dragging && Math.Abs(y - p.Y) < DragSlop) return true;
        if (!_dragging) { _dragging = true; _notesDragFrom = _notesScroll; }
        _notesScroll = Math.Clamp((_notesDragFrom ?? 0) - (y - p.Y), 0, _notesMax);
        return true;
    }

    private void DrawNotes()
    {
        if (!_notesOpen) return;
        FillRect(0, 0, _clientW, _clientH, 0xC0000000);
        int fs = Math.Max(14, 7 * HudZoom), small = Math.Max(12, fs * 5 / 6), pad = fs, edge = Math.Max(3, HudZoom);
        int w = Math.Min(_clientW - 32, fs * 34), inner = w - 2 * pad;
        int h = Math.Min(_clientH - 32, fs * 30);
        int x = (_clientW - w) / 2, y = (_clientH - h) / 2;
        FillRect(x - edge, y - edge, w + 2 * edge, h + 2 * edge, FdEdge);
        FillRect(x, y, w, h, FdBlue);
        var head = Text.Render(_notesHead, fs * 3 / 2, FdYellow);
        DrawText(head, x + pad, y + pad / 2);
        var foot = Text.Render(TouchUi ? "끌어서 넘기기 · 누르면 닫기" : "↑↓ · 휠 넘기기 · Esc · Enter · 누르면 닫기", Math.Max(12, fs * 3 / 4), FdEdgeLit, bold: false);
        int top = y + pad / 2 + head.H + pad / 2, bottom = y + h - foot.H - pad / 2 - 4;
        FillRect(x + pad, top - 2, inner, Math.Max(2, edge / 2), FdEdgeLit);

        // 줄을 펼친다 — 대목 머리(노랑) · 「- 」 줄은 「·」 와 들여쓰기
        var rows = new List<(Text.Image Img, int Indent)>();
        int gap = small / 2;
        foreach (var (ver, lines) in _notesShown)
        {
            rows.Add((Text.Render(ver.StartsWith('#') ? ver[1..] : $"v{ver}", fs, 0xFFFFE070), 0));
            foreach (var raw in lines)
            {
                bool bullet = raw.StartsWith("- ");
                string body = bullet ? raw[2..] : raw;
                int indent = bullet ? small * 2 : small;
                var wrapped = Wrap(body, small, inner - indent - small).ToList();
                for (int k = 0; k < wrapped.Count; k++)
                    rows.Add((Text.Render((bullet && k == 0 ? "· " : "  ") + wrapped[k], small, 0xFFFFFFFF, bold: false), indent - small));
            }
            rows.Add((null!, -1));   // 대목 사이
        }
        if (rows.Count > 0) rows.RemoveAt(rows.Count - 1);
        int total = rows.Sum(r => r.Indent < 0 ? gap : r.Img.H + 4);
        _notesStep = Math.Max(1, small + 4);
        _notesMax = Math.Max(0, total - (bottom - top - 8));
        _notesScroll = Math.Clamp(_notesScroll, 0, _notesMax);
        int cy = top + 8 - _notesScroll;
        foreach (var (img, indent) in rows)
        {
            if (indent < 0) { cy += gap; continue; }
            if (cy >= top && cy + img.H <= bottom) DrawText(img, x + pad + indent, cy);   // 칸 밖 줄은 그리지 않는다
            cy += img.H + 4;
        }
        if (_notesScroll > 0) { var up = Text.Render("▲", small, FdYellow); DrawText(up, x + w - up.W - pad / 2, top + 4); }
        if (_notesScroll < _notesMax) { var dn = Text.Render("▼", small, FdYellow); DrawText(dn, x + w - dn.W - pad / 2, bottom - dn.H); }
        DrawText(foot, x + (w - foot.W) / 2, y + h - foot.H - pad / 2);
    }
}
