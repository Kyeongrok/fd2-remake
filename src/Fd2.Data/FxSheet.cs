using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace Fd2.Data;

/// <summary>
/// 바깥 이펙트 시트(pixelfx 의 <c>render.mjs --sheet</c> 꼴 — 투명 PNG 한 장에 컷을 격자로, Aseprite 꼴 JSON 에 컷 자리 · 시간 · 발 자리).
/// 지도 위 마법 그림을 원작 대신 이것으로 그릴 때 쓴다(w-158 · w-159). 색은 알파가 곱해지지 않은 BGRA.
/// </summary>
public sealed class FxSheet
{
    public required List<(uint[] Px, int W, int H)> Frames { get; init; }

    /// <summary>컷마다 보이는 때(ms).</summary>
    public required List<int> Durations { get; init; }

    /// <summary>발 자리(컷 안의 점) — 대상 칸의 아래 가운데에 맞춘다.</summary>
    public int PivotX { get; init; }
    public int PivotY { get; init; }

    public int TotalMs => Durations.Sum();

    private int _half = -1, _above = -1;

    /// <summary>발 자리에서 위로 가장 높이 보이는 점까지(모든 컷) — 원작 이펙트 높이에 맞춰 키를 맞출 때(w-161).</summary>
    public int Above
    {
        get
        {
            if (_above >= 0) return _above;
            int top = PivotY;
            foreach (var (px, w, h) in Frames)
                for (int y = 0; y < Math.Min(h, top); y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x] >> 24 > 16) { top = Math.Min(top, y); break; }
            return _above = Math.Max(1, PivotY - top);
        }
    }

    /// <summary>발 자리에서 좌우로 가장 멀리 보이는 점까지(모든 컷, 거의 안 보이는 알파 16 이하는 빼고) — 칸 너비에 맞춰 줄일 때 쓴다(fb-86).</summary>
    public int HalfWidth
    {
        get
        {
            if (_half >= 0) return _half;
            int half = 0;
            foreach (var (px, w, h) in Frames)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x] >> 24 > 16) half = Math.Max(half, x < PivotX ? PivotX - x : x + 1 - PivotX);
            return _half = Math.Max(1, half);
        }
    }

    /// <summary>발 자리에서 그 방향으로 가장 멀리 보이는 점까지 — 0 위 · 1 오른쪽 · 2 아래 · 3 왼쪽(<see cref="FxDir"/>).</summary>
    public int Reach(FxDir dir)
    {
        if (_box is not { } b)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (var (px, w, h) in Frames)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x] >> 24 > 16) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x + 1); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y + 1); }
            if (x0 == int.MaxValue) (x0, y0, x1, y1) = (PivotX, PivotY, PivotX + 1, PivotY + 1);
            _box = b = (x0, y0, x1, y1);
        }
        return Math.Max(1, dir switch
        {
            FxDir.Up => PivotY - b.Y0,
            FxDir.Down => b.Y1 - PivotY,
            FxDir.Left => PivotX - b.X0,
            _ => b.X1 - PivotX,
        });
    }

    private (int X0, int Y0, int X1, int Y1)? _box;

    /// <summary>모든 컷에서 보이는 점(알파 16 넘음)을 감싸는 상자.</summary>
    public (int X0, int Y0, int X1, int Y1) Box { get { Reach(FxDir.Up); return _box!.Value; } }
    private readonly Dictionary<FxDir, FxSheet> _turned = new();

    /// <summary>
    /// 위로 자라는 그림(발 아래 가운데)을 그 방향으로 돌린 시트 — 왼쪽은 시계 반대로 90도, 오른쪽은 시계로 90도, 아래는 180도(w-162).
    /// 발 자리도 같이 돌린다. 위면 그대로.
    /// </summary>
    public FxSheet Turned(FxDir dir)
    {
        if (dir == FxDir.Up) return this;
        lock (_turned)
        {
            if (_turned.TryGetValue(dir, out var t)) return t;
            var frames = new List<(uint[], int, int)>(Frames.Count);
            foreach (var (px, w, h) in Frames)
            {
                bool side = dir is FxDir.Left or FxDir.Right;
                int nw = side ? h : w, nh = side ? w : h;
                var o = new uint[nw * nh];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var (nx, ny) = Map(dir, x, y, w, h);
                        o[ny * nw + nx] = px[y * w + x];
                    }
                frames.Add((o, nw, nh));
            }
            var (pw, ph) = (Frames[0].W, Frames[0].H);
            var (qx, qy) = Map(dir, Math.Clamp(PivotX, 0, pw - 1), Math.Clamp(PivotY, 0, ph - 1), pw, ph);
            return _turned[dir] = new FxSheet { Frames = frames, Durations = Durations, PivotX = qx, PivotY = qy };
        }
    }

    private static (int X, int Y) Map(FxDir dir, int x, int y, int w, int h) => dir switch
    {
        FxDir.Left => (y, w - 1 - x),          // 위 → 왼쪽(시계 반대로 90도)
        FxDir.Right => (h - 1 - y, x),         // 위 → 오른쪽(시계로 90도)
        FxDir.Down => (w - 1 - x, h - 1 - y),  // 180도
        _ => (x, y),
    };

    /// <summary>t ms 째의 컷 번호(끝을 넘으면 -1).</summary>
    public int FrameAt(double ms)
    {
        for (int i = 0; i < Durations.Count; i++)
        {
            if (ms < Durations[i]) return i;
            ms -= Durations[i];
        }
        return -1;
    }

    public static FxSheet? Load(byte[] png, string json)
    {
        try
        {
            var (px, w, h) = Png.Decode(png);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var frames = new List<(uint[], int, int)>();
            var times = new List<int>();
            IEnumerable<JsonElement> list = root.GetProperty("frames") is { ValueKind: JsonValueKind.Array } arr
                ? arr.EnumerateArray() : root.GetProperty("frames").EnumerateObject().Select(p => p.Value);
            foreach (var f in list)
            {
                var r = f.GetProperty("frame");
                int fx = r.GetProperty("x").GetInt32(), fy = r.GetProperty("y").GetInt32(), fw = r.GetProperty("w").GetInt32(), fh = r.GetProperty("h").GetInt32();
                var cut = new uint[fw * fh];
                for (int y = 0; y < fh && fy + y < h; y++)
                    for (int x = 0; x < fw && fx + x < w; x++) cut[y * fw + x] = px[(fy + y) * w + fx + x];
                frames.Add((cut, fw, fh));
                times.Add(f.TryGetProperty("duration", out var d) ? Math.Max(1, d.GetInt32()) : 100);
            }
            if (frames.Count == 0) return null;
            int pvx = frames[0].Item2 / 2, pvy = frames[0].Item3;
            if (root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("pixelfx", out var pf) && pf.TryGetProperty("pivot", out var pv) && pv.GetArrayLength() == 2)
                (pvx, pvy) = (pv[0].GetInt32(), pv[1].GetInt32());
            return new FxSheet { Frames = frames, Durations = times, PivotX = pvx, PivotY = pvy };
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentException) { return null; }
    }
}

