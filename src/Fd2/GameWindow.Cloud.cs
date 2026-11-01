namespace Fd2;

/// <summary>세이브 연동 화면 쪽 — 구글 로그인 · 올리기 · 받기를 뒤에서 돌리고 결과를 알림으로(<see cref="CloudSave"/>).</summary>
internal sealed partial class GameWindow
{
    private CloudSave? _cloud;
    private volatile bool _cloudBusy;

    private CloudSave Cloud => _cloud ??= new CloudSave(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "google-token.dat"), OpenBrowser);

    /// <summary>시스템 브라우저로 주소를 연다 — 플랫폼마다.</summary>
    private partial void OpenBrowser(string url);

    private string CloudDir => Path.GetDirectoryName(SavePath)!;

    /// <summary>
    /// 클라우드 기록 이름 — 폴더 이름은 PC 와 폰이 다를 수 있어(압축 파일 이름 · 폴더 이름) FD2.EXE 내용으로 정한다. 같은 버전이면 어느 기기든 같은 기록을 본다.
    /// </summary>
    private string CloudKey
    {
        get
        {
            string exe = Path.Combine(_dir, "FD2.EXE");
            return File.Exists(exe) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(exe)))[..16] : Path.GetFileName(CloudDir);
        }
    }

    /// <summary>세이브 연동 창 — 플랫폼마다.</summary>
    private partial void OpenCloudDialog();

    private void CloudRun(string start, Func<Task<string>> work)
    {
        if (_cloudBusy) { Toast("세이브 연동이 아직 돌고 있습니다"); return; }
        if (!CloudSave.Configured) { Toast("세이브 연동이 아직 설정되지 않았습니다"); return; }
        _cloudBusy = true;
        Toast(start);
        _ = Task.Run(async () =>
        {
            try { Toast(await work()); }
            catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException
                                      or System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or System.Net.Sockets.SocketException)
            {
                Toast($"세이브 연동 실패 — {e.Message}");
            }
            finally { _cloudBusy = false; }
        });
    }

    private void CloudLogin() => CloudRun("브라우저에서 구글 계정으로 로그인하세요", async () => { await Cloud.Login(); return "구글 계정을 연결했습니다"; });

    private void CloudUpload() => CloudRun("클라우드에 올리는 중…", async () =>
        await Cloud.Upload(CloudDir, CloudKey) is var n and > 0 ? $"기록 파일 {n}개를 올렸습니다" : "올릴 기록이 없습니다");

    private void CloudDownload() => CloudRun("클라우드에서 받는 중…", async () =>
    {
        int n = await Cloud.Download(CloudDir, CloudKey);
        _hasSaves = null;
        return n < 0 ? "클라우드에 이 버전 기록이 없습니다" : $"기록 파일 {n}개를 받았습니다 — 불러오기에서 고르세요(이전 기록은 백업 폴더에)";
    });

    private void CloudLogout()
    {
        Cloud.Logout();
        Toast("이 기기의 구글 연결을 끊었습니다");
    }
}
