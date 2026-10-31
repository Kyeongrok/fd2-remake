using System.Text.Json;

namespace Fd2.Data;

/// <summary>
/// 장면 사건을 글로 — 편집기 「이벤트」 탭. 시작 연출 · 사건 줄(차례 · 때) · 땅 칸 사건 · 사건 상자 · 승리 뒤 연출을 차례대로,
/// 낱말(<see cref="SceneEvents"/>)마다 무엇을 하는지 한 줄씩. 유닛 배열 번호는 이 장 유닛 표(우리 자리 P 다음부터)의 이름으로 푼다.
/// </summary>
public static class EventText
{
    private static readonly string[] Whens = ["적 차례 앞", "우리 차례 끝", "우리 차례 시작"];

    public static List<string> Describe(SceneEvents? ev, Scene scene)
    {
        var lines = new List<string>();
        int players = scene.Info.Length > 1 ? scene.Info[1] : 0;
        string Unit(int slot) =>
            slot < players ? $"우리 편 {slot}번" :
            slot - players < scene.Roster.Count ? $"{slot}번 {CharacterNames.Of(scene.Roster[slot - players].Icon)}" : $"{slot}번";

        lines.Add($"■ {scene.Index + 1}장 (장면 {scene.Index}) — 우리 자리 {players}, 그 밖 유닛 {scene.Roster.Count}");
        foreach (var g in scene.Roster.Select((r, i) => (r, i)).GroupBy(x => x.r.Phase).OrderBy(g => g.Key))
            lines.Add($"   등장 차례 {g.Key}{(g.Key == 0 ? "(처음부터)" : "")}: " +
                      string.Join(", ", g.Select(x => $"{Unit(players + x.i)} Lv{x.r.Level} ({x.r.X},{x.r.Y}){(x.r.Type == 2 ? " 합류" : "")}")));
        if (ev == null)
        {
            lines.Add("");
            lines.Add("(대본 SceneEvents.json 이 없는 빌드 — 원작 EXE 에서 tools/scene-events 로 만들어 넣으면 보입니다)");
            return lines;
        }

        lines.Add("");
        lines.Add("■ 시작 연출");
        Ops(ev.Start(scene.Index) ?? [], 1);

        lines.Add("");
        lines.Add("■ 사건 줄 (차례 · 때 → 사건)");
        for (int k = 0; k < scene.EventLines.Count; k++)
        {
            var l = scene.EventLines[k];
            if (l.Event == 255 || l.Turn == 0 && l.Event == 0 && l.When == 0) continue;   // 쓰지 않는 줄
            string turn = l.Turn == 255 ? "꺼짐(다른 사건이 켬)" : $"{l.Turn}차례";
            lines.Add($"  줄 {k}: {turn} · {(l.When < Whens.Length ? Whens[l.When] : $"때 {l.When}")} → 사건 {l.Event}");
            Ops(ev.Event(l.Event), 2);
        }

        var marks = new Dictionary<int, List<string>>();
        for (int y = 0; y < scene.Height; y++)
            for (int x = 0; x < scene.Width; x++)
                if (scene.Mark(x, y) is var m and > 0 && (scene.TileFlags(x, y) & 0x60) == 0)
                    (marks.TryGetValue(m, out var list) ? list : marks[m] = new()).Add($"({x},{y})");
        if (Enumerable.Range(0, scene.GroundEvents.Count).Any(k => marks.ContainsKey(k + 1)))
        {
            lines.Add("");
            lines.Add("■ 땅 칸 사건 (밟으면)");
            for (int k = 0; k < scene.GroundEvents.Count; k++)
            {
                var (e, side) = scene.GroundEvents[k];
                if (!marks.TryGetValue(k + 1, out var cells)) continue;
                lines.Add($"  표지 {k + 1} {string.Join(" ", cells.Take(10))}{(cells.Count > 10 ? " …" : "")} — {(side == 0 ? "적" : "우리 쪽")}이 밟으면 사건 {e}");
                Ops(ev.Event(e), 2);
            }
        }

        var chestMarks = new HashSet<int>();   // 지도에 실제로 있는 상자(속성 0x20 · 0x40 칸의 표지 = 상자 번호)
        for (int y = 0; y < scene.Height; y++)
            for (int x = 0; x < scene.Width; x++)
                if ((scene.TileFlags(x, y) & 0x60) != 0) chestMarks.Add(scene.Mark(x, y));
        var chestEvents = scene.Chests.Select((c, k) => (c, k)).Where(x => x.c.Kind >= 2 && chestMarks.Contains(x.k)).ToList();
        if (chestEvents.Count > 0)
        {
            lines.Add("");
            lines.Add("■ 사건 상자 (열면)");
            foreach (var (c, k) in chestEvents)
            {
                lines.Add($"  상자 {k} → 사건 {c.Value}");
                Ops(ev.Event(c.Value), 2);
            }
        }

        lines.Add("");
        lines.Add("■ 승리 뒤 연출");
        Ops(ev.Victory(scene.Index) ?? [], 1);
        return lines;

        void Ops(JsonElement[] ops, int depth)
        {
            string pad = new(' ', depth * 3);
            if (ops.Length == 0) { lines.Add(pad + "(없음)"); return; }
            foreach (var op in ops)
            {
                if (op.ValueKind != JsonValueKind.Array || op.GetArrayLength() == 0) continue;
                string name = op[0].GetString() ?? "";
                int A(int i) => op.GetArrayLength() > i && op[i].ValueKind == JsonValueKind.Number ? op[i].GetInt32() : 0;
                string S(int i) => op.GetArrayLength() > i && op[i].ValueKind == JsonValueKind.String ? op[i].GetString() ?? "" : "";
                if (name == "if")
                {
                    lines.Add($"{pad}만약 {Condition(S(1))} 이면");
                    if (op.GetArrayLength() > 2) Ops(op[2].EnumerateArray().ToArray(), depth + 1);
                    if (op.GetArrayLength() > 3 && op[3].GetArrayLength() > 0)
                    {
                        lines.Add($"{pad}아니면");
                        Ops(op[3].EnumerateArray().ToArray(), depth + 1);
                    }
                    continue;
                }
                lines.Add(pad + name switch
                {
                    "spawn" => $"등장 차례 {A(1)} 무리가 나타남: " + Group(A(1)),
                    "spawnfx" => $"등장 차례 {A(1)} 무리가 빛과 함께 나타남: " + Group(A(1)),
                    "talk" => $"대사 묶음 {A(1)}",
                    "script" => $"이동 대본 {A(1)} (사람들이 걸음)",
                    "camera" => $"화면을 ({A(1)},{A(2)}) 로",
                    "join" => $"{CharacterNames.Of(A(1))} 동료로 합류",
                    "remove" => $"{Unit(A(1))} 사라짐",
                    "ally" => $"{Unit(A(1))} 컴퓨터 아군이 됨",
                    "setunit" when A(2) == 5 => $"{Unit(A(1))} {((A(3) & 1) != 0 ? "숨김" : "다시 세움")}",
                    "setunit" => $"{Unit(A(1))} 칸 +{A(2)} = {A(3)}",
                    "arm" => $"사건 줄 {A(1)} 을 {(op.GetArrayLength() > 2 ? A(2) : 1)}차례 뒤로 켬",
                    "flag" => $"깃발 {A(1)} 켬",
                    "reward" => $"보상 {A(1)}: {(A(1) == 0 ? $"아이템 {A(2)}" : $"{A(2)}")}",
                    "win" => "승리",
                    "lose" => "패배",
                    "stack" => "(원작 스택 정리)",
                    "dark" => "화면 어둡게",
                    "music" => $"음악 {A(1)}",
                    "raw" => $"원작 코드: {S(1)}",
                    _ => op.GetRawText(),
                });
            }
        }

        string Group(int phase)
        {
            var who = scene.Roster.Select((r, i) => (r, i)).Where(x => x.r.Phase == phase).Select(x => Unit(players + x.i)).ToList();
            return who.Count == 0 ? "(없음)" : string.Join(", ", who.Take(8)) + (who.Count > 8 ? $" 외 {who.Count - 8}" : "");
        }

        string Condition(string c) => System.Text.RegularExpressions.Regex.Replace(c, @"\b(dead|alive|anyalive|anydead|inparty|inroster|hasitem|flag|turn|nitems|deadcount)\b",
            m => m.Value switch
            {
                "dead" => "쓰러짐", "alive" => "살아 있음", "anyalive" => "하나라도 살아 있음", "anydead" => "하나라도 쓰러짐",
                "inparty" => "동료에", "inroster" => "싸움터에", "hasitem" => "아이템 가짐", "flag" => "깃발", "turn" => "차례",
                "nitems" => "아이템 수", "deadcount" => "쓰러진 수", _ => m.Value,
            }).Replace("arg0", "밟은/연 사람").Replace("&&", " 그리고 ").Replace("||", " 또는 ");
    }
}
