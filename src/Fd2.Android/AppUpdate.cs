using System.Text.Json;
using Android.Content;
using Android.Content.PM;

// 새 판 APK 를 앱이 직접 깔도록 묻는다(PackageInstaller)
[assembly: UsesPermission("android.permission.REQUEST_INSTALL_PACKAGES")]

namespace Fd2;

/// <summary>
/// 자동 업데이트 — 켤 때 GitHub 최신 릴리즈(fd2-v*)를 보고, 이 앱보다 새 판이면 묻고 APK 를 받아 PackageInstaller 로 깐다.
/// </summary>
/// <remarks>
/// 덮어 깔려면 릴리즈 APK 가 늘 같은 키로 서명돼야 한다(저장소 비밀값 ANDROID_KEYSTORE_B64 · ANDROID_KEYSTORE_PASS — fd2-release.yml).
/// 처음 까는 판은 안드로이드가 「이 출처의 앱 설치 허용」을 한 번 묻는다. 못 받거나 거절하면 조용히 넘어간다 — 게임은 그대로 돈다.
/// </remarks>
internal static class AppUpdate
{
    private const string LatestApi = "https://api.github.com/repos/Kyeongrok/fd2-remake/releases/latest";

    /// <summary>설치가 끝났을 때 받는 알림 이름.</summary>
    private const string InstallAction = "com.kyeongrok.fd2remake.INSTALLED";

    public static void Check(Activity activity)
    {
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("fd2-remake-android");
                using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi));
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith("fd2-v") || !Version.TryParse(tag[5..], out var latest)) return;
                string? apk = doc.RootElement.GetProperty("assets").EnumerateArray()
                    .Select(a => a.GetProperty("browser_download_url").GetString())
                    .FirstOrDefault(u => u != null && u.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
                if (apk == null || !Version.TryParse(CurrentVersion(activity), out var current) || latest <= current) return;
                activity.RunOnUiThread(() => Ask(activity, latest, apk));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        });
    }

    private static string CurrentVersion(Activity a) =>
        a.PackageManager?.GetPackageInfo(a.PackageName!, 0)?.VersionName ?? "0.0.0";

    private static void Ask(Activity activity, Version latest, string url)
    {
        new AlertDialog.Builder(activity)
            .SetTitle("새 버전")!
            .SetMessage($"용의기사2 새 버전 {latest} 이(가) 나왔습니다(지금 {CurrentVersion(activity)}). 받아서 설치할까요?")!
            .SetPositiveButton("설치", (_, _) => Download(activity, url))!
            .SetNegativeButton("나중에", (_, _) => { })!
            .Show();
    }

    private static void Download(Activity activity, string url)
    {
        var toast = Toast.MakeText(activity, "새 버전을 받는 중…", ToastLength.Long);
        toast?.Show();
        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var bytes = await http.GetByteArrayAsync(url);
                activity.RunOnUiThread(() => Install(activity, bytes));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                activity.RunOnUiThread(() => Toast.MakeText(activity, $"새 버전을 받지 못했습니다: {e.Message}", ToastLength.Long)?.Show());
            }
        });
    }

    /// <summary>PackageInstaller 세션에 APK 를 쓰고 맡긴다 — 안드로이드가 「업데이트할까요?」를 묻는다(<see cref="InstallResult"/>).</summary>
    private static void Install(Activity activity, byte[] apk)
    {
        try
        {
            var installer = activity.PackageManager!.PackageInstaller;
            var p = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            p.SetAppPackageName(activity.PackageName);
            int id = installer.CreateSession(p);
            using var session = installer.OpenSession(id);
            using (var output = session.OpenWrite("fd2.apk", 0, apk.Length))
            {
                output.Write(apk, 0, apk.Length);
                session.Fsync(output);
            }
            var intent = new Intent(activity, typeof(InstallResult)).SetAction(InstallAction);
            var flags = PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
            var pending = PendingIntent.GetBroadcast(activity, id, intent, flags)!;
            session.Commit(pending.IntentSender!);
        }
        catch (Exception e) when (e is Java.IO.IOException or Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
        {
            Toast.MakeText(activity, $"설치하지 못했습니다: {e.Message}", ToastLength.Long)?.Show();
        }
    }

    /// <summary>설치 결과 — 사용자 확인이 필요하면 안드로이드의 확인 화면을 띄운다.</summary>
    [BroadcastReceiver(Exported = false)]
    public sealed class InstallResult : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null) return;
            var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, -999);
            if (status == PackageInstallStatus.PendingUserAction)
            {
#pragma warning disable CA1422
                if (intent.GetParcelableExtra(Intent.ExtraIntent) is Intent confirm)
#pragma warning restore CA1422
                {
                    confirm.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(confirm);
                }
            }
            else if (status != PackageInstallStatus.Success)
                Toast.MakeText(context, $"설치하지 못했습니다: {intent.GetStringExtra(PackageInstaller.ExtraStatusMessage)}", ToastLength.Long)?.Show();
        }
    }
}
