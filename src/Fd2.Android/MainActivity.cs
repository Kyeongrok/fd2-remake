using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.Views;
using Fd2.Data;

namespace Fd2;

/// <summary>
/// 안드로이드 시작 화면 — 게임 파일이 앱 안에 없으면 폴더를 고르게 해(문서 트리) .DAT · .B24 를 앱 안으로 베끼고, 있으면 바로 게임.
/// </summary>
/// <remarks>원작 자료는 앱에 들어 있지 않다 — 갖고 계신 용의기사2 폴더(FDFIELD.DAT 가 있는 곳)를 폰에 넣고 고르면 된다.</remarks>
[Activity(Label = "용의기사2", MainLauncher = true, Icon = "@mipmap/appicon", Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
          ScreenOrientation = ScreenOrientation.SensorLandscape,
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.Navigation)]
public class MainActivity : Activity
{
    private const int PickFolder = 1;

    private GameView? _view;
    private TextView? _message;

    /// <summary>앱 안의 게임 폴더 — 폴더 고르기로 가져온 파일이 여기로 온다.</summary>
    private string GameDir => System.IO.Path.Combine(FilesDir!.AbsolutePath, "game");

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
        if (GameFolder.Missing(GameDir) == null) StartGame(GameDir);
        else if (ExternalGameDir is { } ext && GameFolder.Missing(ext) == null) StartGame(ext);
        else ShowPicker("용의기사2 게임 폴더(FDFIELD.DAT · FIGANI.DAT 가 있는 곳)를 골라 주세요.\n고른 폴더의 .DAT · .B24 파일을 앱 안으로 복사합니다.");
    }

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
        button.Click += (_, _) => StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree), PickFolder);
        layout.AddView(_message);
        layout.AddView(button);
        SetContentView(layout);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickFolder || resultCode != Result.Ok || data?.Data is not { } tree) return;
        _message!.Text = "복사하는 중…";
        Task.Run(() =>
        {
            string result;
            try { result = Import(tree); }
            catch (Exception e) { result = $"못 가져왔습니다: {e.Message}"; }
            RunOnUiThread(() =>
            {
                if (GameFolder.Missing(GameDir) == null) StartGame(GameDir);
                else _message!.Text = result + $"\n빠진 파일: {GameFolder.Missing(GameDir)} — 다른 폴더를 골라 주세요.";
            });
        });
    }

    /// <summary>고른 폴더(바로 아래 폴더 하나까지)에서 .DAT · .B24 를 앱 안으로.</summary>
    private string Import(Android.Net.Uri tree)
    {
        Directory.CreateDirectory(GameDir);
        string root = DocumentsContract.GetTreeDocumentId(tree)!;
        int copied = CopyFrom(tree, root);
        if (copied == 0 || GameFolder.Missing(GameDir) != null)
            foreach (var (id, _, mime) in Children(tree, root))
                if (mime == DocumentsContract.Document.MimeTypeDir) copied += CopyFrom(tree, id);
        return $"{copied}개 파일을 가져왔습니다.";
    }

    private int CopyFrom(Android.Net.Uri tree, string dirId)
    {
        int n = 0;
        foreach (var (id, name, mime) in Children(tree, dirId))
        {
            if (mime == DocumentsContract.Document.MimeTypeDir) continue;
            string ext = System.IO.Path.GetExtension(name).ToUpperInvariant();
            if (ext is not (".DAT" or ".B24")) continue;
            var uri = DocumentsContract.BuildDocumentUriUsingTree(tree, id)!;
            using var input = ContentResolver!.OpenInputStream(uri)!;
            using var output = File.Create(System.IO.Path.Combine(GameDir, name.ToUpperInvariant()));
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

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideBars();
    }
}
