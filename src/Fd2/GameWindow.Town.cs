using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 장 사이 마을(sub_26152) — 이기면 바로 들어온다(사용자 요청 — 예전엔 이긴 뒤 메뉴의 「마을」), 출구는 다음 장으로. 다음 장면 n17 앞에 마을이 있으면(byte_523E7[n17] = 0) 그 마을 기록 sub_4E809(n17)의
/// 무기점 · 도구점에서 사고팔고, 교회에서 부활 · 전직하고, 주점에서 동료 보기 · 기록 · 불러오기 · 전장 이탈을 한다. 원작의 건물 그림 · 걷기 없이 메뉴로만.
/// 첫 메뉴 줄은 원작 건물 차례(n5 0 주점 · 1 무기점 · 2 출구 · 3 도구점 · 4 교회) — 기록 +1 건물 줄에서 +2 키(Shift · Ctrl · Alt + F1~F10)를 누르면 비밀상점(n5 = 5)이 열린다.
/// 고친 것은 동료 표(<see cref="Campaign.Party"/>)와 돈에 바로 들어가, 그 뒤 기록하면 그대로 남는다.
/// </summary>
internal sealed partial class GameWindow
{
    private Town? _town;

    /// <summary>마을 메뉴에서 물릴 곳(Esc · 오른쪽 단추).</summary>
    private Action? _townBack;

    /// <summary>한 쪽에 보일 사람 수 — 동료가 많으면 쪽을 넘긴다.</summary>
    private const int TownPage = 10;

    /// <summary>첫 메뉴가 떠 있나 — 비밀상점 열쇠는 이때만 받는다.</summary>
    private bool _townTop;

    /// <summary>이번 마을에서 비밀상점을 찾았나(원작 n5 = 5) — 마을을 나가면 다시 숨는다.</summary>
    private bool _townSecret;

    /// <summary>지금 상점 — 1 무기점 · 3 도구점 · 5 비밀상점(원작 n5).</summary>
    private int _shopAt = 1;

    private bool HasTown => _campaign is { } c && _tables?.HasTown(c.NextScene) == true;

    private void BeginTownMenu(Action? back)
    {
        _stage = MenuStage.Town;
        _townTop = false;
        _menu.Clear();
        _menuGlyphs.Clear();
        _townBack = back;
        _townButtons = false;
        if (_campaign is { } c) _battle.Gold = c.Gold;
    }

    /// <summary>건물 안 첫 메뉴를 원작 네 단추로 — 줄 넷(단추 차례), 주인 말 줄.</summary>
    private void TownButtons(int greeting)
    {
        _townButtons = true;
        _townGreeting = greeting;
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }


    private void EndTownMenu(int row)
    {
        _menuIndex = Math.Clamp(row, 0, _menu.Count - 1);
        _bs = BattleState.Menu;
    }

    private void TownBanner(string text, Action after, int name = -1)
    {
        _menuGlyphs.Clear();
        ShowBanner(text, after, 1.0);
        _bannerName = name;
    }

    private string GoldLine(string what) => $"돈 {_campaign?.Gold ?? 0}원 — {what}";

    /// <summary>마을 첫 메뉴 — 원작 건물 차례(주점 · 무기점 · 출구 · 도구점 · 교회), 찾았으면 비밀상점. row = 건물 n5.</summary>
    private void OpenTownMenu() => OpenTownMenu(-1);

    private void OpenTownMenu(int building)
    {
        if (_campaign is not { } c || _tables?.ShopFor(c.NextScene) is not { } shop) { OpenOverMenu(); return; }
        _town = new Town(c, _battle);
        _townGreeting = -1;   // 건물에 새로 들어가면 들어오는 말부터
        BeginTownMenu(() => { });   // 첫 메뉴에서 Esc 는 그대로 — 나가는 것은 출구로
        _menu.Add((GoldLine($"{ChapterName(c.NextScene)} 앞 마을"), () => { }, default));
        _menu.Add(("주점 — 동료 보기 · 기록 · 불러오기", OpenTavern, default));
        _menu.Add(("무기점 — 사기 · 팔기", () => OpenShopMenu(shop, 1), default));
        _menu.Add(($"출구 — 다음 장으로({ChapterName(c.NextScene)})", LeaveTown, default));
        _menu.Add(("도구점 — 사기 · 팔기", () => OpenShopMenu(shop, 3), default));
        _menu.Add(("교회 — 부활 · 전직", OpenChurchMenu, default));
        if (_townSecret && shop.Secret.Length > 0) _menu.Add(("??? — 비밀상점", () => OpenShopMenu(shop, 5), default));
        EndTownMenu(building < 0 ? (_shopAt == 5 && _townSecret ? 6 : 1) : building + 1);
        _townTop = true;
    }

