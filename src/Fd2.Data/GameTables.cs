using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>동료 표(0x61DA1, 24바이트) 한 줄 — 합류할 때의 바탕.</summary>
/// <remarks>Spells = 처음 아는 마법 비트(마법 m = 비트 m). Bag = 가방 4칸(255 = 없음). 덧값은 FD2 Edit 의 「공격추가 · 방어추가 · 속도추가」.</remarks>
public sealed record HeroInfo(int Race, int Class, int Level, int Hp, int Mp, int Move, uint Spells, int Weapon, int Armor,
                              int[] Bag, int StrAdd, int DefAdd, int SpdAdd);

/// <summary>
/// 레벨업 상승치(0x620A1, 11바이트) 한 꼴 — (최소, 최대) 쌍 다섯. 오르는 값은 최소 + 난수 % (최대 − 최소)라 최대는 안 나온다.
/// SpellList 는 레벨에 배우는 마법 표 번호(255 없음).
/// </summary>
public sealed record Growth(int StrMin, int StrMax, int DefMin, int DefMax, int SpdMin, int SpdMax,
                            int HpMin, int HpMax, int MpMin, int MpMax, int SpellList);

/// <summary>몬스터 표(0x61AF9, 10바이트) 한 줄 — 능력은 레벨당 값 × 레벨, 이동력 · 경험 바탕은 그대로.</summary>
public sealed record MonsterInfo(int Race, int Class, int Hp, int Mp, int Str, int Def, int Spd, int Move, int Exp);

/// <summary>
/// 물건 표(0x602AD, 23바이트) 한 줄. 번호 0~127 은 무기 쪽, 128~ 은 옷 쪽. Special: 0 없음 · 2 독(확률 SpecialValue %) ·
/// 3 늘 두 번 · 4 필살 + SpecialValue %. Use: 쓰는 효과 번호(0 못 씀), UseRange 16 이상은 직선(거리 − 16), UseSide 1 우리 · 0 적.
/// </summary>
public sealed record ItemInfo(int Kind, int Atk, int Hit, int Def, int Evade, int Special, int SpecialValue, int RangeMin, int RangeMax,
                              int Use, int UseValue, int UseRange, int UseSide, int UseArea, int Price)
{
    public bool IsWeapon(int id) => id < 128;

    /// <summary>물건 그림 단추(<see cref="Buttons"/>) — 무기 칼(0) · 옷 갑옷(10) · 그 밖 주머니(2). 게임 메뉴 · 편집기 아이템 표가 같이 쓴다.</summary>
    public static int IconOf(int id, int kind, int atk) => id < 128 && kind < 32 && atk > 0 ? 0 : kind < 32 && id >= 128 ? 10 : 2;
}

/// <summary>
/// 마법 표(0x619FD, 7바이트) 한 줄 — Power 세기, Hit 명중 %, Range 사거리(0 = 자기 칸 가운데, 16 이상 = 한 줄),
/// Area 범위(마름모 걸음 수, 16 이상 = 십자), Mp, Side 대상 편(0 상대 · 1 우리 · 3 전송).
/// </summary>
public sealed record SpellInfo(int Power, int Hit, int Range, int Area, int Mp, int Side);

/// <summary>
/// 직업(유닛 +32) 하나 — 장비 종류 6칸(0x6188A), 필살 %(0x524A8), 받는 마법 피해 ×/10(0x51F96), 부활값/레벨(0x52397),
/// 지형 종류 0~5 마다 드는 이동력(0x61646, 20 = 못 감).
/// </summary>
public sealed record ClassInfo(int[] EquipKinds, int Crit, int MagicMul, int Revive, int[] MoveCost);

