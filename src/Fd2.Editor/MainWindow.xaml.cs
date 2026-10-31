using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Fd2.Data;
using Microsoft.Win32;

namespace Fd2.Editor;

/// <summary>
/// 용의기사2 편집기 — 원본 게임 폴더를 그 자리에서 읽어 보여 준다.
/// 맵 캐릭터 탭은 FDICON.B24 의 네 방향 걸음, 전투 모션 탭은 FIGANI.DAT 를 한 컷씩.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] FacingNames = ["앞 (아래)", "왼옆", "뒤 (위)", "오른옆"];
    private static readonly int[] FacingOrder = [0, 2, 1, 3];   // 앞 · 뒤 · 왼 · 오른 순으로 늘어놓는다
    private static readonly int[] WalkCycle = [0, 1, 2, 1];

    private string _dir = "";
    private uint[] _pal = [];
    private IconBank? _icons;

    private readonly DispatcherTimer _walkTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private int _walkStep;

    private readonly DispatcherTimer _playTimer = new();
    private Motion? _motion;
    private int _frame, _held;

    public MainWindow(string dir)
    {
        InitializeComponent();
        _walkTimer.Tick += (_, _) => { if (WalkAnim.IsChecked == true) { _walkStep++; ShowFacings(); } };
        _playTimer.Tick += OnPlayTick;
        Open(dir);
    }

    // ── 게임 폴더 ───────────────────────────────────────────────────────────

    private void Open(string dir)
    {
        if (GameFolder.Missing(dir) is { } f)
        {
            Status.Text = $"'{dir}' 에 {f} 가 없습니다 — 파일 > 게임 폴더 열기로 골라 주세요.";
            return;
        }
        _dir = dir;
        _pal = GameFolder.Palette(dir);
        _icons = new IconBank(System.IO.Path.Combine(dir, "FDICON.B24"), _pal);

        IconList.ItemsSource = Enumerable.Range(0, _icons.Characters)
            .Select(i => new CharItem(i, $"{i} {CharacterNames.Of(i)}", ToBitmap(_icons.Frame(i, 0), Rle.Tile, Rle.Tile))).ToList();
        int motions = Motion.Characters(dir);
        MotionList.ItemsSource = Enumerable.Range(0, motions)
            .Select(i => new CharItem(i, $"{i} {CharacterNames.Of(i)}", i < _icons.Characters ? ToBitmap(_icons.Frame(i, 0), Rle.Tile, Rle.Tile) : null)).ToList();
        IconList.SelectedIndex = 0;
        MotionList.SelectedIndex = 0;
        _walkTimer.Start();
        Title = $"용의기사2 편집기 — {dir}";
        FillVersionBoxes();
        foreach (var v in FindVersions()) MotionFiles.Ensure(v.Dir);   // 버전마다 .obs 가 없으면 뽑아 둔다
        FillMotionVersions();
        FillMusic();
        LoadStats();
        FillSpellMotions();
        int scenes = Dat.Count(System.IO.Path.Combine(dir, "FDFIELD.DAT")) / 3;
        EventScenes.ItemsSource = Enumerable.Range(0, scenes).Select(i => $"{i + 1}장").ToList();
        EventScenes.SelectedIndex = 0;
        Status.Text = $"캐릭터 그림 {_icons.Characters}명 · 전투 모션 {motions}명  ·  ←/→ 한 컷씩, 스페이스 재생/정지";
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "용의기사2 게임 폴더(FDFIELD.DAT 가 있는 곳)", InitialDirectory = _dir };
        if (dlg.ShowDialog(this) == true) Open(dlg.FolderName);
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    // ── 여러 판 ────────────────────────────────────────────────────────────

    private readonly List<string> _extraVersions = new();

    private List<VersionItem> FindVersions() =>
        GameVersions.Discover(_extraVersions.Append(_dir).Where(d => d.Length > 0))
            .Select(v => new VersionItem(v.Dir, $"{v.Name}  —  {v.Dir}")).ToList();

    private void OnVersionMenuOpened(object sender, RoutedEventArgs e)
    {
        VersionMenu.Items.Clear();
        foreach (var v in FindVersions())
        {
            var item = new MenuItem { Header = v.Label, IsCheckable = true, IsChecked = string.Equals(v.Dir, _dir, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => Open(v.Dir);
            VersionMenu.Items.Add(item);
        }
        if (VersionMenu.Items.Count == 0) VersionMenu.Items.Add(new MenuItem { Header = "(찾은 버전이 없습니다)", IsEnabled = false });
    }

    // ── 메뉴에서 여는 창 — 버전 > 버전 비교 · 합치기, 에셋 > 배경음악(숨긴 탭의 내용을 따로 창으로 옮겨 띄운다) ──────

    private Window? _compareWindow, _musicWindow;
    private bool _quitting;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _quitting = true;   // 편집기를 닫으면 숨겨 둔 창들도 함께 닫힌다
        base.OnClosing(e);
    }

    private void OnOpenCompare(object sender, RoutedEventArgs e) => ShowPanel(CompareTab, "버전 비교 · 합치기", ref _compareWindow);

    private void OnOpenMusic(object sender, RoutedEventArgs e) => ShowPanel(MusicTab, "배경음악", ref _musicWindow);

    private void ShowPanel(TabItem tab, string title, ref Window? window)
    {
        if (window == null)
        {
            var content = (UIElement)tab.Content;
            tab.Content = null;
            var w = new Window { Title = $"{title} — 용의기사2 편집기", Content = content, Owner = this, Width = 1200, Height = 760, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            w.Closing += (_, ce) => { if (_quitting) return; ce.Cancel = true; w.Hide(); };   // 닫으면 숨겨 두고 다음에 그대로 다시 연다
            window = w;
        }
        window.Show();
        window.Activate();
    }

    private void FillVersionBoxes()
    {
        var list = FindVersions();
        BaseVersion.ItemsSource = list;
        OtherVersion.ItemsSource = list;
        BaseVersion.SelectedItem = list.FirstOrDefault(v => v.Dir.Contains("original", StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
        OtherVersion.SelectedItem = list.FirstOrDefault(v => v != BaseVersion.SelectedItem && string.Equals(v.Dir, _dir, StringComparison.OrdinalIgnoreCase))
                                    ?? list.FirstOrDefault(v => v != BaseVersion.SelectedItem);
    }

    private void OnAddVersion(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "버전 폴더(FDFIELD.DAT 가 있는 곳)" };
        if (dlg.ShowDialog(this) != true) return;
        if (GameFolder.Missing(dlg.FolderName) is { } f) { Status.Text = $"{dlg.FolderName} 에 {f} 가 없습니다."; return; }
        _extraVersions.Add(dlg.FolderName);
        FillVersionBoxes();
    }

    private void OnCompare(object sender, RoutedEventArgs e)
    {
        if (BaseVersion.SelectedItem is not VersionItem a || OtherVersion.SelectedItem is not VersionItem b) return;
        Status.Text = "비교하는 중…";
        var rows = GameVersions.Diff(a.Dir, b.Dir)
            .Where(d => d.Status != "같음")
            .Select(d => new DiffRow(d.File, d.Status,
                d.ChangedEntries.Count == 0 ? "" : $"{d.ChangedEntries.Count}개: " + string.Join(", ", d.ChangedEntries.Take(40)) + (d.ChangedEntries.Count > 40 ? " …" : "")))
            .ToList();
        DiffGrid.ItemsSource = rows;
        Status.Text = $"다른 파일 {rows.Count}개 — 「비교 버전 것」을 체크하고 합치면, 기준 버전에 그 파일만 바꿔 넣은 새 폴더를 만듭니다.";
    }

    /// <summary>
    /// 고른 파일의 상세 — 갈래(지도 · 사건 줄 · 상자 · 유닛 …)마다 탭 하나, 줄마다 어디 · 무엇과 기준 값(왼쪽) · 비교 값(오른쪽)(<see cref="VersionDetail"/>).
    /// </summary>
    private void OnDiffSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DiffGrid.SelectedItem is not DiffRow row || BaseVersion.SelectedItem is not VersionItem a || OtherVersion.SelectedItem is not VersionItem b) return;
        var changes = VersionDetail.Describe(a.Dir, b.Dir, row.File);
        DiffHeader.Text = $"{row.File} — 바뀐 곳 {changes.Count}개 · 왼쪽 기준 {a.Label} · 오른쪽 비교 {b.Label}";
        DiffTabs.Items.Clear();
        var otherCell = new Style(typeof(DataGridCell));
        otherCell.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xD6))));
        foreach (var g in changes.GroupBy(c => c.Tab))
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
                ItemsSource = g.ToList(), FontSize = 13,
            };
            DataGridTextColumn Column(string header, string path, double star) =>
                new() { Header = header, Binding = new System.Windows.Data.Binding(path), Width = new DataGridLength(star, DataGridLengthUnitType.Star) };
            grid.Columns.Add(Column("어디", nameof(VersionDetail.Change.Where), 2));
            grid.Columns.Add(Column("무엇", nameof(VersionDetail.Change.What), 1.2));
            grid.Columns.Add(Column($"기준 — {a.Label}", nameof(VersionDetail.Change.Base), 2));
            var other = Column($"비교 — {b.Label}", nameof(VersionDetail.Change.Other), 2);
            other.CellStyle = otherCell;
            grid.Columns.Add(other);
            DiffTabs.Items.Add(new TabItem { Header = $"{g.Key} ({g.Count()})", Content = grid });
        }
        if (DiffTabs.Items.Count > 0) DiffTabs.SelectedIndex = 0;
    }

    private void OnMerge(object sender, RoutedEventArgs e)
    {
        if (BaseVersion.SelectedItem is not VersionItem a || OtherVersion.SelectedItem is not VersionItem b || DiffGrid.ItemsSource is not List<DiffRow> rows) return;
        var picks = rows.Where(r => r.UseOther).ToDictionary(r => r.File, _ => b.Dir);
        string target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(a.Dir)!, MergeName.Text.Trim());
        try
        {
            GameVersions.Merge(a.Dir, picks, target);
            _extraVersions.Add(target);
            FillVersionBoxes();
            Status.Text = $"합쳤습니다 — {target} (비교 버전 것 {picks.Count}개). 게임의 게임 > 버전 고르기에서 고르실 수 있습니다.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"못 합쳤습니다: {ex.Message}"; }
    }

    // ── 맵 캐릭터 ───────────────────────────────────────────────────────────

    private int IconScale => IconZoom.SelectedIndex switch { 0 => 2, 1 => 3, 3 => 6, _ => 4 };

    private void OnIconSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_icons == null || IconList.SelectedItem is not CharItem item) return;
        AllFrames.ItemsSource = Enumerable.Range(0, IconBank.FramesPerChar)
            .Select(f => new FrameItem(f, $"{f} {FacingNames[f / 3]} {f % 3 + 1}", ToBitmap(_icons.Frame(item.Index, f), Rle.Tile, Rle.Tile), 0))
            .ToList();
        ShowFacings();
    }

    private void OnIconZoomChanged(object sender, SelectionChangedEventArgs e) => ShowFacings();

    private void ShowFacings()
    {
        if (_icons == null || IconList?.SelectedItem is not CharItem item) return;
        int step = WalkAnim.IsChecked == true ? WalkCycle[_walkStep % WalkCycle.Length] : 0;
        double size = Rle.Tile * IconScale;
        Facings.ItemsSource = FacingOrder
            .Select(d => new FrameItem(d, FacingNames[d], ToBitmap(_icons.Frame(item.Index, d * 3 + step), Rle.Tile, Rle.Tile), size))
            .ToList();
    }

    // ── 전투 모션 ───────────────────────────────────────────────────────────

    private int MotionKind => KindStand.IsChecked == true ? 0 : 1;

    private void OnMotionCharSelected(object sender, SelectionChangedEventArgs e)
    {
        ShowMotionPick();
        LoadMotion();
    }

    // ── 이벤트 — 장마다 사건을 글로(EventText) ───────────────────────────────────

    private SceneEvents? _events;
    private bool _eventsTried;

    private void OnEventSceneSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_dir == "" || _icons == null || EventScenes.SelectedIndex < 0) return;
        if (!_eventsTried) { _eventsTried = true; _events = SceneEvents.Load(); }
        try
        {
            var scene = new Scene(_dir, EventScenes.SelectedIndex, _pal, _icons);
            EventText.Text = string.Join(Environment.NewLine, Fd2.Data.EventText.Describe(_events, scene));
            EventText.ScrollToHome();
        }
        catch (Exception ex) when (ex is System.IO.IOException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            EventText.Text = $"이 장을 읽지 못했습니다: {ex.Message}";
        }
    }

    // ── 표 편집 — 능력치(아군 · 적) · 적 배치(장마다) · 아이템 · 마법 · 상점 ─────────────────────────
    // FD2.EXE · FDFIELD.DAT 를 통째로 읽어 두고(_exe · _field), 탭마다 TableEditor 가 칸을 바로 고친다.

    private byte[]? _exe, _field;
    private readonly List<TableEditor> _tableEditors = new();
    private readonly Dictionary<int, ImageSource?> _faces = new();

    private void LoadStats()
    {
        string exe = System.IO.Path.Combine(_dir, "FD2.EXE"), field = System.IO.Path.Combine(_dir, "FDFIELD.DAT");
        _exe = System.IO.File.Exists(exe) && new System.IO.FileInfo(exe).Length == GameTables.ExeSize ? System.IO.File.ReadAllBytes(exe) : null;
        _field = System.IO.File.Exists(field) ? System.IO.File.ReadAllBytes(field) : null;
        _faces.Clear();
        _towns.Clear();
        _unitMapBase = null;
        if (_tableEditors.Count == 0) BuildTableEditors();
        foreach (var t in _tableEditors) t.Refresh();
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private void BuildTableEditors()
    {
        var side = new ComboBox { Width = 260, ItemsSource = new[] { "아군 (동료 꼴 · 레벨업 성장)", "적군 (몬스터 — 능력은 레벨당 값)" }, SelectedIndex = 0 };
        var stats = new TableEditor(this, () => side.SelectedIndex == 1 ? StatTables.Enemies() : StatTables.Allies(), () => _exe, "FD2.EXE 에 저장", SaveExe, LoadStats,
            r => Face(side.SelectedIndex == 1 ? GameTables.MonsterBase + r : r), Label("표"), side);
        side.SelectionChanged += (_, _) => stats.Refresh();
        StatsHost.Content = stats;

        int scenes = _field == null ? 0 : ((Dat.U32(_field, 6) - 6) / 4 - 1) / 3;   // 장면 n = FDFIELD 3n · 3n+1 · 3n+2
        var scene = new ComboBox { Width = 120, ItemsSource = Enumerable.Range(0, scenes).Select(i => $"{i + 1}장").ToList(), SelectedIndex = 0 };
        StatTables.Table? Units() => _field == null || scene.SelectedIndex < 0 ? null : StatTables.StageUnits(_field, scene.SelectedIndex);
        var units = new TableEditor(this, Units, () => _field, "FDFIELD.DAT 에 저장", SaveField, LoadStats,
            r => Units() is { } t && r < t.Rows.Count && _field != null ? Face(StatTables.Get(_field, t.Rows[r].Fields[1])) : null,
            Label("장"), scene, Label("   편 · 그림 · 레벨 · 자리 · 등장 차례 · 장비 · 아이템(255 = 없음) · AI · 떨굼"));
        scene.SelectionChanged += (_, _) => units.Refresh();
        UnitsHost.Content = UnitsWithMap(units, scene);

        ItemsHost.Content = new TableEditor(this, StatTables.Items, () => _exe, "FD2.EXE 에 저장", SaveExe, LoadStats);
        SpellsHost.Content = new TableEditor(this, StatTables.Spells, () => _exe, "FD2.EXE 에 저장", SaveExe, LoadStats);
        ShopsHost.Content = new TableEditor(this, StatTables.Shops, () => _exe, "FD2.EXE 에 저장", SaveExe, LoadStats,
            TownPicture, Label("마을마다 무기점 12 · 도구점 8 · 비밀상점 8칸 — 「없음(255)」 칸에 아이템을 고르면 추가, 「없음(255)」으로 바꾸면 삭제"))
        { PortraitHeight = 100 };
        _tableEditors.AddRange([stats, units, (TableEditor)ItemsHost.Content, (TableEditor)SpellsHost.Content, (TableEditor)ShopsHost.Content]);
    }

    /// <summary>초상화 — DATO 얼굴 첫 장, 없으면 지도 그림.</summary>
    private ImageSource? Face(int icon)
    {
        if (_faces.TryGetValue(icon, out var img)) return img;
        try
        {
            var f = Portrait.Load(_dir, icon, _pal);
            img = f.Count > 0 && f[0].W > 0 ? ToBitmap(f[0].Px, f[0].W, f[0].H)
                : _icons != null && icon < _icons.Characters ? ToBitmap(_icons.Frame(icon, 0), Rle.Tile, Rle.Tile) : null;
        }
        catch (Exception e) when (e is System.IO.IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { img = null; }
        return _faces[icon] = img;
    }

    /// <summary>
    /// 상점 줄의 마을 그림 — 마을 화면(sub_26152)이 기록 +0 으로 고르는 FDOTHER 11 · 61 · 62(표 0x52405, RLE 320x200).
    /// </summary>
    private ImageSource? TownPicture(int row)
    {
        if (_exe == null || StatTables.Shops().Rows is not { } rows || row >= rows.Count) return null;
        int look = StatTables.Get(_exe, rows[row].Fields[0]);
        if (look > 2) return null;
        int entry = _exe[0x52405 + 0x25014 + look];
        if (_towns.TryGetValue(entry, out var img)) return img;
        try
        {
            var a = Dat.Load(System.IO.Path.Combine(_dir, "FDOTHER.DAT"), entry);
            int w = Dat.U16(a, 0), h = Dat.U16(a, 2);
            img = ToBitmap(Rle.Decode(a, 4, w, h, _pal), w, h);
        }
        catch (Exception e) when (e is System.IO.IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { img = null; }
        return _towns[entry] = img;
    }

    private readonly Dictionary<int, ImageSource?> _towns = [];

    private bool Confirm(string file) =>
        MessageBox.Show(this, $"{System.IO.Path.Combine(_dir, file)} 에 씁니다.\n처음 한 번은 옆에 백업({System.IO.Path.GetFileNameWithoutExtension(file)}-backup{System.IO.Path.GetExtension(file)})을 남깁니다.",
            "저장", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    private void SaveExe()
    {
        if (_exe == null) { Status.Text = "이 폴더의 FD2.EXE 를 읽지 못했습니다(크기가 원본과 다름)."; return; }
        if (!Confirm("FD2.EXE")) return;
        try { StatTables.Save(_dir, _exe); Status.Text = "저장했습니다 — 게임은 다시 켜면 새 값으로 돕니다."; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"저장하지 못했습니다: {ex.Message}"; }
    }

    private void SaveField()
    {
        if (_field == null) return;
        if (!Confirm("FDFIELD.DAT")) return;
        try { StatTables.SaveField(_dir, _field); Status.Text = "저장했습니다 — 게임은 그 장을 새로 시작하면 바뀐 배치로 돕니다."; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"저장하지 못했습니다: {ex.Message}"; }
    }

    // ── 모션 .obs — 버전마다 뽑아 두고, 캐릭터마다 어느 버전 것으로 돌지(MotionFiles) ──────────

    private bool _pickSync;

    private void FillMotionVersions()
    {
        _pickSync = true;
        MotionVersion.ItemsSource = new[] { $"(지금 버전 — {MotionFiles.VersionOf(_dir)})" }.Concat(MotionFiles.Versions()).ToList();
        _pickSync = false;
        ShowMotionPick();
    }

    private void ShowMotionPick()
    {
        if (_dir == "" || MotionList.SelectedItem is not CharItem item || MotionVersion.ItemsSource is not List<string> names) return;
        MotionFiles.Reload();
        string pick = MotionFiles.PickFor(_dir, item.Index);
        _pickSync = true;
        MotionVersion.SelectedIndex = pick == MotionFiles.VersionOf(_dir) ? 0 : Math.Max(0, names.IndexOf(pick));
        _pickSync = false;
    }

    private void OnMotionVersionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickSync || _dir == "" || MotionList.SelectedItem is not CharItem item || MotionVersion.SelectedItem is not string name) return;
        try
        {
            MotionFiles.SetPick(item.Index, MotionVersion.SelectedIndex == 0 ? null : name);
            Status.Text = $"{CharacterNames.Of(item.Index)} 모션 — {(MotionVersion.SelectedIndex == 0 ? "지금 버전" : name)} 것으로 돕니다(게임은 다음 장부터).";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = $"고르기를 저장하지 못했습니다: {ex.Message}"; }
        LoadMotion();
    }

    private void OnExportMotions(object sender, RoutedEventArgs e)
    {
        int files = 0, versions = 0;
        foreach (var v in FindVersions())
        {
            try { files += MotionFiles.Export(v.Dir); versions++; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                Status.Text = $"{v.Label} 을(를) 뽑지 못했습니다: {ex.Message}";
            }
        }
        FillMotionVersions();
        LoadMotion();
        Status.Text = $"버전 {versions}개에서 모션 .obs {files}개를 뽑았습니다 — {MotionFiles.Root}";
    }

    private void OnOpenMotionFolder(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(MotionFiles.Root);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", MotionFiles.Root) { UseShellExecute = true });
    }

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) LoadMotion();
    }

    private void LoadMotion()
    {
        StopPlay();
        _motion = null;
        if (_dir == "" || MotionList.SelectedItem is not CharItem item) return;
        try { _motion = Motion.Load(_dir, item.Index, MotionKind, _pal); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            Status.Text = $"{item.Index}번 모션을 풀지 못했습니다: {ex.Message}";
        }
        _frame = 0;
        if (_motion == null)
        {
            MotionImage.Source = null;
            MotionStrip.ItemsSource = null;
            FrameSlider.Maximum = 0;
            FrameInfo.Text = "이 캐릭터에는 이 모션이 없습니다.";
            return;
        }
        FrameSlider.Maximum = _motion.Frames.Count - 1;
        MotionStrip.ItemsSource = _motion.Frames
            .Select((f, i) => new FrameItem(i, $"{i + 1}  ({f.Hold}틱{(f.Hit != 0 ? " · 맞음" : "")})", Compose(i, onion: false, box: false), 0))
            .ToList();
        ShowFrame();
    }

    private void ShowFrame()
    {
        if (_motion == null) return;
        var f = _motion.Frames[_frame];
        MotionImage.Source = Compose(_frame, Onion.IsChecked == true, ShowBox.IsChecked == true);
        if ((int)FrameSlider.Value != _frame) FrameSlider.Value = _frame;
        FrameInfo.Text = $"컷 {_frame + 1}/{_motion.Frames.Count}   자리 ({f.X},{f.Y})   크기 {f.Width}×{f.Height}   " +
                         $"붙드는 틱 {f.Hold}   맞음 {f.Hit}   효과음 {f.Sound}   (모션 머리 [4] 소리 묶음 = {_motion.SoundBank})";
    }

    /// <summary>320x200 전투 화면에 컷을 제자리에 놓는다. onion 이면 앞 컷을 흐리게 밑에 깐다.</summary>
    private BitmapSource Compose(int index, bool onion, bool box)
    {
        const int W = Motion.ScreenWidth, H = Motion.ScreenHeight;
        var px = new uint[W * H];
        if (onion && index > 0) Put(px, _motion!.Frames[index - 1], 0x60000000u);
        Put(px, _motion!.Frames[index], 0);
        if (box)
        {
            var f = _motion.Frames[index];
            for (int x = f.X; x < f.X + f.Width; x++) { Dot(px, x, f.Y); Dot(px, x, f.Y + f.Height - 1); }
            for (int y = f.Y; y < f.Y + f.Height; y++) { Dot(px, f.X, y); Dot(px, f.X + f.Width - 1, y); }
        }
        return ToBitmap(px, W, H);

        static void Dot(uint[] px, int x, int y)
        {
            if ((uint)x < W && (uint)y < H) px[y * W + x] = 0xFFFFE040;
        }
    }

    /// <summary>컷 그림을 제자리에 찍는다. ghost 가 0 이 아니면 알파를 그 값으로 낮춘다.</summary>
    private static void Put(uint[] px, MotionFrame f, uint ghost)
    {
        for (int y = 0; y < f.Height; y++)
        {
            int sy = f.Y + y;
            if ((uint)sy >= Motion.ScreenHeight) continue;
            for (int x = 0; x < f.Width; x++)
            {
                int sx = f.X + x;
                if ((uint)sx >= Motion.ScreenWidth) continue;
                uint c = f.Pixels[y * f.Width + x];
                if (c == 0) continue;
                px[sy * Motion.ScreenWidth + sx] = ghost != 0 ? (c & 0x00FFFFFF) | ghost : c;
            }
        }
    }

    private void OnFrameSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_motion == null || (int)e.NewValue == _frame) return;
        _frame = Math.Clamp((int)e.NewValue, 0, _motion.Frames.Count - 1);
        ShowFrame();
    }

    private void Step(int delta)
    {
        if (_motion == null) return;
        _frame = (_frame + delta + _motion.Frames.Count) % _motion.Frames.Count;
        _held = 0;
        ShowFrame();
    }

    private void OnPrevFrame(object sender, RoutedEventArgs e) { StopPlay(); Step(-1); }
    private void OnNextFrame(object sender, RoutedEventArgs e) { StopPlay(); Step(1); }
    private void OnOnionChanged(object sender, RoutedEventArgs e) => ShowFrame();

    private void OnStripClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int i } && _motion != null)
        {
            StopPlay();
            _frame = i;
            ShowFrame();
        }
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_playTimer.IsEnabled) { StopPlay(); return; }
        if (_motion == null) return;
        _playTimer.Interval = TimeSpan.FromMilliseconds(int.TryParse(TickMs.Text, out int ms) ? Math.Clamp(ms, 10, 2000) : 55);
        _held = 0;
        _playTimer.Start();
        PlayButton.Content = "■ 정지";
    }

    private void StopPlay()
    {
        _playTimer.Stop();
        PlayButton.Content = "▶ 재생";
    }

    /// <summary>원본처럼 컷마다 「붙드는 틱」만큼 머문 뒤 다음 컷으로.</summary>
    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (_motion == null) { StopPlay(); return; }
        if (++_held < _motion.Frames[_frame].Hold) return;
        _held = 0;
        _frame = (_frame + 1) % _motion.Frames.Count;
        ShowFrame();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Tabs.SelectedIndex != 1 || e.OriginalSource is TextBox) return;
        switch (e.Key)
        {
            case Key.Left: StopPlay(); Step(-1); e.Handled = true; break;
            case Key.Right: StopPlay(); Step(1); e.Handled = true; break;
            case Key.Space: OnPlay(this, e); e.Handled = true; break;
        }
    }

    // ── 공용 ───────────────────────────────────────────────────────────────

    private static BitmapSource ToBitmap(uint[] px, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }
}

public sealed record CharItem(int Index, string Label, ImageSource? Thumb);

public sealed record VersionItem(string Dir, string Label);

public sealed class DiffRow(string file, string status, string entries)
{
    public string File { get; } = file;
    public string Status { get; } = status;
    public string Entries { get; } = entries;
    public bool UseOther { get; set; }
}

public sealed record FrameItem(int Index, string Label, ImageSource Image, double Size);
