using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 아군 · 적 능력치 표를 FD2.EXE 에서 읽고 고쳐 쓰기 — 편집기 「능력치」 탭.
/// 아군은 동료 꼴 표(0x61DA1, 24바이트)와 레벨업 성장표(0x620A1, 11바이트)를 한 줄로, 적은 몬스터 표(0x61AF9, 10바이트 — 능력은 레벨당 값).
/// </summary>
public static class StatTables
{
    /// <summary>한 칸 — 이름, 표 시작에서 떨어진 곳, 바이트 수(1 · 2), 부호.</summary>
    public sealed record Field(string Name, int Va, int Size, bool Signed = false, int Empty = 0, bool Absolute = false, Choice Pick = Choice.None);

    /// <summary>칸 값을 이름으로 고르는 갈래 — 편집기가 「이름(번호)」 고르개로 보인다.</summary>
    public enum Choice { None, Race, Class, Weapon, Armor, Item, Town, Building, Key }

    /// <summary>고르개 목록 — (값, 「이름(번호)」). 빈 칸 값(255)을 쓰는 칸이면 「없음(255)」도.</summary>
    public static List<(int Value, string Label)> Choices(Field f)
    {
        var values = f.Pick switch
        {
            Choice.Race => Enumerable.Range(0, GameNames.RaceCount),
            Choice.Class => Enumerable.Range(0, GameNames.ClassCount),
            Choice.Weapon => Enumerable.Range(0, 128),
            Choice.Armor => Enumerable.Range(128, GameTables.ItemCount - 128),
            Choice.Item => Enumerable.Range(0, GameTables.ItemCount),
            Choice.Town => Enumerable.Range(0, 3),
            Choice.Building => Enumerable.Range(0, 5),
            Choice.Key => Enumerable.Range(0x54, 30),
            _ => [],
        };
        var list = values.Select(v => (v, Label(f, v))).ToList();
        if (f.Empty == 255) list.Insert(0, (255, Label(f, 255)));
        return list;
    }

    /// <summary>마을 건물 0~4(FDTXT 0 의 495~499). 비밀상점 열쇠는 이 건물에 커서를 두고 키를 누른다.</summary>
    private static readonly string[] Buildings = ["주점", "무기점", "출구", "도구점", "교회"];

    /// <summary>비밀상점 열쇠 키 스캔코드 0x54~0x71 = Shift · Ctrl · Alt + F1~F10.</summary>
    private static readonly string[] KeyMods = ["Shift", "Ctrl", "Alt"];

    /// <summary>값 하나의 「이름(번호)」 — 이름이 없으면 「(빈 이름)(번호)」.</summary>
    public static string Label(Field f, int v)
    {
        if (v == 255 && f.Empty == 255) return "없음(255)";
        string name = f.Pick switch
        {
            Choice.Race => v == 0 ? "없음" : GameNames.Race(v),
            Choice.Class => v == 0 ? "없음" : GameNames.Class(v),
            Choice.Weapon or Choice.Armor or Choice.Item => GameNames.Item(v),
            Choice.Town => v < 3 ? $"마을 그림 {v + 1}" : "",
            Choice.Building => v < Buildings.Length ? Buildings[v] : "",
            Choice.Key => v is >= 0x54 and <= 0x71 ? $"{KeyMods[(v - 0x54) / 10]}+F{(v - 0x54) % 10 + 1}" : "",
            _ => "",
        };
        return $"{(name == "" ? "(빈 이름)" : name)}({v})";
    }

    /// <summary>표 하나 — 줄 이름과 줄마다의 칸들(Va 는 그 줄의 절대 주소).</summary>
    public sealed record Table(string Title, List<(string Name, List<Field> Fields)> Rows);

    private static List<Field> Hero(int n)
    {
        int a = 0x61DA1 + 24 * n, g = 0x620A1 + 11 * n;
        return
        [
            new("종족", a, 1, Pick: Choice.Race), new("직업", a + 1, 1, Pick: Choice.Class), new("레벨", a + 2, 1), new("HP", a + 3, 2), new("MP", a + 5, 2), new("이동", a + 7, 1),
            new("무기", a + 12, 1, Pick: Choice.Weapon), new("방어구", a + 13, 1, Pick: Choice.Armor), new("역량", a + 18, 2, true), new("내력", a + 20, 2, true), new("속도", a + 22, 2, true),
            new("성장 역량 최소", g, 1), new("성장 역량 최대", g + 1, 1), new("성장 내력 최소", g + 2, 1), new("성장 내력 최대", g + 3, 1),
            new("성장 속도 최소", g + 4, 1), new("성장 속도 최대", g + 5, 1), new("성장 HP 최소", g + 6, 1), new("성장 HP 최대", g + 7, 1),
            new("성장 MP 최소", g + 8, 1), new("성장 MP 최대", g + 9, 1),
        ];
    }

