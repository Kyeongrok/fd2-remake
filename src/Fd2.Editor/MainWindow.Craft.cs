using System.Windows;
using System.Windows.Controls;
using Fd2.Data;

namespace Fd2.Editor;

/// <summary>
/// 「참혹술 검기 붙이기」(사용자 요청) — 로테만 참혹술 몸짓에서 검기만 떼어(<see cref="MotionCraft"/>) 고른 캐릭터의 몸짓 뒤에 깐다.
/// 결과는 새 모션 버전 「검기-캐릭터」 로 적고 그 캐릭터가 그 버전을 쓰게 한다 — 되돌리려면 「이 캐릭터 모션 버전」 에서 원래 버전을 고른다.
/// </summary>
public partial class MainWindow
{
    private void OnCraftChamhok(object sender, RoutedEventArgs e)
    {
        if (_dir == "" || MotionList.SelectedItem is not CharItem item) { Status.Text = "검기를 붙일 캐릭터를 먼저 고르세요."; return; }
        int c = item.Index;
        // 기본은 마법 · 기술 몸짓(없으면 공격), 시작은 첫 맞는 컷
        int kind = SafeMotion(c, 2) != null ? 2 : 1;
        var target = SafeMotion(c, kind);
        if (target == null) { Status.Text = $"{CharacterNames.Of(c)} 에는 공격 · 마법 몸짓이 없습니다."; return; }
        int firstHit = Math.Max(0, Enumerable.Range(0, target.Frames.Count).FirstOrDefault(k => target.Frames[k].Hit != 0));

        var kindBox = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 0, 8) };
        kindBox.Items.Add("공격 몸짓");
        kindBox.Items.Add("마법 · 기술 몸짓");
        kindBox.SelectedIndex = kind == 2 ? 1 : 0;
        var startBox = new TextBox { Text = firstHit.ToString(), Width = 60, Margin = new Thickness(0, 0, 0, 8) };
        var dxBox = new TextBox { Text = "0", Width = 60, Margin = new Thickness(0, 0, 0, 8) };
        var dyBox = new TextBox { Text = "0", Width = 60, Margin = new Thickness(0, 0, 0, 8) };
        var mirror = new CheckBox { Content = "좌우 뒤집기", Margin = new Thickness(0, 0, 0, 8) };
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(int r, string label, UIElement field)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 3, 12, 8) };
            Grid.SetRow(l, r); Grid.SetRow(field, r); Grid.SetColumn(field, 1);
            grid.Children.Add(l); grid.Children.Add(field);
        }
        Row(0, "붙일 몸짓", kindBox);
        Row(1, "검기 시작 컷(0부터)", startBox);
        Row(2, "가로 옮김(점, + 오른쪽)", dxBox);
        Row(3, "세로 옮김(점, + 아래)", dyBox);
        Row(4, "", mirror);
        var hint = new TextBlock
        {
            Text = $"로테만 참혹술의 검기 8컷을 {CharacterNames.Of(c)} 몸짓 뒤에 한 컷씩 깝니다(몸이 앞). 몸짓이 모자라면 마지막 컷을 이어 씁니다. " +
                   "결과는 새 모션 버전 「검기-이름」 으로 저장되고 이 캐릭터가 그 버전을 씁니다.",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 4, 0, 8),
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(hint, 5); Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);
        var ok = new Button { Content = "붙이기", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "취소", IsCancel = true, Width = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(buttons, 6); Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        var dlg = new Window
        {
            Title = $"참혹술 검기 붙이기 — {CharacterNames.Of(c)}", Content = grid, Owner = this, Width = 440, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        ok.Click += (_, _) => dlg.DialogResult = true;
        if (dlg.ShowDialog() != true) return;
        if (!int.TryParse(startBox.Text, out int start) || !int.TryParse(dxBox.Text, out int dx) || !int.TryParse(dyBox.Text, out int dy))
        {
            Status.Text = "시작 컷 · 옮김은 숫자로 넣어 주세요.";
            return;
        }
        kind = kindBox.SelectedIndex == 1 ? 2 : 1;
        try
        {
            var raw = MotionFiles.Read(_dir, c, kind) ?? Dat.Load(System.IO.Path.Combine(_dir, "FIGANI.DAT"), 3 * c + kind);
            var beam = MotionCraft.ChamhokBeam(_dir, _pal);
            var made = MotionCraft.Composite(raw, _pal, beam, Math.Max(0, start), dx, dy, mirror.IsChecked == true);
            string version = "검기-" + CharacterNames.Of(c).Split('-')[0];
            MotionCraft.SaveAsVersion(_dir, c, kind, made, version);
            FillMotionVersions();
            (kind == 2 ? KindMagic : KindAttack).IsChecked = true;
            LoadMotion();
            Status.Text = $"{CharacterNames.Of(c)} {(kind == 2 ? "마법 · 기술" : "공격")} 몸짓 {start}컷부터 참혹술 검기를 붙였습니다 — 모션 버전 「{version}」(게임은 다음 장부터). 되돌리려면 「이 캐릭터 모션 버전」 에서 원래 버전을 고르세요.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            Status.Text = $"검기를 붙이지 못했습니다: {ex.Message}";
        }
    }
}
