using System.Text.Json;
using System.Text.RegularExpressions;
using Fd2.Data;

namespace Fd2;

/// <summary>
/// 장면 사건(분석-장면 §2 · §5) — 사건 줄(차례 · 때), 땅 칸 사건, 상자 사건, 장면 시작 · 승리 뒤 함수를 <see cref="SceneEvents"/> 의 낱말 묶음으로 돌린다.
/// 낱말: spawn · spawnfx k(등장 차례 k 세우기), script m(이동 대본), camera x y, stepup i, talk g(대사 묶음), join c(동료 표에 넣기),
/// remove i · ally i · setunit i off v(배열 번호 i 사람), arm k · armset k v(사건 줄 켜기), reward 종류 값, music 곡, flag k, win · lose, if 조건 [그러면] [아니면].
/// 대본이 없는 판(표를 못 읽은 것)은 예전처럼 「등장 차례 k 무리는 k 턴에」로 돈다.
/// </summary>
internal sealed partial class GameWindow
{
    private static readonly SceneEvents? Events = SceneEvents.Load();

    /// <summary>원작 사건 대본으로 도는가 — 대본 표가 있고 그 장면이 싸움 장면(0~29)이면.</summary>
    private bool UseEvents => Events is { EventCount: > 0 } && _scene.Index < 30;

    /// <summary>사건이 정한 판정(win · lose) — 0 이면 판정 함수대로.</summary>
    private int _forcedOutcome;

    /// <summary>한 번 건 땅 칸 사건(표시 번호) — 같은 줄을 여럿이 밟아도 한 번만.</summary>
    private readonly HashSet<int> _groundFired = new();

    // ── 걸기 ────────────────────────────────────────────────────────────────

    /// <summary>사건 하나를 돌리고 after.</summary>
    private void RunEvent(int id, Action after, int arg = 0)
    {
        if (Events == null) { after(); return; }
        _eventArg = arg;
        RunOps(Events.Event(id), 0, after);
    }

    /// <summary>사건 함수의 인자(arg0) — 사건을 일으킨 사람의 배열 번호(땅 칸 · 떨굼 · 상자), 차례 사건은 0.</summary>
    private int _eventArg;

    /// <summary>
    /// 그 때(0 적 차례 앞 · 1 우리 차례 끝 · 2 우리 차례 시작)의 사건 줄들을 차례로(sub_1A813) — 차례 수가 같고 때가 같은 줄.
    /// 사건이 줄을 고쳐도(다음 줄 켜기) 이번 훑기에는 처음 본 줄들만 건다.
    /// </summary>
    private void RunTiming(int when, Action after)
    {
        if (!UseEvents) { after(); return; }
        var due = _scene.EventLines.Where(l => l.Turn == Turn && l.When == when).Select(l => l.Event).ToList();
        void Next(int i)
        {
            if (i >= due.Count) { after(); return; }
            RunEvent(due[i], () => Next(i + 1));
        }
        Next(0);
    }

    /// <summary>
    /// 땅 칸 사건(sub_13A44) — 멈춘 칸의 표시 k(1 이상)가 상자 칸이 아니고, 그 사람의 편이 사건 k 의 편이면 건다.
    /// </summary>
    private void GroundCheck(Unit u, Action after)
    {
        if (!UseEvents) { after(); return; }
        int k = _scene.Mark(u.X, u.Y);
        if (k < 1 || (_scene.TileFlags(u.X, u.Y) & 0x60) != 0 || k > _scene.GroundEvents.Count || _groundFired.Contains(k)) { after(); return; }
        var (ev, team) = _scene.GroundEvents[k - 1];
        if (u.Team != team) { after(); return; }
        _groundFired.Add(k);
        RunEvent(ev, after, u.Slot);
    }

    /// <summary>장면 시작 연출 — 대본에 시작 함수가 있으면 그것, 없으면 대사 묶음 0.</summary>
    private void RunStart(Action after)
    {
        if (UseEvents && Events!.Start(_scene.Index) is { } ops) RunOps(ops, 0, after);
        else StartTalk(_scene.Index, [0], after);
    }

    /// <summary>승리 뒤 함수(대사 · 합류) — 없으면 바로.</summary>
    private void RunVictory(Action after)
    {
        if (UseEvents && Events!.Victory(_scene.Index) is { } ops) RunOps(ops, 0, after);
        else after();
    }

    // ── 낱말 돌리기 ──────────────────────────────────────────────────────────

