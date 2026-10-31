using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Fd2;

/// <summary>
/// PC 게임 창에서 띄우는 창의 꼴(사용자 요청 — 창세기전3 파트2 처럼 디자인이 들어간 창) — 게임 제목 줄과 같은 남색 바탕 · 금색 테두리와 줄,
/// 위에 끌어 옮기는 제목 줄과 ✕, 납작한 금테 단추. 칸은 모두 글자 크기에 맞춰 자란다(화면 배율이 커도 글이 잘리지 않게).
/// </summary>
internal sealed class GameDialog : Form
{
    public static readonly Color Back = Color.FromArgb(0x16, 0x1D, 0x38), Bar = Color.FromArgb(0x1C, 0x28, 0x50), Panel = Color.FromArgb(0x10, 0x16, 0x2C);
    public static readonly Color Gold = Color.FromArgb(0xC8, 0xA0, 0x40), GoldText = Color.FromArgb(0xFF, 0xE0, 0x70), Ink = Color.FromArgb(0xEE, 0xF0, 0xFF), Dim = Color.FromArgb(0x88, 0x90, 0xB0);
    public static readonly Color ButtonBack = Color.FromArgb(0x28, 0x45, 0x8A), ButtonHover = Color.FromArgb(0x35, 0x5A, 0xB0);

    public static readonly Font Body = new("맑은 고딕", 10.5f), Bold = new("맑은 고딕", 10.5f, FontStyle.Bold), TitleFont = new("맑은 고딕", 12f, FontStyle.Bold);

    /// <summary>화면 배율(100% = 1) — 점 수로 정하는 너비에 곱한다.</summary>
    public readonly float Scale;

    /// <summary>제목 줄과 닫기 줄 사이 — 내용을 여기에.</summary>
    public readonly TableLayoutPanel Content;

    /// <summary>아래 단추 줄(오른쪽부터).</summary>
    public readonly FlowLayoutPanel Footer;

    public GameDialog(string title, int minWidth)
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = Back;
        ForeColor = Ink;
        Font = Body;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(2);   // 금색 테두리 자리
        MinimumSize = new Size(Px(minWidth), 0);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Back };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 제목 줄 — 끌면 창이 옮겨진다, 오른쪽 ✕
        var head = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, BackColor = Bar, Margin = new Padding(0), Padding = new Padding(Px(12), Px(6), Px(4), Px(6)) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleLabel = new Label { Text = title, AutoSize = true, Font = TitleFont, ForeColor = Ink, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(2), 0, 0) };
        var close = new Label { Text = "✕", AutoSize = true, Font = TitleFont, ForeColor = Dim, Cursor = Cursors.Hand, Padding = new Padding(Px(8), Px(2), Px(8), Px(2)), Anchor = AnchorStyles.Right };
        close.MouseEnter += (_, _) => { close.BackColor = Color.FromArgb(0xE8, 0x11, 0x23); close.ForeColor = Color.White; };
        close.MouseLeave += (_, _) => { close.BackColor = Bar; close.ForeColor = Dim; };
        close.Click += (_, _) => Close();
        head.Controls.Add(titleLabel, 0, 0);
        head.Controls.Add(close, 1, 0);
        foreach (Control c in new Control[] { head, titleLabel }) c.MouseDown += Drag;
        root.Controls.Add(head);
        root.Controls.Add(new Panel { Height = Math.Max(1, Px(2)), Dock = DockStyle.Fill, BackColor = Gold, Margin = new Padding(0) });

        Content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Back,
            Padding = new Padding(Px(14), Px(10), Px(14), Px(6)), Margin = new Padding(0),
        };
        Content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(Content);

        Footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = Back,
            Padding = new Padding(Px(10), Px(4), Px(10), Px(10)), Margin = new Padding(0),
        };
        root.Controls.Add(Footer);
        Controls.Add(root);
    }

    public int Px(int v) => (int)Math.Round(v * Scale);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Gold, 2);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void Drag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessageW(Handle, 0xA1, 2, 0);   // WM_NCLBUTTONDOWN, HTCAPTION
    }

    /// <summary>금테 납작한 단추.</summary>
    public Button MakeButton(string text, Action click)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = ButtonBack, ForeColor = Ink, Font = Body, Cursor = Cursors.Hand,
            Padding = new Padding(Px(10), Px(3), Px(10), Px(3)), Margin = new Padding(Px(6), Px(4), 0, Px(2)), MinimumSize = new Size(Px(84), 0),
        };
        b.FlatAppearance.BorderColor = Gold;
        b.FlatAppearance.MouseOverBackColor = ButtonHover;
        b.FlatAppearance.MouseDownBackColor = Gold;
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>금색 굵은 글 — 묶음 머리.</summary>
    public Label MakeHeading(string text) =>
        new() { Text = text, AutoSize = true, Font = Bold, ForeColor = GoldText, Margin = new Padding(0, Px(10), 0, Px(4)) };

    public Label MakeLabel(string text, bool dim = false, int wrap = 0) =>
        new() { Text = text, AutoSize = true, ForeColor = dim ? Dim : Ink, Margin = new Padding(0, Px(3), 0, Px(3)), MaximumSize = wrap > 0 ? new Size(Px(wrap), 0) : Size.Empty };

    /// <summary>게임 창을 주인으로 띄운다.</summary>
    public DialogResult ShowOver(IntPtr owner)
    {
        var w = new NativeWindow();
        w.AssignHandle(owner);
        try { return ShowDialog(w); }
        finally { w.ReleaseHandle(); }
    }
}
