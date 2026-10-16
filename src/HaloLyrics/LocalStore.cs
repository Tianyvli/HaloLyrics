using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HaloLyrics;

public sealed record AppleCredentials(string Bearer, string UserToken);
public sealed record SongMapping(string Storefront, string Id);
public sealed class Settings
{
    public int OffsetMilliseconds { get; set; }
    public int AnimationPreset { get; set; } = 0;
    public bool InterludeAnimation { get; set; } = true;
    public int InterludeSeconds { get; set; } = 5;
    public Dictionary<string, SongMapping> Mappings { get; set; } = [];
}

public static class LocalStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HaloLyrics.Apple.Session.v1");
    public static string PathFor(string name) => Path.Combine(Program.DataDir, name);
    public static void WriteAtomic(string path, string value)
    {
        File.WriteAllText(path + ".tmp", value, new UTF8Encoding(false));
        File.Move(path + ".tmp", path, true);
    }
    public static Settings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathFor("settings.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public static void SaveSettings(Settings settings) => WriteAtomic(PathFor("settings.json"), JsonSerializer.Serialize(settings));
    public static ScreenSnapshot? LoadScreen()
    {
        var path = PathFor("screen-restore.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<ScreenSnapshot>(File.ReadAllText(path)) ?? throw new IOException("屏幕恢复记录为空。"); }
        catch (JsonException) { throw new IOException("屏幕恢复记录无法读取，请保留该文件以便检查。"); }
    }
    public static void SaveScreen(ScreenSnapshot snapshot) => WriteAtomic(PathFor("screen-restore.json"), JsonSerializer.Serialize(snapshot));
    public static void ClearScreen() => File.Delete(PathFor("screen-restore.json"));
    public static void SaveCredentials(AppleCredentials credentials)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        WriteAtomic(PathFor("apple-session.dat"), Convert.ToBase64String(protectedBytes));
        CryptographicOperations.ZeroMemory(bytes);
    }
    public static AppleCredentials? LoadCredentials()
    {
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(PathFor("apple-session.dat"))), Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<AppleCredentials>(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception e) when (e is IOException or FormatException or CryptographicException or JsonException) { return null; }
    }
    public static void Log(string text)
    {
        var path = PathFor("activity.log");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}
