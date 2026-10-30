using FluentTube;
var cases = new (string Input, string? Expected)[]
{
    ("dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10", "dQw4w9WgXcQ"),
    ("https://youtu.be/dQw4w9WgXcQ?si=test", "dQw4w9WgXcQ"),
    ("https://m.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ", null),
    ("https://evil.example/watch?v=dQw4w9WgXcQ", null),
    ("javascript:alert(1)", null),
    ("https://youtube.com/watch?v=<script>", null),
    ("https://youtu.be/dQw4w9WgXcQ/extra", null),
    ("https://youtube.com/watch?v=short", null),
    ("relaxing music", null),
};
foreach (var (input, expected) in cases)
    if (YouTubeService.ParseVideoId(input) != expected) throw new Exception($"URL parsing failed: {input}");
Console.WriteLine($"Passed {cases.Length} video URL and untrusted-input checks.");
var parsed = YouTubeService.ParseSearchResults("""
{"entries":[null,{"id":"bad"},{"id":"dQw4w9WgXcQ","title":"A &amp; B","channel":"Creator"},{"id":"dQw4w9WgXcQ"},{"id":"jNQXAC9IVRw","uploader":"Uploader"}]}
""");
if (parsed.Count != 2 || parsed[0].Title != "A & B" || parsed[1].Channel != "Uploader") throw new Exception("Search metadata filtering failed.");
var hostileQuery = "music \" --exec calc.exe & echo test";
var start = YouTubeService.SearchProcess("yt-dlp.exe", hostileQuery);
if (start.UseShellExecute || !start.CreateNoWindow || start.ArgumentList.Last() != "ytsearch24:" + hostileQuery || start.ArgumentList[^2] != "--") throw new Exception("Search arguments must remain a single literal query.");
Console.WriteLine("Passed metadata filtering and literal process-argument checks.");
var stream = YouTubeService.ParseStream("""{"url":"https://example.com/video.mp4","title":"Public video","height":360}""");
if (stream.Quality != "360p" || stream.Title != "Public video") throw new Exception("Stream metadata failed.");
foreach (var invalid in new[] { "file:///C:/video.mp4", "javascript:alert(1)", "http://example.com/video.mp4" })
{
    try { YouTubeService.ParseStream(System.Text.Json.JsonSerializer.Serialize(new { url = invalid })); throw new Exception("Unsafe stream URL accepted."); }
    catch (InvalidOperationException) { }
}
Console.WriteLine("Passed native stream metadata and HTTPS-only source checks.");
var now = DateTimeOffset.UtcNow;
var releases = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var calls = 0;
var cache = new StreamCache<int>(async (_, token) => { Interlocked.Increment(ref calls); return await releases.Task.WaitAsync(token); }, _ => now.AddMinutes(1), clock: () => now);
using var cancelOne = new CancellationTokenSource();
var first = cache.Get("video", cancelOne.Token);
var second = cache.Get("video", CancellationToken.None);
cancelOne.Cancel();
try { await first; throw new Exception("Canceled cache caller completed."); } catch (OperationCanceledException) { }
releases.SetResult(42);
if (await second != 42 || await cache.Get("video", CancellationToken.None) != 42 || calls != 1) throw new Exception("Shared extraction or warm reuse failed.");
now = now.AddMinutes(2);
await cache.Get("video", CancellationToken.None);
if (calls != 2) throw new Exception("Expired stream was reused.");
cache.Invalidate("video");
await cache.Get("video", CancellationToken.None);
if (calls != 3) throw new Exception("Stream invalidation failed.");
if (PlaybackMath.Seek(3, -10, 60) != 0 || PlaybackMath.Seek(55, 10, 60) != 60 || PlaybackMath.Resume(20, 60) != 20 || PlaybackMath.Resume(55, 60) != 0) throw new Exception("Seek/resume bounds failed.");
var expired = YouTubeService.StreamExpiry(new(new Uri("https://example.com/video?expire=1"), "", ""));
if (expired > DateTimeOffset.UtcNow) throw new Exception("Expired signed URL was cached.");
Console.WriteLine("Passed shared extraction, caller cancellation, expiry, invalidation, and seek/resume checks.");
var workerStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var canceledCache = new StreamCache<int>(async (_, token) =>
{
    try { await Task.Delay(Timeout.Infinite, token); return 0; }
    finally { workerStopped.TrySetResult(true); }
}, _ => DateTimeOffset.UtcNow.AddMinutes(1));
using var stop = new CancellationTokenSource();
var pending = canceledCache.Get("cancel", stop.Token);
stop.Cancel();
try { await pending; throw new Exception("Canceled extraction continued."); } catch (OperationCanceledException) { }
await workerStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
var attempts = 0;
var faultCache = new StreamCache<int>((_, _) => ++attempts == 1 ? Task.FromException<int>(new InvalidOperationException()) : Task.FromResult(7), _ => DateTimeOffset.UtcNow.AddMinutes(1));
try { await faultCache.Get("retry", CancellationToken.None); throw new Exception("Expected extraction failure."); } catch (InvalidOperationException) { }
if (await faultCache.Get("retry", CancellationToken.None) != 7) throw new Exception("Faulted extraction could not be retried.");
Console.WriteLine("Passed abandoned-worker cancellation and failed-extraction retry checks.");
