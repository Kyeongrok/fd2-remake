using System.Buffers.Binary;
using System.Text;

namespace Fd2.Data;

/// <summary>
/// 대사 내보내기 · 불러오기(FDTXT.DAT 의 장면 항목 ↔ UTF-8 글 파일). 원본 글자는 유니코드가 아니라 글꼴 번호라,
/// 글자표(번호 → 글자)로 아는 글자만 글로 쓰고 모르는 글자는 {번호} 로 둔다 — 고치지 않고 불러오면 바이트까지 같다.
/// </summary>
/// <remarks>
/// 파일 꼴: 「=== 항목 n ===」(장면 n-1) · 「--- 묶음 m ---」 머리 아래에 묶음 내용. 줄 바꿈 = FFFE, &lt;왼 c&gt; · &lt;오른 c&gt; 말하는 사람,
/// &lt;왼표 k&gt; · &lt;오른표 k&gt; 유닛 표 차례의 말하는 사람, &lt;쪽&gt; = FFFD, &lt;끝&gt; = FFFF, 그 밖은 {번호}.
/// 태그의 번호 뒤 이름은 보기 위한 것이라 읽을 때 넘긴다.
/// </remarks>
public static class DialogueFile
{
    public const int Space = 0x9E;

    /// <summary>이름 표와 이름 글이 글자 수까지 같은 줄에서 글자표 씨앗을 만든다 — 한 글자 번호에 두 글자가 나오면 뺀다.</summary>
    public static Dictionary<int, char> SeedGlyphs(string dir)
    {
        var map = new Dictionary<int, char> { [Space] = ' ' };
        var bad = new HashSet<int>();
        var names = new NameTable(dir);
        void Add(int[] codes, string text)
        {
            if (codes.Length != text.Length) return;
            for (int i = 0; i < codes.Length; i++)
            {
                if (map.TryGetValue(codes[i], out char had) && had != text[i]) bad.Add(codes[i]);
                map[codes[i]] = text[i];
            }
        }
        for (int i = 0; i < GameTables.ItemCount; i++) Add(names.Item(i), GameNames.Item(i));
        for (int i = 0; i < GameTables.SpellCount; i++) if (i != 31) Add(names.Spell(i), SpellNames.Of(i));
        for (int i = 0; i < 30; i++) { Add(names.Race(i), GameNames.Race(i)); Add(names.Class(i), GameNames.Class(i)); }
        foreach (int b in bad) map.Remove(b);
        foreach (int c in map.Keys.Where(k => map[k] is '<' or '>' or '{' or '}' or '#').ToList()) map.Remove(c);
        return map;
    }

