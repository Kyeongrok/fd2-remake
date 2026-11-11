namespace Fd2.Data;

/// <summary>
/// 장면 대본(SceneEvents.json)의 사람 칸 쓰기 — setunit i off v · ally i. 게임(GameWindow)과 점검(Fd2.Check)이 같은 것을 쓴다.
/// 원작 사건은 유닛 칸(dword_53A45 + 80i)을 +5(쓰러짐 · 숨김 비트)와 상관없이 바로 쓴다 — 쓰러진 사람에게도 쓴다.
/// </summary>
public static class ScriptUnit
{
    /// <summary>
    /// 배열 번호 slot 의 칸 — 산 사람 먼저, 그다음 쓰러진 사람 · 숨긴 사람.
    /// 9장 사건 30(래터의 떨굼)은 쓰러진 래터(배열 11)의 칸을 고쳐 HP 1 의 컴퓨터 아군으로 되세운다 — 산 사람만 찾던 때는 다 넘어가 래터가 그냥 사라졌다(버그 리포트 #8).
    /// </summary>
    public static Unit? Slot(Scene s, int slot) =>
        s.Units.Find(u => u.Slot == slot && u.Alive) ?? s.Units.Find(u => u.Slot == slot) ?? s.Hidden.Find(u => u.Slot == slot);

    /// <summary>ally i(dword_53A45 + 80i + 6 = 1) — 컴퓨터 아군으로.</summary>
    public static void Ally(Unit u) => (u.Side, u.Ai) = (Side.Player, true);

    /// <summary>
    /// setunit — +0 · +1 자리, +3 보는 쪽, +6 편, +7 · +8 꼴 · 캐릭터 번호, +33 레벨, +49 떨굼 종류, +52 ~ +54 AI, +61 상자 번호, +64 HP.
    /// +5(상태 비트)는 숨김과 얽혀 부르는 쪽이 맡는다. 그 밖의 자리는 넘긴다.
    /// </summary>
    public static void Set(Scene s, Unit u, int off, int v)
    {
        switch (off)
        {
            case 0: if (s.Inside(v, u.Y)) (u.X, u.FromX) = (v, v); break;
            case 1: if (s.Inside(u.X, v)) (u.Y, u.FromY) = (v, v); break;
            case 3: u.Facing = (Dir)(v & 3); break;
            case 6: u.Side = v == 0 ? Side.Other : Side.Player; u.Ai = v == 1; break;
            // +7 꼴 · +8 캐릭터 번호 — 9장 사건 30 이 래터(몬스터 50 꼴)를 동료 꼴 6(래터-기사)으로, 29장 승리 뒤가 유닛 20 을 126 으로. 능력은 그대로 둔다
            case 7 when CharacterNames.SpriteOf(v, s.RoyalsMoved) is var sp && sp < s.IconCount: (u.Form, u.Icon) = (v, sp); break;
            case 8: u.Id = v; break;
            case 33: u.Level = Math.Max(1, v); break;
            case 49: u.DropKind = v; break;
            case 52: u.AiByte = v; break;
            case 53: u.AiX = v; break;
            case 54: u.AiY = v; break;
            case 61: u.Chest = v; break;
            // +64 HP — 쓰러진 사람(HP 0)에게 쓰면 다시 선다(9장 사건 30 의 래터 HP 1)
            case 64: u.Hp = Math.Max(0, v); break;
        }
    }
}
