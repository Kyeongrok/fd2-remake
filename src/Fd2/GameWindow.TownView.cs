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

    /// <summary>이름 상자 FDOTHER 10.</summary>
    private (uint[] Px, int W, int H)? TownLabel()
    {
        if (_townLabel != null) return _townLabel;
        try { return _townLabel = Pcx(Dat.Load(Path.Combine(_dir, "FDOTHER.DAT"), 10), 0); }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>at 에서 u16 w · h 뒤 날 점 w × h 바이트, 0 은 비침 — 원작 sub_4ED4F(건물 안 단추).</summary>
    private (uint[] Px, int W, int H) RawPicture(byte[] e, int at)
    {
        int w = Dat.U16(e, at), h = Dat.U16(e, at + 2);
        var px = new uint[w * h];
        for (int i = 0; i < px.Length && at + 4 + i < e.Length; i++) { byte b = e[at + 4 + i]; px[i] = b == 0 ? 0 : _pal[b] | 0xFF000000; }
        return (px, w, h);
    }

    /// <summary>at 에서 u16 w · h 뒤 PCX 꼴(0xC0 넘으면 다음 바이트를 (b − 0xC0) 번, 아니면 그 색 한 점 — 원작 sub_4EBFF).</summary>
    private (uint[] Px, int W, int H) Pcx(byte[] e, int at)
    {
        int w = Dat.U16(e, at), h = Dat.U16(e, at + 2), p = at + 4, d = 0;
        var px = new uint[w * h];
        while (d < px.Length && p < e.Length)
        {
            byte b = e[p++];
            if (b > 0xC0) { int run = b - 0xC0; uint c = p < e.Length ? _pal[e[p++]] | 0xFF000000 : 0; for (int k = 0; k < run && d < px.Length; k++) px[d++] = c; }
            else px[d++] = _pal[b] | 0xFF000000;
        }
        return (px, w, h);
    }

    /// <summary>건물 안 그림 묶음(원작 sub_279BC · sub_29300 · sub_29DAA) — n5 0 주점 13 · 1 무기점 12 · 3 도구점 29 · 4 교회 14 · 5 비밀상점 63, 2(출구)는 없음.</summary>
    private static int InsideBundle(int n5) => n5 switch { 0 => 13, 1 => 12, 3 => 29, 4 => 14, 5 => 63, _ => -1 };

    private sealed record InsideArt(uint[] Back, (uint[] Px, int W, int H)? Gold, List<(uint[] Px, int W, int H)> Icons);

    private readonly Dictionary<int, InsideArt?> _insidePics = new();

    /// <summary>
    /// 건물 안 — 묶음의 0번(320x200 가게 주인 그림), 1번(돈 상자 63x15 PCX, (5, 95)), 3~10번(네 단추 24x20 — 2k+3 안 고름 · 2k+4 고름).
    /// </summary>
    private InsideArt? InsidePicture(int n5)
    {
        int bundle = InsideBundle(n5);
        if (bundle < 0) return null;
        if (_insidePics.TryGetValue(bundle, out var got)) return got;
        try
        {
            var e = Dat.Load(Path.Combine(_dir, "FDOTHER.DAT"), bundle);
            (uint[] Px, int W, int H) Part(int k)
            {
                int o = Dat.U32(e, 6 + 4 * k), w = Dat.U16(e, o), h = Dat.U16(e, o + 2);
                return (Rle.Decode(e, o + 4, w, h, _pal), w, h);
            }
            var back = Part(0);
            int g = Dat.U32(e, 10);   // 1번(돈 상자)은 PCX 꼴 — 원작도 sub_4EBFF 로 그린다
            got = back.W == AfmPlayer.Width && back.H == AfmPlayer.Height
                ? new InsideArt(back.Px, Pcx(e, g), Enumerable.Range(3, 8).Select(k => RawPicture(e, Dat.U32(e, 6 + 4 * k))).ToList()) : null;
        }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { got = null; }
        return _insidePics[bundle] = got;
    }

    /// <summary>건물 안 그림 자리(화면 점) · 배율 — 단추 · 말 창을 그 위에 맞춘다.</summary>
    private (int X, int Y, double S) _insideAt;

    /// <summary>건물 안 첫 메뉴가 원작 네 단추(오른쪽 아래)로 떠 있나 — 상점 사기 · 팔기 · 정보 · 장비, 주점 동료 · 기록 · 불러오기 · 이탈, 교회 동료 · 장비 · 부활 · 전직.</summary>
    private bool _townButtons;

    /// <summary>주인 말 — FDTXT 줄(들어오면 무기점 501 · 그 밖 440 · 주점 · 교회 585, 볼일 뒤 503 · 440 · 586).</summary>
    private int _townGreeting = -1;

    /// <summary>건물 주인 얼굴(DATO, 표 0x52387) — 주점 129 · 무기점 128 · 도구점 130 · 교회 131 · 비밀상점 132.</summary>
    private static int KeeperFace(int n5) => n5 switch { 0 => 129, 1 => 128, 3 => 130, 4 => 131, 5 => 132, _ => -1 };

    /// <summary>네 단추 자리(그림 점) — (240 + {−39, −13, 13, 39}, 169), 표 0x52408.</summary>
    private static readonly int[] ButtonDx = [-39, -13, 13, 39];

    private void DrawTownView()
    {
        if (!InTownView || _campaign is not { } c || _tables?.ShopFor(c.NextScene) is not { } shop) return;
        if (_townTop && _stage == MenuStage.Town) _townSpot = Math.Clamp(_menuIndex - 1, 0, 5);
        // 건물 안이면 가게 주인 그림과 돈 상자(원작처럼 왼쪽 가운데)
        if (!_townTop && InsidePicture(_townSpot) is { } inside)
        {
            FillRect(0, 0, _clientW, _clientH, 0xFF000000);
            double si = Math.Min(_clientW / (double)AfmPlayer.Width, _clientH / (double)AfmPlayer.Height);
            int iw = (int)(AfmPlayer.Width * si), ih = (int)(AfmPlayer.Height * si), ix = (_clientW - iw) / 2, iy = (_clientH - ih) / 2;
            _insideAt = (ix, iy, si);
            BlitFit(inside.Back, AfmPlayer.Width, AfmPlayer.Height, ix, iy, iw, ih);
            if (inside.Gold is { } gold)
            {
                int gx = ix + (int)(5 * si), gy = iy + (int)(95 * si);
                BlitFit(gold.Px, gold.W, gold.H, gx, gy, (int)(gold.W * si), (int)(gold.H * si));
                var money = Text.Render($"{c.Gold}", Math.Max(12, (int)(9 * si)), 0xFFFFE070);
                DrawText(money, gx + (int)(gold.W * si) - money.W - (int)(4 * si), gy + ((int)(gold.H * si) - money.H) / 2);
            }
            if (_townButtons) DrawKeeperTalk();
            return;
        }
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

    /// <summary>주인 말 창(원작 sub_15F84 — (12, 119) 에 얼굴과 글) — 단추 왼쪽 아래 반투명 창.</summary>
    private void DrawKeeperTalk()
    {
        var (ix, iy, si) = _insideAt;
        int X(int v) => ix + (int)(v * si);
        int Y(int v) => iy + (int)(v * si);
        int bx = X(8), by = Y(119), bw = X(196) - bx, bh = Y(165) - by;
        FillRect(bx, by, bw, bh, 0xC0101830);
        FillRect(bx, by, bw, Math.Max(1, (int)si), 0xFF8090C0);
        FillRect(bx, by + bh - Math.Max(1, (int)si), bw, Math.Max(1, (int)si), 0xFF8090C0);
        int tx = bx + (int)(6 * si);
        if (KeeperFace(_townSpot) is int face and >= 0 && Face(face) is { Count: > 0 } f)
        {
            int fh = bh - (int)(6 * si), fw = f[0].W * fh / Math.Max(1, f[0].H);
            int k = (int)(_time / 0.25) % Math.Max(1, f.Count);   // 말하는 동안처럼 장을 돌린다
            BlitFit(f[k].Px, f[k].W, f[k].H, bx + (int)(3 * si), by + (int)(3 * si), fw, fh);
            tx = bx + (int)(3 * si) + fw + (int)(6 * si);
        }
        if (_townGreeting >= 0)
        {
            int g = Math.Max(1, (int)si);
            var line = NameImage(_townGreeting, g, 0xFFFFFFFF);
            while (line != null && g > 1 && tx + line.W > bx + bw - 4) line = NameImage(_townGreeting, --g, 0xFFFFFFFF);
            if (line != null) DrawText(line, tx, by + (int)(8 * si));
        }
    }

    /// <summary>네 단추(원작 sub_26EDA) — 메뉴 줄 i 가 단추 i. 고른 것은 2k+4 그림, 위에 이름. 누를 자리는 단추 그림.</summary>
    private void DrawTownButtons(int fs)
    {
        if (InsidePicture(_townSpot) is not { } inside) return;
        var (ix, iy, si) = _insideAt;
        for (int i = 0; i < _menu.Count && i < 4; i++)
        {
            var (px, w, h) = inside.Icons[Math.Min(inside.Icons.Count - 1, 2 * i + (i == _menuIndex ? 1 : 0))];
            int x = ix + (int)((240 + ButtonDx[i]) * si), y = iy + (int)(169 * si), dw = (int)(w * si), dh = (int)(h * si);
            if (_mouseMoved && !_menuDragging && _mouseX >= x && _mouseY >= y && _mouseX < x + dw && _mouseY < y + dh) _menuIndex = i;
            BlitFit(px, w, h, x, y, dw, dh);
            _menu[i] = (_menu[i].Label, _menu[i].Do, (x, y, dw, dh));
        }
        if (_menuIndex is >= 0 and < 4 && _menuIndex < _menu.Count)
        {
            var label = Text.Render(_menu[_menuIndex].Label, fs, 0xFFFFE070);
            int lx = ix + (int)(240 * si) - label.W / 2, ly = iy + (int)(165 * si) - label.H - 4;
            FillRect(lx - 8, ly - 4, label.W + 16, label.H + 8, 0xC0101830);
            DrawText(label, lx, ly);
        }
    }
}
