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
        Status.Text = $"캐릭터 그림 {_icons.Characters}명 · 전투 모션 {motions}명  ·  ←/→ 한 컷씩, 스페이스 재생/정지";
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "용의기사2 게임 폴더(FDFIELD.DAT 가 있는 곳)", InitialDirectory = _dir };
        if (dlg.ShowDialog(this) == true) Open(dlg.FolderName);
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

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

    private void OnMotionCharSelected(object sender, SelectionChangedEventArgs e) => LoadMotion();

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
            .Select((f, i) => new FrameItem(i, $"{i + 1}  ({f.Hold}틱{(f.Hit != 0 ? " · 타격" : "")})", Compose(i, onion: false, box: false), 0))
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
                         $"붙드는 틱 {f.Hold}   타격 {f.Hit}   효과음 {f.Sound}   (모션 머리 [4] = {_motion.HitFrame})";
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

public sealed record FrameItem(int Index, string Label, ImageSource Image, double Size);