    private void LeaveTown()
    {
        _menuGlyphs.Clear();
        _townBack = null;
        _townTop = _townSecret = false;
        _shopAt = 1;
        _town = null;
        PlayCampaign();   // 원작처럼 마을 출구는 다음 장으로
    }

    /// <summary>
    /// 비밀상점 열쇠(sub_26152) — 첫 메뉴에서 기록 +1 건물 줄을 고른 채 +2 스캔코드 키를 누르면 「???」 줄이 열리고 커서가 그리로.
    /// 스캔코드 = Shift+F 0x54 · Ctrl+F 0x5E · Alt+F 0x68 + (F 번호 − 1). 받았으면 참.
    /// </summary>
    private bool TownSecretKey(int vk)
    {
        if (!_townTop || _stage != MenuStage.Town || _bs != BattleState.Menu || vk < Win32.VK_F1 || vk > Win32.VK_F1 + 9) return false;
        if (_campaign is not { } c || _tables?.ShopFor(c.NextScene) is not { } shop || shop.Secret.Length == 0) return false;
        int scan = _keys.Contains(Win32.VK_MENU) ? 0x68 : _keys.Contains(Win32.VK_CONTROL) ? 0x5E : _keys.Contains(Win32.VK_SHIFT) ? 0x54 : -1;
        if (scan < 0) return false;
        if (scan + vk - Win32.VK_F1 != shop.SecretKey || _menuIndex - 1 != shop.SecretAt) return true;   // 틀린 열쇠 — 아무 일도 없다(도움말 · 단축키로 새지 않게)
        _townSecret = true;
        _shopAt = 5;
        PlaySfx(Sound.UiBank, 5);
        OpenTownMenu(5);
        return true;
    }

    // ── 상점 ───────────────────────────────────────────────────────────────

    /// <summary>상점 이름 · 물건 — 1 무기점(+3, 12칸) · 3 도구점(+15) · 5 비밀상점(+23, 8칸), sub_26A0D.</summary>
    private static (string Name, int[] Goods) ShopGoods(ShopInfo shop, int at) => at switch
    {
        1 => ("무기점", shop.Weapons),
        3 => ("도구점", shop.Tools),
        _ => ("비밀상점", shop.Secret),
    };

    private void OpenShopMenu(ShopInfo shop) => OpenShopMenu(shop, _shopAt, back: true);

    /// <summary>상점(sub_279BC) — 원작 네 단추: 사기 · 팔기 · 정보 · 장비. Esc 로 마을.</summary>
    private void OpenShopMenu(ShopInfo shop, int at, bool back = false)
    {
        _shopAt = at;
        var (name, goods) = ShopGoods(shop, at);
        BeginTownMenu(() => OpenTownMenu(at));
        _menu.Add(("사기", () => { if (goods.Length > 0) OpenBuy(shop, goods, 0); else TownBanner("팔 물건이 없습니다", () => OpenShopMenu(shop)); }, default));
        _menu.Add(("팔기", () => OpenSellWho(shop, 0), default));
        _menu.Add(("정보", () => OpenTavernView(0), default));   // 원작 sub_28EFE — 동료를 골라 상태 창
        _menu.Add(("장비", () => OpenGiveFrom(0), default));     // 원작 sub_28F65 — 물건 건네주기
        TownButtons(at == 1 ? (back ? 503 : 501) : 440);
    }

