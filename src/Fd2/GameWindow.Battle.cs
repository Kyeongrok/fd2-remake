using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 전투 — 아군 턴(고르기 → 메뉴: 이동 · 공격 · 마법 · 대기) → 컴퓨터 아군 → 적군 턴, 이동 범위(파랑) · 공격 범위(빨강),
/// 컷인(공격하는 쪽 FIGANI 공격 모션 + 맞는 쪽 서기 모션, 원거리 · 마법은 맞는 쪽 화면으로 넘어간다).
/// </summary>
/// <remarks>
/// 수(능력치 · 명중 · 피해 · 필살 · 두 번 치기 · 되받아치기 · 경험치 · 레벨업 · 상태 · 떨굼 · 이기고 짐)는 원작 규칙
/// <see cref="Battle"/>(FD2.EXE 의 표와 식, 분석-전투)이 셈하고, 컴퓨터 아군 · 적의 생각은 <see cref="BattleAi"/>(분석-AI — 갈래 · 공격/마법/물건 점수 · 길),
/// 여기는 화면 · 차례 흐름만 맡는다.
/// 적은 장면 정보의 등장 차례 k 가 k 턴 시작에 나온다(원작은 판마다 사건 함수가 부른다 — 그 코드는 아직 못 옮겨 차례 번호로 갈음한 짐작).
/// 편 2(나중에 합류하는 아군)는 그때 우리 편으로 든다.
/// </remarks>
internal sealed partial class GameWindow
{
    private enum BattleState { None, Select, Moving, Menu, Target, CutIn, Enemy, Banner, Over, Talk, Script, Fx }

    private const uint ReachLight = 0x58FFFFFF, ReachColor = 0x5A3C78FF, AttackColor = 0x5AFF4646, TargetColor = 0x80FF3030;
    private const double FigTick = 0.055;

    private BattleState _bs = BattleState.None;

    /// <summary>지금 장면의 원작 규칙(LoadScene 이 만든다) — 차례 수 · 돈 · 난수도 여기 있다.</summary>
    private Battle _battle = null!;

    /// <summary>이름 · 물건 글 표(FDTXT 0) — 처음 쓸 때 읽는다.</summary>
    private NameTable? _names;

    private int Turn => _battle.Turn;

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
    /// <summary>알림 앞에 원본 글꼴로 그릴 이름 줄(FDTXT 0, -1 없음).</summary>
    private int _bannerName = -1;
    private double _bannerLeft;
    private Action? _afterBanner;

    /// <summary>
    /// 한 번 치기 — Spell ≥ 0 이면 그 마법(원작 표), Heal &gt; 0 이면 물건 회복, 둘 다 없으면 무기.
    /// </summary>
    private sealed record Hit(Unit A, Unit D, int Spell = -1, int Heal = 0)
    {
        public bool Magic => Spell >= 0 || Heal > 0;
    }

    // 컷인 — 한 번 치기씩. 두 번 치기는 같은 짝을 한 번 더, 되받아치기는 첫 공격이 다 끝난 뒤, 범위 마법은 대상마다
    private readonly Queue<Hit> _strikes = new();
    private Action? _afterStrikes;
    private Hit _strike = null!;

    /// <summary>지금 컷인이 회복 · 도움 쪽인가(초록 숫자).</summary>
    private bool _strikeFriendly;

    /// <summary>이번에 건 마법(-1 없음)과 건 사람, 대상마다 결과 — 다 끝나면 마법 경험을 셈한다.</summary>
    private int _castSpell = -1;
    private Unit? _castCaster;
    private readonly List<SpellResult> _spellResults = new();
    private Motion? _strikeMotion;
    private int _strikeFrame, _strikeHeld, _strikeDamage = -1;
    private bool _strikeMiss, _strikeCrit;
    private double _strikeClock, _strikeTail;

    /// <summary>첫 공격이 끝나면 되받아칠 짝(맞은 쪽, 친 쪽) — 없으면 null.</summary>
    private (Unit D, Unit A)? _counter;

    /// <summary>이번 무리(한 사람의 치기들)에서 얻은 경험 — 원작은 대입이라 마지막 한 대 몫만 남는다.</summary>
    private (Unit Who, int Exp)? _groupExp;

    /// <summary>이번 공격에서 쓰러진 사람과 쓰러뜨린 사람 — 다 끝나면 떨굼을 처리한다.</summary>
    private readonly List<(Unit Dead, Unit Killer)> _fallen = new();

    /// <summary>공격이 끝나면 보일 알림(레벨업 · 독 · 떨굼).</summary>
    private readonly List<Notice> _strikeNotes = new();

    private readonly Dictionary<int, Motion?> _motions = new(), _stands = new();

    /// <summary>지금 메뉴가 어느 단계인가 — 취소할 때 어디로 돌아갈지.</summary>
    private enum MenuStage { System, AfterMove, Spell, Over, Ask, Deploy, Town, Item }
    private MenuStage _stage;

    /// <summary>이동할 칸을 고르는 중인가(메뉴에서 「이동」을 고른 뒤).</summary>
    private bool _choosingMove;

    /// <summary>대상 고르기에 쓸 마법(-1 이면 보통 공격), 그 앞 메뉴 단계.</summary>
    private int _pendingSpell = -1;
    private MenuStage _targetFrom;

    // 컴퓨터 차례 — 한 명씩(컴퓨터 아군 → 적). 적은 두 번 훑는다: 첫 번은 마법 · 물건 점수 6 이상만, 둘째 번은 남은 모두(sub_1D8BA)
    private BattleAi _ai = null!;
    private readonly List<Unit> _aiOrder = new();
    private int _aiIndex, _aiPass;
    private Unit? _enemyNow;
    private AiPlan? _plan;
    private double _enemyWait;

    /// <summary>지금 도는 컴퓨터 차례가 컴퓨터 아군(편 1) 차례인가 — 끝나면 적 차례로.</summary>
    private bool _alliesPhase;

    private bool InBattle => _bs != BattleState.None;

    /// <summary>적군 차례인가 — 컷인 · 알림 중에도 누구 차례인지 알도록.</summary>
    private bool _enemyPhase;

    // ── 시작 · 끝 ─────────────────────────────────────────────────────────────

    private void StartBattle(int scene)
    {
        LoadScene(scene, BattleParty);
        _partyAtStart = _campaign?.Party.Select(r => (byte[])r.Clone()).ToList();   // 지면 되돌릴 동료 표(싸움 중 합류가 겹치지 않게)
        Array.Clear(_flags);
        _battle.Turn = 0;
        _battle.Gold = _campaign?.Gold ?? 0;
        _outcome = 0;
        _enemyPhase = false;
        SetMusic(PlayerMusic);   // 장면에 들어갈 때 곡(byte_51E63[장면])
        // 전투 앞 대사 — 묶음 0. 묶음 k(k ≥ 1)는 등장 차례 k 의 무리가 나올 때 보인다(1장: 1 해적 두목, 2 해적과 마주침,
        // 3 하노 · 하와터, 4 해적 F · G, 5 해적 두목 97, 6 해양순찰대 68 — 대사를 그려 맞춰 본 것).
        _bs = BattleState.Talk;
        if (Controlled.OrderBy(u => u.Slot).FirstOrDefault() is { } lead) { _cursor = (lead.X, lead.Y); _centerCursor = true; }
        _forcedOutcome = 0;
        _groundFired.Clear();
        RunStart(NextTurn);   // 장면 시작 함수(대본이 없으면 대사 묶음 0)
    }

    private void StopBattle()
    {
        _bs = BattleState.None;
        SetMusic(-1);
        LoadScene(_scene.Index);
    }

    /// <summary>
    /// 그 장면의 우리 차례 곡(byte_51E63[장면]) · 다른 차례 곡(byte_51E81[장면]) — FDMUS 곡 번호, 표가 없으면 -1.
    /// 원작(sub_1A30B)은 컴퓨터 아군 차례 뒤 적 차례 앞에서 다른 차례 곡으로, 차례 수가 오른 뒤 우리 차례 곡으로 바꾼다(둘이 다를 때만 멈췄다 튼다).
    /// </summary>
    private int PlayerMusic => _tables?.SceneMusic(_scene.Index) ?? -1;
    private int EnemyMusic => _tables?.EnemyMusic(_scene.Index) ?? -1;

    private static int Dist(Unit a, Unit b) => Battle.Dist(a, b);

    private IEnumerable<Unit> Living(Side side) => _scene.Units.Where(u => u.Side == side && u.Hp > 0);

    /// <summary>u 가 칠 상대 — 원작의 편으로 편 0 ↔ 편 1 · 2.</summary>
    private IEnumerable<Unit> Opponents(Unit u) => _scene.Units.Where(o => o.Hp > 0 && o.Team == 0 != (u.Team == 0));

    /// <summary>내가 움직이는 아군(컴퓨터가 움직이는 아군은 빼고).</summary>
    private IEnumerable<Unit> Controlled => Living(Side.Player).Where(u => !AiControlled(u));

    /// <summary>컴퓨터가 움직이는 아군(NPC)인가 — 설정 「NPC 아군도 내가 움직이기」를 켜면 아니다.</summary>
    private bool AiControlled(Unit u) => u.Ai && !_settings.ControlAllies;

    /// <summary>무기로 칠 수 있는 상대(편 0) — 지금 자리에서.</summary>
    private List<Unit> StrikeTargets(Unit u) => Living(Side.Other).Where(e => _battle.CanStrike(u, (u.X, u.Y), e)).ToList();

    /// <summary>
    /// 그 마법을 걸 수 있는 가운데 칸 — 사거리 안 칸 가운데 범위 안에 대상(표 +6 의 편)이 하나라도 있는 칸.
    /// 회복 · 풀기는 다쳤거나 걸린 사람이 없어도 원작은 걸 수 있지만, 헛걸음이 없게 대상이 있는 칸만 보인다.
    /// </summary>
    private List<(int X, int Y)> SpellCenters(Unit u, int spell) =>
        _battle.SpellCenters(u, spell).Where(c => _battle.SpellTargets(u, spell, c).Count > 0).ToList();

    /// <summary>지금 쓸 수 있는 마법 — 아는 마법 가운데 봉쇄가 아니고 MP 가 되며 걸 대상이 있는 것.</summary>
    private List<int> CastableSpells(Unit u) =>
        _battle.KnownSpells(u).Where(m => _battle.CanCast(u, m) && SpellCenters(u, m).Count > 0).ToList();

