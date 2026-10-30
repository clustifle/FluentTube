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
                ChannelId: IsChannelId(channelId) ? channelId : null, Duration: duration, Views: views, Published: published));
        }
        return videos.DistinctBy(v => v.Id).ToList();
    }
    internal static ProcessStartInfo SearchProcess(string executable, string query)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--flat-playlist", "--skip-download", "--dump-single-json", "--no-warnings", "--socket-timeout", "15", "--retries", "1", "--", $"ytsearch24:{query}" }) start.ArgumentList.Add(arg);
        return start;
    }
    internal static async Task<List<Video>> Fetch(string query, CancellationToken cancellation, int page = 1)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("The search helper is missing. Extract the entire FluentTube download, including its Tools folder.");
        var start = SearchProcess(executable, query);
        var end = Math.Clamp(page, 1, 10) * 24;
        start.ArgumentList[^1] = $"ytsearch{end}:{query}";
        start.ArgumentList.Insert(start.ArgumentList.Count - 2, "--playlist-start");
        start.ArgumentList.Insert(start.ArgumentList.Count - 2, ((Math.Clamp(page, 1, 10) - 1) * 24 + 1).ToString());
        return ParseSearchResults(await Execute(start, cancellation));
    }
    internal sealed record ChannelPage(ChannelSubscription Channel, List<Video> Videos);
    internal static async Task<ChannelPage> FetchChannel(string input, CancellationToken cancellation, int page = 1)
    {
        var url = ParseChannelUrl(input) ?? throw new InvalidOperationException("Enter a YouTube channel URL, @handle, or channel ID.");
        var start = SearchProcess(Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe"), "");
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
    internal sealed record StreamInfo(Uri Url, string Title, string Quality, string? ChannelId = null, string Channel = "YouTube", string Description = "");
    private static readonly StreamCache<StreamInfo> Streams = new(ExtractStream, StreamExpiry);
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
    internal static Task<StreamInfo> ResolveStream(string id, CancellationToken cancellation) => Streams.Get(id, cancellation);
    internal static void InvalidateStream(string id) => Streams.Invalidate(id);
    internal static StreamInfo ParseStream(string jsonText)
    {
        using var json = JsonDocument.Parse(jsonText);
        var root = json.RootElement;
        if (!root.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("No compatible video stream was available.");
        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "YouTube video" : "YouTube video";
        var quality = root.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number && height.TryGetInt32(out var h) ? $"{h}p" : "Available quality";
        var channelId = root.TryGetProperty("channel_id", out var cid) ? cid.GetString() : null;
        var channelName = root.TryGetProperty("channel", out var cn) ? cn.GetString() ?? "YouTube" : "YouTube";
        var description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
        return new(uri, title, quality, IsChannelId(channelId) ? channelId : null, channelName, description);
    }
    private static async Task<StreamInfo> ExtractStream(string id, CancellationToken cancellation)
    {
        if (!VideoIdPattern().IsMatch(id)) throw new InvalidOperationException("Invalid YouTube video ID.");
        var executable = Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe");
        var deno = Path.Combine(AppContext.BaseDirectory, "Tools", "deno.exe");
        if (!File.Exists(executable) || !File.Exists(deno)) throw new InvalidOperationException("The playback helper is missing. Extract the entire app download, including Tools.");
        var start = SearchProcess(executable, "");
        start.ArgumentList.Clear();
        foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--skip-download", "--dump-single-json", "--js-runtimes", "deno:" + deno, "--socket-timeout", "15", "--retries", "1", "-f", "b[ext=mp4]/b", "--extractor-args", "youtube:skip=hls,dash,translated_subs", "--", "https://www.youtube.com/watch?v=" + id }) start.ArgumentList.Add(arg);
        try { return ParseStream(await Execute(start, cancellation)); }
        catch (InvalidOperationException e) when (e.Message.StartsWith("No combined audio/video", StringComparison.Ordinal))
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
        cancellation.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var process = new Process { StartInfo = start };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
            await Task.WhenAll(output, errors);
            if (cancellation.IsCancellationRequested) throw;
            throw new InvalidOperationException("YouTube took too long to respond. Check your connection and try again.");
        }
        var result = await output;
        var error = await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || error.Contains("bot", StringComparison.OrdinalIgnoreCase)
            ? "YouTube requires verification for this request. Try a different public video or open it on YouTube."
            : error.Contains("Requested format", StringComparison.OrdinalIgnoreCase) ? "No combined audio/video stream is available for native playback. Try a different video or import a local file."
            : "Could not retrieve this YouTube content. It may be restricted or unavailable. Try again, or install a newer FluentTube build.");
        return result;
    }
}