    /// <summary>살 물건 고르기(sub_2872B) — 원본 글꼴 이름 · 값.</summary>
    private void OpenBuy(ShopInfo shop, int[] goods, int row)
    {
        if (_town is not { } t) return;
        BeginTownMenu(() => OpenShopMenu(shop));
        _menu.Add((GoldLine("살 물건"), () => { }, default));
        foreach (int item in goods)
        {
            int line = _menu.Count;
            _menuGlyphs[line] = 181 + item;
            _menu.Add(($"{t.BuyPrice(item),6}원  ", () => OpenBuyWho(shop, goods, line, item, 0), default));
        }
        _menu.Add(("돌아가기", () => OpenShopMenu(shop), default));
        EndTownMenu(row <= 0 ? 1 : row);
    }

    /// <summary>받을 사람 고르기 — 장비면 장비할 수 있는 사람만(없으면 원작 「현재대원중, 장비가능자 없음」).</summary>
    private void OpenBuyWho(ShopInfo shop, int[] goods, int row, int item, int page)
    {
        if (_town is not { } t || _campaign is not { } c) return;
        void Back() => OpenBuy(shop, goods, row);
        int price = t.BuyPrice(item);
        if (c.Gold < price) { PlaySfx(Sound.UiBank, 7); TownBanner("돈이 모자랍니다", Back); return; }
        var takers = t.Takers(item);
        if (takers.Count == 0) { PlaySfx(Sound.UiBank, 7); TownBanner("현재 대원 중 장비 가능자 없음", Back); return; }
        BeginTownMenu(Back);
        _menuGlyphs[0] = 181 + item;
        _menu.Add(($"{price}원 — 누가 받나  ", () => { }, default));
        foreach (int i in takers.Skip(page * TownPage).Take(TownPage))
        {
            var r = c.Party[i];
            int n = Town.Bag(r).Count;
            _menuGlyphs[_menu.Count] = HeroRecord.Id(r) + 1;
            _menu.Add(($"Lv{Town.Level(r),-3} 가방 {n}/8  ", () =>
            {
                if (Town.Bag(c.Party[i]).Count >= 8) { PlaySfx(Sound.UiBank, 7); TownBanner(" 가방이 가득 찼습니다", Back, HeroRecord.Id(r) + 1); return; }
                if (_tables!.Items[item].Kind < 32) AskEquip(i, item, Back);
                else Bought(t.Buy(i, item, false), i, Back);
            }, default));
        }
        AddPager(takers.Count, page, p => OpenBuyWho(shop, goods, row, item, p));
        _menu.Add(("돌아가기", Back, default));
        EndTownMenu(1);
    }

    /// <summary>산 장비를 바로 장비하나(원작 「장비합니까?」).</summary>
    private void AskEquip(int member, int item, Action back)
    {
        if (_town is not { } t) return;
        BeginTownMenu(back);
        _menuGlyphs[0] = 181 + item;
        _menu.Add(("장비합니까? — ", () => { }, default));
        _menu.Add(("장비한다", () => Bought(t.Buy(member, item, true), member, back), default));
        _menu.Add(("가방에만 넣는다", () => Bought(t.Buy(member, item, false), member, back), default));
        EndTownMenu(1);
    }

    private void Bought(TownResult res, int member, Action back)
    {
        int name = _campaign is { } c ? HeroRecord.Id(c.Party[member]) + 1 : -1;
        if (_campaign is { } cc) _battle.Gold = cc.Gold;
        PlaySfx(Sound.UiBank, res == TownResult.Ok ? 5 : 7);
        if (res == TownResult.Ok) StartDeal();
        TownBanner(res switch
        {
            TownResult.Ok => $" 샀습니다 — 돈 {_campaign?.Gold}원",
            TownResult.NoMoney => "돈이 모자랍니다",
            TownResult.BagFull => " 가방이 가득 찼습니다",
            _ => " 장비할 수 없습니다",
        }, back, res == TownResult.NoMoney ? -1 : name);
    }

    /// <summary>팔 사람 고르기(sub_28CBD).</summary>
    private void OpenSellWho(ShopInfo shop, int page)
    {
        if (_campaign is not { } c) return;
        void Back() => OpenShopMenu(shop);
        BeginTownMenu(Back);
        _menu.Add((GoldLine("누구 물건을 파나"), () => { }, default));
        foreach (int i in Enumerable.Range(0, c.Party.Count).Skip(page * TownPage).Take(TownPage))
        {
            var r = c.Party[i];
            _menuGlyphs[_menu.Count] = HeroRecord.Id(r) + 1;
            _menu.Add(($"Lv{Town.Level(r),-3} 가방 {Town.Bag(r).Count}/8  ", () => OpenSellItem(shop, page, i), default));
        }
        AddPager(c.Party.Count, page, p => OpenSellWho(shop, p));
        _menu.Add(("돌아가기", Back, default));
        EndTownMenu(1);
    }

