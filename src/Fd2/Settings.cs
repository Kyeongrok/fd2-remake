using System.Text.Json;

namespace Fd2;

/// <summary>설정 > 화면에서 고른 값 — %LOCALAPPDATA%\Fd2\settings.json 에 둔다.</summary>
internal sealed class Settings
{
    /// <summary>해상도 = 창 안쪽 크기(화면 픽셀).</summary>
    public int ViewW { get; set; } = 960;
    public int ViewH { get; set; } = 624;

    /// <summary>배율(정수 배). 0 = 자동(원본 화면 480x312 가 창에 들어가는 가장 큰 배율).</summary>
    public int Zoom { get; set; }

    public bool Fullscreen { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fd2", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
