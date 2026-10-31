using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fd2.Data;

/// <summary>
/// FD2.EXE 의 수치 표(<see cref="GameTables"/>)를 표마다 .json 한 파일로 — 동료 · 성장 · 몬스터 · 아이템 · 마법 · 직업 · 상점 · 지형 · 배우는 마법 · 전직 · 전직 물건.
/// 게임 폴더의 <c>tables</c> 에 두면 <see cref="GameTables.Load"/> 가 EXE 대신 이 값을 쓴다(표가 없는 파일은 EXE 값 그대로).
/// </summary>
/// <remarks>
/// 줄마다 <c>id</c> · <c>name</c>(게임 이름)과 <c>raceName</c> 같은 이름 칸을 덧붙여 읽기 쉽게 한다 — 읽을 때는 그 칸들을 무시하고 수치만 쓴다.
/// 원작 자료라 저장소에는 넣지 않는다.
/// </remarks>
public static class TablesJson
{
    /// <summary>게임 폴더 안 표 폴더 이름.</summary>
    public const string Folder = "tables";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글을 \uXXXX 로 바꾸지 않는다
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DirOf(string gameDir) => Path.Combine(gameDir, Folder);

    private static string Item(int i) => i is < 0 or 255 ? "" : GameNames.Item(i);

    /// <summary>표 줄 하나 — 앞에 id · name, 뒤에 수치(레코드 그대로), 그다음 이름 칸들.</summary>
    private static JsonObject Row<T>(int id, string name, T record, params (string Key, JsonNode? Value)[] hints)
    {
        var row = new JsonObject { ["id"] = id, ["name"] = name };
        foreach (var (k, v) in JsonSerializer.SerializeToNode(record, Options)!.AsObject().ToList())
        {
            if (k == "secretKeyName") continue;   // 셈해 내는 칸
            row[k] = v?.DeepClone();
        }
        foreach (var (k, v) in hints) row[k] = v;
        return row;
    }

    private static JsonArray Names(IEnumerable<int> items) => new(items.Select(x => (JsonNode)Item(x)).ToArray());

    /// <summary>표들을 gameDir/tables 에 쓴다. 쓴 파일 경로들을 돌려준다.</summary>
    public static List<string> Export(GameTables t, string gameDir)
    {
        string outDir = DirOf(gameDir);
        Directory.CreateDirectory(outDir);
        var written = new List<string>();
        void Write(string name, IEnumerable<JsonNode> rows)
        {
            string path = Path.Combine(outDir, name + ".json");
            File.WriteAllText(path, new JsonArray(rows.ToArray()).ToJsonString(Options));
            written.Add(path);
        }

        Write("heroes", t.Heroes.Select((h, i) => Row(i, CharacterNames.Of(i), h,
            ("raceName", GameNames.Race(h.Race)), ("className", GameNames.Class(h.Class)),
            ("weaponName", Item(h.Weapon)), ("armorName", Item(h.Armor)), ("bagNames", Names(h.Bag)))));
        Write("growths", t.Growths.Select((g, i) => Row(i, CharacterNames.Of(i), g)));
        Write("monsters", t.Monsters.Select((m, i) => Row(i, CharacterNames.Of(GameTables.MonsterBase + i), m,
            ("raceName", GameNames.Race(m.Race)), ("className", GameNames.Class(m.Class)))));
        Write("items", t.Items.Select((it, i) => Row(i, GameNames.Item(i), it)));
        Write("spells", t.Spells.Select((s, i) => Row(i, SpellNames.Of(i), s)));
        Write("classes", t.Classes.Select((c, i) => Row(i, GameNames.Class(i), c)));
        Write("shops", t.Shops.Select((s, i) => Row(i, $"{i + 2}장 앞 마을", s,
            ("weaponNames", Names(s.Weapons)), ("toolNames", Names(s.Tools)), ("secretNames", Names(s.Secret)), ("secretKeyName", s.SecretKeyName))));
        Write("terrain", Enumerable.Range(0, t.TerrainAttack.Count).Select(g =>
            (JsonNode)new JsonObject { ["id"] = g, ["attack"] = t.TerrainAttack[g], ["defense"] = t.TerrainDefense[g] }));
        Write("spell-learn", t.SpellLearn.Select((row, k) => (JsonNode)new JsonObject
        {
            ["id"] = k,
            ["learn"] = new JsonArray(row.Select(p => (JsonNode)new JsonObject { ["level"] = p.Level, ["spell"] = p.Spell, ["name"] = SpellNames.Of(p.Spell) }).ToArray()),
        }));
        Write("class-change", t.ClassChange.Select((c, f) => (JsonNode)new JsonObject
        {
            ["id"] = 32 + f, ["name"] = CharacterNames.Of(32 + f), ["class"] = c.Class, ["className"] = GameNames.Class(c.Class), ["moveAdd"] = c.MoveAdd,
        }));
        Write("class-change-items", t.ClassChangeItem.Select((it, f) => (JsonNode)new JsonObject
        {
            ["id"] = f, ["name"] = CharacterNames.Of(f), ["item"] = it, ["itemName"] = Item(it),
        }));
        return written;
    }

