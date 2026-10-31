namespace Fd2.Data;

/// <summary>
/// 모션 손보기(편집기, 사용자 요청) — 한 모션에 붙어 그려진 효과(참혹술 검기)를 떼어 내고, 다른 캐릭터 모션 뒤에 깔아 새 모션(.obs)을 만든다.
/// <list type="bullet">
/// <item>떼어 내기: 효과 끝(몸에서 먼 곳)에서 시작해 효과 색으로 이어진 점을 채워 나간다 — 몸 외곽선에서 멈춘다.
/// 팔레트만으로는 안 된다(검기 크림 · 흰색이 갑옷 밝은 색과 같다). 몸에 가려 원래 없던 곳은 그 줄 · 그 칸 위아래가 모두 효과면 효과 바탕색으로 메운다.</item>
/// <item>붙이기: 효과 컷을 대상 모션의 시작 컷부터 한 컷씩 뒤에 깔고(대상 몸이 앞), 대상 컷이 모자라면 마지막 컷을 이어 쓴다.</item>
/// <item>적기: FIGANI 꼴 — 머리 8바이트(대상 것을 두고 컷 수만), 컷 표, 컷마다 x · y · 맞음 · 소리 · 머묾 · 0 · 0 · w · h + RLE(건너뛰기 · 한 색 되풀이 · 날 점들).</item>
/// </list>
/// </summary>
public static class MotionCraft
{
    public const int W = Motion.ScreenWidth, H = Motion.ScreenHeight;

    /// <summary>참혹술 — 로테만(19)의 마법 몸짓 7~14컷이 검기.</summary>
    public const int ChamhokCaster = 19, ChamhokFirst = 7, ChamhokLast = 14;

