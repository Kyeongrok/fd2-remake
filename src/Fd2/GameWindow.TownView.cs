using Fd2.Data;

namespace Fd2;

/// <summary>
/// 마을 그림(원작 sub_26152 · sub_265EC, 사용자 요청) — 마을 꼴(기록 [0] = 0 · 1 · 2)마다 FDOTHER 11 · 61 · 62(표 0x52405) 의 320x200 그림을 깔고,
/// 고른 건물(n5 — 0 주점 · 1 무기점 · 2 출구 · 3 도구점 · 4 교회 · 5 ???) 앞에 앞장선 사람(FDICON, 0 · 1 · 2 · 1 장을 4틱마다)을 세운다(자리 표 x 0x52363 · y 0x52375, 꼴마다 6칸).
/// 오른쪽 아래(244, 162)에 이름 상자(FDOTHER 10, 62x26)와 건물 이름(FDTXT 495 + n5). 원작은 312x192 만 (4, 4) 에 보인다 — 여기서는 화면에 맞게 키운다.
/// 메뉴는 그 위 오른쪽에 그대로 뜬다.
/// </summary>
internal sealed partial class GameWindow
{
    private readonly Dictionary<int, uint[]?> _townPics = new();
    private (uint[] Px, int W, int H)? _townLabel;

    /// <summary>그림 위 건물 — 첫 메뉴에서는 고른 줄, 건물 안에서는 들어간 건물.</summary>
    private int _townSpot;

    private bool InTownView => _town != null && _screen == Screen.Game && _outcome == 2 && _bs is BattleState.Menu or BattleState.Banner;

    private uint[]? TownPicture(int kind)
    {
        if (_townPics.TryGetValue(kind, out var px)) return px;
        try
        {
            int entry = _tables?.TownPicture(kind) ?? -1;
            if (entry < 0) return _townPics[kind] = null;
            var e = Dat.Load(Path.Combine(_dir, "FDOTHER.DAT"), entry);
            int w = Dat.U16(e, 0), h = Dat.U16(e, 2);
            px = w == AfmPlayer.Width && h == AfmPlayer.Height ? Rle.Decode(e, 4, w, h, _pal) : null;
        }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { px = null; }
        return _townPics[kind] = px;
    }

    /// <summary>이름 상자 FDOTHER 10 — u16 w · h 뒤 PCX 꼴(0xC0 넘으면 다음 바이트를 (b − 0xC0) 번).</summary>
    private (uint[] Px, int W, int H)? TownLabel()
    {
        if (_townLabel != null) return _townLabel;
        try
        {
            var e = Dat.Load(Path.Combine(_dir, "FDOTHER.DAT"), 10);
            int w = Dat.U16(e, 0), h = Dat.U16(e, 2), p = 4, d = 0;
            var px = new uint[w * h];
            while (d < px.Length && p < e.Length)
            {
                byte b = e[p++];
                if (b > 0xC0) { int run = b - 0xC0; uint c = p < e.Length ? _pal[e[p++]] | 0xFF000000 : 0; for (int k = 0; k < run && d < px.Length; k++) px[d++] = c; }
                else px[d++] = _pal[b] | 0xFF000000;
            }
            return _townLabel = (px, w, h);
        }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    private void DrawTownView()
    {
        if (!InTownView || _campaign is not { } c || _tables?.ShopFor(c.NextScene) is not { } shop) return;
        if (_townTop && _stage == MenuStage.Town) _townSpot = Math.Clamp(_menuIndex - 1, 0, 5);
        var pic = TownPicture(shop.Town);
        FillRect(0, 0, _clientW, _clientH, 0xFF000000);
        if (pic == null) return;
        double s = Math.Min(_clientW / (double)AfmPlayer.Width, _clientH / (double)AfmPlayer.Height);
        int pw = (int)(AfmPlayer.Width * s), ph = (int)(AfmPlayer.Height * s), px = (_clientW - pw) / 2, py = (_clientH - ph) / 2;
        BlitFit(pic, AfmPlayer.Width, AfmPlayer.Height, px, py, pw, ph);
        int X(int v) => px + (int)(v * s);
        int Y(int v) => py + (int)(v * s);
        // 앞장선 사람 — 고른 건물 앞(원작은 0 · 1 · 2 · 1 장을 돌린다)
        if (_scene.Units.Where(u => u.Side == Side.Player && u.Hp > 0).OrderBy(u => u.Slot).FirstOrDefault() is { } lead)
        {
            var (tx, ty) = _tables.TownSpot(shop.Town, _townSpot);
            int frame = (int)(_time / 0.22) % 4;
            var icon = _icons.Frame(lead.Icon, (int)Dir.Down * 3 + (frame == 3 ? 1 : frame));
            BlitFit(icon, T, T, X(tx), Y(ty), (int)(T * s), (int)(T * s));
        }
        // 이름 상자와 건물 이름
        if (TownLabel() is { } label)
        {
            BlitFit(label.Px, label.W, label.H, X(244), Y(162), (int)(label.W * s), (int)(label.H * s));
            int g = Math.Max(1, (int)s);
            if (NameImage(495 + _townSpot, g, 0xFFFFFFFF) is { } name)
                DrawText(name, X(244) + ((int)(label.W * s) - name.W) / 2, Y(162) + ((int)(label.H * s) - name.H) / 2);
        }
    }
}
