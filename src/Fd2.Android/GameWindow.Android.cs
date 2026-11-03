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

    /// <summary>폰은 제목 줄이 없어 버전 이름을 화면 왼쪽 위에 그린다.</summary>
    private static readonly bool VersionOnScreen = true;

    /// <summary>가져온 판들(이름, 폴더) — MainActivity 가 채운다.</summary>
    internal static Func<List<(string Name, string Dir)>>? VersionsProvider;

    /// <summary>판을 바꿔 다시 켜기 — MainActivity 가 채운다.</summary>
    internal static Action<string>? VersionSwitcher;

    /// <summary>화면 방향 바꾸기 — 값은 <see cref="Settings.ScreenLock"/>. MainActivity 가 넣어 준다.</summary>
    internal static Action<int>? ScreenLocker;

    partial void ApplyScreenLock() => ScreenLocker?.Invoke(_settings.ScreenLock);

    /// <summary>버그 리포트 글 쓰는 창(요약, 보내기) — MainActivity 가 채운다.</summary>
    internal static Action<string, Action<string>>? BugAsker;

    private partial void AskBugText(string summary, Action<string> send) => BugAsker?.Invoke(summary, send);

    /// <summary>폴더 하나 더 가져오기 — MainActivity 가 채운다.</summary>
    internal static Action? VersionAdder;

    // ── 고해상도(w-104) — PC 와 같은 모델 · 같은 캐시 꼴을 폰 CPU 로 차례로 만든다. 지도는 CPU 로 찍으므로 지도 전체 그림에 덮는다 ──

    private HiRes _hires = null!;

    partial void PlatformInit()
    {
        _cpuMap = true;
        _hires = new HiRes(_dir, _pal, _sceneCount, _icons.Characters, _settings.HiResPhone);
    }

    partial void OnSceneLoaded()
    {
        if (_hires.Enabled) _hires.Want(_scene.Index, _scene.Units.Select(u => u.Icon));
    }

    partial void PlatformTick()
    {
        // 다 된 조각 — 지금 장면이고 지도 전체 그림이 있으면 그 자리만 갈아 끼운다
        while (_hires.MapChunks.TryDequeue(out var c))
        {
            if (!_hires.Enabled || _scene == null || c.Scene != _scene.Index || _mapAll == null || _mapAllKey.Item1 != _scene || _mapAllKey.Item2 != _zoom) continue;
            PasteHi(c.Px, c.X, c.Y, c.W, c.H);
            _mapPrevKey = default;   // 앞 배율 그림은 옛것 — 다시 찍게
        }
    }

    private partial uint[]? HiIcon(int icon, int frame) => _hires.Enabled ? _hires.Icon(icon, frame) : null;

    partial void HiMapAll()
    {
        _mapHi = false;
        if (!_hires.Enabled || _mapAll == null) return;
        int lw = _scene.Width * T, lh = _scene.Height * T;
        try
        {
            if (HiRes.LoadMap(_scene.Index, lw, lh) is { } hi) PasteHi(hi, 0, 0, lw * HiRes.Scale, lh * HiRes.Scale);
        }
        catch (OutOfMemoryException) { }   // 큰 장면에서 메모리가 모자라면 원본 그대로
    }

    /// <summary>고해상도 그림 한 조각(4배 좌표 x, y, w x h)을 지금 배율의 지도 전체 그림에 — 가까운 점으로 줄이거나 늘려.</summary>
    private void PasteHi(uint[] px, int x, int y, int w, int h)
    {
        int z = _zoom, s = HiRes.Scale;
        int dx0 = (x * z + s - 1) / s, dx1 = Math.Min(_mapAllW, ((x + w) * z + s - 1) / s);
        int dy0 = (y * z + s - 1) / s, dy1 = Math.Min(_mapAllH, ((y + h) * z + s - 1) / s);
        for (int dy = dy0; dy < dy1; dy++)
        {
            int hy = dy * s / z - y;
            if (hy < 0 || hy >= h) continue;
            int src = hy * w, dst = dy * _mapAllW;
            for (int dx = dx0; dx < dx1; dx++)
            {
                int hx = dx * s / z - x;
                if (hx >= 0 && hx < w) _mapAll![dst + dx] = px[src + hx] | 0xFF000000;
            }
        }
        _mapHi = true;   // 물결 · 번갈아 타일은 고해상도 위에 그리지 않는다(PC 와 같이)
    }

    partial void HiResToggle()
    {
        _settings.HiResPhone = _hires.Enabled = !_hires.Enabled;
        _settings.Save();
        _mapAllKey = _mapPrevKey = default;   // 지도 전체 그림을 다시 찍는다
        _mapHi = false;
        if (_hires.Enabled && _scene != null) _hires.Want(_scene.Index, _scene.Units.Select(u => u.Icon));
    }

    partial void HiResInfo(ref string label)
    {
        if (_hires.Enabled && _hires.Status is { Length: > 0 } st) label += $" — {st}";
    }

    private partial void OpenEditor() { }

    /// <summary>글 줄 고르기 창 — MainActivity 가 채운다.</summary>
    internal static Action<string, string[], Action<int>>? ChoiceAsker;

    private partial void OpenCloudDialog()
    {
        if (!CloudSave.Configured) { Toast("세이브 연동이 아직 설정되지 않았습니다"); return; }
        if (!Cloud.LoggedIn) ChoiceAsker?.Invoke("세이브 연동(개발중)", ["구글 계정으로 로그인"], _ => CloudLogin());
        else ChoiceAsker?.Invoke("세이브 연동(개발중)", ["클라우드에 올리기", "클라우드에서 받기(이 기기 기록은 백업)", "구글 연결 끊기"],
                                 i => { if (i == 0) CloudUpload(); else if (i == 1) CloudDownload(); else CloudLogout(); });
    }

    private partial void OpenBrowser(string url) =>
        Android.App.Application.Context.StartActivity(new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(url)).AddFlags(Android.Content.ActivityFlags.NewTask));

    private partial List<(string Name, string Dir)> FindVersions() => VersionsProvider?.Invoke() ?? [(VersionName, _dir)];

    private partial void SwitchVersion(string dir)
    {
        _settings.GameDir = dir;
        _settings.Save();
        VersionSwitcher?.Invoke(dir);
    }

    // ── 배경음악 — 미디어 플레이어는 음악 스레드에서만 만진다 ──
    // 예전에는 게임 루프에서 곡을 바꿨다(Stop · Release · Prepare 를 기다림). MIDI 플레이어가 그 자리에서 가끔 돌아오지 않으면
    // 적 차례 → 우리 차례(곡이 바뀌는 때)에 게임이 통째로 멈췄다(w-88, 곡우 버전 폰 제보). 이제 걸려도 음악만 끊긴다.

    private Android.Media.MediaPlayer? _player;
    /// <summary>게임 쪽에서 본 곡 — 음악 스레드에 맡긴 마지막 곡(-1 이면 끔).</summary>
    private int _musicPlaying = -1;
    /// <summary>음악 스레드가 실제로 틀고 있는 곡.</summary>
    private volatile int _musicActual = -1;
    private Worker? _musicWorker, _sfxWorker;

    /// <summary>
    /// 미디어 플레이어 일을 하나씩 하는 뒤 스레드 — 게임 루프는 맡기기만 하고 기다리지 않는다.
    /// 1초 넘게 걸린 일 · 실패는 음악 기록(버그 리포트에 붙는다)에 남는다.
    /// </summary>
    private sealed class Worker
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(string What, Action Job)> _jobs = new();
        public volatile string Doing = "";

        public Worker(string name, Action<string> log)
        {
            new Thread(() =>
            {
                foreach (var (what, job) in _jobs.GetConsumingEnumerable())
                {
                    Doing = what;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try { job(); }
                    catch (Exception e) { log($"{what} 실패: {e.GetType().Name}: {e.Message}"); }   // 소리가 안 나도 게임은 계속
                    if (clock.Elapsed.TotalSeconds > 1) log($"{what} 느림 {clock.Elapsed.TotalSeconds:0.0}초");
                    Doing = "";
                }
            }) { IsBackground = true, Name = name }.Start();
        }

        /// <summary>맡긴다 — 밀린 일이 많으면(플레이어가 걸렸으면) 버려도 되는 일은 버린다.</summary>
        public void Add(string what, Action job, bool droppable = false)
        {
            if (droppable && _jobs.Count > 4) return;
            _jobs.Add((what, job));
        }
    }

    /// <summary>음악 일을 음악 스레드에 맡긴다.</summary>
    private void MusicJob(string what, Action job) => (_musicWorker ??= new Worker("fd2-music", MusicFail)).Add(what, job);

    /// <summary>배경음악 — 곡을 MIDI 파일로 바꿔 두고 안드로이드 미디어 플레이어로 되풀이.</summary>
    partial void PlayMusic(int index)
    {
        if (index == _musicPlaying) return;
        _musicPlaying = index;
        MusicJob($"곡 {index} 틀기", () =>
        {
            if (_musicPlaying != index) return;   // 그 사이 다른 곡으로 바뀌었다
            StopPlayer();
            try
            {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "music");
                if (Fd2.Data.Music.MidiFile(_dir, index, cache) is not { } file) { MusicFail($"곡 {index} 없음(FDMUS.DAT)"); return; }
                var p = new Android.Media.MediaPlayer();
                _player = p;
                p.SetDataSource(file);
                p.Looping = true;
                p.Prepare();
                float v = _settings.MusicVolume / 100f;
                p.SetVolume(v, v);
                if (!_away) p.Start();
                _musicActual = index;
            }
            catch (Exception e)   // 소리가 안 나도 게임은 계속 — 다음에 같은 곡을 부르면 다시 해 본다
            {
                MusicFail($"곡 {index} 재생 실패: {e.GetType().Name}: {e.Message}");
                StopPlayer();
                Interlocked.CompareExchange(ref _musicPlaying, -1, index);
            }
        });
    }

    /// <summary>버그 리포트에 붙일 지금 음악 상태 — 곡 번호와 음악 스레드가 하는 일(플레이어는 건드리지 않는다).</summary>
    private partial string MusicStatus() =>
        (_musicActual < 0 ? "안 트는 중" : $"곡 {_musicActual}") + (_musicPlaying != _musicActual ? $" → 곡 {_musicPlaying} 기다림" : "") +
        (_musicWorker?.Doing is { Length: > 0 } doing ? $" · 하는 중: {doing}" : "") +
        (_sfxWorker?.Doing is { Length: > 0 } sfx ? $" · 효과음 하는 중: {sfx}" : "");

    /// <summary>설정 소리 크기의 배경음 — 트는 곡에 바로.</summary>
    partial void ApplyVolume()
    {
        float v = _settings.MusicVolume / 100f;
        MusicJob("소리 크기", () => { try { _player?.SetVolume(v, v); } catch (Java.Lang.IllegalStateException) { } });
    }

    private volatile bool _away;

    /// <summary>앱이 뒤로 가면 배경음악을 멈추고, 돌아오면 이어서 — MainActivity.OnPause · OnResume.</summary>
    internal void Background(bool away)
    {
        _away = away;
        if (away) PlaySaveOpen();   // 뒤로 간 앱은 꺼질 수 있다 — 하던 판을 적어 둔다(플레이 통계)
        MusicJob(away ? "음악 쉬기" : "음악 잇기", () =>
        {
            try
            {
                if (away) { if (_player?.IsPlaying == true) _player.Pause(); }
                else if (_settings.Music) _player?.Start();
            }
            catch (Java.Lang.IllegalStateException) { }
        });
    }

    partial void StopMusic()
    {
        _musicPlaying = -1;
        MusicJob("음악 끄기", () => { if (_musicPlaying < 0) StopPlayer(); });
    }

    /// <summary>음악 스레드에서만 — 플레이어를 멈추고 놓는다.</summary>
    private void StopPlayer()
    {
        var p = _player;
        _player = null;
        _musicActual = -1;
        if (p == null) return;
        try { p.Stop(); } catch (Java.Lang.IllegalStateException) { }
        p.Release();
    }

    // ── 효과음 — 소리 묶음을 WAV 파일로 만들어 두고 미디어 플레이어 둘(원작 소리 갈래 A · B)로 ──

    // 플레이어는 효과음 스레드에서만 만진다(배경음악과 같은 까닭 — Prepare 가 걸리면 게임이 멈췄다)
    private Android.Media.MediaPlayer? _sfxA, _sfxB;

    partial void PlaySfx(int entry, int index) => SfxJob(entry, index, false);

    partial void PlaySfx2(int entry, int index) => SfxJob(entry, index, true);

    private void SfxJob(int entry, int index, bool second)
    {
        if (!_settings.Sound || _settings.SoundVolume <= 0) return;
        (_sfxWorker ??= new Worker("fd2-sfx", MusicFail)).Add($"효과음 {entry}-{index}", () =>
        {
            if (second) PlayWav(ref _sfxB, entry, index);
            else PlayWav(ref _sfxA, entry, index);
        }, droppable: true);
    }

    private void PlayWav(ref Android.Media.MediaPlayer? player, int entry, int index)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "sfx");
            string path = Path.Combine(dir, $"e{entry}_{index}.wav");
            if (!File.Exists(path))
            {
                var bank = Fd2.Data.Sound.Bank(_dir, entry);
                if (index < 0 || index >= bank.Count || bank[index].Length == 0) return;
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, Fd2.Data.Sound.Wav(bank[index]));
            }
            player?.Release();
            player = new Android.Media.MediaPlayer();
            player.SetDataSource(path);
            player.Prepare();
            player.SetVolume(_settings.SoundVolume / 100f, _settings.SoundVolume / 100f);
            player.Start();
        }
        catch (Exception) { }   // 소리가 안 나도 게임은 계속
    }

    partial void StopSfx() => _sfxWorker?.Add("효과음 끄기", () =>
    {
        _sfxA?.Release();
        _sfxB?.Release();
        _sfxA = _sfxB = null;
    });

    private partial bool CanAddVersion() => VersionAdder != null;

    private partial void AddVersion() => VersionAdder?.Invoke();
}