    // ── 차례(sub_1A30B) ─────────────────────────────────────────────────────

    /// <summary>
    /// 우리 편 차례 시작 — 차례 수 +1, 그 차례의 무리가 나오고, 우리 편 독 · 상태 줄이기(sub_1A866(2)), 이기고 짐 확인.
    /// </summary>
    private void NextTurn()
    {
        _battle.Turn++;
        SetMusic(PlayerMusic);
        int turn = Turn;
        bool heuristic = !UseEvents;   // 대본이 없으면 「등장 차례 k 무리는 k 턴에」
        if (heuristic) SpawnPhase(turn);
        foreach (var u in _scene.Units) u.Acted = false;
        _enemyPhase = false;
        ClearSelection();
        if (Controlled.OrderBy(u => u.Slot).FirstOrDefault() is { } lead) { _cursor = (lead.X, lead.Y); _centerCursor = true; }
        var notes = Tick(2);
        void Banner() => ShowBanner($"{turn}턴 — 아군 차례", () =>
        {
            _bs = BattleState.Select;
            if (!Controlled.Any(u => !u.Paralyzed)) EndPlayerTurn();   // 움직일 우리 편이 모두 마비면 저절로 끝(sub_13565)
        });
        void Notes() => ShowNotices(notes, () => RunTiming(2, () => { if (!CheckOver()) Banner(); }));
        if (heuristic && _scene.Roster.Any(e => e.Phase == turn))
        {
            _bs = BattleState.Talk;
            StartTalk(_scene.Index, [turn], Notes);   // 이 차례의 무리가 나올 때의 대사(없으면 바로 넘어간다)
        }
        else Notes();
    }

    /// <summary>그 편 차례 시작의 독 · 상태 줄이기 — 독으로 쓰러진 사람의 떨굼까지 알림으로.</summary>
    private List<Notice> Tick(int team)
    {
        var died = new List<Unit>();
        var notes = _battle.StatusTick(team, died);
        notes.AddRange(_battle.Drops(died, null));
        return notes;
    }

    /// <summary>
    /// 우리 편 차례 끝 — 한 번도 안 움직인 우리 편은 쉬어 최대 HP 의 1/5 회복(독 · 마비 빼고), 컴퓨터 아군 독 · 상태 → 컴퓨터 아군 차례.
    /// </summary>
    private void EndPlayerTurn()
    {
        ClearSelection();
        _enemyPhase = true;
        foreach (var u in _scene.Units.Where(u => u.Team == 2 && u.Hp > 0 && !u.Acted)) _battle.Rest(u);
        var notes = Tick(1);
        ShowNotices(notes, () => RunTiming(1, () =>
        {
            if (CheckOver()) return;
            StartAi(allies: true);
        }));
    }

    /// <summary>적 차례 — 끝냄 표시를 모두 지우고, 적 독 · 상태(sub_1A866(0)), 「적군 차례」 뒤 배열 차례대로.</summary>
    private void StartEnemyPhase() => RunTiming(0, () => { if (!CheckOver()) BeginEnemyPhase(); });

    private void BeginEnemyPhase()
    {
        foreach (var u in _scene.Units) u.Acted = false;
        SetMusic(EnemyMusic);
        var notes = Tick(0);
        ShowNotices(notes, () =>
        {
            if (CheckOver()) return;
            ShowBanner($"{Turn}턴 — 적군 차례", () => StartAi(allies: false));
        });
    }

    /// <summary>컴퓨터 차례 시작 — 그 편 사람들을 배열 차례로. 컴퓨터 아군(sub_1D80B)은 한 번, 적(sub_1D8BA)은 두 번 훑는다.</summary>
    private void StartAi(bool allies)
    {
        _alliesPhase = allies;
        _ai = new BattleAi(_battle);
        _aiOrder.Clear();
        _aiOrder.AddRange(_scene.Units.Where(u => u.Team == (allies ? 1 : 0) && (!allies || !_settings.ControlAllies)).OrderBy(u => u.Slot));
        _aiIndex = 0;
        _aiPass = allies ? 2 : 1;
        _bs = BattleState.Enemy;
        _enemyNow = null;
        _enemyWait = 0.2;
    }

    private void ShowBanner(string text, Action after, double seconds = 1.1)
    {
        _banner = text;
        _bannerName = -1;
        _bannerLeft = seconds;
        _afterBanner = after;
        _bs = BattleState.Banner;
    }

    /// <summary>알림들을 차례로(한 줄씩 짧게, 대사 묶음이면 대사로) 보이고 다 보면 after.</summary>
    private void ShowNotices(List<Notice> notes, Action after)
    {
        if (notes.Count == 0) { after(); return; }
        var n = notes[0];
        var rest = notes.GetRange(1, notes.Count - 1);
        if (n.Talk >= 0)
        {
            _bs = BattleState.Talk;
            StartTalk(_scene.Index, [n.Talk], () => ShowNotices(rest, after));
            return;
        }
        if (n.Event >= 0)
        {
            RunEvent(n.Event, () => ShowNotices(rest, after));
            return;
        }
        ShowBanner(n.Text, () => ShowNotices(rest, after), 0.9);
        _bannerName = n.Name;
    }

    /// <summary>이기고 졌나 — 원작 판정(<see cref="Battle.Judge"/>: 기본은 적 전멸 = 이김, 사울 쓰러짐 = 짐, 판마다 덧붙인 조건).</summary>
    private bool CheckOver()
    {
        if (_bs == BattleState.Over || _victoryRunning) return true;
        switch (_forcedOutcome != 0 ? _forcedOutcome : _battle.Judge())
        {
            case 1:
                _bs = BattleState.Over;
                OnOutcome(1);
                _banner = "패배 — 누르면 메뉴";
                _bannerName = -1;
                return true;
            case 2:
                _bs = BattleState.Over;
                int turns = Turn;
                _victoryRunning = true;
                RunVictory(() =>   // 승리 뒤 함수(대사 · 합류) 뒤 동료 표를 고친다
                {
                    _victoryRunning = false;
                    OnOutcome(2);
                    _bs = BattleState.Over;
                    _banner = $"승리! ({turns}턴) — 누르면 메뉴";
                    _bannerName = -1;
                });
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
                if (_actor is { Moving: false } m && m.Path.Count == 0)
                {
                    _bs = BattleState.None;   // 땅 칸 사건이 도는 동안 다시 들어오지 않게
                    GroundCheck(m, () => { if (m.Hp > 0 && !CheckOver()) { _actor = m; OpenMenu(); } });   // 우리 편도 멈춘 칸의 사건(sub_18890 → sub_13A44)
                }
                break;
            case BattleState.CutIn:
                CutInTick(dt);
                break;
            case BattleState.Talk:
                TalkTick(dt);
                break;
            case BattleState.Script:
                BattleScriptTick(dt);
                break;
            case BattleState.Fx:
                FxTick(dt);
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
        _pendingSpell = -1;
        _reach = null;
        _attackTiles.Clear();
        _menu.Clear();
    }

    private void Select(Unit u)
    {
        _actor = u;
        _origin = (u.X, u.Y);
        _reach = _battle.Reach(u);
        _attackTiles = AttackTiles(u, _scene.Stops(_reach, u));
    }

    /// <summary>멈출 수 있는 칸들에서 사거리 안이지만 이동 범위 밖인 칸 — 공격 가능 범위(최대 16 이상은 십자 직선).</summary>
    private HashSet<(int X, int Y)> AttackTiles(Unit u, IEnumerable<(int X, int Y)> stands)
    {
        var set = new HashSet<(int X, int Y)>();
        var reach = stands.ToHashSet();
        int r = u.RangeMax < 16 ? u.RangeMax : u.RangeMax - 16;
        foreach (var (sx, sy) in reach)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if ((dx, dy) == (0, 0) || !Battle.InArea(dx, dy, u.RangeMax, u.RangeMin)) continue;
                    var t = (sx + dx, sy + dy);
                    if (_scene.Inside(t.Item1, t.Item2) && !reach.Contains(t)) set.Add(t);
                }
        return set;
    }

    /// <summary>내가 고를 수 있는 우리 편인가 — 편 2, 아직 안 끝냈고 마비가 아님(sub_117E7).</summary>
    private bool Ready(Unit u) => u.Side == Side.Player && !u.Acted && !AiControlled(u) && !u.Paralyzed;

