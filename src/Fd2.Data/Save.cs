using System.Buffers.Binary;

namespace Fd2.Data;

/// <summary>
/// 원작 세이브 FD2.SAV(분석-세이브) — 22987 바이트 고정, 통째로 XOR(sub_4DF28), 끝 4바이트 앞까지의 바이트 합(sub_4DF09).
/// 0 ~ 12500 은 싸움 중 기록(전황기록) 하나, 12587 + 2600k 는 마을 기록 슬롯 4개(k = 0..3).
/// </summary>
/// <remarks>
/// 리메이크는 같은 꼴로 %LOCALAPPDATA%\Fd2 에 쓴다 — 원작 폴더의 FD2.SAV 는 읽기만 한다(원작으로 이어 할 수 있게 꼴을 지킨다).
/// </remarks>
public sealed class SaveFile
{
    public const int Size = 22987, SumAt = 22983, Slots = 4, SlotBase = 12587, SlotSize = 2600;
    public const int Empty = 255;

    // 싸움 중 기록 자리
    public const int BattleInfo = 0, BattleInfoSize = 2211, BattleParty = 2211, BattleUnits = 4771, BattleUnitMax = 96,
        BattleChests = 12451, BattleTurn = 12483, BattleUnitCount = 12484, BattleScene = 12485, BattleView = 12486,
        BattleCursor = 12488, BattlePartyCount = 12492, BattleGold = 12493;

    /// <summary>풀린 자료 22987 바이트.</summary>
    public byte[] Data { get; }

    private SaveFile(byte[] plain) => Data = plain;

    /// <summary>빈 세이브 — 원작처럼 0xFF 로 채운다(슬롯 4개 · 싸움 중 기록 모두 빔).</summary>
    public static SaveFile New()
    {
        var d = new byte[Size];
        Array.Fill(d, (byte)0xFF);
        return new SaveFile(d);
    }

    /// <summary>파일을 읽어 푼다 — 없거나 크기가 다르면 빈 세이브.</summary>
    public static SaveFile Load(string path)
    {
        try
        {
            var raw = File.ReadAllBytes(path);
            if (raw.Length != Size) return New();
            Crypt(raw);
            return new SaveFile(raw);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return New(); }
    }

