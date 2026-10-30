using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 용의기사2(FD2) 자료 파일 읽개. 설치 폴더의 파일을 그 자리에서 읽는다.
/// </summary>
/// <remarks>
/// <para><c>*.DAT</c> 는 모두 <c>"LLLLLL"</c> + u32 오프셋 표. 첫 오프셋이 곧 표의 끝이라 칸 수는 <c>(off[0] - 6) / 4</c>,
/// 마지막 칸은 파일 길이(끝 표시)라 항목 수는 그보다 하나 적다. i 번째 항목은 <c>off[i]..off[i+1]</c>.</para>
/// <para>장면 n 은 FDFIELD 3n(지도) · 3n+1(장면 정보·유닛 표) · 3n+2(좌표),
/// 타일은 FDSHAP 2t(그림) · 2t+1(타일 속성, 4바이트씩). t 는 장면 정보 첫 바이트.</para>
/// </remarks>
public static class Dat
{
    public static byte[] Load(string path, int index)
    {
        using var fs = File.OpenRead(path);
        Span<byte> two = stackalloc byte[8];
        fs.Seek(6 + 4 * index, SeekOrigin.Begin);
        fs.ReadExactly(two);
        uint a = BinaryPrimitives.ReadUInt32LittleEndian(two), b = BinaryPrimitives.ReadUInt32LittleEndian(two[4..]);
        var data = new byte[b - a];
        fs.Seek(a, SeekOrigin.Begin);
        fs.ReadExactly(data);
        return data;
    }

    /// <summary>항목 수 — 오프셋 칸 수에서 끝 표시 칸 하나를 뺀다(원본 읽개 sub_111BA 는 표 크기를 보지 않는다).</summary>
    public static int Count(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> four = stackalloc byte[4];
        fs.Seek(6, SeekOrigin.Begin);
        fs.ReadExactly(four);
        return ((int)BinaryPrimitives.ReadUInt32LittleEndian(four) - 6) / 4 - 1;
    }

    public static ushort U16(byte[] d, int at) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at));
    public static int U32(byte[] d, int at) => (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at));
}

/// <summary>VGA 6비트 팔레트 256색을 BGRA 로.</summary>
public static class Palette
{
    public static uint[] FromVga(byte[] raw)
    {
        var pal = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            uint r = Six(raw[3 * i]), g = Six(raw[3 * i + 1]), b = Six(raw[3 * i + 2]);
            pal[i] = 0xFF000000 | (r << 16) | (g << 8) | b;
        }
        return pal;
    }

    private static uint Six(byte v) { v &= 63; return (uint)((v << 2) | (v >> 4)); }
}

/// <summary>
/// 원본 RLE(sub_4E98D · 24x24 조각은 sub_4E22A). 지도 타일 · FDICON.B24 캐릭터 · FIGANI 전투 모션이 같은 꼴이다.
/// </summary>
/// <remarks>
/// 코드 바이트 <c>ttnnnnnn</c>, 개수 n+1. <c>11</c> 건너뜀(투명), <c>10</c> 그대로 복사,
/// <c>00</c> 다음 바이트로 채움, <c>01</c> 다음 바이트를 한 칸 걸러(홀수 자리에) 찍고 2n 칸 나아감.
/// 결과는 BGRA, 투명은 0.
/// </remarks>
public static class Rle
{
    public const int Tile = 24;

    /// <summary>머리 없는 24x24 조각.</summary>
    public static uint[] DecodeTile(byte[] src, int p, uint[] pal) => Decode(src, p, Tile, Tile, pal);

    /// <summary>w x h 그림을 풀어 BGRA 로.</summary>
    public static uint[] Decode(byte[] src, int p, int w, int h, uint[] pal)
    {
        var dst = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * w, x = 0;
            while (x < w)
            {
                int v = src[p++], n = (v & 0x3F) + 1;
                switch (v >> 6)
                {
                    case 3:
                        x += n;
                        break;
                    case 2:
                        for (int k = 0; k < n; k++) Put(dst, w, row, x + k, pal[src[p++]]);
                        x += n;
                        break;
                    case 0:
                        uint c = pal[src[p++]];
                        for (int k = 0; k < n; k++) Put(dst, w, row, x + k, c);
                        x += n;
                        break;
                    default:
                        uint a = pal[src[p++]];
                        for (int k = 0; k < n; k++) Put(dst, w, row, x + 1 + 2 * k, a);
                        x += 2 * n;
                        break;
                }
            }
        }
        return dst;
    }

    private static void Put(uint[] dst, int w, int row, int x, uint c)
    {
        if (x < w) dst[row + x] = c;
    }
}

