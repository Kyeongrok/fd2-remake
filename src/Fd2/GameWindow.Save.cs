using Fd2.Data;

namespace Fd2;

/// <summary>
/// 장 이어가기와 기록(분석-세이브) — 원작 FD2.SAV 꼴 그대로 버전마다 %LOCALAPPDATA%\Fd2\saves\버전 이름\FD2.SAV 에 쓴다.
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

    /// <summary>
    /// 기록은 버전마다 따로 — %LOCALAPPDATA%\Fd2\saves\버전 이름\FD2.SAV. 그 버전 기록이 아직 없으면
    /// 예전에 모든 버전이 함께 쓰던 %LOCALAPPDATA%\Fd2\FD2.SAV 를 한 번 베껴 온다(예전 것은 그대로 둔다).
    /// </summary>
    private string SavePath => _savePath ??= VersionSavePath();

    private string? _savePath;

    private string VersionSavePath()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2");
        string name = string.Concat(VersionName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        string path = Path.Combine(root, "saves", name, "FD2.SAV"), shared = Path.Combine(root, "FD2.SAV");
        try
        {
            if (!File.Exists(path) && File.Exists(shared))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(shared, path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return path;
    }

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
        if (_campaign is null) return;
        PlayOrDeploy();
    }

    /// <summary>판정이 났다 — 이긴 장면이면 동료 표를 고치고 다음 장면으로 넘길 차례를 정한다.</summary>
    /// <summary>싸움을 시작할 때의 동료 표 — 지면 이리로 되돌린다.</summary>
    private List<byte[]>? _partyAtStart;

    private void OnOutcome(int outcome)
    {
        _outcome = outcome;
        PlayEnd(outcome == 2 ? "win" : "lose");
        if (outcome == 1 && _campaign is { } lost && _partyAtStart != null)
        {
            lost.Party.Clear();
            lost.Party.AddRange(_partyAtStart.Select(r => (byte[])r.Clone()));
            if (_poolAtStart != null) { lost.Shared.Clear(); lost.Shared.AddRange(_poolAtStart); }
        }
        if (outcome != 2 || _campaign is not { } c) return;
        c.AfterVictory(_scene.Units, addNew: !UseEvents);
        c.Gold = _battle.Gold;
        c.NextScene = _scene.Index == Campaign.FinalScene ? Campaign.FinalScene + 1 : Campaign.NextOf(_scene.Index);
    }

    /// <summary>판정 뒤 메뉴 — 이기면 다음 장 · 기록 · 타이틀, 지면 다시 · 타이틀.</summary>
    private void OpenOverMenu()
    {
        _stage = MenuStage.Over;
        _menu.Clear();
        ClearSelection();
        if (_outcome == 2 && _endingKind != 0)
        {
            _menu.Add(("끝 — 엔딩 보기", StartEnding, default));
        }
        else if (_outcome == 2 && _campaign is { } c)
        {
            // 다음 장 앞에 마을이 있으면 묻지 않고 바로 마을로(사용자 요청, 원작처럼) — 기록은 주점, 다음 장은 출구
            if (c.NextScene < _sceneCount && c.NextScene <= Campaign.FinalScene && HasTown && _tables?.ShopFor(c.NextScene) != null)
            {
                OpenTownMenu();
                return;
            }
            if (c.NextScene < _sceneCount && c.NextScene <= Campaign.FinalScene)
            {
                _menu.Add(($"다음 장으로 — {ChapterName(c.NextScene)}", PlayCampaign, default));
                if (HasTown) _menu.Add(($"마을 — 주점 · 상점 · 교회 (돈 {c.Gold}원)", OpenTownMenu, default));
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

    private void WriteSlot(int k, Action? after = null)
    {
        after ??= OpenOverMenu;
        if (_campaign is not { } c) return;
        var save = SaveFile.Load(SavePath);
        c.Music = (byte)(_settings.Music ? 1 : 0);
        save.WriteSlot(k, c);
        try
        {
            save.Save(SavePath);
            _hasSaves = null;
            ShowBanner($"기록 {k + 1} 에 남겼습니다", after, 0.9);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner($"기록하지 못했습니다 — {e.Message}", after, 1.5);
        }
    }

    private static string SlotDetail(Campaign c) => $"동료 {c.Party.Count}명 · 돈 {c.Gold}";

    private string SlotLabel(SaveFile save, int k) => save.ReadSlot(k) is { } c
        ? $"{ChapterName(c.NextScene)} 앞 · 동료 {c.Party.Count}명 · 돈 {c.Gold}"
        : "빈 칸";

    // ── 타이틀의 이어하기 ────────────────────────────────────────────────────

    // ── 싸움 중 기록(전황기록) ───────────────────────────────────────────────

    /// <summary>손으로 기록하는 전황 칸 수 — 1번은 FD2.SAV 의 전황(원작과 같은 자리), 2번부터는 따로 파일.</summary>
    private const int BattleSlots = 8;

    /// <summary>자동저장 — 우리 차례가 열릴 때마다 턴 번호 % 3 칸에(최근 3턴이 남는다).</summary>
    private const int AutoSlots = 3;

    private string BattleSlotPath(bool auto, int k) =>
        !auto && k == 0 ? SavePath : Path.Combine(Path.GetDirectoryName(SavePath)!, auto ? $"자동{k + 1}.SAV" : $"전황{k + 1}.SAV");

    private static BattleRecord? ReadBattleAt(string path) => File.Exists(path) ? SaveFile.Load(path).ReadBattle() : null;

    /// <summary>부를 수 있는 전황 — 손 기록 칸들, 그다음 자동저장(새것부터).</summary>
    private List<(string Label, BattleRecord Rec)> BattleSaves()
    {
        var list = new List<(string, BattleRecord)>();
        for (int k = 0; k < BattleSlots; k++)
            if (ReadBattleAt(BattleSlotPath(false, k)) is { } b && b.Scene < _sceneCount)
                list.Add(($"전황 {k + 1} — {ChapterName(b.Scene)} {b.Turn}턴 · {SavedAt(BattleSlotPath(false, k))}", b));
        var autos = Enumerable.Range(0, AutoSlots).Select(k => (k, b: ReadBattleAt(BattleSlotPath(true, k))))
            .Where(x => x.b != null && x.b.Scene < _sceneCount).OrderByDescending(x => File.GetLastWriteTime(BattleSlotPath(true, x.k)));
        foreach (var (k, b) in autos)
            list.Add(($"자동 — {ChapterName(b!.Scene)} {b.Turn}턴 · {SavedAt(BattleSlotPath(true, k))}", b));
        return list;
    }

    private static string SavedAt(string path) => File.GetLastWriteTime(path).ToString("MM-dd HH:mm");

    /// <summary>전황 칸 고르기 — save 면 기록할 칸(덮어쓰면 묻는다), 아니면 부를 기록.</summary>
    private void OpenBattleSlots(bool save)
    {
        _stage = MenuStage.Slots;
        _menu.Clear();
        _menuGlyphs.Clear(); _menuIcons.Clear();
        if (save)
        {
            for (int k = 0; k < BattleSlots; k++)
            {
                int slot = k;
                var b = ReadBattleAt(BattleSlotPath(false, k));
                string label = b != null && b.Scene < _sceneCount ? $"전황 {k + 1} — {ChapterName(b.Scene)} {b.Turn}턴 · {SavedAt(BattleSlotPath(false, k))}" : $"전황 {k + 1} — 빈 칸";
                _menu.Add((label, () => AskSys(TxtAskSave, () => SaveBattleRecord(BattleSlotPath(false, slot))), default));
            }
        }
        else
        {
            // 전황 · 자동저장에 더해 마을 기록(기록 1~4)과 원작 기록도 — 타이틀 Load 와 같은 목록(사용자 요청)
            foreach (var (label, camp) in ContinueSlots())
                _menu.Add((label, () => AskSys(TxtAskLoad, () => SysBanner(TxtWait, "기다리세요.", () => ContinueFrom(camp))), default));
        }
        _menu.Add(("돌아가기", OpenRecordMenu, default));
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    /// <summary>자동저장 — 우리 차례가 열릴 때, 턴 % 3 칸에 말없이(실패해도 놀이는 그대로).</summary>
    private void AutoSaveBattle()
    {
        if (!InBattle || _outcome != 0) return;
        PlaySaveOpen();
        SaveBattleRecord(BattleSlotPath(true, Turn % AutoSlots), quiet: true);
    }

    /// <summary>
    /// 지금 싸움을 기록한다(원작 시스템 메뉴의 전황기록, sub_19DF7) — 우리 차례에만. 마을 슬롯은 그대로 둔다.
    /// path 가 FD2.SAV 면 그 안의 전황 자리에, 아니면 전황만 든 따로 파일에. quiet 면 알림 없이(자동저장).
    /// </summary>
    private void SaveBattleRecord() => SaveBattleRecord(SavePath);

    private void SaveBattleRecord(string path, bool quiet = false)
    {
        var b = new BattleRecord
        {
            Scene = _scene.Index, Turn = Turn, Gold = _battle.Gold, ViewX = Math.Max(0, _camX / T), ViewY = Math.Max(0, _camY / T),
            CursorX = _cursor.X, CursorY = _cursor.Y, Info = InfoNow(), Music = (byte)(_settings.Music ? 1 : 0),
        };
        if (_campaign != null) b.Party.AddRange(_campaign.Party);
        else b.Party.AddRange(_scene.Units.Where(u => u.Side == Side.Player && !u.Ai).Select(HeroRecord.From));
        var bySlot = _scene.Units.Where(u => u.Slot >= 0).ToDictionary(u => u.Slot);
        for (int i = 0; i < _scene.SlotCount; i++) b.Units.Add(bySlot.TryGetValue(i, out var u) ? HeroRecord.From(u) : null);
        b.Chests.UnionWith(_battle.OpenedChests);
        b.Shared.AddRange(Pool);   // 공통 가방(세이브 옆 파일)
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var save = SaveFile.Load(path);
            save.WriteBattle(b);
            save.Save(path);
            _hasSaves = null;
            if (!quiet) SysBanner(TxtSaved, "전황을 기록했습니다", () => _bs = BattleState.Select);   // 원작 「전황기록완료」
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!quiet) ShowBanner($"기록하지 못했습니다 — {e.Message}", () => _bs = BattleState.Select, 1.5);
        }
    }

    /// <summary>지금 장면 정보 — 사건이 켠 사건 줄 차례를 넣는다(원작은 장면 정보를 그 자리에서 고친다).</summary>
    private byte[] InfoNow()
    {
        var info = (byte[])_scene.Info.Clone();
        for (int k = 0; k < _scene.EventLines.Count && 3 + 3 * k < info.Length; k++) info[3 + 3 * k] = (byte)_scene.EventLines[k].Turn;
        return info;
    }

    /// <summary>싸움 중 기록을 불러온다(sub_10010) — 그 장면을 띄우고 사람들을 기록대로, 우리 차례부터.</summary>
    private void LoadBattleRecord(BattleRecord b)
    {
        PlayEnd("quit");   // 하던 판이 있으면 장면을 바꾸기 전에 닫는다
        _continueOpen = false;
        _campaign = new Campaign { NextScene = b.Scene, Gold = b.Gold };
        _campaign.Party.AddRange(b.Party);
        _campaign.Shared.AddRange(b.Shared);
        SweepRecords();   // 옛 기록이면 동료 가방의 소모품을 공통 가방으로
        _partyAtStart = _campaign.Party.Select(r => (byte[])r.Clone()).ToList();
        _screen = Screen.Game;
        LoadScene(b.Scene, new List<byte[]>());
        var units = b.Units.Select((r, i) => (r, i))
            .Where(x => x.r != null && !HeroRecord.Gone(x.r))
            .Select(x => HeroRecord.Restore(x.r!, x.i, _scene.RoyalsMoved, _battle))
            .Where(u => _scene.Inside(u.X, u.Y) && u.Icon < _icons.Characters)
            .ToList();
        _scene.Restore(units, b.Units.Count);
        // 기록의 장면 정보에서 상자 내용(바꾼 물건)과 사건 줄을 되살린다
        for (int k = 0; k < _scene.Chests.Count && 85 + 3 * k < b.Info.Length; k++)
            _scene.SetChest(k, b.Info[83 + 3 * k], b.Info[84 + 3 * k] | b.Info[85 + 3 * k] << 8);
        for (int k = 0; k < _scene.EventLines.Count && 5 + 3 * k < b.Info.Length; k++) _scene.EventLines[k].Turn = b.Info[3 + 3 * k];
        _battle.Turn = Math.Max(1, b.Turn);
        _battle.Gold = b.Gold;
        _battle.OpenedChests.Clear();
        _battle.OpenedChests.UnionWith(b.Chests);
        _scene.ShowOpenedChests(_battle.OpenedChests);
        _forcedOutcome = 0;
        _enemyPhase = false;
        _outcome = 0;
        SetMusic(PlayerMusic);
        _selected = units.Find(u => u.Side == Side.Player);
        _cursor = (b.CursorX, b.CursorY);
        _centerCursor = true;
        _bs = BattleState.Select;
        SweepUnits();
        _poolAtStart = Pool.ToList();
        PlayBegin("load");   // 플레이 통계 — 불러온 판(w-93)
    }

    /// <summary>이어 할 수 있는 슬롯 — 리메이크 기록 넷, 그다음 원작 폴더의 기록 넷(있는 것만).</summary>
    private List<(string Label, Campaign Campaign)> ContinueSlots()
    {
        var list = new List<(string, Campaign)>();
        // 카드 큰 글은 장(전황은 「3장」, 기록은 「3장 앞」), 아랫줄에 어느 칸인지 · 턴 · 동료 · 돈(사용자 요청 w-105)
        var mine = SaveFile.Load(SavePath);
        for (int k = 0; k < BattleSlots; k++)
            if (ReadBattleAt(BattleSlotPath(false, k)) is { } b && b.Scene < _sceneCount)
                list.Add(($"{ChapterName(b.Scene)} — 전황 {k + 1} · {b.Turn}턴 · {SavedAt(BattleSlotPath(false, k))}", BattleCampaign(b)));
        var autos = Enumerable.Range(0, AutoSlots).Select(k => (k, b: ReadBattleAt(BattleSlotPath(true, k))))
            .Where(x => x.b != null && x.b.Scene < _sceneCount).OrderByDescending(x => File.GetLastWriteTime(BattleSlotPath(true, x.k)));
        foreach (var (k, b) in autos)
            list.Add(($"{ChapterName(b!.Scene)} — 자동 · {b.Turn}턴 · {SavedAt(BattleSlotPath(true, k))}", BattleCampaign(b)));
        for (int k = 0; k < SaveFile.Slots; k++)
            if (mine.ReadSlot(k) is { } c && c.NextScene < _sceneCount) list.Add(($"{ChapterName(c.NextScene)} 앞 — 기록 {k + 1} · {SlotDetail(c)}", c));
        if (File.Exists(OriginalSavePath))
        {
            var orig = SaveFile.Load(OriginalSavePath);
            if (orig.ReadBattle() is { } ob && ob.Scene < _sceneCount) list.Add(($"{ChapterName(ob.Scene)} — 원작 전황 · {ob.Turn}턴", BattleCampaign(ob)));
            for (int k = 0; k < SaveFile.Slots; k++)
                if (orig.ReadSlot(k) is { } c && c.NextScene < _sceneCount) list.Add(($"{ChapterName(c.NextScene)} 앞 — 원작 기록 {k + 1} · {SlotDetail(c)}", c));
        }
        return list;
    }

    private bool _continueOpen;
    private bool? _hasSaves;
    private int _continueRow;
    private List<(string Label, Campaign Campaign)> _continueList = new();

    /// <summary>싸움 중 「전황 부르기」 — 타이틀 Load 와 똑같은 불러오기 화면(전황 · 자동 · 기록 · 원작, 사용자 요청 w-105). 돌아가기면 싸움으로.</summary>
    private bool _loadFromGame;

    private void OpenLoadFromGame()
    {
        PlaySfx(Sound.UiBank, Sound.Menu);
        _menu.Clear();
        _bs = BattleState.Select;
        _titleBack ??= TitleImage();
        _loadFromGame = true;
        _screen = Screen.Title;
        OpenContinue();
        _titleIndex = 0;
    }

    /// <summary>불러오기 화면을 닫는다 — 싸움에서 왔으면 싸움으로, 아니면 타이틀 첫 메뉴로.</summary>
    private void CloseContinue()
    {
        _continueOpen = false;
        _titleIndex = 0;
        if (_loadFromGame) { _loadFromGame = false; _screen = Screen.Game; }
    }

    private void OpenContinue()
    {
        _continueList = ContinueSlots();
        _continueRow = 0;
        _loadTop = 0;
        _loadFollow = true;
        _continueOpen = true;
    }

    /// <summary>싸움 중 기록을 이어하기 목록에 넣는 꼴 — <see cref="Campaign.Battle"/> 이 있으면 그 싸움부터.</summary>
    private static Campaign BattleCampaign(BattleRecord b)
    {
        var c = new Campaign { NextScene = b.Scene, Gold = b.Gold, Battle = b };
        c.Shared.AddRange(b.Shared);
        return c;
    }

    private void ContinueFrom(Campaign c)
    {
        _loadFromGame = false;
        if (c.Battle is { } b) { LoadBattleRecord(b); return; }
        PlayEnd("quit");   // 싸움 중에 마을 기록을 부르면 하던 판은 그만둔 판으로
        _continueOpen = false;
        _campaign = c;
        // 다음 장 앞에 마을이 있으면 마을에서 이어 간다(기록은 마을 주점에서 한다, 사용자 제보) — 전장은 출구로
        if (c.NextScene < _sceneCount && HasTown && _tables?.ShopFor(c.NextScene) != null)
        {
            _screen = Screen.Game;
            LoadScene(c.NextScene, c.Ready.ToList());   // 마을 그림에 세울 앞장선 사람까지
            _battle.Gold = c.Gold;
            _outcome = 2;   // 마을 화면은 이긴 뒤 화면이다
            _endingKind = 0;
            _shopAt = 1;
            OpenTownMenu();
            return;
        }
        PlayCampaign();
    }
}
