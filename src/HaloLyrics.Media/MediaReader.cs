#nullable enable
using Windows.Media.Control;

namespace HaloLyrics;

public record Playback(string Title, string Artist, string Album, double Duration, double Position,
    bool Playing, string Source, DateTimeOffset ObservedAt)
{
    public string Key => $"{Title}\n{Artist}\n{Album}\n{Math.Round(Duration)}";
    public bool SameTrack(Playback? other) => other is not null && Title == other.Title && Artist == other.Artist
        && Album == other.Album && Math.Round(Duration) == Math.Round(other.Duration);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041.0")]
public sealed class MediaReader : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private readonly PlaybackClock clock = new();
    private int sessionsDirty = 1;
    private int metadataVersion;
    private int observedMetadataVersion = -1;
    private int stateDirty = 1;
    private DateTime nextSessionScan;
    private DateTime nextMetadataRead;
    private DateTime nextStateRead;
    private string title = "", artist = "", album = "", source = "";
    private double duration, position, rate = 1;
    private DateTimeOffset updatedAt;
    private bool playing;
    public int MetadataReads { get; private set; }
    public int StateReads { get; private set; }
    public int SessionScans { get; private set; }

    public async ValueTask<Playback?> ReadAsync()
    {
        try
        {
            if (manager is null)
            {
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                manager.SessionsChanged += OnSessionsChanged;
            }
            var now = DateTime.UtcNow;
            if (Interlocked.Exchange(ref sessionsDirty, 0) != 0 || now >= nextSessionScan)
            {
                SessionScans++;
                var selected = manager.GetSessions().FirstOrDefault(s =>
                    s.SourceAppUserModelId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));
                if (!Equals(session, selected)) Attach(selected);
                nextSessionScan = now.AddSeconds(2);
            }
            if (session is null) { clock.Reset(); return null; }
            var version = Volatile.Read(ref metadataVersion);
            if (version != observedMetadataVersion || now >= nextMetadataRead)
            {
                var media = await MediaSnapshot.ReadMetadataAsync(session.TryGetMediaPropertiesAsync());
                MetadataReads++;
                title = media.Title; artist = media.Artist; album = media.Album;
                var separator = artist.IndexOf(" — ", StringComparison.Ordinal);
                if (separator >= 0)
                {
                    if (string.IsNullOrEmpty(album)) album = artist[(separator + 3)..];
                    artist = artist[..separator];
                }
                observedMetadataVersion = version;
                nextMetadataRead = now.AddSeconds(2);
                Interlocked.Exchange(ref stateDirty, 1);
            }
            if (Interlocked.Exchange(ref stateDirty, 0) != 0 || now >= nextStateRead)
            {
                var state = MediaSnapshot.Read(session);
                StateReads++;
                playing = state.Playing;
                rate = state.Rate;
                position = state.Position;
                duration = state.Duration;
                updatedAt = state.Updated;
                nextStateRead = now.AddMilliseconds(250);
            }
            var observed = DateTimeOffset.UtcNow;
            var currentPosition = position;
            if (playing && updatedAt > DateTimeOffset.UnixEpoch)
                currentPosition += Math.Max(0, (observed - updatedAt).TotalSeconds) * rate;
            return clock.Apply(new(title, artist, album, duration, Math.Clamp(currentPosition, 0, Math.Max(0, duration)),
                playing, source, observed));
        }
        catch { Dispose(); throw; }
    }
    private void Attach(GlobalSystemMediaTransportControlsSession? selected)
    {
        if (session is not null)
        {
            session.MediaPropertiesChanged -= OnMetadataChanged;
            session.PlaybackInfoChanged -= OnPlaybackChanged;
            session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        session = selected;
        clock.Reset(); observedMetadataVersion = -1;
        Interlocked.Exchange(ref stateDirty, 1);
        if (session is null) return;
        source = session.SourceAppUserModelId;
        session.MediaPropertiesChanged += OnMetadataChanged;
        session.PlaybackInfoChanged += OnPlaybackChanged;
        session.TimelinePropertiesChanged += OnTimelineChanged;
    }
    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager _, SessionsChangedEventArgs __) => Interlocked.Exchange(ref sessionsDirty, 1);
    private void OnMetadataChanged(GlobalSystemMediaTransportControlsSession _, MediaPropertiesChangedEventArgs __) => Interlocked.Increment(ref metadataVersion);
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession _, PlaybackInfoChangedEventArgs __) => Interlocked.Exchange(ref stateDirty, 1);
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession _, TimelinePropertiesChangedEventArgs __) => Interlocked.Exchange(ref stateDirty, 1);
    public void Dispose()
    {
        try
        {
            try { Attach(null); }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
            try { if (manager is not null) manager.SessionsChanged -= OnSessionsChanged; }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        }
        finally { session = null; manager = null; clock.Reset(); Interlocked.Exchange(ref sessionsDirty, 1); }
    }
}
