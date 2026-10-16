using System.Globalization;
using System.Text;

namespace HaloLyrics;

public static class TextSegments
{
    public static List<string> Split(string text)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var runes = 0;
        var bytes = 0;
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var elementRunes = element.EnumerateRunes().Count();
            var elementBytes = Encoding.UTF8.GetByteCount(element);
            if (elementBytes > 48 || elementRunes > 16) throw new InvalidOperationException("歌词中包含超出音响容量的组合字符。");
            if (runes + elementRunes > 16 || bytes + elementBytes > 48)
            {
                result.Add(builder.ToString()); builder.Clear(); runes = 0; bytes = 0;
            }
            builder.Append(element); runes += elementRunes; bytes += elementBytes;
        }
        if (builder.Length > 0) result.Add(builder.ToString());
        return result;
    }
    public static string At(LyricLine line, double position)
    {
        var segments = Split(line.Text);
        if (segments.Count == 0) return "";
        var fraction = Math.Clamp((position - line.Begin) / (line.End - line.Begin), 0, 0.999999);
        return segments[(int)(fraction * segments.Count)];
    }
    public static LyricLine? Current(IReadOnlyList<LyricLine> lines, double position)
    {
        var index = -1;
        for (var i = 0; i < lines.Count && lines[i].Begin <= position; i++) index = i;
        if (index < 0) return null;
        var line = lines[index];
        if (position < line.End) return line;
        // Small gaps between adjacent vocal lines should not flash a blank frame.
        if (index + 1 < lines.Count && lines[index + 1].Begin - line.End <= 0.5
            && position < lines[index + 1].Begin) return line;
        return null;
    }
}
