using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 용의기사2(FD2) 자료 파일 읽개. 설치 폴더의 파일을 그 자리에서 읽는다.
/// </summary>
/// <remarks>
/// <para><c>*.DAT</c> 는 모두 <c>"LLLLLL"</c> + u32 오프셋 표. 첫 오프셋이 곧 표의 끝이라
/// 항목 수는 <c>(off[0] - 6) / 4</c>, i 번째 항목은 <c>off[i]..off[i+1]</c>.</para>
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

    public static int Count(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> four = stackalloc byte[4];
        fs.Seek(6, SeekOrigin.Begin);
        fs.ReadExactly(four);
        return ((int)BinaryPrimitives.ReadUInt32LittleEndian(four) - 6) / 4;
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

    public IconBank(string path, uint[] pal)
    {
        _file = File.ReadAllBytes(path);
        _pal = pal;
        Characters = Dat.U16(_file, 4) / FramesPerChar;
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
public sealed class Unit
{
    public required int Icon;
    public required Side Side;
    public int X, Y;
    public Dir Facing = Dir.Down;

    // 칸 사이를 걷는 중이면 0..1 로 나아간다
    public int FromX, FromY;
    public double Step = 1;
    public readonly Queue<(int X, int Y)> Path = new();

    public bool Moving => Step < 1;
    public double DrawX => FromX + (X - FromX) * Step;
    public double DrawY => FromY + (Y - FromY) * Step;
}

public enum Dir { Down = 0, Left = 1, Up = 2, Right = 3 }

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

    public Scene(string dir, int index, uint[] pal, IconBank icons)
    {
        Index = index;
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
            if (u + 26 > info.Length || info[u + 21] != 0) continue;
            int icon = info[u + 1];
            if (icon >= icons.Characters) continue;
            Place(new Unit { Icon = icon, Side = Side.Other }, pos[2 + 6 * i], pos[4 + 6 * i]);
        }
        for (int k = 0; k < players; k++)
        {
            int p = 2 + 6 * (others + k);
            if (p + 3 > pos.Length) break;
            Place(new Unit { Icon = k % icons.Characters, Side = Side.Player, Facing = Dir.Up }, pos[p], pos[p + 2]);
        }
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

    /// <summary>타일 속성 둘째 바이트: 1 못 감(물·집·통나무), 2 숲.</summary>
    public bool Walkable(int x, int y) => Inside(x, y) && _attr[4 * _cells[y * Width + x] + 1] != 1;

    public Unit? UnitAt(int x, int y) => Units.Find(u => u.X == x && u.Y == y);

    public bool Free(int x, int y) => Walkable(x, y) && UnitAt(x, y) == null;

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
