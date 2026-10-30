using System.Windows;
using Fd2.Data;
using Microsoft.Win32;

namespace Fd2.Editor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 인자 · FD2_DIR · 바탕화면의 모션패치 폴더 → 자동으로 찾기 → 폴더 고르기 창
        string? dir = GameFolder.Accept(GameFolder.Find(e.Args)) ?? GameFolder.Search() ?? AskFolder();
        if (dir == null) { Shutdown(); return; }
        new MainWindow(dir).Show();
    }

    /// <summary>게임 폴더를 못 찾았을 때 — 맞는 폴더를 고를 때까지 폴더 고르기 창(닫으면 null).</summary>
    private static string? AskFolder()
    {
        MessageBox.Show("용의기사2 게임 폴더를 찾지 못했습니다.\nFDFIELD.DAT 가 있는 폴더를 골라 주세요.", "FD2 편집기");
        while (true)
        {
            var dialog = new OpenFolderDialog { Title = "용의기사2 게임 폴더 고르기 (FDFIELD.DAT 가 있는 폴더)" };
            if (dialog.ShowDialog() != true) return null;
            if (GameFolder.Accept(dialog.FolderName) is { } dir) return dir;
            if (MessageBox.Show($"'{dialog.FolderName}' 에 {GameFolder.Missing(dialog.FolderName)} 가 없습니다.\n다른 폴더를 고르시겠어요?",
                    "FD2 편집기", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return null;
        }
    }
}