    private void OpenSellItem(ShopInfo shop, int page, int member)
    {
        if (_town is not { } t || _campaign is not { } c) return;
        void Back() => OpenSellWho(shop, page);
        var r = c.Party[member];
        var bag = Town.Bag(r);
        if (bag.Count == 0) { PlaySfx(Sound.UiBank, 7); TownBanner(" 가방이 비었습니다", Back, HeroRecord.Id(r) + 1); return; }
        BeginTownMenu(Back);
        _menuGlyphs[0] = HeroRecord.Id(r) + 1;
        _menu.Add((GoldLine("팔 물건 — "), () => { }, default));
        for (int k = 0; k < bag.Count; k++)
        {
            int slot = k, item = bag[k].Item;
            _menuGlyphs[_menu.Count] = 181 + item;
            _menu.Add(($"{t.SellPrice(item),6}원 {(bag[k].Equipped ? "(장비) " : "")} ", () =>
            {
                int gain = t.SellPrice(item);
                t.Sell(member, slot);
                _battle.Gold = c.Gold;
                PlaySfx(Sound.UiBank, 5);
                StartDeal();
                TownBanner($"팔았습니다 +{gain}원 — 돈 {c.Gold}원", () => OpenSellItem(shop, page, member));
            }, default));
        }
        _menu.Add(("돌아가기", Back, default));
        EndTownMenu(1);
    }

    // ── 주점 ───────────────────────────────────────────────────────────────

    /// <summary>주점(sub_29300) — 원작 글 585 「도와드릴까요?」, 동료 보기(sub_29620) · 기록(sub_2968D) · 불러오기(sub_2986F) · 전장 이탈(글 415).</summary>
    private void OpenTavern()
    {
        BeginTownMenu(() => OpenTownMenu(0));
        _menu.Add(("동료 보기", () => OpenTavernView(0), default));
        _menu.Add(("기록하기", OpenTavernSave, default));
        _menu.Add(("불러오기", OpenTavernLoad, default));
        _menu.Add(("전장 이탈", AskQuit, default));
        TownButtons(_townGreeting >= 0 ? 586 : 585);
    }

    /// <summary>동료 보기 · 건네주기에서 물릴 곳 — 지금 건물의 네 단추(주점 · 교회 · 상점).</summary>
    private Action ViewBack => _townSpot switch
    {
        4 => OpenChurchMenu,
        0 => OpenTavern,
        _ => () => { if (_campaign is { } c && _tables?.ShopFor(c.NextScene) is { } s) OpenShopMenu(s); },
    };

    // ── 장비(건네주기, sub_28F65) — 글 512 누구 것을 · 510 누구에게 · 511 물건이 없다 · 0x5238D 가방이 찼다 ──

    private void OpenGiveFrom(int page)
    {
        if (_campaign is not { } c) return;
        BeginTownMenu(ViewBack);
        _menu.Add((GoldLine("누구의 물건을 건네주나"), () => { }, default));
        foreach (int i in Enumerable.Range(0, c.Party.Count).Skip(page * TownPage).Take(TownPage))
        {
            var r = c.Party[i];
            _menuGlyphs[_menu.Count] = HeroRecord.Id(r) + 1;
            _menu.Add(($"Lv{Town.Level(r),-3} 가방 {Town.Bag(r).Count}/8  ", () => OpenGiveItem(page, i), default));
        }
        AddPager(c.Party.Count, page, OpenGiveFrom);
        _menu.Add(("돌아가기", ViewBack, default));
        EndTownMenu(1);
    }

