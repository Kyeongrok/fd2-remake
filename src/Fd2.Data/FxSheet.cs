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

    private int _half = -1;

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
    public static readonly string[] Builtin = ["holy-heal"];

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
