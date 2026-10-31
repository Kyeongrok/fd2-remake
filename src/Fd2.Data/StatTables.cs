using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 아군 · 적 능력치 표를 FD2.EXE 에서 읽고 고쳐 쓰기 — 편집기 「능력치」 탭.
/// 아군은 동료 꼴 표(0x61DA1, 24바이트)와 레벨업 성장표(0x620A1, 11바이트)를 한 줄로, 적은 몬스터 표(0x61AF9, 10바이트 — 능력은 레벨당 값).
/// </summary>
public static class StatTables
{
    /// <summary>한 칸 — 이름, 표 시작에서 떨어진 곳, 바이트 수(1 · 2), 부호.</summary>
    public sealed record Field(string Name, int Va, int Size, bool Signed = false, int Empty = 0);

    /// <summary>표 하나 — 줄 이름과 줄마다의 칸들(Va 는 그 줄의 절대 주소).</summary>
    public sealed record Table(string Title, List<(string Name, List<Field> Fields)> Rows);

    private static List<Field> Hero(int n)
    {
        int a = 0x61DA1 + 24 * n, g = 0x620A1 + 11 * n;
        return
        [
            new("종족", a, 1), new("직업", a + 1, 1), new("레벨", a + 2, 1), new("HP", a + 3, 2), new("MP", a + 5, 2), new("이동", a + 7, 1),
            new("무기", a + 12, 1), new("옷", a + 13, 1), new("역량", a + 18, 2, true), new("내력", a + 20, 2, true), new("속도", a + 22, 2, true),
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
            new("종족", a, 1), new("직업", a + 1, 1), new("HP/레벨", a + 2, 2), new("MP/레벨", a + 4, 1), new("역량/레벨", a + 5, 1),
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

    /// <summary>상점 기록(0x6238D, 31바이트) — +0 마을 꼴, +1 · +2 비밀 상점 자리 · 키, +3 무기 12칸, +15 도구 8칸, +23 비밀 8칸(255 = 빈 칸).</summary>
    private static List<Field> Shop(int n)
    {
        int a = 0x6238D + 31 * n;
        var f = new List<Field> { new("마을 꼴", a, 1), new("비밀 자리", a + 1, 1), new("비밀 키", a + 2, 1) };
        for (int i = 0; i < 12; i++) f.Add(new($"무기 {i + 1}", a + 3 + i, 1, Empty: 255));
        for (int i = 0; i < 8; i++) f.Add(new($"도구 {i + 1}", a + 15 + i, 1, Empty: 255));
        for (int i = 0; i < 8; i++) f.Add(new($"비밀 {i + 1}", a + 23 + i, 1, Empty: 255));
        return f;
    }

    /// <summary>물건 표(0x602AD, 23바이트) 215줄 — 0~127 무기 쪽, 128~ 옷 · 도구 쪽.</summary>
    public static Table Items() =>
        new("물건", Enumerable.Range(0, GameTables.ItemCount).Select(i => ($"물건 {i}", Item(i))).ToList());

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

    public static int Get(byte[] exe, Field f)
    {
        var s = exe.AsSpan(FileOffset(f.Va));
        return f.Size == 1 ? s[0] : f.Signed ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);
    }

    /// <summary>칸에 넣을 수 있는 범위.</summary>
    public static (int Min, int Max) Range(Field f) => f.Size == 1 ? (0, 255) : f.Signed ? (short.MinValue, short.MaxValue) : (0, ushort.MaxValue);

    public static void Set(byte[] exe, Field f, int value)
    {
        var (min, max) = Range(f);
        value = Math.Clamp(value, min, max);
        var s = exe.AsSpan(FileOffset(f.Va));
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

    public static void Save(string gameDir, byte[] exe)
    {
        CompactShops(exe);
        if (exe.Length != GameTables.ExeSize) throw new IOException("FD2.EXE 크기가 원본과 달라 쓰지 않습니다.");
        string path = Path.Combine(gameDir, "FD2.EXE"), backup = Path.Combine(gameDir, "FD2-backup.EXE");
        if (!File.Exists(backup)) File.Copy(path, backup);
        File.WriteAllBytes(path, exe);
    }
}
