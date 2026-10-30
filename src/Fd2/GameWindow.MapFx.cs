using Fd2.Data;

namespace Fd2;

/// <summary>
/// 지도의 움직이는 것(분석-지도 · 분석-자료형식 §6) — 지도 텍스처(처음 한 장) 위에 칸을 덧그린다.
/// <list type="bullet">
/// <item>덮개(타일 속성 0x80, sub_12AC6): 다음 타일(+1)을 사람 위에 한 번 더 — 지붕 · 나무 꼭대기.</item>
/// <item>두 장 번갈아(0x04 → +1, 0x08 → +2, sub_11EEE): 시계 틱마다 뒤집히는 dword_53A40.</item>
/// <item>물결 색(sub_4E31C): BIOS 틱 2 마다 단계 +1(16 단계), 색 224~239 = EXE 표[단계 .. 단계+15].</item>
/// </list>
/// 고해상도 지도는 칸 그림이 따로 없어 덮개 · 연 상자만 낮은 해상도 그림으로 얹고, 번갈아 · 물결은 그리지 않는다.
/// 0x10(여러 장 돌기)은 세는 값(dword_53C0B)을 바꾸는 곳을 못 찾아 그대로 둔다.
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
                if ((f & 0x0C) != 0 || WaterVariants(t) != null || WaterVariants(t + ((f & 0x08) != 0 ? 2 : 1)) != null) _liveCells.Add(i);
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