/// <summary>캐릭터 그림 FDICON.B24 — 캐릭터마다 12장(아래·왼쪽·위·오른쪽 × 걸음 3장).</summary>
public sealed class IconBank
{
    public const int FramesPerChar = 12;

    private readonly byte[] _file;
    private readonly uint[] _pal;
    private readonly Dictionary<int, uint[]> _cache = new();

    public int Characters { get; }

    /// <summary>
    /// 왕 · 왕비 그림을 57 · 75 로 옮긴 판인가(모션패치) — 원본은 57 이 빈 그림이고 왕은 48 이다.
    /// </summary>
    public bool RoyalsMoved { get; }

    public IconBank(string path, uint[] pal)
    {
        _file = File.ReadAllBytes(path);
        _pal = pal;
        Characters = Dat.U16(_file, 4) / FramesPerChar;
        RoyalsMoved = Characters > 57 && Frame(57, 0).Any(c => c != 0);
    }

    public uint[] Frame(int character, int frame)
    {
        int i = character * FramesPerChar + frame;
        if (!_cache.TryGetValue(i, out var px))
            _cache[i] = px = Rle.DecodeTile(_file, Dat.U32(_file, 6 + 4 * i), _pal);
        return px;
    }
}

public enum Side { Player, Other }

/// <summary>지도 위의 한 사람.</summary>
/// <remarks>전투 칸은 원본 유닛 기록(80바이트, 분석-전투 1절)을 따른다 — 채우는 것은 <see cref="Battle"/>.</remarks>
public sealed class Unit
{
    public required int Icon;
    public required Side Side;
    public int X, Y;
    public Dir Facing = Dir.Down;

    /// <summary>쓰러져 누워 있다 — 옆모습을 눕혀 그린다(프롤로그의 유니).</summary>
    public bool Lying;

    /// <summary>레벨(+33). 장면 정보의 레벨(+4), 우리 편은 동료 표의 합류 레벨.</summary>
    public int Level = 1;

    /// <summary>꼴(+7) — 표를 찾는 캐릭터 번호(0~67 동료 꼴, 68~ 몬스터). 그림(Icon)은 판에 따라 옮겨 그릴 수 있어 따로 둔다. -1 이면 Icon.</summary>
    public int Form = -1;

    /// <summary>처음 캐릭터 번호(+8) — 이름 · 대사 FFEE/FFEF 의 사람 찾기. -1 이면 Form.</summary>
    public int Id = -1;

    /// <summary>전투 배열 번호 — 우리 편 출전자가 0 부터, 그다음 유닛 표에서 나온 차례(승패 판정 · 이동 대본이 이 번호를 쓴다). -1 은 배열 밖.</summary>
    public int Slot = -1;

    /// <summary>장면 유닛 표에서 나왔으면 그 줄(장비 · 마법 · AI · 떨굼).</summary>
    public RosterEntry? Entry;

    public int FormId => Form >= 0 ? Form : Icon;
    public int NameId => Id >= 0 ? Id : FormId;

    // 전투 — Battle 이 채운다
    /// <summary>Hp · MaxHp(+64 · +66), Mp · MaxMp(+68 · +70), Atk · Def · Hit · Evade = 셈한 공격 · 방어 · 명중 · 회피(+72 ~ +78).</summary>
    public int Hp, MaxHp, Mp, MaxMp, Atk, Def, Hit, Evade, Move = 4, RangeMin = 1, RangeMax = 1;

    /// <summary>바탕 공격(역량 +55) · 바탕 방어(내력 +57) · 속도(+62).</summary>
    public int BaseAtk, BaseDef, Speed;

    /// <summary>종족(+31) · 직업(+32).</summary>
    public int Race, Class;

    /// <summary>경험치(+60) 0~99, 적 · 컴퓨터 편은 255.</summary>
    public int Exp;

    /// <summary>가방 8칸(+10 ~ +25) — 물건 번호와 장비 중인가. 무기는 장비 중인 첫 번호 &lt; 128, 옷은 128 이상.</summary>
    public readonly List<(int Item, bool Equipped)> Bag = new();