    private void OpenGiveItem(int page, int member)
    {
        if (_campaign is not { } c) return;
        var r = c.Party[member];
        var bag = Town.Bag(r);
        if (bag.Count == 0) { PlaySfx(Sound.UiBank, 7); TownBanner(" 건넬 물건이 없습니다", () => OpenGiveFrom(page), HeroRecord.Id(r) + 1); return; }
        BeginTownMenu(() => OpenGiveFrom(page));
        _menuGlyphs[0] = HeroRecord.Id(r) + 1;
        _menu.Add((GoldLine("건넬 물건 — "), () => { }, default));
        for (int k = 0; k < bag.Count; k++)
        {
            int slot = k;
            _menuGlyphs[_menu.Count] = 181 + bag[k].Item;
            _menu.Add(($"{(bag[k].Equipped ? "(장비) " : "")} ", () => OpenGiveTo(page, member, slot, 0), default));
        }
        _menu.Add(("돌아가기", () => OpenGiveFrom(page), default));
        EndTownMenu(1);
    }

    private void OpenGiveTo(int fromPage, int member, int slot, int page)
    {
        if (_town is not { } t || _campaign is not { } c) return;
        int item = Town.Bag(c.Party[member])[slot].Item;
        BeginTownMenu(() => OpenGiveItem(fromPage, member));
        _menuGlyphs[0] = 181 + item;
        _menu.Add((GoldLine("누구에게 — "), () => { }, default));
        foreach (int i in Enumerable.Range(0, c.Party.Count).Where(i => i != member).Skip(page * TownPage).Take(TownPage))
        {
            var r = c.Party[i];
            int to = i;
            _menuGlyphs[_menu.Count] = HeroRecord.Id(r) + 1;
            _menu.Add(($"Lv{Town.Level(r),-3} 가방 {Town.Bag(r).Count}/8  ", () =>
            {
                var result = t.Give(member, slot, to);
                PlaySfx(Sound.UiBank, result == TownResult.Ok ? 5 : 7);
                string msg = result switch
                {
                    TownResult.Ok => " 에게 건넸습니다",
                    TownResult.BagFull => " 의 가방이 꽉 찼습니다",
                    TownResult.CantEquip => " 은(는) 장비할 수 없습니다",
                    _ => " 건네지 못했습니다",
                };
                TownBanner(msg, () => OpenGiveFrom(fromPage), HeroRecord.Id(r) + 1);
            }, default));
        }
        AddPager(c.Party.Count - 1, page, p => OpenGiveTo(fromPage, member, slot, p));
        _menu.Add(("돌아가기", () => OpenGiveItem(fromPage, member), default));
        EndTownMenu(1);
    }

    /// <summary>동료 보기 — 고른 사람의 레벨 · HP · MP · 셈한 능력 · 장비(원작 sub_17AED 상태 창 대신 알림 줄로).</summary>
    private void OpenTavernView(int page)
    {
        if (_town is not { } t || _campaign is not { } c) return;
        BeginTownMenu(ViewBack);
        _menu.Add(($"동료 {c.Party.Count}명 — 누구를 보나", () => { }, default));
        foreach (int i in Enumerable.Range(0, c.Party.Count).Skip(page * TownPage).Take(TownPage))
        {
            var r = c.Party[i];
            int name = HeroRecord.Id(r) + 1;
            _menuGlyphs[_menu.Count] = name;
            _menu.Add(($"Lv{Town.Level(r),-3} HP {HeroRecord.Hp(r),3}  ", () =>
            {
                var u = t.View(i);
                var notes = new List<Notice>
                {
                    new($" Lv {u.Level}  경험 {u.Exp}{(Town.Down(r) ? "  (쓰러짐)" : "")}", name),
                    new($"HP {u.Hp}/{u.MaxHp}  MP {u.Mp}/{u.MaxMp}  이동 {u.Move}"),
                    new($"공격 {u.Atk}  방어 {u.Def}  명중 {u.Hit}  회피 {u.Evade}"),
                };
                foreach (var (item, on) in u.Bag) notes.Add(new Notice(on ? " (장비)" : "", 181 + item));
                _menuGlyphs.Clear();
                ShowNotices(notes, () => OpenTavernView(page));
            }, default));
        }
        AddPager(c.Party.Count, page, OpenTavernView);
        _menu.Add(("돌아가기", ViewBack, default));
        EndTownMenu(1);
    }

