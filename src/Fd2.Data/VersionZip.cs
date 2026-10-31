using System.IO.Compression;

namespace Fd2.Data;

/// <summary>
/// 버전 묶음(.zip) — 카페 · 사이트에서 받은 용의기사2 판(모션패치 · 파워 인핸스드 …)을 풀어 버전 폴더로.
/// 묶음 안 어느 깊이든 FD2.EXE 가 든 폴더를 게임 폴더로 보고, 그 폴더의 .DAT · .B24 · FD2.EXE 와 수치 표(tables/*.json)만 꺼낸다.
/// </summary>
/// <remarks>윈도우 압축 프로그램이 만든 묶음은 이름이 CP949 일 수 있다 — 찾는 이름(FD2.EXE · .DAT)은 영문이라 그대로 맞고, 폴더 이름은 버린다.</remarks>
public static class VersionZip
{
    /// <summary>묶음 안 게임 폴더(앞 경로, 맨 위면 "") — FD2.EXE 와 FDFIELD.DAT 가 함께 있는 곳. 없으면 null.</summary>
    public static string? GameRoot(ZipArchive zip)
    {
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        foreach (var exe in names.Where(n => Name(n).Equals("FD2.EXE", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n.Length))
        {
            string root = exe[..^Name(exe).Length];
            if (names.Any(n => n.Equals(root + "FDFIELD.DAT", StringComparison.OrdinalIgnoreCase))) return root;
        }
        return null;
    }

    /// <summary>그 묶음이 용의기사2 버전인가 — 읽지 못하는 묶음이면 false.</summary>
    public static bool IsVersion(Stream zip)
    {
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            return GameRoot(archive) != null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// 게임 폴더를 target 에 푼다(먼저 target.part 에 풀고 다 되면 옮긴다). 다 풀린 게임 폴더면 true.
    /// progress 는 0~100.
    /// </summary>
    public static bool Extract(Stream zip, string target, Action<int>? progress = null)
    {
        string part = target + ".part";
        if (Directory.Exists(part)) Directory.Delete(part, recursive: true);
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (GameRoot(archive) is not { } root) return false;
            var picks = archive.Entries.Where(e =>
            {
                string n = e.FullName.Replace('\\', '/');
                if (!n.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                string rest = n[root.Length..];
                if (rest.StartsWith("tables/", StringComparison.OrdinalIgnoreCase)) return rest.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && rest.Count(c => c == '/') == 1;
                if (rest.Contains('/')) return false;
                string ext = Path.GetExtension(rest).ToUpperInvariant();
                return ext is ".DAT" or ".B24" || rest.Equals("FD2.EXE", StringComparison.OrdinalIgnoreCase);
            }).ToList();
            for (int i = 0; i < picks.Count; i++)
            {
                string rest = picks[i].FullName.Replace('\\', '/')[root.Length..];
                string to = rest.Contains('/') ? Path.Combine(part, TablesJson.Folder, Path.GetFileName(rest).ToLowerInvariant())
                                               : Path.Combine(part, rest.ToUpperInvariant());
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                picks[i].ExtractToFile(to, overwrite: true);
                progress?.Invoke((i + 1) * 100 / picks.Count);
            }
        }
        if (GameFolder.Missing(part) != null) { Directory.Delete(part, recursive: true); return false; }
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(part, target);
        return true;
    }

    /// <summary>묶음 파일 이름 → 버전 이름(확장자를 빼고 파일 이름에 못 쓰는 글자는 _).</summary>
    public static string VersionName(string zipName)
    {
        string name = Path.GetFileNameWithoutExtension(zipName);
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 0 ? name : "버전";
    }

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
}