    private void RunOps(JsonElement[] ops, int i, Action after)
    {
        while (i < ops.Length)
        {
            var op = ops[i++];
            if (op.ValueKind != JsonValueKind.Array || op.GetArrayLength() == 0) continue;
            string name = op[0].GetString() ?? "";
            int A(int k) => op.GetArrayLength() > k && op[k].ValueKind == JsonValueKind.Number ? op[k].GetInt32() : 0;
            int next = i;
            void Go() => RunOps(ops, next, after);
            switch (name)
            {
                case "spawn":
                    SpawnPhase(A(1));
                    break;
                case "spawnfx":
                {
                    // 나타나는 그림(FDOTHER 9, 12장 — sub_32999)과 소리(FDOTHER 95 의 0번, 둘째 장에서)
                    var born = SpawnPhase(A(1));
                    if (born.Count == 0) break;
                    PlayTileFx(9, 0, 12, born.Select(u => (u.X, u.Y)), Go, n => { if (n == 1) PlaySfx(95, 0); });
                    return;
                }
                case "script":
                    if (_tables == null) break;
                    StartBattleScript(_tables.MoveScript(A(1)), Go);
                    return;
                case "camera":
                    _cursor = (Math.Clamp(A(1) + 6, 0, _scene.Width - 1), Math.Clamp(A(2) + 4, 0, _scene.Height - 1));
                    _centerCursor = true;
                    break;
                case "stepup":
                    if (BySlot(A(1)) is { } up) StartBattleScript([new MoveStep(1, false, [(A(1), Dir.Up)])], Go);
                    else break;
                    return;
                case "talk":
                    _bs = BattleState.Talk;
                    StartTalk(_scene.Index, [A(1)], Go);
                    return;
                case "join":
                    Join(A(1));
                    break;
                case "remove":
                    if (BySlot(A(1)) is { } gone) _scene.Units.Remove(gone);
                    break;
                case "ally":
                    if (BySlot(A(1)) is { } ally) { ally.Side = Side.Player; ally.Ai = true; }
                    break;
                case "setunit":
                    if (BySlot(A(1)) is { } su) SetUnitField(su, A(2), A(3));
                    break;
                case "arm":
                    if (A(1) < _scene.EventLines.Count) _scene.EventLines[A(1)].Turn = Turn + (op.GetArrayLength() > 2 ? A(2) : 1);
                    break;
                case "raw":
                    RawOp(op.GetArrayLength() > 1 ? op[1].GetString() ?? "" : "");
                    break;
                case "armset":
                    if (A(1) < _scene.EventLines.Count) _scene.EventLines[A(1)].Turn = A(2);
                    break;
                case "reward":
                    if (Reward(A(1), A(2)) is { Count: > 0 } notes) { ShowNotices(notes, Go); return; }
                    break;
                case "music":
                    SetMusic(A(1));
                    break;
                case "flag":
                    _battle.OpenedChests.Add(A(1));
                    break;
                case "win":
                    _forcedOutcome = 2;
                    break;
                case "lose":
                    _forcedOutcome = 1;
                    break;
                case "if":
                    bool yes = op.GetArrayLength() > 1 && Condition(op[1].GetString() ?? "");
                    int branch = yes ? 2 : 3;
                    if (op.GetArrayLength() > branch && op[branch].ValueKind == JsonValueKind.Array)
                    {
                        RunOps(op[branch].EnumerateArray().ToArray(), 0, Go);
                        return;
                    }
                    break;
            }
        }
        after();
    }

    /// <summary>
    /// 사람 칸 쓰기(setunit) — +0 · +1 자리, +3 보는 쪽, +6 편, +33 레벨, +52 ~ +54 AI, +61 상자 번호. +5(상태 비트)는 넘긴다.
    /// </summary>
    private void SetUnitField(Unit u, int off, int v)
    {
        switch (off)
        {
            case 0: if (_scene.Inside(v, u.Y)) (u.X, u.FromX) = (v, v); break;
            case 1: if (_scene.Inside(u.X, v)) (u.Y, u.FromY) = (v, v); break;
            case 3: u.Facing = (Dir)(v & 3); break;
            case 6: u.Side = v == 0 ? Side.Other : Side.Player; u.Ai = v == 1; break;
            case 33: u.Level = Math.Max(1, v); break;
            case 52: u.AiByte = v; break;
            case 53: u.AiX = v; break;
            case 54: u.AiY = v; break;
            case 61: u.Chest = v; break;
        }
    }