    /// <summary>기록(sub_2968D) — 네 칸 가운데 하나에, 남긴 뒤 주점으로(원작 글 660 「저장완료」).</summary>
    private void OpenTavernSave()
    {
        var save = SaveFile.Load(SavePath);
        BeginTownMenu(OpenTavern);
        _menu.Add(("어디에 기록하나", () => { }, default));
        for (int k = 0; k < SaveFile.Slots; k++)
        {
            int slot = k;
            _menu.Add(($"기록 {k + 1}: {SlotLabel(save, k)}", () => WriteSlot(slot, OpenTavern), default));
        }
        _menu.Add(("돌아가기", OpenTavern, default));
        EndTownMenu(1);
    }

    /// <summary>불러오기(sub_2986F) — 마을 앞 기록만(아니면 원작 글 479 「상점내 불러오기 안 됨」), 불러온 마을로.</summary>
    private void OpenTavernLoad()
    {
        var save = SaveFile.Load(SavePath);
        BeginTownMenu(OpenTavern);
        _menu.Add(("어느 기록을 부르나", () => { }, default));
        for (int k = 0; k < SaveFile.Slots; k++)
        {
            int slot = k;
            _menu.Add(($"기록 {k + 1}: {SlotLabel(save, k)}", () =>
            {
                if (save.ReadSlot(slot) is not { } loaded || loaded.NextScene >= _sceneCount) { PlaySfx(Sound.UiBank, 7); return; }
                if (_tables?.HasTown(loaded.NextScene) != true) { PlaySfx(Sound.UiBank, 7); TownBanner("마을 안에서는 불러올 수 없는 기록입니다", OpenTavernLoad); return; }
                _campaign = loaded;
                _battle.Gold = loaded.Gold;
                _townSecret = false;
                _shopAt = 1;
                PlaySfx(Sound.UiBank, 5);
                TownBanner($"기록 {slot + 1} 을 불러왔습니다", () => OpenTownMenu(0));
            }, default));
        }
        _menu.Add(("돌아가기", OpenTavern, default));
        EndTownMenu(1);
    }

    /// <summary>전장 이탈(글 415 「전장이탈합니까?」 → 416 「그럼 쉬세요.」) — 타이틀로.</summary>
    private void AskQuit()
    {
        BeginTownMenu(OpenTavern);
        _menu.Add(("전장 이탈합니까?", () => { }, default));
        _menu.Add(("예 — 타이틀로", () =>
        {
            _townBack = null;
            _townTop = _townSecret = false;
            TownBanner("그럼 쉬세요.", () => { _town = null; ToTitle(); });
        }, default));
        _menu.Add(("아니오", OpenTavern, default));
        EndTownMenu(2);
    }

    // ── 교회 ───────────────────────────────────────────────────────────────

    private void OpenChurchMenu()
    {
        BeginTownMenu(() => OpenTownMenu(4));
        _menu.Add(("동료 보기", () => OpenTavernView(0), default));
        _menu.Add(("장비", () => OpenGiveFrom(0), default));
        _menu.Add(("부활", () => OpenRevive(0), default));
        _menu.Add(("전직", () => OpenClassChange(0), default));
        TownButtons(_townGreeting >= 0 ? 586 : 585);
    }

    /// <summary>부활(sub_2A43E) — 쓰러진 동료, 값 = 직업 부활값 × 레벨.</summary>
    private void OpenRevive(int page)
    {
        if (_town is not { } t || _campaign is not { } c) return;
        var down = Enumerable.Range(0, c.Party.Count).Where(i => Town.Down(c.Party[i])).ToList();
        if (down.Count == 0) { TownBanner("쓰러진 동료가 없습니다", OpenChurchMenu); return; }
        BeginTownMenu(OpenChurchMenu);
        _menu.Add((GoldLine("누구를 되살리나"), () => { }, default));
        foreach (int i in down.Skip(page * TownPage).Take(TownPage))
        {
            var r = c.Party[i];
            int name = HeroRecord.Id(r) + 1;
            _menuGlyphs[_menu.Count] = name;
            _menu.Add(($"{t.ReviveCost(r),7}원  Lv{Town.Level(r),-3} ", () =>
            {
                var res = t.Revive(i);
                _battle.Gold = c.Gold;
                PlaySfx(Sound.UiBank, res == TownResult.Ok ? 5 : 7);
                if (res == TownResult.Ok) { StartDeal(); TownBanner($" 되살아났습니다 — 돈 {c.Gold}원", () => OpenRevive(0), name); }
                else TownBanner("돈이 모자랍니다", () => OpenRevive(page));
            }, default));
        }
        AddPager(down.Count, page, OpenRevive);
        _menu.Add(("돌아가기", OpenChurchMenu, default));
        EndTownMenu(1);
    }