/// <summary>가장 작은 PNG 읽개 — 8비트 RGBA(6) · RGB(2), 엮지 않은(non-interlaced) 것만. BGRA(알파 곱하지 않음)로.</summary>
public static class Png
{
    public static (uint[] Px, int W, int H) Decode(byte[] b)
    {
        if (b.Length < 33 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') throw new InvalidDataException("PNG 가 아닙니다");
        int w = 0, h = 0, type = 0;
        using var idat = new MemoryStream();
        for (int at = 8; at + 8 <= b.Length;)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at));
            string kind = System.Text.Encoding.ASCII.GetString(b, at + 4, 4);
            var data = b.AsSpan(at + 8, len);
            if (kind == "IHDR")
            {
                w = BinaryPrimitives.ReadInt32BigEndian(data); h = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (data[8] != 8 || data[9] is not (2 or 6) || data[12] != 0) throw new InvalidDataException("8비트 RGB · RGBA, 엮지 않은 PNG 만 읽습니다");
                type = data[9];
            }
            else if (kind == "IDAT") idat.Write(data);
            else if (kind == "IEND") break;
            at += 12 + len;
        }
        int bpp = type == 6 ? 4 : 3, stride = w * bpp;
        var raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.ReadExactly(raw);
        var px = new uint[w * h];
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, up = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = (byte)(cur[i] + f switch
                {
                    1 => a, 2 => up, 3 => (a + up) / 2,
                    4 => Paeth(a, up, c),
                    _ => 0,
                });
            }
            for (int x = 0; x < w; x++)
            {
                int o = x * bpp;
                uint al = bpp == 4 ? cur[o + 3] : 255u;
                px[y * w + x] = al << 24 | (uint)cur[o] << 16 | (uint)cur[o + 1] << 8 | cur[o + 2];
            }
            (prev, cur) = (cur, prev);
        }
        return (px, w, h);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}

