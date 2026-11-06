using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;

namespace Fd2;

/// <summary>
/// 세이브 보내기(w-169) — 게임이 골라 둔 세이브 파일을 압축 없이 안드로이드 공유 창(카톡 · 이메일 · 메신저 …)에 넘긴다.
/// 다른 앱이 앱 안 파일을 읽을 수 있게 내용 제공자(<see cref="Files"/>)로 <c>content://</c> 주소를 주고 그 앱에 읽기만 허락한다.
/// AndroidX FileProvider 를 들이지 않으려고 작은 제공자를 따로 둔다 — 게임이 묶어 둔 곳(임시 폴더/fd2-share)의 파일만 낸다.
/// </summary>
internal static class SaveShare
{
    public const string Authority = "com.kyeongrok.fd2remake.saves";

    public static string ShareDir => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fd2-share");

    /// <summary>세이브 파일들(1 ~ 2개 — 기록과 공통 가방 옆 파일)을 압축 없이 그대로 보낸다(fb-89).</summary>
    public static void Share(Activity activity, string[] files, string title, string note)
    {
        if (files.Length == 0) return;
        try
        {
            var uris = files.Select(f => Android.Net.Uri.Parse($"content://{Authority}/{Android.Net.Uri.Encode(System.IO.Path.GetFileName(f))}")!).ToList();
            var send = new Intent(uris.Count == 1 ? Intent.ActionSend : Intent.ActionSendMultiple)
                .SetType("application/octet-stream")!
                .PutExtra(Intent.ExtraSubject, title)!
                .PutExtra(Intent.ExtraText, note)!
                .AddFlags(ActivityFlags.GrantReadUriPermission)!;
            if (uris.Count == 1) send.PutExtra(Intent.ExtraStream, uris[0]);
            else send.PutParcelableArrayListExtra(Intent.ExtraStream, uris.Cast<IParcelable>().ToList());
            // 몇몇 앱은 ClipData 로 권한을 받는다
            var clip = ClipData.NewRawUri(title, uris[0]);
            foreach (var u in uris.Skip(1)) clip.AddItem(new ClipData.Item(u));
            send.ClipData = clip;
            activity.StartActivity(Intent.CreateChooser(send, "세이브 보내기")!.AddFlags(ActivityFlags.GrantReadUriPermission));
        }
        catch (Exception e) when (e is ActivityNotFoundException or Java.Lang.SecurityException)
        {
            Toast.MakeText(activity, $"보낼 앱을 찾지 못했습니다: {e.Message}", ToastLength.Long)?.Show();
        }
    }

    /// <summary>골라 둔 세이브 파일을 다른 앱에 읽기로만 낸다 — 이름 · 크기(카톡 · 메일이 파일 이름으로 씀)와 내용.</summary>
    [ContentProvider([Authority], Exported = false, GrantUriPermissions = true)]
    public sealed class Files : ContentProvider
    {
        public override bool OnCreate() => true;

        /// <summary>주소의 파일 이름 → 묶어 둔 곳의 파일(다른 폴더로 빠지는 이름은 막는다).</summary>
        private static Java.IO.File? Resolve(Android.Net.Uri? uri)
        {
            string name = System.IO.Path.GetFileName(uri?.LastPathSegment ?? "");
            if (name.Length == 0) return null;
            string path = System.IO.Path.Combine(ShareDir, name);
            return File.Exists(path) ? new Java.IO.File(path) : null;
        }

        public override ParcelFileDescriptor? OpenFile(Android.Net.Uri uri, string mode) =>
            Resolve(uri) is { } f ? ParcelFileDescriptor.Open(f, ParcelFileMode.ReadOnly) : throw new Java.IO.FileNotFoundException(uri.ToString());

        public override string? GetType(Android.Net.Uri uri) =>
            (uri.LastPathSegment ?? "").EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "application/octet-stream";

        public override ICursor? Query(Android.Net.Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder)
        {
            if (Resolve(uri) is not { } f) return null;
            var cols = projection is { Length: > 0 } ? projection : [IOpenableColumns.DisplayName, IOpenableColumns.Size];
            var c = new MatrixCursor(cols, 1);
            c.AddRow(cols.Select(col => col == IOpenableColumns.DisplayName ? new Java.Lang.String(f.Name) : col == IOpenableColumns.Size ? (Java.Lang.Object)new Java.Lang.Long(f.Length()) : null!).ToArray());
            return c;
        }

        public override Android.Net.Uri? Insert(Android.Net.Uri uri, ContentValues? values) => null;
        public override int Delete(Android.Net.Uri uri, string? selection, string[]? selectionArgs) => 0;
        public override int Update(Android.Net.Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
    }
}
