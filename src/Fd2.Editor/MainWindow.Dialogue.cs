using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fd2.Data;
using Microsoft.Win32;

namespace Fd2.Editor;

/// <summary>
/// 위 메뉴 「에셋 → 대사 내보내기 · 불러오기」 — 대사를 글 파일로 내보내 고치고 다시 넣는다. 글 파일 옆에 글자표.txt · 글자표.png 를 둔다
/// (원본 글자는 글꼴 번호라 표로 읽는다, <see cref="DialogueFile"/>).
/// </summary>
public partial class MainWindow
{
    private void OnExportDialogue(object sender, RoutedEventArgs e)
    {
        if (_dir == "") return;
        var dlg = new SaveFileDialog { FileName = "대사.txt", Filter = "글 파일 (*.txt)|*.txt" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            string folder = Path.GetDirectoryName(dlg.FileName)!, table = Path.Combine(folder, "글자표.txt");
            var map = DialogueFile.SeedGlyphs(_dir);
            DialogueFile.ReadGlyphs(table, map);
            File.WriteAllText(dlg.FileName, DialogueFile.Export(_dir, map), new UTF8Encoding(false));
            DialogueFile.WriteGlyphs(table, _dir, map);
            SaveGlyphSheet(Path.Combine(folder, "글자표.png"));
            Status.Text = $"대사를 내보냈습니다: {dlg.FileName} (글자표 {map.Count}자, 나머지는 {{번호}})";
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, x.Message, "대사 내보내기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImportDialogue(object sender, RoutedEventArgs e)
    {
        if (_dir == "") return;
        var dlg = new OpenFileDialog { Filter = "글 파일 (*.txt)|*.txt" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var map = DialogueFile.SeedGlyphs(_dir);
            DialogueFile.ReadGlyphs(Path.Combine(Path.GetDirectoryName(dlg.FileName)!, "글자표.txt"), map);
            var data = DialogueFile.Import(_dir, File.ReadAllText(dlg.FileName, Encoding.UTF8), map);
            if (MessageBox.Show(this, $"이 버전의 FDTXT.DAT 를 고칩니다({_dir}).\n처음 한 번은 FDTXT-backup.DAT 를 남깁니다.", "대사 불러오기",
                                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            DialogueFile.Save(_dir, data);
            Status.Text = "대사를 불러왔습니다.";
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, x.Message, "대사 불러오기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>글꼴 전체를 번호와 함께 그린 그림 — 글자표의 빈 곳을 채울 때 본다.</summary>
    private void SaveGlyphSheet(string path)
    {
        const int Cols = 24, CellW = 60, CellH = 52;
        var font = new Font16(_dir);
        int count = 0;
        while (font.Has(count)) count++;
        int rows = (count + Cols - 1) / Cols;
        var visual = new DrawingVisual();
        using (var g = visual.RenderOpen())
        {
            g.DrawRectangle(Brushes.White, null, new Rect(0, 0, Cols * CellW, rows * CellH));
            var face = new Typeface("Malgun Gothic");
            for (int c = 0; c < count; c++)
            {
                double x = c % Cols * CellW, y = c / Cols * CellH;
                g.DrawText(new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, Brushes.DimGray, 1), new Point(x + 2, y));
                for (int py = 0; py < Font16.Size; py++)
                    for (int px = 0; px < Font16.Size; px++)
                        if (font.Pixel(c, px, py)) g.DrawRectangle(Brushes.Black, null, new Rect(x + 14 + 2 * px, y + 16 + 2 * py, 2, 2));
            }
        }
        var bmp = new RenderTargetBitmap(Cols * CellW, rows * CellH, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