    /// <summary>아는 마법 비트(+26 ~ +30, 40개) — 마법 m = 비트 m.</summary>
    public ulong Spells;

    /// <summary>상태(+34 ~ +39) 남은 차례 — 0 공격↑ · 1 방어↑ · 2 속도↑ · 3 독 · 4 마비 · 5 봉쇄.</summary>
    public readonly int[] Status = new int[6];

    /// <summary>AI(+52: 아래 4비트 갈래, 0x40 공격 좋아함, 0x80 아끼는 유닛) · 인자(+53 · +54) · 상자 번호(+61).</summary>
    public int AiByte, AiX, AiY, Chest;

    /// <summary>쓰러지면 주는 것(+49 · +50): 0 물건 · 1 돈 · 2 사건 · 3 대사 묶음, 255 없음.</summary>
    public int DropKind = 255, DropValue;

    public bool Acted;

    /// <summary>컴퓨터가 움직이는 우리 편(장면 정보의 편 1 — 1장의 해양순찰대 같은).</summary>
    public bool Ai;

    /// <summary>원본의 편(+6): 0 적 · 1 컴퓨터 아군 · 2 우리 편.</summary>
    public int Team => Side == Side.Other ? 0 : Ai ? 1 : 2;

    /// <summary>쓰러지지 않았나 — 전투 능력치가 없는(전투 밖) 사람은 늘 서 있다.</summary>
    public bool Alive => Hp > 0 || MaxHp == 0;

    public bool Poisoned => Status[3] > 0;
    public bool Paralyzed => Status[4] > 0;
    public bool Sealed => Status[5] > 0;

    // 칸 사이를 걷는 중이면 0..1 로 나아간다
    public int FromX, FromY;
    public double Step = 1;
    public readonly Queue<(int X, int Y)> Path = new();

    public bool Moving => Step < 1;
    public double DrawX => FromX + (X - FromX) * Step;
    public double DrawY => FromY + (Y - FromY) * Step;
}

public enum Dir { Down = 0, Left = 1, Up = 2, Right = 3 }

/// <summary>
/// 장면 정보의 유닛 한 줄(26바이트, sub_10C50) — 편(0 적 · 1 컴퓨터 아군 · 2 합류하는 아군), 캐릭터 번호, 레벨, 등장 차례, 자리.
/// Raw 의 나머지: +2 상자 번호(+61), +5 · +6 무기 · 옷(+5 = 255 면 +6 하나만), +7 ~ +12 물건 6칸, +13 ~ +16 마법 비트,
/// +17 ~ +19 AI(+52 ~ +54), +22 떨굼 종류 · +23 u16 값.
/// </summary>
public sealed record RosterEntry(int Type, int Icon, int Level, int Phase, int X, int Y)
{
    public byte[] Raw { get; init; } = new byte[26];

    public int Chest => Raw[2];
    public int Weapon => Raw[5];
    public int Armor => Raw[6];
    public IEnumerable<int> Items => Raw.Skip(7).Take(6).Where(b => b != 255).Select(b => (int)b);
    public uint Spells => (uint)(Raw[13] | Raw[14] << 8 | Raw[15] << 16 | Raw[16] << 24);
    public int AiByte => Raw[17];
    public int AiX => Raw[18];
    public int AiY => Raw[19];
    public int DropKind => Raw[22];
    public int DropValue => Raw[23] | Raw[24] << 8;
}

