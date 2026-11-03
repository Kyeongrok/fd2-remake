using System.Runtime.InteropServices;
using Velopack;
using Velopack.Sources;

namespace Fd2;

/// <summary>
/// 설치판(Setup.exe)의 자동 업데이트 — 창세기전3 파트2 · 대항해시대(cds-remake)와 같은 Velopack.
/// 켤 때 GitHub 릴리즈에서 새 버전을 뒤에서 받아 두고, 지금 다시 시작할지 묻는다. 「아니오」면 게임을 끌 때 적용된다.
/// </summary>
/// <remarks>
/// Setup.exe 로 깔지 않은 판(단일 파일 Fd2.exe · zip · 개발 중 빌드)은 <see cref="UpdateManager.IsInstalled"/> 가 거짓이라 아무것도 안 한다.
/// 인터넷이 없거나 GitHub 이 막혀도 게임은 그대로 돈다 — 조용히 넘어간다. 진행은 제목 줄 끝에(<see cref="Status"/>).
/// </remarks>
internal static class Updater
{
    private const string RepoUrl = "https://github.com/Kyeongrok/fd2-remake";

    /// <summary>제목 줄에 붙일 글 — 확인 중 · 받는 중 · 받음 · 실패. 보일 것이 없으면 빈 글.</summary>
    public static string Status => DateTime.UtcNow < _hideAt ? _status : "";

    private static volatile string _status = "";
    private static DateTime _hideAt = DateTime.MinValue;

    private static void Show(string text, double seconds = 3600)
    {
        _status = text;
        _hideAt = DateTime.UtcNow.AddSeconds(seconds);
    }

    public static void CheckInBackground() => _ = Task.Run(CheckAsync);

    private static async Task CheckAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!manager.IsInstalled) return;

            Show("업데이트 확인 중…");
            var update = await manager.CheckForUpdatesAsync();
            if (update == null) { Show($"최신 버전입니다 (v{manager.CurrentVersion})", 3); return; }

            var target = update.TargetFullRelease;
            Show($"새 버전 v{target.Version} 받는 중… 0%");
            await manager.DownloadUpdatesAsync(update, p => Show($"새 버전 v{target.Version} 받는 중… {p}%"));
            Show($"새 버전 v{target.Version} 을(를) 받았습니다", 8);

            // 게임 창은 제 스레드에서 돈다 — 여기(뒤 스레드)서 띄우는 창이 게임 창 뒤에 묻히지 않게 맨 위로.
            bool now = MessageBoxW(IntPtr.Zero,
                $"새 버전 v{target.Version} 을(를) 받았습니다.\n지금 다시 시작해 적용하겠습니까?\n\n"
                + "(기록하지 않은 진행은 사라집니다. 아니오를 고르면 게임을 끌 때 적용됩니다.)",
                "용의기사2 — 업데이트", MB_YESNO | MB_ICONQUESTION | MB_TOPMOST | MB_SETFOREGROUND) == IDYES;
            if (now) manager.ApplyUpdatesAndRestart(target);
            else manager.WaitExitThenApplyUpdates(target, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] {ex.GetType().Name}: {ex.Message}");
            Show("업데이트를 확인하지 못했습니다(인터넷 연결을 확인해 주세요)", 4);
        }
    }

    private const uint MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;
    private const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
