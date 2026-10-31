using System.Drawing;
using System.Windows.Forms;

namespace Fd2;

/// <summary>PC 의 버그 리포트 창 — WinForms 여러 줄 글 칸(한글 IME · 붙여 넣기 · 여러 줄이 그대로 된다). 게임 창을 주인으로 뜬다.</summary>
internal sealed partial class GameWindow
{
    private partial void AskBugText(string summary, Action<string> send)
    {
        if (Offscreen) { Toast("화면 밖 시험에서는 버그 리포트 창을 띄우지 않습니다"); return; }
        using var form = new Form
        {
            Text = "버그 리포트", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(560, 380), Font = new System.Drawing.Font("맑은 고딕", 10f),
            AutoScaleMode = AutoScaleMode.Dpi,
        };
        var hint = new Label { Text = "무엇이 어떻게 잘못됐는지 적어 주세요(어디서 · 무엇을 했더니 · 어떻게 됐나).", Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 8, 8, 0) };
        var box = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill, MaxLength = BugMaxText };
        var info = new Label { Text = summary, Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 4, 8, 0), ForeColor = System.Drawing.Color.DimGray };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6) };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 90 };
        var ok = new Button { Text = "보내기", DialogResult = DialogResult.OK, Width = 90 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        middle.Controls.Add(box);
        form.Controls.Add(middle);
        form.Controls.Add(hint);
        form.Controls.Add(info);
        form.Controls.Add(buttons);
        form.AcceptButton = null;   // 여러 줄 글이라 Enter 는 줄 바꿈
        form.CancelButton = cancel;
        var owner = new NativeWindow();
        owner.AssignHandle(_hwnd);
        try
        {
            if (form.ShowDialog(owner) == DialogResult.OK && box.Text.Trim().Length > 0) send(box.Text);
        }
        finally { owner.ReleaseHandle(); }
    }
}
