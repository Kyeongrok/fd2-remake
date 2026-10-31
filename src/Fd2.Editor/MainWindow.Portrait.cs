using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fd2.Data;
using Microsoft.Win32;

namespace Fd2.Editor;

/// <summary>
/// 「초상화」 탭(사용자 요청) — 대사 창 얼굴(DATO.DAT)을 그림 파일로 바꾼다. 장마다(말할 때 돌리는 몇 장) 그림 바꾸기 · PNG 로 내보내기,
/// 모든 장을 한 그림으로, 원본으로 되돌리기. 바꾼 것은 실행 파일 옆 「초상화\NNNN.dat」(<see cref="PortraitFiles"/>)에 적고 게임은 다시 켜면 그 얼굴을 쓴다.
/// 넣는 그림은 원래 장 크기로 맞추고 색은 게임 팔레트에서 가장 가까운 색으로 바꾼다(투명한 곳은 얼굴 바탕 파랑).
/// </summary>
public partial class MainWindow
{
    private const int PortraitZoom = 3;
    private ListBox? _portraitList;
    private WrapPanel? _portraitFrames;
    private TextBlock? _portraitInfo;

    private void FillPortraits()
    {
        string dato = System.IO.Path.Combine(_dir, "DATO.DAT");
        int count = System.IO.File.Exists(dato) ? Dat.Count(dato) : 0;
        if (_portraitList == null) BuildPortraitTab();
        var items = new List<CharItem>();
        for (int i = 0; i < count; i++)
        {
            var f = SafePortrait(i);
            if (f.Count == 0) continue;
            items.Add(new CharItem(i, PortraitLabel(i), ToBitmap(f[0].Px, f[0].W, f[0].H)));
        }
        _portraitList!.ItemsSource = items;
        if (items.Count > 0) _portraitList.SelectedIndex = 0;
    }

    private static string PortraitLabel(int i) => $"{i} {CharacterNames.Of(i).Split('-')[0]}{(PortraitFiles.Has(i) ? "  (바꿈)" : "")}";

