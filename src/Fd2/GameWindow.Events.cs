using System.Text.Json;
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
    private void RunEvent(int id, Action after)
    {
        if (Events == null) { after(); return; }
        RunOps(Events.Event(id), 0, after);
    }

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
        RunEvent(ev, after);
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
                case "spawn" or "spawnfx":
                    SpawnPhase(A(1));
                    break;
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
                case "setunit" when A(2) == 6:
                    if (BySlot(A(1)) is { } su) { su.Side = A(3) == 0 ? Side.Other : Side.Player; su.Ai = A(3) == 1; }
                    break;
                case "arm":
                    if (A(1) < _scene.EventLines.Count) _scene.EventLines[A(1)].Turn = Turn + 1;
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

    private Unit? BySlot(int slot) => _scene.Units.Find(u => u.Slot == slot && u.Alive);

    /// <summary>등장 차례 k 의 유닛 표 줄들을 세운다(sub_10B4E).</summary>
    private void SpawnPhase(int k)
    {
        foreach (var e in _scene.Roster.Where(e => e.Phase == k))
            if (_scene.Spawn(e) is { } u) _battle.Setup(u);
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
    /// 조건 글 — dead(i) · alive(i)(배열 번호), turn&gt;=n · turn==n · turn&lt;n, inparty(c), flag(k), 앞에 ! 면 뒤집기. 모르는 글은 거짓.
    /// </summary>
    private bool Condition(string text)
    {
        text = text.Replace(" ", "");
        if (text.StartsWith('!')) return !Condition(text[1..]);
        int Arg()
        {
            int a = text.IndexOf('('), b = text.IndexOf(')');
            return a >= 0 && b > a && int.TryParse(text[(a + 1)..b], out int v) ? v : -1;
        }
        if (text.StartsWith("dead(")) return BySlot(Arg()) == null;
        if (text.StartsWith("alive(")) return BySlot(Arg()) != null;
        if (text.StartsWith("inparty(")) return _campaign?.Party.Any(r => HeroRecord.Id(r) == Arg()) == true
                                                || _scene.Units.Any(u => u.NameId == Arg() && u.Side == Side.Player && !u.Ai);
        if (text.StartsWith("flag(")) return _battle.OpenedChests.Contains(Arg());
        foreach (var (op, f) in new (string, Func<int, int, bool>)[] { (">=", (a, b) => a >= b), ("==", (a, b) => a == b), ("<=", (a, b) => a <= b), (">", (a, b) => a > b), ("<", (a, b) => a < b) })
            if (text.StartsWith("turn" + op) && int.TryParse(text[(4 + op.Length)..], out int n)) return f(Turn, n);
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
