using System.Runtime.InteropServices;

namespace Fd2.Native;

/// <summary>순수 네이티브(Win32) 창 하나를 만드는 데 필요한 최소한의 P/Invoke 묶음.</summary>
internal static class Win32
{
    public const int CS_HREDRAW = 0x0002, CS_VREDRAW = 0x0001;
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int GWL_STYLE = -16;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_NOMOVE = 0x0002;
    public const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    public const uint SPI_GETWORKAREA = 0x0030;

    public const uint MF_STRING = 0x0000, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800, MF_GRAYED = 0x0001;
    public const uint MF_BYCOMMAND = 0x0000, MF_CHECKED = 0x0008, MF_UNCHECKED = 0x0000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_ERASEBKGND = 0x0014;

    public const uint PM_REMOVE = 0x0001;

    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    public const int VK_RETURN = 0x0D, VK_SPACE = 0x20, VK_ESCAPE = 0x1B;
    public const int VK_TAB = 0x09, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_F11 = 0x7A, VK_F12 = 0x7B;

    public const int IDC_ARROW = 32512;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProc WindowProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);

    /// <summary>모니터 DPI 인식 — 이걸 안 하면 고배율 화면에서 창이 통째로 다시 늘려져
    /// 우리가 이미 2배로 그린 그림이 또 늘어나 뭉갠다(<c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>).</summary>
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WndClassEx wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName,
        int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool AdjustWindowRect(ref Rect rect, int style, [MarshalAs(UnmanagedType.Bool)] bool menu);

    /// <summary>exe 에 실린 앱 아이콘 — .NET 은 ApplicationIcon 을 리소스 32512 로 싣는다.</summary>
    public const int IDI_APPLICATION = 32512;

    [DllImport("user32.dll")]
    public static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconId);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern IntPtr CreateMenu();

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    public static extern bool SetMenu(IntPtr hWnd, IntPtr menu);

    [DllImport("user32.dll")]
    public static extern uint CheckMenuItem(IntPtr menu, uint id, uint check);

    [DllImport("user32.dll")]
    public static extern bool DrawMenuBar(IntPtr hWnd);

    public const uint TPM_RETURNCMD = 0x0100;

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point p);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfoW(uint action, uint param, ref Rect rect, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowTextW(IntPtr hWnd, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    /// <summary>lParam 의 아래 16비트 — 마우스 메시지의 클라이언트 x.</summary>
    public static int LowWord(IntPtr lParam) => unchecked((short)(long)lParam);

    /// <summary>lParam 의 위 16비트 — 마우스 메시지의 클라이언트 y.</summary>
    public static int HighWord(IntPtr lParam) => unchecked((short)((long)lParam >> 16));
}