/// <summary>
/// 마을 상점(0x6238D, 31바이트) — 무기점 12 · 도구점 8 · 비밀상점 8 (물건 번호, 255 끝 — sub_26A0D). Town = 마을 그림 · 건물 배치 번호. 0 만 든 기록은 빈 기록.
/// 비밀상점 열쇠(sub_26152): 건물 SecretAt(+1, 0 주점 · 1 무기점 · 2 출구 · 3 도구점 · 4 교회)을 고른 채 스캔코드 SecretKey(+2) 키를 누르면 건물 5(「???」)로.
/// 키는 0x54~0x5D Shift+F1~F10 · 0x5E~0x67 Ctrl+F1~F10 · 0x68~0x71 Alt+F1~F10.
/// </summary>
public sealed record ShopInfo(int Town, int[] Weapons, int[] Tools, int[] Secret, int SecretAt = -1, int SecretKey = -1)
{
    /// <summary>열쇠 키 이름 — 「Ctrl+F2」 꼴, 모르면 빈 글.</summary>
    public string SecretKeyName => SecretKey switch
    {
        >= 0x54 and <= 0x5D => $"Shift+F{SecretKey - 0x53}",
        >= 0x5E and <= 0x67 => $"Ctrl+F{SecretKey - 0x5D}",
        >= 0x68 and <= 0x71 => $"Alt+F{SecretKey - 0x67}",
        _ => "",
    };
}

/// <summary>
/// 이동 대본(0x627D8, sub_1366A) 한 걸음 — Wait 가 아니면 Units 가 다 함께 그 쪽으로 Count 칸 걷고,
/// Wait 면 걷지 않고 그 쪽을 보며 Count 그림 동안 쉰다(0 이면 한 번 그리기만).
/// </summary>
public sealed record MoveStep(int Count, bool Wait, (int Unit, Dir Dir)[] Units);

/// <summary>
/// 원작 실행 파일 FD2.EXE 의 규칙 표 — 게임 폴더의 EXE 를 그 자리에서 읽는다(저장소에는 원작 자료를 넣지 않는다).
/// 주소는 EXE 안 주소(VA). 파일 자리 = VA + 0x19014(VA 0x60000~, 오브젝트 3) · VA + 0x25014(VA 0x50000~, 오브젝트 2).
/// 원본 판과 모션패치5 의 EXE 는 크기 · 자리가 같고 값만 몇 곳 다르다.
/// </summary>
public sealed class GameTables
{
    /// <summary>원본 판 · 모션패치5 FD2.EXE 의 크기 — 다르면 자리가 어긋났을 수 있어 읽지 않는다.</summary>
    public const int ExeSize = 509158;

    public const int HeroCount = 32, Forms = 68, MonsterBase = 68, MonsterCount = 68, ItemCount = 215, SpellCount = 36;

    private readonly byte[] _exe;

    /// <summary>동료 표 — 0~31 이 동료. 원본 sub_10C50 은 꼴 32~67 도 같은 식으로 읽는다(그 뒤는 성장표와 겹친 값이다).</summary>
    public IReadOnlyList<HeroInfo> Heroes { get; internal set; }
    public IReadOnlyList<Growth> Growths { get; internal set; }
    /// <summary>몬스터 표 — 캐릭터 번호 − 68.</summary>
    public IReadOnlyList<MonsterInfo> Monsters { get; internal set; }
    public IReadOnlyList<ItemInfo> Items { get; internal set; }
    public IReadOnlyList<SpellInfo> Spells { get; internal set; }
    /// <summary>직업 0~28(0 은 쓰지 않는 칸, 이동비는 「다 1」 = 지형을 안 따지는 걸음 수).</summary>
    public IReadOnlyList<ClassInfo> Classes { get; internal set; }
    /// <summary>레벨에 배우는 마법 표(0x626B3, 12바이트 = (레벨, 마법) 6쌍, 255 끝) 20개.</summary>
    public IReadOnlyList<(int Level, int Spell)[]> SpellLearn { get; internal set; }
    /// <summary>전직 뒤 꼴(32~67) 의 (직업, 이동력 덧값) — 0x615FE, 꼴 − 32.</summary>
    public IReadOnlyList<(int Class, int MoveAdd)> ClassChange { get; internal set; }
    /// <summary>처음 꼴(0~17) 의 아이템 전직에 드는 물건(0x523D5, 255 없음).</summary>
    public IReadOnlyList<int> ClassChangeItem { get; internal set; }
    /// <summary>지형 종류 0~5 의 공격 % (0x51A12) · 방어 % (0x51A2A).</summary>
    public IReadOnlyList<int> TerrainAttack { get; internal set; }
    public IReadOnlyList<int> TerrainDefense { get; internal set; }
    /// <summary>마을 상점 — 기록 번호 1 부터(원본 sub_4E809), [0] 은 기록 1.</summary>
    public IReadOnlyList<ShopInfo> Shops { get; internal set; }