    private static List<Field> Monster(int m)
    {
        int a = 0x61AF9 + 10 * m;
        return
        [
            new("종족", a, 1, Pick: Choice.Race), new("직업", a + 1, 1, Pick: Choice.Class), new("HP/레벨", a + 2, 2), new("MP/레벨", a + 4, 1), new("역량/레벨", a + 5, 1),
            new("방어/레벨", a + 6, 1), new("속도/레벨", a + 7, 1), new("이동", a + 8, 1), new("경험", a + 9, 1),
        ];
    }

    private static List<Field> Item(int i)
    {
        int a = 0x602AD + 23 * i;
        return
        [
            new("종류", a, 1), new("공격", a + 1, 2, true), new("명중", a + 3, 2, true), new("방어", a + 5, 2, true), new("회피", a + 7, 2, true),
            new("특수", a + 9, 1), new("특수 값", a + 10, 1), new("사거리 최소", a + 11, 1), new("사거리 최대", a + 12, 1),
            new("쓰임", a + 13, 1), new("쓰임 값", a + 14, 2), new("쓰는 거리", a + 16, 1), new("쓰는 편", a + 17, 1), new("쓰는 범위", a + 18, 1),
            new("값", a + 19, 2),
        ];
    }

    private static List<Field> Spell(int m)
    {
        int a = 0x619FD + 7 * m;
        return [new("세기", a, 2), new("명중 %", a + 2, 1), new("사거리", a + 3, 1), new("범위", a + 4, 1), new("MP", a + 5, 1), new("대상 편", a + 6, 1)];
    }

    /// <summary>상점 기록(0x6238D, 31바이트) — +0 마을 그림(FDOTHER 11 · 61 · 62), +1 · +2 비밀상점 열쇠 건물 · 키, +3 무기점 12칸, +15 도구점 8칸, +23 비밀상점 8칸(255 = 빈 칸).</summary>
    private static List<Field> Shop(int n)
    {
        int a = 0x6238D + 31 * n;
        var f = new List<Field> { new("마을 그림", a, 1, Pick: Choice.Town), new("비밀상점 건물", a + 1, 1, Pick: Choice.Building), new("비밀상점 키", a + 2, 1, Pick: Choice.Key) };
        for (int i = 0; i < 12; i++) f.Add(new($"무기점 {i + 1}", a + 3 + i, 1, Empty: 255, Pick: Choice.Item));
        for (int i = 0; i < 8; i++) f.Add(new($"도구점 {i + 1}", a + 15 + i, 1, Empty: 255, Pick: Choice.Item));
        for (int i = 0; i < 8; i++) f.Add(new($"비밀상점 {i + 1}", a + 23 + i, 1, Empty: 255, Pick: Choice.Item));
        return f;
    }

    /// <summary>물건 표(0x602AD, 23바이트) 215줄 — 0~127 무기 쪽, 128~ 방어구 · 도구 쪽. 줄 이름은 게임 이름(번호).</summary>
    public static Table Items() =>
        new("아이템", Enumerable.Range(0, GameTables.ItemCount).Select(i => (Label(new Field("", 0, 1, Pick: Choice.Item), i), Item(i))).ToList());

    /// <summary>마법 표(0x619FD, 7바이트) 36줄.</summary>
    public static Table Spells() =>
        new("마법", Enumerable.Range(0, GameTables.SpellCount).Select(m => ($"{m} {SpellNames.Of(m)}", Spell(m))).ToList());

    /// <summary>상점 26곳 — 기록 n 은 장면 n+1 앞 마을(n+2장 앞, sub_4E809). 22~24 는 하늘 위 장이라 비어 있다.</summary>
    public static Table Shops() =>
        new("상점", Enumerable.Range(0, 26).Select(n => ($"{n + 2}장 앞 마을", Shop(n))).ToList());

    /// <summary>줄을 비운다(삭제) — 수치는 0, 상점 물건 칸은 255(빈 칸). 표 크기가 정해져 있어 줄 자체는 남는다.</summary>
    public static void Clear(byte[] exe, IEnumerable<Field> row)
    {
        foreach (var f in row) Set(exe, f, f.Empty);
    }

    public static Table Allies() =>
        new("아군", Enumerable.Range(0, GameTables.HeroCount).Select(n => ($"{n} {CharacterNames.Of(n)}", Hero(n))).ToList());

    public static Table Enemies() =>
        new("적", Enumerable.Range(0, GameTables.MonsterCount).Select(m => ($"{m} {CharacterNames.Of(GameTables.MonsterBase + m)}", Monster(m))).ToList());

    private static int FileOffset(int va) => va >= 0x60000 ? va + 0x19014 : va + 0x25014;

    private static int At(Field f) => f.Absolute ? f.Va : FileOffset(f.Va);

