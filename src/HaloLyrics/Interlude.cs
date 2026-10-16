namespace HaloLyrics;

public static class Interlude
{
    public static bool ShouldAnimate(IReadOnlyList<LyricLine> lines, double position, double duration, bool playing, int minimumSeconds)
    {
        if (!playing || lines.Count == 0 || TextSegments.Current(lines, position) is not null) return false;
        var begin = 0.0;
        var end = duration;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Begin <= position) begin = lines[i].End;
            else { end = lines[i].Begin; break; }
        }
        return position >= begin && position < end && end - begin >= Math.Max(3, minimumSeconds);
    }
}