    /// <summary>글자표 파일(한 줄에 「번호=글자」, 글자가 비면 넘김)을 map 에 덧씌운다.</summary>
    public static void ReadGlyphs(string path, Dictionary<int, char> map)
    {
        if (!File.Exists(path)) return;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            int eq = line.IndexOf('=');
            if (eq > 0 && eq == line.Length - 2 && int.TryParse(line[..eq], out int code)) map[code] = line[^1];
        }
    }

    /// <summary>글자표 파일을 쓴다 — 대사에 쓰인 번호와 아는 번호 모두, 모르는 번호는 「번호=」 로 두어 채우게 한다.</summary>
    public static void WriteGlyphs(string path, string dir, Dictionary<int, char> map)
    {
        var used = new SortedSet<int>(map.Keys);
        foreach (var (_, t) in Entries(dir))
            foreach (var group in Groups(t))
                for (int i = 0; i < group.Length; i++)
                {
                    int c = group[i];
                    if (c is 0xFFEE or 0xFFEF or 0xFFEC or 0xFFED) i++;
                    else if (c < 0xFF00) used.Add(c);
                }
        var text = new StringBuilder("# 글자표 — 「번호=글자」. 글자표.png 를 보고 빈 곳을 채운 뒤 다시 내보내면 그 글자가 글로 나온다.\n");
        foreach (int c in used) text.Append(c).Append('=').Append(map.TryGetValue(c, out char ch) ? ch.ToString() : "").Append('\n');
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>대사 전체를 글로.</summary>
    public static string Export(string dir, Dictionary<int, char> map)
    {
        var reverse = Reverse(map);
        var s = new StringBuilder("# 용의기사2 대사 — 줄 바꿈 · <쪽> · {번호}(글자표에 없는 글자)는 원본 그대로 옮겨진다. 맨 앞 # 줄은 읽지 않는다.\n");
        foreach (var (entry, t) in Entries(dir))
        {
            s.Append("=== 항목 ").Append(entry).Append(" ===\n");
            int m = 0;
            foreach (var g in Groups(t))
            {
                s.Append("--- 묶음 ").Append(m++).Append(" ---\n");
                for (int i = 0; i < g.Length; i++)
                {
                    int c = g[i];
                    string? tag = c switch { 0xFFEE => "왼", 0xFFEF => "오른", 0xFFEC => "왼표", 0xFFED => "오른표", _ => null };
                    if (tag != null && i + 1 < g.Length)
                    {
                        int v = g[++i];
                        s.Append('<').Append(tag).Append(' ').Append(v);
                        if (c is 0xFFEE or 0xFFEF && v < CharacterNames.Heroes) s.Append(' ').Append(CharacterNames.Of(v));
                        s.Append('>');
                    }
                    else if (c == 0xFFFE) s.Append('\n');
                    else if (c == 0xFFFD) s.Append("<쪽>");
                    else if (c == 0xFFFF) s.Append("<끝>");
                    else if (map.TryGetValue(c, out char ch) && reverse[ch] == c) s.Append(ch);
                    else s.Append('{').Append(c).Append('}');
                }
                s.Append('\n');
            }
        }
        return s.ToString();
    }

    /// <summary>글을 읽어 새 FDTXT.DAT 바이트로. 파일에 없는 항목은 그대로 둔다. 틀린 글은 줄 번호와 함께 InvalidDataException.</summary>
    public static byte[] Import(string dir, string text, Dictionary<int, char> map)
    {
        var reverse = Reverse(map);
        var entries = new SortedDictionary<int, SortedDictionary<int, List<ushort>>>();
        int entry = -1, group = -1;
        var body = new List<(int Line, string Text)>();
        void Flush()
        {
            if (group >= 0)
            {
                var words = new List<ushort>();
                for (int k = 0; k < body.Count; k++)
                {
                    if (k > 0) words.Add(0xFFFE);
                    Parse(body[k].Text, body[k].Line, reverse, words);
                }
                entries[entry][group] = words;
            }
            body.Clear();
            group = -1;
        }
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int last = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (int i = 0; i < last; i++)
        {
            string l = lines[i];
            if (l.StartsWith("=== 항목 ") && l.EndsWith(" ===") && int.TryParse(l[7..^4], out int e))
            {
                Flush();
                entry = e;
                entries[e] = new();
            }
            else if (l.StartsWith("--- 묶음 ") && l.EndsWith(" ---") && int.TryParse(l[7..^4], out int g))
            {
                if (entry < 1) throw new InvalidDataException($"{i + 1}줄: 묶음 앞에 항목 머리가 없습니다.");
                Flush();
                group = g;
            }
            else if (group >= 0) body.Add((i + 1, l));
            else if (l.Length > 0 && l[0] != '#') throw new InvalidDataException($"{i + 1}줄: 항목 · 묶음 머리 밖의 글입니다.");
        }
        Flush();

        string path = Path.Combine(dir, "FDTXT.DAT");
        var replaced = new Dictionary<int, byte[]>();
        int count = Dat.Count(path);
        foreach (var (e, groups) in entries)
        {
            if (e < 1 || e >= count) throw new InvalidDataException($"항목 {e} 은 FDTXT.DAT 에 없습니다.");
            var old = Groups(Dat.Load(path, e));
            if (groups.Count != old.Count || groups.Keys.Any(g => g >= old.Count))
                throw new InvalidDataException($"항목 {e}: 묶음은 {old.Count}개여야 합니다(사건 대본이 번호로 부른다).");
            replaced[e] = Pack(groups.Values.ToList());
        }
        return Rebuild(File.ReadAllBytes(path), replaced);
    }

    /// <summary>FDTXT.DAT 를 쓴다 — 처음 한 번은 FDTXT-backup.DAT 를 남긴다.</summary>
    public static void Save(string dir, byte[] data)
    {
        string path = Path.Combine(dir, "FDTXT.DAT"), backup = Path.Combine(dir, "FDTXT-backup.DAT");
        if (!File.Exists(backup)) File.Copy(path, backup);
        File.WriteAllBytes(path, data);
    }

    private static Dictionary<char, int> Reverse(Dictionary<int, char> map)
    {
        var r = new Dictionary<char, int>();
        foreach (var (code, ch) in map.OrderBy(kv => kv.Key)) r.TryAdd(ch, code);
        return r;
    }

    private static IEnumerable<(int Entry, byte[] Data)> Entries(string dir)
    {
        string path = Path.Combine(dir, "FDTXT.DAT");
        int count = Dat.Count(path);
        for (int e = 1; e < count; e++)
            if (Dat.Load(path, e) is { Length: >= 2 } t) yield return (e, t);
    }

    private static List<ushort[]> Groups(byte[] t)
    {
        var list = new List<ushort[]>();
        if (t.Length < 2) return list;
        int count = Dat.U16(t, 0) / 2;
        for (int m = 0; m < count; m++)
        {
            int from = Dat.U16(t, 2 * m), to = m + 1 < count ? Dat.U16(t, 2 * m + 2) : t.Length;
            list.Add(Enumerable.Range(0, Math.Max(0, to - from) / 2).Select(k => Dat.U16(t, from + 2 * k)).ToArray());
        }
        return list;
    }

    private static byte[] Pack(List<List<ushort>> groups)
    {
        int table = 2 * groups.Count, size = table + 2 * groups.Sum(g => g.Count);
        if (size > 0xFFFF) throw new InvalidDataException("한 장의 대사가 원본 꼴(64KB)보다 깁니다.");
        var o = new byte[size];
        int at = table;
        for (int m = 0; m < groups.Count; m++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(2 * m), (ushort)at);
            foreach (ushort w in groups[m]) { BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(at), w); at += 2; }
        }
        return o;
    }

    private static void Parse(string line, int lineNo, Dictionary<char, int> reverse, List<ushort> words)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch is '{' or '<')
            {
                int end = line.IndexOf(ch == '{' ? '}' : '>', i);
                if (end < 0) throw new InvalidDataException($"{lineNo}줄: 「{ch}」 가 닫히지 않았습니다.");
                string inner = line[(i + 1)..end];
                i = end;
                if (ch == '{')
                {
                    if (!ushort.TryParse(inner, out ushort v)) throw new InvalidDataException($"{lineNo}줄: {{{inner}}} 는 번호가 아닙니다.");
                    words.Add(v);
                    continue;
                }
                if (inner == "쪽") { words.Add(0xFFFD); continue; }
                if (inner == "끝") { words.Add(0xFFFF); continue; }
                var p = inner.Split(' ', 3);
                ushort? tag = p[0] switch { "왼" => (ushort)0xFFEE, "오른" => (ushort)0xFFEF, "왼표" => (ushort)0xFFEC, "오른표" => (ushort)0xFFED, _ => null };
                if (tag == null || p.Length < 2 || !ushort.TryParse(p[1], out ushort who)) throw new InvalidDataException($"{lineNo}줄: 모르는 표시 <{inner}>");
                words.Add(tag.Value);
                words.Add(who);
            }
            else if (reverse.TryGetValue(ch, out int code)) words.Add((ushort)code);
            else throw new InvalidDataException($"{lineNo}줄: 「{ch}」 는 글자표에 없습니다 — 원본 글꼴에 있는 글자만 쓸 수 있습니다.");
        }
    }

    /// <summary>항목을 갈아 끼우고 뒤 오프셋을 민다. 머리 6바이트와 끝 표시 뒤 꼬리는 그대로 둔다.</summary>
    private static byte[] Rebuild(byte[] dat, Dictionary<int, byte[]> replaced)
    {
        int first = BinaryPrimitives.ReadInt32LittleEndian(dat.AsSpan(6));
        int slots = (first - 6) / 4;
        var off = Enumerable.Range(0, slots).Select(i => BinaryPrimitives.ReadInt32LittleEndian(dat.AsSpan(6 + 4 * i))).ToArray();
        for (int i = 1; i < slots; i++) if (off[i] < off[i - 1]) throw new InvalidDataException("FDTXT.DAT 오프셋 차례가 맞지 않습니다.");
        using var o = new MemoryStream();
        o.Write(dat, 0, first);
        var head = new byte[first];
        Array.Copy(dat, head, first);
        for (int i = 0; i < slots; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(6 + 4 * i), (int)o.Position);
            if (i + 1 == slots) break;
            if (replaced.TryGetValue(i, out var r)) o.Write(r);
            else o.Write(dat, off[i], off[i + 1] - off[i]);
        }
        o.Write(dat, off[^1], dat.Length - off[^1]);
        var result = o.ToArray();
        Array.Copy(head, result, first);
        return result;
    }
}
