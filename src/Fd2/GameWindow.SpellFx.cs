using Fd2.Data;

namespace Fd2;

/// <summary>
/// 전투 화면 마법 효과(sub_2FF01, 마법 0~8) — FDOTHER 18~44 의 FIGANI 꼴 효과 그림을 맞는 쪽 둘레에 입자로 돌린다.
/// 원작은 마법마다 따로 짠 함수(표 0x524C6)인데, 여기서는 그 함수들의 자리 표 · 컷 범위 · 빠르기 · 다시 나오기 · 소리 때를 한 꼴로 옮겼다.
/// 맞는 쪽은 효과 동안 불(빨강) · 번개 · 빛(푸른빛)으로 물들고, 맞는 컷에서 화면이 번쩍인다(마법 0~3 빨강, 4~7 푸른 회색, 8 흰색).
/// </summary>
internal sealed partial class GameWindow
{
    /// <summary>
    /// 한 마법의 입자 꼴 — Ours · Theirs 효과 그림(FDOTHER), 입자 자리(X · Y, 320x200 화면), 벌(컷 더하기), 컷 수, 몇 틱에 한 컷,
    /// 입자마다 늦게 시작(틱), 맞는 구간 틱 수, 다 그리면 다시 나오나, 소리 내는 컷, 뒤(맞는 쪽 뒤) 입자, 적이 쓸 때 x 더하기, 두 겹(컷 + Layer 도 함께).
    /// </summary>
    private sealed record SpellFxSpec(int Ours, int Theirs, int[] X, int[] Y, int[] Sets, int Frames, int Rate, int Delay, int Ticks, bool Respawn,
                                      int SoundAt, bool[]? Back = null, int EnemyDx = 0, int Layer = 0, int Count = 0);

    private static readonly SpellFxSpec?[] SpellFxSpecs =
    [
        // 0 화염술 — 불기둥 7개
        new(18, 20, [40, 70, 120, 80, 50, 100, 70], [0, -10, -20, 0, -15, -5, 0], [0], 16, 1, 2, 28, false, 3,
            [false, false, true, false, true, false, false], 148),
        // 1 열염술 — 고리 8개, 두 겹(0~14 / 15~29), 위 호 넷은 뒤
        new(19, 21, [21, 41, 80, 119, 135, 119, 80, 41], [-10, -24, -30, -24, -10, 4, 10, 4], [0], 15, 1, 2, 31, false, 5,
            [true, true, true, true, false, false, false, false], 148, 15),
        // 2 염룡술 — 화면 절대 자리의 용(컷 15~17 번갈아)
        new(26, 27, [0], [0], [15], 3, 1, 0, 12, true, 2),
        // 3 천화술 — 운석 12개, 세 벌
        new(39, 43, [30, 0, 70, 40, 130, 70, -30, 30, 110, 80, -10, 30], [0, -10, 0, -20, -5, -20, 0, -10, 0, -18, 0, -10],
            [22, 0, 0, 0, 0, 0, 11, 0, 22, 0, 0, 0], 11, 2, 2, 40, true, 0, null, 20),
        // 4 뇌격술 — 번개 6개, 두 벌 중 하나
        new(22, 23, [30, 50, 70, 40, 80, 100, 70, 30, 60, 90], [0], [0, 7], 7, 1, 2, 12, true, 0, null, 143, 0, 6),
        // 5 낙뇌술
        new(24, 25, [30, 50, 70, 40, 80, 100, 70, 30, 60, 90], [0], [0, 6], 6, 1, 2, 12, true, 0, null, 143, 0, 6),
        // 6 횡뇌술 — 구슬 다섯(둥글게)
        new(32, 33, [60, 39, 5, 5, 39], [30, 64, 51, 9, -4], [0], 10, 1, 1, 12, false, 0),
        // 7 신뇌술 — 번개 셋
        new(37, 38, [30, -10, 70, 20, 100, 130, 40, 80, 110, 60], [0], [0], 5, 2, 3, 32, true, 1, null, 130, 0, 3),
        // 8 성광탄 — 빛 16개, 네 벌
        new(28, 30, [0], [0], [0, 8, 24, 16, 8, 0, 24, 16, 0, 8, 24, 16, 8, 0, 24, 16], 8, 1, 1, 34, false, 0, null, 0, 0, 16),
    ];

    private readonly Dictionary<int, Motion?> _fxMotions = new();
    private SpellFxSpec? _sfx;
    private Motion? _sfxMotion;
    private int _sfxTick, _sfxSpell;
    private bool _sfxEnemy;
    private double _sfxClock;
    private readonly List<(int Slot, int Start, int Set)> _sfxParts = new();

    /// <summary>효과가 아직 도는가 — 컷인이 끝나기를 기다린다.</summary>
    private bool SpellFxRunning => _sfx != null;

    private Motion? FxMotion(int entry)
    {
        if (!_fxMotions.TryGetValue(entry, out var m)) _fxMotions[entry] = m = Motion.LoadOther(_dir, entry, _pal);
        return m;
    }

