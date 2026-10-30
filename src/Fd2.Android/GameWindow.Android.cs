namespace Fd2;

/// <summary>
/// 안드로이드의 갈고리 — 지도까지 CPU 로 찍고(GPU 지도 텍스처 · 고해상도 없음), 편집기 · 전체화면 · 스크린샷은 없다.
/// </summary>
internal sealed partial class GameWindow
{
    private const bool HasEditor = false;

    partial void PlatformInit() => _cpuMap = true;

    private partial uint[]? HiIcon(int icon, int frame) => null;

    private partial void OpenEditor() { }
}
