using System.Runtime.InteropServices;

namespace Fd2;

/// <summary>
/// 글 그림 — GDI 로 흰 글을 검은 바탕에 찍어 밝기를 알파로 쓴다(맑은 고딕, 안티앨리어싱). 같은 글 · 크기 · 색은 한 번만 만든다.
/// </summary>
internal static class Text
{
    internal sealed record Image(uint[] Px, int W, int H);

    private static readonly Dictionary<(string, int, uint, bool), Image> Cache = new();

    public static Image Render(string text, int size, uint color, bool bold = true)
    {
        var key = (text, size, color, bold);
        if (Cache.TryGetValue(key, out var img)) return img;
        if (Cache.Count > 2000) Cache.Clear();

        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr font = CreateFontW(-size, 0, 0, 0, bold ? 700 : 400, 0, 0, 0, 1 /*DEFAULT_CHARSET*/, 0, 0,
                                  4 /*ANTIALIASED_QUALITY*/, 0, "맑은 고딕");
        IntPtr oldFont = SelectObject(dc, font);
        GetTextExtentPoint32W(dc, text, text.Length, out var ext);
        int w = Math.Max(1, ext.Cx), h = Math.Max(1, ext.Cy);

        var bmi = new BitmapInfo { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        IntPtr dib = CreateDIBSection(dc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr oldBmp = SelectObject(dc, dib);
        SetBkMode(dc, 1 /*TRANSPARENT*/);
        SetTextColor(dc, 0x00FFFFFF);
        TextOutW(dc, 0, 0, text, text.Length);
        GdiFlush();

        var raw = new int[w * h];
        Marshal.Copy(bits, raw, 0, raw.Length);
        var px = new uint[w * h];
        uint rgb = color & 0x00FFFFFF;
        for (int i = 0; i < px.Length; i++)
        {
            uint v = (uint)raw[i];
            uint a = Math.Max(v & 0xFF, Math.Max(v >> 8 & 0xFF, v >> 16 & 0xFF));
            if (a != 0) px[i] = a << 24 | rgb;
        }

        SelectObject(dc, oldBmp);
        SelectObject(dc, oldFont);
        DeleteObject(dib);
        DeleteObject(font);
        DeleteDC(dc);
        return Cache[key] = new Image(px, w, h);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        public int Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeStruct { public int Cx, Cy; }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr dc, int x, int y, string text, int length);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int length, out SizeStruct size);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
