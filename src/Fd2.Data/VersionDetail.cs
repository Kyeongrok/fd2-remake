using System.Collections;

namespace Fd2.Data;

/// <summary>
/// 두 버전의 한 파일이 무엇이 다른지 — 편집기 「버전 비교」의 상세 보기. 바뀐 곳마다 한 줄(<see cref="Change"/>)로, 기준 값과 비교 값을 따로 둔다.
/// 편집기는 <see cref="Change.Tab"/> 으로 탭을 나누고 왼쪽에 기준 버전 값, 오른쪽에 비교 버전 값을 보인다.
/// FIGANI 는 캐릭터 · 모션마다 컷의 자리 · 크기 · 그림 · 소리 · 박자, FD2.EXE 는 수치 표(동료 · 성장 · 몬스터 · 물건 · 마법 · 직업 · 지형 · 상점)를 칸마다,
/// FDFIELD 는 장면마다 지도 칸 · 사건 줄 · 상자 · 유닛 · 유닛 자리, FDTXT 0 은 이름 표 줄, 그 밖은 항목 · 바이트 수.
/// </summary>
public static class VersionDetail
{
    /// <summary>바뀐 곳 하나 — 탭(갈래), 어디(장 · 사람 · 컷 …), 무엇(칸 이름), 기준 버전 값, 비교 버전 값.</summary>
    public sealed record Change(string Tab, string Where, string What, string Base, string Other);

