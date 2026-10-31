using Android.Content;
using Android.Content.PM;
using Fd2.Data;

// 다운로드 폴더의 버전 묶음(.zip)을 찾아 읽는다 — 안드로이드 11 부터는 「모든 파일 접근」(사용자가 설정에서 한 번 켬), 그 아래는 저장소 읽기
[assembly: UsesPermission(Android.Manifest.Permission.ReadExternalStorage, MaxSdkVersion = 32)]
[assembly: UsesPermission("android.permission.MANAGE_EXTERNAL_STORAGE")]

namespace Fd2;

/// <summary>
/// 카페 · 사이트에서 받은 버전 묶음(.zip) — 다운로드 폴더에서 FD2.EXE 가 든 묶음을 찾아 목록으로 띄우고, 고르면 풀어서 그 버전으로 켠다.
/// 파일 앱에서 .zip 을 「용의기사2로 열기」 해도 같다. 푸는 일은 <see cref="VersionZip"/>(PC 에서도 시험한다).
/// </summary>
/// <remarks>
/// 켤 때 저절로 묻는 목록에서 「나중에」를 고른 묶음은 다시 저절로 묻지 않는다(files/zips-skipped.txt) — 타이틀 「+ 버전 더하기」에서는 늘 보인다.
/// 다운로드 폴더를 읽을 권한이 없으면 「+ 버전 더하기」가 그 권한을 켜러 간다.
/// </remarks>
public partial class MainActivity
{
    private const int FileAccessRequest = 2;

    private string SkippedFile => System.IO.Path.Combine(FilesDir!.AbsolutePath, "zips-skipped.txt");

    /// <summary>다운로드 폴더를 읽을 수 있는가.</summary>
    private bool HasFileAccess => OperatingSystem.IsAndroidVersionAtLeast(30)
        ? Android.OS.Environment.IsExternalStorageManager
        : CheckSelfPermission(Android.Manifest.Permission.ReadExternalStorage) == Permission.Granted;