    /// <summary>한 컷을 320x200 화면 크기 점으로(빈 곳 0).</summary>
    public static uint[] ToScreen(MotionFrame f)
    {
        var s = new uint[W * H];
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                int X = f.X + x, Y = f.Y + y;
                if (X < 0 || Y < 0 || X >= W || Y >= H) continue;
                uint c = f.Pixels[y * f.Width + x];
                if (c != 0) s[Y * W + X] = c;
            }
        return s;
    }

    /// <summary>
    /// 효과만 떼어 낸다 — seed(효과만 있는 곳: 화면 점 번호를 고르는 함수)에서 효과 색으로 이어진 점, 그 둘레에 몸에 없는 색, 그리고 몸에 가려진 구멍 메우기.
    /// bodyColors 는 효과 없는 컷들의 색.
    /// </summary>
    public static uint[] Extract(uint[] screen, Func<int, int, bool> seed, HashSet<uint> bodyColors)
    {
        var fxColors = new HashSet<uint>();
        for (int Y = 0; Y < H; Y++) for (int X = 0; X < W; X++) if (seed(X, Y) && screen[Y * W + X] != 0) fxColors.Add(screen[Y * W + X]);
        var fx = new bool[W * H];
        var q = new Queue<int>();
        for (int Y = 0; Y < H; Y++) for (int X = 0; X < W; X++)
            if (seed(X, Y) && screen[Y * W + X] != 0) { fx[Y * W + X] = true; q.Enqueue(Y * W + X); }
        void Grow(Func<uint, bool> ok)
        {
            while (q.Count > 0)
            {
                int at = q.Dequeue(), X = at % W, Y = at / W;
                for (int k = 0; k < 4; k++)
                {
                    int nx = X + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = Y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int ni = ny * W + nx;
                    if (fx[ni] || screen[ni] == 0 || !ok(screen[ni])) continue;
                    fx[ni] = true;
                    q.Enqueue(ni);
                }
            }
        }
        Grow(fxColors.Contains);
        // 둘레 — 몸에 없는 색(가장자리 파란 선 같은 것)은 효과로
        for (int i = 0; i < fx.Length; i++) if (fx[i]) q.Enqueue(i);
        Grow(c => !bodyColors.Contains(c));

        var outp = new uint[W * H];
        for (int i = 0; i < fx.Length; i++) if (fx[i]) outp[i] = screen[i];
        // 구멍 메우기 — 그 줄 왼쪽 · 오른쪽과 그 칸 위가 효과인 빈 점을 효과에서 가장 많은 색으로(몸은 효과 아래로 삐져나가 아래는 안 본다.
        // 효과 아래 가장자리 밑은 그 줄 오른쪽에 효과가 없어 메워지지 않는다)
        var count = new Dictionary<uint, int>();
        foreach (var c in outp) if (c != 0) count[c] = count.GetValueOrDefault(c) + 1;
        if (count.Count == 0) return outp;
        uint fill = count.MaxBy(kv => kv.Value).Key;
        var rowL = new int[H]; var rowR = new int[H]; var colT = new int[W]; var colB = new int[W];
        Array.Fill(rowL, W); Array.Fill(rowR, -1); Array.Fill(colT, H); Array.Fill(colB, -1);
        for (int Y = 0; Y < H; Y++) for (int X = 0; X < W; X++)
            if (outp[Y * W + X] != 0)
            {
                rowL[Y] = Math.Min(rowL[Y], X); rowR[Y] = Math.Max(rowR[Y], X);
                colT[X] = Math.Min(colT[X], Y); colB[X] = Math.Max(colB[X], Y);
            }
        for (int Y = 0; Y < H; Y++) for (int X = 0; X < W; X++)
            if (outp[Y * W + X] == 0 && X > rowL[Y] && X < rowR[Y] && Y > colT[X]) outp[Y * W + X] = fill;
        return outp;
    }

    /// <summary>참혹술 검기 컷들 — 로테만 마법 몸짓에서 떼어 낸 화면 크기 점들(8장).</summary>
    public static List<uint[]> ChamhokBeam(string gameDir, uint[] pal)
    {
        var m = Motion.Load(gameDir, ChamhokCaster, 2, pal) ?? throw new InvalidDataException("로테만 마법 몸짓을 못 읽었습니다.");
        var body = new HashSet<uint>();
        foreach (int k in new[] { 0, 1, m.Frames.Count - 1 }) foreach (var c in m.Frames[k].Pixels) if (c != 0) body.Add(c);
        var beams = new List<uint[]>();
        for (int k = ChamhokFirst; k <= Math.Min(ChamhokLast, m.Frames.Count - 1); k++)
            beams.Add(Extract(ToScreen(m.Frames[k]), (x, y) => x >= 250 && y < 60, body));
        return beams;
    }

    /// <summary>
    /// 대상 모션(바이트) 컷 start 부터 효과 컷들을 뒤에 깐 새 모션 바이트 — 효과는 (dx, dy) 만큼 옮기고, mirror 면 좌우로 뒤집는다.
    /// 대상이 모자라면 마지막 컷을 이어 쓴다(머묾 · 맞음은 그 컷 것, 이어 쓴 컷은 맞음 0).
    /// </summary>
    public static byte[] Composite(byte[] target, uint[] pal, List<uint[]> fx, int start, int dx, int dy, bool mirror)
    {
        var m = ParseRaw(target, pal);
        int n = Math.Max(m.Count, start + fx.Count);
        var frames = new List<(uint[] Screen, RawFrame Info)>();
        for (int k = 0; k < n; k++)
        {
            var src = m[Math.Min(k, m.Count - 1)];
            var scr = ToScreen(src.Frame);
            var info = src with { Hit = k < m.Count ? src.Hit : (byte)0, Sound = k < m.Count ? src.Sound : (byte)0 };
            int e = k - start;
            if (e >= 0 && e < fx.Count)
            {
                var under = fx[e];
                for (int Y = 0; Y < H; Y++)
                    for (int X = 0; X < W; X++)
                    {
                        int sx = (mirror ? W - 1 - X : X) - dx, sy = Y - dy;
                        if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                        uint c = under[sy * W + sx];
                        if (c != 0 && scr[Y * W + X] == 0) scr[Y * W + X] = c;   // 몸이 앞
                    }
            }
            frames.Add((scr, info));
        }
        return Encode(target, frames, pal);
    }

    /// <summary>
    /// 만든 모션을 새 모션 버전 「버전 이름」 의 그 캐릭터 폴더에 적고(나머지 두 종류는 지금 쓰는 것을 같이 베낌), 그 캐릭터가 이 버전을 쓰게 고르기에 적는다.
    /// </summary>
    public static void SaveAsVersion(string gameDir, int c, int kind, byte[] motion, string version)
    {
        string folder = Path.Combine(MotionFiles.VersionFolder(version), MotionFiles.CharacterFolder(c));
        Directory.CreateDirectory(folder);
        for (int k = 0; k < 3; k++)
        {
            string path = Path.Combine(folder, MotionFiles.FileName(c, k));
            if (k == kind) File.WriteAllBytes(path, motion);
            else if (!File.Exists(path) && (MotionFiles.Read(gameDir, c, k) ?? SafeFigani(gameDir, 3 * c + k)) is { Length: >= 12 } keep && keep[0] != 0)
                File.WriteAllBytes(path, keep);
        }
        File.WriteAllText(Path.Combine(MotionFiles.VersionFolder(version), "버전.txt"), $"{version}\n편집기에서 만든 모션(효과 붙이기)\n{DateTime.Now:yyyy-MM-dd HH:mm}\n");
        MotionFiles.SetPick(c, version);
        MotionFiles.Reload();
    }

    private static byte[]? SafeFigani(string gameDir, int entry)
    {
        string dat = Path.Combine(gameDir, "FIGANI.DAT");
        try { return File.Exists(dat) && entry < Dat.Count(dat) ? Dat.Load(dat, entry) : null; }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or IOException) { return null; }
    }

    public sealed record RawFrame(MotionFrame Frame, byte Hit, byte Sound, byte Hold, byte B7, byte B8);

    private static List<RawFrame> ParseRaw(byte[] a, uint[] pal)
    {
        int n = a[0];
        var list = new List<RawFrame>(n);
        for (int i = 0; i < n; i++)
        {
            int o = Dat.U32(a, 8 + 4 * i);
            int w = Dat.U16(a, o + 9), h = Dat.U16(a, o + 11);
            var f = new MotionFrame
            {
                X = Dat.U16(a, o), Y = Dat.U16(a, o + 2), Width = w, Height = h,
                Hit = a[o + 4], Sound = a[o + 5], Hold = Math.Max(1, (int)a[o + 6]), Pixels = Rle.Decode(a, o + 13, w, h, pal),
            };
            list.Add(new RawFrame(f, a[o + 4], a[o + 5], a[o + 6], a[o + 7], a[o + 8]));
        }
        return list;
    }

    /// <summary>화면 크기 컷들을 FIGANI 꼴로 — 머리는 원래 모션 것(컷 수 · 나눔 컷만 고침), 컷은 그림이 있는 사각형만.</summary>
    private static byte[] Encode(byte[] original, List<(uint[] Screen, RawFrame Info)> frames, uint[] pal)
    {
        var exact = new Dictionary<uint, byte>();
        for (int i = Math.Min(255, pal.Length - 1); i >= 0; i--) exact[pal[i]] = (byte)i;
        byte Index(uint c) => exact.TryGetValue(c, out byte b) ? b : PortraitFiles.Quantize([c | 0xFF000000], pal)[0];

        var body = new List<byte>();
        var offsets = new List<int>();
        int head = 8 + 4 * frames.Count;
        foreach (var (scr, info) in frames)
        {
            int x0 = W, y0 = H, x1 = -1, y1 = -1;
            for (int Y = 0; Y < H; Y++) for (int X = 0; X < W; X++)
                if (scr[Y * W + X] != 0) { x0 = Math.Min(x0, X); x1 = Math.Max(x1, X); y0 = Math.Min(y0, Y); y1 = Math.Max(y1, Y); }
            if (x1 < 0) (x0, y0, x1, y1) = (0, 0, 0, 0);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            offsets.Add(head + body.Count);
            void U16(int v) { body.Add((byte)v); body.Add((byte)(v >> 8)); }
            U16(x0); U16(y0);
            body.Add(info.Hit); body.Add(info.Sound); body.Add(info.Hold); body.Add(info.B7); body.Add(info.B8);
            U16(w); U16(h);
            for (int y = 0; y < h; y++)
            {
                int x = 0;
                while (x < w)
                {
                    uint c = scr[(y0 + y) * W + x0 + x];
                    int run = 1;
                    if (c == 0)
                    {
                        while (x + run < w && run < 64 && scr[(y0 + y) * W + x0 + x + run] == 0) run++;
                        body.Add((byte)(0xC0 | (run - 1)));
                    }
                    else
                    {
                        while (x + run < w && run < 64 && scr[(y0 + y) * W + x0 + x + run] == c) run++;
                        if (run >= 3) { body.Add((byte)(run - 1)); body.Add(Index(c)); }
                        else
                        {
                            // 날 점들 — 다음 빈 점이나 셋 넘게 같은 색이 나오기 전까지
                            run = 0;
                            var lit = new List<byte>();
                            while (x + run < w && run < 64)
                            {
                                uint cc = scr[(y0 + y) * W + x0 + x + run];
                                if (cc == 0) break;
                                if (run > 0 && x + run + 2 < w && scr[(y0 + y) * W + x0 + x + run + 1] == cc && scr[(y0 + y) * W + x0 + x + run + 2] == cc) break;
                                lit.Add(Index(cc));
                                run++;
                            }
                            body.Add((byte)(0x80 | (run - 1)));
                            body.AddRange(lit);
                        }
                    }
                    x += run;
                }
            }
        }
        var all = new byte[head + body.Count];
        Array.Copy(original, all, 8);
        all[0] = (byte)frames.Count;
        all[1] = 0;
        if (all[2] > frames.Count) all[2] = (byte)frames.Count;
        for (int k = 0; k < offsets.Count; k++) BitConverter.TryWriteBytes(all.AsSpan(8 + 4 * k), offsets[k]);
        body.CopyTo(all, head);
        return all;
    }
}
