using System.Text.Json;

namespace HaloLyrics;

internal static class Program
{
    public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HaloLyrics");
    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(DataDir);
        try
        {
            if (args.Contains("--probe-media") || args.Contains("--probe-media-fast"))
            {
                var reader = new MediaReader();
                var fast = args.Contains("--probe-media-fast");
                for (var i = 0; i < (fast ? 100 : 10); i++)
                {
                    Console.WriteLine(JsonSerializer.Serialize(reader.ReadAsync().GetAwaiter().GetResult()));
                    Thread.Sleep(fast ? 100 : 1000);
                }
                return 0;
            }
            if (args.Contains("--probe-device") || args.Contains("--test-screen"))
            {
                foreach (var d in PixelBar.Devices())
                    Console.WriteLine($"A160 input={d.GetMaxInputReportLength()} output={d.GetMaxOutputReportLength()} feature={d.GetMaxFeatureReportLength()} collection={d.DevicePath.Split('&').FirstOrDefault(x => x.StartsWith("col"))}");
                using var bar = new PixelBar();
                bar.Connect();
                Console.WriteLine("Screen state: " + Convert.ToHexString(bar.SavedLayout!));
                Console.WriteLine("Saved text payload bytes: " + bar.SavedText!.Length);
                if (args.Contains("--test-screen"))
                {
                    try
                    {
                        bar.Show("苹果歌词同步测试");
                        Console.WriteLine("Test text sent. Restoring in 12 seconds.");
                        Thread.Sleep(12000);
                        var readback = bar.Query(0xE7, [0]);
                        Console.WriteLine("Readback UTF8: " + System.Text.Encoding.UTF8.GetString(readback.AsSpan(4, readback[3])));
                    }
                    finally { bar.Restore(); }
                }
                return 0;
            }
            if (args.Contains("--test-effects"))
            {
                using var bar = new PixelBar(); bar.Connect();
                try
                {
                    for (var preset = 0; preset < 5; preset++)
                    {
                        bar.Show("歌词动画效果" + (preset + 1), preset);
                        Thread.Sleep(1200);
                        var state = bar.Query(0xEE, []);
                        var text = PixelBar.ParseTextState(bar.Query(0xE7, [PixelBar.LyricSource]), PixelBar.LyricSource);
                        // E7 exposes capability metadata for channel 0E, not its transient overlay text.
                        Console.WriteLine($"Preset {preset} acknowledged: state={Convert.ToHexString(state)} nativeChannel={text[0]:X2}");
                        bar.Show("苹果原词同步", preset);
                        Thread.Sleep(1200);
                    }
                    bar.ShowRhythm(); Thread.Sleep(6000);
                    Console.WriteLine("Rhythm state: " + Convert.ToHexString(bar.Query(0xEE, [])));
                }
                finally { bar.Restore(); Console.WriteLine("Original screen and both text buffers restored."); }
                return 0;
            }
            if (args.Contains("--test-reconnect"))
            {
                ScreenSnapshot? original = null;
                var restored = false;
                try
                {
                    using (var first = new PixelBar())
                    {
                        first.Connect(); original = first.Snapshot(); LocalStore.SaveScreen(original);
                        first.ShowRhythm();
                    }
                    using var reconnected = new PixelBar();
                    reconnected.Connect(); reconnected.UseSnapshot(original);
                    reconnected.Restore(); restored = true; LocalStore.ClearScreen();
                    Console.WriteLine("HID handle reopened; original scene, animation and text state verified.");
                }
                finally
                {
                    if (!restored && original is not null)
                    {
                        using var recovery = new PixelBar(); recovery.Connect(); recovery.UseSnapshot(original);
                        recovery.Restore(); LocalStore.ClearScreen();
                    }
                }
                return 0;
            }
            ApplicationConfiguration.Initialize();
            using var instance = new Mutex(true, "Local\\HaloLyrics.App", out var firstInstance);
            if (!firstInstance) { MessageBox.Show("歌词同步工具已经在运行，请双击系统托盘图标。", "Apple Music 原词同步"); return 0; }
            Application.Run(args.Contains("--login") ? new AuthProbeForm() : new MainForm());
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }
}
