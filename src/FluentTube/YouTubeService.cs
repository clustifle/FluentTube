using System.Net;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FluentTube;
internal static partial class YouTubeService
{
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
            videos.Add(new Video(id, WebUtility.HtmlDecode(Text("title", "YouTube video")), Text("channel", Text("uploader", "YouTube"))));
        }
        return videos.DistinctBy(v => v.Id).ToList();
    }
    internal static ProcessStartInfo SearchProcess(string executable, string query)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--flat-playlist", "--skip-download", "--dump-single-json", "--no-warnings", "--socket-timeout", "15", "--retries", "1", "--", $"ytsearch24:{query}" }) start.ArgumentList.Add(arg);
        return start;
    }
    internal static async Task<List<Video>> Fetch(string query, CancellationToken cancellation)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("The search helper is missing. Extract the entire FluentTube download, including its Tools folder.");
        return ParseSearchResults(await Execute(SearchProcess(executable, query), cancellation));
    }
    internal sealed record StreamInfo(Uri Url, string Title, string Quality);
    internal static StreamInfo ParseStream(string jsonText)
    {
        using var json = JsonDocument.Parse(jsonText);
        var root = json.RootElement;
        if (!root.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("No compatible video stream was available.");
        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "YouTube video" : "YouTube video";
        var quality = root.TryGetProperty("height", out var height) && height.TryGetInt32(out var h) ? $"{h}p" : "Available quality";
        return new(uri, title, quality);
    }
    internal static async Task<StreamInfo> ResolveStream(string id, CancellationToken cancellation)
    {
        if (!VideoIdPattern().IsMatch(id)) throw new InvalidOperationException("Invalid YouTube video ID.");
        var executable = Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe");
        var deno = Path.Combine(AppContext.BaseDirectory, "Tools", "deno.exe");
        if (!File.Exists(executable) || !File.Exists(deno)) throw new InvalidOperationException("The playback helper is missing. Extract the entire app download, including Tools.");
        var start = SearchProcess(executable, "");
        start.ArgumentList.Clear();
        foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--skip-download", "--dump-single-json", "--js-runtimes", "deno:" + deno, "--socket-timeout", "15", "--retries", "1", "-f", "b[ext=mp4]/b", "--", "https://www.youtube.com/watch?v=" + id }) start.ArgumentList.Add(arg);
        return ParseStream(await Execute(start, cancellation));
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
