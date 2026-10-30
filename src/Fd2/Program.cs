using Fd2.Native;

namespace Fd2;

internal static class Program
{
    /// <summary>원본 게임 폴더: 첫 인자 → 환경 변수 FD2_DIR → 바탕화면의 모션패치 폴더.</summary>
    private static string GameDir(string[] args)
    {
        if (args.Length > 0) return args[0];
        var env = Environment.GetEnvironmentVariable("FD2_DIR");
        if (!string.IsNullOrEmpty(env)) return env;
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desk, "용의기사2 모션패치5", "용의기사2 모션패치5");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string dir = GameDir(args);
        foreach (var f in new[] { "FDFIELD.DAT", "FDSHAP.DAT", "FDOTHER.DAT", "FDICON.B24" })
        {
            if (File.Exists(Path.Combine(dir, f))) continue;
            Win32.MessageBoxW(IntPtr.Zero, $"'{dir}' 에 {f} 가 없습니다.\n게임 폴더를 첫 인자나 FD2_DIR 로 알려 주세요.", "FD2", 0x10);
            return;
        }
        using var window = new GameWindow(dir);
        window.Run();
    }
}
