using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 아군 · 적 능력치 표를 FD2.EXE 에서 읽고 고쳐 쓰기 — 편집기 「능력치」 탭.
/// 아군은 동료 꼴 표(0x61DA1, 24바이트)와 레벨업 성장표(0x620A1, 11바이트)를 한 줄로, 적은 몬스터 표(0x61AF9, 10바이트 — 능력은 레벨당 값).
/// </summary>
public static class StatTables
{
    /// <summary>한 칸 — 이름, 표 시작에서 떨어진 곳, 바이트 수(1 · 2), 부호.</summary>
    public sealed record Field(string Name, int Va, int Size, bool Signed = false);

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
    public static void Save(string gameDir, byte[] exe)
    {
        if (exe.Length != GameTables.ExeSize) throw new IOException("FD2.EXE 크기가 원본과 달라 쓰지 않습니다.");
        string path = Path.Combine(gameDir, "FD2.EXE"), backup = Path.Combine(gameDir, "FD2-backup.EXE");
        if (!File.Exists(backup)) File.Copy(path, backup);
        File.WriteAllBytes(path, exe);
    }
}
