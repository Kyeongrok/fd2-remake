namespace Fd2.Data;

/// <summary>
/// 효과음 묶음 — FDOTHER 31 · 48~53 · 64 · 77 · 78 · 80~95 는 그 안이 다시 LLLLLL 묶음이고, 칸마다 머리 없는 8비트 부호 없는 모노 PCM(분석-자료형식-FDOTHER §5).
/// 빠르기는 AIL 기본값 11025 Hz 로 본다(게임이 빠르기를 정하지 않는다).
/// </summary>
public static class Sound
{
    public const int Rate = 11025;

    /// <summary>
    /// 묶음 31(메뉴 · 커서 소리, sub_25A96(dword_53EEC, n)) 의 번호 — 0 커서 움직임, 2 · 5 · 6 · 7 메뉴 쪽, 3 공격 뒤(sub_1DB65), 4 쉬기(sub_13FD4), 8 명령 단추 열기(sub_1741C), 12 상자 열기(sub_190AC).
    /// </summary>
    public const int UiBank = 31, Cursor = 0, Rest = 4, Menu = 8, Chest = 12;

    /// <summary>FDOTHER 항목 entry 의 소리들 — 묶음이 아니면 빈 목록.</summary>
    public static List<byte[]> Bank(string dir, int entry)
    {
        var list = new List<byte[]>();
        byte[] d;
        try { d = Dat.Load(Path.Combine(dir, "FDOTHER.DAT"), entry); }
        catch (Exception e) when (e is IOException or ArgumentOutOfRangeException or OverflowException) { return list; }
        if (d.Length < 14 || !d.AsSpan(0, 6).SequenceEqual("LLLLLL"u8)) return list;
        int n = (Dat.U32(d, 6) - 6) / 4 - 1;
        for (int i = 0; i < n; i++)
        {
            int a = Dat.U32(d, 6 + 4 * i), b = Dat.U32(d, 10 + 4 * i);
            list.Add(a >= 0 && b <= d.Length && b > a ? d[a..b] : []);
        }
        return list;
    }

    /// <summary>8비트 PCM 을 WAV 파일 꼴로(머리 44바이트).</summary>
    public static byte[] Wav(byte[] pcm, int rate = Rate)
    {
        var w = new byte[44 + pcm.Length];
        void S(int at, string s) { for (int i = 0; i < 4; i++) w[at + i] = (byte)s[i]; }
        void U32(int at, int v) => BitConverter.TryWriteBytes(w.AsSpan(at), v);
        void U16(int at, short v) => BitConverter.TryWriteBytes(w.AsSpan(at), v);
        S(0, "RIFF"); U32(4, 36 + pcm.Length); S(8, "WAVE");
        S(12, "fmt "); U32(16, 16); U16(20, 1); U16(22, 1); U32(24, rate); U32(28, rate); U16(32, 1); U16(34, 8);
        S(36, "data"); U32(40, pcm.Length);
        pcm.CopyTo(w, 44);
        return w;
    }
}