    /// <summary>검사합을 넣고 걸어서 쓴다(sub_19DF7 · sub_2968D 차례).</summary>
    public void Save(string path)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Data.AsSpan(SumAt), Sum(Data));
        var raw = (byte[])Data.Clone();
        Crypt(raw);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, raw);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>XOR — 열쇠 165 에서 바이트마다 k = rol16(k − 28652, 3), b ^= k &amp; 0xFF. 걸기 = 풀기.</summary>
    public static void Crypt(byte[] b)
    {
        int k = 165;
        for (int i = 0; i < b.Length; i++)
        {
            k = (k - 28652) & 0xFFFF;
            k = ((k << 3) | (k >> 13)) & 0xFFFF;
            b[i] ^= (byte)k;
        }
    }

    public static uint Sum(byte[] plain)
    {
        uint s = 0;
        for (int i = 0; i < SumAt; i++) s += plain[i];
        return s;
    }

    /// <summary>검사합이 맞나 — 싸움 중 기록을 불러오기 전에 본다(sub_10010, 틀리면 글 436).</summary>
    public bool SumOk => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(SumAt)) == Sum(Data);

    // ── 마을 기록 슬롯 ──────────────────────────────────────────────────────

    private static int SlotAt(int k) => SlotBase + SlotSize * k;

    /// <summary>슬롯 k 의 다음에 할 장면 — 빈 슬롯이면 null.</summary>
    public int? SlotScene(int k) => Data[SlotAt(k) + 2560] is var s and not Empty ? s : null;

    public Campaign? ReadSlot(int k)
    {
        int at = SlotAt(k);
        if (SlotScene(k) is not { } scene) return null;
        int n = Math.Min(Campaign.MaxParty, (int)Data[at + 2561]);
        var c = new Campaign { NextScene = scene, Gold = BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(at + 2562)) };
        for (int i = 0; i < n; i++) c.Party.Add(Data.AsSpan(at + HeroRecord.Size * i, HeroRecord.Size).ToArray());
        (c.TerrainWindow, c.Option3, c.Music, c.Sound) = (Data[at + 2566], Data[at + 2567], Data[at + 2568], Data[at + 2569]);
        return c;
    }

    public void WriteSlot(int k, Campaign c)
    {
        int at = SlotAt(k);
        Array.Clear(Data, at, 2560);
        for (int i = 0; i < c.Party.Count && i < Campaign.MaxParty; i++) c.Party[i].CopyTo(Data, at + HeroRecord.Size * i);
        Data[at + 2560] = (byte)c.NextScene;
        Data[at + 2561] = (byte)Math.Min(c.Party.Count, Campaign.MaxParty);
        BinaryPrimitives.WriteInt32LittleEndian(Data.AsSpan(at + 2562), c.Gold);
        (Data[at + 2566], Data[at + 2567], Data[at + 2568], Data[at + 2569]) = (c.TerrainWindow, c.Option3, c.Music, c.Sound);
        Array.Clear(Data, at + 2570, 30);
    }

    // ── 싸움 중 기록 ────────────────────────────────────────────────────────

    /// <summary>싸움 중 기록의 장면 — 없으면 null.</summary>
    public int? BattleSceneIndex => Data[BattleScene] is var s and not Empty ? s : null;

    /// <summary>
    /// 싸움 중 기록을 쓴다(sub_19DF7) — 슬롯은 그대로 두고 0 ~ 12500 만. 처음 쓰는 파일이면 슬롯 장면 넷은 0xFF(빈 칸) 그대로.
    /// </summary>
    public void WriteBattle(BattleRecord b)
    {
        Array.Clear(Data, 0, SlotBase);
        b.Info.AsSpan(0, Math.Min(b.Info.Length, BattleInfoSize)).CopyTo(Data.AsSpan(BattleInfo));
        for (int i = 0; i < b.Party.Count && i < Campaign.MaxParty; i++) b.Party[i].CopyTo(Data, BattleParty + HeroRecord.Size * i);
        int n = Math.Min(b.Units.Count, BattleUnitMax);
        for (int i = 0; i < n; i++)
        {
            var r = b.Units[i] ?? HeroRecord.Absent();
            r.CopyTo(Data, BattleUnits + HeroRecord.Size * i);
        }
        foreach (int k in b.Chests) if (k is >= 0 and < 32) Data[BattleChests + k] = 1;
        Data[BattleTurn] = (byte)b.Turn;
        Data[BattleUnitCount] = (byte)n;
        Data[BattleScene] = (byte)b.Scene;
        (Data[BattleView], Data[BattleView + 1]) = ((byte)b.ViewX, (byte)b.ViewY);
        (Data[BattleCursor], Data[BattleCursor + 1]) = ((byte)b.CursorX, (byte)b.CursorY);
        (Data[BattleCursor + 2], Data[BattleCursor + 3]) = ((byte)Math.Max(0, b.CursorX - b.ViewX), (byte)Math.Max(0, b.CursorY - b.ViewY));
        Data[BattlePartyCount] = (byte)Math.Min(b.Party.Count, Campaign.MaxParty);
        BinaryPrimitives.WriteInt32LittleEndian(Data.AsSpan(BattleGold), b.Gold);
        (Data[12497], Data[12498], Data[12499], Data[12500]) = (b.Option3, b.TerrainWindow, b.Music, b.Sound);
    }

    /// <summary>싸움 중 기록을 읽는다(sub_10010) — 없거나 검사합이 틀리면 null(원작은 글 436 「전황기록훼손됨」).</summary>
    public BattleRecord? ReadBattle()
    {
        if (BattleSceneIndex is not { } scene || !SumOk) return null;
        var b = new BattleRecord
        {
            Scene = scene, Turn = Data[BattleTurn], Gold = BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(BattleGold)),
            ViewX = Data[BattleView], ViewY = Data[BattleView + 1], CursorX = Data[BattleCursor], CursorY = Data[BattleCursor + 1],
            Info = Data.AsSpan(BattleInfo, BattleInfoSize).ToArray(),
            Option3 = Data[12497], TerrainWindow = Data[12498], Music = Data[12499], Sound = Data[12500],
        };
        int party = Math.Min(Campaign.MaxParty, (int)Data[BattlePartyCount]);
        for (int i = 0; i < party; i++) b.Party.Add(Data.AsSpan(BattleParty + HeroRecord.Size * i, HeroRecord.Size).ToArray());
        int n = Math.Min(BattleUnitMax, (int)Data[BattleUnitCount]);
        for (int i = 0; i < n; i++) b.Units.Add(Data.AsSpan(BattleUnits + HeroRecord.Size * i, HeroRecord.Size).ToArray());
        for (int k = 0; k < 32; k++) if (Data[BattleChests + k] == 1) b.Chests.Add(k);
        return b;
    }
}

/// <summary>싸움 중 기록(「전황기록」) 한 벌 — 장면 · 차례 · 지도 위 사람(배열 번호 차례) · 동료 표 · 연 상자 · 돈 · 화면 · 커서.</summary>
public sealed class BattleRecord
{
    public int Scene, Turn, Gold, ViewX, ViewY, CursorX, CursorY;

    /// <summary>장면 정보(FDFIELD 3n+1) — 원작은 실행 중 바뀐 값(상자 내용 · 사건 줄)까지 담는다.</summary>
    public byte[] Info = [];

