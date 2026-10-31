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

    /// <summary>게임 > 판 고르기로 고른 게임 폴더 — 인자 · FD2_DIR 가 없을 때 쓴다. 비면 바탕화면의 원본 판 폴더.</summary>
    public string GameDir { get; set; } = "";

    /// <summary>설치할 때 만든 무작위 번호 — 버그 리포트에 붙는다(누구인지는 모른다).</summary>
    public string InstallId { get; set; } = "";

    /// <summary>모드 > 고해상도 — 기본 켬. 처음에는 이 PC 에서 차례로 만들어 다 된 것부터 갈아 끼운다.</summary>
    public bool HiRes { get; set; } = true;

    /// <summary>모드 > 배경음악 — 기본 켬.</summary>
    public bool Music { get; set; } = true;

    /// <summary>효과음(원작 FDOTHER 소리 묶음).</summary>
    public bool Sound { get; set; } = true;

    /// <summary>설정 > 소리 크기 — 배경음 · 효과음 크기(0~100%).</summary>
    public int MusicVolume { get; set; } = 35;   // 기본 35%(사용자 요청)
    public int SoundVolume { get; set; } = 100;

    /// <summary>모드 > 이동 · 공격 범위 보기 — 전투 밖에서도 고른 사람의 이동(파랑) · 공격(빨강) 범위를 보인다.</summary>
    public bool ShowRange { get; set; } = true;

    /// <summary>전투 위 줄의 조작법 · 그 밑 승패 조건 — 설정에서 켜고 끈다(윈도우판, 기본 보임).</summary>
    public bool ShowHelp { get; set; } = true;
    public bool ShowGoals { get; set; } = true;

    /// <summary>NPC 아군(컴퓨터가 움직이는 우리 편)도 내가 움직인다 — 설정(사용자 요청). 끄면 원작처럼 컴퓨터 아군 차례에 컴퓨터가.</summary>
    public bool ControlAllies { get; set; }

    /// <summary>폰 지도 배율(1~4, 두 손가락으로 바꾼다) — 기본 2.</summary>
    public int TouchZoom { get; set; } = 2;

    /// <summary>폰 「고를 때 확대」 — 우리 편을 고르거나 걸은 뒤 메뉴 동안 지도를 두 배로(사용자 요청). 기본 끔.</summary>
    public bool TouchFocusZoom { get; set; }

    /// <summary>윈도우판을 폰과 같은 화면으로(큰 글 · ESC · 메뉴 단추 · 지도 끌기) — 설정 「모바일 화면으로 보기」(사용자 요청). 폰은 늘 이 화면.</summary>
    public bool MobileView { get; set; }

    /// <summary>폰에서 보일 것 — 기본은 모두 숨김, 화면의 메뉴 단추 → 모바일 메뉴에서 켠다(사용자 요청). 윈도우판은 늘 보인다.</summary>
    public bool TouchTileInfo { get; set; }
    public bool TouchUnitInfo { get; set; }
    public bool TouchChests { get; set; }
    public bool TouchGoals { get; set; }
    public bool TouchHelp { get; set; }


    /// <summary>전투 미니맵(오른쪽 위, M 키 · 아이콘) — 기본은 접음(사용자 요청).</summary>
    public bool ShowMinimap { get; set; }

    /// <summary>안 연 보물상자 목록(왼쪽 위, T 키) — 창세기전3 처럼 상자마다 든 것을 보인다.</summary>
    public bool ShowChests { get; set; } = true;

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
