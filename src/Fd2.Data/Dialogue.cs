namespace Fd2.Data;

/// <summary>
/// 대사 — FDTXT.DAT 의 장면 n+1 항목. 앞에 u16 오프셋 표(첫 값 = 표 크기), 대사 묶음마다 u16 글자 번호들.
/// 글자 번호는 원본 글꼴(FDOTHER 4번, 16x16 한 비트 그림, 글자당 32바이트)의 차례 — 한글판은 한글 글자가 들어 있다.
/// </summary>
/// <remarks>
/// 제어 번호: 0xFFEE 다음 값 = 말하는 사람(그림 번호, 왼쪽), 0xFFEF 다음 값 = 말하는 사람(오른쪽),
/// 0xFFED = 이름 없는 사람(뒤의 글자 하나가 「A」 같은 꼬리표), 0xFFFE 줄 바꿈, 0xFFFD 쪽 넘김, 0xFFFF 끝.
/// 0x9E 는 빈칸. 이 뜻은 1장 대사를 그려 맞춰 본 것이다.
/// </remarks>
public static class Dialogue
{
    public const int Generic = -1;

    public sealed class Page
    {
        public int Speaker = Generic;
        public bool Right;
        public readonly List<List<int>> Lines = [[]];

        /// <summary>원본 글꼴이 아닌 보통 글(프롤로그 대사 파일) — 있으면 Lines 대신 이것을 보인다.</summary>
        public string? Text;

        /// <summary>이름 — 비면 Speaker 의 원작 이름.</summary>
        public string? Name;
    }

    /// <summary>장면 scene 의 대사 묶음들 — 묶음마다 쪽들.</summary>
    public static List<List<Page>> Load(string dir, int scene)
    {
        var t = Dat.Load(Path.Combine(dir, "FDTXT.DAT"), scene + 1);
        var result = new List<List<Page>>();
        if (t.Length < 2) return result;
        int count = Dat.U16(t, 0) / 2;
        var offs = Enumerable.Range(0, count).Select(i => (int)Dat.U16(t, 2 * i)).Append(t.Length).ToList();
        for (int m = 0; m < count; m++)
        {
            var pages = new List<Page>();
            Page? page = null;
            for (int p = offs[m]; p + 1 < offs[m + 1]; p += 2)
            {
                int c = Dat.U16(t, p);
                switch (c)
                {
                    case 0xFFEE or 0xFFEF when p + 3 < offs[m + 1]:
                        page = new Page { Speaker = Dat.U16(t, p + 2), Right = c == 0xFFEF };
                        pages.Add(page);
                        p += 2;
                        break;
                    case 0xFFED:
                        page = new Page();
                        pages.Add(page);
                        break;
                    case 0xFFFE:
                        page?.Lines.Add([]);
                        break;
                    case 0xFFFD when page != null:
                        page = new Page { Speaker = page.Speaker, Right = page.Right };
                        pages.Add(page);
                        break;
                    case >= 0xFF00:
                        break;
                    default:
                        if (page == null) { page = new Page(); pages.Add(page); }
                        page.Lines[^1].Add(c);
                        break;
                }
            }
            foreach (var pg in pages) pg.Lines.RemoveAll(l => l.Count == 0);
            result.Add(pages.Where(pg => pg.Lines.Count > 0).ToList());
        }
        return result;
    }
}

/// <summary>원본 글꼴 — FDOTHER 4번, 16x16 한 비트(위 비트가 왼쪽), 글자당 32바이트.</summary>
public sealed class Font16
{
    public const int Size = 16;
    private readonly byte[] _data;

    public Font16(string dir) => _data = Dat.Load(Path.Combine(dir, "FDOTHER.DAT"), 4);

    public bool Has(int code) => code >= 0 && 32 * code + 32 <= _data.Length;

    public bool Pixel(int code, int x, int y)
    {
        int row = _data[32 * code + 2 * y] << 8 | _data[32 * code + 2 * y + 1];
        return (row & (0x8000 >> x)) != 0;
    }
}

/// <summary>얼굴 그림 — DATO.DAT 의 그림 번호 항목. 앞에 u32 오프셋들(첫 값 = 표 크기), 장마다 u16 너비 · 높이 · RLE(11nnnnnn 이면 다음 바이트 n 번).</summary>
public static class Portrait
{
    public static List<(uint[] Px, int W, int H)> Load(string dir, int icon, uint[] pal)
    {
        var frames = new List<(uint[], int, int)>();
        string path = Path.Combine(dir, "DATO.DAT");
        if (icon < 0 || icon >= Dat.Count(path)) return frames;
        var e = Dat.Load(path, icon);
        if (e.Length < 8) return frames;
        int n = Dat.U32(e, 0) / 4;
        for (int i = 0; i < n; i++)
        {
            int o = Dat.U32(e, 4 * i);
            if (o + 4 > e.Length) break;
            int w = Dat.U16(e, o), h = Dat.U16(e, o + 2);
            var px = new uint[w * h];
            int p = o + 4, d = 0;
            while (d < px.Length && p < e.Length)
            {
                byte b = e[p++];
                if ((b & 0xC0) == 0xC0)
                {
                    int run = b & 0x3F;
                    uint c = p < e.Length ? pal[e[p++]] : 0;
                    for (int k = 0; k < run && d < px.Length; k++) px[d++] = c;
                }
                else px[d++] = pal[b];
            }
            frames.Add((px, w, h));
        }
        return frames;
    }
}
