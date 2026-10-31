using System.IO.Compression;
using System.Text.Json;

namespace Fd2.Data;

/// <summary>
/// 모션 내보내기 · 불러오기(편집기, 사용자 요청) — 전투 모션(캐릭터의 FIGANI 3c · 3c+1 · 3c+2)이나 마법 모션(전투 화면 효과 FDOTHER 항목 · 기술은 쓰는 사람의 3c+2)을
/// .zip 한 파일로. 안에는 <see cref="MotionFiles"/> 와 같은 꼴의 .obs 와 무엇인지 적은 「모션.json」.
/// <code>
/// 0005_로나-기사\0015.obs · 0016.obs · 0017.obs     전투 모션
/// 기타\0018.obs · 0020.obs                          FDOTHER 항목(마법 효과)
/// 모션.json  { "type": "battle" | "spell", "character": 5, "spell": 3, "entries": [18, 20], "characters": [5], "from": "용의기사2_original" }
/// </code>
/// 불러오면 모션\가져옴-파일이름 버전 폴더에 풀고, 고르기(고르기.json)에 그 캐릭터 · 항목이 이 버전을 쓰게 적는다 — 게임은 다음 장부터.
/// </summary>
public static class MotionPack
{
    public const string Manifest = "모션.json";

    public sealed record Info(string Type, int Character, int Spell, int[] Entries, int[] Characters, string From);

    /// <summary>캐릭터 c 의 전투 모션 셋(서기 · 공격 · 마법)을 zip 으로 — 지금 그 캐릭터가 쓰는 버전의 것. 넣은 모션 수.</summary>
    public static int ExportCharacter(string gameDir, int c, string zipPath)
    {
        using var zip = Create(zipPath);
        int n = AddCharacter(zip, gameDir, c);
        WriteManifest(zip, new Info("battle", c, -1, [], [c], MotionFiles.PickFor(gameDir, c)));
        return n;
    }

    /// <summary>마법 모션 — FDOTHER 항목들과(전투 화면 효과) 기술이면 쓰는 사람의 마법 몸짓(3c+2). 넣은 모션 수.</summary>
    public static int ExportSpell(string gameDir, int spell, int[] entries, int[] casters, string zipPath)
    {
        using var zip = Create(zipPath);
        int n = 0;
        foreach (int entry in entries)
        {
            var bytes = MotionFiles.ReadOther(gameDir, entry) ?? SafeLoad(Path.Combine(gameDir, "FDOTHER.DAT"), entry);
            if (bytes is not { Length: >= 12 } || bytes[0] == 0) continue;
            Put(zip, $"{MotionFiles.OtherFolder}/{MotionFiles.OtherFileName(entry)}", bytes);
            n++;
        }
        foreach (int c in casters)
        {
            var bytes = MotionFiles.Read(gameDir, c, 2) ?? SafeLoad(Path.Combine(gameDir, "FIGANI.DAT"), 3 * c + 2);
            if (bytes is not { Length: >= 12 } || bytes[0] == 0) continue;
            Put(zip, $"{MotionFiles.CharacterFolder(c)}/{MotionFiles.FileName(c, 2)}", bytes);
            n++;
        }
        WriteManifest(zip, new Info("spell", -1, spell, entries, casters, MotionFiles.VersionOf(gameDir)));
        return n;
    }

