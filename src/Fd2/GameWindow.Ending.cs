using Fd2.Data;
using Fd2.Native;

namespace Fd2;

/// <summary>
/// 엔딩 — 30장(장면 29)을 이기면 승리 뒤 함수(sub_25757)가 대사 9 · 대본 88 · 대사 10 뒤 ++n17 → sub_1088D(30) 으로 끝 장면(장면 30, 지도는 11장 것)을 올리고
/// 그 지도 위에서 대사 0 · 화면 (11,12) · 대본 89 · 대사 1, 이어서 끝 연출 sub_31529 를 튼다. 판정 메뉴의 「끝」이 이것을 시작한다.
/// 27장(장면 26)을 이기고 열쇠(물건 100)가 없으면 승리 뒤 함수가 바로 sub_31529 로 간다(다른 끝 — n17 = 26 이라 대사가 장면 26 의 17 ~ 23).
/// sub_31529 는 지도를 안 쓰고 320x200 한 장에 그린다 — 여기서도 그 한 장(_ef)에 원작 차례대로 그려 창 가운데에 키워 놓는다.
/// Enter · 클릭 = 다음(대사 넘기기 · 지금 컷 건너뛰기), Esc = 「THE END」 로 건너뛰기.
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>엔딩 연출 한 컷 — Len 초 동안 Draw(t), 대사가 있으면(Group ≥ 0) 대사가 끝날 때까지(얼굴 Face 는 sub_31BDF 의 DATO 번호).</summary>
    private sealed record Shot(double Len, Action<double> Draw, Action? Enter = null, int Group = -1, int Face = -1, int TextScene = Campaign.FinalScene);

    /// <summary>
    /// 이긴 뒤 엔딩이 기다리나 — 0 없음, 1 끝(장면 29), 2 다른 끝(장면 26, 열쇠 없음). 1 이면 _endingOps 는 sub_1088D 뒤 승리 함수의 나머지(끝 장면 지도 연출).
    /// </summary>
    private int _endingKind;
    private System.Text.Json.JsonElement[]? _endingOps;

    /// <summary>끝 장면 지도 연출(대사 0 · 대본 89 · 대사 1) 중 — 그때 만나는 sub_31529 는 바로 끝 연출로.</summary>
    private bool _endingPrelude;

    /// <summary>승리 뒤 함수를 다 돌았을 때 할 일 — 안쪽 갈래(if)에서 sub_31529 를 만나도 바깥 대본을 이어 돌리지 않고 끝내려고.</summary>
    private Action? _victoryAfter;

    private const int EW = 320, EH = 200;

    /// <summary>BIOS 틱(sub_17AA9) 하나 — 1/18.2 초.</summary>
    private const double ETick = 1 / 18.2;

    /// <summary>팔레트 어둡게 · 밝게 64 단계(sub_1F882 · sub_1F525 는 2ms, 흰 빛에서 돌아오기는 4ms씩).</summary>
    private const double EFade = 64 * 0.002, EWhite = 64 * 0.004;

    private readonly uint[] _ef = new uint[EW * EH];

    /// <summary>이 컷의 밝기 — −1 검게 · 0 그대로 · +1 희게(원작 팔레트 6비트 값에서 빼고 더하기, sub_11D40 · sub_11DF2).</summary>
    private double _eLight;

    private List<Shot>? _shots;
    private int _shot, _theEnd;
    private double _shotT;
    private bool _endBad;

    // 끝 연출 자료 — FDOTHER 54(하늘 · 황금성 · 용 탄 둘, FIGANI 꼴) · 58(스태프 이름, 팔레트 57) · 56 · 59 · 60(320x200 RLE), ANI.DAT 셋째 편
    private Motion? _sky, _staff;
    private uint[]? _img56, _img59, _img60;
    private List<uint[]> _flash = new();
    private Dictionary<uint, uint>? _toPal57;
    private List<List<Dialogue.Page>>? _endText;

    // 끝 크레디트의 붙잡은 그림 — 싸움 연출 마지막 장을 팔레트 57 로 바꿔(파랗게) 이름 그림 밑에 깐다
    private readonly uint[] _frozen = new uint[EW * EH];

    /// <summary>
    /// sub_31529 의 표 셋(0x526E9 · 0x526FD · 0x52711) — 끝 크레디트 20 장마다 싸움 연출 한 판: 앞 사람(76 아래면 우리 편) · 뒷사람 · 전투 배경 BG 번호.
    /// 앞 사람이 치고 뒷사람이 되받아친다(sub_2E2B0(0,1), n3_9 = 배경).
    /// </summary>
    private static readonly byte[] DemoA = [51, 110, 19, 105, 54, 117, 30, 123, 39, 127, 64, 81, 52, 125, 26, 115, 41, 91, 31, 126];
    private static readonly byte[] DemoB = [103, 20, 83, 28, 124, 38, 93, 34, 112, 44, 86, 53, 80, 55, 120, 36, 106, 60, 122, 50];
    private static readonly byte[] DemoBg = [4, 3, 51, 14, 25, 18, 40, 53, 22, 24, 28, 17, 30, 31, 50, 33, 34, 52, 36, 47];

    /// <summary>판정 메뉴의 「끝」 — 끝 장면 지도 연출이 있으면 그것부터, 없으면 바로 끝 연출.</summary>
    private void StartEnding()
    {
        _menu.Clear();
        _endBad = _endingKind == 2;
        var ops = _endingOps;
        _endingOps = null;
        if (_endingKind == 1 && ops is { Length: > 0 } && _campaign is { } c && _sceneCount > Campaign.FinalScene)
        {
            // sub_1088D(30) — 끝 장면에 동료(쓰러지지 않은 사람)를 세우고, 원작처럼 곡은 30장 것을 그대로
            LoadScene(Campaign.FinalScene, c.Party.Where(r => !HeroRecord.Gone(r)).ToList());
            _screen = Screen.Game;
            _bs = BattleState.Talk;
            _endingPrelude = true;
            if (_scene.Units.Find(u => u.Side == Side.Player) is { } lead) { _cursor = (lead.X, lead.Y); _centerCursor = true; }
            RunOps(ops, 0, StartEndingFilm);
            return;
        }
        StartEndingFilm();
    }

    /// <summary>sub_31529 — 끝 연출을 컷 목록으로 세우고 튼다.</summary>
    private void StartEndingFilm()
    {
        _endingPrelude = false;
        _endingKind = 0;
        _talk.Clear();
        _afterTalk = null;
        _bs = BattleState.None;
        ClearSelection();
        LoadEndingData();
        _shots = EndingShots();
        _shot = -1;
        _screen = Screen.Ending;
        NextShot();
    }

    private void LoadEndingData()
    {
        string other = Path.Combine(_dir, "FDOTHER.DAT");
        uint[]? Image(int entry)
        {
            try
            {
                var e = Dat.Load(other, entry);
                return e.Length > 4 && Dat.U16(e, 0) == EW && Dat.U16(e, 2) == EH ? Rle.Decode(e, 4, EW, EH, _pal) : null;
            }
            catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { return null; }
        }
        _sky ??= Motion.LoadOther(_dir, 54, _pal);
        _img56 ??= Image(56);
        _img59 ??= Image(59);
        _img60 ??= Image(60);
        if (_toPal57 == null)
        {
            _toPal57 = new();
            try
            {
                var p57 = Data.Palette.FromVga(Dat.Load(other, 57));
                _staff = Motion.LoadOther(_dir, 58, p57);
                for (int i = 0; i < 256; i++) _toPal57.TryAdd(_pal[i], p57[i]);
            }
            catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { }
        }
        if (_flash.Count == 0)
        {
            try
            {
                var player = new AfmPlayer(_dir, 2);   // sub_20421(2, 100, 0) — 황금성 쪽 하늘에서 빛이 터져 하얘진다
                while (player.Next() is { } f) _flash.Add(f);
            }
            catch (IOException) { }
        }
        try { _endText ??= Dialogue.Load(_dir, Campaign.FinalScene); }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { _endText = []; }
        try
        {
            _names ??= new NameTable(_dir);
            _font ??= new Font16(_dir);
        }
        catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentOutOfRangeException) { }
    }

    // ── 컷 목록 ─────────────────────────────────────────────────────────────

    private List<Shot> EndingShots()
    {
        var s = new List<Shot>();
        int textScene = _endBad ? 26 : Campaign.FinalScene;
        void Talks(Action<double> draw, params (int Face, int Group)[] talks)
        {
            foreach (var (face, group) in talks) s.Add(new Shot(0, draw, Group: group, Face: face, TextScene: textScene));
        }

        // 1. 하늘(54 의 0번)이 밝아지고 1초, ANI 셋째 편(컷마다 100ms) — 빛이 터져 하얘진다
        s.Add(new Shot(EFade + 1.0, t => { Sky(); _eLight = t < EFade ? t / EFade - 1 : 0; }));
        if (_flash.Count > 0)
            s.Add(new Shot(_flash.Count * 0.1, t => Array.Copy(_flash[Math.Min(_flash.Count - 1, (int)(t / 0.1))], _ef, EW * EH)));
        // 2. 흰 빛에서 하늘과 황금성(9번)이 드러나고 2초, 넷이 황금성을 보며
        s.Add(new Shot(EWhite + 2.0, t => { Island(0); _eLight = t < EWhite ? 1 - t / EWhite : 0; }));
        if (_endBad) Talks(_ => Island(0), (4, 17));
        else Talks(_ => Island(0), (37, 2), (21, 3), (26, 4), (105, 5), (32, 6));
        // 3. 0.5초 뒤 세 번 번쩍이고, 황금성이 하늘빛으로 스러진다(12 ~ 108번을 20ms씩 겹쳐)
        const double Blink = EWhite + 0.2;
        s.Add(new Shot(0.5 + 3 * Blink, t =>
        {
            Island(0);
            double k = t - 0.5;
            _eLight = k >= 0 && k % Blink < EWhite ? 1 - k % Blink / EWhite : 0;
        }));
        s.Add(new Shot(97 * 0.02, t => Island(Math.Min(96, (int)(t / 0.02)) + 12)));
        if (_endBad) Talks(_ => Sky(), (21, 18), (24, 19), (26, 20));
        else Talks(_ => Sky(), (45, 7));
        // 4. 2초 뒤 용을 탄 둘(1 ~ 4번 오른쪽에서, 5 ~ 8번 왼쪽에서)이 40 그림 동안 날아와 만난다
        s.Add(new Shot(2.0, _ => Sky()));
        s.Add(new Shot(40 * 0.02, t =>
        {
            int n = Math.Min(39, (int)(t / 0.02));
            Riders(n % 4, 130 - 2 * n - 2 * Math.Min(n, 25), -80 + 2 * n);
        }));
        if (_endBad) Talks(_ => Riders(0, 0, 0), (32, 21), (36, 22), (32, 23));
        else Talks(_ => Riders(0, 0, 0), (32, 8), (36, 9));
        // 200 그림 날갯짓, 135 그림 뒤부터 한 단계씩 어두워진다
        s.Add(new Shot(200 * 0.02, t =>
        {
            int n = Math.Min(199, (int)(t / 0.02));
            Riders(n % 4, 0, 0);
            _eLight = -Math.Min(63, Math.Max(0, n - 135)) / 63.0;
        }));

        // 5. sub_31C49 — 시(장면 30 묶음 44)가 아래에서 올라온다(500 틱, 처음 · 끝은 어둡게)
        s.Add(new Shot(500 * ETick, t => Poem(Math.Min(499, (int)(t / ETick)))));
        // 동료마다(뒤에서부터, 차례 1 · 0 은 맞바꿔 유니가 맨 끝) — 서기 · 공격 모션, 얼굴 · 이름 · 직업 · 뒷이야기(묶음 12 + 캐릭터)
        if (_campaign is { } c)
        {
            int n = c.Party.Count;
            for (int i = n - 1; i >= 0; i--)
            {
                int slot = i >= 2 ? i : 1 - i;
                if (slot >= n) continue;
                AddMember(s, c.Party[slot], last: i == 0, first: i == n - 1);
            }
        }

        // 6. 곡을 끄고 50 틱, 「STAFF」(60) — 곡 18, 80 틱
        s.Add(new Shot(50 * ETick, _ => Clear(), Enter: () => SetMusic(-1)));
        s.Add(new Shot(EFade + 80 * ETick, t => { Put(_img60); _eLight = t < EFade ? t / EFade - 1 : 0; }, Enter: () => SetMusic(18)));
        s.Add(new Shot(EFade, t => { Put(_img60); _eLight = -t / EFade; }));
        // 7. 스태프 20 장 — 싸움 한 판(앞 사람이 치고 뒷사람이 되받아침) 뒤 그 장을 팔레트 57 로 바꾸고 20 틱, 이름 그림(58 의 n번) 78 틱
        for (int k = 0; k < DemoA.Length; k++) AddDemo(s, k);

        // 8. 50 틱 뒤 「THE END」(59) — 원작은 여기서 멈춘다. 누르면 타이틀로
        s.Add(new Shot(50 * ETick, _ => Clear()));
        _theEnd = s.Count - 1;
        s.Add(new Shot(double.PositiveInfinity, t => { Put(_img59); _eLight = t < EFade ? t / EFade - 1 : 0; }));
        return s;
    }

    /// <summary>동료 한 사람 — sub_31C49 의 한 바퀴.</summary>
    private void AddMember(List<Shot> s, byte[] r, bool last, bool first)
    {
        int form = CharacterNames.SpriteOf(HeroRecord.Form(r), _icons.RoyalsMoved), id = HeroRecord.Id(r), cls = r[32];
        var stand = StandOf(form);
        var attack = MotionOf(form, 1);
        // 서기 모션 20 틱(앞은 sub_2E9A8 이 밝힌다 — 짐작으로 한 번 밝아지기)
        s.Add(new Shot(EFade + 20 * ETick, t =>
        {
            Put(_img56);
            EMotion(stand, MotionFrameAt(stand, t, loop: true));
            _eLight = t < EFade ? t / EFade - 1 : 0;
        }, Enter: first ? () => SetMusic(4) : null));
        // 공격 모션 — 컷마다 머무름(+6) 틱
        if (attack != null)
            s.Add(new Shot(attack.Frames.Sum(f => f.Hold) * ETick, t => { Put(_img56); EMotion(attack, MotionFrameAt(attack, t, loop: false)); }));
        int len = last ? 440 : 220;
        s.Add(new Shot(len * ETick, t => Member(form, id, cls, stand, Math.Min(len - 1, (int)(t / ETick)), last)));
        s.Add(new Shot(EFade, t => { Member(form, id, cls, stand, len - 1, last); _eLight = -t / EFade; }));
    }

    /// <summary>스태프 한 장 — 싸움 연출 sub_2E2B0(0,1) 뒤 이름 그림.</summary>
    private void AddDemo(List<Shot> s, int k)
    {
        int a = CharacterNames.SpriteOf(DemoA[k], _icons.RoyalsMoved), b = CharacterNames.SpriteOf(DemoB[k], _icons.RoyalsMoved), bg = DemoBg[k];
        var hitA = MotionOf(a, 1);
        var hitB = MotionOf(b, 1);
        double lenA = (hitA?.Frames.Sum(f => f.Hold) ?? 10) * ETick, lenB = (hitB?.Frames.Sum(f => f.Hold) ?? 10) * ETick;
        // 둘이 서서 밝아지고(TAI 받침은 안 그린다 — 근사), 앞 사람이 치고, 뒷사람이 되받아친다
        s.Add(new Shot(EFade + 0.4, t => { Duel(bg, a, b, null, 0, t); _eLight = t < EFade ? t / EFade - 1 : 0; }));
        s.Add(new Shot(lenA + 0.3, t => Duel(bg, a, b, hitA, MotionFrameAt(hitA, t, loop: false), t)));
        s.Add(new Shot(lenB + 0.3, t => Duel(bg, b, a, hitB, MotionFrameAt(hitB, t, loop: false), t),
            Enter: null));
        // 마지막 장을 붙잡아 팔레트 57 로(파랗게) — 20 틱 뒤 이름 그림 78 틱, 어두워지며 다음 장
        s.Add(new Shot(20 * ETick, _ => Frozen(-1), Enter: () => Freeze(bg, b, a, hitB)));
        s.Add(new Shot(78 * ETick, _ => Frozen(k)));
        s.Add(new Shot(EFade, t => { Frozen(k); _eLight = -t / EFade; }));
    }

    // ── 돌리기 ──────────────────────────────────────────────────────────────

    private void NextShot()
    {
        if (_shots == null) return;
        if (++_shot >= _shots.Count) { EndingToTitle(); return; }
        _shotT = 0;
        var shot = _shots[_shot];
        shot.Enter?.Invoke();
        if (shot.Group >= 0) EndingTalk(shot.TextScene, shot.Group, shot.Face, NextShot);
    }

    /// <summary>sub_31BDF — 얼굴 DATO face 를 붙여 묶음 group 을 보인다(그 장면 대사에는 말하는 사람 표시가 없다).</summary>
    private void EndingTalk(int scene, int group, int face, Action after)
    {
        StartTalk(scene, [group], after);
        foreach (var p in _talk)
        {
            p.Speaker = face;
            p.Name = face < CharacterNames.Heroes ? CharacterNames.Of(face).Split('-')[0] : "";
        }
    }

    private void EndingTick(double dt)
    {
        if (_shots == null || _shot < 0 || _shot >= _shots.Count) return;
        _shotT += dt;
        var shot = _shots[_shot];
        if (shot.Group >= 0) { TalkTick(dt); return; }
        if (_shotT >= shot.Len) NextShot();
    }

    /// <summary>Enter · 클릭 — 대사면 넘기고, 아니면 이 컷을 건너뛴다.</summary>
    private void EndingNext()
    {
        if (_shots == null) return;
        if (Talking) { TalkNext(); return; }
        NextShot();
    }

    /// <summary>Esc — 「THE END」 로.</summary>
    private void SkipEnding()
    {
        if (_shots == null || _shot >= _theEnd) { EndingToTitle(); return; }
        _talk.Clear();
        _afterTalk = null;
        _shot = _theEnd - 1;
        NextShot();
    }

    private void EndingToTitle()
    {
        _shots = null;
        _talk.Clear();
        _afterTalk = null;
        SetMusic(-1);
        ToTitle();
    }

    private bool EndingKey(int vk)
    {
        if (_screen != Screen.Ending) return false;
        if (TalkAdvanceKey(vk)) EndingNext();
        else if (vk == Win32.VK_ESCAPE) SkipEnding();
        return true;
    }

    // ── 그리기(320x200 한 장) ──────────────────────────────────────────────

    private void Clear() => Array.Fill(_ef, 0xFF000000);

    private void Put(uint[]? img)
    {
        if (img == null) { Clear(); return; }
        for (int i = 0; i < _ef.Length; i++) _ef[i] = img[i] == 0 ? 0xFF000000 : img[i];
    }

    /// <summary>w x h 그림을 (x, y) 에 — 0 은 비침.</summary>
    private void EBlit(uint[] px, int w, int h, int x, int y)
    {
        for (int yy = Math.Max(0, -y); yy < h && y + yy < EH; yy++)
            for (int xx = Math.Max(0, -x); xx < w && x + xx < EW; xx++)
            {
                uint c = px[yy * w + xx];
                if (c != 0) _ef[(y + yy) * EW + x + xx] = c | 0xFF000000;
            }
    }

    /// <summary>모션 컷 하나를 제 자리(+ dx)에 — sub_2EB9F.</summary>
    private void EMotion(Motion? m, int i, int dx = 0)
    {
        if (m == null || i < 0 || i >= m.Frames.Count) return;
        var f = m.Frames[i];
        EBlit(f.Pixels, f.Width, f.Height, f.X + dx, f.Y);
    }

    /// <summary>t 초에 보일 컷 — 컷마다 머무름 틱만큼.</summary>
    private static int MotionFrameAt(Motion? m, double t, bool loop)
    {
        if (m == null || m.Frames.Count == 0) return -1;
        int ticks = (int)(t / ETick), total = m.Frames.Sum(f => f.Hold);
        if (loop && total > 0) ticks %= total;
        for (int i = 0; i < m.Frames.Count; i++)
        {
            if (ticks < m.Frames[i].Hold) return i;
            ticks -= m.Frames[i].Hold;
        }
        return m.Frames.Count - 1;
    }

    private void Sky()
    {
        Clear();
        EMotion(_sky, 0);
    }

    /// <summary>하늘 + 황금성(9번), upTo ≥ 12 면 12 ~ upTo 번을 차례로 겹친다(원작은 화면에 그대로 덧그린다).</summary>
    private void Island(int upTo)
    {
        Sky();
        EMotion(_sky, 9);
        for (int k = 12; k <= upTo; k++) EMotion(_sky, k);
    }

    /// <summary>용 탄 둘 — 1 ~ 4번(오른쪽)을 dxA, 5 ~ 8번(왼쪽)을 dxB 만큼 밀어(원작은 640 폭 버퍼의 160 부터를 보인다).</summary>
    private void Riders(int beat, int dxA, int dxB)
    {
        Sky();
        EMotion(_sky, beat + 1, dxA);
        EMotion(_sky, beat + 5, dxB);
    }

    /// <summary>원본 글꼴 줄 — 205 번 색, 76 번 색 그림자를 한 점 아래오른쪽에. limit 글자까지.</summary>
    private int EGlyphs(IEnumerable<int> codes, int x, int y, int limit)
    {
        if (_font == null) return limit;
        uint ink = _pal[205] | 0xFF000000, shade = _pal[76] | 0xFF000000;
        foreach (int c in codes)
        {
            if (limit-- <= 0) break;
            if (_font.Has(c))
                for (int pass = 0; pass < 2; pass++)
                    for (int yy = 0; yy < Font16.Size; yy++)
                        for (int xx = 0; xx < Font16.Size; xx++)
                        {
                            if (!_font.Pixel(c, xx, yy)) continue;
                            int px = x + xx + 1 - pass, py = y + yy + 1 - pass;
                            if ((uint)px < EW && (uint)py < EH) _ef[py * EW + px] = pass == 0 ? shade : ink;
                        }
            x += Font16.Size;
        }
        return limit;
    }

    /// <summary>장면 30 의 묶음 g 를 줄들로(쪽을 이어서).</summary>
    private List<List<int>> EndLines(int g) =>
        _endText != null && g >= 0 && g < _endText.Count ? _endText[g].SelectMany(p => p.Lines).ToList() : [];

    /// <summary>시 — 묶음 44 를 높은 버퍼의 240 줄 · 48 칸부터 25 줄 간격으로 쓰고 n 줄째부터 보인다. 어둡기 40 에서 200 그림까지 5 그림마다 하나씩 밝아지고 300 그림 뒤 다시.</summary>
    private void Poem(int n)
    {
        Clear();
        var lines = EndLines(44);
        for (int k = 0; k < lines.Count; k++)
        {
            int y = 240 + 25 * k - n;
            if (y > -Font16.Size && y < EH) EGlyphs(lines[k], 48, y, 999);
        }
        int dark = n < 200 ? Math.Max(0, 40 - n / 5) : n > 300 ? Math.Min(40, (n - 300) / 5) : 0;
        _eLight = -dark / 63.0;
    }

    /// <summary>
    /// 동료 소개 한 장(n 그림째) — 바탕 56, 창 틀(sub_168B6) 속 얼굴(가끔 눈을 감는 넷째 장), 서기 모션, 「이름」 · 이름 · 「직업」 · 직업(이름 표 150 + 직업),
    /// 뒷이야기(묶음 12 + 캐릭터, 마지막 사람은 220 그림 뒤 묶음 45). 글은 원작처럼 그림 수만큼 한 자씩.
    /// </summary>
    private void Member(int form, int id, int cls, Motion? stand, int n, bool last)
    {
        Put(_img56);
        var faces = Face(form);
        if (faces.Count > 0)
        {
            var f = faces[faces.Count > 3 && n % 50 >= 48 ? 3 : 0];
            // 창 틀은 원작 대화 상자 조각 대신 대사 창 색으로 — 근사
            for (int y = 6; y < 14 + f.H; y++)
                for (int x = 4; x < 12 + f.W && x < EW; x++)
                    if (y < EH) _ef[y * EW + x] = x < 6 || y < 8 || x >= 10 + f.W || y >= 12 + f.H ? 0xFF8090C0 : 0xFF28458A;
            EBlit(f.Px, f.W, f.H, 8, 10);
        }
        EMotion(stand, MotionFrameAt(stand, n * ETick, loop: true));
        var label = EndLines(10);
        if (label.Count > 0) EGlyphs(label[0], 105, 18, n);
        if (_names != null) EGlyphs(_names.Character(id), 155, 18, n);
        label = EndLines(11);
        if (label.Count > 0) EGlyphs(label[0], 105, 38, n);
        if (_names != null) EGlyphs(_names.Line(cls + 150), 155, 38, n);
        int g = last && n >= 220 ? 45 : id + 12;
        if (g is >= 12 and <= 45 && (g < 44 || g == 45))
        {
            var lines = EndLines(g);
            int left = n;
            for (int k = 0; k < lines.Count; k++) left = EGlyphs(lines[k], 8, 100 + 20 * k, left);
        }
    }

    /// <summary>싸움 연출 한 장 — 배경(BG 0 의 줄무늬 위아래 + BG bg 를 50 줄부터), 맞는 사람 서기, 치는 사람 모션 컷 i(없으면 서기).</summary>
    private void Duel(int bg, int att, int def, Motion? hit, int i, double t)
    {
        Clear();
        if (Back(0) is { } stripe)
        {
            EBlit(stripe, EW, 50, 0, 0);
            EBlit(stripe[(50 * EW)..], EW, 50, 0, 150);
        }
        if (Back(bg) is { } back) EBlit(back, EW, back.Length / EW, 0, 50);
        var ds = StandOf(def);
        EMotion(ds, MotionFrameAt(ds, t, loop: true));
        if (hit != null && i >= 0) EMotion(hit, i);
        else { var a = StandOf(att); EMotion(a, MotionFrameAt(a, t, loop: true)); }
    }

    /// <summary>싸움이 끝난 장을 붙잡아 팔레트 57 색으로 — 원작은 화면을 그대로 두고 팔레트만 바꾼다.</summary>
    private void Freeze(int bg, int att, int def, Motion? hit)
    {
        Duel(bg, att, def, hit, hit == null ? -1 : hit.Frames.Count - 1, 0);
        for (int p = 0; p < _ef.Length; p++)
        {
            uint c = _ef[p] & 0xFFFFFF;
            if (_toPal57 != null && _toPal57.TryGetValue(_ef[p], out var to)) _frozen[p] = to | 0xFF000000;
            else
            {
                uint l = ((c >> 16 & 255) * 3 + (c >> 8 & 255) * 6 + (c & 255)) / 10 / 2;   // 표에 없는 색은 어두운 파랑으로
                _frozen[p] = 0xFF000000 | l << 16 | l << 8 | Math.Min(255, l * 2);
            }
        }
    }

    private void Frozen(int card)
    {
        Array.Copy(_frozen, _ef, _ef.Length);
        if (card >= 0) EMotion(_staff, card);
    }

    private void DrawEnding()
    {
        FillRect(0, 0, _clientW, _clientH, 0xFF000000);
        if (_shots == null || _shot < 0 || _shot >= _shots.Count) return;
        var shot = _shots[_shot];
        _eLight = 0;
        shot.Draw(Math.Min(_shotT, shot.Len));
        Lighten(_ef, _eLight);
        int s = Math.Max(1, Math.Min(_clientW / EW, _clientH / EH));
        BlitScaled(_ef, EW, EH, (_clientW - EW * s) / 2, (_clientH - EH * s) / 2, s, 0, 0, _clientW, _clientH);
        int fs = Math.Max(14, 7 * _zoom);
        if (_shot == _shots.Count - 1)
        {
            if (_shotT > 2)
            {
                var back = Text.Render(TouchUi ? "누르면 타이틀로" : "Enter · Esc 타이틀로", fs, 0xC0FFFFFF, bold: false);
                DrawText(back, (_clientW - back.W) / 2, _clientH - back.H - 24);
            }
        }
        else
        {
            var skip = Text.Render(TouchUi ? "누르면 다음 · 뒤로 끝까지" : "Enter 다음 · Esc 끝까지 건너뛰기", Math.Max(12, fs * 3 / 4), 0xB0FFFFFF, bold: false);
            FillRect(_clientW - skip.W - 24, 6, skip.W + 16, skip.H + 8, 0x80000000);
            DrawText(skip, _clientW - skip.W - 16, 10);
        }
        DrawTalk();
    }
}