    /// <summary>맞는 컷에서 효과를 시작한다(전투 화면 마법 0~8 만).</summary>
    private void StartSpellFx(int spell, bool enemyCaster)
    {
        _sfx = null;
        if (spell is < 0 or > 8 || SpellFxSpecs[spell] is not { } spec) return;
        _sfxMotion = FxMotion(enemyCaster ? spec.Theirs : spec.Ours);
        if (_sfxMotion == null) return;
        (_sfx, _sfxSpell, _sfxEnemy, _sfxTick, _sfxClock) = (spec, spell, enemyCaster, 0, 0);
        _sfxParts.Clear();
        int n = spec.Count > 0 ? spec.Count : spec.X.Length;
        for (int i = 0; i < n; i++)
            _sfxParts.Add((i % spec.X.Length, -spec.Delay * i, spec.Sets.Length > 1 && spec.Count > 0 && spec.Sets.Length < n ? spec.Sets[_battle.Rng.Next(spec.Sets.Length)] : spec.Sets[i % spec.Sets.Length]));
        PlaySfx(SpellSoundBank(spell), 0);
        _sfxFlash = 0.33;
    }

    /// <summary>마법 n 의 소리 묶음(0x526D9 「RRSTUVWXYZ」 — FDOTHER 82, 82, 83 … 89).</summary>
    private static int SpellSoundBank(int spell) => spell <= 1 ? 82 : 81 + spell;

    private double _sfxFlash;

    private void SpellFxTick(double dt)
    {
        if (_sfxFlash > 0) _sfxFlash -= dt;
        if (_sfx is not { } spec) return;
        _sfxClock += dt;
        while (_sfxClock >= FigTick && _sfx != null)
        {
            _sfxClock -= FigTick;
            _sfxTick++;
            // 소리 — 첫 입자가 소리 컷에 닿을 때
            if (_sfxParts.Count > 0 && Frame(_sfxParts[0].Start) == spec.SoundAt && (_sfxTick - _sfxParts[0].Start) % spec.Rate == 0)
                PlaySfx(SpellSoundBank(_sfxSpell), 1);
            for (int i = 0; i < _sfxParts.Count; i++)
            {
                var p = _sfxParts[i];
                if (spec.Respawn && _sfxTick < spec.Ticks && Frame(p.Start) >= spec.Frames + 1)
                    _sfxParts[i] = ((p.Slot + _sfxParts.Count) % spec.X.Length, _sfxTick, spec.Sets.Length > 1 && spec.Count > 0 ? spec.Sets[_battle.Rng.Next(spec.Sets.Length)] : p.Set);
            }
            bool alive = _sfxParts.Any(p => Frame(p.Start) < spec.Frames);
            if (_sfxTick >= spec.Ticks && !alive) _sfx = null;
        }
    }

    /// <summary>시작 틱 start 인 입자의 지금 컷(시작 전이면 음수).</summary>
    private int Frame(int start) => _sfx == null ? -1 : (_sfxTick - start) < 0 ? -1 : (_sfxTick - start) / _sfx.Rate;

    /// <summary>입자들을 그린다 — back 이면 맞는 쪽 뒤 것만, 아니면 앞 것만.</summary>
    private void DrawSpellFx(int bx, int by, int s, (int, int, int, int) clip, bool back)
    {
        if (_sfx is not { } spec || _sfxMotion is not { } m) return;
        int dx = _sfxEnemy ? spec.EnemyDx : 0;
        foreach (var (slot, start, set) in _sfxParts)
        {
            int f = Frame(start);
            if (f < 0 || f >= spec.Frames) continue;
            bool isBack = spec.Back != null && spec.Back[slot % spec.Back.Length];
            if (isBack != back) continue;
            int x = spec.X[slot], y = spec.Y.Length > 1 ? spec.Y[slot] : spec.Y[0];
            foreach (int k in spec.Layer > 0 ? new[] { f, f + spec.Layer } : new[] { f })
            {
                int cut = set + k;
                if (cut >= m.Frames.Count) continue;
                var c = m.Frames[cut];
                BlitFrame(c.Pixels, c.Width, c.Height, bx + (c.X + x + dx) * s, by + (c.Y + y) * s, s, false, 0, 0, clip);
            }
        }
    }

    /// <summary>효과 동안 맞는 쪽 물들이기 — 불 마법 빨강, 번개 · 빛 푸른빛(sub_2FF01 의 실루엣 색 32 · 176 쪽).</summary>
    private (uint Tint, int Amount) SpellFxTint() =>
        _sfx == null ? (0u, 0) : (_sfxSpell <= 3 ? 0xFFFF4020u : 0xFF90B0FFu, 90 + (int)(Math.Sin(_time * 30) * 60));

    /// <summary>맞는 컷의 번쩍임 색(6비트 RGB 를 8비트로) — 0~3 (63,0,0), 4~7 (43,50,60), 8 (63,63,63).</summary>
    private uint SpellFlashColor() => _sfxSpell <= 3 ? 0x80FF0000u : _sfxSpell <= 7 ? 0x80ACC8F0u : 0x90FFFFFFu;
}
