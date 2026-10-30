using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Fd2.Data;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Fd2;

/// <summary>
/// 모드 &gt; 고해상도 — 지도와 캐릭터 그림을 업스케일 모델로 네 배 키운다(창세기전3 고해상도 모세스와 같은 방법, 기본 켬).
/// </summary>
/// <remarks>
/// <para>모델은 Rebout Blend 4x(LyonHrt, CC BY-NC-SA 4.0, ESRGAN)를 ONNX 로 바꾼 것으로, 대항해시대(cds-remake)가 올려 둔 것을 받는다(64MB).
/// 키운 그림은 원본 그림에서 나온 것이라 배포하지 않고 <b>그 PC 에서</b> 만든다 — %LOCALAPPDATA%\Fd2\hires 에 쌓는다.</para>
/// <para>처음에는 하나씩 만들어야 하므로 <b>다 된 조각부터</b> 화면에 갈아 끼운다 — 지도는 128 조각마다, 캐릭터는 12장 묶음마다.
/// 지금 장면의 지도 → 그 장면의 사람 → 나머지 장면 차례로 뒤 스레드에서 만든다. 그래픽 카드(DirectML), 안 되면 CPU.</para>
/// </remarks>
internal sealed class HiRes
{
    internal const string ModelFile = "4x-ReboutBlend.onnx";
    internal const string ModelUrl = "https://github.com/Kyeongrok/cds-remake/releases/download/model-assets/" + ModelFile;
    internal const int Scale = 4, Tile = 128, Pad = 12;

    /// <summary>캐릭터 12장을 한 줄로 늘어놓을 때 한 장의 칸 — 사이를 띄워 옆 그림이 번지지 않게.</summary>
    private const int Cell = Rle.Tile + 8;

