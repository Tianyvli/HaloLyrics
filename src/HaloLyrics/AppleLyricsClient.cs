using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace HaloLyrics;

public sealed record CatalogSong(string Id, string Name, string Artist, string Album, double Duration, bool HasLyrics)
{
    public override string ToString() => $"{Name}　—　{Artist}　|　{Album}　|　{TimeSpan.FromSeconds(Duration):m\\:ss}";
}
public sealed record SongLyrics(CatalogSong Song, List<LyricLine> Lines);

public sealed class AppleLyricsClient : IDisposable
{
    private readonly HttpClient http;
    public string? Storefront { get; private set; }
    public AppleLyricsClient(AppleCredentials credentials, HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", credentials.Bearer);
        http.DefaultRequestHeaders.TryAddWithoutValidation("media-user-token", credentials.UserToken);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Music-User-Token", credentials.UserToken);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://music.apple.com");
        http.DefaultRequestHeaders.Referrer = new("https://music.apple.com/");
    }
    private async Task<JsonDocument> GetAsync(string path, CancellationToken token)
    {
        using var response = await http.GetAsync("https://amp-api.music.apple.com/v1/" + path, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new AppleLoginException("Apple 登录已过期或订阅不可用，请重新登录。");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("苹果未提供这首歌的同步歌词。");
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Apple 接口 HTTP {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    public async Task InitializeAsync(CancellationToken token)
    {
        if (Storefront is not null) return;
        using var json = await GetAsync("me/storefront", token);
        var value = json.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
        if (!value.All(char.IsAsciiLetter)) throw new InvalidOperationException("Apple 账号地区无效。");
        Storefront = value;
    }
    public async Task<List<CatalogSong>> SearchAsync(Playback playback, CancellationToken token)
    {
        await InitializeAsync(token);
        var term = Uri.EscapeDataString(playback.Title + " " + playback.Artist);
        using var json = await GetAsync($"catalog/{Storefront}/search?types=songs&limit=25&l=zh-Hans-CN&term={term}", token);
        if (!json.RootElement.GetProperty("results").TryGetProperty("songs", out var songs)) return [];
        return songs.GetProperty("data").EnumerateArray().Select(ParseSong).Where(x => x is not null).Cast<CatalogSong>().ToList();
    }
    public async Task<CatalogSong> GetSongAsync(string id, CancellationToken token)
    {
        await InitializeAsync(token);
        if (!id.All(char.IsAsciiDigit)) throw new InvalidOperationException("曲目 ID 无效。");
        using var json = await GetAsync($"catalog/{Storefront}/songs/{id}?l=zh-Hans-CN", token);
        return ParseSong(json.RootElement.GetProperty("data")[0]) ?? throw new InvalidOperationException("Apple 曲目信息不可用。");
    }
    private static CatalogSong? ParseSong(JsonElement song)
    {
        var a = song.GetProperty("attributes");
        var id = song.GetProperty("id").GetString()!;
        if (!id.All(char.IsAsciiDigit)) return null;
        return new(id, a.GetProperty("name").GetString()!, a.GetProperty("artistName").GetString()!,
            a.GetProperty("albumName").GetString()!, a.GetProperty("durationInMillis").GetDouble() / 1000,
            a.TryGetProperty("hasLyrics", out var lyrics) && lyrics.GetBoolean());
    }
    public async Task<SongLyrics> LoadAsync(CatalogSong song, CancellationToken token)
    {
        await InitializeAsync(token);
        var directory = LocalStore.PathFor("Lyrics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Storefront}.{song.Id}.ttml");
        if (File.Exists(path))
        {
            try
            {
                var cached = Lyrics.Parse(await File.ReadAllTextAsync(path, token));
                if (cached.Count > 0) return new(song, cached);
            }
            catch (Exception e) when (e is IOException or System.Xml.XmlException) { LocalStore.Log("歌词缓存不可读，重新获取。"); }
        }
        using var json = await GetAsync($"catalog/{Storefront}/songs/{song.Id}/syllable-lyrics", token);
        var ttml = Lyrics.ExtractTtml(json.RootElement);
        var lines = Lyrics.Parse(ttml);
        if (lines.Count == 0) throw new InvalidOperationException("苹果未提供这首歌的同步时间轴。");
        token.ThrowIfCancellationRequested();
        LocalStore.WriteAtomic(path, ttml);
        return new(song, lines);
    }
    public static bool ExactMatch(Playback playback, CatalogSong song) =>
        Normalize(playback.Title) == Normalize(song.Name) && Normalize(playback.Artist) == Normalize(song.Artist)
        && Normalize(playback.Album) == Normalize(song.Album) && playback.Duration > 0
        && Math.Abs(playback.Duration - song.Duration) <= 2;
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
    public void Dispose() => http.Dispose();
}
public sealed class AppleLoginException(string message) : Exception(message);
