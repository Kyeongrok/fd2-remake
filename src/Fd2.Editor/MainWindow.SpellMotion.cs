using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Fd2.Data;

namespace Fd2.Editor;

/// <summary>
/// 마법 모션 탭 — 마법마다 원작이 쓰는 그림 두 가지를 한 컷씩 · 재생으로.
/// 지도 위 그림: FDOTHER 6(LMI1)의 시작 장 · 장 수(EXE 0x51F33 · 0x51F54, sub_1C4CC — 마법 0~32).
/// 전투 화면 효과: 마법 0~8 은 FDOTHER 18~44 의 FIGANI 꼴 입자(우리 편이 쓸 때 · 적이 쓸 때 따로, sub_2FF01 표 0x524C6), 주술(9)은 적만 44.
/// 기술(24 · 28~30)은 쓰는 사람의 FIGANI 3c+2 몸짓(sub_2CF30), 소환(32~35)은 FDOTHER 65~68(sub_2D80D, 소리 91~94).
/// </summary>
public partial class MainWindow
{
    /// <summary>전투 화면 효과 그림(우리 편 · 적) — 게임 GameWindow.SpellFx 의 SpellFxSpecs 와 같은 번호.</summary>
    private static readonly (int Ours, int Theirs)[] BattleFx = [(18, 20), (19, 21), (26, 27), (39, 43), (22, 23), (24, 25), (32, 33), (37, 38), (28, 30)];

    private EffectFrames? _mapFx;
    private readonly DispatcherTimer _spellTimer = new() { Interval = TimeSpan.FromMilliseconds(83) };
    private List<ImageSource> _spellMapFrames = [], _spellBattleFrames = [];
    private int _spellTick;

