using Fd2.Data;

namespace Fd2;

/// <summary>
/// 출전 편성(sub_2AF28) — 쓰러지지 않은 동료가 그 장의 우리 자리(장면 정보 [1])보다 많으면 누가 나갈지 고른다.
/// 사울(동료 표 0)은 늘 나가고, 장에 따라 꼭 나가야 하는 사람(sub_2B439)은 빼지 못하며, 원작처럼 그 사람을 동료 표 둘째 자리로 옮긴다(sub_2B843) —
/// 판정 함수가 배열 번호 1(유니 같은)을 보기 때문이다.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>이번에 나가는 사람(캐릭터 번호, 동료 표 차례) — 편성 화면이 정한다.</summary>
    private readonly HashSet<int> _deploy = new();

    /// <summary>
    /// 장면마다 꼭 나갈 사람(캐릭터 번호, sub_2AF28): 16 은 미티(18)가 동료면 미티, 17 · 19 · 26~ 유니(9), 18 → 22, 20 → 7, 21 · 22 → 19, 25 → 유니 · 26.
    /// </summary>
    private static int[] RequiredFor(int scene, Campaign c) => scene switch
    {
        16 when c.Party.Any(r => HeroRecord.Id(r) == 18) => [18],
        17 or 19 or > 25 => [9],
        18 => [22],
        20 => [7],
        21 or 22 => [19],
        25 => [9, 26],
        _ => [],
    };

    /// <summary>원작이 동료 표 둘째 자리로 옮기는 사람(sub_2B843) — 16 장의 미티 · 18 장은 옮기지 않는다.</summary>
    private static int[] MovedFor(int scene) => scene switch
    {
        17 or 19 or > 25 => [9],
        20 => [7],
        21 or 22 => [19],
        25 => [9, 26],
        _ => [],
    };

    /// <summary>다음 장면 앞 — 고를 사람이 자리보다 많으면 편성 화면, 아니면 바로 싸움.</summary>
    private void PlayOrDeploy()
    {
        if (_campaign is not { } c) return;
        foreach (int id in MovedFor(c.NextScene)) c.MoveToSecond(id);
        _screen = Screen.Game;
        LoadScene(c.NextScene, new List<byte[]>());   // 지도만(편성 화면 바탕)
        int slots = _scene.Info.Length > 1 ? _scene.Info[1] : 0;
        var ready = c.Ready.ToList();
        _deploy.Clear();
        if (ready.Count <= slots || slots <= 0)
        {
            StartBattle(c.NextScene);
            return;
        }
        // 처음 고름 — 사울, 꼭 나갈 사람, 그다음 동료 표 차례로 자리만큼
        var required = RequiredFor(c.NextScene, c);
        foreach (var r in ready.Where(r => HeroRecord.Id(r) == 0 || required.Contains(HeroRecord.Id(r)))) _deploy.Add(HeroRecord.Id(r));
        foreach (var r in ready)
            if (_deploy.Count < slots) _deploy.Add(HeroRecord.Id(r));
        // 원작(sub_2670E 출구)은 동료가 16명(28장부터 20명)을 넘을 때만 편성 화면(sub_2AF28)을 띄운다 — 그 밖은 위 차례로 저절로 출전(사용자 지적)
        if (ready.Count <= (c.NextScene > 26 ? 20 : 16))
        {
            StartBattle(c.NextScene);
            return;
        }
        OpenDeployMenu(0);
    }

    private void OpenDeployMenu(int row)
    {
        if (_campaign is not { } c) return;
        int slots = _scene.Info[1];
        var required = RequiredFor(c.NextScene, c);
        _stage = MenuStage.Deploy;
        _menu.Clear();
        _menuGlyphs.Clear(); _menuIcons.Clear();
        _menu.Add(($"출전 — {_deploy.Count}/{slots}명으로 싸움 시작", () =>
        {
            _menuGlyphs.Clear(); _menuIcons.Clear();
            StartBattle(c.NextScene);
        }, default));
        foreach (var r in c.Ready)
        {
            int id = HeroRecord.Id(r), line = _menu.Count;
            bool locked = id == 0 || required.Contains(id), on = _deploy.Contains(id);
            _menuGlyphs[line] = id + 1;
            _menu.Add(($"{(on ? "■" : "□")} Lv{r[33],-3} {(locked ? "(꼭) " : "")}", () =>
            {
                if (locked) { PlaySfx(Sound.UiBank, 7); return; }
                if (_deploy.Contains(id)) _deploy.Remove(id);
                else if (_deploy.Count < slots) _deploy.Add(id);
                else { PlaySfx(Sound.UiBank, 7); return; }
                PlaySfx(Sound.UiBank, 5);
                OpenDeployMenu(line);
            }, default));
        }
        _menuIndex = Math.Clamp(row, 0, _menu.Count - 1);
        _bs = BattleState.Menu;
    }

    /// <summary>싸움에 세울 동료 — 편성 화면이 고른 사람만(고르지 않았으면 동료 표 전부, 앞에서부터 자리만큼).</summary>
    private IReadOnlyList<byte[]>? BattleParty =>
        _campaign is not { } c ? null : _deploy.Count == 0 ? c.Party : c.Party.Where(r => _deploy.Contains(HeroRecord.Id(r))).ToList();
}
