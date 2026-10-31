using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.Views;
using Fd2.Data;

// 원본 자료(fd2-assets 릴리즈)와 새 판 APK 를 받는다
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace Fd2;

/// <summary>
/// 안드로이드 시작 화면 — 게임 파일이 앱 안에 없으면 원본 자료를 받아 풀고(<see cref="DownloadOriginal"/>), 있으면 바로 게임.
/// 다른 버전(모션패치 …)은 폴더를 골라(문서 트리) .DAT · .B24 를 앱 안으로 베낀다. 켤 때마다 새 판이 나왔는지 본다(<see cref="AppUpdate"/>).
/// </summary>
/// <remarks>
/// 판(원본 · 모션패치 …)은 가져온 폴더마다 files/versions/폴더이름 에 따로 둔다. 게임의 타이틀 「판:」 줄에서 고르거나 더 가져온다.
/// 고른 판은 설정(Settings.GameDir)에 남아 다음에 켤 때도 그 판으로 시작한다.
/// </remarks>
/// <remarks>원작 자료는 앱에 들어 있지 않다 — 갖고 계신 용의기사2 폴더(FDFIELD.DAT 가 있는 곳)를 폰에 넣고 고르면 된다.</remarks>
[Activity(Label = "용의기사2", MainLauncher = true, Icon = "@mipmap/appicon", Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
          ScreenOrientation = ScreenOrientation.SensorLandscape,
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.Navigation,
          LaunchMode = LaunchMode.SingleTask)]
// 파일 앱 · 브라우저에서 받은 .zip 을 「용의기사2로 열기」 — 버전 묶음이면 풀어서 그 버전으로(MainActivity.Zips.cs)
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataMimeTypes = ["application/zip", "application/x-zip-compressed"])]
public partial class MainActivity : Activity
{
    private const int PickFolder = 1;

    private GameView? _view;
    private TextView? _message;

    /// <summary>앱 안의 옛 게임 폴더(0.5.1 이 가져온 곳) — 판 하나로 친다.</summary>
    private string GameDir => System.IO.Path.Combine(FilesDir!.AbsolutePath, "game");

    /// <summary>가져온 판들이 쌓이는 곳.</summary>
    private string VersionsDir => System.IO.Path.Combine(FilesDir!.AbsolutePath, "versions");

