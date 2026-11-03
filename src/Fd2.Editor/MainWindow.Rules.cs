using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Fd2.Data;

namespace Fd2.Editor;

/// <summary>
/// 「계산식」 탭(사용자 요청 w-99) — 이 버전 FD2.EXE 코드에서 읽은 규칙 숫자(<see cref="ExeRules"/>)를 줄마다 보이고 고친다.
/// 저장하면 게임 폴더 tables/rules.json 에 EXE 값과 다른 것만 적고, 게임은 그 값을 먼저 쓴다. FD2.EXE 는 건드리지 않는다.
/// </summary>
public partial class MainWindow
{
    private sealed class RuleRow
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public string Help { get; init; } = "";
        public int Exe { get; init; }
        public int Value { get; set; }
        public string Changed => Value != Exe ? "고침" : "";
    }

    private List<RuleRow> _ruleRows = new();
    private DataGrid? _ruleGrid;

    private void LoadRules()
    {
        if (_ruleGrid == null) BuildRulesTab();
        var t = GameTables.Load(_dir, json: false);
        if (t == null) { _ruleRows = new(); _ruleGrid!.ItemsSource = null; return; }
        var over = RuleParams.Load(_dir);
        _ruleRows = RuleParams.List(t.ExeRules).Select(p => new RuleRow
        {
            Key = p.Key, Label = p.Label, Help = p.Help, Exe = p.Value, Value = over.TryGetValue(p.Key, out int v) ? v : p.Value,
        }).ToList();
        _ruleGrid!.ItemsSource = _ruleRows;
    }

    private void BuildRulesTab()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        Button Btn(string text, Action click, double left)
        {
            var b = new Button { Content = text, Margin = new Thickness(left, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
            b.Click += (_, _) => click();
            bar.Children.Add(b);
            return b;
        }
        bar.Children.Add(Label("이 버전 FD2.EXE 코드 안 숫자 — 「지금 값」을 고치고 저장하면 tables/rules.json 에 적혀 게임이 먼저 씁니다(FD2.EXE 는 그대로)"));
        Btn("tables/rules.json 에 저장", SaveRules, 16);
        Btn("모두 EXE 값으로", () =>
        {
            foreach (var r in _ruleRows) r.Value = r.Exe;
            _ruleGrid!.Items.Refresh();
            Status.Text = "모두 EXE 값으로 돌렸습니다 — 저장을 누르면 rules.json 을 지웁니다.";
        }, 8);
        Btn("되돌리기 (파일에서 다시 읽기)", () => { LoadRules(); Status.Text = "rules.json 을 다시 읽었습니다."; }, 8);
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        _ruleGrid = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single,
        };
        _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "규칙", Binding = new Binding(nameof(RuleRow.Label)), IsReadOnly = true });
        _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "EXE 값", Binding = new Binding(nameof(RuleRow.Exe)), IsReadOnly = true });
        _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "지금 값", Binding = new Binding(nameof(RuleRow.Value)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus } });
        _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "", Binding = new Binding(nameof(RuleRow.Changed)), IsReadOnly = true });
        _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "설명", Binding = new Binding(nameof(RuleRow.Help)), IsReadOnly = true });
        _ruleGrid.Columns.Add(new DataGridTextColumn { Header = "이름", Binding = new Binding(nameof(RuleRow.Key)), IsReadOnly = true });
        _ruleGrid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction != DataGridEditAction.Commit || e.EditingElement is not TextBox box) return;
            if (!int.TryParse(box.Text.Trim(), out int v) || v < 0 || v > 255 * 255)
            {
                e.Cancel = true;
                Status.Text = "0 ~ 65025 사이 숫자를 적으세요.";
                return;
            }
            Dispatcher.BeginInvoke(() => _ruleGrid.Items.Refresh());
            Status.Text = "저장을 눌러야 rules.json 에 들어갑니다.";
        };
        panel.Children.Add(_ruleGrid);
        RulesHost.Content = panel;
    }

    private void SaveRules()
    {
        _ruleGrid?.CommitEdit(DataGridEditingUnit.Row, true);
        if (GameTables.Load(_dir, json: false) is not { } t) { Status.Text = "이 폴더에 FD2.EXE 가 없어 저장하지 못했습니다."; return; }
        try
        {
            RuleParams.Save(_dir, t.ExeRules, _ruleRows.ToDictionary(r => r.Key, r => r.Value));
            int n = _ruleRows.Count(r => r.Value != r.Exe);
            Status.Text = n == 0 ? "모두 EXE 값입니다 — rules.json 을 지웠습니다." : $"계산식 {n}개를 {RuleParams.PathOf(_dir)} 에 저장했습니다 — 게임을 다시 켜면 들어갑니다.";
            _ruleGrid?.Items.Refresh();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Status.Text = $"저장하지 못했습니다: {e.Message}";
        }
    }
}
