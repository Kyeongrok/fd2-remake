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
        bool hasMap = _spellMapFrames.Count > 0;
        if (FillSpellFxChoice(spell, hasMap) is { } sheet)
        {
            // 짝지은 바깥 이펙트 — 미리 보기도 그것으로(게임과 같은 그림)
            _spellMapFrames = sheet.Frames.Select(f => (ImageSource)ToBitmap(f.Px, f.W, f.H)).ToList();
            info.Add($"지도 위 이펙트: 「{SpellFxChoice.SelectedItem}」 {sheet.Frames.Count}컷 · {sheet.TotalMs / 1000.0:0.##}초, 발 ({sheet.PivotX},{sheet.PivotY}) — 게임은 원작 그림 대신 이것을 대상 칸 위에(첫 장 소리는 원작 것)");
        }

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

    /// <summary>마법 spell 의 전투 화면 효과 항목(FDOTHER)과 기술이면 쓰는 사람들 — 내보내기 · 불러오기에 담는 것.</summary>
    private static (int[] Entries, int[] Casters) SpellParts(int spell) =>
        spell < BattleFx.Length ? ([BattleFx[spell].Ours, BattleFx[spell].Theirs], [])
        : spell == 9 ? ([44], [])
        : spell is >= 32 and <= 35 ? ([spell + 33], [])
        : SkillCasters.TryGetValue(spell, out var c) ? ([], c) : ([], []);

    private void OnExportSpellMotion(object sender, RoutedEventArgs e)
    {
        int spell = SpellMotionList.SelectedIndex;
        if (_dir == "" || spell < 0) return;
        var (entries, casters) = SpellParts(spell);
        if (entries.Length + casters.Length == 0) { Status.Text = $"{SpellNames.Of(spell)} 은(는) 전투 화면 효과가 없습니다(지도 위 그림은 여러 마법이 한 묶음을 나눠 써서 따로 내보내지 않습니다)."; return; }
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "마법 모션 내보내기", Filter = "모션 묶음 (*.zip)|*.zip", FileName = $"마법{spell:D2}_{SpellNames.Of(spell)}_모션.zip",
        };
        if (save.ShowDialog(this) != true) return;
        try
        {
            int n = MotionPack.ExportSpell(_dir, spell, entries, casters, save.FileName);
            Status.Text = $"{SpellNames.Of(spell)} 마법 모션 {n}개를 내보냈습니다 — {save.FileName}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"내보내지 못했습니다: {ex.Message}"; }
    }

    private void OnImportSpellMotion(object sender, RoutedEventArgs e)
    {
        if (_dir == "") return;
        var open = new Microsoft.Win32.OpenFileDialog { Title = "마법 모션 불러오기", Filter = "모션 묶음 (*.zip)|*.zip" };
        if (open.ShowDialog(this) != true) return;
        try
        {
            var info = MotionPack.Read(open.FileName);
            if (info is not { Type: "spell" }) { Status.Text = "마법 모션 묶음이 아닙니다(전투 모션은 「전투 모션」 탭에서 불러오세요)."; return; }
            var (version, what) = MotionPack.Import(open.FileName, _dir);
            if (info.Spell >= 0 && info.Spell < SpellMotionList.Items.Count) SpellMotionList.SelectedIndex = info.Spell;
            LoadSpellMotion();
            Status.Text = $"불러왔습니다 — {what}. 모션 버전 「{version}」 으로 돕니다(게임은 다시 켜면).";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            Status.Text = $"불러오지 못했습니다: {ex.Message}";
        }
    }

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

    // ── 지도 위 이펙트 짝(w-159) ─────────────────────────────────────────────

    private bool _fxChoiceFilling;

    /// <summary>고르개를 채우고 지금 짝지은 시트를 돌려준다(원작이면 null). 지도 위 그림이 없는 마법은 고를 수 없다.</summary>
    private FxSheet? FillSpellFxChoice(int spell, bool hasMap)
    {
        _fxChoiceFilling = true;
        try
        {
            var names = new List<string> { "원작" };
            names.AddRange(SpellFxMap.Names(_dir));
            SpellFxChoice.ItemsSource = names;
            var map = SpellFxMap.Load(_dir);
            string cur = map.TryGetValue(spell, out var n) && n.Length > 0 ? n : "";
            SpellFxChoice.SelectedIndex = cur.Length > 0 && names.IndexOf(cur) is var i and > 0 ? i : 0;
            SpellFxChoice.IsEnabled = hasMap;
            SpellFxHint.Text = hasMap
                ? "pixelfx(github.com/patch-gg/pixelfx)의 render.mjs --sheet 로 뽑은 PNG + JSON 을 불러와 고르면, 게임은 다음 마법부터 이 그림을 씁니다"
                : "이 마법은 지도 위 그림이 없어 바꿀 수 없습니다";
            return hasMap && cur.Length > 0 ? SpellFxMap.Sheet(_dir, cur) : null;
        }
        finally { _fxChoiceFilling = false; }
    }

    private void OnSpellFxChoice(object sender, SelectionChangedEventArgs e)
    {
        int spell = SpellMotionList.SelectedIndex;
        if (_fxChoiceFilling || spell < 0 || _dir == "" || SpellFxChoice.SelectedItem is not string name) return;
        var map = SpellFxMap.Load(_dir);
        map[spell] = name == "원작" ? "" : name;
        try
        {
            SpellFxMap.Save(_dir, map);
            Status.Text = $"{SpellNames.Of(spell)} 지도 위 이펙트 → {name} (게임은 다음 마법부터)";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"저장하지 못했습니다: {ex.Message}"; }
        LoadSpellMotion();
    }

    /// <summary>PNG(옆에 같은 이름의 JSON)를 게임 폴더 fx 에 넣고 지금 마법에 짝짓는다.</summary>
    private void OnImportFxSheet(object sender, RoutedEventArgs e)
    {
        int spell = SpellMotionList.SelectedIndex;
        if (_dir == "" || spell < 0) return;
        var open = new Microsoft.Win32.OpenFileDialog { Title = "이펙트 시트 불러오기 (PNG — 옆에 같은 이름의 JSON)", Filter = "이펙트 시트 (*.png)|*.png" };
        if (open.ShowDialog(this) != true) return;
        string json = System.IO.Path.ChangeExtension(open.FileName, ".json");
        if (!System.IO.File.Exists(json)) { Status.Text = $"같은 이름의 JSON 이 없습니다: {json}"; return; }
        try
        {
            var sheet = FxSheet.Load(System.IO.File.ReadAllBytes(open.FileName), System.IO.File.ReadAllText(json));
            if (sheet == null) { Status.Text = "이펙트 시트로 읽지 못했습니다(8비트 RGBA PNG + Aseprite 꼴 JSON)."; return; }
            string name = System.IO.Path.GetFileNameWithoutExtension(open.FileName);
            string dir = SpellFxMap.DirOf(_dir);
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.Copy(open.FileName, System.IO.Path.Combine(dir, name + ".png"), overwrite: true);
            System.IO.File.Copy(json, System.IO.Path.Combine(dir, name + ".json"), overwrite: true);
            var map = SpellFxMap.Load(_dir);
            map[spell] = name;
            SpellFxMap.Save(_dir, map);
            LoadSpellMotion();
            Status.Text = $"「{name}」 {sheet.Frames.Count}컷을 넣고 {SpellNames.Of(spell)} 에 짝지었습니다 — {dir}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"불러오지 못했습니다: {ex.Message}"; }
    }
}