    /// <summary>zip 의 「모션.json」 — 없거나 못 읽으면 null.</summary>
    public static Info? Read(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry(Manifest) is not { } e) return null;
        using var s = e.Open();
        try { return JsonSerializer.Deserialize<Info>(s); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// 불러오기 — 모션\가져옴-파일이름 에 풀고 고르기에 적는다. 전투 모션은 <paramref name="toCharacter"/>(편집기에서 고른 캐릭터)에 — 내보낸 캐릭터와 달라도 된다(모션 바꿔 끼우기).
    /// 마법 모션은 내보낸 그 항목들에. 돌려주는 것: 고르기에 적은 버전 이름과 바뀐 것 설명.
    /// </summary>
    public static (string Version, string What) Import(string zipPath, string gameDir, int toCharacter = -1)
    {
        var info = Read(zipPath) ?? throw new InvalidDataException("모션.json 이 없는 묶음입니다 — 편집기에서 내보낸 .zip 만 불러올 수 있습니다.");
        string version = "가져옴-" + MotionFiles.Safe(Path.GetFileNameWithoutExtension(zipPath));
        string root = MotionFiles.VersionFolder(version);
        using var zip = ZipFile.OpenRead(zipPath);
        var what = new List<string>();
        if (info.Type == "battle")
        {
            int to = toCharacter >= 0 ? toCharacter : info.Character;
            string folder = Path.Combine(root, MotionFiles.CharacterFolder(to));
            Directory.CreateDirectory(folder);
            int n = 0;
            for (int kind = 0; kind < 3; kind++)
            {
                var e = zip.GetEntry($"{MotionFiles.CharacterFolder(info.Character)}/{MotionFiles.FileName(info.Character, kind)}");
                if (e == null) continue;
                e.ExtractToFile(Path.Combine(folder, MotionFiles.FileName(to, kind)), overwrite: true);
                n++;
            }
            MotionFiles.SetPick(to, version);
            what.Add(to == info.Character ? $"{CharacterNames.Of(to)} 전투 모션 {n}개" : $"{CharacterNames.Of(info.Character)} 의 전투 모션 {n}개 → {CharacterNames.Of(to)}");
        }
        else
        {
            foreach (int entry in info.Entries)
            {
                var e = zip.GetEntry($"{MotionFiles.OtherFolder}/{MotionFiles.OtherFileName(entry)}");
                if (e == null) continue;
                Directory.CreateDirectory(Path.Combine(root, MotionFiles.OtherFolder));
                e.ExtractToFile(Path.Combine(root, MotionFiles.OtherFolder, MotionFiles.OtherFileName(entry)), overwrite: true);
                MotionFiles.SetPick(MotionFiles.OtherKey(entry), version);
                what.Add($"효과 FDOTHER {entry}");
            }
            foreach (int c in info.Characters)
            {
                var e = zip.GetEntry($"{MotionFiles.CharacterFolder(c)}/{MotionFiles.FileName(c, 2)}");
                if (e == null) continue;
                string folder = Path.Combine(root, MotionFiles.CharacterFolder(c));
                Directory.CreateDirectory(folder);
                e.ExtractToFile(Path.Combine(folder, MotionFiles.FileName(c, 2)), overwrite: true);
                // 그 캐릭터의 서기 · 공격은 지금 고른 버전 것을 같이 베껴 둔다 — 고르기는 캐릭터 단위라 마법 몸짓만 바꾸려고
                MotionFiles.Reload();
                foreach (int kind in new[] { 0, 1 })
                    if (!File.Exists(Path.Combine(folder, MotionFiles.FileName(c, kind)))
                        && (MotionFiles.Read(gameDir, c, kind) ?? SafeLoad(Path.Combine(gameDir, "FIGANI.DAT"), 3 * c + kind)) is { } keep)
                        File.WriteAllBytes(Path.Combine(folder, MotionFiles.FileName(c, kind)), keep);
                MotionFiles.SetPick(c, version);
                what.Add($"{CharacterNames.Of(c)} 마법 몸짓");
            }
            if (info.Spell >= 0) what.Insert(0, $"마법 {info.Spell} {SpellNames.Of(info.Spell)}:");
        }
        File.WriteAllText(Path.Combine(root, "버전.txt"), $"{version}\n가져온 묶음: {Path.GetFileName(zipPath)} (내보낸 버전 {info.From})\n{DateTime.Now:yyyy-MM-dd HH:mm}\n");
        MotionFiles.Reload();
        return (version, string.Join(" ", what));
    }

    private static int AddCharacter(ZipArchive zip, string gameDir, int c)
    {
        int n = 0;
        for (int kind = 0; kind < 3; kind++)
        {
            var bytes = MotionFiles.Read(gameDir, c, kind) ?? SafeLoad(Path.Combine(gameDir, "FIGANI.DAT"), 3 * c + kind);
            if (bytes is not { Length: >= 12 } || bytes[0] == 0) continue;
            Put(zip, $"{MotionFiles.CharacterFolder(c)}/{MotionFiles.FileName(c, kind)}", bytes);
            n++;
        }
        return n;
    }

    private static ZipArchive Create(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        return ZipFile.Open(zipPath, ZipArchiveMode.Create);
    }

    private static void Put(ZipArchive zip, string name, byte[] bytes)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(bytes);
    }

    private static void WriteManifest(ZipArchive zip, Info info) =>
        Put(zip, Manifest, JsonSerializer.SerializeToUtf8Bytes(info, new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));

    private static byte[]? SafeLoad(string dat, int entry)
    {
        try { return File.Exists(dat) && entry < Dat.Count(dat) ? Dat.Load(dat, entry) : null; }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or IOException) { return null; }
    }
}
