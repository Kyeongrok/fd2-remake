using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Fd2.Data;
using Microsoft.Win32;

namespace Fd2.Editor;

/// <summary>
/// 용의기사2 편집기 — 원본 게임 폴더를 그 자리에서 읽어 보여 준다.
/// 맵 캐릭터 탭은 FDICON.B24 의 네 방향 걸음, 전투 모션 탭은 FIGANI.DAT 를 한 컷씩.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] FacingNames = ["앞 (아래)", "왼옆", "뒤 (위)", "오른옆"];
    private static readonly int[] FacingOrder = [0, 2, 1, 3];   // 앞 · 뒤 · 왼 · 오른 순으로 늘어놓는다
    private static readonly int[] WalkCycle = [0, 1, 2, 1];

    private string _dir = "";
    private uint[] _pal = [];
    private IconBank? _icons;

    private readonly DispatcherTimer _walkTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private int _walkStep;

    private readonly DispatcherTimer _playTimer = new();
    private Motion? _motion;
    private int _frame, _held;

    public MainWindow(string dir)
    {
        InitializeComponent();
        _walkTimer.Tick += (_, _) => { if (WalkAnim.IsChecked == true) { _walkStep++; ShowFacings(); } };
        _playTimer.Tick += OnPlayTick;
        Open(dir);
    }

    // ── 게임 폴더 ───────────────────────────────────────────────────────────

    private void Open(string dir)
    {
        if (GameFolder.Missing(dir) is { } f)
        {
            Status.Text = $"'{dir}' 에 {f} 가 없습니다 — 파일 > 게임 폴더 열기로 골라 주세요.";
            return;
        }
        _dir = dir;
        _pal = GameFolder.Palette(dir);
        _icons = new IconBank(System.IO.Path.Combine(dir, "FDICON.B24"), _pal);

        IconList.ItemsSource = Enumerable.Range(0, _icons.Characters)
            .Select(i => new CharItem(i, $"{i} {CharacterNames.Of(i)}", ToBitmap(_icons.Frame(i, 0), Rle.Tile, Rle.Tile))).ToList();
        int motions = Motion.Characters(dir);
        MotionList.ItemsSource = Enumerable.Range(0, motions)
            .Select(i => new CharItem(i, $"{i} {CharacterNames.Of(i)}", i < _icons.Characters ? ToBitmap(_icons.Frame(i, 0), Rle.Tile, Rle.Tile) : null)).ToList();
        IconList.SelectedIndex = 0;
        MotionList.SelectedIndex = 0;
        _walkTimer.Start();
        Title = $"용의기사2 편집기 — {dir}";
        FillVersionBoxes();
        foreach (var v in FindVersions()) MotionFiles.Ensure(v.Dir);   // 버전마다 .obs 가 없으면 뽑아 둔다
        FillMotionVersions();
        FillMusic();
        Status.Text = $"캐릭터 그림 {_icons.Characters}명 · 전투 모션 {motions}명  ·  ←/→ 한 컷씩, 스페이스 재생/정지";
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "용의기사2 게임 폴더(FDFIELD.DAT 가 있는 곳)", InitialDirectory = _dir };
        if (dlg.ShowDialog(this) == true) Open(dlg.FolderName);
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    // ── 여러 판 ────────────────────────────────────────────────────────────

    private readonly List<string> _extraVersions = new();

    private List<VersionItem> FindVersions() =>
        GameVersions.Discover(_extraVersions.Append(_dir).Where(d => d.Length > 0))
            .Select(v => new VersionItem(v.Dir, $"{v.Name}  —  {v.Dir}")).ToList();

    private void OnVersionMenuOpened(object sender, RoutedEventArgs e)
    {
        VersionMenu.Items.Clear();
        foreach (var v in FindVersions())
        {
            var item = new MenuItem { Header = v.Label, IsCheckable = true, IsChecked = string.Equals(v.Dir, _dir, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => Open(v.Dir);
            VersionMenu.Items.Add(item);
        }
        if (VersionMenu.Items.Count == 0) VersionMenu.Items.Add(new MenuItem { Header = "(찾은 버전이 없습니다)", IsEnabled = false });
    }

    private void FillVersionBoxes()
    {
        var list = FindVersions();
        BaseVersion.ItemsSource = list;
        OtherVersion.ItemsSource = list;
        BaseVersion.SelectedItem = list.FirstOrDefault(v => v.Dir.Contains("original", StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
        OtherVersion.SelectedItem = list.FirstOrDefault(v => v != BaseVersion.SelectedItem && string.Equals(v.Dir, _dir, StringComparison.OrdinalIgnoreCase))
                                    ?? list.FirstOrDefault(v => v != BaseVersion.SelectedItem);
    }

    private void OnAddVersion(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "버전 폴더(FDFIELD.DAT 가 있는 곳)" };
        if (dlg.ShowDialog(this) != true) return;
        if (GameFolder.Missing(dlg.FolderName) is { } f) { Status.Text = $"{dlg.FolderName} 에 {f} 가 없습니다."; return; }
        _extraVersions.Add(dlg.FolderName);
        FillVersionBoxes();
    }

    private void OnCompare(object sender, RoutedEventArgs e)
    {
        if (BaseVersion.SelectedItem is not VersionItem a || OtherVersion.SelectedItem is not VersionItem b) return;
        Status.Text = "비교하는 중…";
        var rows = GameVersions.Diff(a.Dir, b.Dir)
            .Where(d => d.Status != "같음")
            .Select(d => new DiffRow(d.File, d.Status,
                d.ChangedEntries.Count == 0 ? "" : $"{d.ChangedEntries.Count}개: " + string.Join(", ", d.ChangedEntries.Take(40)) + (d.ChangedEntries.Count > 40 ? " …" : "")))
            .ToList();
        DiffGrid.ItemsSource = rows;
        Status.Text = $"다른 파일 {rows.Count}개 — 「비교 버전 것」을 체크하고 합치면, 기준 버전에 그 파일만 바꿔 넣은 새 폴더를 만듭니다.";
    }

    /// <summary>고른 파일의 상세 — 모션은 컷마다, FD2.EXE 는 수치 표 칸마다(<see cref="VersionDetail"/>).</summary>
    private void OnDiffSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DiffGrid.SelectedItem is not DiffRow row || BaseVersion.SelectedItem is not VersionItem a || OtherVersion.SelectedItem is not VersionItem b) return;
        DiffDetail.Text = $"{row.File} — {a.Label} → {b.Label}\n\n" + string.Join("\n", VersionDetail.Describe(a.Dir, b.Dir, row.File));
        DiffDetail.ScrollToHome();
    }

    private void OnMerge(object sender, RoutedEventArgs e)
    {
        if (BaseVersion.SelectedItem is not VersionItem a || OtherVersion.SelectedItem is not VersionItem b || DiffGrid.ItemsSource is not List<DiffRow> rows) return;
        var picks = rows.Where(r => r.UseOther).ToDictionary(r => r.File, _ => b.Dir);
        string target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(a.Dir)!, MergeName.Text.Trim());
        try
        {
            GameVersions.Merge(a.Dir, picks, target);
            _extraVersions.Add(target);
            FillVersionBoxes();
            Status.Text = $"합쳤습니다 — {target} (비교 버전 것 {picks.Count}개). 게임의 게임 > 버전 고르기에서 고르실 수 있습니다.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"못 합쳤습니다: {ex.Message}"; }
    }

    // ── 맵 캐릭터 ───────────────────────────────────────────────────────────

    private int IconScale => IconZoom.SelectedIndex switch { 0 => 2, 1 => 3, 3 => 6, _ => 4 };

    private void OnIconSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_icons == null || IconList.SelectedItem is not CharItem item) return;
        AllFrames.ItemsSource = Enumerable.Range(0, IconBank.FramesPerChar)
            .Select(f => new FrameItem(f, $"{f} {FacingNames[f / 3]} {f % 3 + 1}", ToBitmap(_icons.Frame(item.Index, f), Rle.Tile, Rle.Tile), 0))
            .ToList();
        ShowFacings();
    }

    private void OnIconZoomChanged(object sender, SelectionChangedEventArgs e) => ShowFacings();

    private void ShowFacings()
    {
        if (_icons == null || IconList?.SelectedItem is not CharItem item) return;
        int step = WalkAnim.IsChecked == true ? WalkCycle[_walkStep % WalkCycle.Length] : 0;
        double size = Rle.Tile * IconScale;
        Facings.ItemsSource = FacingOrder
            .Select(d => new FrameItem(d, FacingNames[d], ToBitmap(_icons.Frame(item.Index, d * 3 + step), Rle.Tile, Rle.Tile), size))
            .ToList();
    }

    // ── 전투 모션 ───────────────────────────────────────────────────────────

    private int MotionKind => KindStand.IsChecked == true ? 0 : 1;

    private void OnMotionCharSelected(object sender, SelectionChangedEventArgs e)
    {
        ShowMotionPick();
        LoadMotion();
    }

    // ── 모션 .obs — 버전마다 뽑아 두고, 캐릭터마다 어느 버전 것으로 돌지(MotionFiles) ──────────

    private bool _pickSync;

    private void FillMotionVersions()
    {
        _pickSync = true;
        MotionVersion.ItemsSource = new[] { $"(지금 버전 — {MotionFiles.VersionOf(_dir)})" }.Concat(MotionFiles.Versions()).ToList();
        _pickSync = false;
        ShowMotionPick();
    }

    private void ShowMotionPick()
    {
        if (_dir == "" || MotionList.SelectedItem is not CharItem item || MotionVersion.ItemsSource is not List<string> names) return;
        MotionFiles.Reload();
        string pick = MotionFiles.PickFor(_dir, item.Index);
        _pickSync = true;
        MotionVersion.SelectedIndex = pick == MotionFiles.VersionOf(_dir) ? 0 : Math.Max(0, names.IndexOf(pick));
        _pickSync = false;
    }

    private void OnMotionVersionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickSync || _dir == "" || MotionList.SelectedItem is not CharItem item || MotionVersion.SelectedItem is not string name) return;
        try
        {
            MotionFiles.SetPick(item.Index, MotionVersion.SelectedIndex == 0 ? null : name);
            Status.Text = $"{CharacterNames.Of(item.Index)} 모션 — {(MotionVersion.SelectedIndex == 0 ? "지금 버전" : name)} 것으로 돕니다(게임은 다음 장부터).";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"고르기를 저장하지 못했습니다: {ex.Message}"; }
        LoadMotion();
    }

    private void OnExportMotions(object sender, RoutedEventArgs e)
    {
        int files = 0, versions = 0;
        foreach (var v in FindVersions())
        {
            try { files += MotionFiles.Export(v.Dir); versions++; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                Status.Text = $"{v.Label} 을(를) 뽑지 못했습니다: {ex.Message}";
            }
        }
        FillMotionVersions();
        LoadMotion();
        Status.Text = $"버전 {versions}개에서 모션 .obs {files}개를 뽑았습니다 — {MotionFiles.Root}";
    }

    private void OnOpenMotionFolder(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(MotionFiles.Root);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", MotionFiles.Root) { UseShellExecute = true });
    }

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) LoadMotion();
    }

    private void LoadMotion()
    {
        StopPlay();
        _motion = null;
        if (_dir == "" || MotionList.SelectedItem is not CharItem item) return;
        try { _motion = Motion.Load(_dir, item.Index, MotionKind, _pal); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            Status.Text = $"{item.Index}번 모션을 풀지 못했습니다: {ex.Message}";
        }
        _frame = 0;
        if (_motion == null)
        {
            MotionImage.Source = null;
            MotionStrip.ItemsSource = null;
            FrameSlider.Maximum = 0;
            FrameInfo.Text = "이 캐릭터에는 이 모션이 없습니다.";
            return;
        }
        FrameSlider.Maximum = _motion.Frames.Count - 1;
        MotionStrip.ItemsSource = _motion.Frames
            .Select((f, i) => new FrameItem(i, $"{i + 1}  ({f.Hold}틱{(f.Hit != 0 ? " · 맞음" : "")})", Compose(i, onion: false, box: false), 0))
            .ToList();
        ShowFrame();
    }

    private void ShowFrame()
    {
        if (_motion == null) return;
        var f = _motion.Frames[_frame];
        MotionImage.Source = Compose(_frame, Onion.IsChecked == true, ShowBox.IsChecked == true);
        if ((int)FrameSlider.Value != _frame) FrameSlider.Value = _frame;
        FrameInfo.Text = $"컷 {_frame + 1}/{_motion.Frames.Count}   자리 ({f.X},{f.Y})   크기 {f.Width}×{f.Height}   " +
                         $"붙드는 틱 {f.Hold}   맞음 {f.Hit}   효과음 {f.Sound}   (모션 머리 [4] 소리 묶음 = {_motion.SoundBank})";
    }

    /// <summary>320x200 전투 화면에 컷을 제자리에 놓는다. onion 이면 앞 컷을 흐리게 밑에 깐다.</summary>
    private BitmapSource Compose(int index, bool onion, bool box)
    {
        const int W = Motion.ScreenWidth, H = Motion.ScreenHeight;
        var px = new uint[W * H];
        if (onion && index > 0) Put(px, _motion!.Frames[index - 1], 0x60000000u);
        Put(px, _motion!.Frames[index], 0);
        if (box)
        {
            var f = _motion.Frames[index];
            for (int x = f.X; x < f.X + f.Width; x++) { Dot(px, x, f.Y); Dot(px, x, f.Y + f.Height - 1); }
            for (int y = f.Y; y < f.Y + f.Height; y++) { Dot(px, f.X, y); Dot(px, f.X + f.Width - 1, y); }
        }
        return ToBitmap(px, W, H);

        static void Dot(uint[] px, int x, int y)
        {
            if ((uint)x < W && (uint)y < H) px[y * W + x] = 0xFFFFE040;
        }
    }

    /// <summary>컷 그림을 제자리에 찍는다. ghost 가 0 이 아니면 알파를 그 값으로 낮춘다.</summary>
    private static void Put(uint[] px, MotionFrame f, uint ghost)
    {
        for (int y = 0; y < f.Height; y++)
        {
            int sy = f.Y + y;
            if ((uint)sy >= Motion.ScreenHeight) continue;
            for (int x = 0; x < f.Width; x++)
            {
                int sx = f.X + x;
                if ((uint)sx >= Motion.ScreenWidth) continue;
                uint c = f.Pixels[y * f.Width + x];
                if (c == 0) continue;
                px[sy * Motion.ScreenWidth + sx] = ghost != 0 ? (c & 0x00FFFFFF) | ghost : c;
            }
        }
    }

    private void OnFrameSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_motion == null || (int)e.NewValue == _frame) return;
        _frame = Math.Clamp((int)e.NewValue, 0, _motion.Frames.Count - 1);
        ShowFrame();
    }

    private void Step(int delta)
    {
        if (_motion == null) return;
        _frame = (_frame + delta + _motion.Frames.Count) % _motion.Frames.Count;
        _held = 0;
        ShowFrame();
    }

    private void OnPrevFrame(object sender, RoutedEventArgs e) { StopPlay(); Step(-1); }
    private void OnNextFrame(object sender, RoutedEventArgs e) { StopPlay(); Step(1); }
    private void OnOnionChanged(object sender, RoutedEventArgs e) => ShowFrame();

    private void OnStripClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int i } && _motion != null)
        {
            StopPlay();
            _frame = i;
            ShowFrame();
        }
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_playTimer.IsEnabled) { StopPlay(); return; }
        if (_motion == null) return;
        _playTimer.Interval = TimeSpan.FromMilliseconds(int.TryParse(TickMs.Text, out int ms) ? Math.Clamp(ms, 10, 2000) : 55);
        _held = 0;
        _playTimer.Start();
        PlayButton.Content = "■ 정지";
    }

    private void StopPlay()
    {
        _playTimer.Stop();
        PlayButton.Content = "▶ 재생";
    }

    /// <summary>원본처럼 컷마다 「붙드는 틱」만큼 머문 뒤 다음 컷으로.</summary>
    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (_motion == null) { StopPlay(); return; }
        if (++_held < _motion.Frames[_frame].Hold) return;
        _held = 0;
        _frame = (_frame + 1) % _motion.Frames.Count;
        ShowFrame();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Tabs.SelectedIndex != 1 || e.OriginalSource is TextBox) return;
        switch (e.Key)
        {
            case Key.Left: StopPlay(); Step(-1); e.Handled = true; break;
            case Key.Right: StopPlay(); Step(1); e.Handled = true; break;
            case Key.Space: OnPlay(this, e); e.Handled = true; break;
        }
    }

    // ── 공용 ───────────────────────────────────────────────────────────────

    private static BitmapSource ToBitmap(uint[] px, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }
}

public sealed record CharItem(int Index, string Label, ImageSource? Thumb);

public sealed record VersionItem(string Dir, string Label);

public sealed class DiffRow(string file, string status, string entries)
{
    public string File { get; } = file;
    public string Status { get; } = status;
    public string Entries { get; } = entries;
    public bool UseOther { get; set; }
}

public sealed record FrameItem(int Index, string Label, ImageSource Image, double Size);