    private void BattleClick(int tx, int ty, int mx, int my)
    {
        switch (_bs)
        {
            case BattleState.Over:
                OpenOverMenu();
                return;
            case BattleState.Menu:
                foreach (var item in _menu)
                    if (mx >= item.Rect.X && my >= item.Rect.Y && mx < item.Rect.X + item.Rect.W && my < item.Rect.Y + item.Rect.H)
                    {
                        item.Do();
                        return;
                    }
                return;
            case BattleState.Target:
            {
                if (_actor == null || !_attackTiles.Contains((tx, ty))) return;
                var a = _actor;
                int spell = _pendingSpell;
                if (_pendingItem >= 0 && _tables != null && _pendingItem < a.Bag.Count && _tables.Items[a.Bag[_pendingItem].Item].Use == 23)
                {
                    // 전송법장 — 고른 우리 편을 갈 칸으로(전송술과 같은 2단계, MP 없이)
                    if (_scene.UnitAt(tx, ty) is not { Team: 2 } who) return;
                    BeginTeleportDest(a, who);
                    _teleportByItem = true;
                    return;
                }
                if (_pendingItem >= 0)
                {
                    int slot = _pendingItem;
                    _pendingItem = -1;
                    a.Acted = true;
                    ClearSelection();
                    UsePlayerItem(a, slot, (tx, ty), AfterPlayerAction);
                    return;
                }
                if (spell == 23 && _teleportWho == null)
                {
                    // 전송술 1단계 — 옮길 우리 편을 골랐다, 이제 갈 칸
                    if (_scene.UnitAt(tx, ty) is not { Team: 2 } who) return;
                    BeginTeleportDest(a, who);
                    return;
                }
                if (spell == 23 && _teleportWho is { } moved)
                {
                    a.Acted = true;
                    ClearSelection();
                    Teleport(a, moved, (tx, ty), AfterPlayerAction);
                    return;
                }
                if (spell >= 0)
                {
                    // 마법 — 고른 칸을 가운데로(사거리 0 이면 자기 칸)
                    a.Acted = true;
                    ClearSelection();
                    CastSpell(a, spell, (tx, ty), AfterPlayerAction);
                    return;
                }
                if (_scene.UnitAt(tx, ty) is not { } t) return;
                a.Acted = true;
                ClearSelection();
                Attack(a, t, AfterPlayerAction);
                return;
            }
            case BattleState.Select:
            {
                if (!_scene.Inside(tx, ty)) return;
                var who = _scene.UnitAt(tx, ty);
                if (_choosingMove && _actor != null && _reach != null && _reach.ContainsKey((tx, ty)) && (who == null || who == _actor))
                {
                    if (who == _actor) { OpenMenu(); return; }   // 제자리를 한 번 더 — 걷지 않고 메뉴
                    // 파란 칸 — 그리로 걷고 다 걸으면 메뉴
                    foreach (var p in Scene.PathTo(_reach, (tx, ty))) _actor.Path.Enqueue(p);
                    _reach = null;
                    _attackTiles.Clear();
                    _choosingMove = false;
                    _bs = BattleState.Moving;
                    return;
                }
                if (_choosingMove && who != null && who != _actor && Ready(who)) { _selected = who; BeginMove(who); return; }   // 다른 아군으로 바꿔 고름
                if (_choosingMove) return;   // 범위 밖 — 무시(취소는 Esc · 오른쪽 단추)
                if (who == null)
                {
                    // 빈 칸 — 키(Enter)와 터치면 시스템 메뉴, PC 마우스 클릭은 고른 것만 푼다(시스템 메뉴는 X 키)
                    if (_actor == null && _reach == null) { if (mx < 0 || TouchUi) OpenSystemMenu(); }
                    else ClearSelection();
                    return;
                }
                if (Ready(who)) { _selected = who; BeginMove(who); return; }
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
        if (_bs == BattleState.Over)
        {
            if (confirm || cancel) OpenOverMenu();
            return true;
        }

        // 단축 동작 — 메뉴에 그 줄이 있으면 바로 고르고, 걷기 전이면 제자리에서
        if (action is KeyAction.Attack or KeyAction.Wait && _bs is BattleState.Menu or BattleState.Select && _actor is { Acted: false } who && who.Side == Side.Player)
        {
            if (_bs == BattleState.Select) OpenMenu();
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
            var ready = Controlled.Where(Ready).OrderBy(u => u.Slot).ToList();
            if (ready.Count > 0)
            {
                int at = _actor == null ? -1 : ready.IndexOf(_actor);
                var next = ready[(at + 1) % ready.Count];
                _cursor = (next.X, next.Y);
                _centerCursor = true;
                BeginMove(next);
            }
            return true;
        }

        if (_bs == BattleState.Menu && CrossMenu)
        {
            // 십자 메뉴 — 방향키는 그쪽 단추로, Enter 로 고른다
            if (vk is Win32.VK_UP or Win32.VK_DOWN or Win32.VK_LEFT or Win32.VK_RIGHT)
            {
                var dir = vk switch { Win32.VK_UP => Dir.Up, Win32.VK_DOWN => Dir.Down, Win32.VK_LEFT => Dir.Left, _ => Dir.Right };
                int i = _menu.FindIndex(m => SlotOf(m.Label) == dir);
                if (i >= 0) _menuIndex = i;
            }
            else if (confirm && _menuIndex < _menu.Count) _menu[_menuIndex].Do();
            else if (cancel) BattleCancel();
            else return false;
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
            if (_scene.Inside(next.Item1, next.Item2)) { _cursor = next; KeepCursorInView(); PlaySfx(Sound.UiBank, Sound.Cursor); }
            return true;
        }
        if (confirm) { BattleClick(_cursor.X, _cursor.Y, -1, -1); return true; }
        if (cancel)
        {
            if (_actor == null && _reach == null && _bs == BattleState.Select) { CursorToNextReady(); return true; }   // 고른 것이 없으면 다음 안 움직인 아군으로(원작)
            BattleCancel();
            return true;
        }
        return false;
    }

    /// <summary>Esc 를 거듭 누르면 — 커서가 아직 행동하지 않은 아군을 차례로 옮겨 다닌다(지금 커서 칸 다음 사람부터).</summary>
    private void CursorToNextReady()
    {
        var ready = Controlled.Where(Ready).OrderBy(u => u.Slot).ToList();
        if (ready.Count == 0) return;
        int at = ready.FindIndex(u => (u.X, u.Y) == _cursor);
        var next = ready[(at + 1) % ready.Count];
        _cursor = (next.X, next.Y);
        _centerCursor = true;
        PlaySfx(Sound.UiBank, Sound.Cursor);
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
            case BattleState.Menu when _stage == MenuStage.Over:   // 판정 뒤 메뉴 — 물릴 곳이 없다
                break;
            case BattleState.Menu when _stage == MenuStage.Ask:    // 묻는 메뉴 — 물리면 「아니오」
                _menu[^1].Do();
                break;
            case BattleState.Menu when _stage == MenuStage.Deploy: // 편성 — 물릴 곳이 없다
                break;
            case BattleState.Menu when _stage == MenuStage.Item:   // 가방 — 걸은 뒤 메뉴로
                _menuGlyphs.Clear();
                BackTo(_itemFrom);
                break;
            case BattleState.Menu when _stage == MenuStage.Town:   // 마을 — 한 단계 앞 메뉴로
                _townBack?.Invoke();
                break;
            case BattleState.Menu when _actor == null || _stage == MenuStage.System:
                _menu.Clear();
                _bs = BattleState.Select;
                break;
            case BattleState.Menu when _stage == MenuStage.Spell:
                BackTo(_targetFrom);
                break;
            case BattleState.Menu:   // 걸은 뒤 메뉴 — 제자리로 돌아가 다시 갈 칸 고르기
                ReturnToOrigin();
                break;
            case BattleState.Target:
                _pendingSpell = -1;
                _pendingItem = -1;
                _teleportWho = null;
                _teleportByItem = false;
                BackTo(_targetFrom);
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
        BeginMove(u);
    }

    private void BackTo(MenuStage stage)
    {
        if (_actor is not { } u) { ClearSelection(); _bs = BattleState.Select; return; }
        OpenMenu();
    }

    /// <summary>
    /// 아군을 처음 고르면 이동 범위(파랑) · 공격 범위(빨강)만 보인다 — 메뉴는 갈 칸을 한 번 더 눌러야 뜬다(원작: 고르고 → 제자리 결정이면 그 자리 메뉴).
    /// </summary>
    private void BeginMove(Unit u)
    {
        PlaySfx(Sound.UiBank, Sound.Cursor);
        Select(u);
        _choosingMove = true;
        _pendingSpell = -1;
        _menu.Clear();
        _bs = BattleState.Select;
    }

    /// <summary>
    /// 공격(무기가 있고 사거리 안에 적이 있으면) · 마법(걸 대상이 있고 봉쇄가 아니면) · 대기 — 메뉴 단계 stage 에서.
    /// 대기는 원작의 쉬기/조사: 안 움직였으면 쉬어 최대 HP 의 1/5 회복(독 · 마비 빼고, sub_13FD4). 상자 조사는 아직 없다.
    /// </summary>
    private void AddActionItems(Unit u, MenuStage stage)
    {
        var targets = StrikeTargets(u);
        if (targets.Count > 0) _menu.Add(("공격", () => BeginTarget(u, targets.Select(t => (t.X, t.Y)).ToList(), -1, stage), default));
        // 걸은 뒤에도 마법을 연다 — 원작 메뉴(sub_18D8C)가 움직인 뒤 마법을 막는지는 아직 안 봤다(짐작)
        if (!u.Sealed && CastableSpells(u).Count > 0)
            _menu.Add(("마법", () => OpenSpellMenu(u, stage), default));
        if (u.Bag.Count > 0) _menu.Add(("물건", () => { _itemFrom = stage; OpenItemMenu(u, stage); }, default));
        _menu.Add(("대기", () =>
        {
            if ((u.X, u.Y) == _origin) { _battle.Rest(u); u.Facing = Dir.Down; PlaySfx(Sound.UiBank, Sound.Rest); }   // 안 걸었으면 쉬어 회복 — 쉬는 사람은 앞을 본다
            u.Acted = true;
            if (_battle.ChestAt(u) is { } chest) { AskChest(u, chest.Index, chest.Hidden); return; }
            ClearSelection();
            AfterPlayerAction();
        }, default));
    }

    /// <summary>쉬고 나서 상자 칸이면 묻는다(sub_190AC) — 「예」면 열고, 아니면 그냥 쉰다. 상자가 사건이면 그 사건을 건다.</summary>
    private void AskChest(Unit u, int k, bool hidden)
    {
        PlaySfx(Sound.UiBank, Sound.Chest);
        _stage = MenuStage.Ask;
        _menu.Clear();
        _menuGlyphs.Clear();
        _menu.Add((hidden ? "보물상자 발견 — 꺼낸다" : "보석상자 — 연다", () =>
        {
            ClearSelection();
            if (_scene.Chests[k].Kind == 0 && u.Bag.Count >= 8) { AskSwap(u, k); return; }
            var (notes, ev) = _battle.OpenChest(u, k);
            ShowNotices(notes, () => { if (ev >= 0) RunEvent(ev, AfterPlayerAction, u.Slot); else AfterPlayerAction(); });
        }, default));
        _menu.Add(("그냥 둔다", () => { ClearSelection(); AfterPlayerAction(); }, default));
        _menuIndex = 0;
        _selected = u;
        _bs = BattleState.Menu;
    }

    /// <summary>가방이 찼다 — 원작처럼 바꿀 물건을 고르게 한다(고른 물건은 상자에 남는다).</summary>
    private void AskSwap(Unit u, int k)
    {
        _stage = MenuStage.Ask;
        _menu.Clear();
        _menuGlyphs.Clear();
        _menu.Add(("가방이 가득 — 바꿀 물건을 고르세요", () => { }, default));
        _menuGlyphs[0] = 181 + _scene.Chests[k].Value;
        for (int i = 0; i < u.Bag.Count; i++)
        {
            int slot = i;
            _menuGlyphs[_menu.Count] = 181 + u.Bag[i].Item;
            _menu.Add((u.Bag[i].Equipped ? "(장비) " : "", () =>
            {
                _menuGlyphs.Clear();
                ClearSelection();
                ShowNotices(_battle.SwapChest(u, k, slot), AfterPlayerAction);
            }, default));
        }
        _menu.Add(("바꾸지 않는다", () => { _menuGlyphs.Clear(); ClearSelection(); AfterPlayerAction(); }, default));
        _menuIndex = 1;
        _bs = BattleState.Menu;
    }

    /// <summary>메뉴 줄에 붙일 원본 글꼴 이름(이름 표 줄 번호) — 물건 이름처럼 글자로 없는 것.</summary>
    private readonly Dictionary<int, int> _menuGlyphs = new();

    /// <summary>마법 고르기 — 아는 마법(원작 이름 · MP 표) 가운데 지금 걸 수 있는 것.</summary>
    private void OpenSpellMenu(Unit u, MenuStage from)
    {
        _menu.Clear();
        foreach (int m in CastableSpells(u))
        {
            int spell = m;
            _menu.Add(($"{SpellNames.Of(spell)}  (MP {_battle.SpellOf(spell)!.Mp})", () => BeginTarget(u, SpellCenters(u, spell), spell, from), default));
        }
        _targetFrom = from;
        _stage = MenuStage.Spell;
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    /// <summary>칠 · 걸 칸 고르기 — 무기는 상대가 선 칸, 마법은 가운데 칸(spell ≥ 0). 가까운 칸에 커서.</summary>
    private void BeginTarget(Unit u, List<(int X, int Y)> cells, int spell, MenuStage from)
    {
        _menu.Clear();
        _reach = null;
        _pendingItem = -1;
        _teleportWho = null;
        _pendingSpell = spell;
        _targetFrom = from;
        _attackTiles = cells.ToHashSet();
        var first = cells.OrderBy(c => Math.Abs(c.X - u.X) + Math.Abs(c.Y - u.Y) == 0 && spell >= 0 && cells.Count > 1 ? 99 : Math.Abs(c.X - u.X) + Math.Abs(c.Y - u.Y)).First();
        _cursor = first;
        _bs = BattleState.Target;
    }

    /// <summary>빈 칸에서 고르면 — 턴 끝 · 취소.</summary>
    private void OpenSystemMenu()
    {
        PlaySfx(Sound.UiBank, Sound.Menu);   // 메뉴가 뜰 때 원작처럼 소리
        _stage = MenuStage.System;
        _menu.Clear();
        _menu.Add(("턴 끝", EndPlayerTurn, default));
        if (!_enemyPhase) _menu.Add(("전황 기록", SaveBattleRecord, default));
        _menu.Add(("타이틀로", ToTitle, default));
        _menu.Add(("취소", () => { _menu.Clear(); _bs = BattleState.Select; }, default));
        _menuIndex = 0;
        _bs = BattleState.Menu;
    }

    /// <summary>갈 칸(제자리도)을 정한 뒤 메뉴 — 공격 · 마법 · 물건 · 쉬기(취소하면 제자리로 돌아가 다시 갈 칸 고르기).</summary>
    private void OpenMenu()
    {
        var u = _actor!;
        PlaySfx(Sound.UiBank, Sound.Menu);
        _choosingMove = false;
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
        if (!Controlled.Any(u => !u.Acted && !u.Paralyzed)) EndPlayerTurn();   // 남은 우리 편이 없거나 모두 마비(sub_13565)
    }


    // ── 공격 · 컷인 ───────────────────────────────────────────────────────────

    /// <summary>
    /// a 가 d 를 친다 — 보통 공격은 칠 횟수(1, 3% 로 2, 무기 특수 3 이면 하나 더)만큼, 상대가 쓰러지면 멈추고,
    /// 다 친 뒤 d 가 되받아칠 수 있으면(바로 옆 · 최소 사거리 1 · 마비 아님) 한 번(그 안에서 다시 두 번 치기 판정). 다 끝나면 after.
    /// </summary>
    private void Attack(Unit a, Unit d, Action after)
    {
        if (a != d)
        {
            a.Facing = FaceTo(a, d);
            d.Facing = FaceTo(d, a);
        }
        BeginStrikes(after);
        for (int n = _battle.StrikeCount(a); n > 0; n--) _strikes.Enqueue(new Hit(a, d));
        _counter = (d, a);
        NextStrike();
    }

    private void BeginStrikes(Action after)
    {
        _strikes.Clear();
        _fallen.Clear();
        _strikeNotes.Clear();
        _spellResults.Clear();
        _groupExp = null;
        _counter = null;
        _castSpell = -1;
        _castCaster = null;
        _afterStrikes = after;
    }

    /// <summary>
    /// 마법을 건다 — MP 를 빼고(물건으로 쓰면 안 뺀다), 가운데 칸 둘레 범위 안 대상마다 컷인 한 번씩. 다 끝나면 마법 경험(우리 편만) 뒤 after.
    /// </summary>
    private void CastSpell(Unit caster, int spell, (int X, int Y) center, Action after, bool fromItem = false)
    {
        if (center != (caster.X, caster.Y)) caster.Facing = DirOf(center.X - caster.X, center.Y - caster.Y);
        if (!fromItem) _battle.PayMp(caster, spell);
        BeginStrikes(after);
        if (!fromItem) (_castSpell, _castCaster) = (spell, caster);   // 물건으로 쓴 마법은 경험이 없다
        var targets = _battle.SpellTargets(caster, spell, center);
        foreach (var t in targets) _strikes.Enqueue(new Hit(caster, t, spell));
        // 원작이 지도에서 보이는 마법이면 대상 칸들 위에 그림을 먼저(FDOTHER 6)
        _strikeOnMap = MapSpell(caster, spell);
        if (_strikeOnMap) PlaySpellFx(spell, targets.Select(t => (t.X, t.Y)), NextStrike);
        else NextStrike();
    }

    /// <summary>
    /// 물건을 쓴다(컴퓨터) — 회복(쓰임 5 닳음 · 13 안 닳음)은 범위 안 대상마다 수치의 90~99.9%(sub_211A4 도 회복 마법과 같은 굴림으로 본 짐작),
    /// 20 · 21 · 24 는 그 마법을 MP 없이. 경험은 없다.
    /// </summary>
    private void UseItem(Unit u, int slot, (int X, int Y) center, Action after)
    {
        if (_tables == null || slot < 0 || slot >= u.Bag.Count) { after(); return; }
        var it = _tables.Items[u.Bag[slot].Item];
        if (it.Use is 20 or 21 or 24) { CastSpell(u, it.UseValue, center, after, fromItem: true); return; }
        var targets = _ai.ItemTargets(u, it, center);
        if (it.Use == 5) u.Bag.RemoveAt(slot);
        BeginStrikes(after);
        foreach (var t in targets) _strikes.Enqueue(new Hit(u, t, Heal: it.UseValue));
        NextStrike();
    }

    private static Dir FaceTo(Unit a, Unit b)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Abs(dx) >= Math.Abs(dy) ? (dx < 0 ? Dir.Left : Dir.Right) : (dy < 0 ? Dir.Up : Dir.Down);
    }

    private void NextStrike()
    {
        while (true)
        {
            if (_strikes.TryDequeue(out _strike))
            {
                // 친 쪽이나 맞을 쪽이 쓰러졌으면 남은 치기는 없다
                if (_strike.A.Hp <= 0 || _strike.D.Hp <= 0) continue;
                BeginStrike();
                return;
            }
            FlushExp();
            if (_counter is { } pair)
            {
                var (d, a) = pair;
                _counter = null;
                if (_battle.CanCounter(d, a))
                {
                    for (int n = _battle.StrikeCount(d); n > 0; n--) _strikes.Enqueue(new Hit(d, a));
                    continue;
                }
            }
            break;
        }
        // 다 쳤다 — 마법 경험, 쓰러진 사람의 떨굼(쓰러뜨린 사람마다), 레벨업 · 독 알림 뒤 다음으로
        _bs = BattleState.Select;
        _strikeOnMap = false;
        if (_castSpell >= 0 && _castCaster != null)
            _strikeNotes.AddRange(_battle.GainExp(_castCaster, _battle.SpellExp(_castCaster, _castSpell, _spellResults)));
        _castSpell = -1;
        var notes = new List<Notice>(_strikeNotes);
        foreach (var group in _fallen.GroupBy(f => f.Killer))
            notes.AddRange(_battle.Drops(group.Select(f => f.Dead), group.Key));
        _strikeNotes.Clear();
        _fallen.Clear();
        var after = _afterStrikes;
        _afterStrikes = null;
        ShowNotices(notes, () => { _bs = BattleState.Select; after?.Invoke(); });
    }

    /// <summary>한 사람의 치기가 끝났다 — 모은 경험을 넣고(sub_1E292) 레벨업 알림.</summary>
    private void FlushExp()
    {
        if (_groupExp is not { } g) return;
        var (who, exp) = g;
        _groupExp = null;
        _strikeNotes.AddRange(_battle.GainExp(who, exp));
    }

    private void BeginStrike()
    {
        // 무리가 바뀌면(되받아치기) 앞 무리의 경험을 먼저
        if (_groupExp is { } g && g.Who != _strike.A) FlushExp();
        // 마법 · 물건은 셋째 모션(FIGANI 3c+2 — 마법 몸짓, 17 캐릭터만 있다), 없으면 공격 모션
        _strikeMotion = _strike.Magic ? MotionOf(_strike.A.Icon, 2) ?? MotionOf(_strike.A.Icon, 1) : MotionOf(_strike.A.Icon, 1);
        _strikeFriendly = _strike.Heal > 0
                          || _strike.Spell >= 0 && _battle.SpellOf(_strike.Spell) is { Side: not 0 };
        _strikeFrame = _strikeHeld = 0;
        _strikeSounded = -1;
        FrameSound();
        _strikeClock = 0;
        _strikeTail = 0;
        _strikeDamage = -1;
        _strikeMiss = _strikeCrit = false;
        if (_strikeOnMap)
        {
            // 지도 위 마법 — 원작처럼 전투 화면 없이 그 자리에서 셈하고 알림만
            ResolveStrike();
            var d = _strike.D;
            if (_strikeMiss) _strikeNotes.Add(new Notice(" 빗나감", Battle.NameLine(d)));
            else if (_strikeDamage > 0) _strikeNotes.Add(new Notice(_strikeFriendly ? $" HP {_strikeDamage} 회복" : $" {_strikeDamage} 피해", Battle.NameLine(d)));
            NextStrike();
            return;
        }
        _bs = BattleState.CutIn;
    }

    /// <summary>승리 뒤 함수가 도는 중 — 그동안 다시 판정하지 않는다.</summary>
    private bool _victoryRunning;

    /// <summary>지금 치기들이 지도 위 마법인가(전투 화면을 안 띄운다).</summary>
    private bool _strikeOnMap;

    private int _strikeSounded = -1;

    /// <summary>모션 컷이 바뀌면 그 컷의 소리(컷 +5, 머리 [4] 의 묶음 FDOTHER 48~53)를 한 번.</summary>
    private void FrameSound()
    {
        if (_strikeMotion is not { SoundEntry: >= 0 } m || _strikeFrame == _strikeSounded || _strikeFrame >= m.Frames.Count) return;
        _strikeSounded = _strikeFrame;
        if (m.Frames[_strikeFrame].Sound is var s and > 0) PlaySfx(m.SoundEntry, s);
    }

    private Motion? MotionOf(int icon, int kind)
    {
        if (_motions.TryGetValue(icon * 4 + kind, out var m)) return m;
        try { m = Motion.Load(_dir, icon, kind, _pal); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException) { m = null; }
        return _motions[icon * 4 + kind] = m;
    }

    /// <summary>맞는 컷 — 원작 식으로 한 번 치기 · 마법 · 물건을 셈해 HP 를 바꾼다.</summary>
    private void ResolveStrike()
    {
        var (a, d) = (_strike.A, _strike.D);
        if (_strike.Spell >= 0)
        {
            var r = _battle.CastOn(a, _strike.Spell, d);
            _spellResults.Add(r);
            _strikeDamage = r.Amount;
            _strikeMiss = !r.Hit;
            if (!_strikeOnMap && r.Hit) StartSpellFx(_strike.Spell, a.Team == 0);   // 전투 화면 마법 효과(0~8)
            if (r.Hit && r.Note != null) _strikeNotes.Add(new Notice(r.Note, Battle.NameLine(d)));
        }
        else if (_strike.Heal > 0)
        {
            _strikeDamage = _battle.HealBy(d, _strike.Heal).Amount;
        }
        else
        {
            var r = _battle.Strike(a, d);
            _strikeDamage = r.Damage;
            _strikeMiss = !r.Hit;
            _strikeCrit = r.Critical;
            if (r.Poisoned) _strikeNotes.Add(new Notice(" 독에 걸림", Battle.NameLine(d)));
            if (a.Team == 2) _groupExp = (a, _battle.AttackExp(a, d, r.Damage));
        }
        if (d.Hp <= 0) _fallen.Add((d, a));
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
        FrameSound();
        SpellFxTick(dt);
        bool hitFrame = _strikeMotion == null ? _strikeFrame >= frames / 2 : _strikeMotion.Frames[_strikeFrame].Hit != 0 || _strikeFrame == frames - 1;
        if (_strikeDamage < 0 && hitFrame) ResolveStrike();
        if (_strikeDamage >= 0 && _strikeFrame == frames - 1 && _strikeTail > 0.7 && !SpellFxRunning) NextStrike();
    }

    // ── 컴퓨터 차례 ───────────────────────────────────────────────────────────

    private void EnemyTick(double dt)
    {
        if ((_enemyWait -= dt) > 0) return;
        if (_enemyNow != null)
        {
            if (_enemyNow.Moving || _enemyNow.Path.Count > 0) return;
            var me = _enemyNow;
            _enemyNow = null;
            var plan = _plan ?? new AiPlan();
            GroundCheck(me, () => { _bs = BattleState.Enemy; Arrive(me, plan); });
            return;
        }
        if (NextAiUnit() is { } next)
        {
            next.Acted = true;
            _selected = next;
            Run(next, _ai.Decide(next));
            return;
        }
        // 다 움직였다 — 컴퓨터 아군 다음은 적, 적 다음은 다음 차례
        _selected = Living(Side.Player).FirstOrDefault();
        if (CheckOver()) return;
        if (_alliesPhase) StartEnemyPhase();
        else NextTurn();
    }

    /// <summary>
    /// 다음에 움직일 컴퓨터 유닛 — 살았고 안 끝냈고 마비가 아닌 사람을 배열 차례로. 적 첫 훑기는 마법 · 물건 점수가 6 이상인 사람만.
    /// </summary>
    private Unit? NextAiUnit()
    {
        while (true)
        {
            if (_aiIndex >= _aiOrder.Count)
            {
                if (_aiPass >= 2) return null;
                _aiPass = 2;
                _aiIndex = 0;
                continue;
            }
            var u = _aiOrder[_aiIndex++];
            if (u.Hp <= 0 || u.Acted || u.Paralyzed) continue;
            if (_aiPass == 1 && _ai.PriorityScore(u) < 6) continue;
            return u;
        }
    }

    /// <summary>계획대로 — 마법 · 물건은 그 자리에서, 걸을 길이 있으면 걸은 뒤 <see cref="Arrive"/>, 쉬기는 바로.</summary>
    private void Run(Unit u, AiPlan plan)
    {
        if (plan.Spell >= 0)
        {
            CastSpell(u, plan.Spell, plan.Center, () =>
            {
                if (plan.ThenAttack && u.Hp > 0 && !CheckOver()) Run(u, _ai.FollowUp(u));
                else AiDone();
            });
            return;
        }
        if (plan.ItemSlot >= 0) { UseItem(u, plan.ItemSlot, plan.Center, AiDone); return; }
        if (plan.Rest) { _battle.Rest(u); u.Facing = Dir.Down; AiDone(); return; }
        if (plan.Path.Count > 0)
        {
            foreach (var c in plan.Path) u.Path.Enqueue(c);
            _enemyNow = u;
            _plan = plan;
            _enemyWait = 0.1;
            return;
        }
        Arrive(u, plan);
    }

    /// <summary>다 걸었다 — 칠 상대가 사거리 안이면 치고, 도둑은 상자 칸이면 훔치고, 달아나는 사람은 목표 칸이면 사라진다.</summary>
    private void Arrive(Unit u, AiPlan plan)
    {
        if (plan.Target is { Hp: > 0 } t && _battle.CanStrike(u, (u.X, u.Y), t)) { Attack(u, t, AiDone); return; }
        var notes = new List<Notice>();
        if (plan.StealChest >= 0 && (u.X, u.Y) == plan.Goal) notes.Add(_ai.Steal(u, plan.StealChest));
        if (plan.VanishAtGoal && (u.X, u.Y) == plan.Goal)
        {
            u.DropKind = 255;   // 판에서 사라짐(sub_32975, +5 = 1) — 쓰러뜨린 것이 아니라 떨굼은 없다
            u.Hp = 0;
            notes.Add(new Notice(" 이(가) 사라졌다", Battle.NameLine(u)));
        }
        ShowNotices(notes, AiDone);
    }

    private void AiDone()
    {
        if (CheckOver()) return;
        _bs = BattleState.Enemy;
        _enemyWait = 0.25;
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
                _freeReach = _battle.Reach(_selected);
                _freeStops = _scene.Stops(_freeReach, _selected).ToList();
                _freeAttack = AttackTiles(_selected, _freeStops);
            }
            foreach (var t in _freeStops) FillTile(t, ReachColor);
            foreach (var t in _freeAttack) FillTile(t, AttackColor);
            return;
        }
        // 처음 고르면 원작처럼 갈 수 있는 칸만 밝게(공격 범위는 대상 고를 때만)
        if (_reach != null && (_actor ?? _rangeOf) is { } who) foreach (var t in _scene.Stops(_reach, who)) FillTile(t, ReachLight);
        if (_bs != BattleState.Target) return;
        uint target = _pendingSpell >= 0 && _battle.SpellOf(_pendingSpell) is { Side: not 0 } ? 0x8040E060u : TargetColor;
        foreach (var t in _attackTiles) FillTile(t, target);
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
        int cell = T * _zoom, gap = argb == ReachLight ? 0 : 1;   // 밝히기는 칸 사이 틈 없이(원작)
        FillRect(sx + gap, sy + gap, cell - 2 * gap, cell - 2 * gap, argb);
    }

