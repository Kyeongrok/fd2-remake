using System.Reflection;
using System.Text.Json;

namespace Fd2.Data;

/// <summary>
/// 계산식 계수 편집(사용자 요청 w-99) — <see cref="ExeRules"/> 의 숫자를 이름(「LevelUpExp」, 「EnemyLevel.SceneCap」 …)으로 꺼내고,
/// 게임 폴더 <c>tables/rules.json</c> 에 적힌 값으로 덮는다. 원본 FD2.EXE 는 건드리지 않는다(DOSBox 원작은 그대로).
/// </summary>
/// <remarks>
/// 이름은 ExeRules 의 공개 int 속성 · 적 레벨 규칙(<see cref="EnemyLevelRule"/>)의 int 칸을 그대로 쓴다 — 규칙이 늘면 저절로 편집기에 보인다.
/// 적 레벨 규칙이 없는 EXE(원본)는 그 줄이 없다. 파일에는 EXE 값과 다른 것만 적는다.
/// </remarks>
public static class RuleParams
{
    public const string FileName = "rules.json";

    public static string PathOf(string dir) => Path.Combine(dir, TablesJson.Folder, FileName);

    /// <summary>편집기 줄의 한글 이름 · 설명 — 없는 이름은 속성 이름 그대로.</summary>
    private static readonly Dictionary<string, (string Label, string Help)> Names = new()
    {
        ["LevelUpExp"] = ("레벨업 경험", "이 만큼 모이면 레벨이 오른다(원본 100, 곡우 60)"),
        ["AttackExpPromoted"] = ("치기 경험 · 전직 덧값", "경험을 나눌 때 전직한 사람 레벨에 더하는 값(원본 30)"),
        ["SpellExpPromoted"] = ("마법 경험 · 쓰는 사람 전직 덧값", "원본 30"),
        ["SpellTargetPromoted"] = ("마법 경험 · 대상 전직 덧값", "원본 30"),
        ["HealMul"] = ("회복 마법 경험 곱", "채운 양 × 곱 × 대상 Lv′ / 최대 HP(원본 40)"),
        ["HealPromoted"] = ("회복 마법 경험 · 전직 덧값", "원본 30"),
        ["RaiseMul"] = ("올리기 마법 경험 곱", "곱 × Lv′(원본 2)"),
        ["CureMul"] = ("풀기 마법 경험 곱", "곱 × Lv′(원본 4)"),
        ["CurePromoted"] = ("풀기 마법 경험 · 전직 덧값", "원본 30"),
        ["ActMul"] = ("행동술 경험 곱", "곱 × Lv′(원본 8)"),
        ["ActPromoted"] = ("행동술 경험 · 전직 덧값", "원본 30"),
        ["HexMul"] = ("걸기 마법 경험 곱", "곱 × 대상 Lv(원본 8)"),
        ["TeleportMul"] = ("전송술 경험 곱", "곱 × Lv′(원본 10)"),
        ["TeleportPromoted"] = ("전송술 경험 · 전직 덧값", "원본 30"),
        ["HexHit"] = ("걸기 마법 명중", "독 · 마비 같은 걸기 마법이 걸릴 확률 %(원본 50, 곡우 · 파워 70)"),
        ["MaxLevelGainKyle"] = ("최대 레벨 · 전직 전(꼴 30 · 31)", "-1 = 견주지 않음 — 리메이크는 지금 110/99 를 쓴다(참고용)"),
        ["MaxLevelGainOther"] = ("최대 레벨 · 전직 전(그 밖)", "-1 = 견주지 않음(참고용)"),
        ["MaxLevelResetKyle"] = ("최대 레벨 · 전직 뒤(꼴 30 · 31)", "-1 = 견주지 않음(참고용)"),
        ["MaxLevelResetOther"] = ("최대 레벨 · 전직 뒤(그 밖)", "-1 = 견주지 않음(참고용)"),
        ["EnemyLevel.Mul"] = ("적 레벨 · 점수 곱", "우리 편 점수 = 곱 × (역량 + 2 × 속도) + 내력 + 최대 HP"),
        ["EnemyLevel.Thr"] = ("적 레벨 · 강한 사람 문턱(/32)", "평균 × 값 / 2^밀기 보다 센 사람만 센다"),
        ["EnemyLevel.Shift"] = ("적 레벨 · 문턱 밀기", "2^값 으로 나눈다"),
        ["EnemyLevel.Pair"] = ("적 레벨 · 짝 빼기", "합 − 값 × k × (P − k)"),
        ["EnemyLevel.Div"] = ("적 레벨 · 나누기 밀기", "2^값 으로 나눈다(곡우 7 = /128)"),
        ["EnemyLevel.SceneCap"] = ("적 레벨 · 올리는 마지막 장", "장면 + 1 이 이보다 크면 안 올린다(0 이면 안 올림)"),
        ["EnemyLevel.Knee"] = ("적 레벨 · 꺾이는 점", "보탬이 이 값 이상이면 넘친 만큼 줄인다"),
        ["EnemyLevel.KneeMul"] = ("적 레벨 · 꺾임 곱", "(a − 꺾이는 점) × 곱 / 2^밀기 를 뺀다"),
        ["EnemyLevel.KneeShift"] = ("적 레벨 · 꺾임 밀기", ""),
        ["EnemyLevel.LowMul"] = ("적 레벨 · 낮은 적 곱", "유닛 표 레벨 ≤ 2 × 장이면 (a + 1) × 곱 / 2^밀기"),
        ["EnemyLevel.LowShift"] = ("적 레벨 · 낮은 적 밀기", ""),
    };

