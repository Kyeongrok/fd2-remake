using Fd2.Data;

namespace Fd2;

/// <summary>
/// 폰 조작법 안내(사용자 요청 w-91) — 처음 타이틀이 뜰 때 한 번 저절로, 그 뒤는 설정 › 「조작법 보기」로.
/// 누르면 다음 장, 마지막 장에서 누르거나 길게 누르기 · ESC(두 손가락 탭)면 닫는다. 열려 있는 동안 다른 입력은 받지 않는다.
/// </summary>
internal sealed partial class GameWindow
{
    private static readonly (string Title, string[] Lines)[] TutorPages =
    [
        ("폰 조작법", ["용의기사2 폰 조작을 짧게 알려 드립니다.", "화면을 누르면 다음 장으로 넘어갑니다."]),
        ("누르기", ["우리 편을 누르면 갈 수 있는 칸이 파랗게 보입니다.", "파란 칸을 누르면 걸어가고, 제자리를 한 번 더 누르면 걷지 않고 명령 메뉴가 열립니다.",
                    "공격 · 마법은 처음 누른 칸을 겨누고, 같은 칸을 한 번 더 누르면 겁니다."]),
        ("끌기", ["손가락으로 끌면 지도가 움직입니다.", "목록도 끌어서 넘깁니다 — 끌었다 뗀 것은 누르기로 치지 않습니다."]),
        ("취소 · 뒤로", ["길게 누르기(0.5초) 또는 두 손가락으로 짧게 톡 — ESC 와 같습니다.", "고른 것을 물리거나 메뉴를 닫습니다. 오른쪽 아래 ESC 단추도 같습니다."]),
        ("확대 · 축소", ["두 손가락을 벌리면 지도가 커지고, 오므리면 작아집니다."]),
        ("화면 단추", ["ESC · 메뉴 · 지도 같은 단추를 3초 누르고 있으면 끌어서 옮길 수 있습니다.", "설정의 「단추 자리 처음대로」로 되돌립니다."]),
        ("싸움 중에", ["마법 연출 중에 누르면 빨리 감습니다.", "적이 걷는 중에 누르면 그 걸음을 건너뜁니다.", "보물상자 창은 제목 줄을 눌러 접고 폅니다."]),
        ("다 봤습니다", ["이 안내는 메뉴 › 설정 › 「조작법 보기」에서 다시 볼 수 있습니다.", "누르면 닫습니다."]),
    ];

    private bool _tutorOpen;
    private int _tutorPage;

    /// <summary>처음 시작이면 안내를 연다 — 타이틀을 그릴 때 부른다.</summary>
    private void MaybeOpenTutorial()
    {
        // 완전 처음 실행일 때만(fb-85 — 업데이트한 사람에게는 안 뜬다). 설정 › 「조작법 보기」로는 언제나
        if (TouchUi && _settings.FirstRun && !_settings.TouchTutorialSeen && !_tutorOpen) OpenTutorial();
    }

    private void OpenTutorial() => (_tutorOpen, _tutorPage) = (true, 0);

    private void CloseTutorial()
    {
        _tutorOpen = false;
        if (_settings.TouchTutorialSeen) return;
        _settings.TouchTutorialSeen = true;
        try { _settings.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>안내가 열려 있으면 누르기 · 키를 먹는다 — 다음 장(마지막이면 닫기). back 이면 닫기.</summary>
    private bool TutorialInput(bool back)
    {
        if (!_tutorOpen) return false;
        PlaySfx(Sound.UiBank, Sound.Cursor);
        if (back || ++_tutorPage >= TutorPages.Length) CloseTutorial();
        return true;
    }

    private void DrawTutorial()
    {
        if (!_tutorOpen) return;
        FillRect(0, 0, _clientW, _clientH, 0xC0000000);
        var (title, lines) = TutorPages[Math.Clamp(_tutorPage, 0, TutorPages.Length - 1)];
        int fs = Math.Max(14, 7 * HudZoom), big = fs * 3 / 2, pad = fs, edge = Math.Max(3, HudZoom);
        int w = Math.Min(_clientW - 32, fs * 26), inner = w - 2 * pad;
        var head = Text.Render(title, big, FdYellow);
        var body = lines.SelectMany(l => Wrap(l, fs, inner).Select(s => Text.Render(s, fs, 0xFFFFFFFF)).Append(null)).ToList();
        if (body.Count > 0) body.RemoveAt(body.Count - 1);
        var foot = Text.Render($"{_tutorPage + 1} / {TutorPages.Length}   ·   누르면 {(_tutorPage == TutorPages.Length - 1 ? "닫기" : "다음")}   ·   길게 누르면 건너뛰기", Math.Max(12, fs * 3 / 4), FdEdgeLit);
        int gap = fs / 2;
        int h = pad + head.H + fs + body.Sum(b => b?.H ?? gap) + fs + foot.H + pad;
        int x = (_clientW - w) / 2, y = (_clientH - h) / 2;
        FillRect(x - edge, y - edge, w + 2 * edge, h + 2 * edge, FdEdge);
        FillRect(x, y, w, h, FdBlue);
        int cy = y + pad;
        DrawText(head, x + pad, cy);
        cy += head.H + fs / 2;
        FillRect(x + pad, cy, inner, Math.Max(2, edge / 2), FdEdgeLit);
        cy += fs / 2;
        foreach (var b in body)
        {
            if (b == null) { cy += gap; continue; }
            DrawText(b, x + pad, cy);
            cy += b.H;
        }
        cy += fs;
        DrawText(foot, x + (w - foot.W) / 2, cy);
    }
}
