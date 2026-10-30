namespace Fd2;

/// <summary>
/// 안드로이드의 갈고리 — 지도까지 CPU 로 찍고(GPU 지도 텍스처 · 고해상도 없음), 편집기 · 전체화면 · 스크린샷은 없다.
/// 판 목록 · 바꾸기 · 더하기는 시작 화면(MainActivity)이 넣어 준 대리자로.
/// </summary>
internal sealed partial class GameWindow
{
    private static readonly bool HasEditor = false;

    /// <summary>안내 글을 터치 기준으로.</summary>
    private static readonly bool TouchUi = true;

    /// <summary>가져온 판들(이름, 폴더) — MainActivity 가 채운다.</summary>
    internal static Func<List<(string Name, string Dir)>>? VersionsProvider;

    /// <summary>판을 바꿔 다시 켜기 — MainActivity 가 채운다.</summary>
    internal static Action<string>? VersionSwitcher;

    /// <summary>폴더 하나 더 가져오기 — MainActivity 가 채운다.</summary>
    internal static Action? VersionAdder;

    partial void PlatformInit() => _cpuMap = true;

    private partial uint[]? HiIcon(int icon, int frame) => null;

    private partial void OpenEditor() { }

    private partial List<(string Name, string Dir)> FindVersions() => VersionsProvider?.Invoke() ?? [(VersionName, _dir)];

    private partial void SwitchVersion(string dir)
    {
        _settings.GameDir = dir;
        _settings.Save();
        VersionSwitcher?.Invoke(dir);
    }

    private Android.Media.MediaPlayer? _player;
    private int _musicPlaying = -1;

    /// <summary>배경음악 — 곡을 MIDI 파일로 바꿔 두고 안드로이드 미디어 플레이어로 되풀이.</summary>
    partial void PlayMusic(int index)
    {
        if (index == _musicPlaying) return;
        StopMusic();
        try
        {
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "music");
            if (Fd2.Data.Music.MidiFile(_dir, index, cache) is not { } file) return;
            _player = new Android.Media.MediaPlayer();
            _player.SetDataSource(file);
            _player.Looping = true;
            _player.Prepare();
            _player.Start();
            _musicPlaying = index;
        }
        catch (Exception) { StopMusic(); }   // 소리가 안 나도 게임은 계속
    }

    partial void StopMusic()
    {
        try { _player?.Stop(); } catch (Java.Lang.IllegalStateException) { }
        _player?.Release();
        _player = null;
        _musicPlaying = -1;
    }

    private partial bool CanAddVersion() => VersionAdder != null;

    private partial void AddVersion() => VersionAdder?.Invoke();
}
