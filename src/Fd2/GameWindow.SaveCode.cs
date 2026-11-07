using System.Net.Http;
using System.Text;
using System.Text.Json;
using Fd2.Data;

namespace Fd2;

/// <summary>
/// 세이브 공유(w-169 · w-171) — 불러오기 화면 아래 「세이브 공유」.
/// <list type="bullet">
/// <item>코드로 올리기 — 기록 하나를 버그 리포트 받는 곳(Cloudflare, <c>tools/bug-worker</c> 의 <c>POST /v1/save</c>)에 올리고 짧은 코드(「K7Q-29M」)를 받는다. 계정 없음, 3일 보관.</item>
/// <item>코드로 받기 — 다른 기기(PC · 폰 어느 쪽이든)에서 그 코드를 넣으면 빈 기록 칸에 들어온다.</item>
/// <item>(폰) 파일로 보내기 — 카톡 · 이메일로 세이브 파일 그대로(<see cref="SaveSharer"/>).</item>
/// </list>
/// 올리는 것은 고른 기록 하나만 든 원작 FD2.SAV 꼴 파일(+ 공통 가방)이라 받는 쪽의 다른 기록은 건드리지 않는다. 올려도 이 기기 기록은 그대로다.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>고르기 중 카드를 누르면 코드로 올린다(아니면 파일로 보낸다).</summary>
    private bool _pickForCode;

    /// <summary>뒤 스레드에서 기록을 받아 넣었으면 — 불러오기 목록을 다시 읽는다(게임 스레드에서).</summary>
    private volatile bool _continueDirty;

    private volatile bool _codeBusy;

    /// <summary>줄 고르기 창 — 플랫폼마다(PC 게임 꼴 창 · 안드로이드 대화 상자).</summary>
    private partial void AskChoice(string title, string[] items, Action<int> pick);

    /// <summary>한 줄 글 넣기 창 — 확인을 누르면 done(글).</summary>
    private partial void AskLine(string title, string message, string hint, Action<string> done);

    /// <summary>코드 알림 창 — 코드를 크게, 안내 글, 복사(폰은 보내기도).</summary>
    private partial void ShowSaveCode(string code, string message);

    private const string CodeGuide =
        "• 서버에 3일 동안만 보관됩니다 — 3일이 지나면 지워지니 얼른 받으세요.\n" +
        "• 받는 기기: 불러오기 → 세이브 공유 → 코드로 받기에 이 코드를 넣으세요(PC · 폰 어느 쪽이든).\n" +
        "• 올려도 이 기기의 기록은 지워지지 않습니다.";

    private void OpenSaveMove()
    {
        if (_shareMode) { _shareMode = false; PlaySfx(Sound.UiBank, Sound.Menu); return; }   // 고르기 중이면 그만
        PlaySfx(Sound.UiBank, Sound.Menu);
        var items = new List<(string Label, Action Do)>();
        if (_continueList.Count > 0) items.Add(("코드로 올리기 — 다른 기기(PC · 폰)에서 코드로 받기", () => { _pickForCode = true; _shareMode = true; }));
        items.Add(("코드로 받기", AskCodeToReceive));
        if (SaveSharer != null && _continueList.Count > 0) items.Add(("파일로 보내기 — 카톡 · 이메일 · 메신저", () => { _pickForCode = false; _shareMode = true; }));
        AskChoice("세이브 공유", items.Select(i => i.Label).ToArray(), i => { if (i >= 0 && i < items.Count) items[i].Do(); });
    }

    // ── 올리기 ──

    /// <summary>고른 카드를 그 기록 하나만 든 세이브로 만들어 올린다.</summary>
    private void UploadSaveCode(string label, Campaign c)
    {
        if (_codeBusy) { Toast("아직 올리거나 받는 중입니다"); return; }
        var one = SaveFile.New();
        string kind;
        if (c.Battle is { } b) { one.WriteBattle(b); kind = "battle"; }
        else { one.WriteSlot(0, c); kind = "slot"; }
        var body = new
        {
            v = 1, install = InstallId(), version = AppVersion, game = CloudKey, gameName = VersionName,
            label, kind, save = Convert.ToBase64String(one.ToBytes()), shared = one.SharedJson,
        };
        string url = BugUrl, json = JsonSerializer.Serialize(body);
        _codeBusy = true;
        Toast("올리는 중…");
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var res = await http.PostAsync($"{url}/v1/save", content);
                string text = await res.Content.ReadAsStringAsync();
                if ((int)res.StatusCode == 429)
                {
                    Toast(text.Contains("daily") ? "하루에 올릴 수 있는 수(50번)를 넘었습니다 — 내일 다시" : "잠깐 사이 너무 많이 올렸습니다 — 조금 뒤에 다시");
                    return;
                }
                if ((int)res.StatusCode == 503) { Toast("서버에 맡긴 세이브가 가득 찼습니다 — 나중에 다시"); return; }
                if (!res.IsSuccessStatusCode) { Toast($"올리지 못했습니다 — {(int)res.StatusCode}"); return; }
                using var doc = JsonDocument.Parse(text);
                string code = doc.RootElement.GetProperty("code").GetString() ?? "";
                string until = DateTime.TryParse(doc.RootElement.GetProperty("expires").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var e)
                    ? e.ToLocalTime().ToString("MM월 dd일 HH:mm") : "3일 뒤";
                ShowSaveCode(FormatCode(code), $"{label}\n\n{until} 까지 받을 수 있습니다.\n\n{CodeGuide}");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                Toast($"올리지 못했습니다 — {e.Message}");
            }
            finally { _codeBusy = false; }
        });
    }

    /// <summary>「K7Q29M」 → 「K7Q-29M」(읽기 쉽게).</summary>
    private static string FormatCode(string code) => code.Length == 6 ? $"{code[..3]}-{code[3..]}" : code;

    // ── 받기 ──

    private void AskCodeToReceive() =>
        AskLine("코드로 받기", "다른 기기에서 올린 세이브 코드를 넣으세요(대시 · 소문자 상관없음).\n받은 기록은 빈 칸에 들어가고, 이 기기의 다른 기록은 그대로입니다.", "예: K7Q-29M", ReceiveSaveCode);

    private void ReceiveSaveCode(string input)
    {
        string code = new string(input.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (code.Length != 6) { Toast("코드는 여섯 글자입니다"); return; }
        if (_codeBusy) { Toast("아직 올리거나 받는 중입니다"); return; }
        string url = BugUrl, key = CloudKey;
        _codeBusy = true;
        Toast("받는 중…");
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                using var res = await http.GetAsync($"{url}/v1/save/{code}");
                if ((int)res.StatusCode == 404) { Toast("그런 코드가 없습니다 — 3일이 지나 지워졌을 수도 있습니다"); return; }
                if ((int)res.StatusCode == 410) { Toast("3일이 지나 지워진 코드입니다"); return; }
                if (!res.IsSuccessStatusCode) { Toast($"받지 못했습니다 — {(int)res.StatusCode}"); return; }
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var r = doc.RootElement;
                string kind = r.GetProperty("kind").GetString() ?? "", label = r.GetProperty("label").GetString() ?? "";
                string? shared = r.TryGetProperty("shared", out var sh) && sh.ValueKind == JsonValueKind.String ? sh.GetString() : null;
                var file = SaveFile.FromBytes(Convert.FromBase64String(r.GetProperty("save").GetString() ?? ""), shared);
                if (file == null) { Toast("받은 세이브가 망가졌습니다"); return; }
                string where = kind == "battle" ? PutBattle(file) : PutSlot(file);
                bool other = !(r.GetProperty("game").GetString() ?? "").StartsWith(key, StringComparison.OrdinalIgnoreCase);
                _continueDirty = true;
                Toast(where.Length == 0 ? "넣을 빈 칸이 없습니다 — 기록 하나를 비우고 다시 받으세요"
                    : $"받았습니다 — {where}{(other ? " (다른 원작 버전에서 올린 기록)" : "")}");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                      or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
            {
                Toast($"받지 못했습니다 — {e.Message}");
            }
            finally { _codeBusy = false; }
        });
    }

    /// <summary>받은 싸움 중 기록을 빈 전황 칸(2 → 8, 그다음 1)에 — 넣은 칸 이름, 없으면 빈 글.</summary>
    private string PutBattle(SaveFile got)
    {
        if (got.ReadBattle() is not { } b) return "";
        foreach (int k in Enumerable.Range(1, BattleSlots - 1).Append(0))
        {
            string path = BattleSlotPath(false, k);
            if (ReadBattleAt(path) != null) continue;
            var save = SaveFile.Load(path);
            save.WriteBattle(b);
            save.Save(path);
            _hasSaves = null;
            return $"전황 {k + 1}";
        }
        return "";
    }

    /// <summary>받은 마을 기록을 빈 기록 칸(1 ~ 4)에.</summary>
    private string PutSlot(SaveFile got)
    {
        if (got.ReadSlot(0) is not { } c) return "";
        var mine = SaveFile.Load(SavePath);
        for (int k = 0; k < SaveFile.Slots; k++)
        {
            if (mine.ReadSlot(k) != null) continue;
            mine.WriteSlot(k, c);
            mine.Save(SavePath);
            _hasSaves = null;
            return $"기록 {k + 1}";
        }
        return "";
    }
}
