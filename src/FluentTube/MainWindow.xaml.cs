using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;
using Microsoft.UI.Xaml.Input;
namespace FluentTube;
public sealed partial class MainWindow : Window
{
    private Video? current;
    private List<Video> saved = [];
    private CancellationTokenSource? request;
    private readonly MediaPlayer media = new();
    private MediaSource? source;
    private Preferences preferences = Preferences.Load();
    private bool ready, applyingSettings, closed, syncingNavigation;
    private int playbackGeneration;
    private readonly PlaybackHistory history = new();
    private readonly System.Diagnostics.Stopwatch openingTime = new();
    private readonly DispatcherTimer historyTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource? preparation;
    private List<Video> queue = [];
    private string? loadedVideoId;
    private bool playWhenOpened;
    private string? preparingId;
    private readonly LocalLibrary library = LocalLibrary.Load();
    private string browseKind = "home";
    private string? browseQuery;
    private ChannelSubscription? browsingChannel;
    private int browsePage = 1;
    private List<Video> browseVideos = [];
    private bool browseBusy;
    private CancellationTokenSource? browseRequest;
    private string? lastRecordedId;
    private string currentDescription = "";
    private int recoveryAttempts;
    private bool recoveringPlayback;
    private double? recoverySeek;
    private bool? recoveryPlay;
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "FluentTube.ico"));
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.Changed += (_, _) => UpdateTitleBar();
        Root.ActualThemeChanged += (_, _) => UpdateTitleBar();
        Activated += (_, e) =>
        {
            TitleBarContent.Opacity = e.WindowActivationState == WindowActivationState.Deactivated ? 0.65 : 1;
            if (e.WindowActivationState != WindowActivationState.Deactivated) ResizePictureInPicture();
        };
        PlayerPanel.SizeChanged += (_, _) => UpdateVideoSize();
        Root.SizeChanged += (_, e) =>
        {
            ContentLayout.Padding = new Thickness(e.NewSize.Width < 800 ? 16 : 24, 16, e.NewSize.Width < 800 ? 16 : 24, 16);
            UpNextPanel.Visibility = e.NewSize.Width >= 1150 ? Visibility.Visible : Visibility.Collapsed;
        };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 840));
        Player.SetMediaPlayer(media);
        InitializeUnifiedPlayer();
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(PlayerKeyDown), true);
        historyTimer.Tick += (_, _) => SavePosition();
        historyTimer.Start();
        media.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || current is null || loadedVideoId != current.Id || lastRecordedId == current.Id || media.PlaybackSession.PlaybackState != MediaPlaybackState.Playing) return;
            library.Record(current);
            lastRecordedId = current.Id;
            SaveLibrary();
        });
        media.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || current?.Id != loadedVideoId || request is not null) return;
            media.PlaybackSession.PlaybackRate = preferences.Speed;
            LoadingPlayback.Visibility = Visibility.Collapsed;
            OnUnifiedMediaOpened();
            ShowMessage($"Video ready • {VideoTitle.Text}");
            if (preferences.PrepareOnHover && preferences.AutoPlayNext) _ = PrepareFollowingVideo();
        });
        media.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (closed || current?.Id != loadedVideoId || request is not null) return;
            LoadingPlayback.Visibility = Visibility.Collapsed;
            if (FallBackFromAdaptive()) return;
            if (recoveringPlayback) return;
            if (current is { LocalPath: null } && recoveryAttempts == 0 && e.Error is not (MediaPlayerError.DecodingError or MediaPlayerError.Aborted))
            {
                await RecoverPlayback();
                return;
            }
            if (current is { LocalPath: null }) YouTubeService.InvalidateStream(current.Id);
            ShowMessage(e.Error switch
            {
                MediaPlayerError.NetworkError => "The video connection was interrupted. Check your connection, then choose Retry video.",
                MediaPlayerError.DecodingError => "Windows could not decode this video. Try another quality or a supported local file.",
                MediaPlayerError.SourceNotSupported => "This stream is not supported by Windows. Retry for a fresh stream or choose another quality.",
                _ => "Windows could not play this source. Choose Retry video or open a supported local file."
            }, true);
        });
        media.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (closed || current?.Id != loadedVideoId || request is not null) return;
            if (preferences.RememberPosition && loadedVideoId is not null)
            {
                try { history.Save(loadedVideoId, 0, media.PlaybackSession.NaturalDuration.TotalSeconds); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            if (!preferences.Loop && PlayerPanel.Visibility == Visibility.Visible && (current?.IsShort == true ? preferences.ShortsAutoNext : preferences.AutoPlayNext)) await MoveQueue(1, autoPlay: true);
        });
        Root.Loaded += Initialize;
        Closed += OnWindowClosed;
    }
    private static void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
    }
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (closed) return;
        Cleanup(SavePosition);
        closed = true;
        playbackGeneration++;
        try
        {
            Cleanup(historyTimer.Stop);
            foreach (var stop in new[] { preparation, request, browseRequest, qualityRequest, captionsRequest }.OfType<CancellationTokenSource>().Distinct()) Cleanup(stop.Cancel);
            Cleanup(YouTubeService.Shutdown);
            Cleanup(() => media.Pause());
            Cleanup(() => Player.SetMediaPlayer(null));
            Cleanup(() => media.Source = null);
            Cleanup(media.Dispose);
            Cleanup(() => source?.Dispose());
            source = null;
            DisposeUnifiedPlayer();
        }
        finally { ((App)Application.Current).CompleteShutdown(); }
    }
    private async void Initialize(object sender, RoutedEventArgs args)
    {
        if (ready) return;
        if (Root.XamlRoot is not null) Root.XamlRoot.Changed += (_, _) => UpdateTitleBar();
        saved = LibraryStore.LoadVideos();
        RefreshSubscriptionNavigation();
        PopulateSettings();
        ready = true;
        ApplyPreferences();
        ShowMessage("Ready");
        var link = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        var id = link is null ? null : YouTubeService.ParseVideoId(link);
        if (id is not null) await Play(new Video(id, "YouTube video", "YouTube", IsShort: YouTubeService.IsShortUrl(link!)));
        else if (link is not null && YouTubeService.ParseChannelUrl(link) is not null) await OpenChannel(link);
        else if (link is not null && File.Exists(link)) await PlayLocal(link);
        else if (preferences.StartWithSaved) Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag as string == "saved");
        else await LoadBrowse("home");
    }
    private void ShowMessage(string message, bool error = false)
    {
        if (closed) return;
        Status.Title = error ? "Something needs attention" : "FluentTube";
        Status.Message = message;
        Status.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        Status.IsOpen = error;
        if (error && current is not null && PlayerPanel.Visibility == Visibility.Visible)
        {
            var retry = new Button { Content = "Retry video" };
            retry.Click += RetryPlayback;
            Status.ActionButton = retry;
        }
        else Status.ActionButton = null;
        StatusText.Text = message;
        StatusText.Visibility = error ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ShowPanel(string panel)
    {
        if (panel != "player") { SetPictureInPicture(false); SetFullscreen(false); playbackGeneration++; SavePosition(); media.Pause(); }
        BrowsePanel.Visibility = panel == "browse" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PlayerPanel.Visibility = panel == "player" ? Visibility.Visible : Visibility.Collapsed;
        AnimatePage(panel == "player" ? PlayerPanel : panel == "settings" ? SettingsPanel : BrowsePanel);
        NowPlayingItem.IsEnabled = current is not null;
        if (panel == "player") DispatcherQueue.TryEnqueue(() => { if (!closed && PlayerPanel.Visibility == Visibility.Visible) Player.Focus(FocusState.Programmatic); });
    }
    private async void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!ready || syncingNavigation) return;
        request?.Cancel();
        if (args.IsSettingsSelected) { ShowPanel("settings"); return; }
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if ((args.SelectedItem as NavigationViewItem)?.Tag is ChannelSubscription channel) { await LoadBrowse("channel", channel: channel); return; }
        if (tag == "import") { await PickVideo(); return; }
        await LoadBrowse(tag is "shorts" or "saved" or "history" or "subscriptions" ? tag : "home");
    }
    private Task LoadVideos(string? query) => LoadBrowse(string.IsNullOrWhiteSpace(query) ? "home" : "search", query);
    private async void SearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText.Trim();
        if (query.Length == 0) return;
        var id = YouTubeService.ParseVideoId(query);
        if (id is not null) await StartVideo(new Video(id, "YouTube video", "YouTube", IsShort: YouTubeService.IsShortUrl(query)));
        else if (YouTubeService.ParseChannelUrl(query) is not null) await OpenChannel(query);
        else await LoadVideos(query);
    }
    private async void VideoClicked(object sender, ItemClickEventArgs args)
    {
        if (ReferenceEquals(sender, QueueView)) await Play((Video)args.ClickedItem);
        else await StartVideo((Video)args.ClickedItem);
    }
    private async Task StartVideo(Video video)
    {
        queue = browseVideos.Any(v => v.Id == video.Id) ? browseVideos.ToList() : [video];
        await Play(video);
    }
    private void SetSource(MediaSource next, string id, AdaptivePlayback? adaptive = null)
    {
        media.Pause();
        media.Source = null;
        source?.Dispose();
        adaptivePlayback?.Dispose();
        adaptivePlayback = adaptive;
        source = next;
        loadedVideoId = id;
        media.Source = next;
    }
    private async Task Play(Video video, bool refresh = false, bool startPlaying = false, bool recovery = false)
    {
        if (!recovery) { recoveryAttempts = 0; recoverySeek = null; recoveryPlay = null; }
        lastRecordedId = null;
        if (preparingId != video.Id) preparation?.Cancel();
        if (video.LocalPath is not null) { await PlayLocal(video.LocalPath, startPlaying); return; }
        if (YouTubeService.ParseVideoId(video.Id) != video.Id) { ShowMessage("This saved video link is invalid.", true); return; }
        if (!refresh && loadedVideoId == video.Id && source is not null && media.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Paused)
        {
            request?.Cancel();
            current = current?.Id == video.Id ? current : video;
            ShowPanel("player");
            UpdateWatchDetails();
            UpdateQueueButtons();
            LoadingPlayback.Visibility = Visibility.Collapsed;
            ShowPlayerControls();
            if (availableStream is null && current is { LocalPath: null }) _ = LoadVideoQualities(current.Id, playbackGeneration);
            if (preferences.AutoPlay || startPlaying) media.Play();
            ShowMessage("Playback resumed");
            return;
        }
        SavePosition();
        if (refresh) YouTubeService.InvalidateStream(video.Id);
        request?.Cancel();
        var active = request = new CancellationTokenSource();
        var generation = ++playbackGeneration;
        current = video;
        ResetPlayerSurface(video);
        currentDescription = "";
        VideoTitle.Text = video.Title;
        ShowPanel("player");
        UpdateWatchDetails();
        UpdateQueueButtons();
        media.Pause();
        LoadingPlayback.Visibility = Visibility.Visible;
        openingTime.Restart();
        ShowMessage("Opening video");
        try
        {
            var stream = await YouTubeService.ResolveStream(video.Id, active.Token);
            if (closed || active.IsCancellationRequested || generation != playbackGeneration) return;
            current = video with { Title = stream.Title, ChannelId = stream.ChannelId ?? video.ChannelId, Channel = stream.Channel != "YouTube" ? stream.Channel : video.Channel };
            currentDescription = stream.Description;
            VideoTitle.Text = $"{stream.Title} • {stream.Quality}";
            UpdateWatchDetails();
            playWhenOpened = preferences.AutoPlay || startPlaying;
            SetSource(MediaSource.CreateFromUri(stream.Url), video.Id);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!active.IsCancellationRequested) { LoadingPlayback.Visibility = Visibility.Collapsed; ShowMessage(e is InvalidOperationException ? e.Message : "Native playback could not be prepared. Try again or import a local video.", true); } }
        finally { if (ReferenceEquals(request, active)) request = null; active.Dispose(); }
    }
    private async Task PlayLocal(string path, bool startPlaying = false)
    {
        SavePosition();
        openingTime.Restart();
        request?.Cancel();
        var generation = ++playbackGeneration;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            if (closed || generation != playbackGeneration) return;
            current = new Video("local:" + file.Path, file.DisplayName, "Local video", file.Path);
            ResetPlayerSurface(current);
            currentDescription = "Local file: " + file.Name;
            lastRecordedId = null;
            VideoTitle.Text = file.DisplayName;
            ShowPanel("player");
            UpdateWatchDetails();
            UpdateQueueButtons();
            LoadingPlayback.Visibility = Visibility.Visible;
            playWhenOpened = preferences.AutoPlay || startPlaying;
            SetSource(MediaSource.CreateFromStorageFile(file), current.Id);
        }
        catch (Exception) { ShowMessage("Could not open this local file. Check that it still exists and is readable.", true); }
    }
    private async Task PickVideo()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            foreach (var type in new[] { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".m4v", ".mp3", ".m4a" }) picker.FileTypeFilter.Add(type);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) await PlayLocal(file.Path);
        }
        catch (Exception) { ShowMessage("Could not open the video picker. Try passing the file path to FluentTube at launch.", true); }
    }
    private async void ImportVideo(object sender, RoutedEventArgs args) => await PickVideo();
    private async void NavigationInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "import") await PickVideo();
        else if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "player" && current is not null) await Play(current);
    }
    private void SaveVideo(object sender, RoutedEventArgs args)
    {
        if (current is null) return;
        SaveForLater(current);
    }
    private void SaveForLater(Video video)
    {
        if (saved.Any(v => v.Id == video.Id)) { ShowMessage("This video is already saved."); return; }
        try { var next = saved.Append(video).ToList(); LibraryStore.SaveVideos(next); saved = next; ShowMessage("Saved to Watch later."); }
        catch (IOException) { ShowMessage("Could not save the video. Check available disk space.", true); }
    }
    private async void OpenInBrowser(object sender, RoutedEventArgs args)
    {
        if (current is { LocalPath: null }) await Launcher.LaunchUriAsync(new Uri($"https://www.youtube.com/watch?v={current.Id}"));
        else ShowMessage("This is a local file. Use the native transport controls to play it.");
    }
    private void BackToVideos(object sender, RoutedEventArgs args) => ShowPanel("browse");
    private void SavePosition()
    {
        if (closed || !preferences.RememberPosition || loadedVideoId is null || source is null) return;
        if (media.PlaybackSession.PlaybackState is MediaPlaybackState.None or MediaPlaybackState.Opening) return;
        try { history.Save(loadedVideoId, media.PlaybackSession.Position.TotalSeconds, media.PlaybackSession.NaturalDuration.TotalSeconds); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Playback remains usable if history cannot be written. */ }
    }
    private async void PrepareVideo(object sender, PointerRoutedEventArgs args)
    {
        if (!preferences.PrepareOnHover || (sender as FrameworkElement)?.DataContext is not Video { LocalPath: null } video) return;
        await Prepare(video, 150);
    }
    private async Task PrepareFollowingVideo()
    {
        var index = queue.FindIndex(v => v.Id == current?.Id);
        if (index >= 0 && index + 1 < queue.Count && queue[index + 1].LocalPath is null) await Prepare(queue[index + 1], 1500);
    }
    private async Task Prepare(Video video, int delay)
    {
        preparation?.Cancel();
        var active = preparation = new CancellationTokenSource();
        preparingId = video.Id;
        try
        {
            await Task.Delay(delay, active.Token);
            await YouTubeService.ResolveStream(video.Id, active.Token);
        }
        catch (Exception) { /* Preparation is optional; opening a video reports actual failures. */ }
        finally { if (ReferenceEquals(preparation, active)) { preparation = null; preparingId = null; } active.Dispose(); }
    }
    private async void CancelPreparation(object sender, PointerRoutedEventArgs args)
    {
        // Allow a click to join extraction before releasing the hover waiter.
        var leaving = preparation;
        await Task.Delay(350);
        if (ReferenceEquals(preparation, leaving) && current?.Id != preparingId) leaving?.Cancel();
    }
    private void UpdateQueueButtons()
    {
        var index = queue.FindIndex(v => v.Id == current?.Id);
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index >= 0 && index < queue.Count - 1;
        ShortNavigation.Visibility = current?.IsShort == true ? Visibility.Visible : Visibility.Collapsed;
        PreviousShortButton.IsEnabled = index > 0;
        NextShortButton.IsEnabled = index >= 0 && index < queue.Count - 1;
        ShortPosition.Text = index >= 0 ? $"{index + 1} / {queue.Count}" : "Shorts";
        QueueView.ItemsSource = queue.ToList();
        QueueView.SelectedItem = index >= 0 ? queue[index] : null;
    }
    private async Task MoveQueue(int direction, bool autoPlay = false)
    {
        var index = queue.FindIndex(v => v.Id == current?.Id);
        if (index < 0 || index + direction < 0 || index + direction >= queue.Count) return;
        await Play(queue[index + direction], startPlaying: autoPlay);
    }
    private async void PreviousVideo(object sender, RoutedEventArgs args) => await MoveQueue(-1);
    private async void NextVideo(object sender, RoutedEventArgs args) => await MoveQueue(1);
    private void Seek(double seconds)
    {
        if (source is null || current?.Id != loadedVideoId || !media.PlaybackSession.CanSeek) return;
        media.PlaybackSession.Position = TimeSpan.FromSeconds(PlaybackMath.Seek(media.PlaybackSession.Position.TotalSeconds, seconds, media.PlaybackSession.NaturalDuration.TotalSeconds));
    }
    private void SeekBackward(object sender, RoutedEventArgs args) => Seek(-10);
    private void SeekForward(object sender, RoutedEventArgs args) => Seek(10);
    private async void RetryPlayback(object sender, RoutedEventArgs args)
    {
        if (current is null) return;
        recoveryAttempts = 0;
        await RecoverPlayback();
    }
    private async Task RecoverPlayback()
    {
        if (current is null || recoveringPlayback) return;
        var video = current;
        recoveryAttempts++;
        recoveringPlayback = true;
        recoverySeek = media.PlaybackSession.Position.TotalSeconds;
        recoveryPlay = media.PlaybackSession.PlaybackState == MediaPlaybackState.Paused ? false : playWhenOpened;
        ShowMessage("Refreshing the video connection…");
        try { await Play(video, refresh: true, recovery: true); }
        finally { recoveringPlayback = false; }
    }
    private void ToggleRepeat(object sender, RoutedEventArgs args) => LoopChoice.IsOn = RepeatButton.IsChecked == true;
    private async void ShowShortcuts(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Playback shortcuts", CloseButtonText = "Close",
            Content = new TextBlock { Text = "Space / K: Play or pause\nJ / L: Back or forward 10 seconds\nM: Mute or unmute\nF or double-click video: Fullscreen\nI: Picture in picture\nEscape: Return to app\nP / N: Previous or next video\nUp / Down: Previous / next Short\n\nShortcuts are inactive while typing or adjusting controls.", TextWrapping = TextWrapping.Wrap }
        };
        await dialog.ShowAsync();
    }
    private async void PlayerKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if ((isFullscreen || isPictureInPicture) && args.Key == VirtualKey.Escape) { ReturnToWindow(sender, args); args.Handled = true; return; }
        if (args.Handled || PlayerPanel.Visibility != Visibility.Visible) return;
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or PasswordBox or AutoSuggestBox or ComboBox or Slider or ToggleSwitch or ContentDialog) return;
            if (args.Key == VirtualKey.Space && node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return;
        }
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        if ((ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0 || (alt & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) return;
        switch (args.Key)
        {
            case VirtualKey.Space:
            case VirtualKey.K:
                TogglePlayPause(sender, args);
                break;
            case VirtualKey.J: Seek(-10); break;
            case VirtualKey.L: Seek(10); break;
            case VirtualKey.M: media.IsMuted = !media.IsMuted; break;
            case VirtualKey.F: SetFullscreen(!isFullscreen); break;
            case VirtualKey.I: SetPictureInPicture(!isPictureInPicture); break;
            case VirtualKey.Add when isPictureInPicture: ChangePictureInPictureSize(0.25); break;
            case VirtualKey.Subtract when isPictureInPicture: ChangePictureInPictureSize(-0.25); break;
            case VirtualKey.Up when current?.IsShort == true: args.Handled = true; await MoveQueue(-1); return;
            case VirtualKey.Down when current?.IsShort == true: args.Handled = true; await MoveQueue(1); return;
            case VirtualKey.Escape: SetFullscreen(false); break;
            case VirtualKey.P: args.Handled = true; await MoveQueue(-1); return;
            case VirtualKey.N: args.Handled = true; await MoveQueue(1); return;
            default: return;
        }
        args.Handled = true;
        ShowPlayerControls();
    }
    private void PopulateSettings()
    {
        applyingSettings = true;
        ThemeChoice.SelectedItem = preferences.Theme;
        AccentChoice.SelectedItem = preferences.Accent;
        CompactChoice.IsOn = preferences.CompactCards;
        AutoplayChoice.IsOn = preferences.AutoPlay;
        LoopChoice.IsOn = preferences.Loop;
        VolumeChoice.Value = preferences.Volume * 100;
        SpeedChoice.SelectedIndex = Array.IndexOf(new[] { 0.5, 0.75, 1, 1.25, 1.5, 2 }, preferences.Speed);
        StartupChoice.IsOn = preferences.StartWithSaved;
        ResumeChoice.IsOn = preferences.RememberPosition;
        NextChoice.IsOn = preferences.AutoPlayNext;
        ShortsNextChoice.IsOn = preferences.ShortsAutoNext;
        QueueAutoplay.IsOn = preferences.AutoPlayNext;
        PrepareChoice.IsOn = preferences.PrepareOnHover;
        RepeatButton.IsChecked = preferences.Loop;
        applyingSettings = false;
    }
    private void ApplyPreferences()
    {
        Root.RequestedTheme = preferences.Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        var color = preferences.Accent switch
        {
            "Purple" => Windows.UI.Color.FromArgb(255, 137, 91, 221),
            "Green" => Windows.UI.Color.FromArgb(255, 25, 145, 103),
            "Orange" => Windows.UI.Color.FromArgb(255, 221, 111, 31),
            "Rose" => Windows.UI.Color.FromArgb(255, 204, 74, 117),
            _ => Windows.UI.Color.FromArgb(255, 0, 120, 212)
        };
        Root.Resources["SystemAccentColor"] = color;
        Root.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(color);
        UpdateTitleBar();
        media.Volume = preferences.Volume;
        media.IsLoopingEnabled = preferences.Loop;
        media.PlaybackSession.PlaybackRate = preferences.Speed;
        ResizeCards(Videos);
    }
    private void SettingsChanged(object sender, RoutedEventArgs args)
    {
        if (!ready || applyingSettings) return;
        preferences.Theme = ThemeChoice.SelectedItem as string ?? "System";
        preferences.Accent = AccentChoice.SelectedItem as string ?? "Blue";
        preferences.CompactCards = CompactChoice.IsOn;
        preferences.AutoPlay = AutoplayChoice.IsOn;
        preferences.Loop = LoopChoice.IsOn;
        preferences.Volume = VolumeChoice.Value / 100;
        preferences.Speed = new[] { 0.5, 0.75, 1, 1.25, 1.5, 2 }[Math.Clamp(SpeedChoice.SelectedIndex, 0, 5)];
        preferences.StartWithSaved = StartupChoice.IsOn;
        preferences.RememberPosition = ResumeChoice.IsOn;
        preferences.AutoPlayNext = NextChoice.IsOn;
        preferences.ShortsAutoNext = ShortsNextChoice.IsOn;
        QueueAutoplay.IsOn = preferences.AutoPlayNext;
        preferences.PrepareOnHover = PrepareChoice.IsOn;
        RepeatButton.IsChecked = preferences.Loop;
        if (!preferences.PrepareOnHover) preparation?.Cancel();
        ApplyPreferences();
        try { preferences.Save(); ShowMessage("Preferences saved."); }
        catch (IOException) { ShowMessage("Could not save preferences. Check disk access.", true); }
    }
    private void ResetSettings(object sender, RoutedEventArgs args)
    {
        preferences = new Preferences();
        PopulateSettings();
        ApplyPreferences();
        try { preferences.Save(); ShowMessage("Preferences restored to defaults."); }
        catch (IOException) { ShowMessage("Could not save preferences.", true); }
    }
    private void VideoCardLoaded(object sender, RoutedEventArgs args) => ResizeCards((DependencyObject)sender);
    private void UpdateTitleBar()
    {
        if (closed) return;
        var titleBar = AppWindow.TitleBar;
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        AppTitleBar.Padding = new Thickness(Math.Max(16, titleBar.LeftInset / scale), 0, titleBar.RightInset / scale, 0);
        FullscreenHeading.Padding = isPictureInPicture ? new Thickness(6, 2, Math.Max(6, titleBar.RightInset / scale), 2) : new Thickness(16);
        ResizePictureInPicture();
        titleBar.ButtonForegroundColor = isPictureInPicture || Root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonInactiveForegroundColor = isPictureInPicture || Root.ActualTheme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(153, 255, 255, 255) : Windows.UI.Color.FromArgb(153, 0, 0, 0);
    }
    private void ResizeCards(DependencyObject node)
    {
        if (node is Border { Name: "VideoCard" } card)
        {
            var isShort = card.DataContext is Video { IsShort: true };
            card.Width = isShort ? (preferences.CompactCards ? 170 : 210) : preferences.CompactCards ? 230 : 290;
            if (card.Child is StackPanel stack && stack.Children[0] is Grid thumbnail) thumbnail.Height = isShort ? card.Width * 16 / 9 : card.Width * 9 / 16;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) ResizeCards(VisualTreeHelper.GetChild(node, i));
    }
}