/// <summary>
/// 마법 → 지도 위 이펙트 짝(편집기 마법 모션 탭에서 바꾼다, w-159) — 게임 폴더 <c>fx/spellfx.json</c> 의 {"마법 번호": "시트 이름"}.
/// 시트 이름은 <c>fx/이름.png</c> + <c>fx/이름.json</c>, 없으면 실행 파일에 넣어 둔 것(holy-heal). 빈 이름 · 짝 없음 = 원작 그림.
/// 파일이 없으면 기본 짝 — 회복술(13) → holy-heal(사용자 요청 w-158).
/// </summary>
public static class SpellFxMap
{
    public const string Folder = "fx", MapFile = "spellfx.json";

    /// <summary>실행 파일에 넣어 둔 시트(pixelfx, MIT — Fx/LICENSE-pixelfx.txt).</summary>
    public static readonly string[] Builtin = ["holy-heal", "fire-flame-pillar"];

    public static Dictionary<int, string> Defaults() => new() { [13] = "holy-heal" };

    public static string DirOf(string gameDir) => Path.Combine(gameDir, Folder);

    public static Dictionary<int, string> Load(string gameDir)
    {
        string path = Path.Combine(DirOf(gameDir), MapFile);
        try
        {
            if (!File.Exists(path)) return Defaults();
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
            return raw.Where(kv => int.TryParse(kv.Key, out _)).ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value ?? "");
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return Defaults(); }
    }

    /// <summary>짝을 쓴다 — 원작으로 되돌린 마법도 빈 이름으로 남겨 기본 짝이 다시 들어오지 않게.</summary>
    public static void Save(string gameDir, Dictionary<int, string> map)
    {
        Directory.CreateDirectory(DirOf(gameDir));
        var raw = map.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        File.WriteAllText(Path.Combine(DirOf(gameDir), MapFile), JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>고를 수 있는 시트 이름 — 넣어 둔 것, 그다음 fx 폴더의 png+json 짝.</summary>
    public static List<string> Names(string gameDir)
    {
        var names = new List<string>(Builtin);
        try
        {
            if (Directory.Exists(DirOf(gameDir)))
                foreach (var png in Directory.GetFiles(DirOf(gameDir), "*.png").Order(StringComparer.OrdinalIgnoreCase))
                {
                    string n = Path.GetFileNameWithoutExtension(png);
                    if (File.Exists(Path.ChangeExtension(png, ".json")) && !names.Contains(n)) names.Add(n);
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return names;
    }

    private static readonly Dictionary<string, (long Stamp, FxSheet? Sheet)> Cache = new();

    /// <summary>시트 하나 — fx 폴더 것이 먼저, 없으면 넣어 둔 것. 못 읽으면 null.</summary>
    public static FxSheet? Sheet(string gameDir, string name)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string png = Path.Combine(DirOf(gameDir), name + ".png"), json = Path.ChangeExtension(png, ".json");
        try
        {
            if (File.Exists(png) && File.Exists(json))
            {
                long stamp = File.GetLastWriteTimeUtc(png).Ticks ^ File.GetLastWriteTimeUtc(json).Ticks;
                string key = png.ToLowerInvariant();
                lock (Cache)
                {
                    if (Cache.TryGetValue(key, out var c) && c.Stamp == stamp) return c.Sheet;
                    var s = FxSheet.Load(File.ReadAllBytes(png), File.ReadAllText(json));
                    Cache[key] = (stamp, s);
                    return s;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (!Builtin.Contains(name)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue("builtin:" + name, out var c)) return c.Sheet;
            var asm = typeof(SpellFxMap).Assembly;
            using var ps = asm.GetManifestResourceStream($"Fd2.Data.Fx.{name}.png");
            using var js = asm.GetManifestResourceStream($"Fd2.Data.Fx.{name}.json");
            FxSheet? sheet = null;
            if (ps != null && js != null)
            {
                using var ms = new MemoryStream();
                ps.CopyTo(ms);
                sheet = FxSheet.Load(ms.ToArray(), new StreamReader(js).ReadToEnd());
            }
            Cache["builtin:" + name] = (0, sheet);
            return sheet;
        }
    }

    /// <summary>그 마법의 이펙트 시트 — 짝이 없거나 원작이면 null.</summary>
    public static FxSheet? For(string gameDir, int spell) =>
        Load(gameDir).TryGetValue(spell, out var n) && n.Length > 0 ? Sheet(gameDir, n) : null;
}

/// <summary>
/// 필살기 모션(FIGANI 3c+2) 나누기(w-160) — 원작은 한 묶음에 몸짓 컷과 이펙트 컷을 차례로 담는다(티노-검성 34 치열도: 0~10 몸짓, 11~17 땅에서 솟는 불기둥).
/// 「몸짓 · 이펙트 따로」 모드에서는 몸짓 끝 컷 뒤로 쓰는 사람을 그 컷에 세워 둔 채 이펙트 컷을 겹친다. 「원작 그대로」면 쓰지 않는다.
/// 기본 표 + 게임 폴더 <c>fx/motionsplit.json</c>({"캐릭터 번호": 몸짓 끝 컷, -1 이면 나누지 않음})로 덮어쓴다.
/// </summary>
public static class MotionSplit
{
    public const string MapFile = "motionsplit.json", EffectFile = "motionfx.json";

    /// <summary>
    /// 나눈 필살기의 이펙트 구간을 바꿔 끼울 시트와 놓는 법(w-161 · w-162) — 기본 티노-검성 34 → 위로 솟는 불기둥, 사울-검성 32 → 왼쪽으로 뻗게 돌린 불기둥.
    /// 시트 이름이 비면 원작 이펙트 컷.
    /// </summary>
    public static Dictionary<int, SkillFx> EffectDefaults() => new()
    {
        [34] = new SkillFx("fire-flame-pillar"),
        [32] = new SkillFx("fire-flame-pillar", FxDir.Left),
    };

    /// <summary>fx/motionfx.json — {"캐릭터": "시트"} 또는 {"캐릭터": {"sheet", "dir": "up|left|right|down", "scale", "dx", "dy"}}.</summary>
    public static Dictionary<int, SkillFx> LoadEffects(string gameDir)
    {
        var map = EffectDefaults();
        string path = Path.Combine(SpellFxMap.DirOf(gameDir), EffectFile);
        try
        {
            if (!File.Exists(path)) return map;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!int.TryParse(p.Name, out int c)) continue;
                var v = p.Value;
                if (v.ValueKind == JsonValueKind.String) { map[c] = new SkillFx(v.GetString() ?? ""); continue; }
                if (v.ValueKind != JsonValueKind.Object) continue;
                string Str(string k) => v.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
                double Num(string k, double d) => v.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : d;
                map[c] = new SkillFx(Str("sheet"), SkillFx.ParseDir(Str("dir")), Num("scale", 1), (int)Num("dx", 0), (int)Num("dy", 0));
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return map;
    }

    public static void SaveEffects(string gameDir, Dictionary<int, SkillFx> map)
    {
        Directory.CreateDirectory(SpellFxMap.DirOf(gameDir));
        var raw = map.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => new
        {
            sheet = kv.Value.Sheet, dir = kv.Value.Dir.ToString().ToLowerInvariant(), scale = kv.Value.Scale, dx = kv.Value.Dx, dy = kv.Value.Dy,
        });
        File.WriteAllText(Path.Combine(SpellFxMap.DirOf(gameDir), EffectFile), JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>캐릭터 c 의 바꿔 끼울 이펙트 — 놓는 법과 그 방향으로 돌린 시트. 없으면 null(원작 이펙트 컷).</summary>
    public static (SkillFx Fx, FxSheet Sheet)? EffectFor(string gameDir, int character) =>
        LoadEffects(gameDir).TryGetValue(character, out var f) && f.Sheet.Length > 0 && SpellFxMap.Sheet(gameDir, f.Sheet) is { } sh ? (f, sh.Turned(f.Dir)) : null;

    public static Dictionary<int, int> Defaults() => new() { [34] = 10, [32] = 8 };

    public static Dictionary<int, int> Load(string gameDir)
    {
        var map = Defaults();
        string path = Path.Combine(SpellFxMap.DirOf(gameDir), MapFile);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) is { } raw)
                foreach (var (k, v) in raw)
                    if (int.TryParse(k, out int c)) map[c] = v;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return map;
    }

    public static void Save(string gameDir, Dictionary<int, int> map)
    {
        Directory.CreateDirectory(SpellFxMap.DirOf(gameDir));
        var raw = map.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        File.WriteAllText(Path.Combine(SpellFxMap.DirOf(gameDir), MapFile), JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>캐릭터 c 필살기 모션의 몸짓 끝 컷 — 나누지 않으면 -1(컷 수 안이어야 한다).</summary>
    public static int BodyEnd(string gameDir, int character, int frames) =>
        Load(gameDir).TryGetValue(character, out int e) && e >= 0 && e < frames - 1 ? e : -1;
}

/// <summary>이펙트가 자라는 쪽 — 시트는 위로 자란다고 보고 돌린다.</summary>
public enum FxDir { Up, Right, Down, Left }

/// <summary>
/// 필살기 이펙트 놓는 법(w-162) — 시트, 자라는 쪽, 크기 배율, 기준점 밀기(320x200 전투 화면 점).
/// 기준점은 원작 이펙트 컷들이 차지한 상자의 「자라기 시작하는 변」 가운데: 위 → 아래 변, 왼쪽 → 오른쪽 변, 오른쪽 → 왼쪽 변, 아래 → 위 변.
/// 크기는 그 상자의 자라는 쪽 길이에 시트의 길이를 맞춘다(1.5배까지) × 배율.
/// </summary>
public sealed record SkillFx(string Sheet, FxDir Dir = FxDir.Up, double Scale = 1, int Dx = 0, int Dy = 0)
{
    public static FxDir ParseDir(string s) => s.ToLowerInvariant() switch
    {
        "left" or "왼쪽" => FxDir.Left, "right" or "오른쪽" => FxDir.Right, "down" or "아래" => FxDir.Down, _ => FxDir.Up,
    };

    public static readonly string[] DirNames = ["위", "오른쪽", "아래", "왼쪽"];
}

