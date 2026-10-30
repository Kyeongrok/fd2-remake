namespace Fd2.Data;

/// <summary>
/// 전투 모션 FIGANI.DAT — 캐릭터(FDICON 과 같은 번호) c 마다 항목 셋: 3c 서기, 3c+1 공격, 3c+2 덤(몇 바이트).
/// </summary>
/// <remarks>
/// <para>항목: [0] 컷 수, [4] 맞는 컷(추정), 8 부터 컷마다 u32 오프셋.</para>
/// <para>컷: u16 x, u16 y(320x200 전투 화면 위 자리), +4 타격 표시, +5 효과음, +6 이 컷을 붙드는 틱 수,
/// +9 부터 u16 너비 · u16 높이 · RLE(원본 sub_2EB9F → sub_4E98D).</para>
/// </remarks>
public sealed class Motion
{
    public const int ScreenWidth = 320, ScreenHeight = 200;

    public IReadOnlyList<MotionFrame> Frames { get; }

    /// <summary>머리 [4] — 공격 모션에서 맞는 컷 번호로 보인다.</summary>
    public int HitFrame { get; }

    private Motion(List<MotionFrame> frames, int hit) { Frames = frames; HitFrame = hit; }

    public static int Characters(string dir) => Dat.Count(Path.Combine(dir, "FIGANI.DAT")) / 3;

    /// <summary>캐릭터 c 의 모션 kind(0 서기, 1 공격). 비어 있으면 null.</summary>
    public static Motion? Load(string dir, int character, int kind, uint[] pal)
    {
        var a = Dat.Load(Path.Combine(dir, "FIGANI.DAT"), 3 * character + kind);
        if (a.Length < 12 || a[0] == 0) return null;
        int n = a[0];
        var frames = new List<MotionFrame>(n);
        for (int i = 0; i < n; i++)
        {
            int o = Dat.U32(a, 8 + 4 * i);
            int w = Dat.U16(a, o + 9), h = Dat.U16(a, o + 11);
            frames.Add(new MotionFrame
            {
                X = Dat.U16(a, o),
                Y = Dat.U16(a, o + 2),
                Width = w,
                Height = h,
                Hit = a[o + 4],
                Sound = a[o + 5],
                Hold = Math.Max(1, (int)a[o + 6]),
                Pixels = Rle.Decode(a, o + 13, w, h, pal),
            });
        }
        return new Motion(frames, a[4]);
    }
}

public sealed class MotionFrame
{
    public int X, Y, Width, Height;
    public int Hit, Sound, Hold;
    public required uint[] Pixels;
}
