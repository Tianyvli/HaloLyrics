namespace HaloLyrics;

public static class Interlude
{
    public static bool ShouldAnimate(IReadOnlyList<LyricLine> lines, double position, double duration, bool playing, int minimumSeconds)
    {
        if (!playing || lines.Count == 0 || TextSegments.Current(lines, position) is not null) return false;
        var previous = lines.LastOrDefault(x => x.Begin <= position);
        var next = lines.FirstOrDefault(x => x.Begin > position);
        var begin = previous?.End ?? 0;
        var end = next?.Begin ?? duration;
        return position >= begin && position < end && end - begin >= Math.Max(3, minimumSeconds);
    }
}