    /// <summary>사람 위에 — 체력 막대, 정보 창, 메뉴, 턴 알림, 컷인.</summary>
    /// <summary>
    /// 승패 조건 글(sub_1B41D) — 이름 표 FDTXT 0 의 597 + 2n(승리) · 598 + 2n(패배), n = 장면. 원본 글꼴로 제목 줄 밑에.
    /// 17장(장면 16)의 「미티사망」은 원작처럼 미티가 동료가 아닐 때만 — 여기서는 늘 원작 글 그대로 보인다.
    /// </summary>
    private void DrawGoals(int fs, int y)
    {
        if (_scene.Index >= 30) return;
        int g = Math.Max(1, fs / Font16.Size);
        var win = NameImage(597 + 2 * _scene.Index, g, 0xFFFFE070);
        var lose = NameImage(598 + 2 * _scene.Index, g, 0xFFFF9090);
        if (win == null && lose == null) return;
        var l1 = Text.Render("승리 ", fs * 4 / 5, 0xFFFFE070);
        var l2 = Text.Render("  패배 ", fs * 4 / 5, 0xFFFF9090);
        int w = l1.W + (win?.W ?? 0) + l2.W + (lose?.W ?? 0), h = Math.Max(l1.H, Math.Max(win?.H ?? 0, lose?.H ?? 0));
        int x = _clientW / 2 - w / 2;
        FillRect(x - 12, y - 4, w + 24, h + 8, 0x90000000);
        DrawText(l1, x, y + (h - l1.H) / 2); x += l1.W;
        if (win != null) { DrawText(win, x, y + (h - win.H) / 2); x += win.W; }
        DrawText(l2, x, y + (h - l2.H) / 2); x += l2.W;
        if (lose != null) DrawText(lose, x, y + (h - lose.H) / 2);
    }

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

