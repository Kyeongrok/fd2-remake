using System.Drawing;
using System.Windows.Forms;

namespace Fd2;

/// <summary>PC 의 세이브 옮기기 창들(w-171) — 줄 고르기 · 코드 넣기 · 코드 알림. 게임 꼴 창(<see cref="GameDialog"/>).</summary>
internal sealed partial class GameWindow
{
    /// <summary>다른 스레드(올리기가 끝난 뒤)에서 띄울 코드 알림 — 게임 스레드가 다음 틀에 띄운다.</summary>
    private volatile string[]? _codeNotice;

    private partial void AskChoice(string title, string[] items, Action<int> pick)
    {
        if (Offscreen) return;
        using var dlg = new GameDialog(title, 460);
        int chosen = -1;
        for (int i = 0; i < items.Length; i++)
        {
            int k = i;
            var b = dlg.MakeButton(items[i], () => { chosen = k; dlg.Close(); });
            b.Dock = DockStyle.Fill;
            b.TextAlign = ContentAlignment.MiddleLeft;
            dlg.Content.Controls.Add(b);
        }
        dlg.Footer.Controls.Add(dlg.MakeButton("닫기", dlg.Close));
        dlg.ShowOver(_hwnd);
        if (chosen >= 0) pick(chosen);
    }

    private partial void AskLine(string title, string message, string hint, Action<string> done)
    {
        if (Offscreen) return;
        using var dlg = new GameDialog(title, 460);
        dlg.Content.Controls.Add(dlg.MakeLabel(message, wrap: 440));
        var box = new TextBox
        {
            Dock = DockStyle.Fill, MaxLength = 16, PlaceholderText = hint, CharacterCasing = CharacterCasing.Upper,
            BackColor = GameDialog.Panel, ForeColor = GameDialog.Ink, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(FontFamily.GenericMonospace, 18f), Margin = new Padding(0, dlg.Px(8), 0, dlg.Px(8)),
        };
        dlg.Content.Controls.Add(box);
        bool ok = false;
        box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ok = true; dlg.Close(); } };
        dlg.Footer.Controls.Add(dlg.MakeButton("취소", dlg.Close));
        dlg.Footer.Controls.Add(dlg.MakeButton("받기", () => { ok = true; dlg.Close(); }));
        dlg.Shown += (_, _) => box.Focus();
        dlg.ShowOver(_hwnd);
        if (ok && box.Text.Trim().Length > 0) done(box.Text);
    }

    /// <summary>올리기는 뒤 스레드에서 끝난다 — 창은 게임 스레드에서 띄우게 맡긴다(<see cref="PollSaveCodeNotice"/>).</summary>
    private partial void ShowSaveCode(string code, string message) => _codeNotice = [code, message];

    /// <summary>게임 스레드의 틀마다 — 맡겨 둔 코드 알림이 있으면 띄운다.</summary>
    private void PollSaveCodeNotice()
    {
        if (_codeNotice is not { } n || Offscreen) return;
        _codeNotice = null;
        string code = n[0], message = n[1];
        bool copied = false;
        try { Clipboard.SetText(code); copied = true; } catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or ThreadStateException) { }
        using var dlg = new GameDialog("세이브 코드", 460);
        dlg.Content.Controls.Add(new Label
        {
            Text = code, AutoSize = true, Font = new Font(FontFamily.GenericMonospace, 32f, FontStyle.Bold), ForeColor = Color.FromArgb(0xFF, 0xE0, 0x70),
            Margin = new Padding(0, dlg.Px(6), 0, dlg.Px(10)), Anchor = AnchorStyles.None,
        });
        if (copied) dlg.Content.Controls.Add(dlg.MakeLabel("코드를 클립보드에 복사했습니다.", dim: true, wrap: 440));
        dlg.Content.Controls.Add(dlg.MakeLabel(message, wrap: 440));
        dlg.Footer.Controls.Add(dlg.MakeButton("닫기", dlg.Close));
        dlg.Footer.Controls.Add(dlg.MakeButton("복사", () => { try { Clipboard.SetText(code); } catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or ThreadStateException) { } }));
        dlg.ShowOver(_hwnd);
    }
}
