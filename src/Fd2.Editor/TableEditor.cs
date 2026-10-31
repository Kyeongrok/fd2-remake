using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Fd2.Data;

namespace Fd2.Editor;

/// <summary>
/// 표 하나를 고치는 탭 — 위에 (고르개) · 고른 줄 비우기 · 저장 · 되돌리기, 아래 칸 표. 칸은 <see cref="StatTables.Field"/> 로 버퍼(FD2.EXE 나 FDFIELD.DAT 통째)를 바로 고친다.
/// 아군 · 적처럼 portrait 를 주면 맨 왼쪽에 초상화 칸.
/// </summary>
internal sealed class TableEditor : DockPanel
{
    private readonly MainWindow _owner;
    private readonly Func<StatTables.Table?> _table;
    private readonly Func<byte[]?> _buffer;
    private readonly Action _save, _reload;
    private readonly Func<int, ImageSource?>? _portrait;
    private readonly DataGrid _grid;
    private StatTables.Table? _shown;

    /// <summary>왼쪽 그림 칸 높이 — 초상화 48, 마을 그림처럼 넓은 것은 더 크게.</summary>
    public double PortraitHeight { get; init; } = 48;

    public TableEditor(MainWindow owner, Func<StatTables.Table?> table, Func<byte[]?> buffer, string saveLabel, Action save, Action reload,
                       Func<int, ImageSource?>? portrait = null, params UIElement[] picker)
    {
        (_owner, _table, _buffer, _save, _reload, _portrait) = (owner, table, buffer, save, reload, portrait);
        Margin = new Thickness(8);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var p in picker) bar.Children.Add(p);
        bar.Children.Add(Button("고른 줄 비우기(삭제)", ClearRow, 12));
        bar.Children.Add(Button(saveLabel, Save, 24));
        bar.Children.Add(Button("되돌리기 (파일에서 다시 읽기)", () => { _reload(); _owner.Status.Text = "파일에서 다시 읽었습니다(저장 안 한 고침은 버렸습니다)."; }, 8));
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        _grid = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, FrozenColumnCount = portrait != null ? 2 : 1, SelectionMode = DataGridSelectionMode.Single,
        };
        _grid.CellEditEnding += OnCellEdit;
        Children.Add(_grid);
    }

    private static Button Button(string text, Action click, double left)
    {
        var b = new Button { Content = text, Margin = new Thickness(left, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>표를 다시 채운다 — 버퍼를 새로 읽었거나 고르개가 바뀌었을 때.</summary>
    public void Refresh()
    {
        _grid.Columns.Clear();
        _shown = _table();
        if (_buffer() is not { } buf || _shown is not { Rows.Count: > 0 } t) { _grid.ItemsSource = null; return; }
        var dt = new System.Data.DataTable();
        if (_portrait != null)
        {
            dt.Columns.Add("얼굴", typeof(object));
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("[얼굴]"));
            img.SetValue(HeightProperty, PortraitHeight);
            img.SetValue(Image.StretchProperty, Stretch.Uniform);
            _grid.Columns.Add(new DataGridTemplateColumn { Header = "", CellTemplate = new DataTemplate { VisualTree = img }, IsReadOnly = true });
        }
        dt.Columns.Add("이름", typeof(string));
        _grid.Columns.Add(new DataGridTextColumn { Header = "이름", Binding = new Binding("[이름]"), IsReadOnly = true });
        foreach (var f in t.Rows[0].Fields)
        {
            if (f.Pick != StatTables.Choice.None)
            {
                // 종족 · 직업 · 아이템 칸은 「이름(번호)」 고르개 — 목록에 없는 값(표에 든 엉뚱한 값)도 그대로 보이게 더한다
                var labels = StatTables.Choices(f).Select(c => c.Label).ToList();
                foreach (var (_, fields) in t.Rows)
                    if (fields.FirstOrDefault(x => x.Name == f.Name) is { } rf && StatTables.Label(f, StatTables.Get(buf, rf)) is var l && !labels.Contains(l)) labels.Add(l);
                dt.Columns.Add(f.Name, typeof(string));
                _grid.Columns.Add(new DataGridComboBoxColumn { Header = f.Name, ItemsSource = labels, SelectedItemBinding = new Binding($"[{f.Name}]") });
                continue;
            }
            dt.Columns.Add(f.Name, typeof(int));
            _grid.Columns.Add(new DataGridTextColumn { Header = f.Name, Binding = new Binding($"[{f.Name}]") });
        }
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var (name, fields) = t.Rows[r];
            var row = dt.NewRow();
            if (_portrait != null) row["얼굴"] = _portrait(r);
            row["이름"] = name;
            for (int i = 0; i < fields.Count && i < t.Rows[0].Fields.Count; i++)
            {
                var f = t.Rows[0].Fields[i];
                int v = StatTables.Get(buf, fields[i]);
                row[f.Name] = f.Pick != StatTables.Choice.None ? StatTables.Label(f, v) : v;
            }
            dt.Rows.Add(row);
        }
        _grid.ItemsSource = dt.DefaultView;
    }

    private int FieldColumn(DataGridColumn c) => c.DisplayIndex - (_portrait != null ? 2 : 1);

    private void OnCellEdit(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (_buffer() is not { } buf || _shown == null || e.EditAction != DataGridEditAction.Commit) return;
        int row = e.Row.GetIndex(), col = FieldColumn(e.Column);
        if (row < 0 || row >= _shown.Rows.Count || col < 0 || col >= _shown.Rows[row].Fields.Count) return;
        var f = _shown.Rows[row].Fields[col];
        var (min, max) = StatTables.Range(f);
        // 고르개는 「이름(번호)」 끝의 번호를, 글 칸은 적은 숫자를
        string text = e.EditingElement switch
        {
            ComboBox cb when cb.SelectedItem is string label => label[(label.LastIndexOf('(') + 1)..].TrimEnd(')'),
            TextBox box => box.Text.Trim(),
            _ => "",
        };
        if (text == "") return;
        if (!int.TryParse(text, out int v) || v < min || v > max)
        {
            e.Cancel = true;
            _owner.Status.Text = $"{f.Name} 은(는) {min} ~ {max} 사이 숫자여야 합니다.";
            return;
        }
        StatTables.Set(buf, f, v);
        // 마을 그림을 바꾸면 왼쪽 그림도 바뀌게 — 편집이 끝난 뒤 다시 채운다
        if (f.Pick == StatTables.Choice.Town) Dispatcher.BeginInvoke(() => { Refresh(); _grid.SelectedIndex = row; });
        _owner.Status.Text = $"{_shown.Rows[row].Name} {f.Name} = {(f.Pick != StatTables.Choice.None ? StatTables.Label(f, v) : v)} — 저장을 눌러야 파일에 들어갑니다.";
    }

    private void ClearRow()
    {
        if (_buffer() is not { } buf || _shown == null || _grid.SelectedIndex < 0 || _grid.SelectedIndex >= _shown.Rows.Count) { _owner.Status.Text = "비울 줄을 먼저 고르세요."; return; }
        int row = _grid.SelectedIndex;
        var (name, fields) = _shown.Rows[row];
        if (MessageBox.Show(_owner, $"{name} 줄을 비웁니다(수치 0, 아이템 칸은 빈 칸 255). 저장 전에는 「되돌리기」로 살릴 수 있습니다.", "줄 비우기",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        StatTables.Clear(buf, fields);
        Refresh();
        _grid.SelectedIndex = row;
        _owner.Status.Text = $"{name} 줄을 비웠습니다 — 저장을 눌러야 파일에 들어갑니다.";
    }

    private void Save()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        _save();
    }
}
