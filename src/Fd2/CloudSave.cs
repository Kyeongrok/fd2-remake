using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fd2;

/// <summary>
/// 세이브 연동 — 사용자의 구글 계정으로 로그인해(OAuth 데스크톱 앱, PKCE, 브라우저 → 127.0.0.1 되돌아오기) 구글 드라이브의 앱 전용 칸(appDataFolder)에
/// 기록 폴더의 *.SAV 를 zip 하나로 올리고 받는다. 앱 전용 칸은 사용자 드라이브 목록에 보이지 않고 이 앱만 읽는다. PC · 폰이 같은 코드.
/// </summary>
/// <remarks>구글 클라우드 콘솔에서 만든 「데스크톱 앱」 OAuth 클라이언트가 있어야 한다 — 빌드에 넣은 google-client.txt 또는 FD2_GOOGLE_CLIENT_ID · FD2_GOOGLE_CLIENT_SECRET.</remarks>
internal sealed class CloudSave(string tokenPath, Action<string> openBrowser)
{
    private const string Scope = "https://www.googleapis.com/auth/drive.appdata";

    /// <summary>빌드가 넣은 google-client.txt(첫 줄 ID · 둘째 줄 보안 비밀) — 저장소에는 없다.</summary>
    private static readonly string[] Embedded = ReadEmbedded();

