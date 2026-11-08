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
        if (DeathFxTick(dt)) return;
        if (SheetFxTick(dt)) return;
        if (FlashTick(dt)) return;
        if (_fx == null) return;
        if ((_fxClock += dt * SpellRate) < FxFrame) return;   // 지도 위 마법 그림도 마법 속도 배율로
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
        DrawDeathFx();
        DrawSheetFx();
        DrawFlash();
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

    /// <summary>
    /// 지도 위 마법 그림 — 첫 장 소리는 표의 값, 몇 마법은 원작처럼 가운데 장에서 한 번 더(sub_1C4CC).
    /// originalArt 면 편집기의 바깥 이펙트 짝(회복술 13 → holy-heal 같은)을 안 보고 늘 원작 FDOTHER 6 그림 — 회복 물건(sub_211A4)이 쓴다.
    /// </summary>
    private void PlaySpellFx(int spell, IEnumerable<(int X, int Y)> cells, Action after, bool originalArt = false)
    {
        if (_tables == null) { after(); return; }
        var (start, count, first) = _tables.MapSpellFx(spell);
        // 편집기 마법 모션 탭에서 이 마법에 바깥 이펙트 시트를 짝지었으면 원작 그림 대신(w-158 · w-159) — 첫 장 소리는 원작 것
        if (!originalArt && SpellFxMap.For(_dir, spell) is { } sheet && cells.ToList() is { Count: > 0 } at)
        {
            if (first > 0) PlaySfx(80, first);
            PlaySheetFx(sheet, at, after);
            return;
        }
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

/// <summary>
/// 맞은 사람 번쩍임(sub_1C2DA) — 지도 위 마법 그림(sub_1C4CC) 다음에 효과음(FDOTHER 80 의 1)과 함께, 대상마다 사람 그림 꼴을
/// 팔레트 200 한 빛으로 덮은 그림(sub_4E127 — 셋째 인자 456 의 아래 바이트 0xC8 이 빛)과 원래 지도를 한 틱(1/18.2초)씩 다섯 번 번갈아.
/// 원작 차례: 원래 · 덮음 × 5 뒤 원래.
/// </summary>
internal sealed partial class GameWindow
{
    private const int FlashTicks = 10, FlashColor = 200;
    private const double FlashTick1 = 1 / 18.2;

    private List<Unit>? _hitFlash;
    private int _hitFlashStep;
    private double _hitFlashClock;
    private Action? _hitFlashAfter;
    private BattleState _hitFlashResume;

    private void PlayTargetFlash(IEnumerable<Unit> targets, Action after)
    {
        var list = targets.Where(u => u.Alive).ToList();
        if (list.Count == 0) { after(); return; }
        (_hitFlash, _hitFlashStep, _hitFlashClock, _hitFlashAfter, _hitFlashResume) = (list, 0, 0, after, _bs);
        _bs = BattleState.Fx;
        PlaySfx(80, 1);
    }

    private bool FlashTick(double dt)
    {
        if (_hitFlash == null) return false;
        if ((_hitFlashClock += dt * SpellRate) < FlashTick1) return true;
        _hitFlashClock -= FlashTick1;
        if (++_hitFlashStep < FlashTicks) return true;
        _hitFlash = null;
        _bs = _hitFlashResume;
        var a = _hitFlashAfter;
        _hitFlashAfter = null;
        a?.Invoke();
        return true;
    }

    /// <summary>홀수 틱에 대상의 지금 지도 그림(방향 · 걸음 컷, 큰 그림이면 큰 그림)을 한 빛으로 — 사람을 그리는 자리 그대로.</summary>
    private void DrawFlash()
    {
        if (_hitFlash == null || (_hitFlashStep & 1) == 0) return;
        uint c = _pal[FlashColor] | 0xFF000000;
        int cell = T * _zoom;
        foreach (var u in _hitFlash)
        {
            int sx = _offX + (int)Math.Round((u.DrawX * T - _camX) * _zoom);
            int sy = _offY + (int)Math.Round((u.DrawY * T - _camY) * _zoom);
            int frame = (int)u.Facing * 3 + WalkCycle[(int)(_time * IdleFps) % WalkCycle.Length];
            var hi = HiIcon(u.Icon, frame);
            var px = hi ?? _icons.Frame(u.Icon, frame);
            int size = hi != null ? T * 4 : T;
            int y0 = Math.Max(0, sy), y1 = Math.Min(_fbH, sy + cell), x0 = Math.Max(0, sx), x1 = Math.Min(_fbW, sx + cell);
            for (int yy = y0; yy < y1; yy++)
            {
                int srow = (yy - sy) * size / cell * size, drow = yy * _fbW;
                for (int xx = x0; xx < x1; xx++)
                    if (px[srow + (xx - sx) * size / cell] != 0) _fb[drow + xx] = c;
            }
        }
    }
}

/// <summary>
/// 죽음 연출(sub_1DB65) — HP 0 이 된 사람 가운데 화면에 보이는 이가 있으면: 13 틱 동안 제자리에서 빙글(방향 = 틱 % 4),
/// 그다음 효과음(FDOTHER 31 의 3)과 함께 FDOTHER 5 의 68~79 장(터지는 불꽃 37x37 → 72x71)을 한 틱에 한 장.
/// 0~5 장은 앞 장 위에 덧그려(사람도 아직 보임) 쌓이고, 6~11 장은 사람이 빠진 지도 위에 한 장씩. 그림 왼쪽 위 = 사람 칸에서 한 칸 왼쪽 위.
/// </summary>
internal sealed partial class GameWindow
{
    private const int DeathSpin = 13, DeathFirst = 68, DeathFrames = 12, DeathStack = 6;
    private const double DeathTick = 1 / 18.2;

    private List<Unit>? _death;
    private EffectFrames? _deathBank;
    private int _deathStep;
    private double _deathClock;
    private Action? _deathAfter;
    private BattleState _deathResume;

    /// <summary>쓰러진 사람들의 죽음 연출 뒤 after — 화면에 보이는 사람이 없거나 그림이 없으면 바로 after.</summary>
    private void PlayDeathFx(IEnumerable<Unit> dead, Action after)
    {
        var list = dead.Where(u => u.MaxHp > 0 && u.Hp <= 0).Distinct()
            .Where(u => CellOnScreen(u.Y * _scene.Width + u.X, out _, out _)).ToList();
        var bank = list.Count > 0 ? FxBank(5) : null;
        if (bank == null || bank.Count < DeathFirst + DeathFrames) { after(); return; }
        (_death, _deathBank, _deathStep, _deathClock, _deathAfter) = (list, bank, 0, 0, after);
        _deathResume = _bs;
        _bs = BattleState.Fx;
        foreach (var u in list) u.Facing = Dir.Down;
    }

    /// <summary>빙글 도는 동안과 불꽃 0~5 장 동안은 쓰러진 사람도 그린다.</summary>
    private bool DeathShown(Unit u) => _death != null && _deathStep < DeathSpin + DeathStack && _death.Contains(u);

    /// <returns>죽음 연출 중이면 true.</returns>
    private bool DeathFxTick(double dt)
    {
        if (_death == null) return false;
        if ((_deathClock += dt) < DeathTick) return true;
        _deathClock -= DeathTick;
        if (++_deathStep < DeathSpin)
            foreach (var u in _death) u.Facing = (Dir)(_deathStep % 4);
        else if (_deathStep == DeathSpin) PlaySfx(31, 3);
        if (_deathStep < DeathSpin + DeathFrames) return true;
        _death = null;
        _bs = _deathResume;
        var a = _deathAfter;
        _deathAfter = null;
        a?.Invoke();
        return true;
    }

    private void DrawDeathFx()
    {
        if (_death == null || _deathBank == null || _deathStep < DeathSpin) return;
        int k = _deathStep - DeathSpin;
        for (int f = k < DeathStack ? 0 : k; f <= k; f++)
        {
            var (px, w, h) = _deathBank[DeathFirst + f];
            foreach (var u in _death)
            {
                int sx = _offX + ((u.X - 1) * T - _camX) * _zoom, sy = _offY + ((u.Y - 1) * T - 6 - _camY) * _zoom;
                BlitScaled(px, w, h, sx, sy, _zoom, 0, 0, _fbW, _fbH);
            }
        }
    }
}

/// <summary>
/// 바깥 이펙트 시트(pixelfx, <see cref="FxSheet"/>)를 지도 칸 위에 — 시트의 발 자리를 대상 칸 아래 가운데에 맞춰 알파로 겹친다.
/// 컷 시간은 시트대로, 마법 속도 모드 · 누르면 빨리 감기(<see cref="SpellRate"/>)를 따른다.
/// </summary>
internal sealed partial class GameWindow
{
    private FxSheet? _sheetFx;
    private List<(int X, int Y)> _sheetCells = new();
    private double _sheetMs;
    private Action? _sheetAfter;
    private BattleState _sheetResume;

    private void PlaySheetFx(FxSheet sheet, List<(int X, int Y)> cells, Action after)
    {
        (_sheetFx, _sheetCells, _sheetMs, _sheetAfter, _sheetResume) = (sheet, cells, 0, after, _bs);
        _bs = BattleState.Fx;
    }

    private bool SheetFxTick(double dt)
    {
        if (_sheetFx is not { } s) return false;
        _sheetMs += dt * 1000 * SpellRate;
        if (_sheetMs < s.TotalMs) return true;
        _sheetFx = null;
        _bs = _sheetResume;
        var a = _sheetAfter;
        _sheetAfter = null;
        a?.Invoke();
        return true;
    }

    /// <summary>
    /// 지금 컷을 대상 칸마다 — 보이는 폭이 칸(24점)을 넘으면 칸 너비에 맞게 줄인다(옆 칸에 걸치지 않게, fb-86).
    /// 화면 배율을 곱한 실수 배로 그려(가장 가까운 점) 배율 2 이상이면 줄여도 점이 뭉개지지 않는다.
    /// </summary>
    private void DrawSheetFx()
    {
        if (_sheetFx is not { } s || s.FrameAt(_sheetMs) is not (>= 0 and var f)) return;
        var (px, w, h) = s.Frames[f];
        double fit = Math.Min(1.0, T / 2.0 / s.HalfWidth), k = _zoom * fit;
        foreach (var (x, y) in _sheetCells)
        {
            // 발 자리 = 칸 아래 가운데(화면 점)
            double fx = _offX + (x * T + T / 2.0 - _camX) * _zoom, fy = _offY + (y * T + T - 2 - _camY) * _zoom;
            BlendScaled(px, w, h, fx - s.PivotX * k, fy - s.PivotY * k, k);
        }
    }

    /// <summary>알파가 있는 그림을 k 배(실수)로 겹친다 — 화면 점마다 가장 가까운 그림 점(그릴 판은 늘 불투명).</summary>
    private void BlendScaled(uint[] px, int w, int h, double x, double y, double k)
    {
        int y0 = Math.Max(0, (int)Math.Floor(y)), y1 = Math.Min(_fbH, (int)Math.Ceiling(y + h * k));
        int x0 = Math.Max(0, (int)Math.Floor(x)), x1 = Math.Min(_fbW, (int)Math.Ceiling(x + w * k));
        for (int yy = y0; yy < y1; yy++)
        {
            int sy = (int)((yy + 0.5 - y) / k);
            if (sy < 0 || sy >= h) continue;
            int srow = sy * w;
            for (int xx = x0; xx < x1; xx++)
            {
                int sx = (int)((xx + 0.5 - x) / k);
                if (sx < 0 || sx >= w) continue;
                uint c = px[srow + sx], a = c >> 24;
                if (a == 0) continue;
                ref uint d = ref _fb[yy * _fbW + xx];
                if (a == 255) { d = c; continue; }
                uint dv = d, na = 255 - a;
                uint r = ((c >> 16 & 255) * a + (dv >> 16 & 255) * na) / 255, g = ((c >> 8 & 255) * a + (dv >> 8 & 255) * na) / 255, b = ((c & 255) * a + (dv & 255) * na) / 255;
                d = 0xFF000000 | r << 16 | g << 8 | b;
            }
        }
    }
}
