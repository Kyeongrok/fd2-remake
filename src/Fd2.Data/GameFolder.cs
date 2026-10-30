namespace Fd2.Data;

/// <summary>원본 게임 폴더 찾기 — 게임과 편집기가 같이 쓴다.</summary>
public static class GameFolder
{
    public static readonly string[] Required = ["FDFIELD.DAT", "FDSHAP.DAT", "FDOTHER.DAT", "FDICON.B24", "FIGANI.DAT"];

    /// <summary>첫 인자 → 환경 변수 FD2_DIR → 바탕화면의 모션패치 폴더.</summary>
    public static string Find(string[] args)
    {
        if (args.Length > 0) return args[0];
        var env = Environment.GetEnvironmentVariable("FD2_DIR");
        if (!string.IsNullOrEmpty(env)) return env;
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desk, "용의기사2 모션패치5", "용의기사2 모션패치5");
    }

    /// <summary>빠진 파일 이름, 다 있으면 null.</summary>
    public static string? Missing(string dir) => Required.FirstOrDefault(f => !File.Exists(Path.Combine(dir, f)));

    /// <summary>기본 팔레트 — FDOTHER.DAT 0번.</summary>
    public static uint[] Palette(string dir) => Data.Palette.FromVga(Dat.Load(Path.Combine(dir, "FDOTHER.DAT"), 0));
}
