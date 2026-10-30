using System.Net;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Net.Http;
namespace FluentTube;
internal static partial class YouTubeService
{
    private static readonly HelperProcessRunner Helpers = new();
    internal static void Shutdown()
    {
        Helpers.Dispose();
        Streams.Dispose(); QualityStreams.Dispose(); FeedClient.Dispose();
    }
    internal static string HelperPath
    {
        get
        {
            var directoryBuild = Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp", "yt-dlp.exe");
            return File.Exists(directoryBuild) ? directoryBuild : Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe");
        }
    }
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();
    [GeneratedRegex("^UC[A-Za-z0-9_-]{22}$")]
    private static partial Regex ChannelIdPattern();
    internal static bool IsChannelId(string? id) => id is not null && ChannelIdPattern().IsMatch(id);
    internal static string? ParseChannelUrl(string input)
    {
        if (IsChannelId(input)) return "https://www.youtube.com/channel/" + input;
        if (input.StartsWith('@')) input = "https://www.youtube.com/" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !new[] { "youtube.com", "www.youtube.com", "m.youtube.com" }.Contains(uri.Host.ToLowerInvariant())) return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length >= 2 && parts[0] == "channel" && IsChannelId(parts[1])) return "https://www.youtube.com/channel/" + parts[1];
        if (parts[0].StartsWith('@') && parts[0].Length > 1 && !parts[0].Contains('%')) return "https://www.youtube.com/" + parts[0];
        return null;
    }
    internal static bool IsShortUrl(string input) => ParseVideoId(input) is not null && Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal);
    private static string? ParseThumbnail(JsonElement item)
    {
        if (!item.TryGetProperty("thumbnails", out var images) || images.ValueKind != JsonValueKind.Array) return null;
        return images.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
            .Select(i => i.GetProperty("url").GetString()).LastOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "i.ytimg.com");
    }
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
    internal static List<Video> ParseSearchResults(string jsonText)
    {
        using var json = JsonDocument.Parse(jsonText);
        if (!json.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return [];
        var videos = new List<Video>();
        foreach (var item in entries.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idValue)) continue;
            var id = idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
            if (id is null || !VideoIdPattern().IsMatch(id)) continue;
            string Text(string name, string fallback) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;
            var channelId = Text("channel_id", "");
            double? duration = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) ? seconds : null;
            long? views = item.TryGetProperty("view_count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var count) ? count : null;
            DateTimeOffset? published = DateTime.TryParseExact(Text("upload_date", ""), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date) ? new DateTimeOffset(date) : null;
            videos.Add(new Video(id, WebUtility.HtmlDecode(Text("title", "YouTube video")), Text("channel", Text("uploader", "YouTube")),
                ChannelId: IsChannelId(channelId) ? channelId : null, Duration: duration, Views: views, Published: published,
                IsShort: IsShortUrl(Text("url", Text("webpage_url", ""))), Preview: ParseThumbnail(item)));
        }
        return videos.DistinctBy(v => v.Id).ToList();
    }
    internal static ProcessStartInfo SearchProcess(string executable, string query)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--flat-playlist", "--skip-download", "--dump-single-json", "--no-warnings", "--socket-timeout", "15", "--retries", "1", "--", $"ytsearch24:{query}" }) start.ArgumentList.Add(arg);
        return start;
    }
    internal static ProcessStartInfo ShortsProcess(string executable, string channel, int page)
    {
        var url = ParseChannelUrl(channel) ?? throw new InvalidOperationException("Choose a YouTube channel for Shorts.");
        var start = SearchProcess(executable, "");
        start.ArgumentList[^1] = url + "/shorts";
        var offset = start.ArgumentList.Count - 2;
        foreach (var arg in new[] { "--playlist-start", ((Math.Clamp(page, 1, 10) - 1) * 24 + 1).ToString(), "--playlist-end", (Math.Clamp(page, 1, 10) * 24).ToString() }) start.ArgumentList.Insert(offset++, arg);
        return start;
    }
    internal static async Task<List<Video>> FetchShorts(string channel, CancellationToken cancellation, int page = 1)
    {
        var text = await Execute(ShortsProcess(HelperPath, channel, page), cancellation);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var id = root.TryGetProperty("channel_id", out var cid) && cid.ValueKind == JsonValueKind.String ? cid.GetString() : null;
        var name = root.TryGetProperty("channel", out var cn) && cn.ValueKind == JsonValueKind.String ? cn.GetString() ?? "YouTube" : "YouTube";
        return ParseSearchResults(text).Select(v => v with { IsShort = true, Channel = v.Channel == "YouTube" ? name : v.Channel, ChannelId = v.ChannelId ?? (IsChannelId(id) ? id : null) }).ToList();
    }
    internal static async Task<List<Video>> Fetch(string query, CancellationToken cancellation, int page = 1)
    {
        var executable = HelperPath;
        if (!File.Exists(executable)) throw new InvalidOperationException("The search helper is missing. Extract the entire FluentTube download, including its Tools folder.");
        var start = SearchProcess(executable, query);
        var end = Math.Clamp(page, 1, 10) * 24;
        start.ArgumentList[^1] = $"ytsearch{end}:{query}";
        start.ArgumentList.Insert(start.ArgumentList.Count - 2, "--playlist-start");
        start.ArgumentList.Insert(start.ArgumentList.Count - 2, ((Math.Clamp(page, 1, 10) - 1) * 24 + 1).ToString());
        return ParseSearchResults(await Execute(start, cancellation));
    }
    internal sealed record ChannelPage(ChannelSubscription Channel, List<Video> Videos);
    internal static async Task<List<Video>> FetchDiscovery(CancellationToken cancellation, int page = 1)
    {
        using var limit = new SemaphoreSlim(2);
        var tasks = new[] { "technology", "music", "gaming" }.Select(async topic =>
        {
            await limit.WaitAsync(cancellation);
            try { return (await Fetch(topic, cancellation, (page - 1) / 3 + 1)).Skip((page - 1) % 3 * 8).Take(8).ToList(); }
            finally { limit.Release(); }
        });
        var groups = await Task.WhenAll(tasks);
        return Enumerable.Range(0, 8).SelectMany(index => groups.Where(g => g.Count > index).Select(g => g[index])).DistinctBy(v => v.Id).ToList();
    }
    internal static async Task<ChannelPage> FetchChannel(string input, CancellationToken cancellation, int page = 1)
    {
        var url = ParseChannelUrl(input) ?? throw new InvalidOperationException("Enter a YouTube channel URL, @handle, or channel ID.");
        var start = SearchProcess(HelperPath, "");
        start.ArgumentList[^1] = url + "/videos";
        var offset = start.ArgumentList.Count - 2;
        foreach (var arg in new[] { "--playlist-start", ((Math.Clamp(page, 1, 10) - 1) * 24 + 1).ToString(), "--playlist-end", (Math.Clamp(page, 1, 10) * 24).ToString() }) start.ArgumentList.Insert(offset++, arg);
        var json = await Execute(start, cancellation);
        var videos = ParseSearchResults(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = (root.TryGetProperty("channel_id", out var channel) ? channel.GetString() : null) ?? videos.FirstOrDefault()?.ChannelId;
        if (!IsChannelId(id)) throw new InvalidOperationException("Could not identify this channel. Try its full /channel/ URL.");
        var name = root.TryGetProperty("channel", out var nameValue) ? nameValue.GetString() ?? id! : videos.FirstOrDefault()?.Channel ?? id!;
        return new(new(id!, name), videos.Select(v => v with { ChannelId = v.ChannelId ?? id, Channel = v.Channel == "YouTube" ? name : v.Channel }).ToList());
    }
    private static readonly HttpClient FeedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    internal static List<Video> ParseFeed(string xml, ChannelSubscription channel)
    {
        var document = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom", yt = "http://www.youtube.com/xml/schemas/2015", media = "http://search.yahoo.com/mrss/";
        return document.Descendants(atom + "entry").Select(entry =>
        {
            var id = entry.Element(yt + "videoId")?.Value;
            if (id is null || !VideoIdPattern().IsMatch(id)) return null;
            long? views = long.TryParse(entry.Descendants(media + "statistics").FirstOrDefault()?.Attribute("views")?.Value, out var n) ? n : null;
            DateTimeOffset? published = DateTimeOffset.TryParse(entry.Element(atom + "published")?.Value, out var date) ? date : null;
            return new Video(id, entry.Element(atom + "title")?.Value ?? "YouTube video", channel.Name, ChannelId: channel.Id, Views: views, Published: published);
        }).OfType<Video>().ToList();
    }
    internal sealed record FeedResult(List<Video> Videos, int FailedChannels);
    internal static async Task<FeedResult> FetchSubscriptions(IEnumerable<ChannelSubscription> subscriptions, CancellationToken cancellation)
    {
        using var limit = new SemaphoreSlim(3);
        var failures = 0;
        var tasks = subscriptions.Where(c => IsChannelId(c.Id)).Select(async channel =>
        {
            await limit.WaitAsync(cancellation);
            try
            {
                try { return ParseFeed(await FeedClient.GetStringAsync("https://www.youtube.com/feeds/videos.xml?channel_id=" + channel.Id, cancellation), channel); }
                catch (Exception e) when (e is HttpRequestException or System.Xml.XmlException or TaskCanceledException && !cancellation.IsCancellationRequested)
                { return (await FetchChannel(channel.Url, cancellation)).Videos; }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception) { Interlocked.Increment(ref failures); return new List<Video>(); }
            finally { limit.Release(); }
        }).ToArray();
        var result = await Task.WhenAll(tasks);
        return new(result.SelectMany(v => v).DistinctBy(v => v.Id).OrderByDescending(v => v.Published ?? DateTimeOffset.MinValue).ToList(), failures);
    }
    internal sealed record StreamInfo(Uri Url, string Title, string Quality, string? ChannelId = null, string Channel = "YouTube", string Description = "", IReadOnlyList<StreamVariant>? Variants = null, IReadOnlyList<CaptionTrack>? Captions = null);
    private static readonly StreamCache<StreamInfo> Streams = new(ExtractStream, StreamExpiry);
    private static readonly StreamCache<StreamInfo> QualityStreams = new((id, token) => Extract(id, token, full: true), StreamExpiry);
    internal static DateTimeOffset StreamExpiry(StreamInfo stream)
    {
        var now = DateTimeOffset.UtcNow;
        var until = now.AddMinutes(10);
        var expiry = stream.Url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "expire");
        if (expiry is not null && long.TryParse(expiry[1], out var seconds))
        {
            try { var signedExpiry = DateTimeOffset.FromUnixTimeSeconds(seconds).AddMinutes(-2); if (signedExpiry < until) until = signedExpiry; }
            catch (ArgumentOutOfRangeException) { return now; }
        }
        return until;
    }
    internal static Task<StreamInfo> ResolveStream(string id, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // Full metadata also contains the compatible stream: never re-extract it on a repeat open.
        return QualityStreams.TryGet(id, out var cached) ? Task.FromResult(cached) : Streams.Get(id, cancellation);
    }
    internal static Task<StreamInfo> ResolveQualities(string id, CancellationToken cancellation) => QualityStreams.Get(id, cancellation);
    internal static void InvalidateStream(string id) { Streams.Invalidate(id); QualityStreams.Invalidate(id); }
    internal static StreamInfo ParseStream(string jsonText)
    {
        using var json = JsonDocument.Parse(jsonText);
        var root = json.RootElement;
        if (!root.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("No compatible video stream was available.");
        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "YouTube video" : "YouTube video";
        var quality = root.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number && height.TryGetInt32(out var h) ? $"{h}p" : "Available quality";
        if (int.TryParse(quality.TrimEnd('p'), out var videoHeight) && root.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number && width.TryGetInt32(out var videoWidth) && videoWidth > 0) quality = $"{Math.Min(videoHeight, videoWidth)}p";
        var channelId = root.TryGetProperty("channel_id", out var cid) ? cid.GetString() : null;
        var channelName = root.TryGetProperty("channel", out var cn) ? cn.GetString() ?? "YouTube" : "YouTube";
        var description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
        return new(uri, title, quality, IsChannelId(channelId) ? channelId : null, channelName, description, StreamVariants.Parse(root), FluentTube.Captions.Tracks(root));
    }
    private static Task<StreamInfo> ExtractStream(string id, CancellationToken cancellation) => Extract(id, cancellation, full: false);
    private static async Task<StreamInfo> Extract(string id, CancellationToken cancellation, bool full)
    {
        if (!VideoIdPattern().IsMatch(id)) throw new InvalidOperationException("Invalid YouTube video ID.");
        var executable = HelperPath;
        var deno = Path.Combine(AppContext.BaseDirectory, "Tools", "deno.exe");
        if (!File.Exists(executable) || !File.Exists(deno)) throw new InvalidOperationException("The playback helper is missing. Extract the entire app download, including Tools.");
        var start = SearchProcess(executable, "");
        start.ArgumentList.Clear();
        foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--skip-download", "--dump-single-json", "--js-runtimes", "deno:" + deno, "--socket-timeout", "15", "--retries", "1", "-f", "b[ext=mp4]/b", "--extractor-args", "youtube:skip=hls,dash,translated_subs", "--", "https://www.youtube.com/watch?v=" + id }) start.ArgumentList.Add(arg);
        if (full)
        {
            var option = start.ArgumentList.IndexOf("--extractor-args");
            start.ArgumentList.RemoveAt(option + 1); start.ArgumentList.RemoveAt(option);
        }
        try { return ParseStream(await Execute(start, cancellation)); }
        catch (InvalidOperationException e) when (!full && e.Message.StartsWith("No combined audio/video", StringComparison.Ordinal))
        {
            // Some live streams require manifests. Retry with full extraction
            // rather than dropping format support to make ordinary opens faster.
            var option = start.ArgumentList.IndexOf("--extractor-args");
            start.ArgumentList.RemoveAt(option + 1);
            start.ArgumentList.RemoveAt(option);
            return ParseStream(await Execute(start, cancellation));
        }
    }
    private static async Task<string> Execute(ProcessStartInfo start, CancellationToken cancellation)
    {
        var completed = await Helpers.Run(start, cancellation);
        var result = completed.Output;
        var error = completed.Error;
        if (completed.ExitCode != 0) throw new InvalidOperationException(error.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || error.Contains("bot", StringComparison.OrdinalIgnoreCase)
            ? "YouTube requires verification for this request. Try a different public video or open it on YouTube."
            : error.Contains("Requested format", StringComparison.OrdinalIgnoreCase) ? "No combined audio/video stream is available for native playback. Try a different video or import a local file."
            : "Could not retrieve this YouTube content. It may be restricted or unavailable. Try again, or install a newer FluentTube build.");
        return result;
    }
}
