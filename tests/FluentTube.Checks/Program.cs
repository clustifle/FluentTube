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
if (!cache.TryGet("video", out var warm) || warm != 42) throw new Exception("Ready stream lookup missed a valid cache entry.");
now = now.AddMinutes(2);
if (cache.TryGet("video", out _)) throw new Exception("Ready stream lookup accepted an expired URL.");
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
var channelId = "UCsBjURrPoezykLs9EqgamOA";
foreach (var address in new[] { channelId, "https://www.youtube.com/channel/" + channelId + "/videos" })
    if (YouTubeService.ParseChannelUrl(address) != "https://www.youtube.com/channel/" + channelId) throw new Exception("Channel ID parsing failed.");
if (YouTubeService.ParseChannelUrl("@Microsoft") != "https://www.youtube.com/@Microsoft" || YouTubeService.ParseChannelUrl("https://youtube.com.evil.example/@Microsoft") is not null) throw new Exception("Channel URL validation failed.");
var metadata = YouTubeService.ParseSearchResults("""{"entries":[{"id":"jNQXAC9IVRw","title":"Test","duration":90,"view_count":12000,"channel_id":"UCsBjURrPoezykLs9EqgamOA"},{"id":"dQw4w9WgXcQ","duration":null,"view_count":null}]}""");
if (metadata.Count != 2 || metadata[0].DurationText != "1:30" || !metadata[0].Details.Contains("12K views") || metadata[1].Duration is not null) throw new Exception("Video metadata parsing failed.");
var xml = """<feed xmlns="http://www.w3.org/2005/Atom" xmlns:yt="http://www.youtube.com/xml/schemas/2015"><entry><yt:videoId>jNQXAC9IVRw</yt:videoId><title>A &amp; B</title><published>2026-10-30T00:00:00Z</published></entry><entry><yt:videoId>invalid</yt:videoId></entry></feed>""";
var uploads = YouTubeService.ParseFeed(xml, new(channelId, "Creator"));
if (uploads.Count != 1 || uploads[0].Title != "A & B" || uploads[0].ChannelId != channelId || uploads[0].Published is null) throw new Exception("Subscription feed parsing failed.");
var localLibrary = new LocalLibrary();
localLibrary.Record(new("jNQXAC9IVRw", "One", "Creator"));
localLibrary.Record(new("dQw4w9WgXcQ", "Two", "Creator"));
localLibrary.Record(new("jNQXAC9IVRw", "One", "Creator"));
if (localLibrary.Watched.Count != 2 || localLibrary.Watched[0].Id != "jNQXAC9IVRw") throw new Exception("Watch history ordering failed.");
Console.WriteLine("Passed channel validation, video metadata, subscription feed, and history checks.");
using var qualityJson = System.Text.Json.JsonDocument.Parse("""
{"formats":[
{"protocol":"m3u8_native","vcodec":"none","ext":"mp4","url":"https://example.com/itag/234/audio.m3u8"},
{"protocol":"m3u8_native","vcodec":"avc1.640028","height":1080,"width":1920,"tbr":3000,"url":"https://example.com/itag/270/video.m3u8"},
{"protocol":"m3u8_native","vcodec":"avc1.640033","height":2160,"width":3840,"url":"https://example.com/4k.m3u8"},
{"protocol":"m3u8_native","vcodec":"vp09.00.40.08","height":1080,"width":1920,"url":"https://example.com/vp9.m3u8"}
]}
""");
var qualities = StreamVariants.Parse(qualityJson.RootElement);
if (qualities.Count != 1 || qualities[0].Height != 1080) throw new Exception("Native quality selection must cap at H.264 1080p.");
var manifest = StreamVariants.Manifest(qualities);
if (!manifest.Contains("RESOLUTION=1920x1080") || !manifest.Contains("TYPE=AUDIO") || !manifest.Contains("mp4a.40.2")) throw new Exception("HD master manifest did not include audio and video.");
if (PlaybackMath.Time(3661) != "1:01:01" || PlaybackMath.Time(double.NaN) != "0:00") throw new Exception("Player time display failed.");
Console.WriteLine("Passed 1080p cap, HLS audio/video manifest, and timeline display checks.");
var shorts = YouTubeService.ParseSearchResults("""{"entries":[{"id":"jNQXAC9IVRw","url":"https://www.youtube.com/shorts/jNQXAC9IVRw","thumbnails":[{"url":"https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg"}]},{"id":"dQw4w9WgXcQ","duration":30}]}""");
if (!shorts[0].IsShort || shorts[1].IsShort || !shorts[0].Thumbnail.Contains("hqdefault")) throw new Exception("Shorts must use source classification, not duration guesses.");
if (YouTubeService.IsShortUrl("https://youtube.com.evil.example/shorts/jNQXAC9IVRw")) throw new Exception("Untrusted Shorts host accepted.");
var shortStart = YouTubeService.ShortsProcess("helper.exe", "@YouTube", 2);
if (shortStart.ArgumentList[^1] != "https://www.youtube.com/@YouTube/shorts" || !shortStart.ArgumentList.Contains("25") || !shortStart.ArgumentList.Contains("48")) throw new Exception("Shorts pagination bounds failed.");
var cues = Captions.Parse("WEBVTT\n\n00:01.000 --> 00:03.000 align:start\n<v Speaker>Hello &amp; welcome</v>\n\n00:02.000 --> 00:04.000\nSecond line\n\n999999999999:00:00.000 --> \ninvalid\n");
if (cues.Count != 2 || Captions.At(cues, 0) != "" || Captions.At(cues, 1.5) != "Hello & welcome" || Captions.At(cues, 2.5) != "Hello & welcome\nSecond line" || Captions.At(cues, 4) != "") throw new Exception("Caption timing, markup, overlap, or end boundary failed.");
if (Captions.Parse("1\n00:00:01,250 --> 00:00:02,500\nLocal subtitles\n").Single().Start != 1.25) throw new Exception("SRT subtitle parsing failed.");
using var captionMetadata = System.Text.Json.JsonDocument.Parse("""{"subtitles":{"en":[{"ext":"vtt","url":"https://example.com/sub.vtt","name":"English"}],"bad":[{"ext":"vtt","url":"file:///bad"}]},"automatic_captions":{"en-orig":[{"ext":"vtt","url":"https://example.com/auto.vtt"}],"fr":[{"ext":"vtt","url":"https://example.com/translated.vtt"}]}}""");
if (Captions.Tracks(captionMetadata.RootElement).Count != 2) throw new Exception("Caption format, HTTPS, or original auto-language filtering failed.");
Console.WriteLine("Passed Shorts classification/pagination and VTT/SRT caption parsing checks.");
using var portraitFormats = System.Text.Json.JsonDocument.Parse("""{"formats":[{"protocol":"m3u8_native","vcodec":"none","ext":"mp4","url":"https://example.com/audio.m3u8"},{"protocol":"m3u8_native","vcodec":"avc1.640028","height":1920,"width":1080,"url":"https://example.com/portrait.m3u8"},{"protocol":"m3u8_native","vcodec":"avc1.640033","height":3840,"width":2160,"url":"https://example.com/4k.m3u8"}]}""");
var portrait = StreamVariants.Parse(portraitFormats.RootElement);
if (portrait.Count != 1 || portrait[0].QualityHeight != 1080 || !StreamVariants.Manifest(portrait).Contains("RESOLUTION=1080x1920")) throw new Exception("Portrait 1080p selection or aspect ratio failed.");
if (YouTubeService.ParseStream("""{"url":"https://example.com/short.mp4","height":640,"width":360}""").Quality != "360p") throw new Exception("Portrait quality label failed.");
Console.WriteLine("Passed portrait 1080p quality and manifest dimensions.");
var takeoutCsv = "\uFEFFChannel Title,Channel Url,Channel Id\r\n\"Creator, \"\"A\"\"\",https://www.youtube.com/channel/UCsBjURrPoezykLs9EqgamOA,UCsBjURrPoezykLs9EqgamOA\r\n\"ไทย\nSecond line\",https://www.youtube.com/channel/UCBR8-60-B28hp2BmDPdntcQ,\r\nDuplicate,,UCsBjURrPoezykLs9EqgamOA\r\nBad,https://youtube.com.evil.example/channel/UCBR8-60-B28hp2BmDPdntcQ,bad\r\n";
var takeout = TakeoutImport.Parse(takeoutCsv);
if (takeout.Channels.Count != 2 || takeout.InvalidRows != 1 || takeout.DuplicateRows != 1 || takeout.Channels[0].Name != "Creator, \"A\"" || takeout.Channels[1].Name != "ไทย Second line") throw new Exception("Takeout CSV quoting, Unicode, invalid rows, or duplicates failed.");
var mergedFollows = TakeoutImport.Merge([new(channelId, "Existing custom name")], takeout.Channels);
if (mergedFollows.Count != 2 || mergedFollows[0].Name != "Existing custom name") throw new Exception("Takeout merge replaced an existing subscription.");
if (TakeoutImport.Merge(mergedFollows, takeout.Channels).Count != 2) throw new Exception("Repeated Takeout import was not idempotent.");
foreach (var badCsv in new[] { "", "title,url\nvideo,https://example.com", "Channel Id,Channel Title\n\"unfinished", "Channel Id,Channel Title\n\"bad\"text,name" })
{
    try { TakeoutImport.Parse(badCsv); throw new Exception("Invalid Takeout CSV accepted."); } catch (InvalidOperationException) { }
}
var takeoutZip = Path.Combine(Path.GetTempPath(), "FluentTube-takeout-check-" + Guid.NewGuid() + ".zip");
try
{
    using (var archive = System.IO.Compression.ZipFile.Open(takeoutZip, System.IO.Compression.ZipArchiveMode.Create))
    {
        foreach (var entryName in new[] { "Takeout/YouTube and YouTube Music/subscriptions/subscriptions.csv", "OtherAccount/subscriptions.csv" })
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), new System.Text.UTF8Encoding(true));
            writer.Write(takeoutCsv);
        }
        using var ignored = new StreamWriter(archive.CreateEntry("unrelated.txt").Open()); ignored.Write("Ignored");
    }
    var zipped = await TakeoutImport.Read(takeoutZip);
    if (zipped.Channels.Count != 2 || zipped.InvalidRows != 2 || zipped.DuplicateRows != 4) throw new Exception("Takeout ZIP reading or cross-file deduplication failed.");
}
finally { File.Delete(takeoutZip); }
Console.WriteLine("Passed Takeout CSV/ZIP, Unicode, quoting, validation, duplicate accounting, and non-destructive merge checks.");
