using System.Runtime.InteropServices;
using Fd2.Data;

namespace Fd2;

/// <summary>
/// 윈도우 효과음 — winmm PlaySound(메모리 WAV, 비동기). 한 번에 하나라 새 소리가 앞 소리를 끊는다(원작 AIL 도 한 소리씩).
/// 크기(설정 소리 크기)는 표본을 줄여서 — waveOutSetVolume 은 프로그램 소리 전체(배경음악 신시사이저까지)를 함께 바꾸므로 쓰지 않는다.
/// </summary>
internal sealed partial class GameWindow
{
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySoundMem(IntPtr wav, IntPtr module, uint flags);

    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndMemory = 0x0004;

    private readonly Dictionary<int, List<byte[]>> _sfxBanks = new();
    private GCHandle _sfxPin;

    /// <summary>8비트 WAV 의 표본을 설정 크기로 줄인 사본 — 100% 면 그대로.</summary>
    private byte[] ScaledWav(byte[] wav)
    {
        int v = _settings.SoundVolume;
        if (v >= 100 || wav.Length <= 44) return wav;
        var w = (byte[])wav.Clone();
        for (int i = 44; i < w.Length; i++) w[i] = (byte)(128 + (w[i] - 128) * v / 100);
        return w;
    }

    partial void PlaySfx(int entry, int index)
    {
        if (!_settings.Sound || _settings.SoundVolume <= 0) return;
        if (!_sfxBanks.TryGetValue(entry, out var bank)) _sfxBanks[entry] = bank = Sound.Bank(_dir, entry).Select(p => p.Length > 0 ? Sound.Wav(p) : p).ToList();
        if (index < 0 || index >= bank.Count || bank[index].Length == 0) return;
        // 소리가 끝날 때까지 메모리가 움직이면 안 된다 — 다음 소리를 틀 때 앞 것을 놓는다
        var old = _sfxPin;
        _sfxPin = GCHandle.Alloc(ScaledWav(bank[index]), GCHandleType.Pinned);
        PlaySoundMem(_sfxPin.AddrOfPinnedObject(), IntPtr.Zero, SndAsync | SndNoDefault | SndMemory);
        if (old.IsAllocated) old.Free();
    }

    private const string SfxAlias2 = "fd2sfxb";

    /// <summary>둘째 갈래 — MCI waveaudio 로(PlaySound 와 따로 섞여 난다). WAV 는 %LOCALAPPDATA%\Fd2\sfx 에 만들어 둔다.</summary>
    partial void PlaySfx2(int entry, int index)
    {
        if (!_settings.Sound || _settings.SoundVolume <= 0) return;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "sfx");
        string path = Path.Combine(dir, $"e{entry}_{index}.wav");
        try
        {
            if (!File.Exists(path))
            {
                var bank = Sound.Bank(_dir, entry);
                if (index < 0 || index >= bank.Count || bank[index].Length == 0) return;
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, Sound.Wav(bank[index]));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        Mci($"close {SfxAlias2}");
        if (_settings.SoundVolume < 100)
        {
            // 줄인 사본을 한 파일에 덮어 써서 튼다(앞 소리는 방금 닫았다)
            string scaled = Path.Combine(dir, "volume.wav");
            try { File.WriteAllBytes(scaled, ScaledWav(File.ReadAllBytes(path))); path = scaled; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        if (Mci($"open \"{path}\" type waveaudio alias {SfxAlias2}") == 0) Mci($"play {SfxAlias2}");
    }

    partial void StopSfx()
    {
        Mci($"close {SfxAlias2}");
        PlaySoundMem(IntPtr.Zero, IntPtr.Zero, 0);
        if (_sfxPin.IsAllocated) _sfxPin.Free();
    }
}
