using Fd2.Data;

namespace Fd2;

/// <summary>
/// 지도의 움직이는 것(분석-지도 · 분석-자료형식 §6) — 지도 텍스처(처음 한 장) 위에 칸을 덧그린다.
/// <list type="bullet">
/// <item>덮개(타일 속성 0x80, sub_12AC6): 다음 타일(+1)을 사람 위에 한 번 더 — 지붕 · 나무 꼭대기.</item>
/// <item>두 장 번갈아(0x04 → +1, 0x08 → +2, sub_11EEE): 시계 틱마다 뒤집히는 dword_53A40.</item>
/// <item>느린 두 장(0x10 → + dword_53C0B / 2, sub_11EEE): 0~3 을 BIOS 틱 5 마다 하나씩 세는 값(sub_1297D) — 0 · 0 · 1 · 1.</item>
/// <item>물결 색(sub_4E31C): BIOS 틱 2 마다 단계 +1(16 단계), 색 224~239 = EXE 표[단계 .. 단계+15].</item>
/// </list>
/// 고해상도 지도는 칸 그림이 따로 없어 덮개 · 연 상자만 낮은 해상도 그림으로 얹고, 번갈아 · 물결은 그리지 않는다.
/// 같이 붙으면 원작 차례대로 0x08 이 먼저, 그다음 0x10, 0x04.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>지도 텍스처가 고해상도(칸 그림을 그대로 덧그리면 어긋남)인가 — 플랫폼이 텍스처를 만들 때 정한다.</summary>
    private bool _mapHi;

    private Scene? _fxScene;
    private readonly List<int> _coverCells = new(), _liveCells = new();
    private readonly Dictionary<int, uint[][]?> _waterTiles = new();
    private uint[]? _waterColors;

    /// <summary>원작 틱(18.2Hz)으로 센 시각.</summary>
    private int BiosTick => (int)(_time * 18.2);

    /// <summary>번갈아 타일의 지금 장(dword_53A40) — 원작은 그리는 틀마다 틱이 바뀌면 뒤집는다. 여기서는 4틱마다(원작 화면 틀 빠르기쯤).</summary>
    private int AltFrame => _mapHi ? 0 : BiosTick / 4 & 1;

    /// <summary>느린 두 장(dword_53C0B / 2) — sub_1297D 가 틱 차이가 4 를 넘으면(5 틱마다) 0~3 을 센다.</summary>
    private int SlowFrame => _mapHi ? 0 : BiosTick / 5 % 4 / 2;

    private int WaterStep => BiosTick / 2 % 16;

    /// <summary>장면이 바뀌면 덮개 칸 · 움직이는 칸을 다시 찾는다.</summary>
    private void PrepareMapFx()
    {
        if (ReferenceEquals(_fxScene, _scene)) return;
        _fxScene = _scene;
        _coverCells.Clear();
        _liveCells.Clear();
        _waterTiles.Clear();
        _waterColors ??= _tables?.WaterColors();
        for (int y = 0; y < _scene.Height; y++)
            for (int x = 0; x < _scene.Width; x++)
            {
                int t = _scene.CellTile(x, y), f = _scene.FlagsOf(t), i = y * _scene.Width + x;
                if ((f & 0x80) != 0) _coverCells.Add(i);
                if ((f & 0x1C) != 0 || WaterVariants(t) != null || WaterVariants(t + ((f & 0x08) != 0 ? 2 : 1)) != null) _liveCells.Add(i);
            }
    }

    /// <summary>타일 t 에 물결 색(224~239)이 있으면 16 단계 그림, 없으면 null.</summary>
    private uint[][]? WaterVariants(int t)
    {
        if (_waterColors == null || t < 0 || t >= _scene.TileCount) return null;
        if (_waterTiles.TryGetValue(t, out var v)) return v;
        // 물결 색 자리를 알아볼 수 있게 다른 값으로 풀어 본다
        var probe = (uint[])_pal.Clone();
        for (int c = 224; c < 240; c++) probe[c] = 0xFF000000u | 0x00ABC000u | (uint)c;
        var px = _scene.DecodeTile(t, probe);
        if (!px.Any(p => (p & 0x00FFF000u) == 0x00ABC000u && (p & 0xFF) is >= 224 and < 240)) return _waterTiles[t] = null;
        v = new uint[16][];
        for (int s = 0; s < 16; s++)
        {
            var pal = (uint[])_pal.Clone();
            for (int c = 0; c < 16; c++) pal[224 + c] = _waterColors[s + c];
            v[s] = _scene.DecodeTile(t, pal);
        }
        return _waterTiles[t] = v;
    }

    /// <summary>칸 i 의 지금 타일 번호 — 번갈아 타일이면 장을 더한다.</summary>
    private int LiveTile(int i)
    {
        int x = i % _scene.Width, y = i / _scene.Width;
        int t = _scene.CellTile(x, y), f = _scene.FlagsOf(t);
        if ((f & 0x08) != 0) t += 2 * AltFrame;
        else if ((f & 0x10) != 0) t += SlowFrame;
        else if ((f & 0x04) != 0) t += AltFrame;
        return t;
    }

    private bool CellOnScreen(int i, out int sx, out int sy)
    {
        int cell = T * _zoom, x = i % _scene.Width, y = i / _scene.Width;
        sx = _offX + (x * T - _camX) * _zoom;
        sy = _offY + (y * T - _camY) * _zoom;
        return sx < _fbW && sy < _fbH && sx + cell > 0 && sy + cell > 0;
    }

    /// <summary>바닥 칸 덧그리기 — 연 상자, 그리고 낮은 해상도 지도면 번갈아 · 물결 칸.</summary>
    private void DrawPatches()
    {
        PrepareMapFx();
        int cell = T * _zoom;
        if (!_mapHi)
            foreach (int i in _liveCells)
            {
                if (_scene.Patched.Contains(i) || !CellOnScreen(i, out int sx, out int sy)) continue;
                int t = LiveTile(i);
                var px = WaterVariants(t) is { } w ? w[WaterStep] : _scene.Tile(t);
                Blit(px, T, sx, sy, cell);
            }
        foreach (int i in _scene.Patched)
            if (CellOnScreen(i, out int sx, out int sy)) Blit(_scene.TileAt(i % _scene.Width, i / _scene.Width), T, sx, sy, cell);
    }

    /// <summary>덮개 — 사람을 그린 뒤 덮개 칸의 다음 타일을 투명 빼고 한 번 더.</summary>
    private void DrawCovers()
    {
        PrepareMapFx();
        int cell = T * _zoom;
        foreach (int i in _coverCells)
        {
            if (!CellOnScreen(i, out int sx, out int sy)) continue;
            int t = LiveTile(i) + 1;
            var px = !_mapHi && WaterVariants(t) is { } w ? w[WaterStep] : _scene.Tile(t);
            Blit(px, T, sx, sy, cell);
        }
    }
}

