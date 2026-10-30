using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fd2.Data;

/// <summary>
/// 프롤로그 대사 파일 — 1장 앞에 트는 「왕 알현 → 아래스와 대화 → 쓰러진 유니」.
/// 원작(DOS) 자료에는 없는 장면이라, 이 PC 에 깔린 FD2Re(유니티 리메이크)의 이야기 대본(resources.assets 안의 S001 XML)과
/// 그 한국어 번역(BepInEx 번역 파일 · translations_*.json)을 읽어 %LOCALAPPDATA%\Fd2\prologue.json 으로 만든다.
/// 저장소 · 배포 파일에는 대사를 넣지 않는다 — 각자 PC 에서 만든다.
/// </summary>
public static partial class Prologue
{
    public sealed class Line
    {
        public string Speaker { get; set; } = "";
        /// <summary>얼굴 그림(DATO) 번호, 없으면 -1.</summary>
        public int Face { get; set; } = -1;
        public string Text { get; set; } = "";
    }

    public sealed class Act
    {
        public string Title { get; set; } = "";
        public List<Line> Lines { get; set; } = [];
    }

    public sealed class Script
    {
        /// <summary>어디서 만들었나 — FD2Re 폴더.</summary>
        public string Source { get; set; } = "";
        /// <summary>번역이 없어 원문(중국어) 그대로 둔 줄 수.</summary>
        public int Untranslated { get; set; }
        public List<Act> Acts { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>대사 파일 자리 — %LOCALAPPDATA%\Fd2\prologue.json.</summary>
    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "prologue.json");

    public static Script? Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Script>(File.ReadAllText(path), Json) : null; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Save(Script script, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(script, Json));
    }

    /// <summary>대사 파일이 있으면 그것을, 없으면 FD2Re 를 찾아 만들어 두고 — 못 만들면 null.</summary>
    public static Script? LoadOrBuild(IEnumerable<string>? near = null)
    {
        if (Load(FilePath) is { Acts.Count: > 0 } s) return s;
        if (FindFd2Re(near) is not { } dir) return null;
        var built = Build(dir);
        if (built.Acts.Count == 0) return null;
        try { Save(built, FilePath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return built;
    }

    // ── FD2Re 찾기 ───────────────────────────────────────────────────────────

    private static bool IsFd2Re(string dir) => File.Exists(Path.Combine(dir, "FD2Re_Data", "resources.assets"));

    /// <summary>
    /// FD2Re 폴더 찾기 — near(게임 폴더 · 실행 파일 자리)의 위 두 단계 언저리, 그다음 사용자 폴더(네 단계까지, AppData · 숨은 폴더 빼고).
    /// </summary>
    public static string? FindFd2Re(IEnumerable<string>? near = null)
    {
        var roots = new List<(string Dir, int Depth)>();
        foreach (var n in near ?? [])
        {
            var d = new DirectoryInfo(n);
            for (int up = 0; up < 3 && d != null; up++, d = d.Parent) roots.Add((d.FullName, 2));
        }
        roots.Add((Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 4));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, depth) in roots)
            foreach (var dir in Walk(root, depth, seen))
                if (IsFd2Re(dir)) return dir;
        return null;
    }

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { "AppData", "node_modules", "Windows", "Program Files", "Program Files (x86)", "FD2Re_Data", "bin", "obj" };