    private List<(uint[] Px, int W, int H)> SafePortrait(int icon)
    {
        try { return Portrait.Load(_dir, icon, _pal); }
        catch (Exception e) when (e is System.IO.IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { return []; }
    }

    private void BuildPortraitTab()
    {
        _portraitList = new ListBox { Width = 210 };
        _portraitList.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal'>" +
            "<Image Source='{Binding Thumb}' Width='48' Height='48' /><TextBlock Text='{Binding Label}' VerticalAlignment='Center' Margin='8,0,0,0' />" +
            "</StackPanel></DataTemplate>");
        _portraitList.SelectionChanged += (_, _) => ShowPortrait();
        DockPanel.SetDock(_portraitList, Dock.Left);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 4) };
        bar.Children.Add(PortraitButton("모든 장을 이 그림으로...", () => ReplacePortrait(-1)));
        bar.Children.Add(PortraitButton("원본으로 되돌리기", ResetPortrait));
        bar.Children.Add(PortraitButton("초상화 폴더 열기", () =>
        {
            System.IO.Directory.CreateDirectory(PortraitFiles.Root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", PortraitFiles.Root) { UseShellExecute = true });
        }));
        DockPanel.SetDock(bar, Dock.Top);
        _portraitInfo = new TextBlock { Margin = new Thickness(12, 0, 12, 8), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(_portraitInfo, Dock.Top);
        _portraitFrames = new WrapPanel { Margin = new Thickness(12, 0, 12, 12) };
        var scroll = new ScrollViewer { Content = _portraitFrames, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var dock = new DockPanel();
        dock.Children.Add(_portraitList);
        dock.Children.Add(bar);
        dock.Children.Add(_portraitInfo);
        dock.Children.Add(scroll);
        PortraitHost.Content = dock;
    }

    private static Button PortraitButton(string text, Action act)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
        b.Click += (_, _) => act();
        return b;
    }

    private int SelectedPortrait => _portraitList?.SelectedItem is CharItem c ? c.Index : -1;

    private void ShowPortrait()
    {
        if (_portraitFrames == null || _portraitInfo == null) return;
        _portraitFrames.Children.Clear();
        int icon = SelectedPortrait;
        if (icon < 0) return;
        var frames = SafePortrait(icon);
        _portraitInfo.Text = $"얼굴 {icon} — {frames.Count}장(말하는 동안 차례로 돈다), {(frames.Count > 0 ? $"{frames[0].W}×{frames[0].H}" : "")}. " +
                             (PortraitFiles.Has(icon) ? $"바꾼 그림: {PortraitFiles.PathOf(icon)}" : "원본(DATO.DAT)") +
                             " · 넣는 그림은 그 장 크기로 맞추고 게임 팔레트 색으로 바꿉니다. 게임은 다시 켜면 바뀐 얼굴을 씁니다.";
        for (int k = 0; k < frames.Count; k++)
        {
            int at = k;
            var (px, w, h) = frames[k];
            var card = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
            card.Children.Add(new Border
            {
                Background = (Brush)FindResource("Checker"),
                Child = new Image { Source = ToBitmap(px, w, h), Width = w * PortraitZoom, Height = h * PortraitZoom, Stretch = Stretch.Fill },
            }.Also(b => RenderOptions.SetBitmapScalingMode(b.Child, BitmapScalingMode.NearestNeighbor)));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(new TextBlock { Text = $"{k + 1}장", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(PortraitButton("그림 바꾸기...", () => ReplacePortrait(at)));
            row.Children.Add(PortraitButton("PNG 로 내보내기...", () => ExportPortraitFrame(icon, at)));
            card.Children.Add(row);
            _portraitFrames.Children.Add(card);
        }
    }

    /// <summary>장 at(−1 이면 모든 장)을 고른 그림 파일로 바꾼다.</summary>
    private void ReplacePortrait(int at)
    {
        int icon = SelectedPortrait;
        if (_dir == "" || icon < 0) return;
        var open = new OpenFileDialog { Title = "초상화로 넣을 그림", Filter = "그림 (*.png;*.bmp;*.jpg;*.gif)|*.png;*.bmp;*.jpg;*.jpeg;*.gif" };
        if (open.ShowDialog(this) != true) return;
        var frames = SafePortrait(icon);
        if (frames.Count == 0) return;
        try
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri(open.FileName);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            // 지금 장들은 팔레트 번호로 되돌려 두고(원본 그대로 다시 적게), 바꿀 장만 새 그림을 그 크기로
            var indexed = frames.Select(f => (PortraitFiles.Unpalette(f.Px, _pal), f.W, f.H)).ToList();
            for (int k = 0; k < frames.Count; k++)
            {
                if (at >= 0 && k != at) continue;
                var (_, w, h) = frames[k];
                indexed[k] = (PortraitFiles.Quantize(Fit(src, w, h), _pal), w, h);
            }
            PortraitFiles.Write(icon, indexed);
            RefreshPortraitItem(icon);
            Status.Text = $"얼굴 {icon} 의 {(at < 0 ? "모든 장" : $"{at + 1}장")}을 바꿨습니다 — {PortraitFiles.PathOf(icon)} (게임은 다시 켜면)";
        }
        catch (Exception e) when (e is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            Status.Text = $"그림을 넣지 못했습니다: {e.Message}";
        }
    }

    /// <summary>그림을 w×h 로(비율은 무시하고 꽉 차게) 맞춰 BGRA 점으로.</summary>
    private static uint[] Fit(BitmapSource src, int w, int h)
    {
        BitmapSource b = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        if (b.PixelWidth != w || b.PixelHeight != h)
            b = new TransformedBitmap(b, new ScaleTransform(w / (double)b.PixelWidth, h / (double)b.PixelHeight));
        var px = new uint[w * h];
        var crop = b.PixelWidth == w && b.PixelHeight == h ? b : new CroppedBitmap(b, new Int32Rect(0, 0, Math.Min(w, b.PixelWidth), Math.Min(h, b.PixelHeight)));
        crop.CopyPixels(px, w * 4, 0);
        return px;
    }

    private void ResetPortrait()
    {
        int icon = SelectedPortrait;
        if (icon < 0 || !PortraitFiles.Has(icon)) { Status.Text = "이 얼굴은 원본 그대로입니다."; return; }
        PortraitFiles.Reset(icon);
        RefreshPortraitItem(icon);
        Status.Text = $"얼굴 {icon} 을 원본으로 되돌렸습니다.";
    }

    private void ExportPortraitFrame(int icon, int at)
    {
        var frames = SafePortrait(icon);
        if (at >= frames.Count) return;
        var save = new SaveFileDialog { Title = "초상화 한 장 내보내기", Filter = "PNG (*.png)|*.png", FileName = $"얼굴{icon:D3}_{at + 1}.png" };
        if (save.ShowDialog(this) != true) return;
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToBitmap(frames[at].Px, frames[at].W, frames[at].H)));
        using var fs = System.IO.File.Create(save.FileName);
        enc.Save(fs);
        Status.Text = $"내보냈습니다 — {save.FileName}";
    }

    /// <summary>목록의 그 줄(썸네일 · 「바꿈」 표시)과 오른쪽을 다시.</summary>
    private void RefreshPortraitItem(int icon)
    {
        if (_portraitList?.ItemsSource is not List<CharItem> items) return;
        int i = items.FindIndex(c => c.Index == icon);
        if (i < 0) return;
        var f = SafePortrait(icon);
        items[i] = new CharItem(icon, PortraitLabel(icon), f.Count > 0 ? ToBitmap(f[0].Px, f[0].W, f[0].H) : null);
        _portraitList.ItemsSource = null;
        _portraitList.ItemsSource = items;
        _portraitList.SelectedIndex = i;
        ShowPortrait();
    }
}

internal static class EditorExtensions
{
    public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
