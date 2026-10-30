using System.Text.Json;

namespace Fd2.Data;

/// <summary>
/// 원작의 장면 사건 · 시작 · 승리 뒤 함수(분석-장면 §5, 표 0x51B91 · 0x51D71 · 0x51DE9)를 낱말 묶음으로 옮긴 표 — SceneEvents.json(저장소에 넣은 리메이크 대본).
/// 낱말은 ["spawn", k] · ["talk", g] · ["join", c] 꼴의 배열이고 GameWindow 가 차례로 돌린다.
/// </summary>
public sealed class SceneEvents
{
    private readonly Dictionary<int, JsonElement[]> _events = new(), _start = new(), _victory = new();

    public static SceneEvents? Load()
    {
        try
        {
            using var s = typeof(SceneEvents).Assembly.GetManifestResourceStream("Fd2.Data.SceneEvents.json");
            if (s == null) return null;
            using var doc = JsonDocument.Parse(s);
            var e = new SceneEvents();
            Fill(doc.RootElement, "events", e._events);
            Fill(doc.RootElement, "start", e._start);
            Fill(doc.RootElement, "victory", e._victory);
            return e;
        }
        catch (JsonException) { return null; }
    }

    private static void Fill(JsonElement root, string name, Dictionary<int, JsonElement[]> into)
    {
        if (!root.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return;
        foreach (var p in obj.EnumerateObject())
            if (int.TryParse(p.Name, out int k) && p.Value.ValueKind == JsonValueKind.Array)
                into[k] = p.Value.EnumerateArray().Select(v => v.Clone()).ToArray();
    }

    /// <summary>사건 함수 표의 사건 — 없으면 빈 묶음.</summary>
    public JsonElement[] Event(int id) => _events.GetValueOrDefault(id, []);

    /// <summary>장면 시작 함수(싸움 앞 연출) — 없으면 null(대사 묶음 0 만 보인다).</summary>
    public JsonElement[]? Start(int scene) => _start.TryGetValue(scene, out var v) ? v : null;

    /// <summary>승리 뒤 함수(대사 · 합류) — 없으면 null.</summary>
    public JsonElement[]? Victory(int scene) => _victory.TryGetValue(scene, out var v) ? v : null;

    public int EventCount => _events.Count;
}

/// <summary>장면 정보의 사건 줄(분석-장면 §2.1) — 차례 255 는 꺼짐, 사건이 다음 줄을 켠다(<c>arm</c>).</summary>
public sealed class EventLine
{
    public int Turn, Event, When;
}