    private static IEnumerable<string> Walk(string dir, int depth, HashSet<string> seen)
    {
        if (!Directory.Exists(dir) || !seen.Add(dir)) yield break;
        yield return dir;
        if (depth == 0) yield break;
        string[] subs;
        try { subs = Directory.GetDirectories(dir); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { yield break; }
        foreach (var s in subs)
        {
            string name = Path.GetFileName(s);
            if (name.StartsWith('.') || Skip.Contains(name)) continue;
            foreach (var d in Walk(s, depth - 1, seen)) yield return d;
        }
    }

    // ── 만들기 ───────────────────────────────────────────────────────────────

    [GeneratedRegex("<Story id=\"S001\".*?</Story>", RegexOptions.Singleline)]
    private static partial Regex StoryRx();

    [GeneratedRegex("<StoryNode\\b.*?</StoryNode>", RegexOptions.Singleline)]
    private static partial Regex NodeRx();

    [GeneratedRegex("<Dialogue speaker=\"([^\"]*)\">(.*?)</Dialogue>", RegexOptions.Singleline)]
    private static partial Regex LineRx();

    /// <summary>원작 판의 이름으로 맞춘다 — 번역 파일은 「아레스」, 이 게임(원작 대사 · FD2 Edit)은 「아래스」.</summary>
    private static readonly (string From, string To)[] Names = [("아레스", "아래스")];

    /// <summary>FD2Re 폴더에서 프롤로그(S001 「开局前三幕」)를 읽어 한국어 대사 파일로.</summary>
    public static Script Build(string fd2re)
    {
        var tr = Translations(fd2re);
        byte[] assets = File.ReadAllBytes(Path.Combine(fd2re, "FD2Re_Data", "resources.assets"));
        string text = Encoding.UTF8.GetString(assets);

        // 간체 · 번체처럼 같은 대본이 여럿 들어 있다 — 번역이 가장 많이 맞는 것을 쓴다
        string? best = null;
        int bestHit = -1;
        foreach (Match m in StoryRx().Matches(text))
        {
            int hit = LineRx().Matches(m.Value).Count(l => tr.ContainsKey(Unescape(l.Groups[2].Value)));
            if (hit > bestHit) (best, bestHit) = (m.Value, hit);
        }
        var script = new Script { Source = fd2re };
        if (best == null) return script;

        // 장면이 바뀌는 곳(ChangeScene)마다 한 막
        var act = new Act();
        foreach (Match node in NodeRx().Matches(best))
        {
            foreach (Match l in LineRx().Matches(node.Value))
            {
                string speaker = Korean(Unescape(l.Groups[1].Value), tr, out _);
                string line = Korean(Unescape(l.Groups[2].Value), tr, out bool ok);
                if (!ok) script.Untranslated++;
                act.Lines.Add(new Line { Speaker = speaker, Face = FaceOf(speaker), Text = line });
            }
            if (node.Value.Contains("<ChangeScene>") && act.Lines.Count > 0)
            {
                script.Acts.Add(act);
                act = new Act();
            }
        }
        if (act.Lines.Count > 0) script.Acts.Add(act);
        for (int i = 0; i < script.Acts.Count; i++) script.Acts[i].Title = $"프롤로그 {i + 1}막";
        return script;
    }

    private static string Korean(string zh, Dictionary<string, string> tr, out bool ok)
    {
        ok = tr.TryGetValue(zh, out var ko);
        string s = ok ? ko! : zh;
        foreach (var (from, to) in Names) s = s.Replace(from, to);
        return s;
    }

    /// <summary>
    /// 얼굴 — 원작 이름 표(FD2 Edit)의 첫 직업 줄(동료 16명)에서 같은 이름. 그 밖의 번호(「미란-왕비」 같은)는 얼굴 그림이 다른 사람이라 쓰지 않는다.
    /// </summary>
    private static int FaceOf(string name)
    {
        for (int i = 0; i < 16; i++)
            if (CharacterNames.Of(i).Split('-')[0] == name) return i;
        return -1;
    }

    private static string Unescape(string s) => System.Net.WebUtility.HtmlDecode(s).Trim();

    /// <summary>
    /// 번역(원문 → 한국어) — BepInEx(XUnity) 번역 파일들(「원문=번역」 한 줄씩, \n · \= 이스케이프), 그다음 FD2Re 폴더의 translations*.json.
    /// </summary>
    private static Dictionary<string, string> Translations(string fd2re)
    {
        var tr = new Dictionary<string, string>();
        string ko = Path.Combine(fd2re, "BepInEx", "Translation", "ko");
        if (Directory.Exists(ko))
            foreach (var f in Directory.GetFiles(ko, "*.txt", SearchOption.AllDirectories))
                foreach (var raw in File.ReadLines(f))
                {
                    if (raw.StartsWith("//")) continue;
                    int eq = SplitAt(raw);
                    if (eq <= 0) continue;
                    string k = XUnity(raw[..eq]).Trim(), v = XUnity(raw[(eq + 1)..]).Trim();
                    if (k.Length > 0 && v.Length > 0) tr.TryAdd(k, v);
                }
        foreach (var f in Directory.GetFiles(fd2re, "translations*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f)) is { } d)
                    foreach (var (k, v) in d) tr.TryAdd(k.Trim(), v.Trim());
            }
            catch (JsonException) { }
        }
        return tr;
    }

    /// <summary>이스케이프 안 된 첫 「=」 자리.</summary>
    private static int SplitAt(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '=') return i;
        }
        return -1;
    }

    private static string XUnity(string s) => s.Replace("\\n", "\n").Replace("\\=", "=").Replace("\\\\", "\\");
}
