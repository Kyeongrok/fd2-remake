using Fd2.Native;

namespace Fd2;

/// <summary>
/// 판 고르기 — 타이틀 메뉴의 「판: 지금 판」 줄에서 연다. 원본 · 모션패치 · 합친 판 들 가운데 고르면 그 판으로 다시 켠다.
/// 판 목록 · 바꾸기 · 더하기는 플랫폼이 맡는다(윈도우: 바탕화면 · 다운로드 · 문서에서 찾아 다시 켬, 안드로이드: 가져온 폴더들).
/// </summary>
internal sealed partial class GameWindow
{
    private bool _versionsOpen;
    private List<(string Name, string Dir)> _versions = new();
    private int _versionIndex;
    private readonly List<(int X, int Y, int W, int H)> _versionRects = new();

    /// <summary>찾은 판들(이름, 폴더).</summary>
    private partial List<(string Name, string Dir)> FindVersions();

    /// <summary>그 판으로 바꾼다 — 고른 판을 기억하고 다시 켠다.</summary>
    private partial void SwitchVersion(string dir);

    /// <summary>목록 끝에 「판 더하기」가 있나(안드로이드: 폴더 하나 더 가져오기).</summary>
    private partial bool CanAddVersion();

    private partial void AddVersion();

    /// <summary>지금 판 이름 — 게임 폴더 이름.</summary>
    private string VersionName => new DirectoryInfo(_dir).Name;

    private int VersionRows => _versions.Count + (CanAddVersion() ? 1 : 0);

    /// <summary>목록을 바꾸지 않고 닫았을 때 할 일 — 켤 때 고르기는 그 뒤에 오프닝.</summary>
    private Action? _afterVersions;

    private void OpenVersions() => OpenVersions(null);

    /// <summary>판 목록을 연다 — 찾은 판이 min 개보다 적으면 안 열고 거짓.</summary>
    private bool OpenVersions(Action? after, int min = 0)
    {
        _versions = FindVersions();
        if (_versions.Count < min) return false;
        _versionIndex = Math.Max(0, _versions.FindIndex(v => SameDir(v.Dir, _dir)));
        _versionsOpen = true;
        _afterVersions = after;
        return true;
    }

    private void CloseVersions()
    {
        _versionsOpen = false;
        var after = _afterVersions;
        _afterVersions = null;
        after?.Invoke();
    }

    /// <summary>판 목록이 연 동안 받는 키 — 위아래 · 결정 · Esc. 먹었으면 참.</summary>
    private bool VersionsKey(int vk)
    {
        if (!_versionsOpen) return false;
        int rows = Math.Max(1, VersionRows);
        switch (vk)
        {
            case Win32.VK_UP: _versionIndex = (_versionIndex + rows - 1) % rows; break;
            case Win32.VK_DOWN: _versionIndex = (_versionIndex + 1) % rows; break;
            case Win32.VK_RETURN or Win32.VK_SPACE: PickVersionRow(_versionIndex); break;
            case Win32.VK_ESCAPE: CloseVersions(); break;
        }
        return true;
    }

    private bool VersionsClick(int mx, int my)
    {
        if (!_versionsOpen) return false;
        for (int i = 0; i < _versionRects.Count; i++)
        {
            var r = _versionRects[i];
            if (mx >= r.X && my >= r.Y && mx < r.X + r.W && my < r.Y + r.H) { PickVersionRow(i); return true; }
        }
        CloseVersions();   // 목록 밖을 누르면 닫는다
        return true;
    }

    private void VersionsHover(int mx, int my)
    {
        for (int i = 0; i < _versionRects.Count; i++)
        {
            var r = _versionRects[i];
            if (mx >= r.X && my >= r.Y && mx < r.X + r.W && my < r.Y + r.H) _versionIndex = i;
        }
    }

    private void PickVersionRow(int i)
    {
        if (i == _versions.Count && CanAddVersion()) { CloseVersions(); AddVersion(); return; }
        if (i < 0 || i >= _versions.Count) return;
        if (SameDir(_versions[i].Dir, _dir)) { CloseVersions(); return; }
        _versionsOpen = false;
        _afterVersions = null;
        SwitchVersion(_versions[i].Dir);
    }

    private static bool SameDir(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private void DrawVersions()
    {
        if (!_versionsOpen) return;
        int fs = Math.Max(14, 7 * HudZoom);
        var rows = _versions.Select(v => (Label: v.Name, Sub: v.Dir, Now: SameDir(v.Dir, _dir))).ToList();
        if (CanAddVersion()) rows.Add(("+ 버전 더하기 (게임 폴더 하나 더 가져오기)", "", false));
        if (rows.Count == 0) rows.Add(("찾은 버전이 없습니다", "", false));

        var title = Text.Render(_afterVersions != null ? "버전 고르기 — 어느 버전으로 할까요 · Esc 지금 버전(●)으로"
                                                       : "버전 고르기 — 고르면 그 버전으로 다시 켭니다 · Esc 닫기", fs, 0xFFFFE070);
        var imgs = rows.Select(r => (L: Text.Render(r.Now ? $"● {r.Label}" : r.Label, fs, r.Now ? 0xFF90E0FF : 0xFFFFFFFF),
                                     S: Text.Render(r.Sub, Math.Max(11, fs * 2 / 3), 0xFF9098A8, bold: false))).ToList();
        int rowH = imgs.Max(i => i.L.H + (i.S.W > 1 ? i.S.H : 0)) + 10;
        int w = Math.Min(_clientW - 20, Math.Max(title.W, imgs.Max(i => Math.Max(i.L.W, i.S.W))) + 60);
        int h = title.H + 24 + rowH * rows.Count;
        int x = (_clientW - w) / 2, y = Math.Max(4, (_clientH - h) / 2);
        FillRect(0, 0, _clientW, _clientH, 0x90000000);
        FillRect(x, y, w, h, 0xF0181828);
        DrawText(title, x + 20, y + 10);
        _versionRects.Clear();
        int cy = y + title.H + 18;
        for (int i = 0; i < rows.Count; i++)
        {
            _versionRects.Add((x + 8, cy - 2, w - 16, rowH));
            if (i == _versionIndex) FillRect(x + 8, cy - 2, w - 16, rowH - 2, 0xC0405090);
            DrawText(imgs[i].L, x + 24, cy);
            if (imgs[i].S.W > 1) DrawText(imgs[i].S, x + 40, cy + imgs[i].L.H);
            cy += rowH;
        }
    }

    /// <summary>왼쪽 위 구석의 지금 버전 이름 — 어느 화면에서나 작게(Text.Render 가 그림을 기억한다).</summary>
    private Text.Image VersionLabel => Text.Render(VersionName, Math.Max(11, 5 * _zoom), 0xA0C8D0E0, bold: false);

    private void DrawVersionLabel()
    {
        if (VersionOnScreen) DrawText(VersionLabel, 4, 2);
    }

    /// <summary>버전 이름 줄 밑 — 왼쪽 위에 놓는 창은 여기부터(이름을 화면에 안 그리면 0).</summary>
    private int VersionLabelBottom => VersionOnScreen ? 2 + VersionLabel.H : 0;
}
