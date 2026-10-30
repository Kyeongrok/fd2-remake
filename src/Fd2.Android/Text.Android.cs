using Android.Graphics;

namespace Fd2;

/// <summary>
/// 글 그림 — 윈도우판(Text.cs, GDI)과 같은 꼴. 안드로이드 Paint 로 흰 글을 그려 알파를 쓴다. 같은 글 · 크기 · 색은 한 번만 만든다.
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

        using var paint = new Paint(PaintFlags.AntiAlias) { TextSize = size, Color = Color.White };
        paint.SetTypeface(bold ? Typeface.DefaultBold : Typeface.Default);
        var fm = paint.GetFontMetricsInt()!;
        int w = Math.Max(1, (int)Math.Ceiling(paint.MeasureText(text))), h = Math.Max(1, fm.Descent - fm.Ascent);
        using var bmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
        using (var canvas = new Canvas(bmp)) canvas.DrawText(text, 0, -fm.Ascent, paint);
        var raw = new int[w * h];
        bmp.GetPixels(raw, 0, w, 0, 0, w, h);
        var px = new uint[w * h];
        uint rgb = color & 0x00FFFFFF;
        for (int i = 0; i < px.Length; i++)
        {
            uint a = (uint)raw[i] >> 24;
            if (a != 0) px[i] = a << 24 | rgb;
        }
        return Cache[key] = new Image(px, w, h);
    }
}