    /// <summary>표 폴더가 없으면 EXE 값으로 처음 한 번 만든다(게임을 처음 켤 때) — 못 쓰는 폴더면 조용히 넘어간다.</summary>
    public static void EnsureExported(string gameDir)
    {
        if (Directory.Exists(DirOf(gameDir)) || GameTables.Load(gameDir, json: false) is not { } t) return;
        try { Export(t, gameDir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// gameDir/tables 에 있는 표로 t 를 덮는다 — 없는 파일은 건너뛴다. 줄 수가 EXE 와 다르면 그 표는 쓰지 않는다(자리가 어긋난 값을 막는다).
    /// 덮은 표 이름들을 돌려준다.
    /// </summary>
    public static List<string> Apply(GameTables t, string gameDir)
    {
        var used = new List<string>();
        string dir = DirOf(gameDir);
        if (!Directory.Exists(dir)) return used;

        List<T>? Read<T>(string name, int count)
        {
            string path = Path.Combine(dir, name + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                var rows = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Options);
                if (rows == null || rows.Count != count) return null;
                used.Add(name);
                return rows;
            }
            catch (JsonException) { return null; }
        }

        if (Read<HeroInfo>("heroes", t.Heroes.Count) is { } heroes) t.Heroes = heroes;
        if (Read<Growth>("growths", t.Growths.Count) is { } growths) t.Growths = growths;
        if (Read<MonsterInfo>("monsters", t.Monsters.Count) is { } monsters) t.Monsters = monsters;
        if (Read<ItemInfo>("items", t.Items.Count) is { } items) t.Items = items;
        if (Read<SpellInfo>("spells", t.Spells.Count) is { } spells) t.Spells = spells;
        if (Read<ClassInfo>("classes", t.Classes.Count) is { } classes) t.Classes = classes;
        if (Read<ShopInfo>("shops", t.Shops.Count) is { } shops) t.Shops = shops;
        if (Read<TerrainRow>("terrain", t.TerrainAttack.Count) is { } terrain)
            (t.TerrainAttack, t.TerrainDefense) = (terrain.Select(r => r.Attack).ToList(), terrain.Select(r => r.Defense).ToList());
        if (Read<LearnRow>("spell-learn", t.SpellLearn.Count) is { } learn)
            t.SpellLearn = learn.Select(r => r.Learn.Select(p => (p.Level, p.Spell)).ToArray()).ToList();
        if (Read<ChangeRow>("class-change", t.ClassChange.Count) is { } change)
            t.ClassChange = change.Select(r => (r.Class, r.MoveAdd)).ToList();
        if (Read<ChangeItemRow>("class-change-items", t.ClassChangeItem.Count) is { } changeItems)
            t.ClassChangeItem = changeItems.Select(r => r.Item).ToList();
        return used;
    }

    private sealed record TerrainRow(int Attack, int Defense);
    private sealed record LearnPair(int Level, int Spell);
    private sealed record LearnRow(LearnPair[] Learn);
    private sealed record ChangeRow(int Class, int MoveAdd);
    private sealed record ChangeItemRow(int Item);
}