    public sealed record Param(string Key, string Label, string Help, int Value);

    private static IEnumerable<PropertyInfo> IntProps(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType == typeof(int) && p.CanRead);

    /// <summary>규칙의 숫자들 — 편집기 줄 차례(ExeRules 속성 차례, 그다음 적 레벨 규칙).</summary>
    public static List<Param> List(ExeRules rules)
    {
        var list = new List<Param>();
        void Add(string key, int v)
        {
            var (label, help) = Names.TryGetValue(key, out var n) ? n : (key, "");
            list.Add(new Param(key, label, help, v));
        }
        foreach (var p in IntProps(typeof(ExeRules))) Add(p.Name, (int)p.GetValue(rules)!);
        if (rules.EnemyLevel is { } e)
            foreach (var p in IntProps(typeof(EnemyLevelRule))) Add($"EnemyLevel.{p.Name}", (int)p.GetValue(e)!);
        // 승리 보상(고친 EXE 만) — 「Victory.장면.차례.칸」, 고칠 수 있는 칸만(차례 한도 · 조건 값 · 물건)
        foreach (var (r, key) in VictoryKeys(rules))
        {
            string head = $"{r.Scene + 1}장 승리 보상";
            string cond = r.CondOp == 0 ? "" : r.CondField == 0x21 ? "배열 0 번 사람 레벨" : r.CondField == 0x20 ? "배열 0 번 사람 직업" : $"배열 0 번 사람 +{r.CondField:X}h";
            list.Add(new Param($"{key}.TurnLimit", $"{head} · 차례 한도", r.TurnAbove == 0 ? "이 차례 이하로 이기면 준다" : "이 차례보다 늦게 이기면 준다", r.TurnLimit));
            if (r.CondOp != 0) list.Add(new Param($"{key}.CondValue", $"{head} · 조건 값", $"{cond} {(r.CondOp == 1 ? "=" : "≥")} 값 일 때만", r.CondValue));
            int n = 0;
            foreach (var (name, v) in new[] { ("Item1", r.Item1), ("Item2", r.Item2), ("Item3", r.Item3), ("Item4", r.Item4) })
                if (v >= 0) list.Add(new Param($"{key}.{name}", $"{head} · 물건 {++n}", "물건 번호(−1 이면 안 줌) — 가방 빈칸이 있는 첫 우리 편에게", v));
        }
        return list;
    }

    /// <summary>덮을 값으로 새 규칙을 만든다 — 없는 이름은 버린다.</summary>
    public static ExeRules Apply(ExeRules rules, IReadOnlyDictionary<string, int> over)
    {
        if (over.Count == 0) return rules;
        var copy = new ExeRules();
        foreach (var p in typeof(ExeRules).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite || p.SetMethod != null))
        {
            object? v = p.GetValue(rules);
            if (p.PropertyType == typeof(int) && over.TryGetValue(p.Name, out int o)) v = o;
            if (p.PropertyType == typeof(EnemyLevelRule) && v is EnemyLevelRule e) v = ApplyRecord(e, over);
            if (p.Name == nameof(ExeRules.VictoryRewards))
                v = VictoryKeys(rules).Select(x => ApplyRecord(x.Reward, over, x.Key)).ToList();
            p.SetValue(copy, v);
        }
        return copy;
    }

    private static EnemyLevelRule ApplyRecord(EnemyLevelRule e, IReadOnlyDictionary<string, int> over) => ApplyRecord(e, over, "EnemyLevel");

    /// <summary>승리 보상마다 이름 머리 — 「Victory.장면.그 장면 안 차례」.</summary>
    private static IEnumerable<(VictoryReward Reward, string Key)> VictoryKeys(ExeRules rules) =>
        rules.VictoryRewards.Select((r, i) => (r, $"Victory.{r.Scene}.{rules.VictoryRewards.Take(i).Count(x => x.Scene == r.Scene)}"));

    private static T ApplyRecord<T>(T e, IReadOnlyDictionary<string, int> over, string prefix) where T : class
    {
        // 기본 생성자 인자 차례 = 속성 차례 — 이름이 맞는 인자만 바꾼다
        var ctor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(a =>
        {
            object? v = typeof(T).GetProperty(a.Name!)?.GetValue(e);
            if (a.ParameterType == typeof(int) && over.TryGetValue($"{prefix}.{a.Name}", out int o)) v = o;
            return v;
        }).ToArray();
        return (T)ctor.Invoke(args);
    }

    /// <summary>게임 폴더 tables/rules.json — 없거나 못 읽으면 빈 것.</summary>
    public static Dictionary<string, int> Load(string dir)
    {
        try
        {
            string path = PathOf(dir);
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) is { } d) return d;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    /// <summary>EXE 값과 다른 것만 적는다 — 하나도 없으면 파일을 지운다(EXE 값 그대로).</summary>
    public static void Save(string dir, ExeRules exe, IReadOnlyDictionary<string, int> values)
    {
        var baseValues = List(exe).ToDictionary(p => p.Key, p => p.Value);
        var diff = values.Where(kv => baseValues.TryGetValue(kv.Key, out int b) && b != kv.Value).OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        string path = PathOf(dir);
        if (diff.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(diff, new JsonSerializerOptions { WriteIndented = true }));
    }
}
