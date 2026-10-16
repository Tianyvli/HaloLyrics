using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Net.Http.Headers;

namespace HaloLyrics;

public sealed class AuthProbeForm : Form
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 56, Text = "请在下面的 Apple 官方页面登录。完成后自动返回；此窗口只获取歌词授权，不播放第二路音频。", Padding = new(12) };
    private string? bearer;
    private string? userToken;
    private bool testing;
    private readonly CancellationTokenSource lifetime = new();
    public AppleCredentials? Credentials { get; private set; }
    public AuthProbeForm()
    {
        Text = "Apple Music 原词同步 · 验证登录";
        Size = new(1100, 820);
        Controls.Add(web); Controls.Add(status);
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => lifetime.Cancel();
    }
    private async Task InitializeAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions("--no-proxy-server");
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDir, "WebProfile"), options);
            await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.IsMuted = true;
            web.CoreWebView2.AddWebResourceRequestedFilter("https://amp-api.music.apple.com/*", CoreWebView2WebResourceContext.All);
            web.CoreWebView2.AddWebResourceRequestedFilter("https://api.music.apple.com/*", CoreWebView2WebResourceContext.All);
            web.CoreWebView2.WebResourceRequested += OnRequest;
            web.CoreWebView2.WebResourceResponseReceived += OnResponse;
            web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) SetStatus("Apple 页面连接失败：" + e.WebErrorStatus);
            };
            web.CoreWebView2.Navigate("https://music.apple.com/cn/new");
        }
        catch (Exception e) { SetStatus("网页登录组件启动失败：" + e.GetType().Name); }
    }
    private void SetStatus(string text)
    {
        if (IsDisposed || Disposing) return;
        status.Text = text;
        File.WriteAllText(Path.Combine(Program.DataDir, "probe-status.txt"), DateTimeOffset.Now + " " + text);
    }
    private void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var headers = e.Request.Headers;
        if (headers.Contains("Authorization")) bearer = headers.GetHeader("Authorization");
        if (headers.Contains("media-user-token")) userToken = headers.GetHeader("media-user-token");
        else if (headers.Contains("Music-User-Token")) userToken = headers.GetHeader("Music-User-Token");
        if (bearer is not null && userToken is not null && !testing)
        {
            testing = true;
            BeginInvoke(async () => await TestLyricsAsync());
        }
    }
    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) ||
                uri.Host != "amp-api.music.apple.com" || !uri.AbsolutePath.Contains("lyrics")) return;
            if (e.Response.StatusCode != 200) { SetStatus($"苹果歌词响应：HTTP {e.Response.StatusCode}"); return; }
            using var body = await e.Response.GetContentAsync();
            using var reader = new StreamReader(body);
            var json = await reader.ReadToEndAsync();
            if (json.Contains("<tt"))
            {
                ValidateLyrics(json, "网页当前歌曲");
            }
        }
        catch { SetStatus("歌词响应暂时不可读取，请重新打开歌词。"); }
    }
    private async Task TestLyricsAsync()
    {
        try
        {
            SetStatus("正在验证 Apple 登录授权……");
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", bearer);
            client.DefaultRequestHeaders.TryAddWithoutValidation("media-user-token", userToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Music-User-Token", userToken);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://music.apple.com");
            client.DefaultRequestHeaders.Referrer = new("https://music.apple.com/");
            using var storefrontResponse = await client.GetAsync("https://amp-api.music.apple.com/v1/me/storefront", lifetime.Token);
            storefrontResponse.EnsureSuccessStatusCode();
            Credentials = new(bearer!, userToken!);
            LocalStore.SaveCredentials(Credentials);
            if (Owner is not null) { SetStatus("Apple 登录成功。"); BeginInvoke(Close); return; }
            var playback = await new MediaReader().ReadAsync();
            if (playback is null) { SetStatus("Apple 登录成功；打开原生 Apple Music 后即可同步。"); return; }
            using var storefrontJson = JsonDocument.Parse(await storefrontResponse.Content.ReadAsStringAsync());
            var storefront = storefrontJson.RootElement.GetProperty("data")[0].GetProperty("id").GetString();
            var query = Uri.EscapeDataString(playback.Title + " " + playback.Artist);
            var search = await client.GetStringAsync($"https://amp-api.music.apple.com/v1/catalog/{storefront}/search?types=songs&limit=10&term={query}");
            File.WriteAllText(Path.Combine(Program.DataDir, "probe-search.json"), search);
            using var searchJson = JsonDocument.Parse(search);
            var results = searchJson.RootElement.GetProperty("results").GetProperty("songs").GetProperty("data");
            foreach (var song in results.EnumerateArray())
            {
                var a = song.GetProperty("attributes");
                var title = a.GetProperty("name").GetString() ?? "";
                if (title != playback.Title) continue;
                var id = song.GetProperty("id").GetString();
                var response = await client.GetAsync($"https://amp-api.music.apple.com/v1/catalog/{storefront}/songs/{id}/syllable-lyrics");
                if (!response.IsSuccessStatusCode) { SetStatus($"苹果原词接口 HTTP {(int)response.StatusCode}；保留验证记录。"); continue; }
                var lyrics = await response.Content.ReadAsStringAsync();
                ValidateLyrics(lyrics, title);
                return;
            }
            SetStatus("当前曲目尚未取得可验证的苹果歌词，请在网页中打开同一首歌的歌词。");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (HttpRequestException e) { testing = false; SetStatus("苹果接口验证失败：" + (e.StatusCode?.ToString() ?? "连接失败")); }
        catch (Exception e) { testing = false; SetStatus("验证未完成：" + e.GetType().Name); }
    }
    private void ValidateLyrics(string json, string title)
    {
        using var document = JsonDocument.Parse(json);
        var ttml = Lyrics.ExtractTtml(document.RootElement);
        var lines = Lyrics.Parse(ttml);
        if (lines.Count == 0) { SetStatus("苹果响应中没有可验证的同步时间轴。"); return; }
        File.WriteAllText(Path.Combine(Program.DataDir, "probe-lyrics.json"), json);
        File.WriteAllText(Path.Combine(Program.DataDir, "probe-lyrics.ttml"), ttml);
        SetStatus($"原词时间轴验证通过：《{title}》，共 {lines.Count} 行，首句时间 {lines[0].Begin:F2} 秒。");
        if (Owner is not null) Close();
    }
}
