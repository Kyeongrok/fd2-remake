using System.Text.Json;
using Fd2.Native;

namespace Fd2;

/// <summary>단축키로 바꿀 수 있는 동작.</summary>
internal enum KeyAction { MoveUp, MoveDown, MoveLeft, MoveRight, Attack, Wait, System, NextUnit, Gauges, KeyHelp }

/// <summary>
/// 동작 ↔ 가상 키 표(창세기전3 의 KeyBindings 와 같은 꼴). %LOCALAPPDATA%\Fd2\keys.json 에 저장한다.
/// 방향키(커서) · Enter/Space(결정) · Esc(취소)는 늘 고정.
/// </summary>
internal sealed class KeyBindings
{
    public static readonly (KeyAction Action, string Label, int DefaultKey)[] All =
    [
        (KeyAction.MoveUp, "커서 위로", 'W'),
        (KeyAction.MoveDown, "커서 아래로", 'S'),
        (KeyAction.MoveLeft, "커서 왼쪽으로", 'A'),
        (KeyAction.MoveRight, "커서 오른쪽으로", 'D'),
        (KeyAction.Attack, "공격", 'E'),
        (KeyAction.Wait, "대기", 'Q'),
        (KeyAction.System, "시스템 메뉴(턴 끝)", 'X'),
        (KeyAction.NextUnit, "다음 아군으로", Win32.VK_TAB),
        (KeyAction.Gauges, "체력바 켜기·끄기", 'H'),
        (KeyAction.KeyHelp, "단축키 목록", 'J'),
    ];

    private readonly Dictionary<KeyAction, int> _keys = [];

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "keys.json");

    public KeyBindings() => ResetDefaults();

    public int this[KeyAction action] => _keys[action];

    public void ResetDefaults()
    {
        foreach (var (action, _, key) in All) _keys[action] = key;
    }

    public KeyAction? ActionFor(int vk) => _keys.FirstOrDefault(p => p.Value == vk) is { Value: not 0 } hit ? hit.Key : null;

    /// <summary>동작에 키를 준다. 그 키를 쓰던 동작은 이 동작의 옛 키를 받는다(맞바꿈).</summary>
    public void Assign(KeyAction action, int vk)
    {
        int old = _keys[action];
        if (ActionFor(vk) is { } other && other != action) _keys[other] = old;
        _keys[action] = vk;
    }

    public static KeyBindings Load()
    {
        var keys = new KeyBindings();
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath)) is { } saved)
                foreach (var (name, vk) in saved)
                    if (Enum.TryParse<KeyAction>(name, out var action) && vk > 0) keys._keys[action] = vk;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return keys;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_keys.ToDictionary(p => p.Key.ToString(), p => p.Value)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string KeyName(int vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        Win32.VK_SPACE => "Space",
        Win32.VK_TAB => "Tab",
        Win32.VK_RETURN => "Enter",
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x08 => "Backspace",
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/", 0xC0 => "`",
        0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
        _ => $"키 {vk:X2}",
    };
}
