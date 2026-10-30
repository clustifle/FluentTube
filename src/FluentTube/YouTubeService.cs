using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FluentTube;
internal static partial class YouTubeService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();
    internal static string? ParseVideoId(string input)
    {
        if (VideoIdPattern().IsMatch(input)) return input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
        string? id = null;
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) id = uri.AbsolutePath.Trim('/');
        else if (new[] { "youtube.com", "www.youtube.com", "m.youtube.com" }.Contains(uri.Host.ToLowerInvariant()))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 2 && segments[0] is "shorts" or "embed" or "live") id = segments[1];
            else if (uri.AbsolutePath == "/watch")
                id = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "v")?.ElementAt(1);
        }
        return id is not null && VideoIdPattern().IsMatch(id) ? id : null;
    }
    internal static async Task<List<Video>> Fetch(string key, string? query, CancellationToken cancellation)
    {
        var endpoint = string.IsNullOrWhiteSpace(query)
            ? "videos?part=snippet&chart=mostPopular&maxResults=24"
            : $"search?part=snippet&type=video&maxResults=24&q={Uri.EscapeDataString(query)}";
        using var response = await Client.GetAsync($"https://www.googleapis.com/youtube/v3/{endpoint}&key={Uri.EscapeDataString(key)}", cancellation);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.Forbidden
                ? "YouTube refused this request. Check that YouTube Data API v3 is enabled, your key restrictions allow this app, and quota is available."
                : $"YouTube returned {(int)response.StatusCode}. Check your API key and try again.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item =>
        {
            var id = item.GetProperty("id");
            var snippet = item.GetProperty("snippet");
            return new Video(id.ValueKind == JsonValueKind.String ? id.GetString()! : id.GetProperty("videoId").GetString()!,
                WebUtility.HtmlDecode(snippet.GetProperty("title").GetString() ?? "Untitled"),
                WebUtility.HtmlDecode(snippet.GetProperty("channelTitle").GetString() ?? "YouTube"));
        }).ToList();
    }
}
