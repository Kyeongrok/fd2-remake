using Fd2.Data;

namespace Fd2;

/// <summary>
/// 지도 위 숫자(원작 sub_1E0DB · sub_1E1DC · sub_1DF58, 사용자 요청 w-118) — 지도에서 거는 마법(올리기 · 회복 · 걸기 · 공격)의 결과를
/// 가운데 알림 한 줄씩이 아니라 <b>맞은 사람 머리 위에 숫자로 한꺼번에</b>: 사람마다 네 자리(오른쪽 맞춤), FDOTHER 5 의 숫자 그림
/// (105 + 숫자 = 오르거나 채운 값, 94 + 숫자 = 피해), 빗나가면 116 · 117 · 118 · 118(「MISS」).
/// 22 틀 동안 자리마다 한 틀씩 늦게 튀어 오르고(원작 튀기 표 0x5202C), 0.5초 뒤 사라진다. Enter · 누르기로 넘긴다.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>원작 튀기 표(VA 0x5202C, 25바이트) — 값 − 3 점만큼 칸 윗변에서 내려 그린다.</summary>
    private static readonly int[] PopBounce = [15, 15, 15, 15, 7, 3, 1, 0, 0, 1, 3, 7, 15, 15, 11, 9, 8, 8, 9, 11, 15, 15, 15, 15, 15];

    private const int PopUp = 105, PopDamage = 94;
    private static readonly int[] PopMiss = [116, 117, 118, 118];
    private const double PopFrame = 0.035, PopHold = 0.5;
    private static double PopLen => 22 * PopFrame + PopHold;

    /// <summary>띄울 숫자 — 사람과 네 자리 그림(0 = 빈 자리).</summary>
    private readonly List<(Unit U, int[] Glyphs)> _pops = new();
    private bool _popsShown;

    /// <summary>숫자를 하나 얹는다(오른쪽 맞춤 네 자리).</summary>
    private void AddPop(Unit u, int value, bool damage)
    {
        string s = Math.Clamp(value, 0, 9999).ToString();
        var g = new int[4];
        for (int n = 0; n < 4; n++)
        {
            int k = n - (4 - s.Length);
            g[n] = k >= 0 ? (damage ? PopDamage : PopUp) + (s[k] - '0') : 0;
        }
        _pops.Add((u, g));
    }

    private void AddMiss(Unit u) => _pops.Add((u, PopMiss.ToArray()));

    /// <summary>얹은 숫자를 한꺼번에 보이고 after. 숫자 그림이 없으면(자료가 다른 판) 바로 after.</summary>
    private void ShowPops(Action after)
    {
        LoadPhaseArt();
        if (_pops.Count == 0 || _phaseArt == null) { _pops.Clear(); after(); return; }
        _popsShown = true;
        ShowBanner("", () => { _popsShown = false; _pops.Clear(); after(); }, PopLen);
    }

    /// <summary>사람 위에 숫자 — 사람 층 다음에.</summary>
    private void DrawPops()
    {
        if (!_popsShown || _bs != BattleState.Banner || _phaseArt is not { } art) return;
        int frame = Math.Clamp((int)((PopLen - _bannerLeft) / PopFrame), 0, 21);
        foreach (var (u, glyphs) in _pops)
        {
            int cx = _offX + (int)Math.Round((u.DrawX * T - _camX) * _zoom), cy = _offY + (int)Math.Round((u.DrawY * T - _camY) * _zoom);
            for (int n = 0; n < 4; n++)
            {
                if (glyphs[n] == 0) continue;
                var (px, w, h) = art[glyphs[n]];
                if (w == 0) continue;
                int dx = 5 * n + (glyphs == null || glyphs[0] != PopMiss[0] || n != 1 ? 2 : 3);
                int dy = PopBounce[n % 4 + frame] - 3;
                BlitScaled(px, w, h, cx + dx * _zoom, cy + dy * _zoom, Math.Max(1, _zoom), 0, 0, _fbW, _fbH);
            }
        }
    }
}