    public static List<Change> Describe(string baseDir, string otherDir, string file)
    {
        string a = Path.Combine(baseDir, file), b = Path.Combine(otherDir, file);
        if (!File.Exists(a) || !File.Exists(b)) return [new("파일", file, "있는 곳", File.Exists(a) ? "있음" : "없음", File.Exists(b) ? "있음" : "없음")];
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
            return [new("항목", file, "읽다가 멈춤", e.Message, ""), .. Generic(a, b)];
        }
    }

    // ── FIGANI: 캐릭터 c 의 모션 3c + 0 서기 · 1 공격 · 2 마법 ───────────────

    private static readonly string[] MotionKinds = ["서기", "공격", "마법 · 특수"];

    private static List<Change> Figani(string a, string b)
    {
        var list = new List<Change>();
        var pal = Enumerable.Range(0, 256).Select(i => 0xFF000000u | (uint)(i * 0x010101)).ToArray();   // 그림 비교용 — 색 번호 그대로
        foreach (int k in GameVersions.ChangedEntries(a, b))
        {
            string tab = MotionKinds[k % 3] + " 모션", who = $"[{k}] {CharacterNames.Of(k / 3)}";
            void Add(string where, string what, object? va, object? vb) => list.Add(new(tab, where, what, $"{va}", $"{vb}"));
            var ma = Parse(a, k, pal);
            var mb = Parse(b, k, pal);
            if (ma == null || mb == null) { Add(who, "모션", ma == null ? "없음" : $"{ma.Frames.Count}컷", mb == null ? "없음(지워짐)" : $"{mb.Frames.Count}컷"); continue; }
            if (ma.SoundBank != mb.SoundBank) Add(who, "소리 묶음", ma.SoundBank, mb.SoundBank);
            if (ma.Frames.Count != mb.Frames.Count) Add(who, "컷 수", ma.Frames.Count, mb.Frames.Count);
            int ta = ma.Frames.Sum(f => f.Hold), tb = mb.Frames.Sum(f => f.Hold);
            if (ta != tb) Add(who, "길이(1틱 ≈ 0.055초)", $"{ta}틱", $"{tb}틱");
            for (int i = 0; i < Math.Max(ma.Frames.Count, mb.Frames.Count); i++)
            {
                string cut = $"{who} 컷 {i + 1}";
                if (i >= ma.Frames.Count) { Add(cut, "컷", "없음", Frame(mb.Frames[i])); continue; }
                if (i >= mb.Frames.Count) { Add(cut, "컷", Frame(ma.Frames[i]), "없음"); continue; }
                var (fa, fb) = (ma.Frames[i], mb.Frames[i]);
                if ((fa.X, fa.Y) != (fb.X, fb.Y)) Add(cut, "자리", $"({fa.X},{fa.Y})", $"({fb.X},{fb.Y})");
                if ((fa.Width, fa.Height) != (fb.Width, fb.Height)) Add(cut, "크기", $"{fa.Width}×{fa.Height}", $"{fb.Width}×{fb.Height}");
                else if (!fa.Pixels.AsSpan().SequenceEqual(fb.Pixels)) Add(cut, "그림", "", $"점 {fa.Pixels.Where((p, j) => p != fb.Pixels[j]).Count()}개 바뀜");
                if (fa.Hold != fb.Hold) Add(cut, "붙드는 틱", fa.Hold, fb.Hold);
                if (fa.Hit != fb.Hit) Add(cut, "맞는 컷", fa.Hit != 0 ? "예" : "아니오", fb.Hit != 0 ? "예" : "아니오");
                if (fa.Sound != fb.Sound) Add(cut, "소리", fa.Sound, fb.Sound);
            }
        }
        return list.Count > 0 ? list : Generic(a, b);

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
        ["Town"] = "마을 그림", ["Weapons"] = "무기점", ["Tools"] = "도구점", ["Secret"] = "비밀상점", ["SecretAt"] = "비밀상점 건물", ["SecretKey"] = "비밀상점 키",
    };

    private static List<Change> Exe(string baseDir, string otherDir, string a, string b)
    {
        var list = new List<Change>();
        var (ta, tb) = (GameTables.Load(baseDir), GameTables.Load(otherDir));
        if (ta == null || tb == null) return [new("수치 표", "FD2.EXE", "크기", "원본과 다름 — 표를 못 읽음", ""), .. Generic(a, b)];
        Table(list, "동료", ta.Heroes, tb.Heroes, i => $"{i} {CharacterNames.Of(i)}");
        Table(list, "레벨업 성장", ta.Growths, tb.Growths, i => $"{i} {CharacterNames.Of(i)}");
        Table(list, "몬스터", ta.Monsters, tb.Monsters, i => CharacterNames.Of(GameTables.MonsterBase + i));
        Table(list, "아이템", ta.Items, tb.Items, i => Named(GameNames.Item(i), i));
        Table(list, "마법", ta.Spells, tb.Spells, i => Named(SpellNames.Of(i), i));
        Table(list, "직업", ta.Classes, tb.Classes, i => Named(GameNames.Class(i), i));
        Table(list, "상점", ta.Shops, tb.Shops, i => $"{i + 2}장 앞 마을");
        for (int g = 0; g < Math.Min(ta.TerrainAttack.Count, tb.TerrainAttack.Count); g++)
        {
            if (ta.TerrainAttack[g] != tb.TerrainAttack[g]) list.Add(new("지형", $"지형 {g}", "공격 %", $"{ta.TerrainAttack[g]}", $"{tb.TerrainAttack[g]}"));
            if (ta.TerrainDefense[g] != tb.TerrainDefense[g]) list.Add(new("지형", $"지형 {g}", "방어 %", $"{ta.TerrainDefense[g]}", $"{tb.TerrainDefense[g]}"));
        }
        var (da, db) = (File.ReadAllBytes(a), File.ReadAllBytes(b));
        int bytes = 0;
        for (int i = 0; i < Math.Min(da.Length, db.Length); i++) if (da[i] != db[i]) bytes++;
        list.Add(new("그 밖", "FD2.EXE", "바뀐 바이트", "", list.Count == 0 ? $"{bytes}개 — 수치 표는 같고 표 밖(코드 · 글 · 그 밖의 표)" : $"모두 {bytes}개(표 밖 것도 들어 있을 수 있음)"));
        return list;
    }

    private static string Named(string name, int i) => $"{(name == "" ? "(빈 이름)" : name)}({i})";

    private static void Table<T>(List<Change> list, string title, IReadOnlyList<T> xa, IReadOnlyList<T> xb, Func<int, string> name) where T : notnull
    {
        for (int i = 0; i < Math.Min(xa.Count, xb.Count); i++)
            foreach (var p in typeof(T).GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract"))
            {
                object? va = p.GetValue(xa[i]), vb = p.GetValue(xb[i]);
                if (Show(va) != Show(vb)) list.Add(new(title, name(i), FieldNames.GetValueOrDefault(p.Name, p.Name), Pretty(p.Name, va), Pretty(p.Name, vb)));
            }
        if (xa.Count != xb.Count) list.Add(new(title, title, "줄 수", $"{xa.Count}", $"{xb.Count}"));

        static string Show(object? v) => v is IEnumerable e and not string ? "[" + string.Join(" ", e.Cast<object>()) + "]" : $"{v}";

        // 종족 · 직업 · 장비 · 상점 칸은 게임 이름으로
        static string Pretty(string field, object? v) => (field, v) switch
        {
            ("Race", int r) => Named(GameNames.Race(r), r),
            ("Class", int c) => Named(GameNames.Class(c), c),
            ("Weapon" or "Armor", int it) => Named(GameNames.Item(it), it),
            ("Weapons" or "Tools" or "Secret", IEnumerable<int> items) => string.Join(", ", items.Select(it => Named(GameNames.Item(it), it))),
            _ => Show(v),
        };
    }

    // ── FDFIELD: 장면 n = 3n 지도 · 3n+1 정보(유닛 표 · 사건 · 상자) · 3n+2 좌표 ─────

    /// <summary>유닛 표 26바이트 안의 칸 이름(StatTables.StageUnits 와 같은 자리).</summary>
    private static string UnitByte(int j) => j switch
    {
        0 => "편", 1 => "그림", 4 => "레벨", 5 => "무기", 6 => "방어구", >= 7 and <= 12 => $"아이템 {j - 6}", >= 13 and <= 16 => $"마법 비트 +{j}",
        17 => "AI", 18 => "AI x", 19 => "AI y", 21 => "등장 차례", 22 => "떨굼 종류", _ => $"+{j}",
    };

    private static string UnitValue(int j, int v) => j switch
    {
        1 => $"{CharacterNames.Of(v)}({v})",
        >= 5 and <= 12 => v == 255 ? "없음(255)" : Named(GameNames.Item(v), v),
        _ => $"{v}",
    };

    private static List<Change> Field(string a, string b)
    {
        var list = new List<Change>();
        foreach (int k in GameVersions.ChangedEntries(a, b))
        {
            int scene = k / 3;
            var (ea, eb) = (Entry(a, k), Entry(b, k));
            string head = $"{scene + 1}장";
            if (ea == null || eb == null) { list.Add(new("장면 정보", head, $"항목 [{k}]", ea == null ? "없음" : "있음", eb == null ? "없음" : "있음")); continue; }
            switch (k % 3)
            {
                case 0:
                {
                    int w = Dat.U16(ea, 0), h = Dat.U16(ea, 2);
                    if ((w, h) != (Dat.U16(eb, 0), Dat.U16(eb, 2))) { list.Add(new("지도", head, "지도 크기", $"{w}×{h}", $"{Dat.U16(eb, 0)}×{Dat.U16(eb, 2)}")); break; }
                    for (int i = 0; i < w * h && 7 + 4 * i < Math.Min(ea.Length, eb.Length); i++)
                    {
                        int tA = Dat.U16(ea, 4 + 4 * i) & 0x3FF, tB = Dat.U16(eb, 4 + 4 * i) & 0x3FF;
                        int mA = ea[6 + 4 * i] & 0x1F, mB = eb[6 + 4 * i] & 0x1F;
                        if (tA != tB) list.Add(new("지도", $"{head} ({i % w},{i / w})", "타일", $"{tA}", $"{tB}"));
                        if (mA != mB) list.Add(new("지도", $"{head} ({i % w},{i / w})", "표지(상자 · 사건 번호)", $"{mA}", $"{mB}"));
                    }
                    break;
                }
                case 1:
                {
                    int before = list.Count;
                    if (ea[0] != eb[0]) list.Add(new("장면 정보", head, "타일 묶음", $"{ea[0]}", $"{eb[0]}"));
                    if (ea[1] != eb[1]) list.Add(new("장면 정보", head, "우리 편 자리 수", $"{ea[1]}", $"{eb[1]}"));
                    if (ea[2] != eb[2]) list.Add(new("장면 정보", head, "그 밖 유닛 수", $"{ea[2]}", $"{eb[2]}"));
                    for (int e = 0; e < 16; e++)
                        if (!Same(ea, eb, 3 + 3 * e, 3))
                            list.Add(new("사건 줄", $"{head} 사건 줄 {e}", "차례 · 사건 · 때", Triple(ea, 3 + 3 * e), Triple(eb, 3 + 3 * e)));
                    for (int e = 0; e < 16; e++)
                        if (!Same(ea, eb, 83 + 3 * e, 3)) list.Add(new("상자", $"{head} 상자 {e}", "든 것", Chest(ea, e), Chest(eb, e)));
                    for (int u = 0; u < Math.Max(ea[2], eb[2]); u++)
                    {
                        int o = 131 + 26 * u;
                        var src = o + 26 <= ea.Length ? ea : eb;
                        string who = $"{head} 유닛 {u} ({CharacterNames.Of(o + 1 < src.Length ? src[o + 1] : 0)})";
                        if (o + 26 > ea.Length || o + 26 > eb.Length) { list.Add(new("유닛", who, "유닛", o + 26 > ea.Length ? "없음" : "있음", o + 26 > eb.Length ? "없음" : "있음")); continue; }
                        if (Same(ea, eb, o, 26)) continue;
                        for (int j = 0; j < 26; j++)
                        {
                            if (j == 23) { if (!Same(ea, eb, o + 23, 2)) list.Add(new("유닛", who, "떨굼 값", $"{Dat.U16(ea, o + 23)}", $"{Dat.U16(eb, o + 23)}")); continue; }
                            if (j == 24 || ea[o + j] == eb[o + j]) continue;
                            list.Add(new("유닛", who, UnitByte(j), UnitValue(j, ea[o + j]), UnitValue(j, eb[o + j])));
                        }
                    }
                    if (list.Count == before) list.Add(new("장면 정보", head, "바이트", "", $"{Diff(ea, eb)}곳 바뀜(뜻 모르는 자리)"));
                    break;
                }
                default:
                {
                    // 좌표: 2 부터 6바이트씩(+0 x, +2 y) — 그 밖 유닛 다음에 우리 편 자리
                    var info = Entry(a, k - 1) ?? [];
                    int others = info.Length > 2 ? info[2] : 0;
                    int n = (Math.Max(ea.Length, eb.Length) - 2) / 6;
                    for (int i = 0; i < n; i++)
                    {
                        int p = 2 + 6 * i;
                        string pa = p + 4 <= ea.Length ? $"({Dat.U16(ea, p)},{Dat.U16(ea, p + 2)})" : "없음";
                        string pb = p + 4 <= eb.Length ? $"({Dat.U16(eb, p)},{Dat.U16(eb, p + 2)})" : "없음";
                        int icon = 131 + 26 * i + 1 < info.Length ? info[131 + 26 * i + 1] : 0;
                        string who = i < others ? $"{head} 유닛 {i} ({CharacterNames.Of(icon)})" : $"{head} 우리 편 자리 {i - others + 1}";
                        if (pa != pb) list.Add(new("유닛 자리", who, "x, y", pa, pb));
                        else if (!Same(ea, eb, p + 4, 2) && p + 6 <= ea.Length && p + 6 <= eb.Length) list.Add(new("유닛 자리", who, "+4", $"{Dat.U16(ea, p + 4)}", $"{Dat.U16(eb, p + 4)}"));
                    }
                    break;
                }
            }
        }
        return list.Count > 0 ? list : Generic(a, b);

        static string Triple(byte[] d, int at) => $"({d[at]}, {d[at + 1]}, {d[at + 2]})";

        static string Chest(byte[] d, int e)
        {
            int v = Dat.U16(d, 84 + 3 * e);
            return d[83 + 3 * e] switch { 0 => $"아이템 {Named(GameNames.Item(v), v)}", 1 => $"돈 {v}", var s => $"사건 {s}" };
        }
    }

    // ── FDTXT: 0 이름 표(줄마다 원본 글꼴 번호), 그 밖은 장면 대사 묶음 ────────────

    private static List<Change> Text(string a, string b)
    {
        var list = new List<Change>();
        foreach (int k in GameVersions.ChangedEntries(a, b))
        {
            if (k != 0) { list.Add(new("대사 묶음", $"[{k}]", "바이트", "", $"{Diff(Entry(a, k) ?? [], Entry(b, k) ?? [])}곳 바뀜")); continue; }
            var (na, nb) = (new NameTable(Path.GetDirectoryName(a)!), new NameTable(Path.GetDirectoryName(b)!));
            foreach (int i in Enumerable.Range(0, Math.Max(na.Count, nb.Count)).Where(i => !na.Line(i).SequenceEqual(nb.Line(i))))
                list.Add(new("이름 표", $"줄 {i}", LineMeaning(i), $"글자 {na.Line(i).Length}", $"글자 {nb.Line(i).Length}"));
        }
        return list.Count > 0 ? list : Generic(a, b);

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

    private static List<Change> Generic(string a, string b)
    {
        var entries = GameVersions.ChangedEntries(a, b);
        if (entries.Count > 0)
            return entries.Select(k => new Change("항목", $"[{k}]", "크기 · 바뀐 바이트", $"{Entry(a, k)?.Length ?? 0}바이트",
                $"{Entry(b, k)?.Length ?? 0}바이트, {Diff(Entry(a, k) ?? [], Entry(b, k) ?? [])}곳 바뀜")).ToList();
        var (da, db) = (File.ReadAllBytes(a), File.ReadAllBytes(b));
        return [new("항목", Path.GetFileName(a), "크기 · 다른 바이트", $"{da.Length}바이트", $"{db.Length}바이트, {Diff(da, db)}곳 다름")];
    }

    private static byte[]? Entry(string path, int k)
    {
        try { return k >= 0 && k < Dat.Count(path) ? Dat.Load(path, k) : null; }
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
