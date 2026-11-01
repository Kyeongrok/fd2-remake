using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Fd2;

/// <summary>
/// 버그 리포트(창세기전3 처럼, 사용자 요청) — 메뉴의 「버그 리포트」가 글 쓰는 창을 띄우고(PC 는 WinForms 창, 폰은 안드로이드 대화 상자),
/// 쓴 글과 게임이 저절로 붙인 것(<see cref="BugContext"/>)을 Cloudflare Worker(<c>tools/bug-worker</c> 의 <c>POST /v1/bug</c>)로 보낸다.
/// </summary>
/// <remarks>
/// 못 보내면 <see cref="BugQueue"/> 에 두었다가 다음에 켤 때 다시 보낸다. 누가 보냈는지는 설치할 때 만든 무작위 번호(<see cref="Settings.InstallId"/>)뿐이고,
/// 경로의 사용자 이름은 지워 보낸다(<see cref="Scrub"/>). 받는 곳은 <c>FD2_BUG_URL</c> 로 바꿀 수 있다(시험용 — https 나 로컬 주소만).
/// </remarks>
internal sealed partial class GameWindow
{
    private const string BugUrlDefault = "https://fd2-bugs.oceanfog1.workers.dev";
    private const int BugMaxText = 8000;

    private static string BugUrl
    {
        get
        {
            string over = Environment.GetEnvironmentVariable("FD2_BUG_URL")?.Trim() ?? "";
            if (over.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || over.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) || over.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                return over;
            return BugUrlDefault;
        }
    }