    private void FillSpellMotions()
    {
        _mapFx = null;
        try { _mapFx = new EffectFrames(_dir, 6, _pal); }
        catch (Exception e) when (e is System.IO.IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { }
        SpellMotionList.ItemsSource = Enumerable.Range(0, GameTables.SpellCount).Select(i => $"{i} {SpellNames.Of(i)}").ToList();
        SpellMotionList.SelectedIndex = 0;
        if (!_spellTimerHooked)
        {
            _spellTimerHooked = true;
            _spellTimer.Tick += (_, _) => SpellAnimate();
        }
        _spellTimer.Start();
    }

    private bool _spellTimerHooked;

    private void OnSpellMotionSelected(object sender, SelectionChangedEventArgs e) => LoadSpellMotion();

    private void OnSpellSideChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) LoadSpellMotion();
    }

    private void LoadSpellMotion()
    {
        int spell = SpellMotionList.SelectedIndex;
        if (spell < 0 || _dir == "") return;
        var info = new List<string>();

        // 지도 위 그림
        _spellMapFrames = [];
        var tables = _exe != null ? GameTables.Load(_dir) : null;
        if (tables != null && _mapFx is { Count: > 0 } fx && spell < 33 && tables.MapSpellFx(spell) is var (start, count, sound) && count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                var (px, w, h) = fx[start + i];
                _spellMapFrames.Add(ToBitmap(px, w, h));
            }
            info.Add($"지도 위 그림: FDOTHER 6 의 {start}~{start + count - 1}장 ({count}장){(sound > 0 ? $", 첫 장 소리 FDOTHER 80 의 {sound}번" : "")}");
        }
        else info.Add("지도 위 그림: 없음(이 마법은 전투 화면에서만 보입니다)");

        // 전투 화면 효과
        _spellBattleFrames = [];
        bool enemy = SpellEnemy.IsChecked == true;
        string side = enemy ? "적이 쓸 때" : "우리 편이 쓸 때";
        if (spell < BattleFx.Length || spell == 9)
        {
            int entry = spell < BattleFx.Length ? (enemy ? BattleFx[spell].Theirs : BattleFx[spell].Ours) : enemy ? 44 : -1;
            if (entry < 0) info.Add("전투 화면 효과: 우리 편이 쓰면 지도 위에서만 보입니다(적이 쓸 때는 FDOTHER 44)");
            else if (Motion.LoadOther(_dir, entry, _pal) is { } m)
            {
                AddBattleFrames(m);
                info.Add($"전투 화면 효과: FDOTHER {entry} ({side}) {m.Frames.Count}컷 — 게임은 이 입자를 맞는 쪽 둘레 여러 자리에 겹쳐 뿌립니다");
            }
            else info.Add($"전투 화면 효과: FDOTHER {entry} 을(를) 읽지 못함");
        }
        else if (SkillCasters.TryGetValue(spell, out var casters))
        {
            // 기술은 따로 효과 그림이 없고 쓰는 사람의 FIGANI 3c+2 몸짓이 곧 효과다
            var found = casters.Select(c => (c, m: SafeMotion(c, 2))).FirstOrDefault(x => x.m != null);
            if (found.m is { } m)
            {
                AddBattleFrames(m);
                info.Add($"전투 화면 효과: 기술 — 쓰는 사람의 몸짓 FIGANI {3 * found.c + 2} ({CharacterNames.Of(found.c)}) {m.Frames.Count}컷. 기술은 쓰는 사람 몸짓이 곧 효과입니다");
            }
            else info.Add("전투 화면 효과: 기술 — 쓰는 사람의 몸짓(FIGANI 3c+2)을 찾지 못함");
        }
        else if (spell is >= 32 and <= 35)
        {
            int entry = spell + 33;
            if (Motion.LoadOther(_dir, entry, _pal) is { } m)
            {
                AddBattleFrames(m);
                info.Add($"전투 화면 효과: 소환 — FDOTHER {entry} {m.Frames.Count}컷(소리 FDOTHER {spell + 59}). 0컷이 오른쪽에서 밀려 들어온 뒤 나머지를 2틱씩 틉니다");
            }
            else info.Add($"전투 화면 효과: FDOTHER {entry} 을(를) 읽지 못함");
        }
        else info.Add("전투 화면 효과: 없음(이 마법은 지도 위에서만 보입니다)");
        if (tables != null && spell < tables.Spells.Count)
        {
            var s = tables.Spells[spell];
            info.Add($"수치: 세기 {s.Power} · 명중 {s.Hit}% · 사거리 {s.Range} · 범위 {s.Area} · MP {s.Mp} · 대상 편 {s.Side}");
        }
        SpellInfo.Text = string.Join("\n", info);
        SpellMapStrip.ItemsSource = _spellMapFrames;
        SpellBattleStrip.ItemsSource = _spellBattleFrames;
        _spellTick = 0;
        SpellAnimate();
    }

    /// <summary>기술 → 그 기술을 가진 사람 꼴(FIGANI 캐릭터 번호, 분석-마법 배우는 표). 앞 사람부터 몸짓이 있는 것을 보인다.</summary>
    private static readonly Dictionary<int, int[]> SkillCasters = new()
    {
        [24] = [32, 50],   // 파룡술 — 사울(검성 · 영웅)
        [28] = [19],       // 참혹술 — 로테만
        [29] = [34],       // 치열도 — 티노(검성)
        [30] = [18, 67],   // 암속인 — 미티 · 미아스튜터(닌자)
    };

    private Motion? SafeMotion(int character, int kind)
    {
        try { return Motion.Load(_dir, character, kind, _pal); }
        catch (Exception e) when (e is System.IO.IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>모션 컷마다 320x200 화면 자리에 놓아 보인다.</summary>
    private void AddBattleFrames(Motion m)
    {
        foreach (var f in m.Frames)
        {
            var screen = new uint[Motion.ScreenWidth * Motion.ScreenHeight];
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < f.Width; x++)
                {
                    int sx = f.X + x, sy = f.Y + y;
                    uint c = f.Pixels[y * f.Width + x];
                    if (c != 0 && sx >= 0 && sy >= 0 && sx < Motion.ScreenWidth && sy < Motion.ScreenHeight) screen[sy * Motion.ScreenWidth + sx] = c;
                }
            _spellBattleFrames.Add(ToBitmap(screen, Motion.ScreenWidth, Motion.ScreenHeight));
        }
    }

    private void SpellAnimate()
    {
        if (_spellMapFrames.Count > 0) SpellMapImage.Source = _spellMapFrames[_spellTick % _spellMapFrames.Count];
        else SpellMapImage.Source = null;
        if (_spellBattleFrames.Count > 0) SpellBattleImage.Source = _spellBattleFrames[_spellTick % _spellBattleFrames.Count];
        else SpellBattleImage.Source = null;
        _spellTick++;
    }
}
