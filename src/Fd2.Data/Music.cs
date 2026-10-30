namespace Fd2.Data;

/// <summary>
/// 배경음악 — FDMUS.DAT 의 곡들(Miles AIL 의 XMIDI)을 보통 MIDI(SMF 0)로 바꾼다.
/// 몇 번 항목은 3바이트 빈칸이고, 곡은 1 · 3 · 4 · 6 · 8 · 10 ~ 19 번.
/// XMIDI: 「FORM XDIR」 → 「CAT XMID」 → 「FORM XMID」 안의 EVNT 덩어리. 시간은 1/120초 단위(쉬는 시간은 0x80 보다 작은 바이트들의 합),
/// 노트 온 뒤에 길이(가변 길이 수)가 붙고 노트 오프는 없다, 상태 바이트 생략(running status)도 없다.
/// </summary>
public static class Music
{
    /// <summary>XMIDI 의 시간 단위 — 1초에 120.</summary>
    private const int TicksPerSecond = 120;

    public static int Count(string dir) => Dat.Count(Path.Combine(dir, "FDMUS.DAT"));

    /// <summary>곡이 든 항목인가(빈칸 아닌).</summary>
    public static bool Has(string dir, int index) =>
        index >= 0 && index < Count(dir) && Dat.Load(Path.Combine(dir, "FDMUS.DAT"), index).Length > 16;

    /// <summary>곡 index 를 MIDI 파일 바이트로. 곡이 아니면 null.</summary>
    public static byte[]? ToMidi(string dir, int index)
    {
        if (!Has(dir, index)) return null;
        return ToMidi(Dat.Load(Path.Combine(dir, "FDMUS.DAT"), index));
    }

    /// <summary>
    /// MT-32 기본 음색 번호 → GM 악기 번호(널리 쓰는 대응표, ScummVM 의 MT-32 → GM 표와 같은 값). 원작 곡은 MT-32 음색으로 짜여 있어
    /// GM 신시사이저에서 번호 그대로 틀면 악기가 엉뚱하게 들린다(분석-음악). 곡이 따로 올리는 음색(TIMB)은 대응이 없어 가까운 기본 음색으로 남는다.
    /// </summary>
    public static readonly byte[] MtToGm =
    [
        0, 1, 0, 2, 4, 4, 5, 3, 16, 17, 18, 16, 16, 19, 20, 21,
        6, 6, 6, 7, 7, 7, 8, 112, 62, 62, 63, 63, 38, 38, 39, 39,
        88, 95, 52, 98, 97, 99, 14, 54, 102, 96, 53, 102, 81, 100, 14, 80,
        48, 48, 49, 45, 41, 40, 42, 42, 43, 46, 45, 24, 25, 28, 27, 104,
        32, 32, 34, 33, 36, 37, 35, 35, 79, 73, 72, 72, 74, 75, 64, 65,
        66, 67, 71, 71, 68, 69, 70, 22, 56, 59, 57, 57, 60, 60, 58, 61,
        61, 11, 11, 98, 14, 9, 14, 13, 12, 107, 107, 77, 78, 78, 76, 76,
        47, 117, 127, 118, 118, 116, 115, 119, 115, 112, 55, 124, 123, 0, 14, 117,
    ];