    private static string BugQueue => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "bug-queue");

    /// <summary>게임 버전(「0.14.6-dev+01ac97e」) — 빌드 때 넣은 버전에서 긴 커밋 꼬리를 뗀다.</summary>
    private static string AppVersion
    {
        get
        {
            string v = typeof(GameWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            int dot = v.IndexOf('+') is int plus and >= 0 ? v.IndexOf('.', plus) : -1;
            return dot > 0 ? v[..dot] : v;
        }
    }

    /// <summary>글 쓰는 창 — 플랫폼마다(PC WinForms · 안드로이드 AlertDialog). 보내기를 누르면 send(글).</summary>
    private partial void AskBugText(string summary, Action<string> send);

    /// <summary>메뉴의 「버그 리포트」.</summary>
    private void OpenBugReport()
    {
        var context = BugContext();
        AskBugText(BugSummary(context), text => SubmitBug(text, context));
    }

    /// <summary>글 하나를 줄에 세우고 뒤에서 보낸다(창의 스레드에서 불려도 된다 — 게임 상태는 건드리지 않는다).</summary>
    private void SubmitBug(string text, Dictionary<string, object?> context)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (text.Length > BugMaxText) text = text[..BugMaxText];
        try
        {
            Directory.CreateDirectory(BugQueue);
            var body = new { v = 1, install = InstallId(), version = AppVersion, text, context };
            File.WriteAllText(Path.Combine(BugQueue, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(body));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Toast("버그 리포트를 적어 두지 못했습니다");
            return;
        }
        UploadBugs(announce: true);
    }

    private static readonly object BugGate = new();
    private static bool _bugUploading, _bugAgain, _bugAnnounce;

    /// <summary>줄에 선 리포트를 뒤에서 보낸다 — 켤 때와 보내기를 누른 뒤. 받은 것 · 받는 쪽이 못 쓴다고 한 것(4xx, 429 빼고)은 지우고 나머지는 다음에.</summary>
    private void UploadBugs(bool announce = false)
    {
        if (!Directory.Exists(BugQueue)) { if (announce) Toast("나중에 다시 보냅니다"); return; }
        lock (BugGate)
        {
            _bugAnnounce |= announce;
            if (_bugUploading) { _bugAgain = true; return; }
            _bugUploading = true;
        }
        string url = BugUrl;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                bool sent = await UploadBugQueue(url);
                bool tell;
                lock (BugGate)
                {
                    if (_bugAgain) { _bugAgain = false; continue; }
                    tell = _bugAnnounce;
                    _bugAnnounce = _bugUploading = false;
                }
                if (tell) Toast(sent ? "보냈습니다 — 고맙습니다" : "지금은 못 보냈습니다 — 다음에 켤 때 다시 보냅니다");
                return;
            }
        });
    }

    private static async Task<bool> UploadBugQueue(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var files = new DirectoryInfo(BugQueue).GetFiles("*.json").OrderBy(f => f.Name).ToList();
            foreach (var old in files.SkipLast(30)) old.Delete();   // 오래 못 보낸 것이 쌓이지 않게 새 것 30 개만
            bool all = true;
            foreach (var file in files.TakeLast(30))
            {
                using var content = new StringContent(await File.ReadAllTextAsync(file.FullName), Encoding.UTF8, "application/json");
                using var reply = await http.PostAsync(url.TrimEnd('/') + "/v1/bug", content);
                int code = (int)reply.StatusCode;
                if (reply.IsSuccessStatusCode || code is >= 400 and < 500 and not 429) file.Delete();
                if (code is 429 or >= 500) { all = false; break; }
            }
            return all;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>설치할 때 만든 무작위 번호 — 설정에 둔다.</summary>
    private string InstallId()
    {
        if (!Guid.TryParse(_settings.InstallId, out _))
        {
            _settings.InstallId = Guid.NewGuid().ToString();
            try { _settings.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return _settings.InstallId;
    }

    /// <summary>게임이 저절로 붙이는 것 — 받는 곳의 <c>bug_reports.context</c> 에 JSON 으로.</summary>
    private Dictionary<string, object?> BugContext()
    {
        bool battle = _screen == Screen.Game && InBattle;
        var c = new Dictionary<string, object?>
        {
            ["game"] = AppVersion,
            ["os"] = $"{Environment.OSVersion.VersionString}{(Environment.Is64BitOperatingSystem ? " x64" : "")}",
            ["platform"] = OperatingSystem.IsAndroid() ? "android" : "windows",
            ["mobileView"] = TouchUi,
            ["frame"] = $"{_clientW}x{_clientH}",
            ["data"] = Scrub(VersionName),
            ["screen"] = _screen.ToString(),
            ["chapter"] = _scene != null && _screen == Screen.Game ? _scene.Index + 1 : null,
            ["battle"] = battle,
            ["turn"] = battle ? Turn : null,
            ["state"] = battle ? $"{_bs}{(_enemyPhase ? " · 적 차례" : "")}" : null,
        };
        c["music"] = $"{(_settings.Music ? "켬" : "끔")} · 크기 {_settings.MusicVolume} · {MusicStatus()}";
        if (MusicLogTail() is { } music) c["musicLog"] = music;
        if (CrashTail() is { } crash) c["crashLog"] = crash;
        return c;
    }

    /// <summary>지금 음악 상태 — 플랫폼마다(PC MCI · 안드로이드 미디어 플레이어).</summary>
    private partial string MusicStatus();

    private static string MusicLog => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "music.txt");

    private string? _musicFailLast;

    /// <summary>음악을 못 튼 까닭을 기록에 남긴다(MusicTick 이 되풀이해도 같은 줄은 한 번만) — 게임은 계속.</summary>
    private void MusicFail(string what)
    {
        if (what == _musicFailLast) return;
        _musicFailLast = what;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MusicLog)!);
            if (File.Exists(MusicLog) && new FileInfo(MusicLog).Length > 32 * 1024) File.Delete(MusicLog);
            File.AppendAllText(MusicLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {AppVersion} {what}\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>음악 기록의 끝 — 마지막 20 줄.</summary>
    private static string? MusicLogTail()
    {
        try
        {
            if (!File.Exists(MusicLog)) return null;
            var lines = File.ReadAllLines(MusicLog);
            return Scrub(string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 20))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>창 아래에 보일 요약 — 무엇이 함께 가는지.</summary>
    private static string BugSummary(Dictionary<string, object?> c) =>
        $"함께 보내는 것: 버전 {c["game"]} · {c["platform"]} · 화면 {c["frame"]} · 자료 {c["data"]}" +
        (c["chapter"] is int ch ? $" · {ch}장" : "") + (c["turn"] is int t ? $" {t}턴" : "") + " · 음악 상태" + (c.ContainsKey("musicLog") ? " · 음악 기록" : "") + (c.ContainsKey("crashLog") ? " · 오류 기록 끝" : "") +
        "\n이름 · 계정 · IP 는 보내지 않습니다.";

    /// <summary>오류 기록(crash.txt)의 끝 — 마지막 100 줄 · 16KB 까지.</summary>
    private static string? CrashTail()
    {
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string path in new[] { Path.Combine(local, "Fd2", "crash.txt"), Path.Combine(local, "crash.txt") })
            {
                if (!File.Exists(path)) continue;
                string text = File.ReadAllText(path);
                if (text.Length > 16 * 1024) text = text[^(16 * 1024)..];
                var lines = text.Split('\n');
                return Scrub(string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 100))));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>사용자 폴더 · 사용자 이름을 지운다.</summary>
    private static string Scrub(string s)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 3) s = s.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length > 1) s = s.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        return s;
    }

    // ── 알림 한 줄(토스트) — 아무 화면에서나 아래 가운데에 잠깐. 다른 스레드에서 불려도 된다. ──

    private volatile string? _toastNext;
    private string _toastText = "";
    private double _toastUntil;

    private void Toast(string text) => _toastNext = text;

    private void DrawToast()
    {
        if (_toastNext is { } next) { _toastNext = null; (_toastText, _toastUntil) = (next, _time + 2.5); }
        if (_time >= _toastUntil || _toastText.Length == 0) return;
        int fs = Math.Max(14, 7 * HudZoom);
        var t = Text.Render(_toastText, fs, 0xFFFFFFFF);
        int x = (_clientW - t.W) / 2, y = _clientH - t.H - 40;
        FillRect(x - 16, y - 8, t.W + 32, t.H + 16, 0xE0182040);
        DrawText(t, x, y);
    }
}
