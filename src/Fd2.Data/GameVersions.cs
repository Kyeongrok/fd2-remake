using System.Security.Cryptography;

namespace Fd2.Data;

/// <summary>
/// 여러 판(원본 · 모션패치 · FD2 Edit 로 고친 판들) — 찾기, 비교하기, 파일마다 고른 판으로 합치기.
/// </summary>
public static class GameVersions
{
    public sealed record Version(string Dir, string Name);

    /// <summary>파일 하나의 비교 — 같음 · 다름(DAT 면 바뀐 항목 번호) · 한쪽에만 있음.</summary>
    public sealed record FileDiff(string File, string Status, IReadOnlyList<int> ChangedEntries);

    /// <summary>바탕화면 · 다운로드 · 문서(네 단계까지)와 더한 폴더들에서 게임 폴더(필요한 파일이 다 있는 곳)를 찾는다.</summary>
    public static List<Version> Discover(IEnumerable<string>? extra = null)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        var found = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in extra ?? [])
            if (GameFolder.Missing(dir) == null) found[Path.GetFullPath(dir)] = new(Path.GetFullPath(dir), NameOf(dir));
        foreach (var root in roots.Where(Directory.Exists))
            foreach (var dir in Walk(root, 4))
                if (GameFolder.Missing(dir) == null) found.TryAdd(Path.GetFullPath(dir), new(Path.GetFullPath(dir), NameOf(dir)));
        return found.Values.OrderBy(v => v.Name).ToList();
    }

    private static string NameOf(string dir) => new DirectoryInfo(dir).Name;

    private static IEnumerable<string> Walk(string dir, int depth)
    {
        yield return dir;
        if (depth == 0) yield break;
        string[] subs;
        try { subs = Directory.GetDirectories(dir); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { yield break; }
        foreach (var s in subs)
        {
            if (Path.GetFileName(s).StartsWith('.')) continue;
            foreach (var d in Walk(s, depth - 1)) yield return d;
        }
    }

    /// <summary>두 판의 게임 파일(.DAT · .EXE · .B24 …, 폴더 맨 위)을 비교한다.</summary>
    public static List<FileDiff> Diff(string baseDir, string otherDir)
    {
        static HashSet<string> Files(string d) => Directory.GetFiles(d).Select(Path.GetFileName).OfType<string>()
            .Where(f => !f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var a = Files(baseDir);
        var b = Files(otherDir);
        var result = new List<FileDiff>();
        foreach (var f in a.Union(b, StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!a.Contains(f)) { result.Add(new(f, "비교 판에만", [])); continue; }
            if (!b.Contains(f)) { result.Add(new(f, "기준 판에만", [])); continue; }
            var pa = Path.Combine(baseDir, f);
            var pb = Path.Combine(otherDir, f);
            if (new FileInfo(pa).Length == new FileInfo(pb).Length && Hash(pa) == Hash(pb)) { result.Add(new(f, "같음", [])); continue; }
            result.Add(new(f, "다름", ChangedEntries(pa, pb)));
        }
        return result;
    }

    private static string Hash(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(s));
    }

    /// <summary>둘 다 "LLLLLL" 묶음이면 내용이 다른 항목 번호들(항목 수가 다르면 넘치는 것도), 아니면 빈 목록.</summary>
    public static List<int> ChangedEntries(string a, string b)
    {
        var da = File.ReadAllBytes(a);
        var db = File.ReadAllBytes(b);
        if (!IsPack(da) || !IsPack(db)) return [];
        var ea = Entries(da);
        var eb = Entries(db);
        var changed = new List<int>();
        for (int i = 0; i < Math.Max(ea.Count, eb.Count); i++)
            if (i >= ea.Count || i >= eb.Count || !ea[i].Span.SequenceEqual(eb[i].Span)) changed.Add(i);
        return changed;
    }

    private static bool IsPack(byte[] d) => d.Length > 10 && d.AsSpan(0, 6).SequenceEqual("LLLLLL"u8);

    private static List<ReadOnlyMemory<byte>> Entries(byte[] d)
    {
        int n = (Dat.U32(d, 6) - 6) / 4 - 1;   // 마지막 칸은 파일 끝 표시
        var list = new List<ReadOnlyMemory<byte>>(n);
        for (int i = 0; i < n; i++)
        {
            int s = Dat.U32(d, 6 + 4 * i), e = i + 1 < n ? Dat.U32(d, 10 + 4 * i) : d.Length;
            list.Add(d.AsMemory(s, Math.Max(0, Math.Min(e, d.Length) - s)));
        }
        return list;
    }

    /// <summary>
    /// 합치기 — 기준 판 폴더를 통째로 target 에 베끼고, picks(파일 → 가져올 판 폴더)의 파일만 그 판 것으로 바꾼다.
    /// target 이 이미 있으면 거절한다(덮어쓰지 않는다).
    /// </summary>
    public static void Merge(string baseDir, IReadOnlyDictionary<string, string> picks, string target)
    {
        if (Directory.Exists(target)) throw new IOException($"'{target}' 이(가) 이미 있습니다.");
        CopyDir(baseDir, target);
        foreach (var (file, from) in picks)
        {
            var src = Path.Combine(from, file);
            if (File.Exists(src)) File.Copy(src, Path.Combine(target, file), overwrite: true);
        }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
