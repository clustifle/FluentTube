using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;
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
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        Root.ActualThemeChanged += (_, _) => UpdateCaptionTheme();
        Root.SizeChanged += (_, e) =>
        {
            HeroArt.Visibility = e.NewSize.Width >= 1100 ? Visibility.Visible : Visibility.Collapsed;
            ToolbarBadge.Visibility = e.NewSize.Width >= 1100 ? Visibility.Visible : Visibility.Collapsed;
            ContentLayout.Padding = new Thickness(e.NewSize.Width < 800 ? 16 : 28, 8, e.NewSize.Width < 800 ? 16 : 28, 20);
        };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 840));
        Player.SetMediaPlayer(media);
        media.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed) return;
            media.PlaybackSession.PlaybackRate = preferences.Speed;
            ShowMessage("Native player ready • " + VideoTitle.Text);
            if (preferences.AutoPlay && PlayerPanel.Visibility == Visibility.Visible) media.Play();
        });
        media.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!closed) ShowMessage("Windows could not play this source. The link may have expired, access may be blocked, or a codec may be unavailable. Reopen the video to retry, or import a supported local file.", true);
        });
        Root.Loaded += Initialize;
        Closed += (_, _) => { closed = true; request?.Cancel(); media.Dispose(); source?.Dispose(); };
    }
    private async void Initialize(object sender, RoutedEventArgs args)
    {
        if (ready) return;
        saved = LibraryStore.LoadVideos();
        PopulateSettings();
        ready = true;
        ApplyPreferences();
        ShowMessage("Search YouTube, paste a video link, or import a local video. No account needed.");
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
        Status.IsOpen = true;
    }
    private void ShowPanel(string panel)
    {
        if (panel != "player") { playbackGeneration++; media.Pause(); }
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
            WelcomePanel.Visibility = Visibility.Collapsed;
            TopicsPanel.Visibility = Visibility.Collapsed;
            PageTitle.Text = "Watch later";
            Videos.ItemsSource = saved.ToList();
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
        PageTitle.Text = query is null ? "Discover" : $"Results for “{query}”";
        WelcomePanel.Visibility = string.IsNullOrWhiteSpace(query) ? Visibility.Visible : Visibility.Collapsed;
        TopicsPanel.Visibility = string.IsNullOrWhiteSpace(query) ? Visibility.Visible : Visibility.Collapsed;
        request?.Cancel();
        if (string.IsNullOrWhiteSpace(query))
        {
            Videos.ItemsSource = null;
            EmptyState.Text = "Search for a topic, creator, or song above. You can also paste a YouTube link or choose Import video.";
            EmptyState.Visibility = Visibility.Visible;
            EmptyCard.Visibility = Visibility.Visible;
            ShowMessage("Search is ready. No API key or billing setup needed.");
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
            Videos.ItemsSource = videos;
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
    private async void ExploreTopic(object sender, RoutedEventArgs args)
    {
        var topic = (sender as Button)?.Tag as string;
        if (topic is null) return;
        Search.Text = topic;
        await LoadVideos(topic);
    }
    private void SetSource(MediaSource next)
    {
        media.Pause();
        media.Source = null;
        source?.Dispose();
        source = next;
        media.Source = next;
    }
    private async Task Play(Video video)
    {
        if (video.LocalPath is not null) { await PlayLocal(video.LocalPath); return; }
        if (YouTubeService.ParseVideoId(video.Id) != video.Id) { ShowMessage("This saved video link is invalid.", true); return; }
        request?.Cancel();
        var active = request = new CancellationTokenSource();
        var generation = ++playbackGeneration;
        current = video;
        VideoTitle.Text = video.Title;
        ShowPanel("player");
        media.Pause();
        ShowMessage("Preparing native playback…");
        try
        {
            var stream = await YouTubeService.ResolveStream(video.Id, active.Token);
            if (closed || active.IsCancellationRequested || generation != playbackGeneration) return;
            current = video with { Title = stream.Title };
            VideoTitle.Text = $"{stream.Title} • {stream.Quality}";
            SetSource(MediaSource.CreateFromUri(stream.Url));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!active.IsCancellationRequested) ShowMessage(e is InvalidOperationException ? e.Message : "Native playback could not be prepared. Try again or import a local video.", true); }
        finally { if (ReferenceEquals(request, active)) request = null; active.Dispose(); }
    }
    private async Task PlayLocal(string path)
    {
        request?.Cancel();
        var generation = ++playbackGeneration;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            if (closed || generation != playbackGeneration) return;
            current = new Video("local:" + file.Path, file.DisplayName, "Local video", file.Path);
            VideoTitle.Text = file.DisplayName;
            ShowPanel("player");
            SetSource(MediaSource.CreateFromStorageFile(file));
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
        ((SolidColorBrush)Root.Resources["BrandBrush"]).Color = color;
        Search.BorderBrush = new SolidColorBrush(color);
        UpdateCaptionTheme();
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
    private void ResizeCards(DependencyObject node)
    {
        if (node is Border { Name: "VideoCard" } card)
        {
            card.Width = preferences.CompactCards ? 230 : 290;
            if (card.Child is StackPanel stack && stack.Children[0] is Grid thumbnail) thumbnail.Height = card.Width * 9 / 16;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) ResizeCards(VisualTreeHelper.GetChild(node, i));
    }
    private void UpdateCaptionTheme()
    {
        AppWindow.TitleBar.ButtonForegroundColor = Root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
    }
}
