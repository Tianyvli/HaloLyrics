using Windows.Media.Control;

namespace HaloLyrics;

public record Playback(string Title, string Artist, string Album, double Duration, double Position,
    bool Playing, string Source, DateTimeOffset ObservedAt)
{
    public string Key => $"{Title}\n{Artist}\n{Album}\n{Math.Round(Duration)}";
}

public sealed class MediaReader
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private readonly PlaybackClock clock = new();

    public async Task<Playback?> ReadAsync()
    {
        manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetSessions().FirstOrDefault(s =>
            s.SourceAppUserModelId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));
        if (session is null) { clock.Reset(); return null; }
        var media = await session.TryGetMediaPropertiesAsync();
        var state = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var playing = state.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var now = DateTimeOffset.UtcNow;
        var position = timeline.Position.TotalSeconds;
        if (playing && timeline.LastUpdatedTime > DateTimeOffset.UnixEpoch)
            position += Math.Max(0, (now - timeline.LastUpdatedTime).TotalSeconds) * (state.PlaybackRate ?? 1);
        var duration = timeline.EndTime.TotalSeconds;
        var artist = media.Artist;
        var album = media.AlbumTitle;
        // Apple's Windows player can combine artist and album in its Artist field.
        var separator = artist.IndexOf(" — ", StringComparison.Ordinal);
        if (separator >= 0)
        {
            if (string.IsNullOrEmpty(album)) album = artist[(separator + 3)..];
            artist = artist[..separator];
        }
        return clock.Apply(new(media.Title, artist, album, duration, Math.Clamp(position, 0, Math.Max(0, duration)),
            playing, session.SourceAppUserModelId, now));
    }
}