    public readonly List<byte[]> Party = new();

    /// <summary>배열 번호 i 의 사람 칸 — 없는 번호(못 세운 줄)는 null 로 써도 된다(쓸 때 「없음」 칸으로).</summary>
    public readonly List<byte[]?> Units = new();

    public readonly HashSet<int> Chests = new();
    public byte TerrainWindow = 1, Option3 = 1, Music = 1, Sound = 1;
}

/// <summary>장 사이에 이어지는 것 — 동료 표(80바이트 × 32), 돈, 다음에 할 장면, 설정 네 바이트.</summary>
public sealed class Campaign
{
    public const int MaxParty = 32;

    /// <summary>새 게임 차례(분석-장면): 프롤로그 32 → 31 → 0 → 1 → … → 29 → 30(끝).</summary>
    public static int NextOf(int scene) => scene switch { 32 => 31, 31 => 0, _ => scene + 1 };

    /// <summary>마지막 장면(엔딩 연출) — 이 장면을 이기면 끝.</summary>
    public const int FinalScene = 30;

    public readonly List<byte[]> Party = new();
    public int Gold;
    public int NextScene;
    public byte TerrainWindow = 1, Option3 = 1, Music = 1, Sound = 1;

    /// <summary>싸움 중 기록에서 왔으면 그 기록(이어하기가 장면 처음이 아니라 그 차례부터 연다).</summary>
    public BattleRecord? Battle;

    /// <summary>처음 동료 — 프롤로그 끝(sub_3231B)에 넣는 사울(0) · 유니(9) · 아래스(4) · 카일(30), 합류 레벨 그대로.</summary>
    public static Campaign NewGame(Battle battle)
    {
        var c = new Campaign { NextScene = 0 };
        foreach (int f in Scene.PartyOrder.Take(4))
        {
            var u = new Unit { Icon = f, Form = f, Id = f, Side = Side.Player };
            battle.Setup(u);
            c.Party.Add(HeroRecord.From(u));
        }
        return c;
    }

    /// <summary>
    /// 이긴 뒤 동료 표를 지도의 사람들로 고친다 — 나갔던 사람은 그 상태(레벨 · 경험 · 가방 · 마법)로, 이번에 합류한 우리 편(유닛 표 편 2)은 끝에 붙인다(sub_112A5).
    /// 원작처럼 산 사람은 HP 가득, 모두 MP 가득, 쓰러진 사람은 쓰러진 채(분석-캐릭터). 상태 이상은 지운다.
    /// </summary>
    public void AfterVictory(IEnumerable<Unit> units)
    {
        foreach (var u in units.Where(u => u.Side == Side.Player && !u.Ai))
        {
            int i = Party.FindIndex(r => HeroRecord.Id(r) == u.NameId);
            if (i < 0 && Party.Count >= MaxParty) continue;
            var r = HeroRecord.From(u);
            if (i >= 0) Party[i] = r; else Party.Add(r);
        }
        foreach (var r in Party)
        {
            bool down = HeroRecord.Hp(r) == 0;
            HeroRecord.Rest(r, down);
        }
    }

    /// <summary>출전자 — 동료 표 앞에서부터 쓰러지지 않은 사람(원작은 출전 편성 sub_2AF28 에서 고른다).</summary>
    public IEnumerable<byte[]> Ready => Party.Where(r => HeroRecord.Hp(r) > 0);
}

/// <summary>
/// 80바이트 사람 칸(분석-캐릭터 §5) — 동료 표 · 지도 유닛 공통. <see cref="Unit"/> 와 오간다.
/// </summary>
public static class HeroRecord
{
    public const int Size = 80;

