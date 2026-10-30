using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 전투 — 아군 턴(고르기 → 메뉴: 이동 · 공격 · 마법 · 대기) ↔ 적군 턴, 이동 범위(파랑) · 공격 범위(빨강),
/// 컷인(공격하는 쪽 FIGANI 공격 모션 + 맞는 쪽 서기 모션, 원거리 · 마법은 맞는 쪽 화면으로 넘어간다).
/// </summary>
/// <remarks>
/// 지도 · 배치 · 그림 · 모션은 원본 자료를 쓰지만, 능력치 · 피해 · 적의 움직임은 원본 규칙이 아니라
/// 직업별 기본값과 흔한 SRPG 규칙이다(<see cref="Equip"/> · <see cref="Damage"/> · <see cref="EnemyStep"/>).
/// 적은 장면 정보의 등장 차례 k 가 k 턴 시작에 나온다. 편 2(나중에 합류하는 아군)는 그때 우리 편으로 든다.
/// </remarks>
internal sealed partial class GameWindow
{
    private enum BattleState { None, Select, Moving, Menu, Target, CutIn, Enemy, Banner, Over, Talk }

    private const uint ReachColor = 0x5A3C78FF, AttackColor = 0x5AFF4646, TargetColor = 0x80FF3030;
    private const double FigTick = 0.055;

    private BattleState _bs = BattleState.None;
    private int _turn, _lastPhase;
    private Unit? _actor;
    private (int X, int Y) _origin;
    private Dictionary<(int X, int Y), (int X, int Y)>? _reach;
    private HashSet<(int X, int Y)> _attackTiles = new();
    private readonly List<(string Label, Action Do, (int X, int Y, int W, int H) Rect)> _menu = new();
    private int _mouseX = -1, _mouseY = -1;

    /// <summary>키보드 커서(칸) — 원본처럼 방향키로 움직이고 Enter · Space · Z 로 고른다. 마우스를 올려도 따라온다.</summary>
    private (int X, int Y) _cursor;
    private int _menuIndex;

    /// <summary>다음 틀에 카메라를 커서 가운데로 한 번 맞춘다(턴이 바뀔 때).</summary>
    private bool _centerCursor;

    private string _banner = "";
    private double _bannerLeft;
    private Action? _afterBanner;

    // 컷인 — 공격 한 번(반격은 그다음 한 번)
    private readonly Queue<(Unit A, Unit D, Spell? S)> _strikes = new();
    private Action? _afterStrikes;
    private (Unit A, Unit D, Spell? S) _strike;
    private Motion? _strikeMotion;
    private int _strikeFrame, _strikeHeld, _strikeDamage = -1;
    private double _strikeClock, _strikeTail;

    private readonly Dictionary<int, Motion?> _motions = new(), _stands = new();

    /// <summary>
    /// 마법 — 원본 규칙이 아니라 기본값. 법사 계열은 공격 마법, 승려 계열은 회복 마법(성자는 둘 다).
    /// 사거리 최소 0 은 자기 자신도 된다는 뜻.
    /// </summary>
    private sealed record Spell(string Name, int Mp, int RangeMin, int RangeMax, bool Heal, int Power);

    private static readonly Spell Fire = new("파이어", 4, 1, 3, false, 6);
    private static readonly Spell Cure = new("힐", 3, 0, 2, true, 12);

    private static List<Spell> SpellsOf(Unit u)
    {
        string n = CharacterNames.Of(u.Icon);
        var list = new List<Spell>();
        if (n.Contains("법사") || n.Contains("소환") || n.Contains("성자")) list.Add(Fire);
        if (n.Contains("승려") || n.Contains("제관") || n.Contains("성자")) list.Add(Cure);
        return list;
    }

    /// <summary>지금 메뉴가 어느 단계인가 — 취소할 때 어디로 돌아갈지.</summary>
    private enum MenuStage { System, Unit, AfterMove, Spell }
    private MenuStage _stage;

    /// <summary>이동할 칸을 고르는 중인가(메뉴에서 「이동」을 고른 뒤).</summary>
    private bool _choosingMove;

    /// <summary>대상 고르기에 쓸 마법(없으면 보통 공격), 그 앞 메뉴 단계.</summary>
    private Spell? _pendingSpell;
    private MenuStage _targetFrom;

    // 적군 턴 — 한 명씩
    private readonly Queue<Unit> _enemyQueue = new();
    private Unit? _enemyNow;
    private double _enemyWait;

    private bool InBattle => _bs != BattleState.None;

    /// <summary>적군 차례인가 — 컷인 · 알림 중에도 누구 차례인지 알도록.</summary>
    private bool _enemyPhase;

    // ── 시작 · 끝 ─────────────────────────────────────────────────────────────

    private void StartBattle(int scene)
    {
        LoadScene(scene);
        foreach (var u in _scene.Units) Equip(u, battle: true);
        _lastPhase = _scene.Roster.Count == 0 ? 0 : _scene.Roster.Max(e => e.Phase);
        _turn = 0;
        _enemyPhase = false;
        // 전투 앞 대사 — 묶음 0. 묶음 k(k ≥ 1)는 등장 차례 k 의 무리가 나올 때 보인다(1장: 1 해적 두목, 2 해적과 마주침,
        // 3 하노 · 하와터, 4 해적 F · G, 5 해적 두목 97, 6 해양순찰대 68 — 대사를 그려 맞춰 본 것).
        _bs = BattleState.Talk;
        if (Controlled.OrderBy(u => u.Icon).FirstOrDefault() is { } lead) { _cursor = (lead.X, lead.Y); _centerCursor = true; }
        StartTalk(scene, [0], NextTurn);
    }

    private void StopBattle()
    {
        _bs = BattleState.None;
        LoadScene(_scene.Index);
    }

    /// <summary>
    /// 직업별 기본 능력치 — 원본 규칙이 아니다. 이름 끝(검사 · 전사 · 기사 · 궁병 · 법사 · 승려 · 무사)으로 나누고,
    /// 몬스터(그림 68 부터)는 한 가지, 적 우두머리는 체력을 늘린다. 레벨마다 체력 +3, 공격 +1, 방어 +1/2.
    /// </summary>
    private static void Equip(Unit u, bool battle)
    {
        string name = CharacterNames.Of(u.Icon);
        (int hp, int atk, int def, int mov, int rmin, int rmax) = name switch
        {
            _ when name.Contains("궁병") || name.Contains("사수") || name.Contains("저격") => (22, 8, 3, 4, 2, 3),
            _ when name.Contains("법사") || name.Contains("소환") || name.Contains("성자") => (20, 10, 2, 4, 1, 2),
            _ when name.Contains("승려") || name.Contains("제관") => (22, 6, 3, 4, 1, 1),
            _ when name.Contains("기사") => (28, 9, 7, 6, 1, 1),
            _ when name.Contains("전사") => (30, 10, 6, 4, 1, 1),
            _ when name.Contains("무사") || name.Contains("투사") || name.Contains("무성") => (26, 11, 4, 5, 1, 1),
            _ when u.Icon >= CharacterNames.Heroes => (18, 7, 2, 4, 1, 1),
            _ => (26, 9, 5, 5, 1, 1),
        };
        u.Move = mov;
        u.RangeMin = rmin;
        u.RangeMax = rmax;
        if (!battle) return;
        int lv = Math.Max(1, u.Level) - 1;
        u.MaxHp = u.Hp = hp + 3 * lv;
        u.Atk = atk + lv;
        u.Def = def + lv / 2;
        u.MaxMp = u.Mp = SpellsOf(u).Count > 0 ? 12 + 2 * lv : 0;
        u.Acted = false;
    }

