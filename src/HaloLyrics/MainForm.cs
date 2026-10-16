using System.Diagnostics;

namespace HaloLyrics;

public sealed class MainForm : Form
{
    private readonly Settings settings = LocalStore.LoadSettings();
    private readonly MediaReader media = new();
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Information, Text = "Apple Music 原词同步", Visible = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 55, Padding = new(12), Text = "准备就绪；点击开始同步。" };
    private readonly Label trackLabel = new() { Dock = DockStyle.Top, Height = 55, Padding = new(12) };
    private readonly Label lyricLabel = new() { Dock = DockStyle.Fill, Padding = new(18), Font = new("Microsoft YaHei UI", 16), Text = "等待 Apple Music 播放" };
    private readonly Button start = new() { Text = "开始同步", AutoSize = true };
    private readonly Button pause = new() { Text = "暂停同步", AutoSize = true };
    private readonly Button login = new() { Text = "Apple 登录", AutoSize = true };
    private readonly Button retry = new() { Text = "重新匹配", AutoSize = true };
    private AppleLyricsClient? client;
    private PixelBar? bar;
    private Playback? playback;
    private SongLyrics? lyrics;
    private CancellationTokenSource? loading;
    private string? activeKey;
    private string? sent;
    private bool running;
    private bool polling;
    private bool exiting;
    private bool loginOpen;
    private DateTime nextDeviceAttempt;
    private int mediaErrors;
    private readonly DateTimeOffset started = DateTimeOffset.Now;
    private int lyricChanges;
    private DateTime nextHeartbeat;
    private DateTime nextTempoCheck;
    private ScreenSnapshot? originalScreen;
    private string lastRestoreError = "";

    public MainForm()
    {
        Text = "Apple Music 原词同步 · Halo PixelBar";
        Size = new(900, 560);
        MinimumSize = new(800, 500);
        StartPosition = FormStartPosition.CenterScreen;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new(8) };
        buttons.Controls.AddRange([start, pause, login, retry]);
        var exit = new Button { Text = "退出并恢复屏幕", AutoSize = true };
        exit.Click += (_, _) => { exiting = true; Close(); };
        buttons.Controls.Add(exit);
        var effects = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 92, Padding = new(8), FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        effects.Controls.Add(new Label { Text = "歌词进出动画", AutoSize = true, Padding = new(0, 5, 0, 0) });
        var animation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 560 };
        animation.Items.AddRange(["无动画（静态）", "底部往上进入 → 底部往上退出", "中间扩散进入 → 两边收缩退出", "左往右进入 → 右往左退出", "底部往上进入 → 顶部往下退出", "顶部往下进入 → 右往左退出"]);
        animation.SelectedIndex = Math.Clamp(settings.AnimationPreset + 1, 0, 5);
        animation.SelectedIndexChanged += (_, _) => { settings.AnimationPreset = animation.SelectedIndex - 1; LocalStore.SaveSettings(settings); bar?.InvalidateMode(); sent = null; };
        effects.Controls.Add(animation);
        effects.SetFlowBreak(animation, true);
        var interlude = new CheckBox { Text = "长间奏显示音乐律动", Checked = settings.InterludeAnimation, AutoSize = true, Margin = new(3, 10, 20, 3) };
        effects.Controls.Add(interlude);
        effects.Controls.Add(new Label { Text = "间奏至少（秒）", AutoSize = true, Padding = new(0, 10, 0, 0) });
        var gap = new NumericUpDown { Minimum = 3, Maximum = 30, Value = Math.Clamp(settings.InterludeSeconds, 3, 30), Width = 65, Margin = new(3, 8, 3, 3) };
        effects.Controls.Add(gap);
        interlude.CheckedChanged += (_, _) => { settings.InterludeAnimation = interlude.Checked; LocalStore.SaveSettings(settings); sent = null; };
        gap.ValueChanged += (_, _) => { settings.InterludeSeconds = (int)gap.Value; LocalStore.SaveSettings(settings); sent = null; };
        var offsets = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new(8) };
        offsets.Controls.Add(new Label { Text = "歌词提前（毫秒）", AutoSize = true, Padding = new(0, 5, 0, 0) });
        var offset = new NumericUpDown { Minimum = -10000, Maximum = 10000, Increment = 50, Value = Math.Clamp(settings.OffsetMilliseconds, -10000, 10000), Width = 100 };
        offset.ValueChanged += (_, _) => { settings.OffsetMilliseconds = (int)offset.Value; LocalStore.SaveSettings(settings); };
        offset.TextChanged += (_, _) =>
        {
            if (decimal.TryParse(offset.Text, out var parsed) && parsed >= offset.Minimum && parsed <= offset.Maximum
                && decimal.Truncate(parsed) == parsed) offset.Value = parsed;
        };
        offsets.Controls.Add(offset);
        offsets.Controls.Add(new Label { Text = "正值提前，负值延后；关闭窗口后继续在托盘运行", AutoSize = true, Padding = new(16, 5, 0, 0) });
        Controls.Add(lyricLabel); Controls.Add(trackLabel); Controls.Add(status); Controls.Add(buttons); Controls.Add(effects); Controls.Add(offsets);
        start.Click += async (_, _) => await StartAsync();
        pause.Click += (_, _) => StopSync();
        login.Click += async (_, _) => await LoginAsync();
        retry.Click += (_, _) => { activeKey = null; lyrics = null; RestoreScreen(); };
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
        menu.Items.Add("开始同步", null, async (_, _) => await StartAsync());
        menu.Items.Add("暂停同步", null, (_, _) => StopSync());
        menu.Items.Add("重新登录 Apple", null, async (_, _) => await LoginAsync());
        menu.Items.Add("退出并恢复屏幕", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWindow();
        timer.Tick += async (_, _) => await TickAsync();
        FormClosing += OnClosing;
        var credentials = LocalStore.LoadCredentials();
        if (credentials is not null) client = new(credentials);
        Shown += async (_, _) => { timer.Start(); if (client is null) await LoginAsync(); };
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
        LocalStore.Log("程序启动；等待手动开始。");
    }
    private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
    {
        if (IsHandleCreated && !IsDisposed) Invoke(StopSync);
    }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void SetStatus(string text)
    {
        if (status.Text != text) LocalStore.Log(text);
        status.Text = text;
        SaveStatus();
    }
    private void SaveStatus()
    {
        LocalStore.WriteAtomic(LocalStore.PathFor("status.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            running, status = status.Text, title = playback?.Title, artist = playback?.Artist, position = playback?.Position,
            source = "Apple Music", lyricLines = lyrics?.Lines.Count ?? 0, lyricChanges, started, observedAt = DateTimeOffset.Now
            , offsetMilliseconds = settings.OffsetMilliseconds, playing = playback?.Playing
            , animationPreset = settings.AnimationPreset, interludeAnimation = settings.InterludeAnimation, screenMode = sent == "\0rhythm" ? "rhythm" : "lyrics"
        }));
    }
    private async Task LoginAsync()
    {
        if (loginOpen) return;
        loginOpen = true;
        StopSync();
        try
        {
            using var form = new AuthProbeForm();
            form.ShowDialog(this);
            var credentials = form.Credentials ?? LocalStore.LoadCredentials();
            if (credentials is not null)
            {
                client?.Dispose(); client = new(credentials);
                await client.InitializeAsync(CancellationToken.None);
                SetStatus("Apple 登录有效；点击开始同步。");
            }
            else SetStatus("尚未完成 Apple 登录。");
        }
        catch (Exception e) { SetStatus(SafeError(e)); }
        finally { loginOpen = false; }
    }
    private static bool TempoHubRunning()
    {
        var processes = Process.GetProcessesByName("EDIFIER TempoHub");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private async Task StartAsync()
    {
        if (running || loginOpen) return;
        if (client is null) { await LoginAsync(); if (client is null) return; }
        if (TempoHubRunning())
        {
            SetStatus("请从系统托盘退出 TempoHub，再点击开始，避免两个程序争抢屏幕。音频驱动无需卸载。");
            ShowWindow(); return;
        }
        try
        {
            bar = new(); bar.Connect();
            originalScreen = LocalStore.LoadScreen() ?? bar.Snapshot();
            bar.UseSnapshot(originalScreen);
            LocalStore.SaveScreen(originalScreen);
            running = true; activeKey = null; sent = null;
            SetStatus("已连接 A160；正在读取 Apple Music。");
        }
        catch (Exception e) { bar?.Dispose(); bar = null; SetStatus(SafeError(e)); }
    }
    private void StopSync()
    {
        running = false; loading?.Cancel(); lyrics = null; activeKey = null;
        var restored = RestoreScreen(); bar?.Dispose(); bar = null; sent = null;
        if (restored && originalScreen is not null) { LocalStore.ClearScreen(); originalScreen = null; }
        SetStatus(restored ? "同步已暂停；音响屏幕已释放。" : "同步已暂停，屏幕恢复未确认：" + lastRestoreError);
    }
    private bool RestoreScreen()
    {
        if (bar is null) return originalScreen is null;
        try { bar.Restore(); sent = null; return true; }
        catch (Exception e) when (e is IOException or TimeoutException)
        {
            lastRestoreError = e.Message;
            LocalStore.Log("屏幕恢复未确认：" + e.GetType().Name);
            return false;
        }
    }
    private async Task TickAsync()
    {
        if (polling || exiting || loginOpen) return;
        polling = true;
        try
        {
            playback = await media.ReadAsync();
            mediaErrors = 0;
            if (DateTime.UtcNow >= nextHeartbeat) { SaveStatus(); nextHeartbeat = DateTime.UtcNow.AddSeconds(1); }
            trackLabel.Text = playback is null ? "未检测到 Apple Music 播放会话" : $"{playback.Title} — {playback.Artist}　{TimeSpan.FromSeconds(playback.Position):m\\:ss} / {TimeSpan.FromSeconds(playback.Duration):m\\:ss}";
            if (!running) return;
            if (DateTime.UtcNow >= nextTempoCheck)
            {
                nextTempoCheck = DateTime.UtcNow.AddSeconds(3);
                if (TempoHubRunning()) { StopSync(); SetStatus("检测到 TempoHub 启动，已暂停以避免争抢屏幕。"); return; }
            }
            if (bar is null && DateTime.UtcNow >= nextDeviceAttempt)
            {
                try
                {
                    bar = new(); bar.Connect();
                    if (originalScreen is not null) bar.UseSnapshot(originalScreen);
                    sent = null; SetStatus("音响已重新连接。");
                }
                catch (Exception e) { bar?.Dispose(); bar = null; nextDeviceAttempt = DateTime.UtcNow.AddSeconds(3); SetStatus(SafeError(e)); }
            }
            if (playback is null || playback.Title.Length == 0)
            {
                loading?.Cancel(); activeKey = null; lyrics = null;
                if (sent is not null) RestoreScreen();
                lyricLabel.Text = "等待 Apple Music 播放";
                return;
            }
            if (activeKey != playback.Key)
            {
                activeKey = playback.Key; lyrics = null; loading?.Cancel();
                loading?.Dispose(); loading = new();
                if (sent is not null) RestoreScreen();
                lyricLabel.Text = "正在获取苹果原词……";
                _ = LoadSongAsync(playback, loading.Token);
            }
            Render();
        }
        catch (Exception e)
        {
            if (++mediaErrors >= 3) { lyrics = null; if (sent is not null) RestoreScreen(); }
            SetStatus(SafeError(e));
        }
        finally { polling = false; }
    }
    private async Task LoadSongAsync(Playback track, CancellationToken token)
    {
        try
        {
            SetStatus("正在确认苹果曲目版本和原词……");
            CatalogSong? song = null;
            await client!.InitializeAsync(token);
            if (settings.Mappings.TryGetValue(track.Key, out var map) && map.Storefront == client.Storefront)
                song = await client.GetSongAsync(map.Id, token);
            else
            {
                var songs = await client.SearchAsync(track, token);
                token.ThrowIfCancellationRequested();
                var exact = songs.Where(x => AppleLyricsClient.ExactMatch(track, x)).ToList();
                if (exact.Count == 1) song = exact[0];
                else if (songs.Count > 0)
                {
                    ShowWindow();
                    using var picker = new SongPicker(track, songs);
                    using var registration = token.Register(() => { if (!picker.IsDisposed && picker.IsHandleCreated) picker.BeginInvoke(picker.Close); });
                    if (picker.ShowDialog(this) != DialogResult.OK) throw new InvalidOperationException("未确认曲目版本，暂不显示歌词。");
                    token.ThrowIfCancellationRequested(); song = picker.Selected;
                }
            }
            if (song is null) throw new InvalidOperationException("苹果曲库中未找到可确认的曲目。");
            var result = await client.LoadAsync(song, token);
            token.ThrowIfCancellationRequested();
            if (activeKey != track.Key || !running) return;
            settings.Mappings[track.Key] = new(client.Storefront!, song.Id);
            LocalStore.SaveSettings(settings);
            lyrics = result;
            SetStatus($"正在同步苹果原词 · {result.Lines.Count} 行 · {song.Name}");
            Render();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (token.IsCancellationRequested || activeKey != track.Key) return;
            lyrics = null; RestoreScreen(); lyricLabel.Text = "当前没有可同步的歌词";
            SetStatus(SafeError(e));
        }
    }
    private void Render()
    {
        if (lyrics is null || playback is null || bar is null) return;
        var position = playback.Position + settings.OffsetMilliseconds / 1000.0;
        var line = TextSegments.Current(lyrics.Lines, position);
        var showRhythm = settings.InterludeAnimation && Interlude.ShouldAnimate(lyrics.Lines, position, playback.Duration, playback.Playing, settings.InterludeSeconds);
        lyricLabel.Text = line?.Text ?? (playback.Playing ? (showRhythm ? "前奏／间奏 · 音乐律动" : "前奏／间奏") : "已暂停");
        var text = showRhythm ? "\0rhythm" : line is null ? "" : TextSegments.At(line, position);
        if (text == sent) return;
        try
        {
            if (showRhythm) bar.ShowRhythm(); else bar.Show(text, settings.AnimationPreset);
            sent = text; lyricChanges++;
        }
        catch (Exception e) when (e is IOException or TimeoutException)
        {
            bar.Dispose(); bar = null; nextDeviceAttempt = DateTime.UtcNow.AddSeconds(2);
            SetStatus("音响连接中断，等待重新连接。");
        }
        catch (InvalidOperationException e)
        {
            lyrics = null; RestoreScreen(); SetStatus(e.Message);
        }
    }
    private static string SafeError(Exception e) => e switch
    {
        AppleLoginException => e.Message,
        InvalidOperationException => e.Message,
        IOException => "音响或本机文件不可用：" + e.Message,
        HttpRequestException => "Apple 网络连接失败，请稍后点击重新匹配。",
        OperationCanceledException => "Apple 请求超时，请稍后点击重新匹配。",
        _ => "暂时无法同步：" + e.GetType().Name
    };
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        timer.Stop(); StopSync(); client?.Dispose(); tray.Visible = false; tray.Dispose();
        Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
        LocalStore.Log($"程序退出；本次更新歌词 {lyricChanges} 次。");
    }
}
