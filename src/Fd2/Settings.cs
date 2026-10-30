using System.Text.Json;

namespace Fd2;

/// <summary>설정 > 화면에서 고른 값 — %LOCALAPPDATA%\Fd2\settings.json 에 둔다.</summary>
internal sealed class Settings
{
    /// <summary>해상도 = 창 안쪽 크기(화면 픽셀).</summary>
    public int ViewW { get; set; } = 1920;
    public int ViewH { get; set; } = 1080;

    /// <summary>배율(정수 배). 0 = 자동(원본 화면 480x312 가 창에 들어가는 가장 큰 배율).</summary>
    public int Zoom { get; set; }

    public bool Fullscreen { get; set; }

    /// <summary>게임 > 판 고르기로 고른 게임 폴더 — 인자 · FD2_DIR 가 없을 때 쓴다. 비면 바탕화면의 모션패치 폴더.</summary>
    public string GameDir { get; set; } = "";

    /// <summary>모드 > 고해상도 — 기본 켬. 처음에는 이 PC 에서 차례로 만들어 다 된 것부터 갈아 끼운다.</summary>
    public bool HiRes { get; set; } = true;

    /// <summary>모드 > 배경음악 — 기본 켬.</summary>
    public bool Music { get; set; } = true;

    /// <summary>모드 > 이동 · 공격 범위 보기 — 전투 밖에서도 고른 사람의 이동(파랑) · 공격(빨강) 범위를 보인다.</summary>
    public bool ShowRange { get; set; } = true;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!GameWindow.Offscreen && File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Save()
    {
        if (GameWindow.Offscreen) return;   // 화면 밖 시험은 쓰는 사람의 설정을 안 건드린다
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
