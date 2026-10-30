using System.Text;
using System.Text.Json;
namespace FluentTube;

internal static class ShortsSearch
{
    // Decode only the initial JSON data. Page scripts are never executed.
    internal static List<Video> ParsePage(string html)
    {
        var marker = html.IndexOf("var ytInitialData = ", StringComparison.Ordinal);
        if (marker < 0 || html.Length > 8 * 1024 * 1024) return [];
        var start = marker + "var ytInitialData = ".Length;
        var data = html.AsSpan(start).TrimStart();
        string json;
        if (data.Length > 0 && data[0] is '\'' or '"')
        {
            var quote = data[0];
            var decoded = new StringBuilder();
            for (var i = 1; i < data.Length; i++)
            {
                var value = data[i];
                if (value == quote) break;
                if (value != '\\') { decoded.Append(value); continue; }
                if (++i >= data.Length) return [];
                value = data[i];
                if (value is 'x' or 'u')
                {
                    var digits = value == 'x' ? 2 : 4;
                    if (i + digits >= data.Length || !ushort.TryParse(data.Slice(i + 1, digits), System.Globalization.NumberStyles.HexNumber, null, out var code)) return [];
                    decoded.Append((char)code); i += digits;
                }
                else decoded.Append(value switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => value });
            }
            json = decoded.ToString();
        }
        else json = data.ToString();
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            using var document = JsonDocument.ParseValue(ref reader);
            var result = new List<Video>();
            Visit(document.RootElement, result);
            return result.DistinctBy(v => v.Id).Take(48).ToList();
        }
        catch (JsonException) { return []; }
    }
    private static JsonElement At(JsonElement value, params string[] path)
    {
        foreach (var key in path)
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value)) return default;
        return value;
    }
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static void Visit(JsonElement value, List<Video> result)
    {
        if (result.Count >= 96) return;
        if (value.ValueKind == JsonValueKind.Array) { foreach (var item in value.EnumerateArray()) Visit(item, result); return; }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name != "shortsLockupViewModel") { Visit(property.Value, result); continue; }
            var model = property.Value;
            var id = Text(At(model, "onTap", "innertubeCommand", "reelWatchEndpoint", "videoId"));
            if (id is null || YouTubeService.ParseVideoId(id) != id) continue;
            var title = Text(At(model, "overlayMetadata", "primaryText", "content")) ?? "Short";
            var channel = Text(At(model, "belowThumbnailMetadata", "primaryText", "content")) ?? "YouTube";
            var channelId = Text(At(model, "belowThumbnailMetadata", "avatar", "avatarViewModel", "endpoint", "innertubeCommand", "browseEndpoint", "browseId"));
            var sources = At(model, "thumbnailViewModel", "thumbnailViewModel", "image", "sources");
            var thumbnail = sources.ValueKind == JsonValueKind.Array ? sources.EnumerateArray().Select(s => Text(At(s, "url")))
                .FirstOrDefault(s => Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "i.ytimg.com") : null;
            result.Add(new(id, title, channel.Trim(), ChannelId: YouTubeService.IsChannelId(channelId) ? channelId : null, IsShort: true, Preview: thumbnail));
        }
    }
}