    public static int Get(byte[] exe, Field f)
    {
        var s = exe.AsSpan(At(f));
        return f.Size == 1 ? s[0] : f.Signed ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);
    }

    /// <summary>칸에 넣을 수 있는 범위.</summary>
    public static (int Min, int Max) Range(Field f) => f.Size == 1 ? (0, 255) : f.Signed ? (short.MinValue, short.MaxValue) : (0, ushort.MaxValue);

    public static void Set(byte[] exe, Field f, int value)
    {
        var (min, max) = Range(f);
        value = Math.Clamp(value, min, max);
        var s = exe.AsSpan(At(f));
        if (f.Size == 1) s[0] = (byte)value;
        else if (f.Signed) BinaryPrimitives.WriteInt16LittleEndian(s, (short)value);
        else BinaryPrimitives.WriteUInt16LittleEndian(s, (ushort)value);
    }

    /// <summary>
    /// 고친 EXE 를 쓴다 — 처음 한 번은 옆에 FD2-backup.EXE 로 원래 것을 남긴다(이미 있으면 그대로 둔다). 크기가 원본과 다르면 거절.
    /// </summary>
    /// <summary>상점 칸마다 빈 칸(255)을 뒤로 — 원작은 255 를 만나면 목록을 끝내서, 가운데를 비우면 뒤 물건이 안 보이게 된다.</summary>
    public static void CompactShops(byte[] exe)
    {
        for (int n = 0; n < 26; n++)
            foreach (var (from, count) in new[] { (3, 12), (15, 8), (23, 8) })
            {
                int at = FileOffset(0x6238D + 31 * n + from);
                var keep = exe.AsSpan(at, count).ToArray().Where(b => b != 255).ToArray();
                for (int i = 0; i < count; i++) exe[at + i] = i < keep.Length ? keep[i] : (byte)255;
            }
    }

    /// <summary>
    /// 장 n 의 유닛 표(FDFIELD 3n+1 의 131 부터 26바이트씩)와 좌표(3n+2 의 2 부터 6바이트씩) — 적 · 합류하는 사람 · 컴퓨터 아군.
    /// fdfield 는 FDFIELD.DAT 통째(칸 주소는 파일 안 절대 위치), 우리 편 자리는 빼고 그 밖의 유닛만.
    /// </summary>
    public static Table StageUnits(byte[] fdfield, int scene)
    {
        int info = Dat.U32(fdfield, 6 + 4 * (3 * scene + 1)), pos = Dat.U32(fdfield, 6 + 4 * (3 * scene + 2));
        int infoEnd = Dat.U32(fdfield, 6 + 4 * (3 * scene + 2)), posEnd = Dat.U32(fdfield, 6 + 4 * (3 * scene + 3));
        int others = fdfield[info + 2];
        var rows = new List<(string, List<Field>)>();
        for (int i = 0; i < others; i++)
        {
            int u = info + 131 + 26 * i, p = pos + 2 + 6 * i;
            if (u + 26 > infoEnd || p + 4 > posEnd) break;
            var f = new List<Field>
            {
                new("편", u, 1, Absolute: true), new("그림", u + 1, 1, Absolute: true), new("레벨", u + 4, 1, Absolute: true),
                new("x", p, 2, Absolute: true), new("y", p + 2, 2, Absolute: true), new("등장 차례", u + 21, 1, Absolute: true),
                new("무기", u + 5, 1, Absolute: true, Empty: 255, Pick: Choice.Weapon), new("방어구", u + 6, 1, Absolute: true, Empty: 255, Pick: Choice.Armor),
            };
            for (int k = 0; k < 6; k++) f.Add(new($"아이템 {k + 1}", u + 7 + k, 1, Absolute: true, Empty: 255, Pick: Choice.Item));
            f.AddRange([new("AI", u + 17, 1, Absolute: true), new("AI x", u + 18, 1, Absolute: true), new("AI y", u + 19, 1, Absolute: true),
                        new("떨굼 종류", u + 22, 1, Absolute: true), new("떨굼 값", u + 23, 2, Absolute: true)]);
            rows.Add(($"{i} {CharacterNames.Of(fdfield[u + 1])}", f));
        }
        return new("장 유닛", rows);
    }

    /// <summary>고친 FDFIELD.DAT 를 쓴다 — 처음 한 번은 FDFIELD-backup.DAT 를 남긴다. 크기가 바뀌지 않는 칸만 고치므로 통째로 쓴다.</summary>
    public static void SaveField(string gameDir, byte[] fdfield)
    {
        string path = Path.Combine(gameDir, "FDFIELD.DAT"), backup = Path.Combine(gameDir, "FDFIELD-backup.DAT");
        if (new FileInfo(path).Length != fdfield.Length) throw new IOException("FDFIELD.DAT 크기가 읽을 때와 달라 쓰지 않습니다.");
        if (!File.Exists(backup)) File.Copy(path, backup);
        File.WriteAllBytes(path, fdfield);
    }

    public static void Save(string gameDir, byte[] exe)
    {
        CompactShops(exe);
        if (exe.Length != GameTables.ExeSize) throw new IOException("FD2.EXE 크기가 원본과 달라 쓰지 않습니다.");
        string path = Path.Combine(gameDir, "FD2.EXE"), backup = Path.Combine(gameDir, "FD2-backup.EXE");
        if (!File.Exists(backup)) File.Copy(path, backup);
        File.WriteAllBytes(path, exe);
    }
}
