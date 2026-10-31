namespace Fd2.Data;

/// <summary>
/// 전투 모션 FIGANI.DAT — 캐릭터(FDICON 과 같은 번호) c 마다 항목 셋: 3c 서기, 3c+1 공격, 3c+2 덤(몇 바이트).
/// </summary>
/// <remarks>
/// <para>항목: [0] 컷 수, [2] 나눔 컷(뜻은 아직 짐작), [4] 소리 묶음 번호(1~6 → FDOTHER 48~53, 0 = 소리 없음 — sub_314DE),
/// 8 부터 컷마다 u32 오프셋.</para>
/// <para>컷: u16 x, u16 y(320x200 전투 화면 위 자리), +4 맞음(1 이면 이 컷에서 HP 를 깎는다 — 여럿이면 나눠 깎는다),
/// +5 소리 묶음 안의 소리 번호(0 = 없음), +6 이 컷을 붙드는 틱 수, +9 부터 u16 너비 · u16 높이 · RLE(원본 sub_2EB9F → sub_4E98D).</para>
/// </remarks>
public sealed class Motion
{
    public const int ScreenWidth = 320, ScreenHeight = 200;

    public IReadOnlyList<MotionFrame> Frames { get; }

    /// <summary>머리 [4] — 소리 묶음 번호(1~6 → FDOTHER 48~53), 0 이면 소리 없음.</summary>
    public int SoundBank { get; }

    /// <summary>소리 묶음이 든 FDOTHER 항목 번호, 없으면 -1.</summary>
    public int SoundEntry => SoundBank is >= 1 and <= 6 ? 47 + SoundBank : -1;

    private Motion(List<MotionFrame> frames, int bank) { Frames = frames; SoundBank = bank; }

    public static int Characters(string dir) => Dat.Count(Path.Combine(dir, "FIGANI.DAT")) / 3;

    /// <summary>
    /// 캐릭터 c 의 모션 kind(0 서기, 1 공격, 2 마법 · 특수 공격 — 17 캐릭터만 있다). 비어 있으면 null.
    /// 버전마다 뽑아 둔 .obs(<see cref="MotionFiles"/>, 캐릭터마다 고른 버전)가 있으면 그것을, 없으면 FIGANI.DAT 를.
    /// </summary>
    public static Motion? Load(string dir, int character, int kind, uint[] pal) =>
        MotionFiles.Read(dir, character, kind) is { } obs ? Parse(obs, pal) : LoadFigani(dir, character, kind, pal);

    /// <summary>그 폴더 FIGANI.DAT 의 모션 그대로(버전 비교처럼 .obs 고르기를 거치지 않을 때).</summary>
    public static Motion? LoadFigani(string dir, int character, int kind, uint[] pal) =>
        Parse(Dat.Load(Path.Combine(dir, "FIGANI.DAT"), 3 * character + kind), pal);

    /// <summary>FDOTHER 의 FIGANI 꼴 항목(전투 화면 마법 효과 18~44 — sub_2FF01) — 비었거나 꼴이 아니면 null.</summary>
    public static Motion? LoadOther(string dir, int entry, uint[] pal)
    {
        try { return Parse(MotionFiles.ReadOther(dir, entry) ?? Dat.Load(Path.Combine(dir, "FDOTHER.DAT"), entry), pal); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    private static Motion? Parse(byte[] a, uint[] pal)
    {
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

    /// <summary>Hit: 이 컷에서 HP 를 깎나(1). Sound: 소리 묶음 안 번호(0 없음). Hold: 붙드는 틱 수.</summary>
    public int Hit, Sound, Hold;
    public required uint[] Pixels;
}
