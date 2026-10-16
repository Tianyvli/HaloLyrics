using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace HaloLyrics;

public record LyricLine(double Begin, double End, string Text);

public static class Lyrics
{
    public static string ExtractTtml(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("ttml", out var ttml) && ttml.ValueKind == JsonValueKind.String)
                return ttml.GetString()!;
            foreach (var property in element.EnumerateObject())
            {
                var found = ExtractTtml(property.Value);
                if (found.Length != 0) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
            {
                var found = ExtractTtml(child);
                if (found.Length != 0) return found;
            }
        return "";
    }

    public static List<LyricLine> Parse(string ttml)
    {
        using var xml = XmlReader.Create(new StringReader(ttml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(xml, LoadOptions.PreserveWhitespace);
        var lines = new List<LyricLine>();
        foreach (var paragraph in document.Descendants().Where(x => x.Name.LocalName == "p"))
        {
            // Metadata/localizations are not the primary lyric body.
            if (!paragraph.Ancestors().Any(x => x.Name.LocalName == "body")) continue;
            var begin = Time((string?)paragraph.Attribute("begin"));
            var end = Time((string?)paragraph.Attribute("end"));
            if (begin is null || end is null || end <= begin) continue;
            var text = string.Concat(paragraph.DescendantNodes().OfType<XText>().Select(x => x.Value));
            if (text.Length > 0) lines.Add(new(begin.Value, end.Value, text));
        }
        return lines.OrderBy(x => x.Begin).ToList();
    }

    public static double? Time(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.EndsWith("ms") && double.TryParse(value[..^2], CultureInfo.InvariantCulture, out var ms)) return ms / 1000;
        if (value.EndsWith('s') && double.TryParse(value[..^1], CultureInfo.InvariantCulture, out var seconds)) return seconds;
        var parts = value.Split(':');
        double result = 0;
        if (parts.Length is < 1 or > 3) return null;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, CultureInfo.InvariantCulture, out var number)) return null;
            result = result * 60 + number;
        }
        return result;
    }
}