    /// <summary>
    /// 앱 바깥 저장소의 게임 폴더(Android/data/com.kyeongrok.fd2remake/files/game) — USB 로 여기에 넣어도 된다.
    /// 다 있으면 고르기 없이 이것을 쓴다.
    /// </summary>
    private string? ExternalGameDir => GetExternalFilesDir(null) is { } d ? System.IO.Path.Combine(d.AbsolutePath, "game") : null;

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        HideBars();
        GameWindow.VersionsProvider = Versions;
        GameWindow.VersionSwitcher = dir => RunOnUiThread(() => StartGame(dir));
        GameWindow.VersionAdder = () => RunOnUiThread(ShowAddVersion);
        if (OpenedZip(Intent)) return;
        string chosen = Settings.Load().GameDir;
        var versions = Versions();
        if (chosen.Length > 0 && GameFolder.Missing(chosen) == null) StartGame(chosen);
        else if (versions.Count > 0) StartGame(versions[0].Dir);
        else DownloadOriginal();
        AppUpdate.Check(this);
        if (_view != null) OfferDownloadedZips(auto: true);
    }

    /// <summary>원본 자료 묶음 — 리메이크가 읽는 .DAT · .B24 · FD2.EXE 와 수치 표 tables/*.json(GitHub 릴리즈 fd2-assets).</summary>
    private const string OriginalUrl = "https://github.com/Kyeongrok/fd2-remake/releases/download/fd2-assets/fd2-original.zip";

    /// <summary>
    /// 가져온 버전이 하나도 없으면 원본 자료를 받아 versions/용의기사2_original 에 풀고 바로 시작한다(대항해시대3 리메이크가 음악 · 동영상을 받듯).
    /// 못 받으면 예전처럼 폰의 게임 폴더를 고르게 한다.
    /// </summary>
    private void DownloadOriginal()
    {
        ShowPicker("용의기사2 원본 자료를 받는 중… (약 8MB)");
        Task.Run(async () =>
        {
            string target = System.IO.Path.Combine(VersionsDir, "용의기사2_original"), part = target + ".part";
            string zip = System.IO.Path.Combine(CacheDir!.AbsolutePath, "fd2-original.zip");
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                using (var res = await http.GetAsync(OriginalUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    res.EnsureSuccessStatusCode();
                    long total = res.Content.Headers.ContentLength ?? 0, got = 0;
                    await using var input = await res.Content.ReadAsStreamAsync();
                    await using var output = File.Create(zip);
                    var buf = new byte[81920];
                    int n, shown = -1;
                    while ((n = await input.ReadAsync(buf)) > 0)
                    {
                        await output.WriteAsync(buf.AsMemory(0, n));
                        got += n;
                        int pct = total > 0 ? (int)(got * 100 / total) : -1;
                        if (pct == shown) continue;
                        shown = pct;
                        string text = $"용의기사2 원본 자료를 받는 중… {(pct >= 0 ? $"{pct}%" : $"{got / 1024}KB")}";
                        RunOnUiThread(() => _message!.Text = text);
                    }
                }
                RunOnUiThread(() => _message!.Text = "푸는 중…");
                if (Directory.Exists(part)) Directory.Delete(part, recursive: true);
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, part);
                if (GameFolder.Missing(part) is { } missing) throw new IOException($"{missing} 이(가) 묶음에 없습니다");
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(part, target);
                File.Delete(zip);
                RunOnUiThread(() =>
                {
                    var s = Settings.Load();
                    s.GameDir = target;
                    s.Save();
                    StartGame(target);
                });
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or InvalidDataException or UnauthorizedAccessException)
            {
                RunOnUiThread(() => _message!.Text = $"원본 자료를 받지 못했습니다({e.Message}).\n인터넷을 확인하고 앱을 다시 켜거나, 폰에 넣어 둔 용의기사2 게임 폴더(FDFIELD.DAT · FIGANI.DAT 가 있는 곳)를 골라 주세요.");
            }
        });
    }

    /// <summary>쓸 수 있는 판들 — 가져온 판들, 옛 game 폴더, USB 로 넣은 폴더.</summary>
    private List<(string Name, string Dir)> Versions()
    {
        var list = new List<(string, string)>();
        if (Directory.Exists(VersionsDir))
            foreach (var d in Directory.GetDirectories(VersionsDir).OrderBy(d => d))
                if (GameFolder.Missing(d) == null) list.Add((System.IO.Path.GetFileName(d), d));
        if (GameFolder.Missing(GameDir) == null) list.Add(("처음 가져온 버전", GameDir));
        if (ExternalGameDir is { } ext && GameFolder.Missing(ext) == null) list.Add(("USB 로 넣은 버전", ext));
        return list;
    }

    private void PickNewFolder() => StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree), PickFolder);

    private void HideBars()
    {
        if (Window?.DecorView is { } v)
#pragma warning disable CA1422
            v.SystemUiFlags = SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky;
#pragma warning restore CA1422
    }

    private void ShowPicker(string text)
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(48, 48, 48, 48);
        _message = new TextView(this) { Text = text, TextSize = 18 };
        var button = new Button(this) { Text = "게임 폴더 고르기" };
        button.Click += (_, _) => PickNewFolder();
        layout.AddView(_message);
        layout.AddView(button);
        SetContentView(layout);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == FileAccessRequest) { if (HasFileAccess) OfferDownloadedZips(auto: false); return; }
        if (requestCode != PickFolder || resultCode != Result.Ok || data?.Data is not { } tree) return;
        ShowPicker("복사하는 중…");
        Task.Run(() =>
        {
            string target = NewVersionDir(tree), result;
            try { result = Import(tree, target); }
            catch (Exception e) { result = $"못 가져왔습니다: {e.Message}"; }
            RunOnUiThread(() =>
            {
                if (GameFolder.Missing(target) == null)
                {
                    var s = Settings.Load();
                    s.GameDir = target;
                    s.Save();
                    StartGame(target);
                }
                else
                {
                    try { Directory.Delete(target, recursive: true); } catch (IOException) { }
                    _message!.Text = result + "\n빠진 파일이 있어 버전으로 쓸 수 없습니다 — 다른 폴더를 골라 주세요.";
                }
            });
        });
    }

    /// <summary>새 판 폴더 이름 — 고른 폴더 이름(겹치면 뒤에 (2) …).</summary>
    private string NewVersionDir(Android.Net.Uri tree)
    {
        string id = DocumentsContract.GetTreeDocumentId(tree) ?? "버전";
        string name = id.Split(':', '/').LastOrDefault(p => p.Length > 0) ?? "버전";
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string dir = System.IO.Path.Combine(VersionsDir, name);
        for (int k = 2; Directory.Exists(dir); k++) dir = System.IO.Path.Combine(VersionsDir, $"{name} ({k})");
        return dir;
    }

    /// <summary>고른 폴더(바로 아래 폴더 하나까지)에서 .DAT · .B24 · FD2.EXE 를 target 으로.</summary>
    private string Import(Android.Net.Uri tree, string target)
    {
        Directory.CreateDirectory(target);
        string root = DocumentsContract.GetTreeDocumentId(tree)!;
        int copied = CopyFrom(tree, root, target);
        if (copied == 0 || GameFolder.Missing(target) != null)
            foreach (var (id, _, mime) in Children(tree, root))
                if (mime == DocumentsContract.Document.MimeTypeDir) copied += CopyFrom(tree, id, target);
        return $"{copied}개 파일을 가져왔습니다.";
    }

    private int CopyFrom(Android.Net.Uri tree, string dirId, string target)
    {
        int n = 0;
        foreach (var (id, name, mime) in Children(tree, dirId))
        {
            if (mime == DocumentsContract.Document.MimeTypeDir) continue;
            string ext = System.IO.Path.GetExtension(name).ToUpperInvariant();
            if (ext is not (".DAT" or ".B24") && !name.Equals("FD2.EXE", StringComparison.OrdinalIgnoreCase)) continue;   // 실행 파일은 규칙 표(GameTables)만 읽는다
            var uri = DocumentsContract.BuildDocumentUriUsingTree(tree, id)!;
            using var input = ContentResolver!.OpenInputStream(uri)!;
            using var output = File.Create(System.IO.Path.Combine(target, name.ToUpperInvariant()));
            input.CopyTo(output);
            n++;
        }
        return n;
    }

    private List<(string Id, string Name, string Mime)> Children(Android.Net.Uri tree, string dirId)
    {
        var list = new List<(string, string, string)>();
        var uri = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, dirId)!;
        string[] cols = [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType];
        using var c = ContentResolver!.Query(uri, cols, null, null, null);
        while (c != null && c.MoveToNext()) list.Add((c.GetString(0)!, c.GetString(1)!, c.GetString(2) ?? ""));
        return list;
    }

    private void StartGame(string dir)
    {
        _view = new GameView(this, dir);
        SetContentView(_view);
        _view.RequestFocus();
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e) => _view?.Key(keyCode, true) == true || base.OnKeyDown(keyCode, e);
    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e) => _view?.Key(keyCode, false) == true || base.OnKeyUp(keyCode, e);

    protected override void OnPause()
    {
        base.OnPause();
        _view?.Background(true);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _view?.Background(false);
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideBars();
    }
}