    /// <summary>대본의 raw 가운데 옮길 수 있는 것 — ai(units a..b, m), warp(u,x,y), giveitem(i), killfrom(n). 그 밖(그림 · 팔레트 연출)은 넘긴다.</summary>
    private void RawOp(string text)
    {
        var m = Regex.Match(text, @"^ai\(units (\d+)\.\.(\d+), *(\d+)\)");
        if (m.Success)
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value), v = int.Parse(m.Groups[3].Value);
            foreach (var u in _scene.Units.Where(u => u.Slot >= a && u.Slot <= b)) u.AiByte = (u.AiByte & 0xF0) | (v & 0x0F);
            return;
        }
        m = Regex.Match(text, @"^warp\((\d+), *(\d+), *(\d+)\)");
        if (m.Success)
        {
            if (BySlot(int.Parse(m.Groups[1].Value)) is { } w)
            {
                int x = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value);
                if (_scene.Inside(x, y)) (w.X, w.Y, w.FromX, w.FromY) = (x, y, x, y);
            }
            return;
        }
        m = Regex.Match(text, @"^giveitem\((\d+)\)");
        if (m.Success)
        {
            var lead = BySlot(0) ?? Controlled.FirstOrDefault();
            if (lead != null && lead.Bag.Count < 8) lead.Bag.Add((int.Parse(m.Groups[1].Value), false));
            return;
        }
        m = Regex.Match(text, @"^killfrom\((\d+)\)");
        if (m.Success)
        {
            int n = int.Parse(m.Groups[1].Value);
            _scene.Units.RemoveAll(u => u.Slot >= n);
        }
    }

    private Unit? BySlot(int slot) => _scene.Units.Find(u => u.Slot == slot && u.Alive);

    /// <summary>등장 차례 k 의 유닛 표 줄들을 세운다(sub_10B4E).</summary>
    private List<Unit> SpawnPhase(int k)
    {
        var born = new List<Unit>();
        foreach (var e in _scene.Roster.Where(e => e.Phase == k))
            if (_scene.Spawn(e) is { } u) { _battle.Setup(u); born.Add(u); }
        return born;
    }

    /// <summary>캐릭터 c 를 동료 표 끝에(sub_112A5) — 지도에 그 사람이 있으면 그 상태로, 없으면 동료 표의 합류 값으로.</summary>
    private void Join(int c)
    {
        if (_campaign is not { } camp || camp.Party.Any(r => HeroRecord.Id(r) == c) || camp.Party.Count >= Campaign.MaxParty) return;
        var onMap = _scene.Units.Find(u => u.NameId == c && u.Side == Side.Player);
        if (onMap != null) { camp.Party.Add(HeroRecord.From(onMap)); return; }
        var u = new Unit { Icon = c, Form = c, Id = c, Side = Side.Player };
        _battle.Setup(u);
        camp.Party.Add(HeroRecord.From(u));
    }

    /// <summary>사건 보상(sub_1AA1D 꼴) — 0 물건(사울 가방에), 1 돈, 3 대사 묶음.</summary>
    private List<Notice> Reward(int kind, int value)
    {
        var notes = new List<Notice>();
        var lead = BySlot(0) ?? Controlled.FirstOrDefault();
        switch (kind)
        {
            case 0 when lead != null && lead.Bag.Count < 8:
                lead.Bag.Add((value, false));
                notes.Add(new Notice(" 획득", 181 + value));
                break;
            case 1:
                _battle.Gold += value;
                notes.Add(new Notice($"돈 {value} 획득"));
                break;
            case 3:
                notes.Add(new Notice("", -1, value));
                break;
        }
        return notes;
    }

    /// <summary>
    /// 조건 글 — dead(i) · alive(i) · anyalive(a..b) · anydead(a..b) · deadcount(a..b) 비교 n(배열 번호), unit(i|arg0).칸 비교 v,
    /// flag(k)[비교 v], turn 비교 n, arg0 비교 v, inparty(c) · inroster(c), hasitem(i) · hasall(a..b)(우리 편 가방), nitems(arg0) 비교 n,
    /// itemslot(arg0,i), 앞에 ! 면 뒤집기. 모르는 글은 거짓.
    /// </summary>
    private bool Condition(string text)
    {
        text = text.Replace(" ", "");
        if (text.StartsWith('!')) return !Condition(text[1..]);
        var m = Regex.Match(text, @"^(\w+)(?:\(([^)]*)\))?(?:\.(\d+))?(?:(==|!=|<=|>=|<|>)(-?\d+))?$");
        if (!m.Success) return false;
        string name = m.Groups[1].Value, args = m.Groups[2].Value;
        bool hasCmp = m.Groups[4].Success;
        int rhs = hasCmp ? int.Parse(m.Groups[5].Value) : 0;
        bool Cmp(int v) => m.Groups[4].Value switch { "==" => v == rhs, "!=" => v != rhs, "<=" => v <= rhs, ">=" => v >= rhs, "<" => v < rhs, ">" => v > rhs, _ => v != 0 };
        int Arg(string a) => a == "arg0" ? _eventArg : int.TryParse(a, out int v) ? v : -1;
        (int A, int B) Range()
        {
            var p = args.Split("..");
            return p.Length == 2 ? (Arg(p[0]), Arg(p[1])) : (Arg(args), Arg(args));
        }
        IEnumerable<int> Slots() { var (a, b) = Range(); return Enumerable.Range(a, Math.Max(0, b - a + 1)); }
        IEnumerable<Unit> Team2() => _scene.Units.Where(u => u.Alive && u.Side == Side.Player && !u.Ai);
        bool HasItem(int i) => Team2().Any(u => u.Bag.Any(b => b.Item == i));
        switch (name)
        {
            case "dead": return BySlot(Arg(args)) == null;
            case "alive": return BySlot(Arg(args)) != null;
            case "anyalive": return Slots().Any(i => BySlot(i) != null);
            case "anydead": return Slots().Any(i => BySlot(i) == null);
            case "deadcount": return Cmp(Slots().Count(i => BySlot(i) == null));
            case "flag": return hasCmp ? Cmp(_battle.OpenedChests.Contains(Arg(args)) ? 1 : 0) : _battle.OpenedChests.Contains(Arg(args));
            case "turn": return Cmp(Turn);
            case "arg0": return Cmp(_eventArg);
            case "inparty": return _campaign?.Party.Any(r => HeroRecord.Id(r) == Arg(args)) == true || Team2().Any(u => u.NameId == Arg(args));
            case "inroster": return _scene.Units.Any(u => u.Alive && u.NameId == Arg(args));
            case "hasitem": return HasItem(Arg(args));
            case "hasall": return Slots().All(HasItem);
            case "nitems": return BySlot(Arg(args)) is { } nu && Cmp(nu.Bag.Count);
            case "itemslot":
            {
                var p = args.Split(',');
                return p.Length == 2 && BySlot(Arg(p[0])) is { } iu && iu.Bag.Any(b => b.Item == Arg(p[1]));
            }
            case "unit":
            {
                if (!m.Groups[3].Success) return false;
                var u = BySlot(Arg(args));
                int off = int.Parse(m.Groups[3].Value);
                int v = u == null ? (off == 5 ? 1 : 0) : off switch
                {
                    0 => u.X, 1 => u.Y, 3 => (int)u.Facing, 5 => 0, 6 => u.Team, 7 => u.FormId, 8 => u.NameId, 33 => u.Level,
                    52 => u.AiByte, 53 => u.AiX, 54 => u.AiY, 61 => u.Chest, 64 => u.Hp, 66 => u.MaxHp, 68 => u.Mp, 70 => u.MaxMp, _ => 0,
                };
                return Cmp(v);
            }
        }
        return false;
    }

    // ── 싸움 중 이동 대본 ────────────────────────────────────────────────────

    private List<MoveStep>? _bScript;
    private int _bStep, _bTiles;
    private double _bWait;
    private Action? _bAfter;
    private BattleState _bResume;

    /// <summary>이동 대본을 지도 위 사람들(배열 번호)로 돌린다 — 다른 사람 · 지형에 막히지 않는다(원작 대본처럼).</summary>
    private void StartBattleScript(List<MoveStep> steps, Action after)
    {
        _bResume = _bs is BattleState.Script ? _bResume : _bs;
        _bScript = steps;
        _bStep = -1;
        _bAfter = after;
        _bs = BattleState.Script;
        NextBattleStep();
    }

    private void NextBattleStep()
    {
        if (_bScript == null) return;
        if (++_bStep >= _bScript.Count)
        {
            _bScript = null;
            _bs = _bResume;
            var a = _bAfter;
            _bAfter = null;
            a?.Invoke();
            return;
        }
        var step = _bScript[_bStep];
        foreach (var (slot, dir) in step.Units)
            if (BySlot(slot) is { } u) u.Facing = dir;
        if (step.Wait) { _bTiles = 0; _bWait = step.Count / 18.0; }
        else { _bTiles = step.Count; _bWait = 0; }
    }

    private void BattleScriptTick(double dt)
    {
        if (_bScript == null) return;
        var step = _bScript[_bStep];
        if (step.Wait)
        {
            if ((_bWait -= dt) <= 0) NextBattleStep();
            return;
        }
        var walkers = step.Units.Select(p => BySlot(p.Unit)).OfType<Unit>().ToList();
        if (walkers.Any(u => u.Moving)) return;
        if (_bTiles <= 0 || walkers.Count == 0) { NextBattleStep(); return; }
        _bTiles--;
        foreach (var (slot, dir) in step.Units)
        {
            if (BySlot(slot) is not { } u) continue;
            var (dx, dy) = Delta(dir);
            (u.FromX, u.FromY) = (u.X, u.Y);
            u.X += dx;
            u.Y += dy;
            u.Facing = dir;
            u.Step = 0;
        }
        if (walkers.Count > 0) { _cursor = (walkers[0].X, walkers[0].Y); _centerCursor = true; }
    }
}
