using System.Text.Json;
using System.Text;
namespace FluentTube;
internal sealed record StreamVariant(int Height, int Width, uint Bandwidth, string Codec, Uri Video, Uri Audio)
{
    internal int QualityHeight => Math.Min(Height, Width);
}
internal static class StreamVariants
{
    internal static IReadOnlyList<StreamVariant> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array) return [];
        static string Text(JsonElement f, string key) => f.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        var items = formats.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object).ToList();
        var audio = items.LastOrDefault(f => Text(f, "protocol").StartsWith("m3u8") && Text(f, "vcodec") == "none" && Text(f, "ext") == "mp4");
        if (audio.ValueKind == JsonValueKind.Undefined || !Uri.TryCreate(Text(audio, "url"), UriKind.Absolute, out var audioUri) || audioUri.Scheme != "https") return [];
        var result = new List<StreamVariant>();
        foreach (var f in items)
        {
            if (!Text(f, "protocol").StartsWith("m3u8") || !Text(f, "vcodec").StartsWith("avc1") ||
                !f.TryGetProperty("height", out var h) || h.ValueKind != JsonValueKind.Number || !h.TryGetInt32(out var height) || height <= 0 ||
                !Uri.TryCreate(Text(f, "url"), UriKind.Absolute, out var videoUri) || videoUri.Scheme != "https") continue;
            var width = f.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var n) ? n : height * 16 / 9;
            if (Math.Min(height, width) is < 144 or > 1080) continue;
            var bitrate = f.TryGetProperty("tbr", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetDouble(out var rate) && double.IsFinite(rate) ? rate : height * 3;
            result.Add(new(height, width, (uint)Math.Clamp((bitrate + 256) * 1000, 128000, 50000000), Text(f, "vcodec"), videoUri, audioUri));
        }
        return result.GroupBy(v => v.QualityHeight).Select(g => g.OrderByDescending(v => v.Bandwidth).First()).OrderBy(v => v.QualityHeight).ToList();
    }
    internal static string Manifest(IEnumerable<StreamVariant> variants)
    {
        var items = variants.ToList();
        if (items.Count == 0) throw new InvalidOperationException("This resolution is not available for this video.");
        static string Url(Uri uri) => uri.AbsoluteUri.Replace("\"", "%22");
        var text = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:6\n");
        text.AppendLine($"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Audio\",DEFAULT=YES,AUTOSELECT=YES,URI=\"{Url(items[0].Audio)}\"");
        foreach (var item in items)
        {
            text.AppendLine($"#EXT-X-STREAM-INF:BANDWIDTH={item.Bandwidth},CODECS=\"{item.Codec},mp4a.40.2\",RESOLUTION={item.Width}x{item.Height},AUDIO=\"audio\"");
            text.AppendLine(Url(item.Video));
        }
        return text.ToString();
    }
}
