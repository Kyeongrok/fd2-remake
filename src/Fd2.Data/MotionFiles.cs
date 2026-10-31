using System.Text.Json;

namespace Fd2.Data;

/// <summary>
/// 버전마다 뽑아 둔 전투 모션 파일(.obs) — 창세기전3 파트2 처럼 캐릭터 폴더에 원본 꼴 그대로 복사해 두고, 게임은 그 파일로 돈다.
/// <code>
/// 모션\&lt;버전 이름&gt;\0005_로나-기사\0015.obs · 0016.obs · 0017.obs   (FIGANI 항목 3c 서기 · 3c+1 공격 · 3c+2 마법, 바이트 그대로)
/// 모션\고르기.json   { "5": "용의기사2 모션패치5" }   (캐릭터마다 다른 버전의 모션을 쓸 때 — 없으면 지금 버전)
/// </code>
/// 지금 버전의 폴더가 없으면 처음 쓸 때 FIGANI.DAT 에서 뽑는다. 파일이 없거나 못 쓰는 곳이면 FIGANI.DAT 를 바로 읽는다.
/// 전투 화면 마법 효과 같은 FDOTHER 의 FIGANI 꼴 항목은 고르기에 그 항목(키 10000 + 번호)이 있을 때만 그 버전의 기타\NNNN.obs 를 쓴다(편집기 「불러오기」).
/// </summary>
public static class MotionFiles
{
    public const string Ext = ".obs";

    /// <summary>모션 폴더 — 실행 파일 옆 「모션」(안드로이드처럼 못 쓰는 곳이면 앱이 바꿔 둔다).</summary>
    public static string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "모션");

    private static string PicksPath => Path.Combine(Root, "고르기.json");

    private static readonly HashSet<string> Ensured = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<int, string>? _picks;

    /// <summary>버전 이름 — 게임 폴더 이름(버전 고르기에 보이는 이름과 같다).</summary>
    public static string VersionOf(string gameDir) => new DirectoryInfo(gameDir).Name;

    public static string VersionFolder(string version) => Path.Combine(Root, Safe(version));

    public static string CharacterFolder(int c) => $"{c:D4}_{Safe(CharacterNames.Of(c))}";

    public static string FileName(int c, int kind) => $"{3 * c + kind:D4}{Ext}";

    /// <summary>뽑아 둔 버전들(모션 폴더 안의 폴더 이름).</summary>
    public static List<string> Versions()
    {
        try { return Directory.Exists(Root) ? Directory.GetDirectories(Root).Select(d => Path.GetFileName(d)!).OrderBy(n => n).ToList() : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>게임 폴더의 FIGANI.DAT 를 캐릭터 폴더마다 .obs 로 뽑는다(이미 있는 파일은 덮어쓴다). 뽑은 파일 수.</summary>
    public static int Export(string gameDir)
    {
        string fig = Path.Combine(gameDir, "FIGANI.DAT");
        string root = VersionFolder(VersionOf(gameDir));
        int n = Dat.Count(fig), made = 0;
        for (int k = 0; k < n; k++)
        {
            var d = Dat.Load(fig, k);
            if (d.Length < 12 || d[0] == 0) continue;   // 빈 모션
            string folder = Path.Combine(root, CharacterFolder(k / 3));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, FileName(k / 3, k % 3)), d);
            made++;
        }
        File.WriteAllText(Path.Combine(root, "버전.txt"), $"{VersionOf(gameDir)}\n{gameDir}\n{DateTime.Now:yyyy-MM-dd HH:mm}\n");
        return made;
    }

    /// <summary>지금 버전 폴더가 없으면 뽑는다 — 못 쓰는 곳이면 조용히 그만둔다(FIGANI.DAT 를 바로 읽게).</summary>
    public static void Ensure(string gameDir)
    {
        if (!Ensured.Add(gameDir)) return;
        try { if (!Directory.Exists(VersionFolder(VersionOf(gameDir)))) Export(gameDir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentOutOfRangeException) { }
    }

    /// <summary>캐릭터 c 가 쓰는 버전 — 고르기.json 에 있으면 그 버전, 아니면 지금 버전.</summary>
    public static string PickFor(string gameDir, int c) => Picks().GetValueOrDefault(c) ?? VersionOf(gameDir);

    /// <summary>캐릭터 c 의 모션 버전을 정한다(null 이면 지금 버전으로 되돌림).</summary>
    public static void SetPick(int c, string? version)
    {
        var p = Picks();
        if (version == null) p.Remove(c); else p[c] = version;
        Directory.CreateDirectory(Root);
        File.WriteAllText(PicksPath, JsonSerializer.Serialize(p.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>다른 프로그램(편집기)이 고르기를 바꿨으면 다시 읽게.</summary>
    public static void Reload() => _picks = null;

    private static Dictionary<int, string> Picks()
    {
        if (_picks != null) return _picks;
        _picks = new();
        try
        {
            if (File.Exists(PicksPath) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PicksPath)) is { } saved)
                foreach (var (k, v) in saved)
                    if (int.TryParse(k, out int c)) _picks[c] = v;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return _picks;
    }

    /// <summary>캐릭터 c 의 모션 kind 바이트 — 고른 버전의 .obs, 없으면 null(그때는 FIGANI.DAT).</summary>
    public static byte[]? Read(string gameDir, int c, int kind)
    {
        Ensure(gameDir);
        string path = Path.Combine(VersionFolder(PickFor(gameDir, c)), CharacterFolder(c), FileName(c, kind));
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>FDOTHER 항목의 고르기 키(캐릭터 번호와 겹치지 않게).</summary>
    public static int OtherKey(int entry) => 10000 + entry;

    public const string OtherFolder = "기타";

    public static string OtherFileName(int entry) => $"{entry:D4}{Ext}";

    /// <summary>FDOTHER 항목 entry 의 바이트 — 고르기에 그 항목이 있으면 그 버전의 기타\NNNN.obs, 아니면 null(그때는 FDOTHER.DAT).</summary>
    public static byte[]? ReadOther(string gameDir, int entry)
    {
        if (!Picks().TryGetValue(OtherKey(entry), out var version)) return null;
        string path = Path.Combine(VersionFolder(version), OtherFolder, OtherFileName(entry));
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static string Safe(string name) => new(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());
}
