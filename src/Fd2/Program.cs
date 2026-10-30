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
        using var window = new GameWindow(dir);
        window.Run();
    }
}
