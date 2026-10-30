using Fd2.Data;
using Fd2.Native;

namespace Fd2;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string dir = GameFolder.Find(args);
        if (GameFolder.Missing(dir) is { } f)
        {
            Win32.MessageBoxW(IntPtr.Zero, $"'{dir}' 에 {f} 가 없습니다.\n게임 폴더를 첫 인자나 FD2_DIR 로 알려 주세요.", "FD2", 0x10);
            return;
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
}
