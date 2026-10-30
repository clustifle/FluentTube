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
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.Changed += (_, _) => UpdateTitleBar();
        Root.ActualThemeChanged += (_, _) => UpdateTitleBar();
        Activated += (_, e) => TitleBarContent.Opacity = e.WindowActivationState == WindowActivationState.Deactivated ? 0.65 : 1;
        Root.SizeChanged += (_, e) =>
        {
            ContentLayout.Padding = new Thickness(e.NewSize.Width < 800 ? 16 : 24, 16, e.NewSize.Width < 800 ? 16 : 24, 16);
        };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 840));
        Player.SetMediaPlayer(media);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(PlayerKeyDown), true);
        historyTimer.Tick += (_, _) => SavePosition();
        historyTimer.Start();
        media.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed) return;
            media.PlaybackSession.PlaybackRate = preferences.Speed;
            LoadingPlayback.Visibility = Visibility.Collapsed;
            if (preferences.RememberPosition && loadedVideoId is not null)
            {
                var position = history.Get(loadedVideoId, media.PlaybackSession.NaturalDuration.TotalSeconds);
                if (position > 0 && media.PlaybackSession.CanSeek) media.PlaybackSession.Position = TimeSpan.FromSeconds(position);
            }
            ShowMessage($"Ready in {openingTime.Elapsed.TotalSeconds:F1} s • {VideoTitle.Text}");
            if (playWhenOpened && PlayerPanel.Visibility == Visibility.Visible) media.Play();
            if (preferences.PrepareOnHover && preferences.AutoPlayNext) _ = PrepareFollowingVideo();
        });
        media.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed) return;
            LoadingPlayback.Visibility = Visibility.Collapsed;
            if (current is { LocalPath: null }) YouTubeService.InvalidateStream(current.Id);
            ShowMessage("Windows could not play this source. Choose Retry playback to get a fresh stream, or open a supported local file.", true);
        });
        media.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (closed) return;
            if (preferences.RememberPosition && loadedVideoId is not null)
            {
                try { history.Save(loadedVideoId, 0, media.PlaybackSession.NaturalDuration.TotalSeconds); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            if (preferences.AutoPlayNext && !preferences.Loop && PlayerPanel.Visibility == Visibility.Visible) await MoveQueue(1, autoPlay: true);
        });
        Root.Loaded += Initialize;
        Closed += (_, _) => { SavePosition(); closed = true; historyTimer.Stop(); preparation?.Cancel(); request?.Cancel(); media.Dispose(); source?.Dispose(); };
    }
    private async void Initialize(object sender, RoutedEventArgs args)
    {
        if (ready) return;
        if (Root.XamlRoot is not null) Root.XamlRoot.Changed += (_, _) => UpdateTitleBar();
        saved = LibraryStore.LoadVideos();
        PopulateSettings();
        ready = true;
        ApplyPreferences();
        ShowMessage("Ready");
        var link = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        var id = link is null ? null : YouTubeService.ParseVideoId(link);
        if (id is not null) await Play(new Video(id, "YouTube video", "YouTube"));
        else if (link is not null && File.Exists(link)) await PlayLocal(link);
        else if (preferences.StartWithSaved) Navigation.SelectedItem = Navigation.MenuItems[1];
    }
    private void ShowMessage(string message, bool error = false)
    {
        if (closed) return;
        Status.Title = error ? "Something needs attention" : "FluentTube";
        Status.Message = message;
        Status.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        Status.IsOpen = error;
        StatusText.Text = message;
        StatusText.Visibility = error ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ShowPanel(string panel)
    {
        if (panel != "player") { playbackGeneration++; SavePosition(); media.Pause(); Player.IsFullWindow = false; }
        BrowsePanel.Visibility = panel == "browse" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PlayerPanel.Visibility = panel == "player" ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!ready || syncingNavigation) return;
        request?.Cancel();
        if (args.IsSettingsSelected) { ShowPanel("settings"); return; }
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag == "import") { await PickVideo(); return; }
        ShowPanel("browse");
        if (tag == "saved")
        {
            PageTitle.Text = "Watch later";
            queue = saved.ToList();
            Videos.ItemsSource = queue;
            EmptyState.Text = "Your saved videos will appear here. Open a video and choose Save for later.";
            EmptyState.Visibility = saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyCard.Visibility = EmptyState.Visibility;
        }
        else await LoadVideos(null);
    }
    private async Task LoadVideos(string? query)
    {
        syncingNavigation = true;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        syncingNavigation = false;
        ShowPanel("browse");
        PageTitle.Text = query is null ? "Home" : $"Results for “{query}”";
        request?.Cancel();
        if (string.IsNullOrWhiteSpace(query))
        {
            Videos.ItemsSource = null;
            EmptyState.Text = "Search for a video above, or choose Open file to play a video from your device.";
            EmptyState.Visibility = Visibility.Visible;
            EmptyCard.Visibility = Visibility.Visible;
            ShowMessage("Ready");
            return;
        }
        var active = request = new CancellationTokenSource();
        Search.IsEnabled = false;
        Videos.ItemsSource = null;
        EmptyCard.Visibility = Visibility.Collapsed;
        ShowMessage("Loading videos…");
        try
        {
            var videos = await YouTubeService.Fetch(query, active.Token);
            if (active.IsCancellationRequested || closed) return;
            queue = videos;
            Videos.ItemsSource = queue;
            EmptyState.Text = "No videos found. Try another search.";
            EmptyState.Visibility = videos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyCard.Visibility = EmptyState.Visibility;
            ShowMessage($"{videos.Count} videos • YouTube search");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!active.IsCancellationRequested)
            {
                EmptyState.Text = "We couldn't load these videos. Try another search or import a local video.";
                EmptyState.Visibility = Visibility.Visible;
                EmptyCard.Visibility = Visibility.Visible;
                ShowMessage(e is InvalidOperationException ? e.Message : "Could not reach YouTube. Check your connection and try again.", true);
            }
        }
        finally { if (ReferenceEquals(active, request)) { if (!closed) Search.IsEnabled = true; request = null; } active.Dispose(); }
    }
    private async void SearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText.Trim();
        if (query.Length == 0) return;
        var id = YouTubeService.ParseVideoId(query);
        if (id is not null) await Play(new Video(id, "YouTube video", "YouTube"));
        else await LoadVideos(query);
    }
    private async void VideoClicked(object sender, ItemClickEventArgs args) => await Play((Video)args.ClickedItem);
    private void SetSource(MediaSource next, string id)
    {
        media.Pause();
        media.Source = null;
        source?.Dispose();
        source = next;
        loadedVideoId = id;
        media.Source = next;
    }
    private async Task Play(Video video, bool refresh = false, bool startPlaying = false)
    {
        if (preparingId != video.Id) preparation?.Cancel();
        if (video.LocalPath is not null) { await PlayLocal(video.LocalPath, startPlaying); return; }
        if (YouTubeService.ParseVideoId(video.Id) != video.Id) { ShowMessage("This saved video link is invalid.", true); return; }
        if (!refresh && loadedVideoId == video.Id && source is not null && media.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Paused)
        {
            request?.Cancel();
            current = video;
            ShowPanel("player");
            UpdateQueueButtons();
            LoadingPlayback.Visibility = Visibility.Collapsed;
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
        VideoTitle.Text = video.Title;
        ShowPanel("player");
        UpdateQueueButtons();
        media.Pause();
        LoadingPlayback.Visibility = Visibility.Visible;
        openingTime.Restart();
        ShowMessage("Preparing native playback…");
        try
        {
            var stream = await YouTubeService.ResolveStream(video.Id, active.Token);
            if (closed || active.IsCancellationRequested || generation != playbackGeneration) return;
            current = video with { Title = stream.Title };
            VideoTitle.Text = $"{stream.Title} • {stream.Quality}";
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
            VideoTitle.Text = file.DisplayName;
            ShowPanel("player");
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
    }
    private void SaveVideo(object sender, RoutedEventArgs args)
    {
        if (current is null) return;
        if (saved.Any(v => v.Id == current.Id)) { ShowMessage("This video is already saved."); return; }
        try { var next = saved.Append(current).ToList(); LibraryStore.SaveVideos(next); saved = next; ShowMessage("Saved to Watch later."); }
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
        await Prepare(video, 450);
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
    private void CancelPreparation(object sender, PointerRoutedEventArgs args) => preparation?.Cancel();
    private void UpdateQueueButtons()
    {
        var index = queue.FindIndex(v => v.Id == current?.Id);
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index >= 0 && index < queue.Count - 1;
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
        if (source is null || !media.PlaybackSession.CanSeek) return;
        media.PlaybackSession.Position = TimeSpan.FromSeconds(PlaybackMath.Seek(media.PlaybackSession.Position.TotalSeconds, seconds, media.PlaybackSession.NaturalDuration.TotalSeconds));
    }
    private void SeekBackward(object sender, RoutedEventArgs args) => Seek(-10);
    private void SeekForward(object sender, RoutedEventArgs args) => Seek(10);
    private async void RetryPlayback(object sender, RoutedEventArgs args)
    {
        if (current is not null) await Play(current, refresh: true);
    }
    private void ToggleRepeat(object sender, RoutedEventArgs args) => LoopChoice.IsOn = RepeatButton.IsChecked == true;
    private async void ShowShortcuts(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "Playback shortcuts", CloseButtonText = "Close",
            Content = new TextBlock { Text = "Space / K: Play or pause\nJ / L: Back or forward 10 seconds\nM: Mute or unmute\nF: Toggle full window\nEscape: Leave full window\nP / N: Previous or next video\n\nShortcuts are inactive while typing or adjusting controls.", TextWrapping = TextWrapping.Wrap }
        };
        await dialog.ShowAsync();
    }
    private async void PlayerKeyDown(object sender, KeyRoutedEventArgs args)
    {
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
                if (source is not null) { if (media.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) media.Pause(); else media.Play(); }
                break;
            case VirtualKey.J: Seek(-10); break;
            case VirtualKey.L: Seek(10); break;
            case VirtualKey.M: media.IsMuted = !media.IsMuted; break;
            case VirtualKey.F: Player.IsFullWindow = !Player.IsFullWindow; break;
            case VirtualKey.Escape: Player.IsFullWindow = false; break;
            case VirtualKey.P: args.Handled = true; await MoveQueue(-1); return;
            case VirtualKey.N: args.Handled = true; await MoveQueue(1); return;
            default: return;
        }
        args.Handled = true;
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
        titleBar.ButtonForegroundColor = Root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonInactiveForegroundColor = Root.ActualTheme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(153, 255, 255, 255) : Windows.UI.Color.FromArgb(153, 0, 0, 0);
    }
    private void ResizeCards(DependencyObject node)
    {
        if (node is Border { Name: "VideoCard" } card)
        {
            card.Width = preferences.CompactCards ? 230 : 290;
            if (card.Child is StackPanel stack && stack.Children[0] is Grid thumbnail) thumbnail.Height = card.Width * 9 / 16;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) ResizeCards(VisualTreeHelper.GetChild(node, i));
    }
}
