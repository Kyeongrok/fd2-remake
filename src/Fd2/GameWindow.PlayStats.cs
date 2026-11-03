using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Fd2;

/// <summary>
/// 익명 플레이 통계(사용자 요청 w-93) — 전투 한 판마다 장 · 이기고 짐(그만둠) · 한 턴 수 · 논 시간을 받는 곳(<c>tools/bug-worker</c> 의 <c>POST /v1/play</c>)에 보낸다.
/// 대시보드는 그 worker 의 <c>/dashboard</c>(PC · 폰 · 전투별).
/// </summary>
/// <remarks>
/// 판 = 전투를 새로 시작하거나 전황 · 자동저장을 불러온 때부터 이기거나 지거나 그만둘(타이틀 · 다른 판 · 앱 끄기) 때까지.
/// 시간은 전투 화면에서 손을 댄 지 3분 안인 동안만 센다(켜 두고 자리를 비운 것은 안 셈). 앱이 꺼져도 남도록 우리 차례마다 「하는 중」 파일에 적어 두고,
/// 다음에 켤 때 그만둔 판으로 보낸다. 설정 「익명 플레이 통계 보내기」를 끄면 적지도 보내지도 않는다. 이름 · 세이브 내용은 보내지 않는다.
/// </remarks>
internal sealed partial class GameWindow
{
    private sealed class PlayRun
    {
        public int Scene { get; set; }
        public int Chapter { get; set; }
        public string How { get; set; } = "new";   // new · load
        public int StartTurn { get; set; }          // 이 판에서 처음 한 턴 - 1
        public int EndTurn { get; set; }
        public double Seconds { get; set; }
        public string Result { get; set; } = "quit";   // win · lose · quit
        public string Started { get; set; } = "";
        public string Ended { get; set; } = "";
        public string Version { get; set; } = "";
        public string Platform { get; set; } = "";
        public bool MobileView { get; set; }
    }

    private static readonly JsonSerializerOptions PlayJson = new(JsonSerializerDefaults.Web);
    private const double PlayIdle = 180;
    private PlayRun? _play;
    private double _playInputAt;
    private double _playSavedAt;

    private static string PlayRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2");
    private static string PlayQueue => Path.Combine(PlayRoot, "play-queue");
    private static string PlayOpenPath => Path.Combine(PlayRoot, "play-open.json");

    private bool PlayOn => _settings.SendStats && !Offscreen;

    /// <summary>판 시작 — 새 전투(how = new) · 전황 불러오기(load). 하던 판이 있으면 그만둔 판으로 닫는다.</summary>
    private void PlayBegin(string how)
    {
        PlayEnd("quit");
        if (!PlayOn) return;
        _play = new PlayRun
        {
            Scene = _scene.Index, Chapter = ChapterOf(_scene.Index), How = how,
            StartTurn = how == "load" ? Math.Max(0, Turn - 1) : 0, EndTurn = Turn,
            Started = DateTime.UtcNow.ToString("o"), Version = AppVersion,
            Platform = OperatingSystem.IsAndroid() ? "android" : "windows", MobileView = TouchUi,
        };
        _playInputAt = _playSavedAt = _time;
    }

    /// <summary>손을 댔다 — 키 · 누르기.</summary>
    private void PlayTouched() => _playInputAt = _time;

    private void PlayTick(double dt)
    {
        if (_play is not { } p || !InBattle || _outcome != 0 || _time - _playInputAt > PlayIdle) return;
        p.Seconds += Math.Min(dt, 0.5);
    }

    /// <summary>판이 끝났다 — 이김(win) · 짐(lose) · 그만둠(quit). 줄에 세우고 뒤에서 보낸다.</summary>
    private void PlayEnd(string result)
    {
        if (_play is not { } p) return;
        _play = null;
        p.Result = result;
        p.EndTurn = Math.Max(p.EndTurn, Turn);
        p.Ended = DateTime.UtcNow.ToString("o");
        try
        {
            File.Delete(PlayOpenPath);
            if (p.Seconds < 5 && p.EndTurn <= p.StartTurn) return;   // 들어갔다 바로 나온 것은 안 센다
            Directory.CreateDirectory(PlayQueue);
            File.WriteAllText(Path.Combine(PlayQueue, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(p, PlayJson));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        UploadPlays();
    }

    /// <summary>하던 판을 적어 둔다 — 우리 차례마다 · 폰이 뒤로 갈 때(앱이 꺼져도 다음에 그만둔 판으로 보낸다).</summary>
    private void PlaySaveOpen()
    {
        if (_play is not { } p) return;
        p.EndTurn = Math.Max(p.EndTurn, Turn);
        p.Ended = DateTime.UtcNow.ToString("o");
        _playSavedAt = _time;
        try
        {
            Directory.CreateDirectory(PlayRoot);
            File.WriteAllText(PlayOpenPath, JsonSerializer.Serialize(p, PlayJson));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>켤 때 — 지난번 끝내지 못한 판을 그만둔 판으로 줄에 세우고, 줄을 보낸다.</summary>
    private void PlayStartup()
    {
        try
        {
            if (File.Exists(PlayOpenPath))
            {
                if (PlayOn)
                {
                    Directory.CreateDirectory(PlayQueue);
                    File.Move(PlayOpenPath, Path.Combine(PlayQueue, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"));
                }
                else File.Delete(PlayOpenPath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (PlayOn) UploadPlays();
    }

    private static readonly object PlayGate = new();
    private static bool _playUploading, _playAgain;

    private void UploadPlays()
    {
        if (!Directory.Exists(PlayQueue)) return;
        lock (PlayGate)
        {
            if (_playUploading) { _playAgain = true; return; }
            _playUploading = true;
        }
        string url = BugUrl, install = InstallId();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await UploadPlayQueue(url, install);
                lock (PlayGate)
                {
                    if (_playAgain) { _playAgain = false; continue; }
                    _playUploading = false;
                }
                return;
            }
        });
    }

    /// <summary>줄에 선 판들을 50 개씩 한 번에 — 받은 것 · 못 쓴다고 한 것(4xx, 429 빼고)은 지운다. 오래 못 보낸 것은 새 것 500 개만.</summary>
    private static async Task UploadPlayQueue(string url, string install)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var files = new DirectoryInfo(PlayQueue).GetFiles("*.json").OrderBy(f => f.Name).ToList();
            foreach (var old in files.SkipLast(500)) old.Delete();
            files = files.TakeLast(500).ToList();
            for (int at = 0; at < files.Count; at += 50)
            {
                var chunk = files.Skip(at).Take(50).ToList();
                var plays = new List<JsonElement>();
                foreach (var f in chunk)
                {
                    try { plays.Add(JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(f.FullName))); }
                    catch (JsonException) { f.Delete(); }
                }
                if (plays.Count == 0) continue;
                using var content = new StringContent(JsonSerializer.Serialize(new { v = 1, install, plays }), Encoding.UTF8, "application/json");
                using var reply = await http.PostAsync(url.TrimEnd('/') + "/v1/play", content);
                int code = (int)reply.StatusCode;
                if (reply.IsSuccessStatusCode || code is >= 400 and < 500 and not 429 and not 404)   // 404 = 받는 곳이 아직 안 올라감 — 남겨 둔다
                    foreach (var f in chunk.Where(f => f.Exists)) f.Delete();
                else return;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { }
    }
}
