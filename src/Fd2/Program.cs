using Fd2.Data;
using Fd2.Native;

namespace Fd2;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 인자 → FD2_DIR → 게임 > 판 고르기 · 폴더 고르기 창으로 고른 판 → 바탕화면의 원본 판 폴더 → 자동으로 찾기(원본 먼저) → 폴더 고르기 창
        var settings = Settings.Load();
        bool told = args.Length > 0 || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FD2_DIR"));
        string? dir = told ? GameFolder.Accept(GameFolder.Find(args))
                           : GameFolder.Accept(settings.GameDir) ?? GameFolder.Accept(GameFolder.Find(args));
        dir ??= GameFolder.Search();
        if (dir == null)
        {
            dir = AskFolder();
            if (dir == null) return;
            settings.GameDir = dir;   // 손으로 고른 폴더는 다음에 켤 때도 그대로(자동으로 찾은 것은 남기지 않는다 — 기본 판이 바뀌면 따라가게)
            settings.Save();
        }
        try
        {
            // 손으로 켰으면 버전 고르기부터(버전을 골라 다시 켠 것은 폴더를 인자로 받으니 묻지 않는다)
            using var window = new GameWindow(dir) { PickVersionFirst = !told };
            window.Run();
        }
        catch (Exception ex)
        {
            // 터지면 까닭을 남기고 알린다 — %LOCALAPPDATA%\Fd2\crash.txt
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, ex.ToString());
            Win32.MessageBoxW(IntPtr.Zero, $"오류로 멈췄습니다.\n{ex.GetType().Name}: {ex.Message}\n\n자세한 내용: {log}", "FD2", 0x10);
        }
    }

    /// <summary>게임 폴더를 못 찾았을 때 — 폴더 고르기 창을 띄운다. 맞는 폴더를 고를 때까지(닫으면 null).</summary>
    private static string? AskFolder()
    {
        if (GameWindow.Offscreen) return null;
        Win32.MessageBoxW(IntPtr.Zero, "용의기사2 게임 폴더를 찾지 못했습니다.\nFDFIELD.DAT 가 있는 폴더를 골라 주세요.", "FD2", 0x40);
        while (true)
        {
            string? picked = FolderPicker.Pick(IntPtr.Zero, "용의기사2 게임 폴더 고르기 (FDFIELD.DAT 가 있는 폴더)");
            if (picked == null) return null;
            if (GameFolder.Accept(picked) is { } dir) return dir;
            const int IDRETRY = 4;
            if (Win32.MessageBoxW(IntPtr.Zero, $"'{picked}' 에 {GameFolder.Missing(picked)} 가 없습니다.\n다른 폴더를 고르시겠어요?", "FD2", 0x05 | 0x30) != IDRETRY)
                return null;
        }
    }
}