    private GameTables(byte[] exe)
    {
        _exe = exe;
        Heroes = Enumerable.Range(0, Forms).Select(ReadHero).ToList();
        Growths = Enumerable.Range(0, Forms).Select(f =>
        {
            var g = Bytes(0x620A1 + 11 * f, 11);
            return new Growth(g[0], g[1], g[2], g[3], g[4], g[5], g[6], g[7], g[8], g[9], g[10]);
        }).ToList();
        Monsters = Enumerable.Range(0, MonsterCount).Select(m =>
        {
            int a = 0x61AF9 + 10 * m;
            return new MonsterInfo(U8(a), U8(a + 1), U16(a + 2), U8(a + 4), U8(a + 5), U8(a + 6), U8(a + 7), U8(a + 8), U8(a + 9));
        }).ToList();
        Items = Enumerable.Range(0, ItemCount).Select(i =>
        {
            int a = 0x602AD + 23 * i;
            return new ItemInfo(U8(a), S16(a + 1), S16(a + 3), S16(a + 5), S16(a + 7), U8(a + 9), U8(a + 10), U8(a + 11), U8(a + 12),
                                U8(a + 13), U16(a + 14), U8(a + 16), U8(a + 17), U8(a + 18), U16(a + 19));
        }).ToList();
        Spells = Enumerable.Range(0, SpellCount).Select(m =>
        {
            int a = 0x619FD + 7 * m;
            return new SpellInfo(U16(a), U8(a + 2), U8(a + 3), U8(a + 4), U8(a + 5), U8(a + 6));
        }).ToList();
        Classes = Enumerable.Range(0, 29).Select(c => new ClassInfo(
            Bytes(0x6188A + 7 * c, 6).Where(k => k != 255).Select(k => (int)k).ToArray(),
            c == 0 ? 0 : U8(0x524A8 + c - 1),
            c == 0 ? 10 : S32(0x51F96 + 4 * (c - 1)),
            U16(0x52397 + 2 * c),
            Bytes(0x61646 + 20 * c, 6).Select(b => (int)b).ToArray())).ToList();
        SpellLearn = Enumerable.Range(0, 20).Select(k =>
        {
            var r = Bytes(0x626B3 + 12 * k, 12);
            return Enumerable.Range(0, 6).Where(i => r[2 * i] != 255).Select(i => ((int)r[2 * i], (int)r[2 * i + 1])).ToArray();
        }).ToList();
        ClassChange = Enumerable.Range(0, 36).Select(f => ((int)U8(0x615FE + 2 * f), (int)U8(0x615FE + 2 * f + 1))).ToList();
        ClassChangeItem = Bytes(0x523D5, 18).Select(b => (int)b).ToList();
        TerrainAttack = Enumerable.Range(0, 6).Select(i => S32(0x51A12 + 4 * i)).ToList();
        TerrainDefense = Enumerable.Range(0, 6).Select(i => S32(0x51A2A + 4 * i)).ToList();
        Shops = Enumerable.Range(0, 26).Select(n =>
        {
            var r = Bytes(0x6238D + 31 * n, 31);
            if (r.All(b => b == 0)) return new ShopInfo(0, [], [], []);   // 빈 기록(22~24 — 하늘 위 장이라 마을이 없다)
            int[] List(int from, int count) => r.Skip(from).Take(count).TakeWhile(b => b != 255).Select(b => (int)b).ToArray();
            return new ShopInfo(r[0], List(3, 12), List(15, 8), List(23, 8), r[1], r[2]);
        }).ToList();
    }

    /// <summary>게임 폴더 tables 의 .json 으로 덮은 표 이름들(<see cref="TablesJson"/>) — 비면 모두 EXE 값.</summary>
    public IReadOnlyList<string> FromJson { get; private set; } = [];

