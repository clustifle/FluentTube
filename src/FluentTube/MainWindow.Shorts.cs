using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Playback;
namespace FluentTube;

public sealed partial class MainWindow
{
    private bool shortsFeed, shortsMoving, shortsFetching;
    private int shortsPage = 1, shortsWheel;
    private string? shortsChannel, shortsSearchQuery;
    private List<Video> searchShorts = [];
    private CancellationTokenSource? shortsRequest;
    private double shortWatched;
    private string? shortFeedbackId;
    private DateTimeOffset shortTick = DateTimeOffset.UtcNow, lastShortScroll;

    private async Task<List<Video>> FetchShortsMix(int page, CancellationToken token)
    {
        var creators = library.Subscriptions.Select(c => c.Url)
            .Concat(library.Watched.Where(v => v.IsShort && v.ChannelId is not null)
                .OrderByDescending(v => library.ShortsAffinity.GetValueOrDefault(ShortsRanking.Creator(v)))
                .Select(v => "https://www.youtube.com/channel/" + v.ChannelId))
            .Concat(new[] { "https://www.youtube.com/@YouTube", "https://www.youtube.com/@DudePerfect", "https://www.youtube.com/@MrBeast" })
            .Distinct().Take(18).ToList();
        var offset = ((page - 1) * 3) % creators.Count;
        var sources = Enumerable.Range(0, Math.Min(3, creators.Count)).Select(i => creators[(offset + i) % creators.Count]);
        var groups = await Task.WhenAll(sources.Select(async url =>
        {
            try { return await YouTubeService.FetchShorts(url, token, (page - 1) / Math.Max(1, (creators.Count + 2) / 3) % 10 + 1); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return new List<Video>(); }
        }));
        return ShortsRanking.Rank(groups.SelectMany(v => v), library.ShortsAffinity, library.SeenShorts);
    }
    private async Task LoadSearchShorts(string query, CancellationToken token)
    {
        searchShorts = [];
        HomeShorts.ItemsSource = null;
        var clips = await YouTubeService.SearchShorts(query, token);
        if (closed || token.IsCancellationRequested || browseKind != "search" || browseQuery != query) return;
        searchShorts = clips;
        HomeShorts.ItemsSource = clips;
        HomeShortsHint.Text = clips.Count > 0 ? "Shorts matching your search. Open one and scroll for more." : "No Shorts found for this search.";
    }
    private void ShowShortsOptions(object sender, RoutedEventArgs args) => MorePlaybackButton.Flyout.ShowAt(ShortsMoreButton);
    private void ShowPlaybackSpeed(object sender, RoutedEventArgs args) => DispatcherQueue.TryEnqueue(() => PlayerSpeed.Flyout.ShowAt(shortsFeed ? ShortsMoreButton : PlayerSpeed));
    private async Task LoadHomeShorts(CancellationToken token)
    {
        HomeShorts.ItemsSource = ShortsRanking.Rank(library.Shorts, library.ShortsAffinity, library.SeenShorts).Take(12).ToList();
        var clips = await FetchShortsMix(1, token);
        if (closed || token.IsCancellationRequested || browseKind != "home") return;
        if (clips.Count > 0) { library.Shorts = clips.Take(120).ToList(); SaveLibrary(); }
        HomeShorts.ItemsSource = library.Shorts.Take(12).ToList();
    }
    private async void FewerShortsLikeThis(object sender, RoutedEventArgs args)
    {
        if (current is not { IsShort: true } video) return;
        RecordShortFeedback();
        library.ShortsAffinity[ShortsRanking.Creator(video)] = -8;
        SaveLibrary();
        await MoveQueue(1, autoPlay: true);
    }
    private void ResetShortsRecommendations(object sender, RoutedEventArgs args)
    {
        library.ShortsAffinity.Clear(); library.SeenShorts.Clear(); SaveLibrary();
        ShowMessage("Shorts recommendations reset on this device.");
    }
    private async void OpenShortsFeed(object sender, RoutedEventArgs args) => await LoadBrowse("shorts");
    private async void HomeShortClicked(object sender, ItemClickEventArgs args)
    {
        shortsFeed = true; shortsChannel = null; shortsPage = 1;
        shortsSearchQuery = browseKind == "search" ? browseQuery : null;
        queue = shortsSearchQuery is null ? ShortsRanking.Rank(library.Shorts, library.ShortsAffinity, library.SeenShorts) : searchShorts.ToList();
        var video = (Video)args.ClickedItem;
        queue.RemoveAll(v => v.Id == video.Id); queue.Insert(0, video);
        await Play(video, startPlaying: true);
        var next = queue.Skip(1).FirstOrDefault();
        if (next is not null && preferences.PrepareOnHover) _ = Prepare(next, 0);
    }
    private void RecordShortFeedback()
    {
        if (current is not { IsShort: true } video || shortFeedbackId != video.Id) return;
        shortFeedbackId = null;
        if (shortWatched <= 0) return;
        var key = ShortsRanking.Creator(video);
        library.ShortsAffinity[key] = Math.Clamp(library.ShortsAffinity.GetValueOrDefault(key) * 0.95 +
            ShortsRanking.Feedback(shortWatched, media.PlaybackSession.NaturalDuration.TotalSeconds), -8, 8);
        library.SeenShorts.Remove(video.Id); library.SeenShorts.Insert(0, video.Id);
        library.SeenShorts = library.SeenShorts.Take(500).ToList();
        library.ShortsAffinity = library.ShortsAffinity.OrderByDescending(x => Math.Abs(x.Value)).Take(200).ToDictionary();
        SaveLibrary();
    }
    private void TickShorts()
    {
        var now = DateTimeOffset.UtcNow;
        if (shortsFeed && PlayerPanel.Visibility == Visibility.Visible && current?.IsShort == true &&
            media.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            shortWatched += Math.Clamp((now - shortTick).TotalSeconds, 0, 1);
        shortTick = now;
    }
    private async Task RefillShorts()
    {
        if (shortsFetching || !shortsFeed || closed) return;
        shortsFetching = true;
        var active = shortsRequest = new CancellationTokenSource();
        try
        {
            var clips = shortsSearchQuery is not null ? await YouTubeService.SearchShorts(shortsSearchQuery, active.Token, ++shortsPage) : shortsChannel is null ? await FetchShortsMix(++shortsPage, active.Token) :
                await YouTubeService.FetchShorts(shortsChannel, active.Token, ++shortsPage);
            if (closed || active.IsCancellationRequested || !shortsFeed) return;
            var currentIndex = queue.FindIndex(v => v.Id == current?.Id);
            if (currentIndex > 120) queue.RemoveRange(0, currentIndex - 60);
            var existing = queue.Select(v => v.Id).ToHashSet();
            queue.AddRange(clips.Where(v => existing.Add(v.Id)));
            UpdateQueueButtons();
            var next = queue.SkipWhile(v => v.Id != current?.Id).Skip(1).FirstOrDefault();
            if (next is not null) _ = Prepare(next, 0);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed && !active.IsCancellationRequested) ShowMessage("Couldn't load more Shorts. Scroll down to retry.", true); }
        finally { if (ReferenceEquals(shortsRequest, active)) shortsRequest = null; shortsFetching = false; active.Dispose(); }
    }
    private async void ShortsWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (!shortsFeed || isPictureInPicture || playerMenuOpen || holdingPlayer) return;
        args.Handled = true;
        if ((DateTimeOffset.UtcNow - lastShortScroll).TotalMilliseconds < 650) return;
        shortsWheel += args.GetCurrentPoint(VideoSurface).Properties.MouseWheelDelta;
        if (Math.Abs(shortsWheel) < 120) return;
        var direction = shortsWheel < 0 ? 1 : -1; shortsWheel = 0; lastShortScroll = DateTimeOffset.UtcNow;
        await MoveQueue(direction, autoPlay: true);
    }
    private async void ShortsSwipeCompleted(object sender, ManipulationCompletedRoutedEventArgs args)
    {
        if (!shortsFeed || isPictureInPicture || Math.Abs(args.Cumulative.Translation.Y) < 60) return;
        args.Handled = true;
        await MoveQueue(args.Cumulative.Translation.Y < 0 ? 1 : -1, autoPlay: true);
    }
}