    private void RequestFileAccess()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var intent = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission, Android.Net.Uri.Parse("package:" + PackageName));
            try { StartActivityForResult(intent, FileAccessRequest); }
            catch (ActivityNotFoundException) { StartActivityForResult(new Intent(Android.Provider.Settings.ActionManageAllFilesAccessPermission), FileAccessRequest); }
        }
        else RequestPermissions([Android.Manifest.Permission.ReadExternalStorage], FileAccessRequest);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == FileAccessRequest && HasFileAccess) OfferDownloadedZips(auto: false);
    }

    /// <summary>다운로드 폴더(과 그 바로 아래 폴더들)의 버전 묶음 — 이미 가져온 이름은 뺀다.</summary>
    private List<string> DownloadedZips()
    {
#pragma warning disable CA1422
        string? root = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
#pragma warning restore CA1422
        if (root == null || !Directory.Exists(root)) return [];
        var have = Versions().Select(v => v.Name).ToHashSet();
        var found = new List<string>();
        try
        {
            var dirs = Directory.GetDirectories(root).Prepend(root);
            foreach (var zip in dirs.SelectMany(d => SafeFiles(d, "*.zip")).OrderByDescending(z => File.GetLastWriteTimeUtc(z)))
            {
                if (have.Contains(VersionZip.VersionName(zip))) continue;
                try { using var f = File.OpenRead(zip); if (VersionZip.IsVersion(f)) found.Add(zip); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return found;

        static IEnumerable<string> SafeFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        }
    }

    /// <summary>
    /// 받은 버전 묶음 목록을 띄운다. auto 면 켤 때 저절로 — 새 묶음이 없거나 권한이 없으면 아무것도 안 띄운다.
    /// </summary>
    private void OfferDownloadedZips(bool auto)
    {
        if (!HasFileAccess) { if (!auto) ShowAddVersion(); return; }
        Task.Run(() =>
        {
            var zips = DownloadedZips();
            var skipped = File.Exists(SkippedFile) ? File.ReadAllLines(SkippedFile).ToHashSet() : [];
            if (auto) zips = zips.Where(z => !skipped.Contains(z)).ToList();
            RunOnUiThread(() =>
            {
                if (zips.Count == 0)
                {
                    if (!auto) Toast.MakeText(this, "다운로드 폴더에 새 버전 묶음(FD2.EXE 가 든 .zip)이 없습니다.", ToastLength.Long)?.Show();
                    return;
                }
                var names = zips.Select(z => System.IO.Path.GetFileName(z)).ToArray();
                new AlertDialog.Builder(this)
                    .SetTitle("받은 버전 묶음 — 고르면 풀어서 그 버전으로 켭니다")!
                    .SetItems(names, (_, e) => ImportZip(names[e.Which], () => File.OpenRead(zips[e.Which])))!
                    .SetNegativeButton("나중에", (_, _) =>
                    {
                        if (auto) File.AppendAllLines(SkippedFile, zips);
                    })!
                    .Show();
            });
        });
    }

    /// <summary>타이틀 「+ 버전 더하기」 — 받은 묶음 목록, 권한 켜기, 폴더 고르기.</summary>
    private void ShowAddVersion()
    {
        var items = new List<(string Label, Action Run)>();
        if (HasFileAccess) items.Add(("다운로드 폴더의 버전 묶음(.zip) 보기", () => OfferDownloadedZips(auto: false)));
        else items.Add(("다운로드 폴더의 버전 묶음(.zip) 보기 — 파일 접근 허용(한 번만)", RequestFileAccess));
        items.Add(("게임 폴더 고르기", PickNewFolder));
        new AlertDialog.Builder(this)
            .SetTitle("버전 더하기")!
            .SetItems(items.Select(i => i.Label).ToArray(), (_, e) => items[e.Which].Run())!
            .SetNegativeButton("닫기", (_, _) => { })!
            .Show();
    }

    /// <summary>파일 앱에서 「용의기사2로 열기」 한 .zip 이면 가져오고 true.</summary>
    private bool OpenedZip(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data is not { } uri) return false;
        string name = DisplayName(uri) ?? "받은 버전.zip";
        ImportZip(name, () =>
        {
            // 묶음 읽기는 앞뒤로 오가야 해서 앱 캐시에 한 번 베껴 둔다
            string copy = System.IO.Path.Combine(CacheDir!.AbsolutePath, "opened.zip");
            using (var input = ContentResolver!.OpenInputStream(uri)!)
            using (var output = File.Create(copy)) input.CopyTo(output);
            return new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
        });
        return true;
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OpenedZip(intent);
    }

    private string? DisplayName(Android.Net.Uri uri)
    {
        try
        {
            using var c = ContentResolver!.Query(uri, [Android.Provider.OpenableColumns.DisplayName], null, null, null);
            return c != null && c.MoveToFirst() ? c.GetString(0) : uri.LastPathSegment;
        }
        catch (Java.Lang.Exception) { return uri.LastPathSegment; }
    }

    /// <summary>묶음을 versions/묶음 이름 에 풀고 그 버전으로 켠다. 버전 묶음이 아니면 알리고 하던 대로.</summary>
    private void ImportZip(string zipName, Func<Stream> open)
    {
        ShowPicker($"{zipName} 푸는 중…");
        Task.Run(() =>
        {
            string target = System.IO.Path.Combine(VersionsDir, VersionZip.VersionName(zipName));
            bool ok;
            string error = "";
            try
            {
                using var zip = open();
                ok = VersionZip.Extract(zip, target, p => RunOnUiThread(() => _message!.Text = $"{zipName} 푸는 중… {p}%"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or Java.Lang.Exception)
            {
                (ok, error) = (false, e.Message);
            }
            RunOnUiThread(() =>
            {
                if (ok)
                {
                    var s = Settings.Load();
                    s.GameDir = target;
                    s.Save();
                    Toast.MakeText(this, $"{System.IO.Path.GetFileName(target)} 버전으로 켭니다.", ToastLength.Short)?.Show();
                    StartGame(target);
                    return;
                }
                Toast.MakeText(this, $"{zipName} 은(는) 용의기사2 버전 묶음이 아닙니다(FD2.EXE · FDFIELD.DAT 가 없음){(error.Length > 0 ? $" — {error}" : "")}.", ToastLength.Long)?.Show();
                string chosen = Settings.Load().GameDir;
                if (GameFolder.Missing(chosen) == null) StartGame(chosen);
                else if (Versions() is { Count: > 0 } versions) StartGame(versions[0].Dir);
                else DownloadOriginal();
            });
        });
    }
}