    /// <summary>
    /// 게임 폴더의 FD2.EXE 를 읽는다 — 없거나 크기가 다르면 null(원작 규칙 없이 돈다).
    /// 폴더에 tables/*.json(<see cref="TablesJson"/>)이 있으면 수치 표는 그 값을 쓴다.
    /// </summary>
    /// <param name="json">false 면 tables/*.json 을 보지 않고 EXE 값 그대로(편집기가 EXE 에서 JSON 을 새로 뽑을 때).</param>
    public static GameTables? Load(string dir, bool json = true)
    {
        string path = Path.Combine(dir, "FD2.EXE");
        if (!File.Exists(path) || new FileInfo(path).Length != ExeSize) return null;
        var t = new GameTables(File.ReadAllBytes(path));
        if (json) t.FromJson = TablesJson.Apply(t, dir);
        return t;
    }

    /// <summary>exe 바이트 위에서 읽은 표(EXE 자리 그대로).</summary>
    public static GameTables FromBytes(byte[] exe) => new((byte[])exe.Clone());

    /// <summary>
    /// 지금 표 값을 exe 바이트(FD2.EXE 통째)의 제자리에 쓴다 — 생성자가 읽는 것의 거울. 편집기가 tables/*.json 값을 EXE 표에 들일 때.
    /// 목록 칸(장비 종류 · 상점)은 값 뒤에 255 하나로 끝낸다. 표를 안 고쳤으면 EXE 바이트가 그대로 나온다(원본 · 모션패치5 · 파워 인핸스드로 확인).
    /// </summary>
    public void WriteTo(byte[] exe)
    {
        void W8(int va, int v) => exe[FileOffset(va)] = (byte)v;
        void W16(int va, int v) => BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(FileOffset(va)), unchecked((ushort)v));
        void W32(int va, int v) => BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(FileOffset(va)), v);
        // 목록은 값들 뒤에 255 하나로 끝낸다 — 그 뒤 칸은 원본이 남긴 쓰레기 값일 수 있어 건드리지 않는다(읽을 때도 255 에서 끊는다)
        void List(int va, int n, IReadOnlyList<int> xs) { for (int i = 0; i < n && i <= xs.Count; i++) W8(va + i, i < xs.Count ? xs[i] : 255); }
        for (int n = 0; n < Heroes.Count; n++)
        {
            var h = Heroes[n];
            int a = 0x61DA1 + 24 * n;
            W8(a, h.Race); W8(a + 1, h.Class); W8(a + 2, h.Level); W16(a + 3, h.Hp); W16(a + 5, h.Mp); W8(a + 7, h.Move); W32(a + 8, unchecked((int)h.Spells));
            W8(a + 12, h.Weapon); W8(a + 13, h.Armor); List(a + 14, 4, h.Bag); W16(a + 18, h.StrAdd); W16(a + 20, h.DefAdd); W16(a + 22, h.SpdAdd);
        }
        for (int f = 0; f < Growths.Count; f++)
        {
            var g = Growths[f];
            int[] v = [g.StrMin, g.StrMax, g.DefMin, g.DefMax, g.SpdMin, g.SpdMax, g.HpMin, g.HpMax, g.MpMin, g.MpMax, g.SpellList];
            for (int i = 0; i < 11; i++) W8(0x620A1 + 11 * f + i, v[i]);
        }
        for (int m = 0; m < Monsters.Count; m++)
        {
            var x = Monsters[m];
            int a = 0x61AF9 + 10 * m;
            W8(a, x.Race); W8(a + 1, x.Class); W16(a + 2, x.Hp); W8(a + 4, x.Mp); W8(a + 5, x.Str); W8(a + 6, x.Def); W8(a + 7, x.Spd); W8(a + 8, x.Move); W8(a + 9, x.Exp);
        }
        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            int a = 0x602AD + 23 * i;
            W8(a, it.Kind); W16(a + 1, it.Atk); W16(a + 3, it.Hit); W16(a + 5, it.Def); W16(a + 7, it.Evade); W8(a + 9, it.Special); W8(a + 10, it.SpecialValue);
            W8(a + 11, it.RangeMin); W8(a + 12, it.RangeMax); W8(a + 13, it.Use); W16(a + 14, it.UseValue); W8(a + 16, it.UseRange); W8(a + 17, it.UseSide);
            W8(a + 18, it.UseArea); W16(a + 19, it.Price);
        }
        for (int m = 0; m < Spells.Count; m++)
        {
            var sp = Spells[m];
            int a = 0x619FD + 7 * m;
            W16(a, sp.Power); W8(a + 2, sp.Hit); W8(a + 3, sp.Range); W8(a + 4, sp.Area); W8(a + 5, sp.Mp); W8(a + 6, sp.Side);
        }
        for (int c = 0; c < Classes.Count; c++)
        {
            var k = Classes[c];
            List(0x6188A + 7 * c, 6, k.EquipKinds);
            if (c > 0) { W8(0x524A8 + c - 1, k.Crit); W32(0x51F96 + 4 * (c - 1), k.MagicMul); }
            W16(0x52397 + 2 * c, k.Revive);
            for (int g = 0; g < 6 && g < k.MoveCost.Length; g++) W8(0x61646 + 20 * c + g, k.MoveCost[g]);
        }
        for (int k = 0; k < SpellLearn.Count; k++)
        {
            var row = SpellLearn[k];
            for (int i = 0; i < 6; i++)
            {
                // 빈 쌍은 레벨만 255 — 짝의 마법 칸은 원본 값을 둔다(읽을 때 레벨 255 쌍은 건너뛴다)
                W8(0x626B3 + 12 * k + 2 * i, i < row.Length ? row[i].Level : 255);
                if (i < row.Length) W8(0x626B3 + 12 * k + 2 * i + 1, row[i].Spell);
            }
        }
        for (int f = 0; f < ClassChange.Count; f++) { W8(0x615FE + 2 * f, ClassChange[f].Class); W8(0x615FE + 2 * f + 1, ClassChange[f].MoveAdd); }
        for (int f = 0; f < ClassChangeItem.Count; f++) W8(0x523D5 + f, ClassChangeItem[f]);
        for (int i = 0; i < TerrainAttack.Count; i++) { W32(0x51A12 + 4 * i, TerrainAttack[i]); W32(0x51A2A + 4 * i, TerrainDefense[i]); }
        for (int n = 0; n < Shops.Count; n++)
        {
            var sh = Shops[n];
            int a = 0x6238D + 31 * n;
            if (sh.Weapons.Length + sh.Tools.Length + sh.Secret.Length == 0 && sh.Town == 0 && sh.SecretAt <= 0 && sh.SecretKey <= 0)
            {
                for (int i = 0; i < 31; i++) W8(a + i, 0);   // 빈 기록은 0 으로(하늘 위 장)
                continue;
            }
            W8(a, sh.Town); W8(a + 1, Math.Max(0, sh.SecretAt)); W8(a + 2, Math.Max(0, sh.SecretKey));
            List(a + 3, 12, sh.Weapons); List(a + 15, 8, sh.Tools); List(a + 23, 8, sh.Secret);
        }
    }

    /// <summary>
    /// 동료 꼴 form 이 레벨 level 로 들어올 때의 능력(sub_112A5 · sub_10C50) — 바탕 표에 성장표의 최솟값을 레벨만큼.
    /// 최대 HP · MP 는 (Lv − 1) 배, 역량 · 내력 · 속도는 Lv 배(원본 그대로).
    /// </summary>
    public (int MaxHp, int MaxMp, int Str, int Def, int Spd) HeroStats(int form, int level)
    {
        var h = Heroes[form];
        var g = Growths[form];
        return (h.Hp + (level - 1) * g.HpMin, h.Mp + (level - 1) * g.MpMin,
                h.StrAdd + level * g.StrMin, h.DefAdd + level * g.DefMin, h.SpdAdd + level * g.SpdMin);
    }

    private HeroInfo ReadHero(int n)
    {
        int a = 0x61DA1 + 24 * n;
        return new HeroInfo(U8(a), U8(a + 1), U8(a + 2), U16(a + 3), U16(a + 5), U8(a + 7), (uint)S32(a + 8), U8(a + 12), U8(a + 13),
                            Bytes(a + 14, 4).Select(b => (int)b).ToArray(), S16(a + 18), S16(a + 20), S16(a + 22));
    }

    // ── 지도 위 마법 그림 ───────────────────────────────────────────────────────

    /// <summary>
    /// 지도에서 보이는 마법의 그림(sub_1C4CC) — FDOTHER 6 의 시작 장(0x51F33) · 장 수(0x51F54) · 첫 장 소리(0x51F75, FDOTHER 80 묶음, 0 없음). 마법 0~32.
    /// </summary>
    public (int Start, int Count, int Sound) MapSpellFx(int spell) =>
        spell is >= 0 and < 33 ? (U8(0x51F33 + spell), U8(0x51F54 + spell), U8(0x51F75 + spell)) : (0, 0, 0);

    /// <summary>밑그림 물결 표(byte_627C8, sub_4EEE0) — 줄 y 를 표[(단계 + y) % 16] 점만큼 옆으로.</summary>
    public int[] WaveTable() => Enumerable.Range(0, 16).Select(i => (int)U8(0x627C8 + i)).ToArray();

    // ── 물결 색 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 물결 색 돌리기 표(0x60003, sub_4E31C) — 6비트 VGA 32색. 단계 s(0~15, BIOS 틱 2 마다 +1)에 색 224~239 = 표[s .. s+15].
    /// </summary>
    public uint[] WaterColors()
    {
        var c = new uint[32];
        for (int i = 0; i < 32; i++)
        {
            uint Six(int a) { int v = U8(a) & 63; return (uint)((v << 2) | (v >> 4)); }
            int a = 0x60003 + 3 * i;
            c[i] = 0xFF000000 | Six(a) << 16 | Six(a + 1) << 8 | Six(a + 2);
        }
        return c;
    }

    // ── 장면 ↔ 곡 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 장면에 들어갈 때 · 우리 차례의 곡(byte_51E63[장면]). 30 칸 표라 장면 30~32 는 원본처럼 바로 뒤 표(byte_51E81)를 넘겨 읽는다.
    /// </summary>
    public int SceneMusic(int scene) => U8(0x51E63 + Math.Clamp(scene, 0, 60));

    /// <summary>다른 차례(컴퓨터 아군 · 적)의 곡(byte_51E81[장면], sub_1A30B 가 차례 사이에 둘을 바꾼다).</summary>
    public int EnemyMusic(int scene) => U8(0x51E81 + Math.Clamp(scene, 0, 60));

    // ── 마을 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 다음 장면 n17 앞에 마을이 있나(sub_26152) — byte_523E7[n17] 이 0 이면 마을, 아니면 기록 · 편성만(장면 22 · 23 · 24 · 27 · 28 · 29).
    /// 표는 30 칸이고 n17 = 0(새 게임) · 프롤로그 31 · 32 는 마을을 거치지 않는다.
    /// </summary>
    public bool HasTown(int scene) => scene is >= 1 and < 30 && U8(0x523E7 + scene) == 0 && Shops[scene - 1].Weapons.Length + Shops[scene - 1].Tools.Length > 0;

    /// <summary>다음 장면 n17 의 마을 — sub_4E809(n17) = 기록 n17(1부터).</summary>
    /// <summary>마을 꼴 kind(기록 [0]) 의 그림 — FDOTHER 번호(표 0x52405: 11 · 61 · 62).</summary>
    public int TownPicture(int kind) => kind is >= 0 and < 3 ? U8(0x52405 + kind) : -1;

    /// <summary>마을 꼴 kind 에서 건물 n5(0 주점 · 1 무기점 · 2 출구 · 3 도구점 · 4 교회 · 5 ???) 앞 사람 자리 — 그림 점(x 0x52363 · y 0x52375).</summary>
    public (int X, int Y) TownSpot(int kind, int n5) =>
        kind is >= 0 and < 3 && n5 is >= 0 and < 6 ? (U8(0x52363 + 6 * kind + n5), U8(0x52375 + 6 * kind + n5)) : (0, 0);

    public ShopInfo? ShopFor(int scene) => HasTown(scene) ? Shops[scene - 1] : null;

    // ── 이동 대본 ──────────────────────────────────────────────────────────────

    public const int MoveScripts = 106;

    /// <summary>
    /// 이동 대본 k(0x627D8 의 u32 표 — 파일에는 오브젝트 3 안 자리로 들어 있다). u8 걸음 수, 걸음마다 u8 r · u8 m · m × (u8 유닛, u8 쪽).
    /// r &lt; 0x80 이면 모두 함께 r 칸, r ≥ 0x80 이면 (r &amp; 0x7F) 그림 쉬기. 없으면 빈 목록.
    /// </summary>
    public List<MoveStep> MoveScript(int k)
    {
        var steps = new List<MoveStep>();
        if (k < 0 || k >= MoveScripts) return steps;
        int p = S32(0x627D8 + 4 * k);
        if (p < 0 || p >= 0x34D2) return steps;
        int q = 0x60000 + p;
        int n = U8(q++);
        for (int s = 0; s < n; s++)
        {
            int r = U8(q), m = U8(q + 1);
            q += 2;
            var units = new (int, Dir)[m];
            for (int j = 0; j < m; j++, q += 2) units[j] = (U8(q), (Dir)(U8(q + 1) & 3));
            steps.Add(new MoveStep(r & 0x7F, r >= 0x80, units));
        }
        return steps;
    }

    // ── 읽기 ─────────────────────────────────────────────────────────────────

    private static int FileOffset(int va) => va >= 0x60000 ? va + 0x19014 : va + 0x25014;

    public byte U8(int va) => _exe[FileOffset(va)];
    public ushort U16(int va) => BinaryPrimitives.ReadUInt16LittleEndian(_exe.AsSpan(FileOffset(va)));
    public short S16(int va) => BinaryPrimitives.ReadInt16LittleEndian(_exe.AsSpan(FileOffset(va)));
    public int S32(int va) => BinaryPrimitives.ReadInt32LittleEndian(_exe.AsSpan(FileOffset(va)));
    public byte[] Bytes(int va, int n) => _exe.AsSpan(FileOffset(va), n).ToArray();
}

