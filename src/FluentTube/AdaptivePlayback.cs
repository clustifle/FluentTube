using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;
namespace FluentTube;
internal sealed class AdaptivePlayback : IDisposable
{
    internal AdaptiveMediaSource Source { get; }
    private readonly InMemoryRandomAccessStream manifest;
    private readonly Windows.Web.Http.HttpClient downloads = new();
    private readonly string audioTag;
    private readonly CancellationTokenSource stopping = new();
    private int disposed;
    private AdaptivePlayback(AdaptiveMediaSource source, InMemoryRandomAccessStream manifest, Uri audio)
    {
        Source = source; this.manifest = manifest;
        audioTag = System.Text.RegularExpressions.Regex.Match(Uri.UnescapeDataString(audio.AbsoluteUri), @"(?:/itag/|[?&]itag=)(\d+)").Groups[1].Value;
        Source.DownloadRequested += Download;
    }
    private async void Download(AdaptiveMediaSource sender, AdaptiveMediaSourceDownloadRequestedEventArgs args)
    {
        if (args.ResourceType != AdaptiveMediaSourceResourceType.MediaSegment) return;
        var deferral = args.GetDeferral();
        try
        {
            var tag = System.Text.RegularExpressions.Regex.Match(Uri.UnescapeDataString(args.ResourceUri.AbsoluteUri), @"(?:/itag/|[?&]itag=)(\d+)").Groups[1].Value;
            if (Volatile.Read(ref disposed) != 0) return;
            var download = downloads.GetInputStreamAsync(args.ResourceUri);
            using var cancel = stopping.Token.Register(download.Cancel);
            args.Result.InputStream = await download;
            // YouTube serves HLS segments as octet-stream. Windows needs the
            // actual elementary stream type to decode separate AAC and TS tracks.
            args.Result.ContentType = tag == audioTag ? "audio/aac" : "video/mp2t";
        }
        catch (Exception) { /* The adaptive source reports download failure to MediaPlayer. */ }
        finally { try { deferral.Complete(); } catch (Exception) { /* The player may have closed while this segment was in flight. */ } }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        foreach (var cleanup in new Action[] { stopping.Cancel, () => Source.DownloadRequested -= Download, Source.Dispose, downloads.Dispose, manifest.Dispose, stopping.Dispose })
        {
            try { cleanup(); }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        }
    }
    internal static async Task<AdaptivePlayback> Create(IEnumerable<StreamVariant> variants, CancellationToken cancellation = default)
    {
        var items = variants.ToList();
        var manifest = StreamVariants.Manifest(items);
        var memory = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(memory.GetOutputStreamAt(0))) { writer.WriteString(manifest); await writer.StoreAsync(); }
        memory.Seek(0);
        var operation = AdaptiveMediaSource.CreateFromStreamAsync(memory.GetInputStreamAt(0), items[0].Video, "application/vnd.apple.mpegurl");
        using var cancel = cancellation.Register(operation.Cancel);
        AdaptiveMediaSourceCreationResult result;
        try { result = await operation; }
        catch { memory.Dispose(); throw; }
        if (result.Status != AdaptiveMediaSourceCreationStatus.Success) { memory.Dispose(); throw new InvalidOperationException($"Windows could not open this quality ({result.Status})."); }
        var initial = items.Where(v => v.QualityHeight <= 360).OrderByDescending(v => v.QualityHeight).FirstOrDefault() ?? items.OrderBy(v => v.QualityHeight).First();
        if (result.MediaSource.AvailableBitrates.Contains(initial.Bandwidth)) result.MediaSource.InitialBitrate = initial.Bandwidth;
        return new(result.MediaSource, memory, items[0].Audio);
    }
}
