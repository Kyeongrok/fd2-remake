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
    /// 땅 칸 사건(sub_13A44) — 칸의 표시 k 둘째 바이트는 편이 아니라 언제 보는가다: 0 = 지나간 모든 칸(sub_1300D 들이 한 칸마다), 1 = 멈춘 칸.
    /// 누가 걸었는지는 보지 않고(사건이 arg0 으로 가른다), 마지막으로 걸린 사건 하나를 이동이 끝난 뒤 건다(0x1A8F). 사건은 멈출 때마다 다시 걸린다 — 한 번만 도는 것은 사건의 깃발이 맡는다.
    /// </summary>
    private void GroundCheck(Unit u, Action after)
    {
        var walked = u.Walked.ToList();
        u.Walked.Clear();
        if (!UseEvents) { after(); return; }
        int ev = 255;
        foreach (var (x, y) in walked) ev = GroundEventAt(x, y, 0) ?? ev;
        ev = GroundEventAt(u.X, u.Y, 1) ?? ev;
        if (ev == 255) after();
        else RunEvent(ev, after, u.Slot);
    }

    /// <summary>그 칸의 땅 칸 사건 — 표시가 없거나 상자 칸이거나 보는 때(on)가 다르면 null.</summary>
    private int? GroundEventAt(int x, int y, int on)
    {
        int k = _scene.Mark(x, y);
        if (k < 1 || k > _scene.GroundEvents.Count || (_scene.TileFlags(x, y) & 0x60) != 0) return null;
        var (ev, when) = _scene.GroundEvents[k - 1];
        return ev != 255 && when == on ? ev : null;
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
        _endingKind = 0;
        _endingOps = null;
        _victoryAfter = after;
        if (UseEvents && Events!.Victory(_scene.Index) is { } ops) RunOps(ops, 0, () => { _victoryAfter = null; after(); });
        else
        {
            _victoryAfter = null;
            if (_scene.Index == Campaign.FinalScene - 1) _endingKind = 1;   // 대본이 없어도 30장 뒤는 끝
            after();
        }
    }

    /// <summary>
    /// 승리 뒤 함수에서 끝으로 가는 자리 — sub_1088D(n17)(30장 뒤: 나머지는 끝 장면 지도 연출이라 남겨 두고) · sub_31529(27장 뒤 열쇠가 없을 때 · 끝 장면 지도 연출 끝).
    /// 승리 함수는 여기서 멈추고 판정 메뉴의 「끝」이 이어 튼다. 그랬으면 true.
    /// </summary>
    private bool EndingOp(string raw, JsonElement[] ops, int next)
    {
        if (raw.StartsWith("sub_1088D") && !_endingPrelude)
        {
            _endingKind = 1;
            _endingOps = ops[next..];
        }
        else if (raw == "sub_31529()")
        {
            if (_endingPrelude) { StartEndingFilm(); return true; }
            _endingKind = _scene.Index == Campaign.FinalScene - 1 ? 1 : 2;
        }
        else return false;
        var done = _victoryAfter;
        _victoryAfter = null;
        done?.Invoke();
        return true;
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
                    if (BySlot(A(1)) is { } gone) { _scene.Units.Remove(gone); _scene.Hidden.Add(gone); }
                    break;
                case "ally":
                    if (BySlot(A(1)) is { } ally) { ally.Side = Side.Player; ally.Ai = true; }
                    break;
                case "setunit" when A(2) == 5:
                    // +5 상태 비트 — 0 이면 숨긴 사람을 되세우고, 비트 0 이 켜지면 숨긴다
                    if ((A(3) & 1) == 0 && _scene.Hidden.Find(u => u.Slot == A(1)) is { } back) { _scene.Hidden.Remove(back); if (back.Hp > 0) _scene.Units.Add(back); }
                    else if ((A(3) & 1) != 0 && BySlot(A(1)) is { } hide) { _scene.Units.Remove(hide); _scene.Hidden.Add(hide); }
                    break;
                case "setunit":
                    if (AnySlot(A(1)) is { } su) SetUnitField(su, A(2), A(3));
                    break;
                case "arm":
                    if (A(1) < _scene.EventLines.Count) _scene.EventLines[A(1)].Turn = Turn + (op.GetArrayLength() > 2 ? A(2) : 1);
                    break;
                case "raw":
                {
                    string raw = op.GetArrayLength() > 1 ? op[1].GetString() ?? "" : "";
                    if (EndingOp(raw, ops, next)) return;
                    // 물건 주기 — 받았다는 알림까지(2장 주민을 다 구한 역량약수처럼, 사용자 제보)
                    var gm = Regex.Match(raw, @"^giveitem\((\d+)\)");
                    if (gm.Success)
                    {
                        ShowNotices(Reward(0, int.Parse(gm.Groups[1].Value)), Go);
                        return;
                    }
                    var cm = Regex.Match(raw, @"^(spawn|talk)\((.+)\)$");
                    if (cm.Success && Eval(cm.Groups[2].Value) is { } cv)
                    {
                        if (cm.Groups[1].Value == "spawn") { SpawnPhase(cv); break; }
                        _bs = BattleState.Talk;
                        StartTalk(_scene.Index, [cv], Go);
                        return;
                    }
                    RawOp(raw);
                    break;
                }
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
                    SetFlag(A(1), 1);
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

    /// <summary>깃발 값(dword_53AD5[k], 0~255) — 상자 열림과 같은 자리다. 0 이 아니면 <see cref="Battle.OpenedChests"/> 에도 든다.</summary>
    private readonly int[] _flags = new int[32];

    private int Flag(int k) => k is >= 0 and < 32 ? Math.Max(_flags[k], _battle.OpenedChests.Contains(k) ? 1 : 0) : 0;

    private void SetFlag(int k, int v)
    {
        if (k is < 0 or >= 32) return;
        _flags[k] = v & 0xFF;
        if (_flags[k] != 0) _battle.OpenedChests.Add(k); else _battle.OpenedChests.Remove(k);
    }

    /// <summary>대본의 셈(정수 · turn · arg0 · nunits · flag(k) · + − × · &gt;&gt; &lt;&lt; · 괄호) — 모르는 글이면 null.</summary>
    private int? Eval(string text)
    {
        int i = 0;
        string s = text.Replace(" ", "");
        int? Expr()
        {
            var a = Term();
            while (a != null && i < s.Length)
            {
                if (s[i] == '+') { i++; var b = Term(); a = b == null ? null : a + b; }
                else if (s[i] == '*') { i++; var b = Term(); a = b == null ? null : a * b; }   // 왼쪽부터(대본의 식은 turn*2-27 꼴뿐)
                else if (s[i] == '-') { i++; var b = Term(); a = b == null ? null : a - b; }
                else if (i + 1 < s.Length && s[i] == '>' && s[i + 1] == '>') { i += 2; var b = Term(); a = b == null ? null : a >> b; }
                else if (i + 1 < s.Length && s[i] == '<' && s[i + 1] == '<') { i += 2; var b = Term(); a = b == null ? null : a << b; }
                else break;
            }
            return a;
        }
        int? Term()
        {
            if (i >= s.Length) return null;
            if (s[i] == '(') { i++; var v = Expr(); if (i < s.Length && s[i] == ')') i++; return v; }
            int st = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i > st) return int.Parse(s[st..i]);
            if (s.AsSpan(i).StartsWith("turn")) { i += 4; return Turn; }
            if (s.AsSpan(i).StartsWith("arg0")) { i += 4; return _eventArg; }
            if (s.AsSpan(i).StartsWith("nunits")) { i += 6; return _scene.SlotCount; }   // 유닛 수(n6_0)
            if (s.AsSpan(i).StartsWith("flag(")) { i += 5; var k = Expr(); if (i < s.Length && s[i] == ')') i++; return k == null ? null : Flag(k.Value); }
            return null;
        }
        var r = Expr();
        return i == s.Length ? r : null;
    }

    /// <summary>대본의 raw 가운데 옮길 수 있는 것 — ai(units a..b, m), warp(u,x,y), giveitem(i), killfrom(n), flag[k]=식. 그 밖(그림 · 팔레트 연출)은 넘긴다.</summary>
    private void RawOp(string text)
    {
        var fm = Regex.Match(text, @"^flag\[(\d+)\]=(.+)$");
        if (fm.Success)
        {
            if (Eval(fm.Groups[2].Value) is { } fv) SetFlag(int.Parse(fm.Groups[1].Value), fv);
            return;
        }
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
        // 화면 · 커서 칸 쓰기 — g53aa9 · g53aad 화면 왼쪽 위 칸 x · y, g53ab1 · g53ab5 커서 칸 x · y(원작 변수)
        m = Regex.Match(text, @"^g53(aa9|aad|ab1|ab5)=(\d+)$");
        if (m.Success)
        {
            int v = int.Parse(m.Groups[2].Value);
            bool view = m.Groups[1].Value is "aa9" or "aad", isX = m.Groups[1].Value is "aa9" or "ab1";
            int x = isX ? v + (view ? 6 : 0) : _cursor.X, y = isX ? _cursor.Y : v + (view ? 4 : 0);
            _cursor = (Math.Clamp(x, 0, _scene.Width - 1), Math.Clamp(y, 0, _scene.Height - 1));
            _centerCursor = true;
            return;
        }
        // 승리 연출 자리 잡기 — place i:(x,y,쪽) …; cam(x,y)(sub_233C6, 배열 번호 i 를 그 칸 · 쪽으로)
        if (text.StartsWith("place "))
        {
            foreach (Match pm in Regex.Matches(text, @"(\d+):\((\d+),(\d+),(\d+)\)"))
                if (AnySlot(int.Parse(pm.Groups[1].Value)) is { } pu)
                {
                    int x = int.Parse(pm.Groups[2].Value), y = int.Parse(pm.Groups[3].Value);
                    if (_scene.Inside(x, y)) (pu.X, pu.Y, pu.FromX, pu.FromY) = (x, y, x, y);
                    pu.Facing = (Dir)(int.Parse(pm.Groups[4].Value) & 3);
                }
            var cam = Regex.Match(text, @"cam\((\d+),(\d+)\)");
            if (cam.Success)
            {
                _cursor = (Math.Clamp(int.Parse(cam.Groups[1].Value) + 6, 0, _scene.Width - 1), Math.Clamp(int.Parse(cam.Groups[2].Value) + 4, 0, _scene.Height - 1));
                _centerCursor = true;
            }
            return;
        }
        // 물건 빼기 — dropitem(arg0, …itemslot(arg0, i)) 는 그 사람의 물건 i 하나, 「items a..b from party」는 우리 편에서 하나씩(sub_1B8E7)
        m = Regex.Match(text, @"^dropitem\(arg0,.*itemslot\(arg0,(\d+)\)\)");
        if (m.Success)
        {
            if (BySlot(_eventArg) is { } du) DropItem(du, int.Parse(m.Groups[1].Value));
            return;
        }
        m = Regex.Match(text, @"^dropitem: remove one each of items (\d+)\.\.(\d+)");
        if (m.Success)
        {
            for (int i = int.Parse(m.Groups[1].Value); i <= int.Parse(m.Groups[2].Value); i++)
                if (_scene.Units.FirstOrDefault(u => u.Alive && u.Team == 2 && u.Bag.Any(b => b.Item == i)) is { } owner) DropItem(owner, i);
            return;
        }
        // 물건 주기 — sub_1BB8C(arg0, i): 일으킨 사람 가방에
        m = Regex.Match(text, @"^sub_1BB8C\(arg0,(\d+)\)");
        if (m.Success)
        {
            if (BySlot(_eventArg) is { Bag.Count: < 8 } gu) gu.Bag.Add((int.Parse(m.Groups[1].Value), false));
            return;
        }
        // 행동 끝 표시 — unit(i).5 |= 0x80(sub_13512)
        m = Regex.Match(text, @"^unit\((\d+)\)\.5\|=0x80");
        if (m.Success)
        {
            if (BySlot(int.Parse(m.Groups[1].Value)) is { } au) au.Acted = true;
            return;
        }
        // AI 바이트 거르기 — unit[a..b]+52 &= v
        m = Regex.Match(text, @"^unit\[(\d+)\.\.(\d+)\]\+52 *&= *(\d+)");
        if (m.Success)
        {
            int a2 = int.Parse(m.Groups[1].Value), b2 = int.Parse(m.Groups[2].Value), v2 = int.Parse(m.Groups[3].Value);
            foreach (var u in _scene.Units.Where(u => u.Slot >= a2 && u.Slot <= b2)) u.AiByte &= v2;
            return;
        }
        m = Regex.Match(text, @"^killfrom\((\d+)\)");
        if (m.Success)
        {
            // sub_35F10 — n 번부터 끝까지 HP 0(쓰러진 것으로 — 판정이 그 번호를 본다), 떨굼은 없다
            int n = int.Parse(m.Groups[1].Value);
            foreach (var u in _scene.Units.Where(u => u.Slot >= n)) { u.Hp = 0; u.DropKind = 255; }
        }
    }

    /// <summary>배열 번호 slot 의 사람 — 숨긴 사람까지.</summary>
    private Unit? AnySlot(int slot) => BySlot(slot) ?? _scene.Hidden.Find(u => u.Slot == slot);

    /// <summary>가방에서 물건 하나를 뺀다 — 장비 중이었으면 능력을 다시 셈한다.</summary>
    private void DropItem(Unit u, int item)
    {
        int k = u.Bag.FindIndex(b => b.Item == item);
        if (k < 0) return;
        u.Bag.RemoveAt(k);
        _battle.Recompute(u);
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
        if (onMap != null)
        {
            // 원작 sub_112A5 는 지도가 아니라 동료 표에서 가방을 채운다 — 장면 유닛 표에 없는 소피아의 황금훈장(동료 표 +14)이 그래서 따라온다
            if (_tables != null && c < GameTables.HeroCount)
                foreach (int item in _tables.Heroes[c].Bag.Where(b => b != 255).Select(b => (int)b))
                    if (onMap.Bag.Count < 8 && onMap.Bag.All(b => b.Item != item)) onMap.Bag.Add((item, false));
            camp.Party.Add(HeroRecord.From(onMap));
            return;
        }
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
            case 0:
                // 앞사람 가방이 차면 자리가 있는 다음 사람에게 — 조용히 사라지지 않게
                var taker = lead is { Bag.Count: < 8 } ? lead : Controlled.OrderBy(u => u.Slot).FirstOrDefault(u => u.Bag.Count < 8);
                if (taker == null) { notes.Add(new Notice(" — 가방이 가득해 받지 못함", 181 + value)); break; }
                taker.Bag.Add((value, false));
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
            case "flag": return hasCmp ? Cmp(Flag(Arg(args))) : Flag(Arg(args)) != 0;
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
                var u = AnySlot(Arg(args));
                int off = int.Parse(m.Groups[3].Value);
                int v = u == null ? (off == 5 ? 1 : 0) : off switch
                {
                    0 => u.X, 1 => u.Y, 3 => (int)u.Facing, 5 => _scene.Hidden.Contains(u) || u.Hp <= 0 ? 1 : 0, 6 => u.Team, 7 => u.FormId, 8 => u.NameId, 33 => u.Level,
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
