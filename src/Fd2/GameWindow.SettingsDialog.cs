using System.Drawing;
using System.Windows.Forms;

namespace Fd2;

/// <summary>
/// PC 햄버거 「설정」 · 「모드」 창(사용자 요청 — 창세기전3 · 대항해시대3 처럼 메뉴를 내리는 대신 창 하나에서 고른다).
/// 고르면 바로 게임에 들어간다(예전 메뉴 명령 <see cref="OnMenuCommand"/> 를 그대로 부른다). 게임 창을 주인으로 뜬다.
/// </summary>
internal sealed partial class GameWindow
{
    private GameDialog? _dlg;

    /// <summary>
    /// 창 꼴(<see cref="GameDialog"/>) — 위에 금테 탭 단추들, 그 밑에 고른 탭의 칸들, 아래 「닫기」. 탭은 가장 큰 쪽 높이에 맞춰 바꿔도 창이 들썩이지 않는다.
    /// </summary>
    private void ShowSettingsForm(string title, params (string Name, Action<FlowLayoutPanel> Fill)[] tabs)
    {
        if (Offscreen) return;
        using var dlg = _dlg = new GameDialog(title, 460);
        var pages = new List<FlowLayoutPanel>();
        var tabRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, dlg.Px(6)), BackColor = GameDialog.Back };
        var tabButtons = new List<Label>();
        void Select(int i)
        {
            for (int k = 0; k < pages.Count; k++)
            {
                pages[k].Visible = k == i;
                tabButtons[k].BackColor = k == i ? GameDialog.ButtonBack : GameDialog.Back;
                tabButtons[k].ForeColor = k == i ? GameDialog.GoldText : GameDialog.Dim;
            }
        }
        foreach (var (name, fill) in tabs)
        {
            int at = pages.Count;
            var t = new Label
            {
                Text = name, AutoSize = true, Font = GameDialog.Bold, Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(dlg.Px(14), dlg.Px(5), dlg.Px(14), dlg.Px(5)), Margin = new Padding(0, 0, dlg.Px(4), 0),
            };
            t.Click += (_, _) => Select(at);
            tabButtons.Add(t);
            if (tabs.Length > 1) tabRow.Controls.Add(t);
            var page = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = GameDialog.Panel,
                Padding = new Padding(dlg.Px(12), dlg.Px(8), dlg.Px(12), dlg.Px(12)), Margin = new Padding(0),
            };
            fill(page);
            pages.Add(page);
        }
        if (tabs.Length > 1) dlg.Content.Controls.Add(tabRow);
        var stack = new Panel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = GameDialog.Panel };
        int maxH = pages.Max(p => p.PreferredSize.Height), maxW = pages.Max(p => p.PreferredSize.Width);
        foreach (var p in pages) { p.MinimumSize = new Size(maxW, maxH); p.Dock = DockStyle.None; stack.Controls.Add(p); }
        dlg.Content.Controls.Add(stack);
        dlg.Footer.Controls.Add(dlg.MakeButton("닫기", dlg.Close));
        Select(0);
        dlg.ShowOver(_hwnd);
        _dlg = null;
        _keys.Clear();   // 창이 떠 있는 동안 뗀 키가 눌린 채로 남지 않게
    }

    private Label Heading(string text) => _dlg!.MakeHeading(text);

    /// <summary>켜고 끄기 — 바꾸면 그 메뉴 명령을 그대로.</summary>
    private CheckBox Toggle(string text, bool on, int command)
    {
        var box = new CheckBox
        {
            Text = text, Checked = on, AutoSize = true, ForeColor = GameDialog.Ink, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Margin = new Padding(_dlg!.Px(4), _dlg.Px(4), 0, _dlg.Px(4)),
        };
        box.FlatAppearance.BorderColor = GameDialog.Gold;
        box.FlatAppearance.CheckedBackColor = GameDialog.ButtonBack;
        box.CheckedChanged += (_, _) => OnMenuCommand(command);
        return box;
    }

    /// <summary>고르기 칸 — 고르면 pick(고른 번호).</summary>
    private FlowLayoutPanel Choice(string label, string[] items, int selected, Action<int> pick)
    {
        var d = _dlg!;
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, d.Px(3), 0, d.Px(3)), BackColor = GameDialog.Panel };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = GameDialog.Ink, Margin = new Padding(d.Px(4), d.Px(6), d.Px(8), 0), MinimumSize = new Size(d.Px(110), 0) });
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = d.Px(200), FlatStyle = FlatStyle.Flat, BackColor = GameDialog.Back, ForeColor = GameDialog.Ink };
        combo.Items.AddRange(items);
        combo.SelectedIndex = Math.Clamp(selected, -1, items.Length - 1);
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedIndex >= 0) pick(combo.SelectedIndex); };
        row.Controls.Add(combo);
        return row;
    }

    /// <summary>0~100 소리 크기 막대.</summary>
    private FlowLayoutPanel Slider(string label, int value, Action<int> set)
    {
        var d = _dlg!;
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, d.Px(3), 0, d.Px(3)), BackColor = GameDialog.Panel };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = GameDialog.Ink, Margin = new Padding(d.Px(4), d.Px(10), d.Px(8), 0), MinimumSize = new Size(d.Px(80), 0) });
        var bar = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Value = Math.Clamp(value, 0, 100), Width = d.Px(220), BackColor = GameDialog.Panel };
        var shown = new Label { Text = $"{bar.Value}%", AutoSize = true, ForeColor = GameDialog.GoldText, Margin = new Padding(d.Px(4), d.Px(10), 0, 0) };
        bar.ValueChanged += (_, _) => { shown.Text = $"{bar.Value}%"; set(bar.Value); };
        row.Controls.Add(bar);
        row.Controls.Add(shown);
        return row;
    }

    private Button ActionButton(string text, Action act)
    {
        var d = _dlg!;
        return d.MakeButton(text, () => { d.Close(); act(); }).Also(b => b.Margin = new Padding(d.Px(4), d.Px(8), 0, d.Px(2)));
    }

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
            f.Controls.Add(Choice("마법 속도", SpellSpeeds.Select(SpellSpeedName).ToArray(), Array.IndexOf(SpellSpeeds, _settings.SpellSpeed),
                i => { _settings.SpellSpeed = SpellSpeeds[i]; _settings.Save(); }));
            f.Controls.Add(Heading("소리"));
            f.Controls.Add(Toggle("배경음악", _settings.Music, MenuMusic));
            f.Controls.Add(Toggle("효과음", _settings.Sound, MenuSound));
        }));
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> act) { act(value); return value; }
}
