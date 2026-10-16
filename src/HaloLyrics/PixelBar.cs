using HidSharp;
using System.Text;
using System.Security.Cryptography;

namespace HaloLyrics;

public sealed class PixelBar : IDisposable
{
    public const byte LyricSource = 0x0E;
    private HidStream? stream;
    private byte protocol;
    private bool textModeSet;
    private bool rhythmMode;
    private int selectedAnimation = int.MinValue;
    public byte[]? SavedLayout { get; private set; }
    public byte[]? SavedText { get; private set; }
    public byte[]? SavedLyricText { get; private set; }
    public string DeviceFingerprint { get; private set; } = "";

    public static IEnumerable<HidDevice> Devices() => DeviceList.Local.GetHidDevices(0x2D99, 0xA160)
        .Where(d => d.DevicePath.Contains("mi_04", StringComparison.OrdinalIgnoreCase)
            && d.GetMaxInputReportLength() == 64 && d.GetMaxOutputReportLength() == 64
            && d.GetReportDescriptor().DeviceItems.Any(i => i.Usages.GetAllValues().Contains(0xFF140001u)));

    public void Connect()
    {
        Dispose();
        var errors = new List<string>();
        var devices = Devices().ToList();
        if (devices.Count > 1) throw new IOException("连接了多个 A160 音响，请先仅连接需要同步的一台。");
        foreach (var device in devices)
        {
            foreach (var deviceType in new byte[] { 0xEC, 0xED })
            {
                try
                {
                    stream = device.Open();
                    stream.ReadTimeout = 700;
                    stream.WriteTimeout = 700;
                    protocol = deviceType;
                    var payload = Query(0xEE, []);
                    // A160 returns 12 bytes: base layout plus lyric enable/animation state.
                    if (payload.Length is not (9 or 12))
                        throw new IOException($"屏幕设置格式尚未验证（{payload.Length} 字节）");
                    SavedLayout = payload;
                    var textState = Query(0xE7, [0]);
                    SavedText = ParseTextState(textState);
                    SavedLyricText = ParseTextState(Query(0xE7, [LyricSource]), LyricSource);
                    DeviceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(device.DevicePath)));
                    return;
                }
                catch (Exception e)
                {
                    errors.Add($"{deviceType:X2}/{device.GetMaxOutputReportLength()}: {e.Message}");
                    Dispose();
                }
            }
        }
        throw new IOException("无法验证 A160 屏幕接口：" + string.Join("; ", errors));
    }

    public static byte[] ParseTextState(byte[] state, byte source = 0)
    {
        // Query payload: source, character capacity, packet capacity, UTF-8 length, text.
        if (state.Length < 4 || state[3] > state.Length - 4 || state[3] > 48 || state[0] != source)
            throw new IOException("文本状态格式尚未验证");
        _ = new UTF8Encoding(false, true).GetString(state, 4, state[3]);
        return [state[0], state[3], .. state.AsSpan(4, state[3]).ToArray()];
    }

    public ScreenSnapshot Snapshot() => new(DeviceFingerprint, (byte[])SavedLayout!.Clone(), (byte[])SavedText!.Clone(), (byte[])SavedLyricText!.Clone());
    public void UseSnapshot(ScreenSnapshot snapshot)
    {
        if (snapshot.Fingerprint != DeviceFingerprint) throw new IOException("屏幕恢复记录属于另一台设备。");
        if (snapshot.Layout.Length is not (9 or 12) || snapshot.Text.Length is < 2 or > 50
            || snapshot.Text[0] != 0 || snapshot.Text[1] != snapshot.Text.Length - 2)
            throw new IOException("屏幕恢复记录无效。");
        SavedLayout = (byte[])snapshot.Layout.Clone(); SavedText = (byte[])snapshot.Text.Clone();
        if (snapshot.LyricText is not null)
        {
            if (snapshot.LyricText.Length is < 2 or > 50 || snapshot.LyricText[0] != LyricSource
                || snapshot.LyricText[1] != snapshot.LyricText.Length - 2) throw new IOException("歌词恢复记录无效。");
            SavedLyricText = (byte[])snapshot.LyricText.Clone();
        }
    }

    public byte[] Query(byte command, byte[] payload)
    {
        if (stream is null) throw new IOException("音响未连接");
        DrainResponses();
        stream.Write(Packet(protocol, command, payload));
        return ReadResponse(command, command == 0xE7 && payload.Length > 0 ? payload[0] : null);
    }
    private byte[] ReadResponse(byte command, byte? source = null)
    {
        if (stream is null) throw new IOException("音响未连接");
        var until = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < until)
        {
            var response = new byte[stream.Device.GetMaxInputReportLength()];
            var count = stream.Read(response, 0, response.Length);
            if (count < 7 || response[0] != 0x2F || response[1] is not (0xBB or 0xCC) || response[3] != command) continue;
            var length = (response[4] << 8) | response[5];
            if (length > count - 7) throw new IOException("屏幕回应长度错误");
            if ((byte)response.Skip(1).Take(length + 5).Sum(b => b) != response[length + 6])
                throw new IOException("屏幕回应校验失败");
            if (source.HasValue && (length == 0 || response[6] != source)) continue;
            return response.AsSpan(6, length).ToArray();
        }
        throw new IOException("屏幕回应超时");
    }

    private void Send(byte command, byte[] payload)
    {
        if (stream is null) throw new IOException("音响未连接");
        DrainResponses();
        stream.Write(Packet(protocol, command, payload));
        var acknowledgement = ReadResponse(command);
        if (acknowledgement.Length == 1 && acknowledgement[0] == 0) throw new IOException("音响未接受显示设置。");
    }
    private void DrainResponses()
    {
        if (stream is null) return;
        var timeout = stream.ReadTimeout;
        try
        {
            stream.ReadTimeout = 2;
            var report = new byte[stream.Device.GetMaxInputReportLength()];
            for (var i = 0; i < 64; i++) if (stream.Read(report, 0, report.Length) <= 0) break;
        }
        catch (TimeoutException) { }
        finally { stream.ReadTimeout = timeout; }
    }

    public void InvalidateMode() { textModeSet = false; selectedAnimation = int.MinValue; }

    public void Show(string text, int animationPreset = -1)
    {
        if (SavedLayout is null) throw new IOException("尚未保存屏幕状态");
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 48 || text.EnumerateRunes().Count() > 16)
            throw new ArgumentException("单段文字超过音响已验证的容量");
        if (rhythmMode)
        {
            var background = SavedLayout.Take(9).ToArray(); background[0] = 2;
            Send(0xEF, background); rhythmMode = false; textModeSet = false;
        }
        if (animationPreset is < -1 or > 4) throw new ArgumentOutOfRangeException(nameof(animationPreset));
        if (animationPreset >= 0)
        {
            if (!textModeSet || selectedAnimation != animationPreset || rhythmMode)
            {
                Send(0xEF, [1, SavedLayout[1], SavedLayout[2], SavedLayout[3], 0, 0, 1, 0, (byte)animationPreset]);
                textModeSet = true; selectedAnimation = animationPreset; rhythmMode = false;
            }
            Send(0xE8, [LyricSource, (byte)bytes.Length, .. bytes]);
            return;
        }
        if (!textModeSet || selectedAnimation != -1 || rhythmMode)
        {
            var layout = SavedLayout.Take(9).ToArray();
            layout[0] = 1;
            layout[4] = 0;
            layout[5] = 2;
            layout[6] = 0;
            layout[7] = 1;
            layout[8] = 0xFF;
            Send(0xEF, layout);
            // Native lyrics have priority over custom text, so disable that overlay in static mode.
            Send(0xEF, [1, SavedLayout[1], SavedLayout[2], SavedLayout[3], 0, 0, 0, 255, 255]);
            textModeSet = true;
            selectedAnimation = -1; rhythmMode = false;
        }
        Send(0xE8, [0, (byte)bytes.Length, .. bytes]);
    }

    public void ShowRhythm()
    {
        if (rhythmMode) return;
        if (SavedLayout is null) throw new IOException("尚未保存屏幕状态");
        Send(0xE8, [LyricSource, 0]);
        Send(0xEF, [1, SavedLayout[1], SavedLayout[2], SavedLayout[3], 0, 0, 0, 255, 255]);
        Send(0xEF, [2, SavedLayout[1], SavedLayout[2], SavedLayout[3], 0, 1, 8, 0, 255]);
        rhythmMode = true; textModeSet = false;
    }

    public void Restore()
    {
        if (stream is null || SavedLayout is null) throw new IOException("音响未连接，暂时无法恢复屏幕。");
        if (SavedText is { Length: >= 2 }) Send(0xE8, SavedText);
        if (SavedLyricText is { Length: >= 2 }) Send(0xE8, SavedLyricText);
        if (SavedLayout.Length == 12)
            Send(0xEF, [1, SavedLayout[1], SavedLayout[2], SavedLayout[3], 0, 0, SavedLayout[9], SavedLayout[10], SavedLayout[11]]);
        var layout = SavedLayout.Take(9).ToArray();
        layout[0] = 2;
        // Scene selection requires selector 2; selector 1 alone leaves custom text active.
        Send(0xEF, layout);
        textModeSet = false;
        rhythmMode = false; selectedAnimation = int.MinValue;
        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(150);
            var state = Query(0xEE, []);
            var text = ParseTextState(Query(0xE7, [0]));
            var lyric = ParseTextState(Query(0xE7, [LyricSource]), LyricSource);
            if (state.SequenceEqual(SavedLayout) && text.SequenceEqual(SavedText!) && lyric.SequenceEqual(SavedLyricText!)) return;
        }
        throw new IOException("音响尚未确认恢复原屏幕设置。");
    }

    public static byte[] Packet(byte protocol, byte command, byte[] payload)
    {
        if (payload.Length > 57) throw new ArgumentException("HID payload too long");
        var data = new byte[64];
        data[0] = 0x2E; data[1] = 0xAA; data[2] = protocol; data[3] = command;
        data[4] = (byte)(payload.Length >> 8); data[5] = (byte)payload.Length;
        payload.CopyTo(data, 6);
        data[payload.Length + 6] = (byte)data.Skip(1).Take(payload.Length + 5).Sum(b => b);
        return data;
    }

    public void Dispose() { stream?.Dispose(); stream = null; textModeSet = false; rhythmMode = false; selectedAnimation = int.MinValue; }
}
public sealed record ScreenSnapshot(string Fingerprint, byte[] Layout, byte[] Text, byte[]? LyricText = null);
