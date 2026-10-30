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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var process = new Process { StartInfo = SearchProcess(executable, query) };
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
            throw new InvalidOperationException("Search timed out. Check your connection and try again.");
        }
        var result = await output;
        var error = await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || error.Contains("bot", StringComparison.OrdinalIgnoreCase)
            ? "YouTube temporarily blocked search. Try again later, or paste a video link to watch."
            : "Search could not reach YouTube. Try again, or install a newer FluentTube build if the problem persists.");
        return ParseSearchResults(result);
    }
}
