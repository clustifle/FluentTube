using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using System.Text.Json;

// Windows integration check: decode a local file and a freshly resolved public
// stream through the same MediaPlayer engine used by the WinUI control.
if (args.Length != 2) throw new ArgumentException("Provide a sample MP4 path and yt-dlp stream metadata JSON path.");
await Check("local import", MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(args[0]))));
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
        media.Pause();
    }
}
