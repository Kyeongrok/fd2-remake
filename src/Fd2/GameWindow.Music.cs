using System.Runtime.InteropServices;
using System.Text;
using Fd2.Data;

namespace Fd2;

/// <summary>
/// 윈도우의 배경음악 — 곡(FDMUS.DAT 의 XMIDI)을 %LOCALAPPDATA%\Fd2\music 에 MIDI 파일로 바꿔 두고 MCI 시퀀서로 튼다(윈도우 기본 GS 신시사이저).
/// MCI 는 되풀이를 못 해서 MusicTick 에서 멈췄으면 처음부터 다시.
/// 크기(설정 소리 크기)는 MCI 시퀀서에 없어서 곡의 노트 세기를 줄인 사본을 튼다 — 크기가 바뀌면 MusicTick 이 그 자리부터 다시 연다.
/// </summary>
internal sealed partial class GameWindow
{
    private const string MusicAlias = "fd2bgm";
    private int _musicPlaying = -1;
    private double _musicCheck;

    /// <summary>지금 연 곡 파일(크기를 줄이기 전)과 그 크기.</summary>
    private string? _musicFile;
    private int _musicVolume = 100;

    partial void PlayMusic(int index)
    {
        if (index == _musicPlaying) return;
        StopMusic();
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "music");
        string? file;
        try { file = Music.MidiFile(_dir, index, cache); }
        catch (Exception e) when (e is IOException or InvalidDataException or IndexOutOfRangeException or UnauthorizedAccessException) { file = null; }
        if (file == null) return;
        if (!OpenMusic(file)) return;
        Mci($"play {MusicAlias} from 0");
        _musicPlaying = index;
    }

    /// <summary>곡을 지금 크기로 연다(시각은 밀리초).</summary>
    private bool OpenMusic(string file)
    {
        _musicFile = file;
        _musicVolume = _settings.MusicVolume;
        string path = file;
        if (_musicVolume < 100)
        {
            path = Path.Combine(Path.GetDirectoryName(file)!, "volume.mid");
            try { File.WriteAllBytes(path, ScaleMidi(File.ReadAllBytes(file), Math.Sqrt(_musicVolume / 100.0))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { path = file; }
        }
        if (Mci($"open \"{path}\" type sequencer alias {MusicAlias}") != 0) return false;
        Mci($"set {MusicAlias} time format milliseconds");
        return true;
    }

    /// <summary>
    /// 노트 온 세기를 amp 배로(GM 신시사이저는 세기의 제곱쯤으로 소리가 커서, 크기 v 에 √v 를 주면 소리 크기가 v 에 비례).
    /// Music.ToMidi 가 만든 SMF 0(트랙 하나, 상태 바이트 생략 없음)만 읽는다 — 다른 꼴이면 그대로.
    /// </summary>
    private static byte[] ScaleMidi(byte[] m, double amp)
    {
        if (m.Length < 22 || m[14] != 'M' || m[15] != 'T' || m[16] != 'r' || m[17] != 'k') return m;
        var o = (byte[])m.Clone();
        int p = 22;
        int VarLen() { int v = 0; byte b; do { b = o[p++]; v = v << 7 | b & 0x7F; } while ((b & 0x80) != 0 && p < o.Length); return v; }
        while (p < o.Length)
        {
            VarLen();
            if (p >= o.Length) break;
            byte s = o[p++];
            if (s < 0x80) return m;
            if (s == 0xFF) { p++; int n = VarLen(); p += n; }
            else if (s is 0xF0 or 0xF7) { int n = VarLen(); p += n; }
            else if ((s & 0xF0) is 0xC0 or 0xD0) p++;
            else
            {
                if ((s & 0xF0) == 0x90 && p + 1 < o.Length && o[p + 1] > 0)
                    o[p + 1] = (byte)Math.Clamp((int)Math.Round(o[p + 1] * amp), amp > 0 ? 1 : 0, 127);
                p += 2;
            }
        }
        return o;
    }

    partial void StopMusic()
    {
        if (_musicPlaying < 0) return;
        Mci($"stop {MusicAlias}");
        Mci($"close {MusicAlias}");
        _musicPlaying = -1;
        _musicFile = null;
    }

    partial void MusicTick()
    {
        if (_musicPlaying < 0 || _time - _musicCheck < 0.3) return;
        _musicCheck = _time;
        var mode = new StringBuilder(32);
        if (_musicVolume != _settings.MusicVolume && _musicFile is { } file)
        {
            // 크기가 바뀌었다 — 줄인 사본으로 바꿔 듣던 자리부터
            long at = mciSendStringW($"status {MusicAlias} position", mode, mode.Capacity, IntPtr.Zero) == 0 && long.TryParse(mode.ToString(), out long pos) ? pos : 0;
            Mci($"stop {MusicAlias}");
            Mci($"close {MusicAlias}");
            if (!OpenMusic(file)) { _musicPlaying = -1; return; }
            Mci($"play {MusicAlias} from {at}");
            return;
        }
        if (mciSendStringW($"status {MusicAlias} mode", mode, mode.Capacity, IntPtr.Zero) == 0 && mode.ToString() == "stopped")
            Mci($"play {MusicAlias} from 0");
    }

    private static int Mci(string command) => mciSendStringW(command, null, 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendStringW(string command, StringBuilder? buffer, int bufferSize, IntPtr callback);
}