        int fs = Math.Max(14, 7 * HudZoom);
        // 위 가운데 — 몇 턴 · 누구 차례
        string top = _bs switch
        {
            BattleState.Talk => TouchUi ? $"{ChapterName(_scene.Index)}  (누르면 다음 · 뒤로 대사 건너뛰기)" : $"{ChapterName(_scene.Index)}  (Enter · 클릭 다음 · Esc 대사 건너뛰기)",
            _ when _enemyPhase && _bs != BattleState.Over => $"{ChapterName(_scene.Index)} 전투 · {Turn}턴 · {(_alliesPhase ? "컴퓨터 아군" : "적군")} 차례",
            BattleState.Over => $"{ChapterName(_scene.Index)} 전투 · {Turn}턴",
            _ when TouchUi => $"{ChapterName(_scene.Index)} 전투 · {Turn}턴 · 아군 차례 · 돈 {_battle.Gold}" + (_settings.TouchHelp ? "   (누르기 고르기 · 길게 누르기/뒤로 물리기 · 빈 칸 누르면 턴 끝)" : ""),
            _ when !_settings.ShowHelp => $"{ChapterName(_scene.Index)} 전투 · {Turn}턴 · 아군 차례 · 돈 {_battle.Gold}",
            _ => $"{ChapterName(_scene.Index)} 전투 · {Turn}턴 · 아군 차례 · 돈 {_battle.Gold}   (방향키 커서 · Enter 결정 · Esc 물리기 · {KeyBindings.KeyName(_keyMap[KeyAction.Attack])} 공격 · " +
                 $"{KeyBindings.KeyName(_keyMap[KeyAction.Wait])} 대기 · {KeyBindings.KeyName(_keyMap[KeyAction.System])} 턴 끝 · {KeyBindings.KeyName(_keyMap[KeyAction.KeyHelp])} 단축키)",
        };
        var title = Text.Render(top, fs, 0xFFFFFFFF);
        FillRect(_clientW / 2 - title.W / 2 - 12, 8, title.W + 24, title.H + 8, 0xA0000000);
        DrawText(title, _clientW / 2 - title.W / 2, 12);
        // 폰은 승패 조건 · 보물상자 · 칸 정보 · 사람 정보를 기본으로 숨기고 ESC 단추의 모바일 메뉴에서 켠다
        if (TouchUi ? _settings.TouchGoals : _settings.ShowGoals) DrawGoals(fs, 12 + title.H + 12);

