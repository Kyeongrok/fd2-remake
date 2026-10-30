using Fd2.Data;

namespace Fd2;

/// <summary>
/// 장 사이 마을(sub_26152) — 이긴 뒤 메뉴의 「마을」. 다음 장면 n17 앞에 마을이 있으면(byte_523E7[n17] = 0) 그 마을 기록 sub_4E809(n17)의
/// 무기점 · 도구점에서 사고팔고, 교회에서 부활 · 전직한다. 원작의 건물 그림 · 걷기 없이 메뉴로만. 비밀상점(숨은 열쇠로 여는 n5 = 5)은 아직.
/// 고친 것은 동료 표(<see cref="Campaign.Party"/>)와 돈에 바로 들어가, 그 뒤 기록하면 그대로 남는다.
/// </summary>
internal sealed partial class GameWindow
{
    private Town? _town;

    /// <summary>마을 메뉴에서 물릴 곳(Esc · 오른쪽 단추).</summary>
    private Action? _townBack;

    /// <summary>한 쪽에 보일 사람 수 — 동료가 많으면 쪽을 넘긴다.</summary>
    private const int TownPage = 10;

    private bool HasTown => _campaign is { } c && _tables?.HasTown(c.NextScene) == true;

    private void BeginTownMenu(Action? back)
    {
        _stage = MenuStage.Town;
        _menu.Clear();
        _menuGlyphs.Clear();
        _townBack = back;
        if (_campaign is { } c) _battle.Gold = c.Gold;
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

    /// <summary>마을 첫 메뉴 — 상점 · 교회 · 돌아가기.</summary>
    private void OpenTownMenu()
    {
        if (_campaign is not { } c || _tables?.ShopFor(c.NextScene) is not { } shop) { OpenOverMenu(); return; }
        _town = new Town(c, _battle);
        BeginTownMenu(LeaveTown);
        _menu.Add((GoldLine($"{ChapterName(c.NextScene)} 앞 마을"), () => { }, default));
        _menu.Add(("상점 — 사기 · 팔기", () => OpenShopMenu(shop), default));
        _menu.Add(("교회 — 부활 · 전직", OpenChurchMenu, default));
        _menu.Add(("돌아가기", LeaveTown, default));
        EndTownMenu(1);
    }

    private void LeaveTown()
    {
        _menuGlyphs.Clear();
        _townBack = null;
        OpenOverMenu();
    }

    // ── 상점 ───────────────────────────────────────────────────────────────

    private void OpenShopMenu(ShopInfo shop)
    {
        BeginTownMenu(OpenTownMenu);
        _menu.Add((GoldLine("상점"), () => { }, default));
        if (shop.Weapons.Length > 0) _menu.Add(("무기점 — 사기", () => OpenBuy(shop, shop.Weapons, 0), default));
        if (shop.Tools.Length > 0) _menu.Add(("도구점 — 사기", () => OpenBuy(shop, shop.Tools, 0), default));
        _menu.Add(("팔기", () => OpenSellWho(shop, 0), default));
        _menu.Add(("돌아가기", OpenTownMenu, default));
        EndTownMenu(1);
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
                TownBanner($"팔았습니다 +{gain}원 — 돈 {c.Gold}원", () => OpenSellItem(shop, page, member));
            }, default));
        }
        _menu.Add(("돌아가기", Back, default));
        EndTownMenu(1);
    }

    // ── 교회 ───────────────────────────────────────────────────────────────

    private void OpenChurchMenu()
    {
        BeginTownMenu(OpenTownMenu);
        _menu.Add((GoldLine("교회"), () => { }, default));
        _menu.Add(("부활 — 쓰러진 동료", () => OpenRevive(0), default));
        _menu.Add(("전직 — Lv 20 이상", () => OpenClassChange(0), default));
        _menu.Add(("돌아가기", OpenTownMenu, default));
        EndTownMenu(1);
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
                if (res == TownResult.Ok) TownBanner($" 되살아났습니다 — 돈 {c.Gold}원", () => OpenRevive(0), name);
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
