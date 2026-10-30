using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace FluentTube;
public sealed partial class MainWindow
{
    private void RefreshSubscriptionNavigation()
    {
        var selected = (Navigation.SelectedItem as NavigationViewItem)?.Tag as ChannelSubscription;
        syncingNavigation = true;
        try
        {
            foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>().Where(i => i.Tag is ChannelSubscription).ToArray()) Navigation.MenuItems.Remove(item);
            foreach (var channel in library.Subscriptions.OrderBy(c => c.Name)) Navigation.MenuItems.Add(new NavigationViewItem { Content = channel.Name, Tag = channel, Icon = new SymbolIcon(Symbol.Contact) });
            if (selected is not null) Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag is ChannelSubscription c && c.Id == selected.Id);
            ChannelsNavHeader.Visibility = library.Subscriptions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { syncingNavigation = false; }
    }
    private bool SaveLibrary()
    {
        try { library.Save(); return true; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { ShowMessage("Could not save your local library. Check disk access.", true); return false; }
    }
    private void RenderBrowse(List<Video> videos, string empty)
    {
        browseVideos = videos;
        Videos.ItemsSource = browseVideos.ToList();
        EmptyState.Text = empty;
        EmptyCard.Visibility = videos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = EmptyCard.Visibility;
    }
    private async Task LoadBrowse(string kind, string? query = null, ChannelSubscription? channel = null, bool append = false)
    {
        request?.Cancel();
        var active = request = new CancellationTokenSource();
        browseRequest = active;
        ShowPanel("browse");
        if (!append) { browseKind = kind; browseQuery = query; browsingChannel = channel; browsePage = 1; }
        var page = append ? browsePage + 1 : 1;
        syncingNavigation = true;
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => channel is not null ? i.Tag is ChannelSubscription c && c.Id == channel.Id : i.Tag as string == (kind == "search" ? "home" : kind));
        syncingNavigation = false;
        PageTitle.Text = kind switch { "search" => $"Results for “{query}”", "channel" => channel?.Name ?? "Channel", "subscriptions" => "Subscriptions", "saved" => "Watch later", "history" => "History", _ => "Home" };
        PageSubtitle.Text = kind switch
        {
            "home" => library.Subscriptions.Count > 0 ? "Recent uploads from channels you follow locally." : "Explore YouTube. Follow channels to build your own feed.",
            "subscriptions" => "Latest uploads from your local subscriptions. These do not sync with a Google account.",
            "history" => "Videos played on this device, most recent first.", "saved" => "Your saved videos. Right-click a video to remove it.",
            "channel" => "Channel uploads", _ => "Videos on YouTube"
        };
        TopicChoice.Visibility = kind == "home" ? Visibility.Visible : Visibility.Collapsed;
        FollowButton.Visibility = kind == "channel" ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryButton.Visibility = kind == "history" ? Visibility.Visible : Visibility.Collapsed;
        UpdateFollowButtons();
        if (!append)
        {
            var cached = kind switch { "saved" => saved.ToList(), "history" => library.Watched.ToList(), "home" or "subscriptions" when library.Subscriptions.Count > 0 => library.Feed.ToList(), _ => new List<Video>() };
            RenderBrowse(cached, kind == "subscriptions" ? "No subscriptions yet. Choose Add channel, or open a channel from a video and follow it." : "Loading videos…");
        }
        BrowseProgress.Visibility = Visibility.Visible;
        LoadMoreButton.Visibility = Visibility.Collapsed;
        browseBusy = true;
        ShowMessage("Loading videos…");
        try
        {
            List<Video> videos;
            var failed = 0;
            if (kind == "saved") videos = saved.ToList();
            else if (kind == "history") videos = library.Watched.ToList();
            else if (kind == "subscriptions" || kind == "home" && library.Subscriptions.Count > 0 && TopicChoice.SelectedIndex == 0)
            {
                var result = await YouTubeService.FetchSubscriptions(library.Subscriptions.ToList(), active.Token);
                failed = result.FailedChannels;
                videos = result.Videos;
                if (failed > 0) videos = videos.Concat(library.Feed).DistinctBy(v => v.Id).OrderByDescending(v => v.Published).ToList();
                if (!active.IsCancellationRequested) { library.Feed = videos; SaveLibrary(); }
            }
            else if (kind == "channel")
            {
                var result = await YouTubeService.FetchChannel(channel!.Url, active.Token, page);
                videos = result.Videos;
            }
            else
            {
                var topic = TopicChoice.SelectedItem as string;
                var search = kind == "search" ? query! : topic is null or "All" ? "science technology music documentaries" : topic;
                videos = await YouTubeService.Fetch(search, active.Token, page);
            }
            if (closed || active.IsCancellationRequested) return;
            browsePage = page;
            var combined = append ? browseVideos.Concat(videos).DistinctBy(v => v.Id).ToList() : videos;
            RenderBrowse(combined, kind switch { "saved" => "Save a video to Watch later to find it here.", "history" => "Play a video to add it to your history.", "subscriptions" => library.Subscriptions.Count == 0 ? "No subscriptions yet. Choose Add channel to follow a creator." : "No recent uploads are available. Try Refresh.", _ => "No videos found. Try another search or refresh this page." });
            var pageable = kind is "search" or "channel" || kind == "home" && (library.Subscriptions.Count == 0 || TopicChoice.SelectedIndex > 0);
            LoadMoreButton.Visibility = pageable && videos.Count >= 24 && page < 10 ? Visibility.Visible : Visibility.Collapsed;
            ShowMessage(failed == 0 ? $"{combined.Count} videos" : $"Couldn't refresh {failed} channel(s). Showing available and cached uploads.", failed > 0);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!active.IsCancellationRequested)
            {
                if (browseVideos.Count == 0) RenderBrowse([], "Videos could not be loaded. Choose Refresh to try again.");
                ShowMessage(e is InvalidOperationException ? e.Message : "Could not load these videos. Check your connection and choose Refresh.", true);
            }
        }
        finally
        {
            if (ReferenceEquals(request, active)) request = null;
            if (ReferenceEquals(browseRequest, active)) { browseRequest = null; browseBusy = false; if (!closed) BrowseProgress.Visibility = Visibility.Collapsed; }
            active.Dispose();
        }
    }
    private async void RefreshPage(object sender, RoutedEventArgs args) => await LoadBrowse(browseKind, browseQuery, browsingChannel);
    private async void LoadMore(object sender, RoutedEventArgs args) { if (!browseBusy) await LoadBrowse(browseKind, browseQuery, browsingChannel, append: true); }
    private async void TopicChanged(object sender, SelectionChangedEventArgs args) { if (ready && browseKind == "home") await LoadBrowse("home"); }
    private async void AddChannel(object sender, RoutedEventArgs args)
    {
        var input = new TextBox { PlaceholderText = "@channel or https://www.youtube.com/@channel" };
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Open a channel", Content = input, PrimaryButtonText = "Open", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await OpenChannel(input.Text.Trim());
    }
    private async Task OpenChannel(string input)
    {
        request?.Cancel();
        var active = request = new CancellationTokenSource();
        ShowMessage("Opening channel…");
        YouTubeService.ChannelPage? result = null;
        try { result = await YouTubeService.FetchChannel(input, active.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!active.IsCancellationRequested) ShowMessage(e is InvalidOperationException ? e.Message : "Could not open this channel. Try its full YouTube channel URL.", true); }
        finally { if (!ReferenceEquals(request, active) || active.IsCancellationRequested) result = null; if (ReferenceEquals(request, active)) request = null; active.Dispose(); }
        if (closed || result is null) return;
        // Opening a channel already retrieved page one; display it without a second lookup.
        browseKind = "channel"; browsingChannel = result.Channel; browsePage = 1; browseQuery = null;
        syncingNavigation = true;
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag is ChannelSubscription c && c.Id == result.Channel.Id);
        syncingNavigation = false;
        ShowPanel("browse");
        PageTitle.Text = result.Channel.Name; PageSubtitle.Text = "Channel uploads";
        TopicChoice.Visibility = Visibility.Collapsed; FollowButton.Visibility = Visibility.Visible; ClearHistoryButton.Visibility = Visibility.Collapsed;
        RenderBrowse(result.Videos, "This channel has no public uploads available.");
        BrowseProgress.Visibility = Visibility.Collapsed; browseBusy = false;
        LoadMoreButton.Visibility = result.Videos.Count >= 24 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFollowButtons();
        ShowMessage($"{result.Videos.Count} channel videos");
    }
    private void UpdateFollowButtons()
    {
        FollowButton.Content = library.Subscriptions.Any(c => c.Id == browsingChannel?.Id) ? "Unfollow" : "Follow";
        WatchFollowButton.Content = library.Subscriptions.Any(c => c.Id == current?.ChannelId) ? "Unfollow channel" : "Follow channel";
        WatchFollowButton.IsEnabled = YouTubeService.IsChannelId(current?.ChannelId);
        WatchChannel.IsEnabled = YouTubeService.IsChannelId(current?.ChannelId);
    }
    private void UpdateWatchDetails()
    {
        WatchChannel.Content = current?.Channel ?? "YouTube";
        VideoDescription.Text = string.IsNullOrWhiteSpace(currentDescription) ? "No description available." : currentDescription.Length > 6000 ? currentDescription[..6000] : currentDescription;
        UpdateFollowButtons();
    }
    private void ChangeFollow(ChannelSubscription channel)
    {
        var followed = library.Subscriptions.Any(c => c.Id == channel.Id);
        if (followed) { library.Subscriptions.RemoveAll(c => c.Id == channel.Id); library.Feed.RemoveAll(v => v.ChannelId == channel.Id); }
        else library.Subscriptions.Add(channel);
        var stored = SaveLibrary(); RefreshSubscriptionNavigation(); UpdateFollowButtons();
        if (stored) ShowMessage(followed ? "Channel unfollowed locally." : "Channel followed locally. Its uploads will appear in Home and Subscriptions.");
    }
    private void FollowChannel(object sender, RoutedEventArgs args) { if (browsingChannel is not null) ChangeFollow(browsingChannel); }
    private void FollowWatchChannel(object sender, RoutedEventArgs args) { if (current?.ChannelId is string id && YouTubeService.IsChannelId(id)) ChangeFollow(new(id, current.Channel)); }
    private static Video? Card(object sender) => (sender as FrameworkElement)?.Tag as Video ?? (sender as FrameworkElement)?.DataContext as Video;
    private async void OpenCardChannel(object sender, RoutedEventArgs args)
    {
        var video = Card(sender);
        if (video?.ChannelId is string id) await OpenChannel(id);
        else ShowMessage("This video does not include a channel ID. Choose Add channel and enter the creator's @handle.", true);
    }
    private async void OpenWatchChannel(object sender, RoutedEventArgs args) { if (current?.ChannelId is string id) await OpenChannel(id); }
    private async void PlayCard(object sender, RoutedEventArgs args) { if (Card(sender) is Video video) await StartVideo(video); }
    private void SaveCard(object sender, RoutedEventArgs args) { if (Card(sender) is Video video) SaveForLater(video); }
    private void RemoveSavedCard(object sender, RoutedEventArgs args)
    {
        if (Card(sender) is not Video video) return;
        var next = saved.Where(v => v.Id != video.Id).ToList();
        try { LibraryStore.SaveVideos(next); saved = next; if (browseKind == "saved") RenderBrowse(saved.ToList(), "No saved videos."); ShowMessage("Removed from Watch later."); }
        catch (System.IO.IOException) { ShowMessage("Could not update Watch later.", true); }
    }
    private async void QueueCardNext(object sender, RoutedEventArgs args)
    {
        if (Card(sender) is not Video video) return;
        if (current is null) { queue = [video]; await Play(video); return; }
        if (video.Id == current?.Id) return;
        queue.RemoveAll(v => v.Id == video.Id);
        var index = queue.FindIndex(v => v.Id == current?.Id);
        queue.Insert(index >= 0 ? index + 1 : queue.Count, video);
        UpdateQueueButtons(); ShowMessage("Added to play next.");
    }
    private async void ClearWatchHistory(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Clear watch history?", Content = "This removes the local list of watched videos. Saved videos and subscriptions are kept.", PrimaryButtonText = "Clear", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        library.Watched.Clear(); SaveLibrary(); await LoadBrowse("history");
    }
    private void QueueAutoplayChanged(object sender, RoutedEventArgs args) { if (ready && !applyingSettings) NextChoice.IsOn = QueueAutoplay.IsOn; }
    private async void ShowQueue(object sender, RoutedEventArgs args)
    {
        var list = new ListView { ItemsSource = queue.ToList(), ItemTemplate = QueueView.ItemTemplate, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.None, MaxHeight = 500 };
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Watch queue", Content = list, CloseButtonText = "Close" };
        Video? selected = null;
        list.ItemClick += (_, e) => { selected = e.ClickedItem as Video; dialog.Hide(); };
        await dialog.ShowAsync();
        if (selected is not null) await Play(selected);
    }
}