/// <summary>
/// 이름 · 물건 글 표 — FDTXT 항목 0(661 줄, 줄마다 원본 글꼴 번호, FFFF 끝). 캐릭터 c → 줄 c+1, 종족 r → 140+r, 직업 k → 150+k,
/// 물건 i → 181+i, 마법 m → 441+m, 승패 글 597 + 2n · 598 + 2n. 글자는 원본 글꼴(FDOTHER 4) 번호라 그대로 그려 보인다.
/// </summary>
public sealed class NameTable
{
    private readonly List<int[]> _lines;

    public NameTable(string dir)
    {
        var t = Dat.Load(Path.Combine(dir, "FDTXT.DAT"), 0);
        int n = Dat.U16(t, 0) / 2;
        _lines = new List<int[]>(n);
        for (int i = 0; i < n; i++)
        {
            int from = Dat.U16(t, 2 * i), to = i + 1 < n ? Dat.U16(t, 2 * i + 2) : t.Length;
            var line = new List<int>();
            for (int p = from; p + 1 < to; p += 2)
            {
                int c = Dat.U16(t, p);
                if (c == 0xFFFF) break;
                if (c < 0xFF00) line.Add(c);
            }
            _lines.Add(line.ToArray());
        }
    }

    public int Count => _lines.Count;
    public int[] Line(int i) => i >= 0 && i < _lines.Count ? _lines[i] : [];
    public int[] Character(int id) => Line(id + 1);
    public int[] Race(int race) => Line(140 + race);
    public int[] Class(int cls) => Line(150 + cls);
    public int[] Item(int item) => Line(181 + item);
    public int[] Spell(int spell) => Line(441 + spell);
}

/// <summary>마법 이름 — 이름 표(FDTXT 0 의 441 + 번호)를 원본 글꼴로 그려 읽은 것(분석-마법). 31 번은 원본에서도 빈 줄이다.</summary>
public static class SpellNames
{
    private static readonly string[] Names =
    [
        "화염술", "열염술", "염룡술", "천화술", "뇌격술", "낙뇌술", "횡뇌술", "신뇌술", "성광탄", "주술",
        "파암술", "지진술", "열지술", "치료술", "회복술", "재생술", "신은술", "공격술", "방어술", "속도술",
        "해독술", "치마술", "봉쇄술", "전송술", "파룡술", "행동술", "시독술", "마비술", "참혹술", "치열도",
        "암속인", "(이름 없음)", "치천사", "풍요정", "파괴신", "사악귀",
    ];

    public static string Of(int spell) => spell >= 0 && spell < Names.Length ? Names[spell] : $"마법 {spell}";
}
