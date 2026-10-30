using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using System.Text.Json;
using FluentTube;
if (args.Length == 1 && args[0] == "--pip-resize") { PipSizingChecks.Run(); return; }

if (args.Length == 2 && args[0] == "--shorts")
{
    var shorts = await YouTubeService.FetchShorts(args[1], CancellationToken.None);
    var next = await YouTubeService.FetchShorts(args[1], CancellationToken.None, 2);
    if (shorts.Count == 0 || shorts.Any(v => !v.IsShort) || next.Count == 0 || next.Any(v => shorts.Any(s => s.Id == v.Id))) throw new Exception("Shorts classification or pagination failed live.");
    Console.WriteLine($"PASS live Shorts: {shorts.Count} items, {next.Count} distinct items on page 2");
    var info = await YouTubeService.ResolveStream(shorts[0].Id, CancellationToken.None);
    await Check("YouTube Short", MediaSource.CreateFromUri(info.Url));
    var full = await YouTubeService.ResolveQualities(shorts[0].Id, CancellationToken.None);
    var hd = full.Variants?.FirstOrDefault(v => v.QualityHeight == 1080);
    if (hd is not null)
    {
        using var adaptiveShort = await AdaptivePlayback.Create([hd]);
        await Check("1080p YouTube Short", MediaSource.CreateFromAdaptiveMediaSource(adaptiveShort.Source));
    }
    else Console.WriteLine("No native 1080p variant available for this Short.");
    return;
}

if (args.Length == 2 && args[0] == "--quality")
{
    var metadata = YouTubeService.ParseStream(await File.ReadAllTextAsync(args[1]));
    var variant = metadata.Variants?.FirstOrDefault(v => v.Height == 1080) ?? throw new Exception("1080p variant missing.");
    using var adaptive = await AdaptivePlayback.Create([variant]);
    adaptive.Source.DownloadFailed += (_, e) => Console.WriteLine($"Adaptive download failed: {e.HttpResponseMessage?.StatusCode}; type {e.ResourceType}");
    using var source1080 = MediaSource.CreateFromAdaptiveMediaSource(adaptive.Source);
    var item = new MediaPlaybackItem(source1080);
    using var player = new MediaPlayer { Volume = 0 };
    var opened1080 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    player.MediaOpened += (_, _) => opened1080.TrySetResult(true);
    player.MediaFailed += (_, e) => opened1080.TrySetException(new Exception($"HD playback failed: {e.Error}: {e.ErrorMessage}"));
    player.Source = item;
    await opened1080.Task.WaitAsync(TimeSpan.FromSeconds(45));
    player.Play();
    await Task.Delay(4000);
    if (player.PlaybackSession.NaturalVideoHeight != 1080 || player.PlaybackSession.Position.TotalSeconds < 1 || item.AudioTracks.Count == 0) throw new Exception("1080p video/audio did not play.");
    Console.WriteLine($"PASS native 1080p: {player.PlaybackSession.NaturalVideoWidth}x{player.PlaybackSession.NaturalVideoHeight}; audio tracks {item.AudioTracks.Count}; position {player.PlaybackSession.Position.TotalSeconds:F1}s");
    if (!player.PlaybackSession.CanSeek) throw new Exception("HD stream is not seekable.");
    player.PlaybackSession.Position = TimeSpan.FromSeconds(15);
    await Task.Delay(3000);
    if (player.PlaybackSession.Position.TotalSeconds < 15) throw new Exception("HD seek failed.");
    Console.WriteLine("PASS native 1080p seeking");
    return;
}

if (args.Length > 0 && args[0] == "--browse")
{
    var channel = await YouTubeService.FetchChannel("@Microsoft", CancellationToken.None);
    if (!YouTubeService.IsChannelId(channel.Channel.Id) || channel.Videos.Count == 0) throw new Exception("Channel browsing returned no identifiable videos.");
    Console.WriteLine($"PASS channel: {channel.Channel.Name}, {channel.Videos.Count} uploads");
    var feed = await YouTubeService.FetchSubscriptions([channel.Channel], CancellationToken.None);
    if (feed.Videos.Count == 0 || feed.FailedChannels > 0) throw new Exception("Subscription feed failed.");
    Console.WriteLine($"PASS subscription feed: {feed.Videos.Count} videos");
    var nextPage = await YouTubeService.FetchChannel(channel.Channel.Url, CancellationToken.None, 2);
    if (nextPage.Videos.Count == 0 || nextPage.Videos.Any(v => channel.Videos.Any(first => first.Id == v.Id))) throw new Exception("Channel pagination repeated page one.");
    Console.WriteLine($"PASS channel page 2: {nextPage.Videos.Count} distinct uploads");
    return;
}

// Windows integration check: decode a local file and a freshly resolved public
// stream through the same MediaPlayer engine used by the WinUI control.
if (args.Length != 2) throw new ArgumentException("Provide a sample MP4 path and yt-dlp stream metadata JSON path.");
await Check("local import", MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(args[0]))));
if (args[1] == "--resolve")
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var first = await YouTubeService.ResolveStream("jNQXAC9IVRw", CancellationToken.None);
    var cold = timer.Elapsed.TotalSeconds;
    timer.Restart();
    var second = await YouTubeService.ResolveStream("jNQXAC9IVRw", CancellationToken.None);
    Console.WriteLine($"Resolve timing: cold {cold:F2}s; cached {timer.Elapsed.TotalMilliseconds:F2}ms");
    if (!ReferenceEquals(first, second)) throw new Exception("Stream cache did not reuse the resolved source.");
    await Check("YouTube native stream", MediaSource.CreateFromUri(second.Url));
    return;
}
using var json = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
await Check("YouTube native stream", MediaSource.CreateFromUri(new Uri(json.RootElement.GetProperty("url").GetString()!)));

static async Task Check(string label, MediaSource source)
{
    using (source)
    using (var media = new MediaPlayer { Volume = 0 })
    {
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        media.MediaOpened += (_, _) => opened.TrySetResult(true);
        media.MediaFailed += (_, e) => opened.TrySetException(new Exception($"{label}: {e.Error}: {e.ErrorMessage}"));
        media.Source = source;
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(45));
        media.Play();
        await Task.Delay(3000);
        if (media.PlaybackSession.Position <= TimeSpan.Zero || media.PlaybackSession.NaturalVideoWidth == 0)
            throw new Exception($"{label}: media opened but video playback did not advance.");
        Console.WriteLine($"PASS {label}: {media.PlaybackSession.NaturalVideoWidth}x{media.PlaybackSession.NaturalVideoHeight}, position {media.PlaybackSession.Position.TotalSeconds:F1}s");
        if (media.PlaybackSession.CanSeek)
        {
            media.Pause();
            media.PlaybackSession.Position = TimeSpan.FromSeconds(10);
            media.PlaybackSession.PlaybackRate = 1.5;
            await Task.Delay(500);
            media.Play();
            await Task.Delay(1000);
            if (media.PlaybackSession.Position.TotalSeconds < 10) throw new Exception($"{label}: seeking failed.");
            Console.WriteLine($"PASS {label}: seek and 1.5x playback");
        }
        media.Pause();
    }
}
