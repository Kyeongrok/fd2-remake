using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fd2.Data;

namespace Fd2.Editor;

/// <summary>
/// 적 배치 탭의 지도 — 고른 장의 지도 위에 유닛 표의 사람을 그 자리에 그린다(창세기전3 편집기의 btl 보기처럼).
/// 칸을 누르면 고른 줄의 유닛을 그 칸으로 옮기고(x · y 칸을 고친다), 사람을 누르면 그 줄을 고른다.
/// </summary>
/// <remarks>테두리 색: 적(편 0) 빨강 · 컴퓨터 아군(편 1) 초록 · 합류하는 우리 편(편 2) 하늘, 우리 편 출전 자리는 파란 칸.
/// 나중에 나오는 사람(등장 차례 &gt; 0)은 흐리게, 고른 사람은 노란 테두리.</remarks>
public partial class MainWindow
{
    private const int UnitX = 3, UnitY = 4, UnitPhase = 5;   // StageUnits 칸 차례: 편 · 그림 · 레벨 · x · y · 등장 차례
    private readonly Image _unitMap = new() { Stretch = Stretch.None, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _unitMapInfo = new() { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
    private (int Scene, uint[] Px, int W, int H)? _unitMapBase;

    /// <summary>표와 지도를 좌우로 놓은 적 배치 탭.</summary>
    private UIElement UnitsWithMap(TableEditor units, ComboBox scene)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(units, 0);
        grid.Children.Add(units);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        var right = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(_unitMapInfo, Dock.Top);
        right.Children.Add(_unitMapInfo);
        right.Children.Add(new ScrollViewer { Content = _unitMap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        units.Changed += () => DrawUnitMap(units, scene.SelectedIndex);
        units.RowSelected += () => DrawUnitMap(units, scene.SelectedIndex);
        _unitMap.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(_unitMap);
            OnUnitMapClick(units, scene.SelectedIndex, (int)(p.X / Rle.Tile), (int)(p.Y / Rle.Tile));
        };
        return grid;
    }

    private StatTables.Table? UnitTable(int scene) => _field == null || scene < 0 ? null : StatTables.StageUnits(_field, scene);

    private void OnUnitMapClick(TableEditor units, int scene, int cx, int cy)
    {
        if (_field == null || UnitTable(scene) is not { } t) return;
        // 사람을 누르면 그 줄을 고른다 — 한 칸에 여럿이면 처음부터 있는 사람을 먼저
        int hit = Enumerable.Range(0, t.Rows.Count)
            .Where(r => StatTables.Get(_field, t.Rows[r].Fields[UnitX]) == cx && StatTables.Get(_field, t.Rows[r].Fields[UnitY]) == cy)
            .OrderBy(r => StatTables.Get(_field, t.Rows[r].Fields[UnitPhase])).DefaultIfEmpty(-1).First();
        int row = units.SelectedRow;
        if (hit >= 0 && hit != row) { units.SelectedRow = hit; return; }
        if (row < 0 || row >= t.Rows.Count) { Status.Text = "옮길 유닛을 표나 지도에서 먼저 고르세요."; return; }
        if (_unitMapBase is not { } b || cx < 0 || cy < 0 || cx >= b.W / Rle.Tile || cy >= b.H / Rle.Tile) return;
        StatTables.Set(_field, t.Rows[row].Fields[UnitX], cx);
        StatTables.Set(_field, t.Rows[row].Fields[UnitY], cy);
        units.Refresh();
        units.SelectedRow = row;
        Status.Text = $"{t.Rows[row].Name} → ({cx}, {cy}) — 저장을 눌러야 FDFIELD.DAT 에 들어갑니다.";
    }

    private void DrawUnitMap(TableEditor units, int scene)
    {
        if (_field == null || _icons == null || scene < 0 || UnitTable(scene) is not { } t) { _unitMap.Source = null; return; }
        if (_unitMapBase?.Scene != scene)
        {
            try
            {
                var s = new Scene(_dir, scene, _pal, _icons);
                _unitMapBase = (scene, s.RenderMap(), s.Width * Rle.Tile, s.Height * Rle.Tile);
            }
            catch (Exception e) when (e is System.IO.IOException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                _unitMapBase = null;
                _unitMap.Source = null;
                _unitMapInfo.Text = $"{scene + 1}장 지도를 읽지 못했습니다: {e.Message}";
                return;
            }
        }
        var (_, basePx, w, h) = _unitMapBase.Value;
        var px = (uint[])basePx.Clone();

        // 우리 편 출전 자리 — 좌표 파일에서 그 밖의 유닛 다음
        int info = Dat.U32(_field, 6 + 4 * (3 * scene + 1)), pos = Dat.U32(_field, 6 + 4 * (3 * scene + 2));
        int players = _field[info + 1], others = _field[info + 2];
        for (int k = 0; k < players; k++)
        {
            int p = pos + 2 + 6 * (others + k);
            if (p + 4 > _field.Length) break;
            int x = Dat.U16(_field, p), y = Dat.U16(_field, p + 2);
            Tint(px, w, h, x, y, 0xFF3060FF);
            Box(px, w, h, x, y, 0xFF3060FF, 1);
        }

        for (int r = 0; r < t.Rows.Count; r++)
        {
            var f = t.Rows[r].Fields;
            int side = StatTables.Get(_field, f[0]), icon = StatTables.Get(_field, f[1]);
            int x = StatTables.Get(_field, f[UnitX]), y = StatTables.Get(_field, f[UnitY]), phase = StatTables.Get(_field, f[UnitPhase]);
            if (icon < _icons.Characters) Sprite(px, w, h, x, y, _icons.Frame(icon, 0), phase > 0);
            Box(px, w, h, x, y, side switch { 0 => 0xFFE03030, 1 => 0xFF30C040, _ => 0xFF40C0FF }, 1);
        }
        int selected = units.SelectedRow;
        if (selected >= 0 && selected < t.Rows.Count)
            Box(px, w, h, StatTables.Get(_field, t.Rows[selected].Fields[UnitX]), StatTables.Get(_field, t.Rows[selected].Fields[UnitY]), 0xFFFFE040, 3);
        _unitMap.Source = ToBitmap(px, w, h);
        _unitMapInfo.Text = $"{scene + 1}장 {w / Rle.Tile}x{h / Rle.Tile} — 빨강 적 · 초록 컴퓨터 아군 · 하늘 합류 · 파란 칸 우리 편 출전 자리, 흐린 것은 나중에 나옴.\n"
            + "사람을 누르면 그 줄을 고르고, 다른 칸을 누르면 고른 유닛을 그 칸으로 옮깁니다.";
    }

    private static void Sprite(uint[] px, int w, int h, int cx, int cy, uint[] tile, bool faint)
    {
        for (int y = 0; y < Rle.Tile; y++)
            for (int x = 0; x < Rle.Tile; x++)
            {
                int sx = cx * Rle.Tile + x, sy = cy * Rle.Tile + y;
                uint c = tile[y * Rle.Tile + x];
                if (c == 0 || sx >= w || sy >= h) continue;
                px[sy * w + sx] = faint ? Blend(px[sy * w + sx], c | 0xFF000000) : c | 0xFF000000;
            }
    }

    private static void Tint(uint[] px, int w, int h, int cx, int cy, uint color)
    {
        for (int y = 0; y < Rle.Tile; y++)
            for (int x = 0; x < Rle.Tile; x++)
            {
                int sx = cx * Rle.Tile + x, sy = cy * Rle.Tile + y;
                if (sx < w && sy < h) px[sy * w + sx] = Blend(px[sy * w + sx], color);
            }
    }

    private static void Box(uint[] px, int w, int h, int cx, int cy, uint color, int thick)
    {
        for (int y = 0; y < Rle.Tile; y++)
            for (int x = 0; x < Rle.Tile; x++)
            {
                if (x >= thick && y >= thick && x < Rle.Tile - thick && y < Rle.Tile - thick) continue;
                int sx = cx * Rle.Tile + x, sy = cy * Rle.Tile + y;
                if (sx < w && sy < h) px[sy * w + sx] = color;
            }
    }

    /// <summary>반반 섞기(BGRA).</summary>
    private static uint Blend(uint a, uint b) => 0xFF000000 | (((a & 0xFEFEFE) >> 1) + ((b & 0xFEFEFE) >> 1));
}