/// <summary>장면 하나 — 지도·타일·속성·처음 배치.</summary>
public sealed class Scene
{
    public int Index { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileSet { get; }

    private readonly ushort[] _cells;
    private readonly uint[][] _tiles;
    private readonly byte[] _attr;

    public List<Unit> Units { get; } = new();

    private readonly int _iconCount;

    /// <summary>다음에 세울 사람의 전투 배열 번호.</summary>
    private int _nextSlot;
    private readonly bool _royalsMoved;

    /// <summary>장면 정보의 유닛 표 전부(등장 차례 상관없이).</summary>
    public List<RosterEntry> Roster { get; } = new();

    public Scene(string dir, int index, uint[] pal, IconBank icons)
    {
        Index = index;
        _iconCount = icons.Characters;
        _royalsMoved = icons.RoyalsMoved;
        string field = Path.Combine(dir, "FDFIELD.DAT"), shap = Path.Combine(dir, "FDSHAP.DAT");
        var map = Dat.Load(field, 3 * index);
        var info = Dat.Load(field, 3 * index + 1);
        var pos = Dat.Load(field, 3 * index + 2);

        Width = (short)Dat.U16(map, 0);
        Height = (short)Dat.U16(map, 2);
        _cells = new ushort[Width * Height];
        for (int i = 0; i < _cells.Length; i++) _cells[i] = (ushort)(Dat.U16(map, 4 + 4 * i) & 0x3FF);

        TileSet = info[0];
        var tiles = Dat.Load(shap, 2 * TileSet);
        _attr = Dat.Load(shap, 2 * TileSet + 1);
        _tiles = new uint[Dat.U16(tiles, 4)][];
        for (int i = 0; i < _tiles.Length; i++) _tiles[i] = Rle.DecodeTile(tiles, Dat.U32(tiles, 6 + 4 * i), pal);

        // 장면 정보: [1] 우리 편 자리 수, [2] 그 밖의 유닛 수, 131 부터 유닛마다 26바이트
        // (+1 그림 번호, +21 등장 차례 — 0 이 처음부터 나와 있는 것).
        // 좌표 파일은 2 부터 6바이트씩 (+0 x, +2 y): 그 밖의 유닛들 다음에 우리 편.
        int players = info[1], others = info[2];
        for (int i = 0; i < others; i++)
        {
            int u = 131 + 26 * i;
            if (u + 26 > info.Length || 4 + 6 * i >= pos.Length) break;
            // 그림 번호가 없는 줄도 넣어 둔다 — 대사의 FFEC · FFED 가 이 표의 차례 번호로 말하는 사람을 가리킨다
            int icon = info[u + 1];
            Roster.Add(new RosterEntry(info[u], icon, Math.Max(1, (int)info[u + 4]), info[u + 21], pos[2 + 6 * i], pos[4 + 6 * i])
            {
                Raw = info.AsSpan(u, 26).ToArray(),
            });
        }

        // 원본(sub_1088D → sub_10C50)처럼 우리 편 출전자를 먼저(배열 번호 0 ~ P-1) 세우고, 그다음 유닛 표의 처음부터 나와 있는 줄.
        // 우리 편 그림은 원본에서는 저장 자료가 정한다 — 여기서는 PartyOrder 차례로, 이 장면에서 나중에 합류하는 아군(편 2)은 건너뛴다.
        var joiners = Roster.Where(e => e.Type == 2).Select(e => e.Icon).ToHashSet();
        var party = PartyOrder.Concat(Enumerable.Range(0, CharacterNames.Heroes)).Distinct().Where(i => !joiners.Contains(i) && i < icons.Characters).ToList();
        for (int k = 0; k < players && k < party.Count; k++)
        {
            int p = 2 + 6 * (others + k);
            if (p + 3 > pos.Length) break;
            Place(new Unit { Icon = party[k], Form = party[k], Side = Side.Player, Facing = Dir.Up, Slot = _nextSlot++ }, pos[p], pos[p + 2]);
        }
        foreach (var e in Roster.Where(e => e.Phase == 0)) Spawn(e);
    }

    /// <summary>
    /// 우리 편 자리를 채우는 차례 — 프롤로그 끝에 원본(sub_3231B)이 넣는 사울(0) · 유니(9) · 아래스(4) · 카일(30), 그다음 마린(회복) …
    /// 원본은 저장 자료(합류 차례)가 정하므로 다섯째부터는 정한 것이다.
    /// </summary>
    public static readonly int[] PartyOrder = [0, 9, 4, 30, 10, 2, 5, 6, 7, 8, 11];

    /// <summary>
    /// 유닛 표의 한 줄을 지도에 세운다 — 편 2 는 합류하는 우리 편, 편 1 은 컴퓨터가 움직이는 우리 편, 편 0 은 적.
    /// (1장 대사로 맞춰 본 뜻.) 놓을 자리가 없으면 null.
    /// </summary>
    public Unit? Spawn(RosterEntry e)
    {
        int slot = _nextSlot++;   // 원본은 늘 배열 뒤에 붙인다 — 못 세워도 번호는 넘긴다
        int sprite = CharacterNames.SpriteOf(e.Icon, _royalsMoved);
        if (sprite >= _iconCount) return null;
        var u = new Unit
        {
            Icon = sprite, Form = e.Icon, Side = e.Type is 1 or 2 ? Side.Player : Side.Other, Ai = e.Type == 1, Level = e.Level,
            Slot = slot, Entry = e,
        };
        Place(u, e.X, e.Y);
        return Units.Contains(u) ? u : null;
    }

    /// <summary>같은 칸에 여럿이 겹치면(입장 연출 자리) 가장 가까운 빈 칸으로 — 원본 sub_10C50 처럼.</summary>
    private void Place(Unit u, int x, int y)
    {
        if (!Free(x, y))
        {
            int best = int.MaxValue;
            for (int yy = 0; yy < Height; yy++)
                for (int xx = 0; xx < Width; xx++)
                {
                    int d = Math.Abs(xx - x) + Math.Abs(yy - y);
                    if (d < best && Free(xx, yy)) { best = d; (u.X, u.Y) = (xx, yy); }
                }
            if (best == int.MaxValue) return;
        }
        else (u.X, u.Y) = (x, y);
        (u.FromX, u.FromY) = (u.X, u.Y);
        Units.Add(u);
    }

    /// <summary>지도 전체를 한 장으로(Width·24 x Height·24, BGRA). 사람은 안 그린다.</summary>
    public uint[] RenderMap()
    {
        int w = Width * Rle.Tile;
        var px = new uint[w * Height * Rle.Tile];
        for (int ty = 0; ty < Height; ty++)
            for (int tx = 0; tx < Width; tx++)
            {
                var tile = TileAt(tx, ty);
                for (int y = 0; y < Rle.Tile; y++)
                    tile.AsSpan(y * Rle.Tile, Rle.Tile).CopyTo(px.AsSpan((ty * Rle.Tile + y) * w + tx * Rle.Tile));
            }
        for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000;
        return px;
    }

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public uint[] TileAt(int x, int y) => _tiles[Math.Min(_cells[y * Width + x], _tiles.Length - 1)];

    /// <summary>
    /// 걸어서 갈 수 있나 — 타일 속성 둘째 바이트(지형 종류 0~5)가 1(날아서만 — 물 · 집 · 바위) · 5(아무도 못 감)가 아닌 칸.
    /// 전투의 이동은 직업별 이동비 표를 쓴다(Battle).
    /// </summary>
    public bool Walkable(int x, int y) => Inside(x, y) && Ground(x, y) is not (1 or 5);

    /// <summary>지형 종류 — 타일 속성 둘째 바이트: 0 평지 · 1 날아서만 · 2 숲 · 3 깊은 숲 · 4 물 · 늪 · 5 못 감(이름은 그림을 보고 붙인 짐작).</summary>
    public int Ground(int x, int y) => Inside(x, y) ? _attr[4 * _cells[y * Width + x] + 1] : 1;

    public Unit? UnitAt(int x, int y) => Units.Find(u => u.X == x && u.Y == y && u.Alive);

    public bool Free(int x, int y) => Walkable(x, y) && UnitAt(x, y) == null;

    /// <summary>지형 — 타일 속성 셋째 바이트(4 풀밭 · 5 숲 · 6 모래 · 0 물 …). 원본은 이것을 전투 배경 BG.DAT 번호로 쓴다(sub_12E38).</summary>
    public int Terrain(int x, int y) => Inside(x, y) ? _attr[4 * _cells[y * Width + x] + 2] : 0;

    /// <summary>보통 직업의 지형 종류 0~5 이동비(원본 표 0x61646 의 검사 줄) — 표가 없을 때.</summary>
    public static readonly int[] DefaultMoveCost = [1, 20, 1, 2, 2, 20];

    /// <summary>이 칸에 들어가는 데 드는 이동력 — 지형 종류(0~5)로 이동비 표 costs 를 찾는다(20 = 사실상 못 감). 지도 밖은 -1.</summary>
    public int MoveCost(int x, int y, int[]? costs = null)
    {
        if (!Inside(x, y)) return -1;
        int g = Ground(x, y);
        costs ??= DefaultMoveCost;
        return g < costs.Length ? costs[g] : 20;
    }

    /// <summary>
    /// 이동 범위(sub_4E390 · sub_4E4BE) — 칸마다 (남은 이동력이 가장 큰) 앞 칸. 칸마다 이동비 표 costs(직업 줄)로 깎는다.
    /// zoc 면 원본처럼 상대 편(편 0 ↔ 편 1 · 2) 칸은 못 들어가고, 그 위아래양옆 칸에 들어가면 거기서 멈춘다(남은 이동력 0).
    /// zoc 가 아니면 다른 편이 선 칸만 막는다. 같은 편은 지나가되 멈출 수는 없다 — 멈출 수 있는 칸만은 <see cref="Stops"/>.
    /// </summary>
    public Dictionary<(int X, int Y), (int X, int Y)> Reach(Unit u, int[]? costs = null, bool zoc = false)
    {
        bool Opponent(Unit o) => zoc ? o.Team == 0 != (u.Team == 0) : o.Side != u.Side;
        var foes = Units.Where(o => o != u && o.Alive && Opponent(o)).Select(o => (o.X, o.Y)).ToHashSet();
        var best = new Dictionary<(int, int), int> { [(u.X, u.Y)] = u.Move };
        var prev = new Dictionary<(int X, int Y), (int X, int Y)> { [(u.X, u.Y)] = (u.X, u.Y) };
        var q = new PriorityQueue<(int X, int Y), int>();
        q.Enqueue((u.X, u.Y), -u.Move);
        ReadOnlySpan<(int, int)> dirs = [(0, 1), (-1, 0), (0, -1), (1, 0)];
        while (q.TryDequeue(out var c, out int negLeft))
        {
            int left = -negLeft;
            if (best[c] != left) continue;
            foreach (var (dx, dy) in dirs)
            {
                var n = (c.X + dx, c.Y + dy);
                int cost = MoveCost(n.Item1, n.Item2, costs);
                if (cost < 0 || left - cost < 0) continue;
                if (foes.Contains(n)) continue;
                int remain = left - cost;
                if (zoc && remain > 0 && (foes.Contains((n.Item1 + 1, n.Item2)) || foes.Contains((n.Item1 - 1, n.Item2))
                                          || foes.Contains((n.Item1, n.Item2 + 1)) || foes.Contains((n.Item1, n.Item2 - 1))))
                    remain = 0;   // 상대 곁 — 들어가면 멈춘다
                if (best.TryGetValue(n, out int had) && had >= remain) continue;
                best[n] = remain;
                prev[n] = c;
                q.Enqueue(n, -remain);
            }
        }
        return prev;
    }

    /// <summary>Reach 에서 멈출 수 있는 칸 — 다른 사람이 서 있지 않은 칸(자기 자리는 된다).</summary>
    public IEnumerable<(int X, int Y)> Stops(Dictionary<(int X, int Y), (int X, int Y)> reach, Unit u) =>
        reach.Keys.Where(k => k == (u.X, u.Y) || UnitAt(k.X, k.Y) == null);

    /// <summary>Reach 의 앞 칸 표로 (x, y) 까지 가는 길(출발 칸 빼고).</summary>
    public static List<(int X, int Y)> PathTo(Dictionary<(int X, int Y), (int X, int Y)> reach, (int X, int Y) to)
    {
        var path = new List<(int X, int Y)>();
        for (var c = to; reach[c] != c; c = reach[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    /// <summary>너비 우선으로 가장 짧은 길. 도착 칸은 비어 있어야 한다.</summary>
    public List<(int X, int Y)>? FindPath(int sx, int sy, int tx, int ty)
    {
        if (!Free(tx, ty)) return null;
        var prev = new int[Width * Height];
        Array.Fill(prev, -1);
        var q = new Queue<int>();
        int start = sy * Width + sx, goal = ty * Width + tx;
        prev[start] = start;
        q.Enqueue(start);
        ReadOnlySpan<(int, int)> dirs = [(0, 1), (-1, 0), (0, -1), (1, 0)];
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            if (c == goal) break;
            int cx = c % Width, cy = c / Width;
            foreach (var (dx, dy) in dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!Free(nx, ny)) continue;
                int n = ny * Width + nx;
                if (prev[n] != -1) continue;
                prev[n] = c;
                q.Enqueue(n);
            }
        }
        if (prev[goal] == -1) return null;
        var path = new List<(int, int)>();
        for (int c = goal; c != start; c = prev[c]) path.Add((c % Width, c / Width));
        path.Reverse();
        return path;
    }
}
