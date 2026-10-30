using System.Runtime.InteropServices;
using System.Text;
using Fd2.Data;

namespace Fd2;

/// <summary>
/// 윈도우의 배경음악 — 곡(FDMUS.DAT 의 XMIDI)을 %LOCALAPPDATA%\Fd2\music 에 MIDI 파일로 바꿔 두고 MCI 시퀀서로 튼다(윈도우 기본 GS 신시사이저).
/// MCI 는 되풀이를 못 해서 MusicTick 에서 멈췄으면 처음부터 다시.
/// </summary>
internal sealed partial class GameWindow
{
    private const string MusicAlias = "fd2bgm";
    private int _musicPlaying = -1;
    private double _musicCheck;

    partial void PlayMusic(int index)
    {
        if (index == _musicPlaying) return;
        StopMusic();
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "music");
        string? file;
        try { file = Music.MidiFile(_dir, index, cache); }
        catch (Exception e) when (e is IOException or InvalidDataException or IndexOutOfRangeException or UnauthorizedAccessException) { file = null; }
        if (file == null) return;
        if (Mci($"open \"{file}\" type sequencer alias {MusicAlias}") != 0) return;
        Mci($"play {MusicAlias} from 0");
        _musicPlaying = index;
    }

    partial void StopMusic()
    {
        if (_musicPlaying < 0) return;
        Mci($"stop {MusicAlias}");
        Mci($"close {MusicAlias}");
        _musicPlaying = -1;
    }

    partial void MusicTick()
    {
        if (_musicPlaying < 0 || _time - _musicCheck < 0.5) return;
        _musicCheck = _time;
        var mode = new StringBuilder(32);
        if (mciSendStringW($"status {MusicAlias} mode", mode, mode.Capacity, IntPtr.Zero) == 0 && mode.ToString() == "stopped")
            Mci($"play {MusicAlias} from 0");
    }

    private static int Mci(string command) => mciSendStringW(command, null, 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendStringW(string command, StringBuilder? buffer, int bufferSize, IntPtr callback);
}
