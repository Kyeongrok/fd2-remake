namespace Fd2.Native;

/// <summary>가상 키 번호 — 게임 로직이 쓰는 것. 윈도우 · 안드로이드가 함께 쓴다(안드로이드는 키를 이 번호로 바꿔 넘긴다).</summary>
internal static partial class Win32
{
    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    public const int VK_RETURN = 0x0D, VK_SPACE = 0x20, VK_ESCAPE = 0x1B;
    public const int VK_TAB = 0x09, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_F11 = 0x7A, VK_F12 = 0x7B;
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_F1 = 0x70;
}
