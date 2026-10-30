using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System.IO;
using Windows.System;
namespace FluentTube;
public sealed partial class MainWindow : Window
{
    private Video? current;
    private List<Video> saved = [];
    private CancellationTokenSource? request;
    private bool ready;
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 840));
        Root.Loaded += Initialize;
        Closed += (_, _) => { request?.Cancel(); Player.Close(); };
    }
    private async void Initialize(object sender, RoutedEventArgs args)
    {
        saved = LibraryStore.LoadVideos();
        ready = true;
        ShowMessage("Search YouTube or paste a video link. No API key or Google Cloud account needed.");
        var link = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        var id = link is null ? null : YouTubeService.ParseVideoId(link);
        if (id is not null) await Play(new Video(id, "YouTube video", "YouTube"));
    }
    private void ShowMessage(string message, bool error = false)
    {
        Status.Title = error ? "Something needs attention" : "FluentTube";
        Status.Message = message;
        Status.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        Status.IsOpen = true;
    }
    private void ShowPanel(string panel)
    {
        // Stop audio when leaving the player.
        if (panel != "player" && Player.CoreWebView2 is not null) Player.CoreWebView2.Navigate("about:blank");
        BrowsePanel.Visibility = panel == "browse" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PlayerPanel.Visibility = panel == "player" ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!ready) return;
        request?.Cancel();
        if (args.IsSettingsSelected) { ShowPanel("settings"); return; }
        ShowPanel("browse");
        if ((args.SelectedItem as NavigationViewItem)?.Tag as string == "saved")
        {
            PageTitle.Text = "Watch later";
            Videos.ItemsSource = saved.ToList();
            EmptyState.Text = "Your saved videos will appear here. Open a video and choose Save for later.";
            EmptyState.Visibility = saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else await LoadVideos(null);
    }
    private async Task LoadVideos(string? query)
    {
        ShowPanel("browse");
        PageTitle.Text = query is null ? "Discover" : $"Results for “{query}”";
        if (string.IsNullOrWhiteSpace(query))
        {
            Videos.ItemsSource = null;
            EmptyState.Text = "Search for a topic, creator, or song above. You can also paste a YouTube video link.";
            EmptyState.Visibility = Visibility.Visible;
            ShowMessage("Search is ready. No API key or billing setup needed.");
            return;
        }
        request?.Cancel();
        var active = request = new CancellationTokenSource();
        Search.IsEnabled = false;
        ShowMessage("Loading videos…");
        try
        {
            var videos = await YouTubeService.Fetch(query, active.Token);
            if (active.IsCancellationRequested) return;
            Videos.ItemsSource = videos;
            EmptyState.Text = "No videos found. Try another search.";
            EmptyState.Visibility = videos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowMessage($"{videos.Count} videos • Powered by YouTube");
        }
        catch (OperationCanceledException) { if (!active.IsCancellationRequested) ShowMessage("The request timed out. Try again.", true); }
        catch (Exception e) { ShowMessage(e is InvalidOperationException ? e.Message : "Could not reach YouTube. Check your connection and try again.", true); }
        finally { if (ReferenceEquals(active, request)) { Search.IsEnabled = true; request = null; } active.Dispose(); }
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
    private async Task Play(Video video)
    {
        if (YouTubeService.ParseVideoId(video.Id) != video.Id) { ShowMessage("This saved video link is invalid.", true); return; }
        request?.Cancel();
        current = video;
        VideoTitle.Text = video.Title;
        ShowPanel("player");
        try
        {
            if (Player.CoreWebView2 is null)
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(LibraryStore.Folder, "WebView2"), null);
                await Player.EnsureCoreWebView2Async(environment);
                Player.CoreWebView2!.SetVirtualHostNameToFolderMapping("clustifle.github.io", Path.Combine(AppContext.BaseDirectory, "Assets", "Player"), CoreWebView2HostResourceAccessKind.DenyCors);
                Player.CoreWebView2.WebMessageReceived += (_, e) =>
                {
                    if (!e.Source.StartsWith("https://clustifle.github.io/index.html", StringComparison.Ordinal)) return;
                    try
                    {
                        using var message = System.Text.Json.JsonDocument.Parse(e.TryGetWebMessageAsString());
                        var kind = message.RootElement.GetProperty("kind").GetString();
                        if (kind == "ready") ShowMessage("YouTube player ready. Press Play to watch.");
                        else if (kind == "playing") ShowMessage("Playing on YouTube");
                        else if (kind == "error")
                        {
                            var code = message.RootElement.GetProperty("code").GetInt32();
                            ShowMessage(code == 153 ? "YouTube could not verify the player identity (153). Choose Open on YouTube to continue."
                                : $"YouTube could not play this video (error {code}). It may restrict embedding. Choose Open on YouTube.", true);
                        }
                    }
                    catch (Exception) { /* Ignore messages outside the player status protocol. */ }
                };
                Player.CoreWebView2!.NewWindowRequested += async (_, e) =>
                {
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") await Launcher.LaunchUriAsync(uri);
                };
            }
            // Load a real enclosing HTTPS document so iframe and player requests retain
            // a consistent browser-generated Referer and origin (YouTube error 153).
            Player.CoreWebView2.Navigate($"https://clustifle.github.io/index.html?v={video.Id}");
            ShowMessage("YouTube player • If this video cannot be embedded, choose Open on YouTube.");
        }
        catch (Exception) { ShowMessage("Could not initialize playback. Install Microsoft Edge WebView2 Runtime, or choose Open on YouTube.", true); }
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
        if (current is not null) await Launcher.LaunchUriAsync(new Uri($"https://www.youtube.com/watch?v={current.Id}"));
    }
    private void BackToVideos(object sender, RoutedEventArgs args) => ShowPanel("browse");
}