        // 왼쪽 아래 — 마우스 밑 사람(없으면 고른 사람)
        DrawHudToggles(fs);
        if (!TouchUi || _settings.TouchChests) DrawChestList(fs);
        DrawMinimap();
        int tileH = !TouchUi || _settings.TouchTileInfo ? DrawTileInfo(fs) : 0;
        var hover = HoverUnit() ?? _actor;
        if (hover is { MaxHp: > 0 } && (!TouchUi || _settings.TouchUnitInfo)) DrawInfo(hover, fs, tileH);

        if (_bs is BattleState.Select or BattleState.Target or BattleState.Moving && !_mouseMoved) DrawCursor();   // 마우스를 쓰는 동안에는 커서를 숨긴다
        if (_bs == BattleState.Menu) DrawMenu(fs);

        if (_bs is BattleState.Banner or BattleState.Over && (_banner.Length > 0 || _bannerName >= 0))
        {
            var b = Text.Render(_banner.Length > 0 ? _banner : " ", fs * 2, 0xFFFFE070);
            var name = _bannerName >= 0 ? NameImage(_bannerName, Math.Max(2, fs * 2 / Font16.Size), 0xFFFFE070) : null;
            int w = b.W + (name?.W ?? 0), h = Math.Max(b.H, name?.H ?? 0);
            FillRect(0, _clientH / 2 - h, _clientW, h * 2, 0xA0000000);
            int x = _clientW / 2 - w / 2;
            if (name != null) { DrawText(name, x, _clientH / 2 - name.H / 2); x += name.W; }
            DrawText(b, x, _clientH / 2 - b.H / 2);
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

    /// <summary>정보 창 — 이름(몬스터는 원본 글꼴 이름) · 레벨 · 경험, HP · MP, 공격 · 방어 · 명중 · 회피, 이동 · 사거리, 상태.</summary>
    private void DrawInfo(Unit u, int fs, int below = 0)
    {
        string range = u.RangeMax == 0 ? "-" : u.RangeMax >= 16 ? $"직선 {u.RangeMax - 16}" : u.RangeMin == u.RangeMax ? $"{u.RangeMax}" : $"{u.RangeMin}~{u.RangeMax}";
        string status = string.Join(" ", Enumerable.Range(0, 6).Where(k => u.Status[k] > 0).Select(k => $"{Battle.StatusNames[k]}({u.Status[k]})"));
        bool monster = u.FormId >= CharacterNames.Heroes;
        var lines = new List<string>
        {
            $"{(monster ? "" : CharacterNames.Of(u.Icon))}  Lv {u.Level}{(u.Team == 2 ? $"  경험 {u.Exp}" : "")}",
            u.MaxMp > 0 ? $"HP {u.Hp} / {u.MaxHp}   MP {u.Mp} / {u.MaxMp}" : $"HP {u.Hp} / {u.MaxHp}",
            $"공격 {u.Atk}  방어 {u.Def}  명중 {u.Hit}  회피 {u.Evade}",
            $"이동 {u.Move}  사거리 {range}",
        };
        if (status.Length > 0) lines.Add(status);
        uint nameColor = u.Side == Side.Player ? 0xFF90E0FF : 0xFFFF9090;
        var imgs = lines.Select((l, i) => Text.Render(l, fs, i == 0 ? nameColor : 0xFFFFFFFF)).ToList();
        if (monster && NameImage(Battle.NameLine(u), Math.Max(1, fs / Font16.Size), nameColor) is { } glyphs)
        {
            // 몬스터 이름은 원본 글꼴로 — 레벨 글 앞에 붙인다
            var first = imgs[0];
            int w0 = glyphs.W + first.W, h0 = Math.Max(glyphs.H, first.H);
            var px = new uint[w0 * h0];
            for (int r = 0; r < glyphs.H; r++) Array.Copy(glyphs.Px, r * glyphs.W, px, r * w0, glyphs.W);
            for (int r = 0; r < first.H; r++) Array.Copy(first.Px, r * first.W, px, r * w0 + glyphs.W, first.W);
            imgs[0] = new Text.Image(px, w0, h0);
        }
        int w = imgs.Max(i => i.W) + 24, h = imgs.Sum(i => i.H + 2) + 16;
        int x = 12, y = _clientH - h - 12 - below;
        FillRect(x, y, w, h, 0xC0101020);
        int cy = y + 8;
        foreach (var img in imgs) { DrawText(img, x + 12, cy); cy += img.H + 2; }
    }

    /// <summary>
    /// 왼쪽 위 보물 목록(창세기전3 처럼) — 이 장의 안 연 보석상자(칸 속성 0x20)마다 든 것: 물건은 원본 글꼴 이름, 돈은 액수, 사건 상자는 「?」.
    /// 숨은 보물(0x40)은 찾는 재미를 남기려고 넣지 않는다. 커서가 그 상자 칸이면 노랗게.
    /// </summary>
    private void DrawChestList(int fs)
    {
        if (_scene.Index >= 30 || _bs is BattleState.CutIn or BattleState.Talk) return;
        if (_chestCellsFor != _scene)
        {
            _chestCellsFor = _scene;
            _chestCells.Clear();
            for (int y = 0; y < _scene.Height; y++)
                for (int x = 0; x < _scene.Width; x++)
                    if ((_scene.TileFlags(x, y) & 0x60) == 0x20 && _scene.Mark(x, y) is var k && k < _scene.Chests.Count) _chestCells.Add((x, y, k));
        }
        var rows = new List<Text.Image>();
        int g = Math.Max(1, fs / Font16.Size);
        foreach (var (x, y, k) in _chestCells)
        {
            if (_battle.OpenedChests.Contains(k)) continue;
            uint c = _cursor == (x, y) ? 0xFFFFE070 : 0xFFFFFFFF;
            var (kind, value) = _scene.Chests[k];
            var head = Text.Render($"({x},{y}) ", fs, c);
            var what = kind == 0 ? NameImage(181 + value, g, c) ?? Text.Render($"물건 {value}", fs, c)
                     : kind == 1 ? Text.Render(value == 0 ? "빈 상자" : $"{value} 골드", fs, c)
                     : Text.Render("?", fs, c);
            rows.Add(Join(head, what));
        }
        if (rows.Count == 0) return;
        var title = Text.Render("보물상자", fs, 0xFFA8C8FF);
        int btn = title.H;
        bool open = _settings.ShowChests;   // 접으면 제목 줄 + [+] 만
        int w = Math.Max(title.W + btn + 16, open ? rows.Max(r => r.W) : 0) + 20, h = open ? title.H + rows.Sum(r => r.H + 2) + 14 : title.H + 12;
        int bx = 12, by = Math.Max(12, VersionLabelBottom + 4);
        FillRect(bx, by, w, h, 0xC0101020);
        DrawText(title, bx + 10, by + 6);
        _chestFold = FoldButton(bx + w - btn - 6, by + 6, btn, open);
        if (!open) return;
        int cy = by + 8 + title.H;
        foreach (var r in rows) { DrawText(r, bx + 10, cy); cy += r.H + 2; }

        static Text.Image Join(Text.Image a, Text.Image b)
        {
            int w0 = a.W + b.W, h0 = Math.Max(a.H, b.H);
            var px = new uint[w0 * h0];
            for (int r = 0; r < a.H; r++) Array.Copy(a.Px, r * a.W, px, (r + (h0 - a.H) / 2) * w0, a.W);
            for (int r = 0; r < b.H; r++) Array.Copy(b.Px, r * b.W, px, (r + (h0 - b.H) / 2) * w0 + a.W, b.W);
            return new Text.Image(px, w0, h0);
        }
    }

    private Scene? _chestCellsFor;
    private readonly List<(int X, int Y, int K)> _chestCells = new();

    /// <summary>
    /// 오른쪽 위 미니맵(접기 아이콘 밑) — 칸마다 타일 평균 색, 우리 편 초록 · 컴퓨터 아군 노랑 · 적 빨강 점, 지금 보이는 곳은 흰 테두리.
    /// </summary>
    private void DrawMinimap()
    {
        if (_bs is BattleState.CutIn or BattleState.Talk || TouchUi && !_settings.ShowMinimap) return;
        int fsM = Math.Max(14, 7 * HudZoom);
        if (!_settings.ShowMinimap)
        {
            // 접힌 미니맵 — 제목 줄 + [+] 만
            var label = Text.Render("미니맵", fsM, 0xFFA8C8FF);
            int bw = label.W + label.H + 36, bh = label.H + 12, bx = _clientW - bw - 14, by = _hudIconBottom;
            FillRect(bx, by, bw, bh, 0xC0101020);
            DrawText(label, bx + 10, by + 6);
            _miniFold = FoldButton(bx + bw - label.H - 6, by + 6, label.H, false);
            return;
        }
        if (_miniFor != _scene)
        {
            _miniFor = _scene;
            _mini = new uint[_scene.Width * _scene.Height];
            for (int y = 0; y < _scene.Height; y++)
                for (int x = 0; x < _scene.Width; x++)
                {
                    long r = 0, gg = 0, b = 0;
                    int n = 0;
                    foreach (uint c in _scene.TileAt(x, y)) { if (c == 0) continue; r += c >> 16 & 255; gg += c >> 8 & 255; b += c & 255; n++; }
                    _mini[y * _scene.Width + x] = n == 0 ? 0xFF000000 : 0xFF000000 | (uint)(r / n) << 16 | (uint)(gg / n) << 8 | (uint)(b / n);
                }
        }
        int m = Math.Max(2, Math.Min(260 / _scene.Width, 200 / _scene.Height) * Math.Max(1, _clientW / 1280));
        int mw = _scene.Width * m, mh = _scene.Height * m, mx = _clientW - mw - 14, my = _hudIconBottom + 3;
        FillRect(mx - 3, my - 3, mw + 6, mh + 6, 0xC0101020);
        int fb = Math.Max(14, fsM * 3 / 4);
        for (int y = 0; y < _scene.Height; y++)
            for (int x = 0; x < _scene.Width; x++) FillRect(mx + x * m, my + y * m, m, m, _mini![y * _scene.Width + x]);
        foreach (var u in _scene.Units.Where(u => u.Alive && u.Hp > 0))
        {
            uint c = u.Ai ? 0xFFF0D040 : u.Side == Side.Player ? 0xFF40F070 : 0xFFFF4040;
            FillRect(mx + u.X * m, my + u.Y * m, m, m, 0xFF000000);
            FillRect(mx + u.X * m + 1, my + u.Y * m + 1, m - 2, m - 2, c);
        }
        int vx = mx + _camX * m / T, vy = my + _camY * m / T;
        int vw = Math.Min(mw, _boardW * m / T), vh = Math.Min(mh, _boardH * m / T);
        FillRect(vx, vy, vw, 1, 0xFFFFFFFF); FillRect(vx, vy + vh - 1, vw, 1, 0xFFFFFFFF);
        FillRect(vx, vy, 1, vh, 0xFFFFFFFF); FillRect(vx + vw - 1, vy, 1, vh, 0xFFFFFFFF);
        _miniFold = FoldButton(mx + mw - fb, my, fb, true);   // 오른쪽 위 [−] — 누르면 접는다
    }

    private Scene? _miniFor;
    private uint[]? _mini;

    /// <summary>
    /// 왼쪽 아래 칸 정보 창(원작) — 커서 칸 그림(사람이 있으면 그 사람, 아래에 HP 세 자리)과 그 칸 지형의 공격 · 방어 보정(A ±% · D ±%).
    /// 창 높이(위 여백 포함)를 돌려준다 — 사람 정보 창은 그 위에.
    /// </summary>
    private int DrawTileInfo(int fs)
    {
        if (!_scene.Inside(_cursor.X, _cursor.Y) || _bs is BattleState.CutIn or BattleState.Talk) return 0;
        int g = Math.Max(2, HudZoom), pad = 4 * g, img = T * g;
        var (ta, td) = _battle.TerrainPercent(_cursor.X, _cursor.Y);
        var la = Text.Render($"A {(ta < 0 ? "-" : "+")}{Math.Abs(ta):00}", fs * 3 / 2, 0xFFD8E4FF);
        var ld = Text.Render($"D {(td < 0 ? "-" : "+")}{Math.Abs(td):00}", fs * 3 / 2, 0xFFD8E4FF);
        int w = pad * 3 + img + Math.Max(la.W, ld.W) + pad, h = pad * 2 + img;
        int x = 12, y = _clientH - h - 12;
        // 테두리만 칠하고 파란 바탕은 살짝 비치게(사용자 요청)
        FillRect(x, y, w, g, 0xFFA8B8E8); FillRect(x, y + h - g, w, g, 0xFFA8B8E8);
        FillRect(x, y + g, g, h - 2 * g, 0xFFA8B8E8); FillRect(x + w - g, y + g, g, h - 2 * g, 0xFFA8B8E8);
        FillRect(x + g, y + g, w - 2 * g, h - 2 * g, 0xB02C4C9C);
        int ix = x + pad, iy = y + pad;
        BlitScaled(_scene.TileAt(_cursor.X, _cursor.Y), T, T, ix, iy, g, 0, 0, _fbW, _fbH);
        if (_scene.UnitAt(_cursor.X, _cursor.Y) is { } u)
        {
            BlitScaled(_icons.Frame(u.Icon, (int)Dir.Down * 3), T, T, ix, iy, g, 0, 0, _fbW, _fbH);
            var hp = Text.Render($"{Math.Clamp(u.Hp, 0, 999):000}", fs, 0xFFFFFFFF);
            FillRect(ix + (img - hp.W) / 2 - 2, iy + img - hp.H, hp.W + 4, hp.H, 0x80000000);
            DrawText(hp, ix + (img - hp.W) / 2, iy + img - hp.H);
        }
        int tx = ix + img + pad * 2;
        DrawText(la, tx, y + h / 2 - la.H - g);
        DrawText(ld, tx, y + h / 2 + g);
        return h + 8;
    }

    /// <summary>원작처럼 사람 둘레 네모 단추로 보이는 메뉴인가 — 고른 뒤 · 걸은 뒤의 행동 메뉴.</summary>
    private bool CrossMenu => _stage == MenuStage.AfterMove;

    /// <summary>
    /// 십자 메뉴의 자리 — 원작(sub_18D8C · sub_179D5): 위 공격, 왼쪽 마법, 오른쪽 물건, 아래 쉬기.
    /// </summary>
    private static Dir SlotOf(string label) => label switch
    {
        "공격" => Dir.Up, "마법" => Dir.Left, "물건" => Dir.Right, _ => Dir.Down,
    };

    private Buttons? _buttons;
    private bool _buttonsTried;

    /// <summary>
    /// 십자 메뉴 — 원작 단추 그림(FDOTHER 2)을 사람 둘레에: 위는 사람 칸 바로 위 20점, 왼쪽 · 오른쪽은 2점 아래, 아래는 사람 칸 바로 밑(sub_179D5).
    /// 못 쓰는 단추는 빨강으로 두고(원작처럼 고를 수 없다), 고른 단추는 4틱(0.22초)마다 반짝인다. 그림이 없으면 글자 단추.
    /// </summary>
    private void DrawCrossMenu(int fs)
    {
        if (_actor is not { } u) return;
        if (!_buttonsTried)
        {
            _buttonsTried = true;
            try { _buttons = new Buttons(_dir, _pal); }
            catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { _buttons = null; }
        }
        var (sx, sy) = TileScreen(u.X, u.Y);
        if (_buttons is not { Count: > 3 * Buttons.Rest + 2 } bank) { DrawTextCross(sx, sy); return; }

        int s = _zoom, blink = (int)(_time * 18.2 / 4) & 1;
        var slots = new[] { (Dir.Up, Buttons.Attack, 0, -20), (Dir.Left, Buttons.Magic, -24, 2), (Dir.Right, Buttons.Item, 24, 2), (Dir.Down, Buttons.Rest, 0, 24) };
        foreach (var (dir, button, ox, oy) in slots)
        {
            int i = _menu.FindIndex(m => SlotOf(m.Label) == dir);
            int x = sx + ox * s, y = sy + oy * s;
            if (i < 0)
            {
                var (rp, rw, rh) = bank.Frame(button, red: true);
                BlitScaled(rp, rw, rh, x, y, s, 0, 0, _fbW, _fbH);
                continue;
            }
            var (px, w, h) = bank.Frame(button, red: false, i == _menuIndex ? blink : 0);
            var rect = (X: x, Y: y, W: w * s, H: h * s);
            if (_mouseMoved && _mouseX >= rect.X && _mouseY >= rect.Y && _mouseX < rect.X + rect.W && _mouseY < rect.Y + rect.H) _menuIndex = i;
            BlitScaled(px, w, h, x, y, s, 0, 0, _fbW, _fbH);
            _menu[i] = (_menu[i].Label, _menu[i].Do, rect);
        }
    }

    /// <summary>그림이 없을 때의 십자 메뉴 — 노란 테두리 · 빨강 바탕(고른 단추는 파랑) 글자 단추.</summary>
    private void DrawTextCross(int sx, int sy)
    {
        int cell = T * _zoom, border = Math.Max(2, _zoom);
        for (int i = 0; i < _menu.Count; i++)
        {
            var (dx, dy) = SlotOf(_menu[i].Label) switch { Dir.Up => (0, -1), Dir.Left => (-1, 0), Dir.Right => (1, 0), _ => (0, 1) };
            var rect = (X: sx + dx * cell, Y: sy + dy * cell, W: cell, H: cell);
            if (_mouseMoved && _mouseX >= rect.X && _mouseY >= rect.Y && _mouseX < rect.X + cell && _mouseY < rect.Y + cell) _menuIndex = i;
            bool now = i == _menuIndex;
            FillRect(rect.X, rect.Y, cell, cell, 0xFFF0B020);                                   // 노란 테두리
            FillRect(rect.X + border, rect.Y + border, cell - 2 * border, cell - 2 * border, now ? 0xFF28458Au : 0xFFA01818u);
            var label = Text.Render(_menu[i].Label, Math.Max(12, cell * 3 / 10), now ? 0xFFFFFFFF : 0xFFFFE070);
            DrawText(label, rect.X + (cell - label.W) / 2, rect.Y + (cell - label.H) / 2);
            _menu[i] = (_menu[i].Label, _menu[i].Do, rect);
        }
    }

    private void DrawMenu(int fs)
    {
        if (CrossMenu) { DrawCrossMenu(fs); return; }
        var (sx, sy) = _actor is { } u ? TileScreen(u.X, u.Y) : TileScreen(_cursor.X, _cursor.Y);
        int cell = T * _zoom;
        var imgs = _menu.Select((m, i) => WithGlyphs(Text.Render(m.Label, fs, 0xFFFFFFFF), i, fs)).ToList();
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

    /// <summary>메뉴 줄 i 에 원본 글꼴 이름이 붙어 있으면 글 뒤에 이어 붙인다.</summary>
    private Text.Image WithGlyphs(Text.Image text, int i, int fs)
    {
        if (_stage is not (MenuStage.Ask or MenuStage.Deploy or MenuStage.Town or MenuStage.Item) || !_menuGlyphs.TryGetValue(i, out int line) || NameImage(line, Math.Max(1, fs / Font16.Size), 0xFFFFE070) is not { } g) return text;
        int w = text.W + g.W, h = Math.Max(text.H, g.H);
        var px = new uint[w * h];
        for (int r = 0; r < text.H; r++) Array.Copy(text.Px, r * text.W, px, (r + (h - text.H) / 2) * w, text.W);
        for (int r = 0; r < g.H; r++) Array.Copy(g.Px, r * g.W, px, (r + (h - g.H) / 2) * w + text.W, g.W);
        return new Text.Image(px, w, h);
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

        var (a, d) = (_strike.A, _strike.D);
        bool magic = _strike.Magic;
        string? spellName = _strike.Spell >= 0 ? SpellNames.Of(_strike.Spell) : _strike.Heal > 0 ? "물건" : null;
        // 배경 — 공격하는 쪽이 선 칸의 지형으로 BG.DAT 를 골라 50줄부터(원본처럼), 위아래는 BG 0번 줄무늬
        if (Back(0) is { } frame)
        {
            BlitFrame(frame, Motion.ScreenWidth, 50, bx, by, s, false, 0, 0, clip);
            BlitFrame(frame[(50 * Motion.ScreenWidth)..], Motion.ScreenWidth, 50, bx, by + 150 * s, s, false, 0, 0, clip);
        }
        if (Back(_scene.Terrain(a.X, a.Y)) is { } back)
            BlitFrame(back, Motion.ScreenWidth, back.Length / Motion.ScreenWidth, bx, by + 50 * s, s, false, 0, 0, clip);
        bool heal = _strikeFriendly;
        bool hit = _strikeDamage >= 0;
        bool ranged = magic || Dist(a, d) >= 2;
        bool showAttacker = !ranged || !hit || a == d;
        bool showDefender = (!ranged || hit) && a != d || a == d && hit;

        int defenderSide = 0;   // 맞는 쪽이 선 쪽(오른쪽 1) — 피해 숫자를 그 위에
        // 맞는 컷의 번쩍임 — 원작(sub_2FF01)은 바탕 색만 6틱 바꾼다. 사람 위에 덮거나 깜빡이면 아군 · 적이 깜빡여 보인다
        if (_sfxFlash > 0) FillRect(bx, by, bw, bh, SpellFlashColor());
        DrawSpellFx(bx, by, s, clip, back: true);
        if (showDefender)
        {
            int shake = hit && !heal && (_strikeTail < 0.4 || SpellFxRunning) ? (int)(Math.Sin(_strikeTail * 70) * 3 * s) : 0;
            (uint tint, int amount) = hit && _strikeTail < 0.18 ? (heal ? (0xFF60FF80u, 150) : (0xFFFFFFFFu, 170)) : SpellFxTint();
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
        }

        DrawSpellFx(bx, by, s, clip, back: false);

        string what = spellName != null ? $"  [{spellName}]" : "";
        // 위 글 — 「몬스터 0 → 몬스터 28」 대신 원작 이름(원본 글꼴)
        int ng = Math.Max(1, fs / Font16.Size);
        var whoA = NameImage(Battle.NameLine(a), ng, 0xFFFFFFFF) ?? Text.Render(CharacterNames.Of(a.Icon), fs, 0xFFFFFFFF);
        var arrow = Text.Render(a == d ? (what.Length > 0 ? what : " ") : "  →  ", fs, 0xFFFFFFFF);
        var whoD = a == d ? null : NameImage(Battle.NameLine(d), ng, 0xFFFFFFFF) ?? Text.Render(CharacterNames.Of(d.Icon), fs, 0xFFFFFFFF);
        var tail = a == d || what.Length == 0 ? null : Text.Render(what, fs, 0xFFFFFFFF);
        int whoW = whoA.W + arrow.W + (whoD?.W ?? 0) + (tail?.W ?? 0), whoH = Math.Max(whoA.H, arrow.H);
        int wx = _clientW / 2 - whoW / 2, wy = by - whoH - 8;
        foreach (var part in new[] { whoA, arrow, whoD, tail })
        {
            if (part == null) continue;
            DrawText(part, wx, wy + (whoH - part.H) / 2);
            wx += part.W;
        }

        // 두 사람 정보 창(원작) — 오른쪽에 선 사람은 위 오른쪽, 왼쪽 사람은 아래 왼쪽
        int aSide = _strikeMotion != null ? SideOf(_strikeMotion) : a.Side == Side.Player ? 1 : 0;
        DrawStrikePanel(a, aSide, bx, by, s);
        if (a != d) DrawStrikePanel(d, 1 - aSide, bx, by, s);
        if (hit)
        {
            var dmg = heal ? Text.Render($"+{_strikeDamage}", fs * 3, 0xFF60FF80)
                    : _strikeMiss ? Text.Render("빗나감", fs * 3, 0xFFC0C0C0)
                    : Text.Render((_strikeCrit ? "필살! " : "") + (d.Hp == 0 ? $"-{_strikeDamage}  쓰러짐!" : $"-{_strikeDamage}"), fs * 3, 0xFFFF5050);
            DrawText(dmg, bx + bw * (defenderSide == 1 ? 3 : 1) / 4 - dmg.W / 2, by + bh / 6);
        }
    }

    /// <summary>
    /// 컷인 정보 창 — 파란 바탕에 이름(원본 글꼴) · LV, HP(노랑) · MP(초록) 막대와 숫자, 줄어든 몫은 빨강.
    /// side 1 이면 위 오른쪽(320x200 화면의 172~316, 4~46), 0 이면 아래 왼쪽(4~148, 154~196).
    /// </summary>
    private void DrawStrikePanel(Unit u, int side, int bx, int by, int s)
    {
        const int W = 144, H = 42;
        int x = bx + (side == 1 ? 172 : 4) * s, y = by + (side == 1 ? 4 : 154) * s;
        FillRect(x, y, W * s, H * s, 0xFFA8B8E8);
        FillRect(x + s, y + s, (W - 2) * s, (H - 2) * s, 0xFF2C4C9C);
        int fs = 9 * s;
        var name = NameImage(Battle.NameLine(u), s, 0xFFFFFFFF) ?? Text.Render(CharacterNames.Of(u.Icon), 14 * s, 0xFFFFFFFF);
        DrawText(name, x + 6 * s, y + 3 * s);
        var lv = Text.Render($"LV·{u.Level}", fs, 0xFFFFFFFF);
        DrawText(lv, x + (W - 6) * s - lv.W, y + 3 * s);
        void Bar(string label, int row, int now, int max, uint color)
        {
            int ry = y + row * s;
            var l = Text.Render(label, fs, 0xFFFFFFFF);
            DrawText(l, x + 6 * s, ry + (8 * s - l.H) / 2);
            var n = Text.Render($"{now}", fs, 0xFFFFFFFF);
            DrawText(n, x + (W - 6) * s - n.W, ry + (8 * s - n.H) / 2);
            int barX = x + 24 * s, barW = (W - 30) * s - Math.Max(n.W, 22 * s), barH = 6 * s;
            FillRect(barX, ry + s, barW, barH, 0xFF3A4670);   // 막대 바탕 — 검정은 너무 짙어 창 바탕보다 조금 어두운 남색으로
            if (max <= 0) return;
            FillRect(barX + s, ry + 2 * s, barW - 2 * s, barH - 2 * s, 0xFFC02020);
            FillRect(barX + s, ry + 2 * s, (barW - 2 * s) * Math.Clamp(now, 0, max) / max, barH - 2 * s, color);
        }
        Bar("HP", 21, u.Hp, u.MaxHp, 0xFFF0C030);
        Bar("MP", 31, u.Mp, u.MaxMp, 0xFF58A030);
    }

    private readonly Dictionary<(int, int, uint), Text.Image?> _nameImages = new();

    /// <summary>이름 표(FDTXT 0) 줄을 원본 글꼴로 — 한 점을 g 배로, 오른쪽 아래에 그림자 한 점. 빈 줄이면 null.</summary>
    private Text.Image? NameImage(int line, int g, uint color)
    {
        if (_nameImages.TryGetValue((line, g, color), out var img)) return img;
        try
        {
            _names ??= new NameTable(_dir);
            _font ??= new Font16(_dir);
        }
        catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
        var font = _font;
        var codes = _names.Line(line).Where(font.Has).ToArray();
        if (codes.Length == 0) return _nameImages[(line, g, color)] = null;
        int s = Font16.Size * g, w = s * codes.Length + g, h = s + g;
        var px = new uint[w * h];
        for (int pass = 0; pass < 2; pass++)   // 그림자 먼저, 그 위에 글자
            for (int i = 0; i < codes.Length; i++)
                for (int y = 0; y < Font16.Size; y++)
                    for (int x = 0; x < Font16.Size; x++)
                    {
                        if (!font.Pixel(codes[i], x, y)) continue;
                        int off = pass == 0 ? g : 0;
                        uint c = pass == 0 ? 0xFF000000 : color | 0xFF000000;
                        for (int yy = 0; yy < g; yy++)
                            for (int xx = 0; xx < g; xx++) px[(y * g + yy + off) * w + i * s + x * g + xx + off] = c;
                    }
        if (_nameImages.Count > 300) _nameImages.Clear();
        return _nameImages[(line, g, color)] = new Text.Image(px, w, h);
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
