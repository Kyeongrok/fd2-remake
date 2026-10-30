using Fd2.Data;

namespace Fd2;

/// <summary>
/// 장 이어가기와 기록(분석-세이브) — 원작 FD2.SAV 꼴 그대로 %LOCALAPPDATA%\Fd2\FD2.SAV 에 쓴다.
/// 새 게임은 사울 · 유니 · 아래스 · 카일로 1장(장면 0)부터, 이기면 동료 표를 고쳐 다음 장면(32 → 31 → 0 → … → 30)으로.
/// 이긴 뒤 원작의 마을처럼 마을 기록 슬롯 4개 가운데 하나에 기록할 수 있고, 타이틀의 「이어하기」가 슬롯을 읽는다.
/// 원작 폴더의 FD2.SAV 슬롯도 읽어 온다(쓰지는 않는다).
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>지금 하고 있는 이야기 — 새 게임 · 이어하기로 생기고, 지도 둘러보기에는 없다.</summary>
    private Campaign? _campaign;

    /// <summary>마지막 판정 — 2 이기면, 1 지면(<see cref="Battle.Judge"/>).</summary>
    private int _outcome;

    private static string SavePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "FD2.SAV");

    private string OriginalSavePath => Path.Combine(_dir, "FD2.SAV");

    /// <summary>장 번호 — 장면 0 이 1장. 프롤로그 장면(31 · 32)은 0.</summary>
    private static int ChapterOf(int scene) => scene is 31 or 32 ? 0 : scene + 1;

    private string ChapterName(int scene) => scene switch
    {
        31 or 32 => "프롤로그",
        _ => $"{ChapterOf(scene)}장",
    };

    /// <summary>새 이야기 — 프롤로그 끝(sub_3231B)의 넷으로 1장부터.</summary>
    private void NewCampaign()
    {
        _campaign = Campaign.NewGame(_battle);
        _campaign.NextScene = 0;
    }

    /// <summary>이야기의 다음 장면을 시작한다.</summary>
    private void PlayCampaign()
    {
        if (_campaign is not { } c) return;
        _screen = Screen.Game;
        StartBattle(c.NextScene);
    }

    /// <summary>판정이 났다 — 이긴 장면이면 동료 표를 고치고 다음 장면으로 넘길 차례를 정한다.</summary>
    private void OnOutcome(int outcome)
    {
        _outcome = outcome;
        if (outcome != 2 || _campaign is not { } c) return;
        c.AfterVictory(_scene.Units);
        c.Gold = _battle.Gold;
        c.NextScene = _scene.Index == Campaign.FinalScene ? Campaign.FinalScene + 1 : Campaign.NextOf(_scene.Index);
    }

    /// <summary>판정 뒤 메뉴 — 이기면 다음 장 · 기록 · 타이틀, 지면 다시 · 타이틀.</summary>
    private void OpenOverMenu()
    {
        _stage = MenuStage.Over;
        _menu.Clear();
        ClearSelection();
        if (_outcome == 2 && _campaign is { } c)
        {
            if (c.NextScene < _sceneCount && c.NextScene <= Campaign.FinalScene)
            {
                _menu.Add(($"다음 장으로 — {ChapterName(c.NextScene)}", PlayCampaign, default));
                _menu.Add(("기록하기", OpenSlotMenu, default));
            }
            else _menu.Add(("끝 — 이야기를 다 했습니다", ToTitle, default));
        }
        else if (_campaign != null) _menu.Add(("이 장 다시 하기", PlayCampaign, default));
        else _menu.Add(("다시 하기", () => StartBattle(_scene.Index), default));
        _menu.Add(("타이틀로", ToTitle, default));
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    /// <summary>기록할 슬롯 고르기 — 원작 마을의 기록(sub_2968D)처럼 네 칸.</summary>
    private void OpenSlotMenu()
    {
        var save = SaveFile.Load(SavePath);
        _stage = MenuStage.Over;
        _menu.Clear();
        for (int k = 0; k < SaveFile.Slots; k++)
        {
            int slot = k;
            _menu.Add(($"기록 {k + 1}: {SlotLabel(save, k)}", () => WriteSlot(slot), default));
        }
        _menu.Add(("돌아가기", OpenOverMenu, default));
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    private void WriteSlot(int k)
    {
        if (_campaign is not { } c) return;
        var save = SaveFile.Load(SavePath);
        c.Music = (byte)(_settings.Music ? 1 : 0);
        save.WriteSlot(k, c);
        try
        {
            save.Save(SavePath);
            _hasSaves = null;
            ShowBanner($"기록 {k + 1} 에 남겼습니다", OpenOverMenu, 0.9);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner($"기록하지 못했습니다 — {e.Message}", OpenOverMenu, 1.5);
        }
    }

    private string SlotLabel(SaveFile save, int k) => save.ReadSlot(k) is { } c
        ? $"{ChapterName(c.NextScene)} 앞 · 동료 {c.Party.Count}명 · 돈 {c.Gold}"
        : "빈 칸";

    // ── 타이틀의 이어하기 ────────────────────────────────────────────────────

    /// <summary>이어 할 수 있는 슬롯 — 리메이크 기록 넷, 그다음 원작 폴더의 기록 넷(있는 것만).</summary>
    private List<(string Label, Campaign Campaign)> ContinueSlots()
    {
        var list = new List<(string, Campaign)>();
        var mine = SaveFile.Load(SavePath);
        for (int k = 0; k < SaveFile.Slots; k++)
            if (mine.ReadSlot(k) is { } c && c.NextScene < _sceneCount) list.Add(($"기록 {k + 1} — {SlotLabel(mine, k)}", c));
        if (File.Exists(OriginalSavePath))
        {
            var orig = SaveFile.Load(OriginalSavePath);
            for (int k = 0; k < SaveFile.Slots; k++)
                if (orig.ReadSlot(k) is { } c && c.NextScene < _sceneCount) list.Add(($"원작 기록 {k + 1} — {SlotLabel(orig, k)}", c));
        }
        return list;
    }

    private bool _continueOpen;
    private bool? _hasSaves;
    private int _continueRow;
    private List<(string Label, Campaign Campaign)> _continueList = new();

    private void OpenContinue()
    {
        _continueList = ContinueSlots();
        _continueRow = 0;
        _continueOpen = true;
    }

    private void ContinueFrom(Campaign c)
    {
        _continueOpen = false;
        _campaign = c;
        PlayCampaign();
    }
}
