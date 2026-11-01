using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>사기 · 팔기 · 부활 · 전직의 결과 — Ok 말고는 원작이 막는 까닭(글 504 돈 모자람, 가방 8칸, 장비할 사람 없음).</summary>
public enum TownResult { Ok, NoMoney, BagFull, CantEquip, NotDown, CantChange, Empty }

/// <summary>
/// 장 사이 마을의 상점 · 교회(분석-아이템 5절, 분석-캐릭터 4절) — 동료 표(80바이트 칸)와 돈을 그 자리에서 고친다.
/// 칸을 사람으로 옮겨(<see cref="HeroRecord.Apply"/>) 고치고 능력을 다시 셈해(<see cref="Battle.Recompute"/>, 원작 sub_1145A) 칸에 되쓴다.
/// </summary>
public sealed class Town(Campaign campaign, Battle battle)
{
    private GameTables T => battle.Tables ?? throw new InvalidOperationException("FD2.EXE 표가 없다");

    public Campaign Campaign => campaign;

    // ── 값 ─────────────────────────────────────────────────────────────────

    /// <summary>사는 값 — 물건 표 [19].</summary>
    public int BuyPrice(int item) => T.Items[item].Price;

    /// <summary>파는 값 — 사는 값 × 3 ÷ 4(sub_28CBD).</summary>
    public int SellPrice(int item) => T.Items[item].Price * 3 / 4;

    /// <summary>부활 값 — 직업 부활값(word_52397) × 레벨(sub_2A43E).</summary>
    public int ReviveCost(byte[] r) => r[32] < T.Classes.Count ? T.Classes[r[32]].Revive * Math.Max(1, (int)r[33]) : 0;

    // ── 칸 읽기 ────────────────────────────────────────────────────────────

    /// <summary>가방에 든 물건(차례대로, 장비 중인가).</summary>
    public static List<(int Item, bool Equipped)> Bag(byte[] r)
    {
        var bag = new List<(int, bool)>();
        for (int k = 0; k < 8; k++)
            if ((r[10 + 2 * k] & 0x80) == 0) bag.Add((r[11 + 2 * k], (r[10 + 2 * k] & 0x40) != 0));
        return bag;
    }

    public static int Level(byte[] r) => Math.Max(1, (int)r[33]);

    /// <summary>쓰러졌나 — 교회 부활 대상(sub_34894 · sub_2A07A). 동료 표에선 HP 0.</summary>
    public static bool Down(byte[] r) => HeroRecord.Hp(r) == 0;

    /// <summary>장비 물건(종류 &lt; 32)이면 직업이 장비할 수 있는 사람만(sub_1C1C3), 나머지는 누구나.</summary>
    public bool CanTake(byte[] r, int item)
    {
        var it = T.Items[item];
        if (it.Kind >= 32) return true;
        return r[32] < T.Classes.Count && T.Classes[r[32]].EquipKinds.Contains(it.Kind);
    }

    /// <summary>물건 item 을 받을 수 있는 동료(표 차례) — 장비면 장비할 수 있는 사람만(sub_2872B).</summary>
    public List<int> Takers(int item) => Enumerable.Range(0, campaign.Party.Count).Where(i => CanTake(campaign.Party[i], item)).ToList();

    // ── 상점 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 산다(sub_2872B) — 돈이 값보다 적으면 NoMoney, 가방 8칸이면 BagFull. 넣고 돈을 뺀다.
    /// equip 이면 원작의 「장비합니까?」에 예 — 같은 쪽(무기 &lt; 128 / 옷)의 장비를 풀고 새 물건을 장비(sub_1C142).
    /// </summary>
    public TownResult Buy(int member, int item, bool equip)
    {
        int price = BuyPrice(item);
        if (campaign.Gold < price) return TownResult.NoMoney;
        var r = campaign.Party[member];
        if (!CanTake(r, item)) return TownResult.CantEquip;
        var u = ToUnit(r);
        if (u.Bag.Count >= 8) return TownResult.BagFull;
        bool on = equip && T.Items[item].Kind < 32;
        if (on)
            for (int k = 0; k < u.Bag.Count; k++)
                if (u.Bag[k].Equipped && u.Bag[k].Item < 128 == item < 128) u.Bag[k] = (u.Bag[k].Item, false);
        u.Bag.Add((item, on));
        campaign.Gold -= price;
        Store(member, u);
        return TownResult.Ok;
    }

    /// <summary>판다(sub_28CBD) — 장비 중인 것도 팔린다. 값 × 3 ÷ 4 를 받고 가방에서 뺀다(sub_1B8E7, 뒤는 당긴다).</summary>
    public TownResult Sell(int member, int slot)
    {
        var u = ToUnit(campaign.Party[member]);
        if (slot < 0 || slot >= u.Bag.Count) return TownResult.Empty;
        campaign.Gold += SellPrice(u.Bag[slot].Item);
        u.Bag.RemoveAt(slot);
        Store(member, u);
        return TownResult.Ok;
    }

    /// <summary>
    /// 건네주기 — 상점 · 교회의 「장비」 단추(sub_28F65): from 의 가방 slot 물건을 to 의 가방으로. 받는 쪽 가방이 8칸이면 BagFull(글 0x5238D),
    /// 받는 쪽이 장비할 수 없는 장비면 CantEquip. 장비 중이던 것은 풀고 건넨다.
    /// </summary>
    public TownResult Give(int from, int slot, int to)
    {
        var a = ToUnit(campaign.Party[from]);
        if (slot < 0 || slot >= a.Bag.Count) return TownResult.Empty;
        if (from == to) return TownResult.Ok;
        var b = ToUnit(campaign.Party[to]);
        if (b.Bag.Count >= 8) return TownResult.BagFull;
        int item = a.Bag[slot].Item;
        if (!CanTake(campaign.Party[to], item)) return TownResult.CantEquip;
        a.Bag.RemoveAt(slot);
        b.Bag.Add((item, false));
        Store(from, a);
        Store(to, b);
        return TownResult.Ok;
    }

