namespace Fd2.Data;

/// <summary>
/// 바꾼 초상화(얼굴) — 편집기에서 그림을 넣으면 실행 파일 옆 「초상화\NNNN.dat」 에 DATO.DAT 한 항목과 같은 꼴(장마다 u16 너비 · 높이 · RLE)로 적고,
/// 게임은 <see cref="Portrait.Load"/> 에서 이 파일이 있으면 DATO.DAT 대신 읽는다(모든 버전에 같이). 파일을 지우면 원본으로 돌아간다.
/// 색은 게임 팔레트(256색)에서 가장 가까운 색으로 맞춘다 — 원작 그리기가 팔레트 번호로 되어 있어서.
/// </summary>
public static class PortraitFiles
{
    public const string Ext = ".dat";

    /// <summary>초상화 폴더 — 모션 폴더 옆 「초상화」.</summary>
    public static string Root => Path.Combine(Path.GetDirectoryName(MotionFiles.Root.TrimEnd(Path.DirectorySeparatorChar))!, "초상화");

    public static string PathOf(int icon) => Path.Combine(Root, $"{icon:D4}{Ext}");

    /// <summary>바꾼 그림이 있으면 그 바이트, 없으면 null.</summary>
    public static byte[]? Read(int icon)
    {
        try { return File.Exists(PathOf(icon)) ? File.ReadAllBytes(PathOf(icon)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static bool Has(int icon) => File.Exists(PathOf(icon));

    /// <summary>원본으로 — 바꾼 파일을 지운다.</summary>
    public static void Reset(int icon)
    {
        if (File.Exists(PathOf(icon))) File.Delete(PathOf(icon));
    }

    /// <summary>장들(팔레트 번호)을 DATO 꼴로 적는다.</summary>
    public static void Write(int icon, IReadOnlyList<(byte[] Index, int W, int H)> frames)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(PathOf(icon), Encode(frames));
    }

    /// <summary>DATO 한 항목 — 앞에 장마다 u32 오프셋(첫 값 = 표 크기), 장은 u16 w · h + RLE(0xC0 넘으면 (b − 0xC0) 번 되풀이할 다음 바이트, 아니면 그 색 한 점).</summary>
    public static byte[] Encode(IReadOnlyList<(byte[] Index, int W, int H)> frames)
    {
        var body = new List<byte>();
        var offsets = new List<int>();
        int head = 4 * frames.Count;
        foreach (var (index, w, h) in frames)
        {
            offsets.Add(head + body.Count);
            body.Add((byte)w); body.Add((byte)(w >> 8));
            body.Add((byte)h); body.Add((byte)(h >> 8));
            for (int i = 0; i < index.Length;)
            {
                byte c = index[i];
                int run = 1;
                while (i + run < index.Length && index[i + run] == c && run < 63) run++;
                if (run > 1 || c >= 0xC0) { body.Add((byte)(0xC0 + run)); body.Add(c); }
                else body.Add(c);
                i += run;
            }
        }
        var all = new byte[head + body.Count];
        for (int k = 0; k < offsets.Count; k++) BitConverter.TryWriteBytes(all.AsSpan(4 * k), offsets[k]);
        body.CopyTo(all, head);
        return all;
    }

    /// <summary>팔레트로 그린 점들을 그 번호로 되돌린다(같은 색이 둘이면 앞 번호 — 그려 보면 같다). 없는 색은 가장 가까운 번호.</summary>
    public static byte[] Unpalette(uint[] argb, uint[] pal)
    {
        var exact = new Dictionary<uint, byte>();
        for (int i = pal.Length - 1; i >= 0 && i < 256; i--) exact[pal[i]] = (byte)i;
        var outp = new byte[argb.Length];
        for (int i = 0; i < argb.Length; i++)
            outp[i] = exact.TryGetValue(argb[i], out byte b) ? b : Quantize([argb[i] | 0xFF000000], pal)[0];
        return outp;
    }

    /// <summary>ARGB 점들을 팔레트 번호로 — 가장 가까운 색. 투명한 점은 얼굴 바탕색(#28458A)에 가장 가까운 번호로.</summary>
    public static byte[] Quantize(uint[] argb, uint[] pal)
    {
        var cache = new Dictionary<uint, byte>();
        byte Nearest(uint c)
        {
            if (cache.TryGetValue(c, out byte b)) return b;
            int r = (int)(c >> 16 & 255), g = (int)(c >> 8 & 255), bl = (int)(c & 255), best = 0, bestD = int.MaxValue;
            for (int i = 0; i < pal.Length && i < 256; i++)
            {
                int pr = (int)(pal[i] >> 16 & 255) - r, pg = (int)(pal[i] >> 8 & 255) - g, pb = (int)(pal[i] & 255) - bl;
                int d = pr * pr * 3 + pg * pg * 4 + pb * pb * 2;
                if (d < bestD) (bestD, best) = (d, i);
            }
            return cache[c] = (byte)best;
        }
        var outp = new byte[argb.Length];
        for (int i = 0; i < argb.Length; i++)
            outp[i] = Nearest(argb[i] >> 24 < 128 ? 0xFF28458Au : argb[i]);
        return outp;
    }
}
