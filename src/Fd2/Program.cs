using Fd2.Data;
using Fd2.Native;

namespace Fd2;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 인자 → FD2_DIR → 게임 > 판 고르기로 고른 판 → 바탕화면의 모션패치 폴더 → 자동으로 찾기 → 폴더 고르기 창
        var settings = Settings.Load();
        bool told = args.Length > 0 || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FD2_DIR"));
        string? dir = told ? GameFolder.Accept(GameFolder.Find(args))
                           : GameFolder.Accept(settings.GameDir) ?? GameFolder.Accept(GameFolder.Find(args));
        dir ??= GameFolder.Search() ?? AskFolder();
        if (dir == null) return;
        if (!told && !string.Equals(dir, settings.GameDir, StringComparison.OrdinalIgnoreCase))
        {
            settings.GameDir = dir;   // 찾은 · 고른 폴더는 다음에 켤 때도 그대로
            settings.Save();
        }
        try
        {
            using var window = new GameWindow(dir);
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