    /// <summary>
    /// 장비 입고 벗기 — 가방의 장비를 입으면 같은 쪽(무기 / 옷)의 것을 풀고, 입은 것이면 벗는다(전투의 sub_1C142 와 같음). 원작 상점 「장비」 단추는 건네주기뿐이라 리메이크가 더한 것(사용자 제보).
    /// </summary>
    public TownResult Equip(int member, int slot)
    {
        var u = ToUnit(campaign.Party[member]);
        if (slot < 0 || slot >= u.Bag.Count) return TownResult.Empty;
        var (item, on) = u.Bag[slot];
        if (T.Items[item].Kind >= 32 || !on && !CanTake(campaign.Party[member], item)) return TownResult.CantEquip;
        if (!on)
            for (int k = 0; k < u.Bag.Count; k++)
                if (u.Bag[k].Equipped && u.Bag[k].Item < 128 == item < 128) u.Bag[k] = (u.Bag[k].Item, false);
        u.Bag[slot] = (item, !on);
        Store(member, u);
        return TownResult.Ok;
    }

    // ── 교회 ───────────────────────────────────────────────────────────────

    /// <summary>부활(sub_2A43E) — 부활 값을 내고 +5 를 0, HP 를 최대로(MP 는 장이 끝날 때 이미 가득).</summary>
    public TownResult Revive(int member)
    {
        var r = campaign.Party[member];
        if (!Down(r)) return TownResult.NotDown;
        int cost = ReviveCost(r);
        if (campaign.Gold < cost) return TownResult.NoMoney;
        campaign.Gold -= cost;
        r[5] = 0;
        r.AsSpan(66, 2).CopyTo(r.AsSpan(64));
        return TownResult.Ok;
    }

    /// <summary>
    /// 전직할 꼴(sub_2AE0E) — Lv 20 이상, 처음 꼴(&lt; 18), 란스로타(7) 아님. 보통 꼴 + 32, 가방에 byte_523D5[꼴] 이 있으면 꼴 + 50(그 물건을 쓴다),
    /// 유니(9)는 천사낙인(90)이 있으면 52(소환사, 성자반지보다 먼저). 못 하면 null.
    /// </summary>
    public (int Form, int Item)? ChangeTo(byte[] r)
    {
        int f = r[7];
        if (r[33] < 20 || f >= 18 || f == 7) return null;
        var bag = Bag(r);
        bool Has(int item) => item != 255 && bag.Any(b => b.Item == item);
        if (f == 9 && Has(90)) return (52, 90);
        int need = f < T.ClassChangeItem.Count ? T.ClassChangeItem[f] : 255;
        return Has(need) ? (f + 50, need) : (f + 32, -1);
    }

    /// <summary>
    /// 전직(sub_2AA00 · sub_2AC7D) — 쓰는 물건을 빼고, 직업 = 전직표[새 꼴 − 32], 꼴 = 새 꼴, 새 꼴 성장표로 한 번 오르고 이동력 + 표값,
    /// Lv 1 · 경험 0 · HP · MP 가득. 오른 값(역량 · 내력 · 속도 · HP · MP · 이동력)을 돌려준다.
    /// </summary>
    public (TownResult Result, int Form, (int Str, int Def, int Spd, int Hp, int Mp) Grow, int MoveAdd) ClassChange(int member)
    {
        var r = campaign.Party[member];
        if (ChangeTo(r) is not (int form, int item)) return (TownResult.CantChange, r[7], default, 0);
        var u = ToUnit(r);
        if (item >= 0) u.Bag.RemoveAt(u.Bag.FindIndex(b => b.Item == item));
        var (cls, moveAdd) = T.ClassChange[form - 32];
        u.Class = cls;
        u.Form = form;
        u.Icon = form;
        var grow = battle.Grow(u);
        u.Move += moveAdd;
        u.Level = 1;
        u.Exp = 0;
        u.Hp = u.MaxHp;
        u.Mp = u.MaxMp;
        Store(member, u);
        return (TownResult.Ok, form, grow, moveAdd);
    }

    // ── 주점 ───────────────────────────────────────────────────────────────

    // ── 칸 ↔ 사람 ──────────────────────────────────────────────────────────

    private Unit ToUnit(byte[] r)
    {
        var u = new Unit { Icon = r[7], Form = r[7], Id = r[8], Side = Side.Player, Record = r };
        battle.Setup(u);
        HeroRecord.Apply(r, u);
        battle.Recompute(u);
        return u;
    }

    /// <summary>사람을 칸에 되쓴다 — 지도 자리 · 쓰러짐 표시(+5)처럼 마을이 건드리지 않는 바이트는 그대로 둔다.</summary>
    private void Store(int member, Unit u)
    {
        battle.Recompute(u);
        var old = campaign.Party[member];
        var r = HeroRecord.From(u);
        foreach (int k in (int[])[0, 1, 2, 3, 4, 5, 6, 49, 50, 51, 52, 53, 54, 61]) r[k] = old[k];
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(64), (ushort)Math.Max(0, u.Hp));
        campaign.Party[member] = r;
    }
}
