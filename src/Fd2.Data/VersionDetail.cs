using System.Collections;

namespace Fd2.Data;

/// <summary>
/// 두 버전의 한 파일이 무엇이 다른지 글로 — 편집기 「버전 비교」의 상세 보기.
/// FIGANI 는 캐릭터 · 모션마다 컷의 자리 · 크기 · 그림 · 소리 · 박자, FD2.EXE 는 수치 표(동료 · 성장 · 몬스터 · 물건 · 마법 · 직업 · 지형 · 상점)를 칸마다,
/// FDFIELD 는 장면마다 지도 칸 · 유닛 · 사건 줄 · 상자, FDTXT 0 은 이름 표 줄, 그 밖은 항목 · 바이트 수.
/// </summary>
public static class VersionDetail
{
    public static List<string> Describe(string baseDir, string otherDir, string file)
    {
        string a = Path.Combine(baseDir, file), b = Path.Combine(otherDir, file);
        if (!File.Exists(a) || !File.Exists(b)) return [File.Exists(a) ? "기준 버전에만 있습니다." : "비교 버전에만 있습니다."];
        try
        {
            return file.ToUpperInvariant() switch
            {
                "FIGANI.DAT" => Figani(a, b),
                "FD2.EXE" => Exe(baseDir, otherDir, a, b),
                "FDFIELD.DAT" => Field(a, b),
                "FDTXT.DAT" => Text(a, b),
                _ => Generic(a, b),
            };
        }
        catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            return [$"읽다가 멈췄습니다: {e.Message}", .. Generic(a, b)];
        }
    }

    // ── FIGANI: 캐릭터 c 의 모션 3c + 0 서기 · 1 공격 · 2 마법 ───────────────

    private static readonly string[] MotionKinds = ["서기", "공격", "마법 · 특수"];

    private static List<string> Figani(string a, string b)
    {
        var lines = new List<string>();
        var pal = Enumerable.Range(0, 256).Select(i => 0xFF000000u | (uint)(i * 0x010101)).ToArray();   // 그림 비교용 — 색 번호 그대로
        foreach (int k in GameVersions.ChangedEntries(a, b))
        {
            int c = k / 3;
            lines.Add($"[{k}] {CharacterNames.Of(c)} — {MotionKinds[k % 3]} 모션");
            var ma = Parse(a, k, pal);
            var mb = Parse(b, k, pal);
            if (ma == null || mb == null) { lines.Add(ma == null ? "    기준 버전에는 없음 → 새로 생김" : "    비교 버전에서 비어 있음(지워짐)"); continue; }
            if (ma.SoundBank != mb.SoundBank) lines.Add($"    소리 묶음 {ma.SoundBank} → {mb.SoundBank}");
            if (ma.Frames.Count != mb.Frames.Count) lines.Add($"    컷 수 {ma.Frames.Count} → {mb.Frames.Count}");
            int ta = ma.Frames.Sum(f => f.Hold), tb = mb.Frames.Sum(f => f.Hold);
            if (ta != tb) lines.Add($"    길이 {ta}틱 → {tb}틱 (1틱 ≈ 0.055초)");
            for (int i = 0; i < Math.Max(ma.Frames.Count, mb.Frames.Count); i++)
            {
                if (i >= ma.Frames.Count) { lines.Add($"    컷 {i + 1}: 새 컷 {Frame(mb.Frames[i])}"); continue; }
                if (i >= mb.Frames.Count) { lines.Add($"    컷 {i + 1}: 없어짐"); continue; }
                var (fa, fb) = (ma.Frames[i], mb.Frames[i]);
                var d = new List<string>();
                if ((fa.X, fa.Y) != (fb.X, fb.Y)) d.Add($"자리 ({fa.X},{fa.Y}) → ({fb.X},{fb.Y})");
                if ((fa.Width, fa.Height) != (fb.Width, fb.Height)) d.Add($"크기 {fa.Width}×{fa.Height} → {fb.Width}×{fb.Height}");
                else if (!fa.Pixels.AsSpan().SequenceEqual(fb.Pixels))
                    d.Add($"그림 점 {fa.Pixels.Where((p, j) => p != fb.Pixels[j]).Count()}개 바뀜");
                if (fa.Hold != fb.Hold) d.Add($"붙드는 틱 {fa.Hold} → {fb.Hold}");
                if (fa.Hit != fb.Hit) d.Add(fb.Hit != 0 ? "맞는 컷이 됨" : "맞는 컷이 아니게 됨");
                if (fa.Sound != fb.Sound) d.Add($"소리 {fa.Sound} → {fb.Sound}");
                if (d.Count > 0) lines.Add($"    컷 {i + 1}: {string.Join(", ", d)}");
            }
        }
        return lines.Count > 0 ? lines : Generic(a, b);

        static string Frame(MotionFrame f) => $"자리 ({f.X},{f.Y}) 크기 {f.Width}×{f.Height} 틱 {f.Hold}{(f.Hit != 0 ? " 맞는 컷" : "")}{(f.Sound != 0 ? $" 소리 {f.Sound}" : "")}";
    }

    private static Motion? Parse(string path, int entry, uint[] pal)
    {
        string dir = Path.GetDirectoryName(path)!;
        try { return entry < Dat.Count(path) ? Motion.LoadFigani(dir, entry / 3, entry % 3, pal) : null; }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    // ── FD2.EXE: 수치 표 ─────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["Race"] = "종족", ["Class"] = "직업", ["Level"] = "레벨", ["Hp"] = "HP", ["Mp"] = "MP", ["Move"] = "이동", ["Spells"] = "마법",
        ["Weapon"] = "무기", ["Armor"] = "방어구", ["Bag"] = "가방", ["StrAdd"] = "역량+", ["DefAdd"] = "내력+", ["SpdAdd"] = "속도+",
        ["StrMin"] = "역량 최소", ["StrMax"] = "역량 최대", ["DefMin"] = "내력 최소", ["DefMax"] = "내력 최대", ["SpdMin"] = "속도 최소", ["SpdMax"] = "속도 최대",
        ["HpMin"] = "HP 최소", ["HpMax"] = "HP 최대", ["MpMin"] = "MP 최소", ["MpMax"] = "MP 최대", ["SpellList"] = "마법 표",
        ["Str"] = "역량", ["Def"] = "방어", ["Spd"] = "속도", ["Exp"] = "경험",
        ["Kind"] = "종류", ["Atk"] = "공격", ["Hit"] = "명중", ["Evade"] = "회피", ["Special"] = "특수", ["SpecialValue"] = "특수 값",
        ["RangeMin"] = "사거리 최소", ["RangeMax"] = "사거리 최대", ["Use"] = "쓰임", ["UseValue"] = "쓰임 값", ["UseRange"] = "쓰는 거리",
        ["UseSide"] = "쓰는 편", ["UseArea"] = "쓰는 범위", ["Price"] = "값",
        ["Power"] = "세기", ["Range"] = "사거리", ["Area"] = "범위", ["Side"] = "대상 편",
        ["EquipKinds"] = "장비 종류", ["Crit"] = "필살 %", ["MagicMul"] = "받는 마법 ×/10", ["Revive"] = "부활값", ["MoveCost"] = "지형 이동비",
        ["Town"] = "마을", ["Weapons"] = "무기 상점", ["Tools"] = "도구 상점", ["Secret"] = "비밀 상점", ["SecretAt"] = "비밀상점 건물", ["SecretKey"] = "비밀상점 키",
    };

    private static List<string> Exe(string baseDir, string otherDir, string a, string b)
    {
        var lines = new List<string>();
        var (ta, tb) = (GameTables.Load(baseDir), GameTables.Load(otherDir));
        if (ta == null || tb == null) return ["EXE 크기가 원본과 달라 수치 표를 읽지 못했습니다.", .. Generic(a, b)];
        Table(lines, "동료", ta.Heroes, tb.Heroes, i => CharacterNames.Of(i));
        Table(lines, "레벨업 성장", ta.Growths, tb.Growths, i => CharacterNames.Of(i));
        Table(lines, "몬스터", ta.Monsters, tb.Monsters, i => $"몬스터 꼴 {i}");
        Table(lines, "아이템", ta.Items, tb.Items, i => $"아이템 {i}");
        Table(lines, "마법", ta.Spells, tb.Spells, i => SpellNames.Of(i));
        Table(lines, "직업", ta.Classes, tb.Classes, i => $"직업 {i}");
        Table(lines, "상점", ta.Shops, tb.Shops, i => $"상점 {i}");
        for (int g = 0; g < Math.Min(ta.TerrainAttack.Count, tb.TerrainAttack.Count); g++)
        {
            if (ta.TerrainAttack[g] != tb.TerrainAttack[g]) lines.Add($"지형 {g}: 공격 % {ta.TerrainAttack[g]} → {tb.TerrainAttack[g]}");
            if (ta.TerrainDefense[g] != tb.TerrainDefense[g]) lines.Add($"지형 {g}: 방어 % {ta.TerrainDefense[g]} → {tb.TerrainDefense[g]}");
        }
        int tables = lines.Count;
        var (da, db) = (File.ReadAllBytes(a), File.ReadAllBytes(b));
        int bytes = 0;
        for (int i = 0; i < Math.Min(da.Length, db.Length); i++) if (da[i] != db[i]) bytes++;
        lines.Add(tables == 0 ? $"수치 표는 같습니다 — 바뀐 바이트 {bytes}개는 표 밖(코드 · 글 · 그 밖의 표)입니다." : $"(바뀐 바이트 모두 {bytes}개 — 표 밖 것도 들어 있을 수 있습니다)");
        return lines;
    }

    private static void Table<T>(List<string> lines, string title, IReadOnlyList<T> xa, IReadOnlyList<T> xb, Func<int, string> name) where T : notnull
    {
        for (int i = 0; i < Math.Min(xa.Count, xb.Count); i++)
        {
            var d = new List<string>();
            foreach (var p in typeof(T).GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract"))
            {
                string va = Show(p.GetValue(xa[i])), vb = Show(p.GetValue(xb[i]));
                if (va != vb) d.Add($"{FieldNames.GetValueOrDefault(p.Name, p.Name)} {va} → {vb}");
            }
            if (d.Count > 0) lines.Add($"{title} {i} {name(i)}: {string.Join(", ", d)}");
        }
        if (xa.Count != xb.Count) lines.Add($"{title} 수 {xa.Count} → {xb.Count}");

        static string Show(object? v) => v is IEnumerable e and not string ? "[" + string.Join(" ", e.Cast<object>()) + "]" : $"{v}";
    }

    // ── FDFIELD: 장면 n = 3n 지도 · 3n+1 정보(유닛 표 · 사건 · 상자) · 3n+2 좌표 ─────

    private static List<string> Field(string a, string b)
    {
        var lines = new List<string>();
        foreach (int k in GameVersions.ChangedEntries(a, b))
        {
            int scene = k / 3;
            var (ea, eb) = (Entry(a, k), Entry(b, k));
            string head = $"[{k}] {scene + 1}장(장면 {scene}) ";
            if (ea == null || eb == null) { lines.Add(head + (ea == null ? "새 항목" : "항목 없어짐")); continue; }
            switch (k % 3)
            {
                case 0:
                {
                    int w = Dat.U16(ea, 0), h = Dat.U16(ea, 2);
                    if ((w, h) != (Dat.U16(eb, 0), Dat.U16(eb, 2))) { lines.Add(head + $"지도 크기 {w}×{h} → {Dat.U16(eb, 0)}×{Dat.U16(eb, 2)}"); break; }
                    int tiles = 0, marks = 0;
                    var first = new List<string>();
                    for (int i = 0; i < w * h && 7 + 4 * i < Math.Min(ea.Length, eb.Length); i++)
                    {
                        bool t = (Dat.U16(ea, 4 + 4 * i) & 0x3FF) != (Dat.U16(eb, 4 + 4 * i) & 0x3FF);
                        bool m = (ea[6 + 4 * i] & 0x1F) != (eb[6 + 4 * i] & 0x1F);
                        if (t) tiles++;
                        if (m) marks++;
                        if ((t || m) && first.Count < 8) first.Add($"({i % w},{i / w})");
                    }
                    lines.Add(head + $"지도: 타일 {tiles}칸 · 표지(상자 · 사건 번호) {marks}칸 바뀜" + (first.Count > 0 ? " — " + string.Join(" ", first) + (tiles + marks > first.Count ? " …" : "") : ""));
                    break;
                }
                case 1:
                {
                    var d = new List<string>();
                    if (ea[0] != eb[0]) d.Add($"타일 묶음 {ea[0]} → {eb[0]}");
                    if (ea[1] != eb[1]) d.Add($"우리 자리 {ea[1]} → {eb[1]}");
                    if (ea[2] != eb[2]) d.Add($"그 밖 유닛 수 {ea[2]} → {eb[2]}");
                    for (int e = 0; e < 16; e++)
                        if (!Same(ea, eb, 3 + 3 * e, 3)) d.Add($"사건 줄 {e} ({ea[3 + 3 * e]},{ea[4 + 3 * e]},{ea[5 + 3 * e]}) → ({eb[3 + 3 * e]},{eb[4 + 3 * e]},{eb[5 + 3 * e]})");
                    for (int e = 0; e < 16; e++)
                        if (!Same(ea, eb, 83 + 3 * e, 3)) d.Add($"상자 {e} {Chest(ea, e)} → {Chest(eb, e)}");
                    for (int u = 0; u < Math.Max(ea[2], eb[2]); u++)
                    {
                        int o = 131 + 26 * u;
                        if (o + 26 > ea.Length || o + 26 > eb.Length) { d.Add($"유닛 {u} 생기거나 없어짐"); continue; }
                        if (Same(ea, eb, o, 26)) continue;
                        var f = new List<string>();
                        if (ea[o + 1] != eb[o + 1]) f.Add($"그림 {CharacterNames.Of(ea[o + 1])} → {CharacterNames.Of(eb[o + 1])}");
                        if (ea[o + 4] != eb[o + 4]) f.Add($"레벨 {ea[o + 4]} → {eb[o + 4]}");
                        if (ea[o + 21] != eb[o + 21]) f.Add($"등장 차례 {ea[o + 21]} → {eb[o + 21]}");
                        if (f.Count == 0) f.Add("그 밖의 칸 " + string.Join(" ", Enumerable.Range(0, 26).Where(j => ea[o + j] != eb[o + j]).Select(j => $"+{j}:{ea[o + j]}→{eb[o + j]}")));
                        d.Add($"유닛 {u} ({CharacterNames.Of(ea[o + 1])}) {string.Join(", ", f)}");
                    }
                    lines.Add(head + "정보: " + (d.Count > 0 ? string.Join("; ", d) : "바이트만 바뀜"));
                    break;
                }
                default:
                    lines.Add(head + $"유닛 자리 바이트 {Diff(ea, eb)}곳 바뀜");
                    break;
            }
        }
        return lines.Count > 0 ? lines : Generic(a, b);

        static string Chest(byte[] d, int e) => d[83 + 3 * e] switch { 0 => $"아이템 {Dat.U16(d, 84 + 3 * e)}", 1 => $"돈 {Dat.U16(d, 84 + 3 * e)}", var s => $"사건 {s}" };
    }

    // ── FDTXT: 0 이름 표(줄마다 원본 글꼴 번호), 그 밖은 장면 대사 묶음 ────────────

    private static List<string> Text(string a, string b)
    {
        var lines = new List<string>();
        foreach (int k in GameVersions.ChangedEntries(a, b))
        {
            if (k != 0) { lines.Add($"[{k}] 대사 묶음 — 바이트 {Diff(Entry(a, k) ?? [], Entry(b, k) ?? [])}곳 바뀜"); continue; }
            var (na, nb) = (new NameTable(Path.GetDirectoryName(a)!), new NameTable(Path.GetDirectoryName(b)!));
            var changed = Enumerable.Range(0, Math.Max(na.Count, nb.Count)).Where(i => !na.Line(i).SequenceEqual(nb.Line(i))).ToList();
            lines.Add($"[0] 이름 표 — {changed.Count}줄 바뀜");
            foreach (int i in changed.Take(200)) lines.Add($"    줄 {i}: {LineMeaning(i)} (글자 {na.Line(i).Length} → {nb.Line(i).Length})");
        }
        return lines.Count > 0 ? lines : Generic(a, b);

        static string LineMeaning(int i) => i switch
        {
            >= 1 and < 140 => $"캐릭터 {i - 1} 이름({CharacterNames.Of(i - 1)})",
            >= 140 and < 150 => $"종족 {i - 140}",
            >= 150 and < 181 => $"직업 {i - 150}",
            >= 181 and < 441 => $"아이템 {i - 181} 이름",
            >= 441 and < 597 => $"마법 {i - 441}",
            >= 597 and < 657 => $"{(i - 597) / 2 + 1}장 {((i - 597) % 2 == 0 ? "승리" : "패배")} 조건",
            _ => "그 밖의 글",
        };
    }

    // ── 그 밖 ───────────────────────────────────────────────────────────────

    private static List<string> Generic(string a, string b)
    {
        var entries = GameVersions.ChangedEntries(a, b);
        if (entries.Count > 0)
            return entries.Select(k => $"[{k}] 바이트 {Diff(Entry(a, k) ?? [], Entry(b, k) ?? [])}곳 바뀜 (크기 {Entry(a, k)?.Length ?? 0} → {Entry(b, k)?.Length ?? 0})").ToList();
        var (da, db) = (File.ReadAllBytes(a), File.ReadAllBytes(b));
        return [$"크기 {da.Length} → {db.Length} 바이트, 다른 바이트 {Diff(da, db)}개"];
    }

    private static byte[]? Entry(string path, int k)
    {
        try { return k < Dat.Count(path) ? Dat.Load(path, k) : null; }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    private static bool Same(byte[] a, byte[] b, int at, int n) =>
        at + n <= a.Length && at + n <= b.Length && a.AsSpan(at, n).SequenceEqual(b.AsSpan(at, n));

    private static int Diff(byte[] a, byte[] b)
    {
        int n = Math.Abs(a.Length - b.Length);
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) n++;
        return n;
    }
}