    /// <summary>
    /// 그 곡의 MIDI 파일을 cacheDir 에 만들어 두고 경로를 준다(이미 있으면 그대로) — 곡이 아니면 null.
    /// 파일 이름에 원본 크기를 넣어, 판이 바뀌어 곡이 달라지면 새로 만든다.
    /// </summary>
    public static string? MidiFile(string dir, int index, string cacheDir)
    {
        if (!Has(dir, index)) return null;
        var xmi = Dat.Load(Path.Combine(dir, "FDMUS.DAT"), index);
        string path = Path.Combine(cacheDir, $"fdmus{index:D2}-{xmi.Length}-gm.mid");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(cacheDir);
            File.WriteAllBytes(path, ToMidi(xmi));
        }
        return path;
    }

    public static byte[] ToMidi(byte[] xmi)
    {
        int evnt = Find(xmi, "EVNT"u8);
        if (evnt < 0) throw new InvalidDataException("XMIDI 에 EVNT 가 없습니다");
        int len = xmi[evnt + 4] << 24 | xmi[evnt + 5] << 16 | xmi[evnt + 6] << 8 | xmi[evnt + 7];
        int p = evnt + 8, end = Math.Min(xmi.Length, p + len);

        // (시각, 차례, 바이트들) — 같은 시각이면 노트 오프를 먼저
        var events = new List<(long Time, int Order, byte[] Bytes)>();
        long time = 0;
        while (p < end)
        {
            byte b = xmi[p];
            if (b < 0x80)
            {
                time += b;
                p++;
                continue;
            }
            p++;
            int kind = b & 0xF0;
            switch (kind)
            {
                case 0x90:
                {
                    byte note = xmi[p++], vel = xmi[p++];
                    long dur = VarLen(xmi, ref p);
                    events.Add((time, 1, [b, note, vel]));
                    events.Add((time + dur, 0, [(byte)(0x80 | b & 0x0F), note, 0x40]));
                    break;
                }
                case 0xC0 when (b & 0x0F) != 9:   // 악기 — MT-32 번호를 GM 번호로(드럼 갈래 10 은 그대로)
                    events.Add((time, 1, [b, MtToGm[xmi[p++] & 0x7F]]));
                    break;
                case 0xC0 or 0xD0:
                    events.Add((time, 1, [b, xmi[p++]]));
                    break;
                case 0x80 or 0xA0 or 0xB0 or 0xE0:
                    events.Add((time, 1, [b, xmi[p++], xmi[p++]]));
                    break;
                default:
                    if (b == 0xFF)
                    {
                        byte type = xmi[p++];
                        int n = (int)VarLen(xmi, ref p);
                        // 끝 · 빠르기는 버린다 — 끝은 맨 뒤에 새로 달고, XMIDI 는 늘 1초에 120 틱으로 돈다
                        if (type is not (0x2F or 0x51)) events.Add((time, 1, [0xFF, type, .. VarLenBytes(n), .. xmi.AsSpan(p, n)]));
                        p += n;
                        if (type == 0x2F) p = end;
                    }
                    else if (b is 0xF0 or 0xF7)
                    {
                        int n = (int)VarLen(xmi, ref p);
                        events.Add((time, 1, [b, .. VarLenBytes(n), .. xmi.AsSpan(p, n)]));
                        p += n;
                    }
                    break;
            }
        }

        // 한 트랙(SMF 0): 사분음표 = 60 틱, 빠르기 500000µs(120 BPM) → 1초에 120 틱
        var track = new List<byte> { 0, 0xFF, 0x51, 3, 0x07, 0xA1, 0x20 };
        long last = 0;
        foreach (var e in events.Select((e, i) => (e, i)).OrderBy(x => x.e.Time).ThenBy(x => x.e.Order).ThenBy(x => x.i).Select(x => x.e))
        {
            track.AddRange(VarLenBytes((int)(e.Time - last)));
            track.AddRange(e.Bytes);
            last = e.Time;
        }
        track.AddRange([0, 0xFF, 0x2F, 0]);

        var midi = new List<byte>();
        midi.AddRange("MThd"u8.ToArray());
        midi.AddRange([0, 0, 0, 6, 0, 0, 0, 1, 0, TicksPerSecond / 2]);
        midi.AddRange("MTrk"u8.ToArray());
        midi.AddRange([(byte)(track.Count >> 24), (byte)(track.Count >> 16), (byte)(track.Count >> 8), (byte)track.Count]);
        midi.AddRange(track);
        return midi.ToArray();
    }

    /// <summary>곡의 길이(초) — 고르기 · 시험용.</summary>
    public static double Seconds(byte[] xmi)
    {
        int evnt = Find(xmi, "EVNT"u8);
        if (evnt < 0) return 0;
        int p = evnt + 8;
        long time = 0, endTime = 0;
        while (p < xmi.Length)
        {
            byte b = xmi[p++];
            if (b < 0x80) { time += b; continue; }
            switch (b & 0xF0)
            {
                case 0x90: p += 2; endTime = Math.Max(endTime, time + VarLen(xmi, ref p)); break;
                case 0xC0 or 0xD0: p++; break;
                case 0x80 or 0xA0 or 0xB0 or 0xE0: p += 2; break;
                default:
                    if (b == 0xFF) { byte t = xmi[p++]; int n = (int)VarLen(xmi, ref p); p += n; if (t == 0x2F) p = xmi.Length; }
                    else { int n = (int)VarLen(xmi, ref p); p += n; }
                    break;
            }
        }
        return Math.Max(time, endTime) / (double)TicksPerSecond;
    }

    /// <summary>곡에 쓰인 악기(GM 프로그램 번호, 타악기 채널 10 은 빼고) — 처음 나온 차례로.</summary>
    public static List<int> Programs(byte[] xmi)
    {
        var list = new List<int>();
        int evnt = Find(xmi, "EVNT"u8);
        if (evnt < 0) return list;
        int p = evnt + 8;
        while (p < xmi.Length)
        {
            byte b = xmi[p++];
            if (b < 0x80) continue;
            switch (b & 0xF0)
            {
                case 0x90: p += 2; VarLen(xmi, ref p); break;
                case 0xC0:
                    if ((b & 0x0F) != 9 && !list.Contains(MtToGm[xmi[p] & 0x7F])) list.Add(MtToGm[xmi[p] & 0x7F]);   // GM 으로 옮긴 번호
                    p++;
                    break;
                case 0xD0: p++; break;
                case 0x80 or 0xA0 or 0xB0 or 0xE0: p += 2; break;
                default:
                    if (b == 0xFF) { byte t = xmi[p++]; int n = (int)VarLen(xmi, ref p); p += n; if (t == 0x2F) p = xmi.Length; }
                    else { int n = (int)VarLen(xmi, ref p); p += n; }
                    break;
            }
        }
        return list;
    }

    /// <summary>GM 악기 이름(프로그램 번호 0~127).</summary>
    public static readonly string[] GmNames =
    [
        "Acoustic Grand Piano", "Bright Piano", "Electric Grand", "Honky-tonk", "E.Piano 1", "E.Piano 2", "Harpsichord", "Clavinet",
        "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
        "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Bandoneon",
        "Nylon Guitar", "Steel Guitar", "Jazz Guitar", "Clean Guitar", "Muted Guitar", "Overdrive Guitar", "Distortion Guitar", "Guitar Harmonics",
        "Acoustic Bass", "Finger Bass", "Pick Bass", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
        "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato", "Harp", "Timpani",
        "Strings", "Slow Strings", "Synth Strings 1", "Synth Strings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
        "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass", "Synth Brass 1", "Synth Brass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
        "Piccolo", "Flute", "Recorder", "Pan Flute", "Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Square Lead", "Saw Lead", "Calliope", "Chiff", "Charang", "Voice Lead", "Fifths", "Bass+Lead",
        "New Age Pad", "Warm Pad", "Polysynth", "Choir Pad", "Bowed Pad", "Metallic Pad", "Halo Pad", "Sweep Pad",
        "Rain", "Soundtrack", "Crystal", "Atmosphere", "Brightness", "Goblins", "Echoes", "Sci-fi",
        "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bagpipe", "Fiddle", "Shanai",
        "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
        "Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone", "Helicopter", "Applause", "Gunshot",
    ];

    private static int Find(byte[] data, ReadOnlySpan<byte> tag) => data.AsSpan().IndexOf(tag);

    private static long VarLen(byte[] d, ref int p)
    {
        long v = 0;
        byte b;
        do
        {
            b = d[p++];
            v = v << 7 | (uint)(b & 0x7F);
        } while ((b & 0x80) != 0 && p < d.Length);
        return v;
    }

    private static byte[] VarLenBytes(int v)
    {
        var bytes = new List<byte> { (byte)(v & 0x7F) };
        while ((v >>= 7) > 0) bytes.Insert(0, (byte)(v & 0x7F | 0x80));
        return bytes.ToArray();
    }
}
