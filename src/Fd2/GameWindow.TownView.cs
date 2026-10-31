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

    private sealed record InsideArt(uint[] Back, (uint[] Px, int W, int H)? Gold, List<(uint[] Px, int W, int H)> Icons, List<(uint[] Px, int W, int H)> Deal);

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
                ? new InsideArt(back.Px, Pcx(e, g), Enumerable.Range(3, 8).Select(k => RawPicture(e, Dat.U32(e, 6 + 4 * k))).ToList(),
                                Enumerable.Range(23, Math.Max(0, (Dat.U32(e, 6) - 6) / 4 - 1 - 23)).Select(Part).ToList()) : null;   // 마지막 칸은 끝 표시
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
        if (DrawDoorZoom(shop)) return;   // 들어가고 나올 때 — 그 건물 쪽으로 다가가며 키우고 어두워진다
        // 건물 안이면 가게 주인 그림 · 주인 얼굴 · 거래 움직임 · 돈 상자 · 말 창
        if (!_townTop && InsidePicture(_townSpot) is { } inside)
        {
            FillRect(0, 0, _clientW, _clientH, 0xFF000000);
            double si = Math.Min(_clientW / (double)AfmPlayer.Width, _clientH / (double)AfmPlayer.Height);
            int iw = (int)(AfmPlayer.Width * si), ih = (int)(AfmPlayer.Height * si), ix = (_clientW - iw) / 2, iy = (_clientH - ih) / 2;
            _insideAt = (ix, iy, si);
            BlitFit(inside.Back, AfmPlayer.Width, AfmPlayer.Height, ix, iy, iw, ih);
            DrawKeeperFace(inside);
            if (inside.Gold is { } gold)
            {
                int gx = ix + (int)(5 * si), gy = iy + (int)(95 * si);
                BlitFit(gold.Px, gold.W, gold.H, gx, gy, (int)(gold.W * si), (int)(gold.H * si));
                var money = Text.Render($"{c.Gold}", Math.Max(12, (int)(9 * si)), 0xFFFFE070);
                DrawText(money, gx + (int)(gold.W * si) - money.W - (int)(4 * si), gy + ((int)(gold.H * si) - money.H) / 2);
            }
            DrawKeeperTalk();
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

    /// <summary>주인 얼굴 자리(그림 점, 원작 sub_1956B 의 n1832) — DATO 얼굴을 가게 그림 위 얼굴에 겹쳐 말하기 · 깜빡임.</summary>
    private static (int X, int Y)? KeeperFacePos(int n5) => n5 switch
    {
        0 => (107, 5), 1 => (123, 13), 3 => (99, 12), 4 => (118, 4), 5 => (124, 11), _ => null,
    };

    /// <summary>거래 뒤 움직임 자리(원작 sub_28B41) — 무기점 (169, 45) · 도구점 (148, 39) · 교회 (147, 32) · 비밀상점 (131, 28). 주점은 없다.</summary>
    private static (int X, int Y)? DealPos(int n5) => n5 switch { 1 => (169, 45), 3 => (148, 39), 4 => (147, 32), 5 => (131, 28), _ => null };

    private double _talkAt = -1, _dealAt = -1, _nextBlink;
    private int _talkedLine = -2;

    /// <summary>사고팔기 · 부활이 끝나면 — 주인 움직임(원작 sub_28B41).</summary>
    private void StartDeal() => _dealAt = _time;

    /// <summary>원작 마을 화면의 2틱(0.11초) — 깜빡임 · 입 · 거래 움직임의 한 걸음.</summary>
    private const double TownTick = 0.055 * 2;

    private void DrawKeeperFace(InsideArt inside)
    {
        var (ix, iy, si) = _insideAt;
        if (_townGreeting != _talkedLine) { _talkedLine = _townGreeting; _talkAt = _time; }   // 새 말 — 입을 움직인다
        int frame = 0;
        double dealT = _dealAt < 0 ? -1 : _time - _dealAt;
        int dealFrames = _townSpot == 3 ? 1 : inside.Deal.Count;
        double dealLen = _townSpot == 3 ? 9 * 0.055 : dealFrames * TownTick + (_townSpot == 4 ? 0.9 : 0);
        if (dealT >= dealLen) { _dealAt = -1; dealT = -1; }
        if (dealT >= 0 && _townSpot == 4) frame = 3;                                       // 교회 — 눈을 감고 빈다
        else if (_time - _talkAt < 1.2) frame = (int)((_time - _talkAt) / TownTick) % 3;   // 말하는 동안 0 · 1 · 2
        else if (_time >= _nextBlink)
        {
            // 깜빡임(sub_26EDA) — 2틱 감고, 다음은 (난수 % 30 + 2) × 2틱 뒤
            frame = 3;
            if (_time >= _nextBlink + TownTick) _nextBlink = _time + (Random.Shared.Next(30) + 2) * TownTick;
        }
        if (KeeperFace(_townSpot) is int face and >= 0 && KeeperFacePos(_townSpot) is { } fp && Face(face) is { Count: > 0 } f)
        {
            var (px, w, h) = f[Math.Min(frame, f.Count - 1)];
            BlitFit(px, w, h, ix + (int)(fp.X * si), iy + (int)(fp.Y * si), (int)(w * si), (int)(h * si));
        }
        // 거래 움직임 — 묶음 23번부터 2틱마다(도구점은 한 장을 9틱)
        if (dealT >= 0 && DealPos(_townSpot) is { } dp && inside.Deal.Count > 0)
        {
            int k = Math.Min(dealFrames - 1, (int)(dealT / TownTick));
            if (dealT < dealFrames * TownTick || _townSpot == 3)
            {
                var (px, w, h) = inside.Deal[k];
                BlitFit(px, w, h, ix + (int)(dp.X * si), iy + (int)(dp.Y * si), (int)(w * si), (int)(h * si));
            }
            else if (_townSpot == 4)
            {
                // 교회 부활 — 하얗게 밝아졌다가 돌아온다(팔레트 0~62, 4ms 마다 2 씩)
                double t = dealT - dealFrames * TownTick, up = 0.15, hold = 0.55;
                double a = t < up ? t / up : t < up + hold ? 1 : Math.Max(0, 1 - (t - up - hold) / up);
                FillRect(ix, iy, (int)(AfmPlayer.Width * si), (int)(AfmPlayer.Height * si), (uint)(a * 230) << 24 | 0xFFFFFF);
            }
        }
    }

    /// <summary>말 창(원작 sub_1956B · sub_168B6) — (5, 112) 에 304 × 80 창, 글은 (12, 119) 부터(sub_15F84). 네 단추도 이 창 오른쪽 아래에 앉는다.</summary>
    private void DrawKeeperTalk()
    {
        if (!_townButtons) return;   // 목록(사기 · 팔기 …)이 떠 있으면 말 창은 숨긴다 — 목록만 보이게(사용자 요청)
        var (ix, iy, si) = _insideAt;
        int bx = ix + (int)(5 * si), by = iy + (int)(112 * si), bw = (int)(304 * si), bh = (int)(80 * si), line = Math.Max(1, (int)si);
        FillRect(bx, by, bw, bh, 0xE028458A);
        FillRect(bx, by, bw, line * 2, 0xFF8090C0); FillRect(bx, by + bh - line * 2, bw, line * 2, 0xFF8090C0);
        FillRect(bx, by, line * 2, bh, 0xFF8090C0); FillRect(bx + bw - line * 2, by, line * 2, bh, 0xFF8090C0);
        if (!_townButtons || _townGreeting < 0) return;
        int g = Math.Max(1, (int)si);
        var text = NameImage(_townGreeting, g, 0xFFFFFFFF);
        if (text == null) return;
        // 한 자씩(말하는 동안) — 글 그림을 왼쪽부터 드러낸다
        double shown = Math.Clamp((_time - _talkAt) / 1.0, 0, 1);
        int w = (int)(text.W * shown);
        if (w <= 0) return;
        var part = new Text.Image(new uint[w * text.H], w, text.H);
        for (int y = 0; y < text.H; y++) Array.Copy(text.Px, y * text.W, part.Px, y * w, w);
        DrawText(part, ix + (int)(12 * si), iy + (int)(119 * si));
    }

    /// <summary>네 단추(원작 sub_26EDA) — 메뉴 줄 i 가 단추 i. 고른 것은 2k+4 그림, 위에 이름. 누를 자리는 단추 그림.</summary>
    private void DrawTownButtons(int fs)
    {
        if (InsidePicture(_townSpot) is not { } inside) return;
        var (ix, iy, si) = _insideAt;
        for (int i = 0; i < _menu.Count && i < 4; i++)
        {
            bool lit = i == _menuIndex && (int)(_time / (2 * TownTick)) % 2 == 0;   // 고른 단추는 두 그림을 번갈아(원작 sub_27079)
            var (px, w, h) = inside.Icons[Math.Min(inside.Icons.Count - 1, 2 * i + (lit ? 1 : 0))];
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

    /// <summary>들어가기 · 나오기(원작 sub_2670E · 각 건물 끝) — 10걸음에 그 건물 자리로 다가가며 배율 128 / (128 − 9n), 밝기를 줄인다.</summary>
    private double _doorAt = -1;
    private bool _doorIn;
    private int _doorSpot;
    private bool _wasTop = true;

    private const double DoorTime = 10 * 0.055;

    private bool DrawDoorZoom(ShopInfo shop)
    {
        // 첫 메뉴 ↔ 건물 안이 바뀌면 시작(출구 · 건물 없는 줄은 아님)
        if (_wasTop != _townTop && _stage == MenuStage.Town)
        {
            if (InsideBundle(_townSpot) >= 0) (_doorAt, _doorIn, _doorSpot) = (_time, !_townTop, _townSpot);
            _wasTop = _townTop;
        }
        if (_doorAt < 0) return false;
        double t = (_time - _doorAt) / DoorTime;
        if (t >= 1) { _doorAt = -1; return false; }
        var pic = TownPicture(shop.Town);
        if (pic == null) return false;
        double n = (_doorIn ? t : 1 - t) * 10;                      // 0 → 10 들어감, 10 → 0 나옴
        double zoom = 128.0 / (128 - 9 * n);
        var (spotX, spotY) = _tables!.TownSpot(shop.Town, _doorSpot);
        double cx = 160 + n * (spotX + 12 - 160) / 10, cy = 100 + n * (spotY + 12 - 100) / 10;
        FillRect(0, 0, _clientW, _clientH, 0xFF000000);
        double s = Math.Min(_clientW / (double)AfmPlayer.Width, _clientH / (double)AfmPlayer.Height);
        int pw = (int)(AfmPlayer.Width * s), ph = (int)(AfmPlayer.Height * s), px = (_clientW - pw) / 2, py = (_clientH - ph) / 2;
        uint keep = (uint)(255 - (int)(255 * n / 10));
        for (int y = Math.Max(0, py); y < Math.Min(_fbH, py + ph); y++)
        {
            int sy = (int)(cy + ((y - py) / s - 100) / zoom);
            if ((uint)sy >= AfmPlayer.Height) continue;
            for (int x = Math.Max(0, px); x < Math.Min(_fbW, px + pw); x++)
            {
                int sx = (int)(cx + ((x - px) / s - 160) / zoom);
                if ((uint)sx >= AfmPlayer.Width) continue;
                uint c = pic[sy * AfmPlayer.Width + sx];
                _fb[y * _fbW + x] = 0xFF000000 | ((c >> 16 & 255) * keep / 255) << 16 | ((c >> 8 & 255) * keep / 255) << 8 | (c & 255) * keep / 255;
            }
        }
        return true;
    }
}