    private static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "hires");
    private static string ModelPath => Path.Combine(Folder, "model", ModelFile);
    private static string MapPath(int scene) => Path.Combine(Folder, "map", $"{scene:D2}.bin.gz");
    private static string IconPath(int icon) => Path.Combine(Folder, "icon", $"{icon:D3}.bin.gz");

    private readonly string _dir;
    private readonly uint[] _pal;
    private readonly int _scenes, _icons;

    /// <summary>뒤 스레드가 다 만든 지도 조각 — 그리는 쪽이 텍스처에 올린다.</summary>
    internal readonly ConcurrentQueue<(int Scene, int X, int Y, int W, int H, uint[] Px)> MapChunks = new();

    /// <summary>다 만든 캐릭터 — 번호 → 12장(96x96, 알파 있음).</summary>
    private readonly ConcurrentDictionary<int, uint[][]> _iconFrames = new();

    /// <summary>그리는 쪽(주 스레드)만 쓴다 — 아직 없던 캐릭터를 마지막으로 본 때, 원본 그림.</summary>
    private readonly Dictionary<int, long> _iconChecked = new();
    private IconBank? _lowBank;

    internal volatile bool Enabled;
    internal volatile string Status = "";
    private volatile bool _running;
    private volatile int _wantScene = -1;
    private int[] _wantIcons = [];

    public HiRes(string dir, uint[] pal, int scenes, int icons, bool enabled)
    {
        _dir = dir;
        _pal = pal;
        _scenes = scenes;
        _icons = icons;
        Enabled = enabled;
    }

    /// <summary>지금 장면 — 이것부터 만든다.</summary>
    internal void Want(int scene, IEnumerable<int> icons)
    {
        _wantScene = scene;
        _wantIcons = icons.Distinct().ToArray();
        Start();
    }

    internal void Start()
    {
        if (!Enabled || _running) return;
        _running = true;
        Task.Run(Work);
    }

    /// <summary>만들어 둔 지도 한 장(원본 w x h 의 4배), 없으면 null.</summary>
    internal static uint[]? LoadMap(int scene, int w, int h) => Load(MapPath(scene), w * Scale, h * Scale);

    /// <summary>만들어 둔 캐릭터 한 장(96x96), 아직이면 null — 파일이 있으면 그때 읽는다.</summary>
    internal uint[]? Icon(int icon, int frame)
    {
        if (_iconFrames.TryGetValue(icon, out var frames)) return frames[frame];
        // 아직 없는 것은 1초에 한 번만 파일을 본다 — 매 틀 사람마다 보면 디스크를 괜히 두드린다.
        long now = Environment.TickCount64;
        if (_iconChecked.TryGetValue(icon, out long at) && now - at < 1000) return null;
        _iconChecked[icon] = now;
        if (!File.Exists(IconPath(icon)) || Load(IconPath(icon), Cell * IconBank.FramesPerChar * Scale, Cell * Scale) is not { } strip)
            return null;
        _lowBank ??= new IconBank(Path.Combine(_dir, "FDICON.B24"), _pal);
        frames = Split(strip, LowStrip(_lowBank, icon));
        _iconFrames[icon] = frames;
        return frames[frame];
    }

    // ── 만들기 ────────────────────────────────────────────────────────────────

    private async Task Work()
    {
        try
        {
            if (!File.Exists(ModelPath) && !await DownloadModel()) return;
            using var session = OpenSession(out bool gpu, out string why);
            string input = session.InputMetadata.Keys.First();
            string device = gpu ? "그래픽 카드" : why.Length > 0 ? $"CPU — 느림 · 그래픽 카드 못 씀: {why}" : "CPU — 느림";
            var bank = new IconBank(Path.Combine(_dir, "FDICON.B24"), _pal);
            while (Enabled)
            {
                // 지금 장면 지도 → 지금 장면 사람 → 나머지 장면 지도 → 나머지 사람
                int scene = _wantScene;
                if (scene >= 0 && !File.Exists(MapPath(scene))) { MakeMap(session, input, scene, device); continue; }
                int icon = _wantIcons.FirstOrDefault(i => !File.Exists(IconPath(i)), -1);
                if (icon >= 0) { MakeIcon(session, input, bank, icon, device); continue; }
                int next = Enumerable.Range(0, _scenes).FirstOrDefault(s => !File.Exists(MapPath(s)), -1);
                if (next >= 0) { MakeMap(session, input, next, device); continue; }
                int nextIcon = Enumerable.Range(0, _icons).FirstOrDefault(i => !File.Exists(IconPath(i)), -1);
                if (nextIcon >= 0) { MakeIcon(session, input, bank, nextIcon, device); continue; }
                break;
            }
            Status = "";
        }
        catch (Exception ex)   // 뒤 스레드라 무엇이 터져도 게임은 가야 한다 — 까닭은 제목줄에 보인다
        {
            Status = $"고해상도 못 만듦 — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }
        finally { _running = false; }
    }

    private void MakeMap(InferenceSession session, string input, int scene, string device)
    {
        var map = new Scene(_dir, scene, _pal, new IconBank(Path.Combine(_dir, "FDICON.B24"), _pal));
        int w = map.Width * Rle.Tile, h = map.Height * Rle.Tile;
        var low = map.RenderMap();
        int done = Enumerable.Range(0, _scenes).Count(s => File.Exists(MapPath(s)));
        var big = Upscale(session, input, low, w, h, (x, y, cw, ch, px) =>
        {
            MapChunks.Enqueue((scene, x, y, cw, ch, px));
            Status = $"고해상도 지도 만드는 중 — 장면 {scene + 1} ({done}/{_scenes} 끝, {device})";
        }, () => Enabled);
        if (big != null) Save(MapPath(scene), big, w * Scale, h * Scale);
    }

    /// <summary>캐릭터 12장을 한 줄(검은 바탕)로 늘어놓아 키운다. 알파는 원본 테두리를 네 배로 키워 쓴다.</summary>
    private void MakeIcon(InferenceSession session, string input, IconBank bank, int icon, string device)
    {
        Status = $"고해상도 캐릭터 만드는 중 — {CharacterNames.Of(icon)} ({device})";
        var low = LowStrip(bank, icon);
        int w = Cell * IconBank.FramesPerChar, h = Cell;
        var opaque = low.Select(c => c | 0xFF000000).ToArray();
        var big = Upscale(session, input, opaque, w, h, null, () => Enabled);
        if (big == null) return;
        Save(IconPath(icon), big, w * Scale, h * Scale);
        _iconFrames[icon] = Split(big, low);
    }

    private static uint[] LowStrip(IconBank bank, int icon)
    {
        int w = Cell * IconBank.FramesPerChar;
        var strip = new uint[w * Cell];
        for (int f = 0; f < IconBank.FramesPerChar; f++)
        {
            var px = bank.Frame(icon, f);
            for (int y = 0; y < Rle.Tile; y++)
                px.AsSpan(y * Rle.Tile, Rle.Tile).CopyTo(strip.AsSpan((4 + y) * w + f * Cell + 4));
        }
        return strip;
    }

    /// <summary>키운 줄을 96x96 열두 장으로 — 원본에서 투명이던 점은 투명하게.</summary>
    private static uint[][] Split(uint[] big, uint[] low)
    {
        int bw = Cell * IconBank.FramesPerChar * Scale, lw = Cell * IconBank.FramesPerChar, s = Rle.Tile * Scale;
        var frames = new uint[IconBank.FramesPerChar][];
        for (int f = 0; f < frames.Length; f++)
        {
            var px = new uint[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int lx = f * Cell + 4 + x / Scale, ly = 4 + y / Scale;
                    if (low[ly * lw + lx] == 0) continue;
                    px[y * s + x] = big[(ly * Scale + y % Scale) * bw + lx * Scale + x % Scale] | 0xFF000000;
                }
            frames[f] = px;
        }
        return frames;
    }

    /// <summary>그래픽 카드(DirectML)로 열고, 안 되면 CPU 로.</summary>
    private static InferenceSession OpenSession(out bool gpu, out string why)
    {
        why = "";
        try
        {
            PreloadDirectML();
            var options = new SessionOptions();
            options.AppendExecutionProvider_DML(0);
            gpu = true;
            return new InferenceSession(ModelPath, options);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException or BadImageFormatException)
        {
            gpu = false;
            why = ex.Message.Split('\n')[0];
            return new InferenceSession(ModelPath);
        }
    }

    /// <summary>
    /// 꾸러미의 DirectML.dll 을 먼저 올려 둔다 — 단일 exe 로 배포하면 DLL 이 임시 폴더에 풀리는데, 이름으로 찾으면
    /// Windows 에 딸린 옛 DirectML(System32)을 잡아 그래픽 카드를 못 쓰고 CPU 로 떨어진다(창세기전3에서 겪은 것).
    /// </summary>
    private static void PreloadDirectML()
    {
        // 단일 exe 는 네이티브 DLL 을 임시 폴더에 푼다 — 그 자리는 NATIVE_DLL_SEARCH_DIRECTORIES 에 있다(Assembly.Location 은 비어 있다).
        var native = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string? dir in native.Prepend(Path.GetDirectoryName(typeof(InferenceSession).Assembly.Location)).Append(AppContext.BaseDirectory))
            if (!string.IsNullOrEmpty(dir) && Path.Combine(dir, "DirectML.dll") is var path && File.Exists(path))
            {
                NativeLibrary.TryLoad(path, out _);
                return;
            }
    }

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    private async Task<bool> DownloadModel()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        string temp = ModelPath + ".part";
        Status = "고해상도 모델 받는 중";
        using (var response = await Client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var from = await response.Content.ReadAsStreamAsync();
            await using var to = File.Create(temp);
            var buffer = new byte[1 << 16];
            long got = 0;
            int read;
            while ((read = await from.ReadAsync(buffer)) > 0)
            {
                if (!Enabled) return false;
                await to.WriteAsync(buffer.AsMemory(0, read));
                got += read;
                if (total > 0) Status = $"고해상도 모델 받는 중 {got * 100 / total.Value}% ({total.Value >> 20}MB)";
            }
        }
        File.Move(temp, ModelPath, overwrite: true);
        return true;
    }

    /// <summary>
    /// 그림을 128 조각으로 나눠 키운다 — 조각마다 둘레 12점을 더 넣고 가운데만 써서 이음매가 안 남게.
    /// 조각이 끝날 때마다 <paramref name="chunk"/> 로 알린다(다 된 조각부터 화면에). 도중에 꺼지면 null.
    /// </summary>
    private static uint[]? Upscale(InferenceSession session, string input, uint[] px, int w, int h,
                                   Action<int, int, int, int, uint[]>? chunk, Func<bool> keepGoing)
    {
        int bw = w * Scale;
        var outPx = new uint[bw * h * Scale];
        for (int ty = 0; ty < h; ty += Tile)
            for (int tx = 0; tx < w; tx += Tile)
            {
                if (!keepGoing()) return null;
                int x0 = Math.Max(0, tx - Pad), y0 = Math.Max(0, ty - Pad);
                int x1 = Math.Min(w, tx + Tile + Pad), y1 = Math.Min(h, ty + Tile + Pad);
                int tw = x1 - x0, th = y1 - y0;
                var t = new DenseTensor<float>([1, 3, th, tw]);
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                    {
                        uint c = px[(y0 + y) * w + x0 + x];
                        t[0, 0, y, x] = (c >> 16 & 255) / 255f;
                        t[0, 1, y, x] = (c >> 8 & 255) / 255f;
                        t[0, 2, y, x] = (c & 255) / 255f;
                    }
                using var results = session.Run([NamedOnnxValue.CreateFromTensor(input, t)]);
                var o = results.First().AsTensor<float>();
                int cx0 = (tx - x0) * Scale, cy0 = (ty - y0) * Scale;
                int cw = Math.Min(Tile, w - tx) * Scale, ch = Math.Min(Tile, h - ty) * Scale;
                var piece = chunk != null ? new uint[cw * ch] : null;
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        uint B(int k) => (uint)Math.Clamp((int)MathF.Round(o[0, k, cy0 + y, cx0 + x] * 255f), 0, 255);
                        uint c = 0xFF000000 | B(0) << 16 | B(1) << 8 | B(2);
                        outPx[(ty * Scale + y) * bw + tx * Scale + x] = c;
                        if (piece != null) piece[y * cw + x] = c;
                    }
                chunk?.Invoke(tx * Scale, ty * Scale, cw, ch, piece!);
            }
        return outPx;
    }

    // ── 캐시 파일: gzip(BGRA 점들) ─────────────────────────────────────────────

    private static void Save(string path, uint[] px, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var fs = File.Create(temp))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
        {
            Span<byte> head = stackalloc byte[8];
            BitConverter.TryWriteBytes(head, w);
            BitConverter.TryWriteBytes(head[4..], h);
            gz.Write(head);
            gz.Write(MemoryMarshal.AsBytes(px.AsSpan()));
        }
        File.Move(temp, path, overwrite: true);
    }

    private static uint[]? Load(string path, int w, int h)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            Span<byte> head = stackalloc byte[8];
            gz.ReadExactly(head);
            if (BitConverter.ToInt32(head) != w || BitConverter.ToInt32(head[4..]) != h) return null;
            var px = new uint[w * h];
            gz.ReadExactly(MemoryMarshal.AsBytes(px.AsSpan()));
            return px;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException) { return null; }
    }
}