    public static int Id(byte[] r) => r[8];
    public static int Form(byte[] r) => r[7];
    public static int Hp(byte[] r) => BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(64));

    /// <summary>쓰러져 없는 칸(+5 비트 0, HP 0) — 싸움 중 기록에서 못 세운 배열 번호를 채운다.</summary>
    public static byte[] Absent()
    {
        var r = new byte[Size];
        r[5] = 1;
        for (int k = 0; k < 8; k++) (r[10 + 2 * k], r[11 + 2 * k]) = (0x80, 0xFF);
        r[49] = 255;
        return r;
    }

    /// <summary>쓰러졌거나 없는 칸인가(+5 비트 0 이고 HP 0).</summary>
    public static bool Gone(byte[] r) => (r[5] & 1) != 0 && Hp(r) == 0;

    /// <summary>싸움 중 기록의 사람을 되살린다 — 표로 채우고(<see cref="Battle.Setup"/>) 세이브 값으로 덮은 뒤 자리 · 편 · AI · 떨굼 · 행동 끝까지.</summary>
    public static Unit Restore(byte[] r, int slot, bool royalsMoved, Battle battle)
    {
        int team = r[6];
        var u = new Unit
        {
            Icon = CharacterNames.SpriteOf(r[7], royalsMoved), Form = r[7], Id = r[8], Side = team == 0 ? Side.Other : Side.Player, Ai = team == 1,
            Slot = slot, Level = Math.Max(1, (int)r[33]), X = r[0], Y = r[1], Facing = Enum.IsDefined((Dir)r[3]) ? (Dir)r[3] : Dir.Down,
        };
        battle.Setup(u);
        Apply(r, u);
        (u.DropKind, u.DropValue) = (r[49], BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(50)));
        (u.AiByte, u.AiX, u.AiY, u.Chest) = (r[52], r[53], r[54], r[61]);
        u.Acted = (r[5] & 0x80) != 0;
        battle.Recompute(u);
        (u.FromX, u.FromY) = (u.X, u.Y);
        return u;
    }

    public static byte[] From(Unit u)
    {
        var r = new byte[Size];
        r[0] = (byte)u.X;
        r[1] = (byte)u.Y;
        r[3] = (byte)u.Facing;
        r[5] = (byte)((u.Hp <= 0 ? 1 : 0) | (u.Acted ? 0x80 : 0));
        r[6] = (byte)u.Team;
        r[7] = (byte)u.FormId;
        r[8] = (byte)u.NameId;
        for (int k = 0; k < 8; k++)
        {
            if (k < u.Bag.Count) (r[10 + 2 * k], r[11 + 2 * k]) = ((byte)(u.Bag[k].Equipped ? 0x40 : 0), (byte)u.Bag[k].Item);
            else (r[10 + 2 * k], r[11 + 2 * k]) = (0x80, 0xFF);
        }
        for (int k = 0; k < 5; k++) r[26 + k] = (byte)(u.Spells >> (8 * k));
        (r[31], r[32], r[33]) = ((byte)u.Race, (byte)u.Class, (byte)u.Level);
        for (int k = 0; k < 6; k++) r[34 + k] = (byte)u.Status[k];
        r[49] = (byte)u.DropKind;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(50), (ushort)u.DropValue);
        (r[52], r[53], r[54], r[61]) = ((byte)u.AiByte, (byte)u.AiX, (byte)u.AiY, (byte)u.Chest);
        BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(55), (short)u.BaseAtk);
        BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(57), (short)u.BaseDef);
        r[59] = (byte)u.Move;
        r[60] = (byte)u.Exp;
        BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(62), (short)u.Speed);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(64), (ushort)Math.Max(0, u.Hp));
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(66), (ushort)u.MaxHp);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(68), (ushort)Math.Max(0, u.Mp));
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(70), (ushort)u.MaxMp);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(72), (ushort)u.Atk);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(74), (ushort)u.Def);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(76), (ushort)u.Hit);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(78), (ushort)u.Evade);
        return r;
    }

    /// <summary>
    /// 칸을 사람에 옮긴다 — 표에서 채운 뒤(<see cref="Battle.Setup"/>) 레벨 · 능력 · 가방 · 마법 · HP · MP 를 세이브 값으로 덮는다.
    /// 자리 · 편은 지도가 정하므로 건드리지 않는다.
    /// </summary>
    public static void Apply(byte[] r, Unit u)
    {
        u.Form = r[7];
        u.Id = r[8];
        u.Bag.Clear();
        for (int k = 0; k < 8; k++)
            if ((r[10 + 2 * k] & 0x80) == 0) u.Bag.Add((r[11 + 2 * k], (r[10 + 2 * k] & 0x40) != 0));
        u.Spells = 0;
        for (int k = 0; k < 5; k++) u.Spells |= (ulong)r[26 + k] << (8 * k);
        (u.Race, u.Class, u.Level) = (r[31], r[32], Math.Max(1, (int)r[33]));
        for (int k = 0; k < 6; k++) u.Status[k] = r[34 + k];
        u.BaseAtk = BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(55));
        u.BaseDef = BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(57));
        u.Move = r[59];
        u.Exp = r[60];
        u.Speed = BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(62));
        u.Hp = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(64));
        u.MaxHp = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(66));
        u.Mp = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(68));
        u.MaxMp = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(70));
    }

    /// <summary>장이 끝난 뒤 — 산 사람은 HP 가득, 모두 MP 가득, 상태 이상은 지운다. 쓰러진 사람은 HP 0 으로 남는다.</summary>
    public static void Rest(byte[] r, bool down)
    {
        r[5] = (byte)(down ? 1 : 0);
        if (!down) r.AsSpan(66, 2).CopyTo(r.AsSpan(64));
        r.AsSpan(70, 2).CopyTo(r.AsSpan(68));
        Array.Clear(r, 34, 6);
    }
}