    private static string[] ReadEmbedded()
    {
        using var s = typeof(CloudSave).Assembly.GetManifestResourceStream("Fd2.google-client.txt");
        if (s == null) return [];
        using var r = new StreamReader(s);
        return r.ReadToEnd().Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ClientId => Environment.GetEnvironmentVariable("FD2_GOOGLE_CLIENT_ID") is { Length: > 0 } v ? v : Embedded.ElementAtOrDefault(0) ?? "";
    private static string ClientSecret => Environment.GetEnvironmentVariable("FD2_GOOGLE_CLIENT_SECRET") is { Length: > 0 } v ? v : Embedded.ElementAtOrDefault(1) ?? "";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static bool Configured => ClientId.Length > 0;
    public bool LoggedIn => File.Exists(tokenPath);

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>브라우저로 구글 로그인 — 돌아온 코드를 토큰으로 바꿔 갱신 토큰을 저장한다. 3분 안에 끝내지 않으면 실패.</summary>
    public async Task Login()
    {
        string verifier = B64Url(RandomNumberGenerator.GetBytes(32)), state = B64Url(RandomNumberGenerator.GetBytes(16));
        string challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            openBrowser("https://accounts.google.com/o/oauth2/v2/auth?response_type=code&access_type=offline&prompt=consent"
                + $"&client_id={Uri.EscapeDataString(ClientId)}&redirect_uri={Uri.EscapeDataString(redirect)}&scope={Uri.EscapeDataString(Scope)}"
                + $"&state={state}&code_challenge={challenge}&code_challenge_method=S256");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            string? code = null;
            while (code == null)
            {
                using var client = await listener.AcceptTcpClientAsync(cts.Token);
                code = await ReadRedirect(client, state, cts.Token);
            }
            using var reply = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["code"] = code, ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code", ["redirect_uri"] = redirect,
            }));
            reply.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
            string refresh = doc.RootElement.GetProperty("refresh_token").GetString() ?? throw new IOException("갱신 토큰이 없습니다");
            Directory.CreateDirectory(Path.GetDirectoryName(tokenPath)!);
            await File.WriteAllTextAsync(tokenPath, refresh);
        }
        finally { listener.Stop(); }
    }

    /// <summary>브라우저가 되돌아온 요청 하나 — 맞는 state 의 code 면 돌려주고, 파비콘 같은 다른 요청이면 null.</summary>
    private static async Task<string?> ReadRedirect(TcpClient client, string state, CancellationToken ct)
    {
        await using var net = client.GetStream();
        var buf = new byte[8192];
        int n = await net.ReadAsync(buf, ct);
        string first = Encoding.ASCII.GetString(buf, 0, n).Split("\r\n")[0];   // GET /?state=…&code=… HTTP/1.1
        int q = first.IndexOf('?'), end = first.LastIndexOf(' ');
        var args = (q < 0 || end <= q ? "" : first[(q + 1)..end]).Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        bool ours = args.GetValueOrDefault("state") == state;
        string text = !ours ? "" : args.ContainsKey("code") ? "로그인했습니다. 이 창을 닫고 게임으로 돌아가세요." : "로그인하지 못했습니다.";
        byte[] page = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>용의기사2</title><p>{text}</p>");
        await net.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {page.Length}\r\nConnection: close\r\n\r\n"), ct);
        await net.WriteAsync(page, ct);
        if (!ours) return null;
        return args.TryGetValue("code", out var code) ? code : throw new IOException("로그인을 취소했습니다");
    }

    public void Logout()
    {
        try { File.Delete(tokenPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private async Task<string> AccessToken()
    {
        using var reply = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["refresh_token"] = (await File.ReadAllTextAsync(tokenPath)).Trim(), ["grant_type"] = "refresh_token",
        }));
        reply.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token)
    {
        var m = new HttpRequestMessage(method, url);
        m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return m;
    }

    /// <summary>앱 전용 칸의 그 이름 파일 id — 없으면 null.</summary>
    private static async Task<string?> Find(string name, string token)
    {
        string q = Uri.EscapeDataString($"name='{name.Replace("\\", "\\\\").Replace("'", "\\'")}'");
        using var m = Request(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&fields=files(id)&q={q}", token);
        using var reply = await Http.SendAsync(m);
        reply.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        var files = doc.RootElement.GetProperty("files");
        return files.GetArrayLength() == 0 ? null : files[0].GetProperty("id").GetString();
    }

    /// <summary>기록 폴더의 *.SAV 를 올린다 — 올린 파일 수. 클라우드의 같은 버전 기록은 바뀐다.</summary>
    public async Task<int> Upload(string dir, string key)
    {
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.SAV") : [];
        if (files.Length == 0) return 0;
        using var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var f in files) zip.CreateEntryFromFile(f, Path.GetFileName(f));
        string token = await AccessToken(), name = $"saves-{key}.zip";
        var bytes = new ByteArrayContent(zipped.ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        HttpRequestMessage m;
        if (await Find(name, token) is { } id)
        {
            m = Request(HttpMethod.Patch, $"https://www.googleapis.com/upload/drive/v3/files/{id}?uploadType=media", token);
            m.Content = bytes;
        }
        else
        {
            m = Request(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart", token);
            var meta = new StringContent(JsonSerializer.Serialize(new { name, parents = new[] { "appDataFolder" } }), Encoding.UTF8, "application/json");
            m.Content = new MultipartContent("related") { meta, bytes };
        }
        using (m)
        using (var reply = await Http.SendAsync(m)) reply.EnsureSuccessStatusCode();
        return files.Length;
    }

    /// <summary>클라우드 기록을 받는다 — 지금 기록은 「백업-시각」 폴더에 옮겨 둔다. 받은 파일 수, 클라우드에 없으면 -1.</summary>
    public async Task<int> Download(string dir, string key)
    {
        string token = await AccessToken();
        if (await Find($"saves-{key}.zip", token) is not { } id) return -1;
        using var m = Request(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{id}?alt=media", token);
        using var reply = await Http.SendAsync(m);
        reply.EnsureSuccessStatusCode();
        var data = await reply.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);   // 망가진 파일이면 여기서 터져 지금 기록은 그대로다
        var entries = zip.Entries.Where(e => e.Name == e.FullName && e.Name.EndsWith(".SAV", StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0) throw new InvalidDataException("클라우드 기록이 비었습니다");
        Directory.CreateDirectory(dir);
        string backup = Path.Combine(dir, $"백업-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backup);
        foreach (var f in Directory.GetFiles(dir, "*.SAV")) File.Copy(f, Path.Combine(backup, Path.GetFileName(f)));
        foreach (var f in Directory.GetFiles(dir, "*.SAV")) File.Delete(f);
        foreach (var e in entries) e.ExtractToFile(Path.Combine(dir, e.Name));
        return entries.Count;
    }
}
