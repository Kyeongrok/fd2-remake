using System.Text.RegularExpressions;

namespace Fd2.Data;

/// <summary>
/// 장면 대본(SceneEvents.json) raw 낱말의 셈과 꼴 — 게임(GameWindow)과 점검(Fd2.Check)이 같은 것을 쓴다.
/// 셈: 정수 · turn · arg0 · nunits · flag(k) · + − × · &gt;&gt; &lt;&lt; · 괄호 · 앞 붙은 −(왼쪽부터 차례로 — 대본의 식은 turn*2-27 · flag(16)*-1+24 꼴뿐).
/// </summary>
public static class ScriptExpr
{
    /// <summary>셈의 값 — 모르는 글이면 null.</summary>
    public static int? Eval(string text, int turn, int arg0, int units, Func<int, int> flag)
    {
        int i = 0;
        string s = text.Replace(" ", "");
        int? Expr()
        {
            var a = Term();
            while (a != null && i < s.Length)
            {
                if (s[i] == '+') { i++; var b = Term(); a = b == null ? null : a + b; }
                else if (s[i] == '*') { i++; var b = Term(); a = b == null ? null : a * b; }
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
            if (s[i] == '-') { i++; var t = Term(); return t == null ? null : -t; }   // flag(16)*-1+24 의 −1(30장 사건 82)
            if (s[i] == '(') { i++; var v = Expr(); if (i < s.Length && s[i] == ')') i++; return v; }
            int st = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i > st) return int.Parse(s[st..i]);
            if (s.AsSpan(i).StartsWith("turn")) { i += 4; return turn; }
            if (s.AsSpan(i).StartsWith("arg0")) { i += 4; return arg0; }
            if (s.AsSpan(i).StartsWith("nunits")) { i += 6; return units; }   // 유닛 수(n6_0)
            if (s.AsSpan(i).StartsWith("flag(")) { i += 5; var k = Expr(); if (i < s.Length && s[i] == ')') i++; return k == null ? null : flag(k.Value); }
            return null;
        }
        var r = Expr();
        return i == s.Length ? r : null;
    }

    /// <summary>
    /// warp(유닛, x, y) — 원작 sub_33F78: 화면을 그 칸으로(sub_12CEA) 옮기고 sub_22253 이 전송 그림과 함께 유닛 +0 · +1 을 바로 고쳐 쓴다(빈칸인지 보지 않는다).
    /// 세 자리 모두 셈일 수 있다(30장 사건 82 의 warp(flag(16)*-1+24,22,18)). 꼴이 아니거나 셈을 못 하면 null.
    /// </summary>
    public static (int Unit, int X, int Y)? Warp(string raw, Func<string, int?> eval)
    {
        var m = Regex.Match(raw, @"^warp\(([^,]+),([^,]+),([^,]+)\)$");
        if (!m.Success) return null;
        return eval(m.Groups[1].Value) is { } u && eval(m.Groups[2].Value) is { } x && eval(m.Groups[3].Value) is { } y ? (u, x, y) : null;
    }

    /// <summary>ai(units a..b, m) — 원작 sub_344F2: 유닛 a ~ b 의 AI 갈래(+52 아래 4비트)를 m 으로. a · b 는 셈일 수 있다. 꼴이 아니면 null.</summary>
    public static (int From, int To, int Mode)? Ai(string raw, Func<string, int?> eval)
    {
        var m = Regex.Match(raw, @"^ai\(units (.+?)\.\.(.+), *(\d+)\)$");
        if (!m.Success) return null;
        return eval(m.Groups[1].Value) is { } a && eval(m.Groups[2].Value) is { } b ? (a, b, int.Parse(m.Groups[3].Value)) : null;
    }
}
