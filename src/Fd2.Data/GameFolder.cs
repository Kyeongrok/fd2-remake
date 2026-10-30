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

    /// <summary>
    /// 그 폴더가 게임 폴더면 그대로, 아니면 바로 아래 폴더 가운데 게임 폴더(겉 폴더를 고른 경우) — 없으면 null.
    /// </summary>
    public static string? Accept(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
        if (Missing(dir) == null) return Path.GetFullPath(dir);
        try { return Directory.GetDirectories(dir).FirstOrDefault(d => Missing(d) == null); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>
    /// 자동으로 찾기 — 실행 파일 옆 · 바탕화면 · 다운로드 · 문서에서. 여럿이면 이름에 「모션패치」가 든 판, 그다음 이름 차례.
    /// 못 찾으면 null.
    /// </summary>
    public static string? Search()
    {
        string here = AppContext.BaseDirectory;
        var extra = new[] { here, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(here)) ?? here }
            .Where(Directory.Exists).SelectMany(d => SafeDirs(d).Prepend(d));
        var found = GameVersions.Discover(extra);
        return found.OrderByDescending(v => v.Name.Contains("모션패치")).Select(v => v.Dir).FirstOrDefault();
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }

    /// <summary>빠진 파일 이름, 다 있으면 null.</summary>
    public static string? Missing(string dir) => Required.FirstOrDefault(f => !File.Exists(Path.Combine(dir, f)));

    /// <summary>기본 팔레트 — FDOTHER.DAT 0번.</summary>
    public static uint[] Palette(string dir) => Data.Palette.FromVga(Dat.Load(Path.Combine(dir, "FDOTHER.DAT"), 0));
}
