using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System.IO;
using Windows.System;
namespace FluentTube;
public sealed partial class MainWindow : Window
{
    private string key = "";
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
        try { key = await LibraryStore.LoadKey(); ApiKey.Password = key; }
        catch (Exception) { ShowMessage("Could not read your saved key. Enter it again in Settings.", true); }
        ready = true;
        if (key.Length > 0) await LoadVideos(null);
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
        if (key.Length == 0) { ShowMessage("Add a YouTube Data API v3 key in Settings to search. You can open video links without a key."); return; }
        request?.Cancel();
        var active = request = new CancellationTokenSource();
        Search.IsEnabled = false;
        ShowMessage("Loading videos…");
        try
        {
            var videos = await YouTubeService.Fetch(key, query, active.Token);
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
                Player.CoreWebView2!.NewWindowRequested += async (_, e) =>
                {
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") await Launcher.LaunchUriAsync(uri);
                };
            }
            // Identify the embedding application with an HTTPS Referer, as YouTube requires.
            var message = Player.CoreWebView2.Environment.CreateWebResourceRequest(
                $"https://www.youtube.com/embed/{video.Id}?playsinline=1", "GET", null,
                "Referer: https://github.com/clustifle/FluentTube\r\n");
            Player.CoreWebView2.NavigateWithWebResourceRequest(message);
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
    private async void SaveSettings(object sender, RoutedEventArgs args)
    {
        try { await LibraryStore.SaveKey(ApiKey.Password.Trim()); key = ApiKey.Password.Trim(); ShowMessage("Settings saved. Choose Discover to load videos."); }
        catch (Exception) { ShowMessage("Could not save the key securely. Please try again.", true); }
    }
}
