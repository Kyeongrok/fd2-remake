using System.Runtime.InteropServices;
using System.Text.Json;
using Fd2.Native;

namespace Fd2;

/// <summary>패드 단추에 묶을 수 있는 동작 — 방향(십자키 · 왼쪽 스틱)은 늘 커서라 여기 없다.</summary>
internal enum PadAction { Confirm, Cancel, Attack, Wait, System, NextUnit, Gauges, KeyHelp }

/// <summary>
/// 게임 패드(XInput — 엑스박스 패드와 그 호환 패드). 틀마다 상태를 읽어 <b>키보드 키를 누른 것처럼</b> 넘긴다(창세기전3 의 GamePad 와 같은 방법).
/// </summary>
/// <remarks>
/// 십자키 · 왼쪽 스틱 = 방향키(누르고 있으면 되풀이). 확인 = Enter, 취소 = Esc, 나머지는 키 설정(<see cref="KeyBindings"/>)에서 그 동작에 묶인 키를 보낸다.
/// 단추 묶음은 %LOCALAPPDATA%\Fd2\pad.json — 설정 &gt; 단축키 설정에서 키와 함께 바꾼다. 창이 앞에 있을 때만 받고, 패드가 없으면 1초에 한 번만 다시 찾는다.
/// </remarks>
internal sealed class GamePad
{
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState14(uint index, out State state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState910(uint index, out State state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const int StickDeadZone = 16000, TriggerOn = 100;
    private const double RepeatDelay = 0.3, RepeatEvery = 0.09;

    // XInput 단추 비트 + 방아쇠 둘을 위에 얹은 것
    private const int Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080,
                      LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, LT = 0x10000, RT = 0x20000;

    internal static readonly (int Button, string Name)[] Buttons =
        [(A, "A"), (B, "B"), (X, "X"), (Y, "Y"), (LB, "LB"), (RB, "RB"), (LT, "LT"), (RT, "RT"), (Back, "Back"), (Start, "Start"), (LS, "L 스틱 누름"), (RS, "R 스틱 누름")];

    internal static readonly (PadAction Action, string Label, int DefaultButton)[] All =
    [
        (PadAction.Confirm, "결정", A), (PadAction.Cancel, "취소", B), (PadAction.Attack, "공격", X), (PadAction.Wait, "대기", Y),
        (PadAction.System, "시스템 메뉴(턴 끝)", Start), (PadAction.NextUnit, "다음 아군으로", RB), (PadAction.Gauges, "체력바 켜기·끄기", LB),
        (PadAction.KeyHelp, "단축키 목록", Back),
    ];

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "pad.json");

    private readonly Dictionary<PadAction, int> _map = LoadMap();
    private readonly IntPtr _hwnd;
    private readonly Action<int> _press;
    private readonly KeyBindings _keys;

    private int _held, _pad = -1;
    private double _lookAgainAt, _repeatAt;
    private bool _noLibrary;

    /// <summary>설정 창이 「단추를 누르세요」로 기다리는 동작 — 다음에 눌린 단추를 여기에 묶는다.</summary>
    internal PadAction? Capture;

    internal bool Connected => _pad >= 0;

    public GamePad(IntPtr hwnd, KeyBindings keys, Action<int> press)
    {
        _hwnd = hwnd;
        _keys = keys;
        _press = press;
    }

    internal static string ButtonName(int button) => Buttons.FirstOrDefault(b => b.Button == button).Name ?? "(없음)";

    internal int ButtonOf(PadAction action) => _map.GetValueOrDefault(action);

    /// <summary>단추를 동작에 묶는다 — 그 단추를 쓰던 동작은 이 동작의 옛 단추를 받는다.</summary>
    internal void Assign(PadAction action, int button)
    {
        int old = _map[action];
        foreach (var (a, b) in _map.ToList())
            if (b == button && a != action) _map[a] = old;
        _map[action] = button;
        Save();
    }

    internal void ResetDefaults()
    {
        foreach (var (a, _, b) in All) _map[a] = b;
        Save();
    }

    /// <summary>틀마다 — 새로 눌린 단추를 키로 넘기고, 방향은 누르고 있으면 되풀이한다.</summary>
    internal void Poll(double now)
    {
        if (_noLibrary || GetForegroundWindow() != _hwnd) { _held = 0; return; }
        if (_pad < 0 && now < _lookAgainAt) return;
        State s = default;
        bool ok = _pad >= 0 && Read((uint)_pad, out s);
        if (!ok)
        {
            _pad = -1;
            for (uint i = 0; i < 4 && !ok; i++)
                if (Read(i, out s)) { _pad = (int)i; ok = true; }
        }
        if (!ok) { _lookAgainAt = now + 1; _held = 0; return; }

        int now0 = s.Buttons;
        if (s.LeftTrigger > TriggerOn) now0 |= LT;
        if (s.RightTrigger > TriggerOn) now0 |= RT;
        if (s.LY > StickDeadZone) now0 |= Up;
        if (s.LY < -StickDeadZone) now0 |= Down;
        if (s.LX < -StickDeadZone) now0 |= Left;
        if (s.LX > StickDeadZone) now0 |= Right;

        int pressed = now0 & ~_held;
        int dirs = now0 & (Up | Down | Left | Right);
        if ((pressed & (Up | Down | Left | Right)) != 0) _repeatAt = now + RepeatDelay;
        else if (dirs != 0 && now >= _repeatAt) { pressed |= dirs; _repeatAt = now + RepeatEvery; }
        _held = now0;
        if (pressed == 0) return;

        if (Capture is { } cap)
        {
            int b = Buttons.Select(x => x.Button).FirstOrDefault(x => (pressed & x) != 0);
            if (b != 0) { Assign(cap, b); Capture = null; }
            return;
        }

        if ((pressed & Up) != 0) _press(Win32.VK_UP);
        if ((pressed & Down) != 0) _press(Win32.VK_DOWN);
        if ((pressed & Left) != 0) _press(Win32.VK_LEFT);
        if ((pressed & Right) != 0) _press(Win32.VK_RIGHT);
        foreach (var (action, button) in _map)
        {
            if ((pressed & button) == 0) continue;
            _press(action switch
            {
                PadAction.Confirm => Win32.VK_RETURN,
                PadAction.Cancel => Win32.VK_ESCAPE,
                PadAction.Attack => _keys[KeyAction.Attack],
                PadAction.Wait => _keys[KeyAction.Wait],
                PadAction.System => _keys[KeyAction.System],
                PadAction.NextUnit => _keys[KeyAction.NextUnit],
                PadAction.Gauges => _keys[KeyAction.Gauges],
                _ => _keys[KeyAction.KeyHelp],
            });
        }
    }

    private bool Read(uint index, out State s)
    {
        s = default;
        try { return GetState14(index, out s) == 0; }
        catch (DllNotFoundException)
        {
            try { return GetState910(index, out s) == 0; }
            catch (DllNotFoundException) { _noLibrary = true; return false; }
        }
    }

    private static Dictionary<PadAction, int> LoadMap()
    {
        var map = All.ToDictionary(a => a.Action, a => a.DefaultButton);
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) is { } saved)
                foreach (var (name, buttonName) in saved)
                    if (Enum.TryParse<PadAction>(name, out var action) && Buttons.FirstOrDefault(b => b.Name == buttonName) is { Button: > 0 } found)
                        map[action] = found.Button;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return map;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_map.ToDictionary(p => p.Key.ToString(), p => ButtonName(p.Value))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
