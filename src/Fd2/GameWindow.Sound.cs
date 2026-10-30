using System.Runtime.InteropServices;
using Fd2.Data;

namespace Fd2;

/// <summary>윈도우 효과음 — winmm PlaySound(메모리 WAV, 비동기). 한 번에 하나라 새 소리가 앞 소리를 끊는다(원작 AIL 도 한 소리씩).</summary>
internal sealed partial class GameWindow
{
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySoundMem(IntPtr wav, IntPtr module, uint flags);

    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndMemory = 0x0004;

    private readonly Dictionary<int, List<byte[]>> _sfxBanks = new();
    private GCHandle _sfxPin;

    partial void PlaySfx(int entry, int index)
    {
        if (!_settings.Sound) return;
        if (!_sfxBanks.TryGetValue(entry, out var bank)) _sfxBanks[entry] = bank = Sound.Bank(_dir, entry).Select(p => p.Length > 0 ? Sound.Wav(p) : p).ToList();
        if (index < 0 || index >= bank.Count || bank[index].Length == 0) return;
        // 소리가 끝날 때까지 메모리가 움직이면 안 된다 — 다음 소리를 틀 때 앞 것을 놓는다
        var old = _sfxPin;
        _sfxPin = GCHandle.Alloc(bank[index], GCHandleType.Pinned);
        PlaySoundMem(_sfxPin.AddrOfPinnedObject(), IntPtr.Zero, SndAsync | SndNoDefault | SndMemory);
        if (old.IsAllocated) old.Free();
    }

    partial void StopSfx()
    {
        PlaySoundMem(IntPtr.Zero, IntPtr.Zero, 0);
        if (_sfxPin.IsAllocated) _sfxPin.Free();
    }
}
