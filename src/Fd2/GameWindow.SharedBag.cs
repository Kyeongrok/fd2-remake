using Fd2.Data;

namespace Fd2;

/// <summary>
/// 공통 가방(모드 · 기본 켬, w-114) — 소모품(쓰면 없어지는 것: 약초 · 회복제 · 해독 · 약수 …)과 장비하지 않은 무기 · 옷은
/// 사람 가방이 아니라 우리 편 하나의 공통 가방에 모인다. 칸 제한이 없다. 장비 중인 것 · 훈장 같은 특별한 물건은 사람 가방.
/// </summary>
/// <remarks>
/// <para>원작 세이브는 크기가 정해져 있어 공통 가방은 세이브 옆 파일(<see cref="SaveFile"/> 이 읽고 쓴다)에 담는다 — 원작 꼴은 그대로.</para>
/// <para>사람 가방에 소모품이 들어오면(상자 · 사건 · 승리 보상 · 옛 세이브) 안전한 때(마을 메뉴를 열 때, 싸움 시작 · 우리 차례에 고르는 중)에 공통 가방으로 옮긴다.
/// 쓸 때는 한 개를 쓰는 사람 가방으로 잠시 옮겨 와 쓰고(쓰면 없어진다), 대상 고르기를 물리면 공통 가방으로 되돌린다.
/// 모드를 끄면 공통 가방 것을 가방이 빈 사람부터 나눠 넣는다(못 넣은 것은 공통 가방에 남아 다시 켜면 보인다).</para>
/// </remarks>
internal sealed partial class GameWindow
{
    /// <summary>동료 표가 없는 싸움(개발용 장면 고르기)에서 쓰는 공통 가방.</summary>
    private readonly List<int> _poolLocal = new();

    /// <summary>지금 공통 가방(물건 번호들, 들어온 차례).</summary>
    private List<int> Pool => _campaign?.Shared ?? _poolLocal;

    /// <summary>싸움을 시작할 때의 공통 가방 — 지면 되돌린다.</summary>
    private List<int>? _poolAtStart;

    /// <summary>
    /// 공통 가방에 드는 물건인가 — 소모품과 무기 · 옷(장비하지 않은 것, 사용자 요청).
    /// 훈장 · 서적 · 보석 · 눈 같은 특별한 물건은 사람 가방에 둔다 — 전직(그 사람 가방의 물건)과 사건(누가 가졌나)이 그것을 본다.
    /// </summary>
    private bool PoolItem(int id) =>
        _tables != null && id >= 0 && id < _tables.Items.Count
        && (_tables.Items[id].Kind < 32 || ConsumedItem(_tables.Items[id].Use));

    /// <summary>동료 표(사람 칸들)의 소모품을 공통 가방으로.</summary>
    private void SweepRecords()
    {
        if (!_settings.SharedBag || _campaign is not { } c || _tables == null) return;
        foreach (var r in c.Party)
        {
            var keep = new List<(byte Flag, byte Item)>();
            bool moved = false;
            for (int k = 0; k < 8; k++)
            {
                byte flag = r[10 + 2 * k], item = r[11 + 2 * k];
                if ((flag & 0x80) != 0) continue;
                if ((flag & 0x40) == 0 && PoolItem(item)) { c.Shared.Add(item); moved = true; }
                else keep.Add((flag, item));
            }
            if (!moved) continue;
            for (int k = 0; k < 8; k++)
                (r[10 + 2 * k], r[11 + 2 * k]) = k < keep.Count ? keep[k] : ((byte)0xFF, (byte)0xFF);
        }
    }

    /// <summary>싸움터 우리 편 가방의 소모품을 공통 가방으로 — 고르는 중(Select)에만 부른다(가방 칸 번호를 쥔 메뉴가 없을 때).</summary>
    private void SweepUnits()
    {
        if (!_settings.SharedBag || !InBattle || _tables == null) return;
        foreach (var u in _scene.Units.Where(u => u.Team == 2 && !u.Ai))
        {
            if (!u.Bag.Any(b => !b.Equipped && PoolItem(b.Item))) continue;
            Pool.AddRange(u.Bag.Where(b => !b.Equipped && PoolItem(b.Item)).Select(b => b.Item));
            u.Bag.RemoveAll(b => !b.Equipped && PoolItem(b.Item));
            _battle.Recompute(u);
        }
    }

    /// <summary>공통 가방을 껐다 — 가방이 빈 사람부터 하나씩 나눠 넣는다.</summary>
    private void SpreadPool()
    {
        var pool = Pool;
        if (pool.Count == 0) return;
        if (InBattle && _scene != null)
        {
            var mine = _scene.Units.Where(u => u.Team == 2 && !u.Ai && u.Hp > 0).ToList();
            while (pool.Count > 0 && mine.Where(u => u.Bag.Count < 8).MinBy(u => u.Bag.Count) is { } to) { to.Bag.Add((pool[0], false)); pool.RemoveAt(0); }
            return;
        }
        if (_campaign is not { } c) return;
        while (pool.Count > 0)
        {
            var to = c.Party.Where(r => !Town.Down(r) && Town.Bag(r).Count < 8).MinBy(r => Town.Bag(r).Count);
            if (to == null) break;
            for (int k = 0; k < 8; k++)
                if ((to[10 + 2 * k] & 0x80) != 0) { (to[10 + 2 * k], to[11 + 2 * k]) = (0, (byte)pool[0]); pool.RemoveAt(0); break; }
        }
    }

    /// <summary>공통 가방을 켜고 끄기(모드).</summary>
    private void ToggleSharedBag()
    {
        _settings.SharedBag = !_settings.SharedBag;
        _settings.Save();
        if (_settings.SharedBag) { SweepRecords(); if (InBattle && _bs == BattleState.Select) SweepUnits(); }
        else SpreadPool();
    }

    /// <summary>「사용」 목록의 공통 가방 줄 — 물건 번호마다 한 줄(개수).</summary>
    private List<(int Item, int Count)> PoolRows() =>
        !_settings.SharedBag ? [] : Pool.GroupBy(i => i).Select(g => (g.Key, g.Count())).OrderBy(g => Pool.IndexOf(g.Key)).ToList();

    /// <summary>공통 가방에서 하나를 꺼내 쓰는 사람 가방 끝에 잠시 넣는다 — 그 칸 번호.</summary>
    private int TakeFromPool(Unit u, int item)
    {
        Pool.Remove(item);
        u.Bag.Add((item, false));
        _poolBorrow = (u, item);
        return u.Bag.Count - 1;
    }

    /// <summary>공통 가방을 물건 목록의 「임자」 한 사람처럼 — 가방에 물건 번호마다 하나(개수는 그릴 때 센다).</summary>
    private readonly Unit _poolUnit = new() { Icon = 0, Side = Side.Player };

    private void RefreshPoolUnit()
    {
        _poolUnit.Bag.Clear();
        foreach (int id in Pool.Distinct()) _poolUnit.Bag.Add((id, false));
    }

    /// <summary>공통 가방에서 꺼내 온 것(대상 고르기 중) — 물리면 되돌린다.</summary>
    private (Unit User, int Item)? _poolBorrow;

    private void ReturnPoolBorrow()
    {
        if (_poolBorrow is not { } b) return;
        _poolBorrow = null;
        int k = b.User.Bag.FindLastIndex(x => x.Item == b.Item && !x.Equipped);
        if (k < 0) return;
        b.User.Bag.RemoveAt(k);
        Pool.Add(b.Item);
    }
}
