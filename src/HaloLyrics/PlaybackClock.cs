namespace HaloLyrics;

/// <summary>Rejects subsecond SMTC timestamp jitter without hiding real seeks or pauses.</summary>
public sealed class PlaybackClock
{
    private Playback? previous;
    public Playback Apply(Playback current)
    {
        var position = current.Position;
        if (previous is not null && previous.Key == current.Key && previous.Playing && current.Playing)
        {
            var elapsed = Math.Max(0, (current.ObservedAt - previous.ObservedAt).TotalSeconds);
            var predicted = previous.Position + elapsed;
            var error = current.Position - predicted;
            if (elapsed <= 3 && Math.Abs(error) < 1.25)
            {
                // Correct phase gently; never revisit a line because the source rounded a timestamp.
                var correction = Math.Clamp(error * 0.2, -elapsed * 0.2, elapsed * 0.2);
                position = Math.Max(previous.Position, predicted + correction);
            }
        }
        previous = current with { Position = Math.Clamp(position, 0, Math.Max(0, current.Duration)) };
        return previous;
    }
    public void Reset() => previous = null;
}
