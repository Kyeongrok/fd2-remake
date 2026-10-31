using System.Drawing;
using System.Windows.Forms;

namespace Fd2;

/// <summary>
/// PC 햄버거 「설정」 · 「모드」 창(사용자 요청 — 창세기전3 · 대항해시대3 처럼 메뉴를 내리는 대신 창 하나에서 고른다).
/// 고르면 바로 게임에 들어간다(예전 메뉴 명령 <see cref="OnMenuCommand"/> 를 그대로 부른다). 게임 창을 주인으로 뜬다.
/// </summary>
internal sealed partial class GameWindow
{
    private static readonly Font DialogFont = new("맑은 고딕", 10f);

    /// <summary>창 꼴 — 위에 탭, 아래 「닫기」.</summary>
    private void ShowSettingsForm(string title, params (string Name, Action<FlowLayoutPanel> Fill)[] tabs)
    {
        if (Offscreen) return;
        using var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(460, 420), Font = DialogFont, AutoScaleMode = AutoScaleMode.Dpi,
        };
        var tab = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        foreach (var (name, fill) in tabs)
        {
            var page = new TabPage(name) { Padding = new Padding(12) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            fill(flow);
            page.Controls.Add(flow);
            tab.TabPages.Add(page);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6) };
        var close = new Button { Text = "닫기", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
        buttons.Controls.Add(close);
        form.Controls.Add(tab);
        form.Controls.Add(buttons);
        form.AcceptButton = close;
        form.CancelButton = close;
        var owner = new NativeWindow();
        owner.AssignHandle(_hwnd);
        try { form.ShowDialog(owner); }
        finally { owner.ReleaseHandle(); }
        _keys.Clear();   // 창이 떠 있는 동안 뗀 키가 눌린 채로 남지 않게
    }

    private static Label Heading(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 10, 0, 4), Font = new Font(DialogFont, FontStyle.Bold) };

    /// <summary>켜고 끄기 — 바꾸면 그 메뉴 명령을 그대로.</summary>
    private CheckBox Toggle(string text, bool on, int command)
    {
        var box = new CheckBox { Text = text, Checked = on, AutoSize = true, Margin = new Padding(4, 3, 0, 3) };
        box.CheckedChanged += (_, _) => OnMenuCommand(command);
        return box;
    }

    /// <summary>고르기 칸 — 고르면 base + 고른 번호 명령.</summary>
    private FlowLayoutPanel Choice(string label, string[] items, int selected, Action<int> pick)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(4, 7, 8, 0) });
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        combo.Items.AddRange(items);
        combo.SelectedIndex = Math.Clamp(selected, -1, items.Length - 1);
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedIndex >= 0) pick(combo.SelectedIndex); };
        row.Controls.Add(combo);
        return row;
    }

    /// <summary>0~100 소리 크기 막대.</summary>
    private FlowLayoutPanel Slider(string label, int value, Action<int> set)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(4, 12, 8, 0), Width = 70 });
        var bar = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Value = Math.Clamp(value, 0, 100), Width = 220 };
        var shown = new Label { Text = $"{bar.Value}%", AutoSize = true, Margin = new Padding(4, 12, 0, 0) };
        bar.ValueChanged += (_, _) => { shown.Text = $"{bar.Value}%"; set(bar.Value); };
        row.Controls.Add(bar);
        row.Controls.Add(shown);
        return row;
    }

    private static Button ActionButton(string text, Action act) =>
        new Button { Text = text, AutoSize = true, Margin = new Padding(4, 6, 0, 2), Padding = new Padding(8, 2, 8, 2) }
            .Also(b => b.Click += (_, _) => { b.FindForm()?.Close(); act(); });

    /// <summary>햄버거 「설정」 — 화면 · 게임 · 소리와 조작.</summary>
    private void OpenSettingsDialog() => ShowSettingsForm("설정",
        ("화면", f =>
        {
            int res = Array.IndexOf(ResChoices, (_settings.ViewW, _settings.ViewH));
            f.Controls.Add(Choice("해상도(창 크기)", ResChoices.Select(r => $"{r.W} × {r.H}").ToArray(), res, i => OnMenuCommand(MenuResBase + i)));
            f.Controls.Add(Choice("배율", ZoomChoices.Select(z => z == 0 ? "자동(창을 채움)" : $"{z * 100}%").ToArray(),
                Array.IndexOf(ZoomChoices, _settings.Zoom), i => OnMenuCommand(MenuZoomBase + ZoomChoices[i])));
            f.Controls.Add(Toggle("전체화면 (F11)", _settings.Fullscreen, MenuFullscreen));
            f.Controls.Add(Toggle("모바일 화면으로 보기", _settings.MobileView, MenuMobileView));
            f.Controls.Add(Heading("전투 화면에 보일 것"));
            f.Controls.Add(Toggle("조작법(전투 위 줄)", _settings.ShowHelp, MenuShowHelp));
            f.Controls.Add(Toggle("승패 조건", _settings.ShowGoals, MenuShowGoals));
            f.Controls.Add(ActionButton("화면 처음 값으로", () => OnMenuCommand(MenuResetView)));
        }),
        ("게임", f =>
        {
            f.Controls.Add(Toggle("NPC 아군도 내가 움직이기(AI 끔)", _settings.ControlAllies, MenuControlAllies));
        }),
        ("소리 · 조작", f =>
        {
            f.Controls.Add(Heading("소리 크기"));
            f.Controls.Add(Slider("배경음악", _settings.MusicVolume, v => { _settings.MusicVolume = v; ApplyVolume(); _settings.Save(); }));
            f.Controls.Add(Slider("효과음", _settings.SoundVolume, v => { _settings.SoundVolume = v; ApplyVolume(); _settings.Save(); }));
            f.Controls.Add(Heading("조작"));
            f.Controls.Add(ActionButton("단축키 · 패드 설정...", () => OnMenuCommand(MenuKeys)));
        }));

    /// <summary>햄버거 「모드」 — 고해상도 · 범위 보기 · 배경음악 · 효과음 켜고 끄기.</summary>
    private void OpenModeDialog() => ShowSettingsForm("모드",
        ("모드", f =>
        {
            f.Controls.Add(Toggle("고해상도(4배, 이 PC 에서 차례로 만듦)", _settings.HiRes, MenuHiRes));
            f.Controls.Add(Toggle("이동 · 공격 범위 보기", _settings.ShowRange, MenuShowRange));
            f.Controls.Add(Toggle("사람 정보 창(전투 왼쪽 아래 — 이름 · HP · 능력치)", _settings.ShowUnitInfo, MenuShowUnitInfo));
            f.Controls.Add(Heading("소리"));
            f.Controls.Add(Toggle("배경음악", _settings.Music, MenuMusic));
            f.Controls.Add(Toggle("효과음", _settings.Sound, MenuSound));
        }));
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