    private static bool IsMagic(Unit u) => CharacterNames.Of(u.Icon) is var n && (n.Contains("법사") || n.Contains("소환") || n.Contains("성자"));

    private static int Damage(Unit a, Unit d, Spell? s) => s switch
    {
        { Heal: true } => s.Power + 2 * a.Level,
        not null => Math.Max(1, s.Power + a.Atk / 2 - d.Def / 4 + Random.Shared.Next(-2, 3)),
        _ => Math.Max(1, a.Atk - (IsMagic(a) ? d.Def / 4 : d.Def / 2) + Random.Shared.Next(-2, 3)),
    };

    private static bool InSpellRange(Unit a, Unit b, Spell s) => Dist(a, b) is var d && d >= s.RangeMin && d <= s.RangeMax;

    /// <summary>그 마법을 걸 수 있는 대상 — 회복은 다친 아군(자기 포함), 공격은 적.</summary>
    private List<Unit> SpellTargets(Unit a, Spell s) => s.Heal
        ? Living(Side.Player).Where(p => p.Hp < p.MaxHp && InSpellRange(a, p, s)).ToList()
        : Living(Side.Other).Where(e => InSpellRange(a, e, s)).ToList();

    private static int Dist(Unit a, Unit b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static bool InRange(Unit a, Unit b) => Dist(a, b) is var d && d >= a.RangeMin && d <= a.RangeMax;

    private IEnumerable<Unit> Living(Side side) => _scene.Units.Where(u => u.Side == side && u.Hp > 0);

    /// <summary>u 가 칠 상대 — 다른 편의 살아 있는 사람.</summary>
    private IEnumerable<Unit> Opponents(Unit u) => Living(u.Side == Side.Player ? Side.Other : Side.Player);

    /// <summary>내가 움직이는 아군(컴퓨터가 움직이는 아군은 빼고).</summary>
    private IEnumerable<Unit> Controlled => Living(Side.Player).Where(u => !u.Ai);

    // ── 턴 ───────────────────────────────────────────────────────────────────

    private void NextTurn()
    {
        _turn++;
        foreach (var e in _scene.Roster.Where(e => e.Phase == _turn))
            if (_scene.Spawn(e) is { } u) Equip(u, battle: true);
        foreach (var u in Living(Side.Player)) u.Acted = false;
        _enemyPhase = false;
        ClearSelection();
        if (Controlled.OrderBy(u => u.Icon).FirstOrDefault() is { } lead) { _cursor = (lead.X, lead.Y); _centerCursor = true; }
        int turn = _turn;
        void Banner() => ShowBanner($"{turn}턴 — 아군 차례", () => _bs = BattleState.Select);
        if (_scene.Roster.Any(e => e.Phase == turn))
        {
            _bs = BattleState.Talk;
            StartTalk(_scene.Index, [turn], Banner);   // 이 차례의 무리가 나올 때의 대사(없으면 바로 넘어간다)
        }
        else Banner();
    }

    private void EndPlayerTurn()
    {
        ClearSelection();
        _enemyPhase = true;
        _enemyQueue.Clear();
        // 컴퓨터가 움직이는 아군이 먼저, 그다음 적
        foreach (var u in Living(Side.Player).Where(u => u.Ai)) _enemyQueue.Enqueue(u);
        foreach (var u in Living(Side.Other).OrderBy(u => u.Y).ThenBy(u => u.X)) _enemyQueue.Enqueue(u);
        ShowBanner($"{_turn}턴 — 적군 차례", () => { _bs = BattleState.Enemy; _enemyNow = null; _enemyWait = 0.2; });
    }

    private void ShowBanner(string text, Action after)
    {
        _banner = text;
        _bannerLeft = 1.1;
        _afterBanner = after;
        _bs = BattleState.Banner;
    }

    /// <summary>이기고 졌나 — 사울(그림 0)이 쓰러지거나 아군이 다 쓰러지면 패배, 적이 없고 더 나올 적도 없으면 승리.</summary>
    private bool CheckOver()
    {
        bool saulDown = _scene.Units.Any(u => u.Side == Side.Player && u.Icon == 0 && u.Hp <= 0);
        if (saulDown || !Controlled.Any())
        {
            _bs = BattleState.Over;
            _banner = "패배 — 게임 > 1장 전투 시작으로 다시";
            return true;
        }
        if (!Living(Side.Other).Any() && _turn >= _lastPhase)
        {
            _bs = BattleState.Over;
            _banner = $"승리! ({_turn}턴)";
            return true;
        }
        return false;
    }

    private void BattleTick(double dt)
    {
        switch (_bs)
        {
            case BattleState.Banner:
                if ((_bannerLeft -= dt) <= 0) { var a = _afterBanner; _afterBanner = null; a?.Invoke(); }
                break;
            case BattleState.Moving:
                if (_actor is { Moving: false } m && m.Path.Count == 0) OpenMenu();
                break;
            case BattleState.CutIn:
                CutInTick(dt);
                break;
            case BattleState.Talk:
                TalkTick(dt);
                break;
            case BattleState.Enemy:
                EnemyTick(dt);
                break;
        }
    }

    // ── 아군: 고르기 · 이동 · 메뉴 · 대상 ─────────────────────────────────────

    /// <summary>범위만 보여 주는 사람(적 · 행동을 마친 아군).</summary>
    private Unit? _rangeOf;

    private void ClearSelection()
    {
        _actor = null;
        _rangeOf = null;
        _choosingMove = false;
        _pendingSpell = null;
        _reach = null;
        _attackTiles.Clear();
        _menu.Clear();
    }

    private void Select(Unit u)
    {
        _actor = u;
        _origin = (u.X, u.Y);
        _reach = _scene.Reach(u);
        _attackTiles = AttackTiles(u, _scene.Stops(_reach, u));
    }

    /// <summary>멈출 수 있는 칸들에서 사거리 안이지만 이동 범위 밖인 칸 — 공격 가능 범위.</summary>
    private HashSet<(int X, int Y)> AttackTiles(Unit u, IEnumerable<(int X, int Y)> stands)
    {
        var set = new HashSet<(int X, int Y)>();
        var reach = stands.ToHashSet();
        foreach (var (sx, sy) in reach)
            for (int dy = -u.RangeMax; dy <= u.RangeMax; dy++)
                for (int dx = -u.RangeMax; dx <= u.RangeMax; dx++)
                {
                    int d = Math.Abs(dx) + Math.Abs(dy);
                    if (d < u.RangeMin || d > u.RangeMax) continue;
                    var t = (sx + dx, sy + dy);
                    if (_scene.Inside(t.Item1, t.Item2) && !reach.Contains(t)) set.Add(t);
                }
        return set;
    }

    private void BattleClick(int tx, int ty, int mx, int my)
    {
        switch (_bs)
        {
            case BattleState.Menu:
                foreach (var item in _menu)
                    if (mx >= item.Rect.X && my >= item.Rect.Y && mx < item.Rect.X + item.Rect.W && my < item.Rect.Y + item.Rect.H)
                    {
                        item.Do();
                        return;
                    }
                // 메뉴 밖 — 걷기 전 메뉴에서 파란 칸을 누르면 바로 그리로(마우스로 빠르게)
                if (mx >= 0 && _stage == MenuStage.Unit && _actor != null && _reach != null && _reach.ContainsKey((tx, ty))
                    && _scene.UnitAt(tx, ty) is null)
                {
                    _menu.Clear();
                    _choosingMove = true;
                    _bs = BattleState.Select;
                    BattleClick(tx, ty, mx, my);
                }
                return;
            case BattleState.Target:
            {
                var t = _scene.UnitAt(tx, ty);
                if (t != null && _actor != null && _attackTiles.Contains((t.X, t.Y)))
                {
                    var a = _actor;
                    var spell = _pendingSpell;
                    a.Acted = true;
                    if (spell != null) a.Mp -= spell.Mp;
                    ClearSelection();
                    Attack(a, t, () => AfterPlayerAction(), spell);
                }
                return;
            }
            case BattleState.Select:
            {
                if (!_scene.Inside(tx, ty)) return;
                var who = _scene.UnitAt(tx, ty);
                if (_choosingMove && _actor != null && _reach != null && _reach.ContainsKey((tx, ty)) && (who == null || who == _actor))
                {
                    // 파란 칸 — 그리로 걷고 다 걸으면 메뉴
                    foreach (var p in Scene.PathTo(_reach, (tx, ty))) _actor.Path.Enqueue(p);
                    _reach = null;
                    _attackTiles.Clear();
                    _choosingMove = false;
                    _bs = BattleState.Moving;
                    return;
                }
                if (_choosingMove) return;   // 범위 밖 — 무시(취소는 Esc · 오른쪽 단추)
                if (who == null)
                {
                    // 빈 칸 — 키(Enter)와 터치면 시스템 메뉴, PC 마우스 클릭은 고른 것만 푼다(시스템 메뉴는 X 키)
                    if (_actor == null && _reach == null) { if (mx < 0 || TouchUi) OpenSystemMenu(); }
                    else ClearSelection();
                    return;
                }
                if (who.Side == Side.Player && !who.Acted && !who.Ai) { _selected = who; OpenUnitMenu(who); return; }
                // 적이나 행동을 마친 아군 — 범위만 보여 준다
                Select(who);
                _actor = null;
                _rangeOf = who;
                return;
            }
        }
    }

    /// <summary>전투 중 키 — 방향키 커서(메뉴면 위아래), Enter · Space · Z 결정, Esc · X 취소. 처리했으면 참.</summary>
    private bool BattleKey(int vk)
    {
        bool confirm = vk is Win32.VK_RETURN or Win32.VK_SPACE;
        bool cancel = vk == Win32.VK_ESCAPE;
        var action = _keyMap.ActionFor(vk);

        // 단축 동작 — 메뉴에 그 줄이 있으면 바로 고르고, 걷기 전이면 제자리에서
        if (action is KeyAction.Attack or KeyAction.Wait && _bs is BattleState.Menu or BattleState.Select && _actor is { Acted: false } who && who.Side == Side.Player)
        {
            if (_bs == BattleState.Select) OpenUnitMenu(who);
            string label = action == KeyAction.Attack ? "공격" : "대기";
            int i = _menu.FindIndex(m => m.Label == label);
            if (i >= 0) _menu[i].Do();
            return true;
        }
        if (action == KeyAction.System && _bs == BattleState.Select)
        {
            ClearSelection();
            OpenSystemMenu();
            return true;
        }
        if (action == KeyAction.NextUnit && _bs == BattleState.Select)
        {
            var ready = Controlled.Where(u => !u.Acted).OrderBy(u => u.Icon).ToList();
            if (ready.Count > 0)
            {
                int at = _actor == null ? -1 : ready.IndexOf(_actor);
                var next = ready[(at + 1) % ready.Count];
                _cursor = (next.X, next.Y);
                _centerCursor = true;
                OpenUnitMenu(next);
            }
            return true;
        }

        if (_bs == BattleState.Menu)
        {
            if (vk == Win32.VK_UP) _menuIndex = (_menuIndex + _menu.Count - 1) % _menu.Count;
            else if (vk == Win32.VK_DOWN) _menuIndex = (_menuIndex + 1) % _menu.Count;
            else if (confirm && _menuIndex < _menu.Count) _menu[_menuIndex].Do();
            else if (cancel) BattleCancel();
            else return false;
            return true;
        }
        if (_bs is not (BattleState.Select or BattleState.Target)) return confirm || cancel || vk is Win32.VK_UP or Win32.VK_DOWN or Win32.VK_LEFT or Win32.VK_RIGHT;
        (int dx, int dy) = vk switch
        {
            Win32.VK_UP => (0, -1), Win32.VK_DOWN => (0, 1), Win32.VK_LEFT => (-1, 0), Win32.VK_RIGHT => (1, 0), _ => (0, 0),
        };
        if (dx != 0 || dy != 0)
        {
            var next = (_cursor.X + dx, _cursor.Y + dy);
            if (_scene.Inside(next.Item1, next.Item2)) { _cursor = next; KeepCursorInView(); }
            return true;
        }
        if (confirm) { BattleClick(_cursor.X, _cursor.Y, -1, -1); return true; }
        if (cancel)
        {
            if (_actor == null && _reach == null && _bs == BattleState.Select) { OpenSystemMenu(); return true; }   // 고른 것이 없으면 시스템 메뉴
            BattleCancel();
            return true;
        }
        return false;
    }

    /// <summary>커서가 화면 가장자리 두 칸 안으로 들어오면 지도를 민다(마우스로 고를 때는 안 민다).</summary>
    private void KeepCursorInView()
    {
        int mapW = _scene.Width * T, mapH = _scene.Height * T, m = 2 * T;
        int cx = _cursor.X * T, cy = _cursor.Y * T;
        if (mapW > _boardW)
        {
            if (cx - _camX < m) _camX = cx - m;
            if (cx + T - _camX > _boardW - m) _camX = cx + T - _boardW + m;
            _camX = Math.Clamp(_camX, 0, mapW - _boardW);
        }
        if (mapH > _boardH)
        {
            if (cy - _camY < m) _camY = cy - m;
            if (cy + T - _camY > _boardH - m) _camY = cy + T - _boardH + m;
            _camY = Math.Clamp(_camY, 0, mapH - _boardH);
        }
    }

    /// <summary>오른쪽 단추 · Esc — 한 단계 물린다(걸은 뒤 메뉴면 제자리로 돌아간다).</summary>
    private void BattleCancel()
    {
        switch (_bs)
        {
            case BattleState.Menu when _actor == null || _stage == MenuStage.System:
                _menu.Clear();
                _bs = BattleState.Select;
                break;
            case BattleState.Menu when _stage == MenuStage.Unit:
                ClearSelection();
                _bs = BattleState.Select;
                break;
            case BattleState.Menu when _stage == MenuStage.Spell:
                BackTo(_targetFrom);
                break;
            case BattleState.Menu:   // 걸은 뒤 메뉴 — 제자리로 돌아가 처음 메뉴
                ReturnToOrigin();
                break;
            case BattleState.Target:
                _pendingSpell = null;
                BackTo(_targetFrom);
                break;
            case BattleState.Select when _choosingMove && _actor != null:
                _choosingMove = false;
                OpenUnitMenu(_actor);
                break;
            case BattleState.Select:
                ClearSelection();
                break;
        }
    }

    private void ReturnToOrigin()
    {
        if (_actor is not { } u) return;
        (u.X, u.Y) = _origin;
        (u.FromX, u.FromY) = _origin;
        u.Path.Clear();
        u.Step = 1;
        _cursor = (u.X, u.Y);
        OpenUnitMenu(u);
    }

    private void BackTo(MenuStage stage)
    {
        if (_actor is not { } u) { ClearSelection(); _bs = BattleState.Select; return; }
        if (stage == MenuStage.AfterMove) OpenMenu();
        else OpenUnitMenu(u);
    }

    /// <summary>
    /// 아군을 고르면 — 이동 · 공격 · 마법 · 대기. 이동 범위(파랑) · 공격 범위(빨강)를 함께 보인다.
    /// </summary>
    private void OpenUnitMenu(Unit u)
    {
        Select(u);
        _choosingMove = false;
        _pendingSpell = null;
        _menu.Clear();
        _menu.Add(("이동", () => { _menu.Clear(); _choosingMove = true; _bs = BattleState.Select; }, default));
        AddActionItems(u, MenuStage.Unit);
        _stage = MenuStage.Unit;
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    /// <summary>공격(칠 적이 있으면) · 마법(걸 대상이 있으면) · 대기 — 메뉴 단계 stage 에서.</summary>
    private void AddActionItems(Unit u, MenuStage stage)
    {
        var targets = Living(Side.Other).Where(e => InRange(u, e)).ToList();
        if (targets.Count > 0) _menu.Add(("공격", () => BeginTarget(u, targets, null, stage), default));
        var spells = SpellsOf(u);
        if (spells.Any(sp => u.Mp >= sp.Mp && SpellTargets(u, sp).Count > 0))
            _menu.Add(("마법", () => OpenSpellMenu(u, stage), default));
        _menu.Add(("대기", () => { u.Acted = true; ClearSelection(); AfterPlayerAction(); }, default));
    }

    private void OpenSpellMenu(Unit u, MenuStage from)
    {
        _menu.Clear();
        foreach (var sp in SpellsOf(u))
        {
            var targets = SpellTargets(u, sp);
            if (u.Mp < sp.Mp || targets.Count == 0) continue;
            _menu.Add(($"{sp.Name}  (MP {sp.Mp})", () => BeginTarget(u, targets, sp, from), default));
        }
        _targetFrom = from;
        _stage = MenuStage.Spell;
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    private void BeginTarget(Unit u, List<Unit> targets, Spell? spell, MenuStage from)
    {
        _menu.Clear();
        _reach = null;
        _pendingSpell = spell;
        _targetFrom = from;
        _attackTiles = targets.Select(t => (t.X, t.Y)).ToHashSet();
        var first = targets.OrderBy(t => Dist(u, t)).First();
        _cursor = (first.X, first.Y);
        _bs = BattleState.Target;
    }

    /// <summary>빈 칸에서 고르면 — 턴 끝 · 취소.</summary>
    private void OpenSystemMenu()
    {
        _stage = MenuStage.System;
        _menu.Clear();
        _menu.Add(("턴 끝", EndPlayerTurn, default));
        _menu.Add(("타이틀로", ToTitle, default));
        _menu.Add(("취소", () => { _menu.Clear(); _bs = BattleState.Select; }, default));
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    /// <summary>걸은 뒤 메뉴 — 공격 · 마법 · 대기(취소하면 제자리로).</summary>
    private void OpenMenu()
    {
        var u = _actor!;
        _menu.Clear();
        _reach = null;
        _attackTiles.Clear();
        AddActionItems(u, MenuStage.AfterMove);
        _stage = MenuStage.AfterMove;
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    private void AfterPlayerAction()
    {
        if (CheckOver()) return;
        _bs = BattleState.Select;
        if (!Controlled.Any(u => !u.Acted)) EndPlayerTurn();
    }

    // ── 공격 · 컷인 ───────────────────────────────────────────────────────────

    /// <summary>a 가 d 를 치고, d 가 살아 있고 사거리 안이면 되받아친다. 다 끝나면 after.</summary>
    private void Attack(Unit a, Unit d, Action after, Spell? spell = null)
    {
        if (a != d)
        {
            a.Facing = FaceTo(a, d);
            if (spell is not { Heal: true }) d.Facing = FaceTo(d, a);
        }
        _strikes.Clear();
        _strikes.Enqueue((a, d, spell));
        _counterPending = spell == null;
        _afterStrikes = after;
        NextStrike();
    }

    private static Dir FaceTo(Unit a, Unit b)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Abs(dx) >= Math.Abs(dy) ? (dx < 0 ? Dir.Left : Dir.Right) : (dy < 0 ? Dir.Up : Dir.Down);
    }

    private void NextStrike()
    {
        if (!_strikes.TryDequeue(out _strike))
        {
            _bs = BattleState.Select;
            var after = _afterStrikes;
            _afterStrikes = null;
            after?.Invoke();
            return;
        }
        if (!_motions.TryGetValue(_strike.A.Icon, out _strikeMotion))
        {
            try { _strikeMotion = Motion.Load(_dir, _strike.A.Icon, 1, _pal); }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException) { _strikeMotion = null; }
            _motions[_strike.A.Icon] = _strikeMotion;
        }
        _strikeFrame = _strikeHeld = 0;
        _strikeClock = 0;
        _strikeTail = 0;
        _strikeDamage = -1;
        _bs = BattleState.CutIn;
    }

    private void CutInTick(double dt)
    {
        if (_strikeDamage >= 0)
        {
            // 맞은 뒤 — 모션 끝까지 돌리고 조금 쉬었다 넘어간다
            _strikeTail += dt;
        }
        _strikeClock += dt;
        int frames = _strikeMotion?.Frames.Count ?? 8;
        while (_strikeClock >= FigTick)
        {
            _strikeClock -= FigTick;
            int hold = _strikeMotion?.Frames[_strikeFrame].Hold ?? 2;
            if (++_strikeHeld < hold) continue;
            _strikeHeld = 0;
            if (_strikeFrame < frames - 1) _strikeFrame++;
        }
        bool hitFrame = _strikeMotion == null ? _strikeFrame >= frames / 2 : _strikeMotion.Frames[_strikeFrame].Hit != 0 || _strikeFrame == frames - 1;
        if (_strikeDamage < 0 && hitFrame)
        {
            _strikeDamage = Damage(_strike.A, _strike.D, _strike.S);
            _strike.D.Hp = _strike.S is { Heal: true }
                ? Math.Min(_strike.D.MaxHp, _strike.D.Hp + _strikeDamage)
                : Math.Max(0, _strike.D.Hp - _strikeDamage);
        }
        if (_strikeDamage >= 0 && _strikeFrame == frames - 1 && _strikeTail > 0.7)
        {
            // 첫 공격 뒤 — 맞은 쪽이 살아 있고 사거리 안이면 한 번 되받아친다
            var (a, d, _) = _strike;
            if (_counterPending && d.Hp > 0 && InRange(d, a)) _strikes.Enqueue((d, a, null));
            _counterPending = false;
            NextStrike();
        }
    }

    private bool _counterPending;

    // ── 적군 턴 ───────────────────────────────────────────────────────────────

    private void EnemyTick(double dt)
    {
        if ((_enemyWait -= dt) > 0) return;
        if (_enemyNow != null)
        {
            if (_enemyNow.Moving || _enemyNow.Path.Count > 0) return;
            // 다 걸었다 — 칠 수 있으면 친다
            var me = _enemyNow;
            _enemyNow = null;
            var target = Opponents(me).Where(p => InRange(me, p)).OrderBy(p => p.Hp).FirstOrDefault();
            if (target != null)
            {
                Attack(me, target, () =>
                {
                    if (CheckOver()) return;
                    _bs = BattleState.Enemy;
                    _enemyWait = 0.25;
                });
                return;
            }
            _enemyWait = 0.15;
            return;
        }
        while (_enemyQueue.TryDequeue(out var e))
        {
            if (e.Hp <= 0) continue;
            EnemyStep(e);
            _enemyNow = e;
            _selected = e;
            return;
        }
        // 다 움직였다
        _selected = Living(Side.Player).FirstOrDefault();
        if (!CheckOver()) NextTurn();
    }

    /// <summary>
    /// 적 하나의 움직임 — 칠 수 있는 자리가 있으면 그중 가장 약한 아군을 칠 자리로, 없으면 가장 가까운 아군 쪽으로 갈 수 있는 데까지.
    /// </summary>
    private void EnemyStep(Unit e)
    {
        var reach = _scene.Reach(e);
        var players = Opponents(e).ToList();
        if (players.Count == 0) return;

        (int X, int Y)? best = null;
        int bestScore = int.MaxValue;
        var stops = _scene.Stops(reach, e).ToList();
        foreach (var spot in stops)
            foreach (var p in players)
            {
                int d = Math.Abs(spot.X - p.X) + Math.Abs(spot.Y - p.Y);
                if (d < e.RangeMin || d > e.RangeMax) continue;
                int score = p.Hp * 10 + Scene.PathTo(reach, spot).Count;
                if (score < bestScore) { bestScore = score; best = spot; }
            }
        if (best == null)
        {
            // 땅 거리(아군에게서 퍼져 나간 거리)가 가장 짧은 칸으로
            var field = DistanceField(players);
            foreach (var spot in stops)
                if (field.TryGetValue(spot, out int d) && d < bestScore) { bestScore = d; best = spot; }
        }
        if (best is { } to && to != (e.X, e.Y))
            foreach (var p in Scene.PathTo(reach, to)) e.Path.Enqueue(p);
        _enemyWait = 0.1;
    }

    private Dictionary<(int X, int Y), int> DistanceField(List<Unit> from)
    {
        var dist = new Dictionary<(int X, int Y), int>();
        var q = new PriorityQueue<(int X, int Y), int>();
        foreach (var p in from) { dist[(p.X, p.Y)] = 0; q.Enqueue((p.X, p.Y), 0); }
        ReadOnlySpan<(int, int)> dirs = [(0, 1), (-1, 0), (0, -1), (1, 0)];
        while (q.TryDequeue(out var c, out int d))
        {
            if (dist[c] != d) continue;
            foreach (var (dx, dy) in dirs)
            {
                var n = (c.X + dx, c.Y + dy);
                int cost = _scene.MoveCost(n.Item1, n.Item2);
                if (cost < 0) continue;
                if (dist.TryGetValue(n, out int had) && had <= d + cost) continue;
                dist[n] = d + cost;
                q.Enqueue(n, d + cost);
            }
        }
        return dist;
    }

    // ── 그리기 ────────────────────────────────────────────────────────────────

    /// <summary>사람보다 먼저 — 이동 범위(파랑) · 공격 범위(빨강). 전투가 아니면 모드 > 이동·공격 범위 보기일 때 고른 사람 것을.</summary>
    private void DrawRanges()
    {
        if (!InBattle)
        {
            if (!_settings.ShowRange || _selected == null || _selected.Moving || _selected.Path.Count > 0) return;
            if (_freeReachFor != (_selected, _selected.X, _selected.Y))
            {
                _freeReachFor = (_selected, _selected.X, _selected.Y);
                _freeReach = _scene.Reach(_selected);
                _freeStops = _scene.Stops(_freeReach, _selected).ToList();
                _freeAttack = AttackTiles(_selected, _freeStops);
            }
            foreach (var t in _freeStops) FillTile(t, ReachColor);
            foreach (var t in _freeAttack) FillTile(t, AttackColor);
            return;
        }
        if (_reach != null && _actor != null) foreach (var t in _scene.Stops(_reach, _actor)) FillTile(t, _bs == BattleState.Menu ? (ReachColor & 0x00FFFFFF) | 0x30000000 : ReachColor);   // 메뉴가 떠 있으면 흐리게
        else if (_reach != null && _rangeOf != null) foreach (var t in _scene.Stops(_reach, _rangeOf)) FillTile(t, ReachColor);
        uint target = _pendingSpell is { Heal: true } ? 0x8040E060u : TargetColor;
        foreach (var t in _attackTiles) FillTile(t, _bs == BattleState.Target ? target : AttackColor);
    }

    private (Unit?, int, int) _freeReachFor;
    private Dictionary<(int X, int Y), (int X, int Y)>? _freeReach;
    private HashSet<(int X, int Y)> _freeAttack = new();
    private List<(int X, int Y)> _freeStops = new();

    private (int X, int Y) TileScreen(int tx, int ty) =>
        (_offX + (tx * T - _camX) * _zoom, _offY + (ty * T - _camY) * _zoom);

    private void FillTile((int X, int Y) t, uint argb)
    {
        var (sx, sy) = TileScreen(t.X, t.Y);
        int cell = T * _zoom;
        FillRect(sx + 1, sy + 1, cell - 2, cell - 2, argb);
    }

    /// <summary>사람 위에 — 체력 막대, 정보 창, 메뉴, 턴 알림, 컷인.</summary>
    private void DrawBattleHud()
    {
        if (!InBattle) return;
        int cell = T * _zoom;
        foreach (var u in _scene.Units.Where(u => _showGauges && u.MaxHp > 0 && u.Hp > 0))
        {
            int sx = _offX + (int)Math.Round((u.DrawX * T - _camX) * _zoom);
            int sy = _offY + (int)Math.Round((u.DrawY * T - _camY) * _zoom);
            int bh = Math.Max(3, _zoom);
            FillRect(sx + 2, sy + cell - bh - 1, cell - 4, bh, 0xC0000000);
            FillRect(sx + 2, sy + cell - bh - 1, (cell - 4) * u.Hp / u.MaxHp, bh,
                     u.Ai ? 0xFFE0C040 : u.Side == Side.Player ? 0xFF40D060 : 0xFFE04040);
        }

        int fs = Math.Max(14, 7 * _zoom);
        // 위 가운데 — 몇 턴 · 누구 차례
        string top = _bs switch
        {
            BattleState.Talk => TouchUi ? "1장  (누르면 다음 · 뒤로 대사 건너뛰기)" : "1장  (Enter · 클릭 다음 · Esc 대사 건너뛰기)",
            _ when _enemyPhase && _bs != BattleState.Over => $"1장 전투 · {_turn}턴 · 적군 차례",
            BattleState.Over => $"1장 전투 · {_turn}턴",
            _ when TouchUi => $"1장 전투 · {_turn}턴 · 아군 차례   (누르기 고르기 · 길게 누르기/뒤로 물리기 · 빈 칸 누르면 턴 끝)",
            _ => $"1장 전투 · {_turn}턴 · 아군 차례   (방향키 커서 · Enter 결정 · Esc 물리기 · {KeyBindings.KeyName(_keyMap[KeyAction.Attack])} 공격 · " +
                 $"{KeyBindings.KeyName(_keyMap[KeyAction.Wait])} 대기 · {KeyBindings.KeyName(_keyMap[KeyAction.System])} 턴 끝 · {KeyBindings.KeyName(_keyMap[KeyAction.KeyHelp])} 단축키)",
        };
        var title = Text.Render(top, fs, 0xFFFFFFFF);
        FillRect(_clientW / 2 - title.W / 2 - 12, 8, title.W + 24, title.H + 8, 0xA0000000);
        DrawText(title, _clientW / 2 - title.W / 2, 12);

        // 왼쪽 아래 — 마우스 밑 사람(없으면 고른 사람)
        var hover = HoverUnit() ?? _actor;
        if (hover is { MaxHp: > 0 }) DrawInfo(hover, fs);

        if (_bs is BattleState.Select or BattleState.Target or BattleState.Moving && !_mouseMoved) DrawCursor();   // 마우스를 쓰는 동안에는 커서를 숨긴다
        if (_bs == BattleState.Menu) DrawMenu(fs);

        if (_bs is BattleState.Banner or BattleState.Over && _banner.Length > 0)
        {
            var b = Text.Render(_banner, fs * 2, 0xFFFFE070);
            FillRect(0, _clientH / 2 - b.H, _clientW, b.H * 2, 0xA0000000);
            DrawText(b, _clientW / 2 - b.W / 2, _clientH / 2 - b.H / 2);
        }

        if (_bs == BattleState.CutIn) DrawCutIn(fs);
        DrawTalk();
    }

    /// <summary>정보 창에 보일 사람 — 커서 칸의 사람.</summary>
    private Unit? HoverUnit() => _scene.UnitAt(_cursor.X, _cursor.Y);

    /// <summary>마우스가 움직였으면 커서를 그 칸으로.</summary>
    private void MouseToCursor()
    {
        if (!InBattle || _mouseX < 0) return;
        int gx = (int)Math.Floor((_mouseX - _offX) / (double)_zoom) + _camX, gy = (int)Math.Floor((_mouseY - _offY) / (double)_zoom) + _camY;
        var t = ((int)Math.Floor(gx / (double)T), (int)Math.Floor(gy / (double)T));
        if (_scene.Inside(t.Item1, t.Item2)) _cursor = t;
    }

    private bool _mouseMoved;

    /// <summary>커서 — 칸 네 귀퉁이에 꺾쇠(깜박이지 않는다 — 눈이 아프다).</summary>
    private void DrawCursor()
    {
        var (sx, sy) = TileScreen(_cursor.X, _cursor.Y);
        int cell = T * _zoom, len = cell / 3, t = 2 * Math.Max(2, _zoom);
        uint c = CursorColor;
        FillRect(sx, sy, len, t, c); FillRect(sx, sy, t, len, c);
        FillRect(sx + cell - len, sy, len, t, c); FillRect(sx + cell - t, sy, t, len, c);
        FillRect(sx, sy + cell - t, len, t, c); FillRect(sx, sy + cell - len, t, len, c);
        FillRect(sx + cell - len, sy + cell - t, len, t, c); FillRect(sx + cell - t, sy + cell - len, t, len, c);
    }

    private void DrawInfo(Unit u, int fs)
    {
        string[] lines =
        [
            $"{CharacterNames.Of(u.Icon)}  Lv {u.Level}",
            u.MaxMp > 0 ? $"HP {u.Hp} / {u.MaxHp}   MP {u.Mp} / {u.MaxMp}" : $"HP {u.Hp} / {u.MaxHp}",
            $"공격 {u.Atk}  방어 {u.Def}",
            $"이동 {u.Move}  사거리 {(u.RangeMin == u.RangeMax ? $"{u.RangeMax}" : $"{u.RangeMin}~{u.RangeMax}")}",
        ];
        var imgs = lines.Select((l, i) => Text.Render(l, fs, i == 0 ? (u.Side == Side.Player ? 0xFF90E0FF : 0xFFFF9090) : 0xFFFFFFFF)).ToList();
        int w = imgs.Max(i => i.W) + 24, h = imgs.Sum(i => i.H + 2) + 16;
        int x = 12, y = _clientH - h - 12;
        FillRect(x, y, w, h, 0xC0101020);
        int cy = y + 8;
        foreach (var img in imgs) { DrawText(img, x + 12, cy); cy += img.H + 2; }
    }

    private void DrawMenu(int fs)
    {
        var (sx, sy) = _actor is { } u ? TileScreen(u.X, u.Y) : TileScreen(_cursor.X, _cursor.Y);
        int cell = T * _zoom;
        var imgs = _menu.Select(m => Text.Render(m.Label, fs, 0xFFFFFFFF)).ToList();
        int w = imgs.Max(i => i.W) + 32, rowH = imgs.Max(i => i.H) + 12;
        int x = Math.Min(sx + cell + 6, _clientW - w - 4), y = Math.Clamp(sy, 4, _clientH - rowH * imgs.Count - 4);
        for (int i = 0; i < _menu.Count; i++)
        {
            var rect = (x, y + i * rowH, w, rowH);
            if (_mouseX >= rect.x && _mouseY >= rect.Item2 && _mouseX < x + w && _mouseY < rect.Item2 + rowH && _mouseMoved) _menuIndex = i;
            FillRect(rect.x, rect.Item2, w, rowH - 2, i == _menuIndex ? 0xE0405090u : 0xD0202030u);
            if (i == _menuIndex) DrawText(Text.Render("▶", fs, 0xFFFFE070), x + 2, rect.Item2 + 5);
            DrawText(imgs[i], x + 16, rect.Item2 + 5);
            _menu[i] = (_menu[i].Label, _menu[i].Do, rect);
        }
    }

    /// <summary>
    /// 컷인 — 어두운 판 위에 오른쪽에 공격하는 쪽(FIGANI 공격 모션), 왼쪽에 맞는 쪽(FIGANI 서기 모션을 좌우로 뒤집어).
    /// 맞는 순간 맞는 쪽이 흔들리고 번쩍인다(회복은 초록). 원거리 · 마법은 쏘는 장면 다음에 맞는 쪽만 보인다.
    /// </summary>
    private void DrawCutIn(int fs)
    {
        FillRect(0, 0, _clientW, _clientH, 0xB0000000);
        int s = Math.Max(1, Math.Min(_clientW * 8 / 10 / Motion.ScreenWidth, _clientH * 7 / 10 / Motion.ScreenHeight));
        int bw = Motion.ScreenWidth * s, bh = Motion.ScreenHeight * s;
        int bx = (_clientW - bw) / 2, by = (_clientH - bh) / 2;
        FillRect(bx, by, bw, bh, 0xFF181820);
        var clip = (bx, by, bx + bw, by + bh);

        var (a, d, spell) = _strike;
        // 배경 — 공격하는 쪽이 선 칸의 지형으로 BG.DAT 를 골라 50줄부터(원본처럼), 위아래는 BG 0번 줄무늬
        if (Back(0) is { } frame)
        {
            BlitFrame(frame, Motion.ScreenWidth, 50, bx, by, s, false, 0, 0, clip);
            BlitFrame(frame[(50 * Motion.ScreenWidth)..], Motion.ScreenWidth, 50, bx, by + 150 * s, s, false, 0, 0, clip);
        }
        if (Back(_scene.Terrain(a.X, a.Y)) is { } back)
            BlitFrame(back, Motion.ScreenWidth, back.Length / Motion.ScreenWidth, bx, by + 50 * s, s, false, 0, 0, clip);
        bool heal = spell is { Heal: true };
        bool hit = _strikeDamage >= 0;
        bool ranged = spell != null || Dist(a, d) >= 2;
        bool showAttacker = !ranged || !hit || a == d;
        bool showDefender = (!ranged || hit) && a != d || a == d && hit;

        int defenderSide = 0;   // 맞는 쪽이 선 쪽(오른쪽 1) — 피해 숫자를 그 위에
        if (showDefender)
        {
            int shake = hit && !heal && _strikeTail < 0.4 ? (int)(Math.Sin(_strikeTail * 70) * 3 * s) : 0;
            (uint tint, int amount) = hit && _strikeTail < 0.18 ? (heal ? (0xFF60FF80u, 150) : (0xFFFFFFFFu, 170)) : (0u, 0);
            var stand = StandOf(d.Icon);
            if (stand != null)
            {
                var f = stand.Frames[(int)(_time * 6) % stand.Frames.Count];
                // 원본 모션은 편마다 자리가 정해져 있다(주인공은 오른쪽, 몬스터는 왼쪽).
                // 공격하는 쪽 모션과 같은 쪽에 서 있을 때만 좌우로 뒤집어 맞은편에 세운다.
                bool mirror = a != d && SideOf(stand) == (_strikeMotion != null ? SideOf(_strikeMotion) : 1);
                defenderSide = mirror ? 1 - SideOf(stand) : SideOf(stand);
                int fx = mirror ? Motion.ScreenWidth - f.X - f.Width : f.X;
                BlitFrame(f.Pixels, f.Width, f.Height, bx + fx * s + shake, by + f.Y * s, s, mirror, tint, amount, clip);
            }
            else
            {
                var icon = _icons.Frame(d.Icon, (int)Dir.Right * 3);
                BlitFrame(icon, T, T, bx + bw / 6 + shake, by + bh / 3, s * 3, false, tint, amount, clip);
            }
        }
        if (showAttacker)
        {
            if (_strikeMotion != null)
            {
                var f = _strikeMotion.Frames[_strikeFrame];
                BlitFrame(f.Pixels, f.Width, f.Height, bx + f.X * s, by + f.Y * s, s, false, 0, 0, clip);
            }
            else
            {
                var big = _icons.Frame(a.Icon, (int)Dir.Left * 3 + _strikeFrame % 3);
                BlitFrame(big, T, T, bx + bw * 2 / 3 - T * s, by + bh / 3, s * 3, false, 0, 0, clip);
            }
            if (spell != null && !hit)
            {
                // 마법을 모으는 빛 — 공격하는 쪽 둘레에 깜박이는 빛
                uint glow = heal ? 0x3040FF80u : 0x30FF8040u;
                if ((int)(_time * 10) % 2 == 0) FillRect(bx + bw / 2, by, bw / 2, bh, glow);
            }
        }

        string what = spell != null ? $"  [{spell.Name}]" : "";
        var who = Text.Render($"{CharacterNames.Of(a.Icon)}  →  {CharacterNames.Of(d.Icon)}{what}", fs, 0xFFFFFFFF);
        DrawText(who, _clientW / 2 - who.W / 2, by - who.H - 8);

        // 맞는 쪽 체력
        int hpW = bw / 2, hpH = Math.Max(8, fs / 2);
        FillRect(bx + bw / 4, by + bh + 10, hpW, hpH, 0xFF000000);
        FillRect(bx + bw / 4, by + bh + 10, hpW * d.Hp / Math.Max(1, d.MaxHp), hpH, d.Side == Side.Player ? 0xFF40D060 : 0xFFE04040);
        var hp = Text.Render($"HP {d.Hp} / {d.MaxHp}", fs, 0xFFFFFFFF);
        DrawText(hp, _clientW / 2 - hp.W / 2, by + bh + 14 + hpH);
        if (hit)
        {
            var dmg = heal ? Text.Render($"+{_strikeDamage}", fs * 3, 0xFF60FF80)
                           : Text.Render(d.Hp == 0 ? $"-{_strikeDamage}  쓰러짐!" : $"-{_strikeDamage}", fs * 3, 0xFFFF5050);
            DrawText(dmg, bx + bw * (defenderSide == 1 ? 3 : 1) / 4 - dmg.W / 2, by + bh / 6);
        }
    }

    private readonly Dictionary<int, uint[]?> _backs = new();

    /// <summary>전투 배경 BG.DAT 번호 i — 320x100 RLE(너비 · 높이 머리). 없으면 null.</summary>
    private uint[]? Back(int i)
    {
        if (_backs.TryGetValue(i, out var px)) return px;
        try
        {
            var e = Dat.Load(Path.Combine(_dir, "BG.DAT"), i);
            px = e.Length > 8 && Dat.U16(e, 0) == Motion.ScreenWidth ? Rle.Decode(e, 4, Motion.ScreenWidth, Dat.U16(e, 2), _pal) : null;
        }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { px = null; }
        return _backs[i] = px;
    }

    /// <summary>모션이 화면 어느 쪽에 서 있나 — 오른쪽 1, 왼쪽 0(컷들의 가운데 x 로).</summary>
    private static int SideOf(Motion m) => m.Frames.Average(f => f.X + f.Width / 2.0) > Motion.ScreenWidth / 2.0 ? 1 : 0;

    private Motion? StandOf(int icon)
    {
        if (_stands.TryGetValue(icon, out var m)) return m;
        try { m = Motion.Load(_dir, icon, 0, _pal); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException) { m = null; }
        return _stands[icon] = m;
    }

    /// <summary>w x h 그림을 s 배로 — 좌우 뒤집기, 색 섞기(amount 0~255), 클립 상자 안에만.</summary>
    private void BlitFrame(uint[] px, int w, int h, int x, int y, int s, bool mirror, uint tint, int amount,
                           (int X0, int Y0, int X1, int Y1) clip)
    {
        int y0 = Math.Max(Math.Max(0, clip.Y0), y), y1 = Math.Min(Math.Min(_fbH, clip.Y1), y + h * s);
        int x0 = Math.Max(Math.Max(0, clip.X0), x), x1 = Math.Min(Math.Min(_fbW, clip.X1), x + w * s);
        uint tr = tint >> 16 & 255, tg = tint >> 8 & 255, tb = tint & 255;
        for (int yy = y0; yy < y1; yy++)
        {
            int srow = (yy - y) / s * w;
            for (int xx = x0; xx < x1; xx++)
            {
                int sx = (xx - x) / s;
                uint c = px[srow + (mirror ? w - 1 - sx : sx)];
                if (c == 0) continue;
                if (amount > 0)
                {
                    uint Mix(uint v, uint t) => (uint)((v * (255 - amount) + t * amount) / 255);
                    c = Mix(c >> 16 & 255, tr) << 16 | Mix(c >> 8 & 255, tg) << 8 | Mix(c & 255, tb);
                }
                _fb[yy * _fbW + xx] = c | 0xFF000000;
            }
        }
    }

    // ── 사람 층에 칠하기(알파 섞기) ────────────────────────────────────────────

    private void FillRect(int x, int y, int w, int h, uint argb)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(_fbW, x + w), y1 = Math.Min(_fbH, y + h);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++) Over(ref _fb[yy * _fbW + xx], argb);
    }

    private void DrawText(Text.Image img, int x, int y)
    {
        for (int yy = 0; yy < img.H; yy++)
        {
            int sy = y + yy;
            if ((uint)sy >= (uint)_fbH) continue;
            for (int xx = 0; xx < img.W; xx++)
            {
                int sx = x + xx;
                if ((uint)sx >= (uint)_fbW) continue;
                uint c = img.Px[yy * img.W + xx];
                if (c >> 24 != 0) Over(ref _fb[sy * _fbW + sx], c);
            }
        }
    }

    /// <summary>w x h 그림을 s 배로 — 클립 상자(cx0..cx1, cy0..cy1) 안에만.</summary>
    private void BlitScaled(uint[] px, int w, int h, int x, int y, int s, int cx0, int cy0, int cx1, int cy1)
    {
        int y0 = Math.Max(Math.Max(0, cy0), y), y1 = Math.Min(Math.Min(_fbH, cy1), y + h * s);
        int x0 = Math.Max(Math.Max(0, cx0), x), x1 = Math.Min(Math.Min(_fbW, cx1), x + w * s);
        for (int yy = y0; yy < y1; yy++)
        {
            int srow = (yy - y) / s * w;
            for (int xx = x0; xx < x1; xx++)
            {
                uint c = px[srow + (xx - x) / s];
                if (c != 0) _fb[yy * _fbW + xx] = c | 0xFF000000;
            }
        }
    }

    /// <summary>src 를 dst 위에 알파로 겹친다(둘 다 알파가 곱해지지 않은 BGRA).</summary>
    private static void Over(ref uint dst, uint src)
    {
        uint sa = src >> 24;
        if (sa == 255) { dst = src; return; }
        uint d = dst, da = d >> 24;
        uint oa = sa + da * (255 - sa) / 255;
        if (oa == 0) { dst = 0; return; }
        uint Mix(int shift) => (((src >> shift & 255) * sa + (d >> shift & 255) * da * (255 - sa) / 255) / oa) & 255;
        dst = oa << 24 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
    }
}