/// <summary>
/// 칸 위 효과 그림 — 지도에서 보이는 마법(FDOTHER 6, sub_1C4CC)과 나타나는 그림(FDOTHER 9, sub_32999).
/// 장마다 대상 칸들 위에 같은 장을(칸 왼쪽 위에서 6점 위로) 얹고 다음 장으로. 다 그리면 after.
/// </summary>
internal sealed partial class GameWindow
{
    private const double FxFrame = 1 / 12.0;

    private readonly Dictionary<int, EffectFrames?> _fxBanks = new();
    private EffectFrames? _fx;
    private List<(int X, int Y)> _fxCells = new();
    private int _fxStart, _fxCount, _fxAt;
    private double _fxClock;
    private Action? _fxAfter;
    private BattleState _fxResume;
    private Action<int>? _fxSound;

    private EffectFrames? FxBank(int entry)
    {
        if (_fxBanks.TryGetValue(entry, out var b)) return b;
        try { b = new EffectFrames(_dir, entry, _pal); }
        catch (Exception e) when (e is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { b = null; }
        return _fxBanks[entry] = b is { Count: > 0 } ? b : null;
    }

    /// <summary>효과 그림을 돌린다 — sound(장 번호) 는 장이 바뀔 때마다 불린다.</summary>
    private void PlayTileFx(int entry, int start, int count, IEnumerable<(int X, int Y)> cells, Action after, Action<int>? sound = null)
    {
        var bank = FxBank(entry);
        var list = cells.ToList();
        if (bank == null || count <= 0 || list.Count == 0) { after(); return; }
        (_fx, _fxCells, _fxStart, _fxCount, _fxAt, _fxClock, _fxAfter, _fxSound) = (bank, list, start, count, 0, 0, after, sound);
        _fxResume = _bs;
        _bs = BattleState.Fx;
        sound?.Invoke(0);
    }

    private void FxTick(double dt)
    {
        if (_fx == null) return;
        if ((_fxClock += dt) < FxFrame) return;
        _fxClock -= FxFrame;
        if (++_fxAt < _fxCount) { _fxSound?.Invoke(_fxAt); return; }
        _fx = null;
        _bs = _fxResume;
        var a = _fxAfter;
        _fxAfter = null;
        a?.Invoke();
    }

    private void DrawTileFx()
    {
        if (_fx == null) return;
        var (px, w, h) = _fx[_fxStart + _fxAt];
        foreach (var (x, y) in _fxCells)
        {
            int sx = _offX + (x * T - _camX) * _zoom, sy = _offY + (y * T - 6 - _camY) * _zoom;
            BlitScaled(px, w, h, sx, sy, _zoom, 0, 0, _fbW, _fbH);
        }
    }

    /// <summary>
    /// 원작이 지도 위에서 보이는 마법인가 — 우리 편의 9 · 10~23 · 25~27(0~8 · 24 · 28~35 는 전투 화면). 컴퓨터 편은 10 이상(분석-마법 §6.1).
    /// </summary>
    private static bool MapSpell(Unit caster, int spell) =>
        caster.Team == 2 ? spell is 9 or (>= 10 and <= 23) or (>= 25 and <= 27) : spell >= 10 && spell != 24 && spell < 28;

    /// <summary>지도 위 마법 그림 — 첫 장 소리는 표의 값, 몇 마법은 원작처럼 가운데 장에서 한 번 더(sub_1C4CC).</summary>
    private void PlaySpellFx(int spell, IEnumerable<(int X, int Y)> cells, Action after)
    {
        if (_tables == null) { after(); return; }
        var (start, count, first) = _tables.MapSpellFx(spell);
        void Sound(int n)
        {
            int s = n == 0 ? first : spell switch
            {
                22 when n == 7 => 3,
                25 when n is 3 or 6 => 5,
                18 when n == 4 => 7,
                19 when n is 3 or 6 => 8,
                8 when n is 3 or 6 => 10,
                9 when n is 15 or 19 => 15,
                _ => 0,
            };
            if (s > 0) PlaySfx(80, s);
        }
        PlayTileFx(6, start, count, cells, after, Sound);
    }
}
