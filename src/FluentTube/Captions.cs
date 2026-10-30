using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FluentTube;

internal sealed record CaptionTrack(string Language, string Label, Uri Url);
internal sealed record CaptionCue(double Start, double End, string Text);
internal static partial class Captions
{
    internal static List<CaptionTrack> Tracks(JsonElement root)
    {
        var tracks = new List<CaptionTrack>();
        Add("subtitles", false);
        Add("automatic_captions", true);
        return tracks.DistinctBy(t => t.Language).Take(40).ToList();
        void Add(string property, bool automatic)
        {
            if (!root.TryGetProperty(property, out var languages) || languages.ValueKind != JsonValueKind.Object) return;
            foreach (var language in languages.EnumerateObject())
            {
                if (language.Value.ValueKind != JsonValueKind.Array) continue;
                // YouTube generates hundreds of translated auto tracks. Keep original tracks only.
                if (automatic && !language.Name.EndsWith("-orig", StringComparison.Ordinal)) continue;
                var format = language.Value.EnumerateArray().FirstOrDefault(f => f.ValueKind == JsonValueKind.Object && f.TryGetProperty("ext", out var ext) && ext.GetString() == "vtt");
                if (format.ValueKind != JsonValueKind.Object || !format.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https") continue;
                var name = format.TryGetProperty("name", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() : language.Name;
                tracks.Add(new(language.Name, name + (automatic ? " · auto" : ""), uri));
            }
        }
    }
    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tags();
    [GeneratedRegex(@"^(?:(\d+):)?(\d{2}):(\d{2})[.,](\d{3})$")]
    private static partial Regex Timestamp();
    private static double? Seconds(string text)
    {
        var match = Timestamp().Match(text);
        if (!match.Success) return null;
        if (match.Groups[1].Success && !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out _)) return null;
        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return hours > 100 || minutes >= 60 || seconds >= 60 ? null : hours * 3600d + minutes * 60 + seconds + int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) / 1000d;
    }
    internal static List<CaptionCue> Parse(string text)
    {
        if (text.Length > 4 * 1024 * 1024) throw new InvalidOperationException("This subtitle file is too large.");
        var lines = text.Replace("\r", "").Split('\n');
        var cues = new List<CaptionCue>();
        for (var i = 0; i < lines.Length && cues.Count < 20000; i++)
        {
            var timing = lines[i].Trim().Split(" --> ", 2);
            if (timing.Length != 2) continue;
            var start = Seconds(timing[0]);
            var end = Seconds(timing[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
            var content = new List<string>();
            while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1])) content.Add(lines[++i]);
            var plain = WebUtility.HtmlDecode(Tags().Replace(string.Join("\n", content), "")).Trim();
            if (start is not null && end > start && plain.Length > 0) cues.Add(new(start.Value, end!.Value, plain));
        }
        return cues.OrderBy(c => c.Start).ToList();
    }
    internal static string At(IReadOnlyList<CaptionCue> cues, double seconds) => string.Join("\n", cues.Where(c => c.Start <= seconds && c.End > seconds).Select(c => c.Text).Distinct());
}