    /// <summary>전직할 사람 고르기(sub_2AE0E).</summary>
    private void OpenClassChange(int page)
    {
        if (_town is not { } t || _campaign is not { } c) return;
        var can = Enumerable.Range(0, c.Party.Count).Where(i => t.ChangeTo(c.Party[i]) != null).ToList();
        if (can.Count == 0) { TownBanner("전직할 수 있는 동료가 없습니다 (Lv 20 이상)", OpenChurchMenu); return; }
        BeginTownMenu(OpenChurchMenu);
        _menu.Add((GoldLine("누가 전직하나"), () => { }, default));
        foreach (int i in can.Skip(page * TownPage).Take(TownPage))
        {
            var r = c.Party[i];
            _menuGlyphs[_menu.Count] = HeroRecord.Id(r) + 1;
            _menu.Add(($"Lv{Town.Level(r),-3} ", () => AskClassChange(i, page), default));
        }
        AddPager(can.Count, page, OpenClassChange);
        _menu.Add(("돌아가기", OpenChurchMenu, default));
        EndTownMenu(1);
    }

    /// <summary>새 직업 · 쓰는 물건을 보이고 묻는다(원작 글 594).</summary>
    private void AskClassChange(int member, int page)
    {
        if (_town is not { } t || _campaign is not { } c || t.ChangeTo(c.Party[member]) is not (int form, int item)) return;
        void Back() => OpenClassChange(page);
        var r = c.Party[member];
        int name = HeroRecord.Id(r) + 1;
        BeginTownMenu(Back);
        _menuGlyphs[0] = name;
        _menu.Add(("전직 — ", () => { }, default));
        _menuGlyphs[1] = 150 + _tables!.ClassChange[form - 32].Class;
        _menu.Add(("새 직업 ", () => { }, default));
        if (item >= 0)
        {
            _menuGlyphs[2] = 181 + item;
            _menu.Add(("쓰는 물건 ", () => { }, default));
        }
        _menu.Add(("전직한다 (Lv 1 부터)", () =>
        {
            var (res, _, g, move) = t.ClassChange(member);
            if (res != TownResult.Ok) { Back(); return; }
            PlaySfx(Sound.UiBank, 5);
            var parts = new List<string>();
            if (g.Str > 0) parts.Add($"역량 +{g.Str}");
            if (g.Def > 0) parts.Add($"내력 +{g.Def}");
            if (g.Spd > 0) parts.Add($"속도 +{g.Spd}");
            if (g.Hp > 0) parts.Add($"MHP +{g.Hp}");
            if (g.Mp > 0) parts.Add($"MMP +{g.Mp}");
            if (move > 0) parts.Add($"이동력 +{move}");
            var notes = new List<Notice> { new(" 전직했습니다! Lv 1", name) };
            if (parts.Count > 0) notes.Add(new Notice(string.Join("  ", parts)));
            _menuGlyphs.Clear();
            ShowNotices(notes, () => OpenClassChange(0));
        }, default));
        _menu.Add(("그만둔다", Back, default));
        EndTownMenu(_menu.Count - 2);
    }

    /// <summary>앞 · 다음 쪽 줄 — 사람이 한 쪽보다 많을 때.</summary>
    private void AddPager(int count, int page, Action<int> open)
    {
        int pages = (count + TownPage - 1) / TownPage;
        if (pages <= 1) return;
        if (page + 1 < pages) _menu.Add(($"다음 쪽 ({page + 2}/{pages})", () => open(page + 1), default));
        if (page > 0) _menu.Add(($"앞 쪽 ({page}/{pages})", () => open(page - 1), default));
    }
}
