using System.Drawing;
using System.Windows.Forms;

namespace Fd2;

/// <summary>PC 의 버그 리포트 창 — 게임 꼴 창(<see cref="GameDialog"/>)에 여러 줄 글 칸(한글 IME · 붙여 넣기 · 여러 줄이 그대로 된다). 게임 창을 주인으로 뜬다.</summary>
internal sealed partial class GameWindow
{
    private partial void AskBugText(string summary, Action<string> send)
    {
        if (Offscreen) { Toast("화면 밖 시험에서는 버그 리포트 창을 띄우지 않습니다"); return; }
        using var dlg = new GameDialog("버그 리포트", 560);
        dlg.Content.Controls.Add(dlg.MakeLabel("무엇이 어떻게 잘못됐는지 적어 주세요(어디서 · 무엇을 했더니 · 어떻게 됐나).", wrap: 540));
        var box = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, MaxLength = BugMaxText, Dock = DockStyle.Fill,
            BackColor = GameDialog.Panel, ForeColor = GameDialog.Ink, BorderStyle = BorderStyle.FixedSingle,
            Height = dlg.Px(200), Margin = new Padding(0, dlg.Px(6), 0, dlg.Px(6)),
        };
        dlg.Content.Controls.Add(box);
        // 함께 보내는 것 — 줄을 나눠 다 보이게(예전엔 높이를 정해 두어 잘렸다, 사용자 요청)
        dlg.Content.Controls.Add(dlg.MakeLabel(summary, dim: true, wrap: 540));
        bool ok = false;
        dlg.Footer.Controls.Add(dlg.MakeButton("취소", dlg.Close));
        dlg.Footer.Controls.Add(dlg.MakeButton("보내기", () => { ok = true; dlg.Close(); }));
        dlg.Shown += (_, _) => box.Focus();
        dlg.ShowOver(_hwnd);
        if (ok && box.Text.Trim().Length > 0) send(box.Text);
    }
}
