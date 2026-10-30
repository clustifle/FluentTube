using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net.Http;
using Windows.ApplicationModel.DataTransfer;
namespace FluentTube;

internal sealed record ShortsSource(string Label, string Url) { public override string ToString() => Label; }
public sealed partial class MainWindow
{
    private bool syncingShortsSources;
    private string SelectedShortsSource => (ShortsSourceChoice.SelectedItem as ShortsSource)?.Url ?? "https://www.youtube.com/@YouTube";
    private void RefreshShortsSources()
    {
        var selected = SelectedShortsSource;
        syncingShortsSources = true;
        var sources = new List<ShortsSource> { new("YouTube", "https://www.youtube.com/@YouTube") };
        sources.AddRange(library.Subscriptions.Select(c => new ShortsSource(c.Name, c.Url)));
        ShortsSourceChoice.ItemsSource = sources;
        ShortsSourceChoice.SelectedItem = sources.FirstOrDefault(s => s.Url == selected) ?? sources[0];
        syncingShortsSources = false;
    }
    private async void ShortsSourceChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ready && !syncingShortsSources && browseKind == "shorts") await LoadBrowse("shorts");
    }
    private async void OpenChannelShorts(object sender, RoutedEventArgs args)
    {
        if (browsingChannel is not null) await LoadBrowse(browseKind == "channelshorts" ? "channel" : "channelshorts", channel: browsingChannel);
    }
    private async void PreviousShort(object sender, RoutedEventArgs args) => await MoveQueue(-1);
    private async void NextShort(object sender, RoutedEventArgs args) => await MoveQueue(1);
    private void CopyVideoLink(object sender, RoutedEventArgs args)
    {
        if (current is null || current.LocalPath is not null) { ShowMessage("This is a local video."); return; }
        var data = new DataPackage();
        data.SetText(current.IsShort ? $"https://www.youtube.com/shorts/{current.Id}" : $"https://www.youtube.com/watch?v={current.Id}");
        try { Clipboard.SetContent(data); ShowMessage("Video link copied."); }
        catch (Exception) { ShowMessage("The clipboard is busy. Try copying the link again.", true); }
    }
    private void ClearUpcoming(object sender, RoutedEventArgs args)
    {
        var index = queue.FindIndex(v => v.Id == current?.Id);
        if (index < 0) queue.Clear(); else queue.RemoveRange(index + 1, queue.Count - index - 1);
        UpdateQueueButtons(); ShowMessage("Upcoming videos cleared.");
    }
    private readonly HttpClient captionsClient = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    private CancellationTokenSource? captionsRequest;
    private List<CaptionCue> captionCues = [];
    private string? activeCaption;
    private void ResetCaptions()
    {
        captionsRequest?.Cancel(); captionCues = []; activeCaption = null;
        CaptionOverlay.Visibility = Visibility.Collapsed;
        UpdateCaptionsMenu([]);
    }
    private void UpdateCaptionsMenu(IReadOnlyList<CaptionTrack> tracks)
    {
        CaptionsMenu.Items.Clear();
        var off = new ToggleMenuFlyoutItem { Text = "Off", IsChecked = activeCaption is null };
        off.Click += (_, _) => { captionsRequest?.Cancel(); captionCues = []; activeCaption = null; UpdateCaptionsMenu(availableStream?.Captions ?? []); RefreshCaptions(); };
        CaptionsMenu.Items.Add(off);
        var import = new MenuFlyoutItem { Text = "Open subtitle file…" };
        import.Click += ImportCaptions;
        CaptionsMenu.Items.Add(import);
        foreach (var track in tracks)
        {
            var item = new ToggleMenuFlyoutItem { Text = track.Label, IsChecked = activeCaption == track.Language };
            item.Click += async (_, _) => await LoadCaptions(track);
            CaptionsMenu.Items.Add(item);
        }
    }
    private async Task LoadCaptions(CaptionTrack track)
    {
        captionsRequest?.Cancel();
        var active = captionsRequest = new CancellationTokenSource();
        var id = current?.Id;
        try
        {
            var cues = Captions.Parse(await captionsClient.GetStringAsync(track.Url, active.Token));
            if (closed || active.IsCancellationRequested || current?.Id != id) return;
            if (cues.Count == 0) throw new InvalidOperationException("No usable captions were returned.");
            captionCues = cues; activeCaption = track.Language;
            UpdateCaptionsMenu(availableStream?.Captions ?? []); RefreshCaptions();
            ShowMessage($"Captions: {track.Label}");
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!closed && !active.IsCancellationRequested)
            {
                UpdateCaptionsMenu(availableStream?.Captions ?? []);
                ShowMessage("Captions could not be loaded. YouTube may limit caption access; you can open a local .vtt or .srt file instead.", true);
            }
        }
        finally { if (ReferenceEquals(captionsRequest, active)) captionsRequest = null; active.Dispose(); }
    }
    private async void ImportCaptions(object sender, RoutedEventArgs args)
    {
        var id = current?.Id;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".vtt"); picker.FileTypeFilter.Add(".srt");
            var file = await picker.PickSingleFileAsync();
            if (file is null || closed || id != current?.Id) return;
            if ((await file.GetBasicPropertiesAsync()).Size > 4 * 1024 * 1024) throw new InvalidOperationException();
            var cues = Captions.Parse(await Windows.Storage.FileIO.ReadTextAsync(file));
            if (closed || id != current?.Id) return;
            if (cues.Count == 0) throw new InvalidOperationException();
            captionsRequest?.Cancel(); captionCues = cues; activeCaption = "local";
            UpdateCaptionsMenu(availableStream?.Captions ?? []); RefreshCaptions(); ShowMessage($"Subtitles: {file.Name}");
        }
        catch (Exception) { if (!closed) ShowMessage("Could not open these subtitles. Choose a valid UTF-8 .vtt or .srt file under 4 MB.", true); }
    }
    private void RefreshCaptions()
    {
        CaptionText.Text = Captions.At(captionCues, media.PlaybackSession.Position.TotalSeconds);
        CaptionOverlay.Visibility = CaptionText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CaptionText.FontSize = isPictureInPicture ? 14 : 18;
        CaptionOverlay.Margin = new Thickness(16, 0, 16, controlsVisible ? PlayerControls.ActualHeight + 28 : 24);
    }
}
