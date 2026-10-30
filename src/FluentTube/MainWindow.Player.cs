using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Windows.Media.Core;
using Windows.Media.Playback;
namespace FluentTube;

internal sealed record PlaybackQualityChoice(string Label, int Height)
{
    public override string ToString() => Label;
}

public sealed partial class MainWindow
{
    private readonly DispatcherTimer playerTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer seekTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool syncingPlayer, seeking, holdingPlayer, playerMenuOpen, isFullscreen;
    private bool controlsVisible = true, pointerInVideo, isPictureInPicture;
    private AppWindowPresenter? pipPreviousPresenter;
    private Windows.Graphics.RectInt32 pipPreviousBounds;
    private double videoRatio = 16d / 9;
    private readonly Windows.UI.ViewManagement.UISettings motionSettings = new();
    private DateTimeOffset lastPlayerInput = DateTimeOffset.UtcNow;
    private AppWindowPresenter? previousPresenter;
    private CancellationTokenSource? qualityRequest;
    private AdaptivePlayback? adaptivePlayback;
    private YouTubeService.StreamInfo? availableStream;
    private double? qualitySeek;
    private bool? qualityPlay;
    private string? qualityLookupId;
    private readonly Windows.System.Display.DisplayRequest displayRequest = new();
    private bool keepingDisplayAwake;

    private void InitializeUnifiedPlayer()
    {
        media.PlaybackSession.NaturalVideoSizeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!closed) UpdateNaturalVideoRatio(); });
        playerTimer.Tick += (_, _) => RefreshPlayerControls();
        playerTimer.Start();
        seekTimer.Tick += (_, _) =>
        {
            seekTimer.Stop();
            if (source is not null && media.PlaybackSession.CanSeek) media.PlaybackSession.Position = TimeSpan.FromSeconds(PlayerTimeline.Value);
            seeking = false;
        };
        PlayerQuality.ItemsSource = new[] { new PlaybackQualityChoice("Auto", 0) };
        PlayerQuality.SelectedIndex = 0;
    }
    private void ResetPlayerSurface(Video video)
    {
        qualityRequest?.Cancel();
        availableStream = null; qualityLookupId = null; qualitySeek = null; qualityPlay = null;
        seeking = false; seekTimer.Stop();
        syncingPlayer = true;
        PlayerQuality.ItemsSource = new[] { new PlaybackQualityChoice(video.LocalPath is null ? "Auto" : "Original", 0) };
        PlayerQuality.SelectedIndex = 0;
        PlayerQuality.IsEnabled = false;
        syncingPlayer = false;
        VideoPoster.Source = video.LocalPath is null ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(video.Thumbnail)) : null;
        VideoPoster.Visibility = Visibility.Visible;
        videoRatio = 16d / 9;
        UpdateVideoSize();
        SetControlsVisible(false);
    }
    private void OnUnifiedMediaOpened()
    {
        VideoPoster.Visibility = Visibility.Collapsed;
        UpdateNaturalVideoRatio();
        var position = qualitySeek ?? (preferences.RememberPosition && loadedVideoId is not null ? history.Get(loadedVideoId, media.PlaybackSession.NaturalDuration.TotalSeconds) : 0);
        if (position > 0 && media.PlaybackSession.CanSeek) media.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Min(position, media.PlaybackSession.NaturalDuration.TotalSeconds));
        var start = qualityPlay ?? playWhenOpened;
        qualitySeek = null; qualityPlay = null;
        if (start && PlayerPanel.Visibility == Visibility.Visible) media.Play();
        if (current is { LocalPath: null } && qualityLookupId != current.Id) _ = LoadVideoQualities(current.Id, playbackGeneration);
        RefreshPlayerControls();
    }
    private async Task LoadVideoQualities(string id, int generation)
    {
        qualityRequest?.Cancel();
        var active = qualityRequest = new CancellationTokenSource();
        qualityLookupId = id;
        try
        {
            var stream = await YouTubeService.ResolveQualities(id, active.Token);
            if (closed || active.IsCancellationRequested || current?.Id != id || generation != playbackGeneration) return;
            availableStream = stream;
            var choices = new List<PlaybackQualityChoice> { new("Auto", 0) };
            var progressive = int.TryParse(stream.Quality.TrimEnd('p'), out var height) ? height : 0;
            choices.AddRange((stream.Variants ?? []).Select(v => v.Height).Append(progressive).Where(h => h > 0 && h <= 1080).Distinct().Order().Select(h => new PlaybackQualityChoice($"{h}p", h)));
            syncingPlayer = true;
            PlayerQuality.ItemsSource = choices;
            PlayerQuality.SelectedItem = choices.FirstOrDefault(q => q.Height == preferences.QualityHeight) ?? choices[0];
            PlayerQuality.IsEnabled = choices.Count > 1;
            syncingPlayer = false;
            if ((stream.Variants?.Count ?? 0) > 0) await ChangeQuality(preferences.QualityHeight, active.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Quality discovery is optional: keep the already playing source.
            if (!closed && !active.IsCancellationRequested) ShowMessage("Playing at the available quality. Additional resolutions could not be loaded; retry playback to try again.");
        }
        finally { if (ReferenceEquals(qualityRequest, active)) qualityRequest = null; active.Dispose(); }
    }
    private async Task ChangeQuality(int height, CancellationToken cancellation)
    {
        if (availableStream is null || current is null) return;
        var id = current.Id;
        var generation = playbackGeneration;
        var variants = (availableStream.Variants ?? []).Where(v => height == 0 || v.Height == height).ToList();
        AdaptivePlayback? prepared = null;
        try
        {
            LoadingPlayback.Visibility = Visibility.Visible;
            if (variants.Count > 0) prepared = await AdaptivePlayback.Create(variants);
            if (closed || cancellation.IsCancellationRequested || current?.Id != id || playbackGeneration != generation) { prepared?.Dispose(); return; }
            qualitySeek = media.PlaybackSession.Position.TotalSeconds;
            qualityPlay = media.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            openingTime.Restart();
            if (prepared is not null)
            {
                SetSource(MediaSource.CreateFromAdaptiveMediaSource(prepared.Source), id, prepared);
                prepared = null;
            }
            else SetSource(MediaSource.CreateFromUri(availableStream.Url), id);
        }
        catch (Exception) { prepared?.Dispose(); if (!closed && !cancellation.IsCancellationRequested) ShowMessage("This quality could not be opened. The current stream remains available; try a lower resolution.", true); }
        finally { if (!closed) LoadingPlayback.Visibility = Visibility.Collapsed; }
    }
    private async void PlayerQualityChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || syncingPlayer || PlayerQuality.SelectedItem is not PlaybackQualityChoice choice) return;
        preferences.QualityHeight = choice.Height;
        try { preferences.Save(); } catch (System.IO.IOException) { }
        qualityRequest?.Cancel();
        var active = qualityRequest = new CancellationTokenSource();
        try { await ChangeQuality(choice.Height, active.Token); }
        finally { if (ReferenceEquals(qualityRequest, active)) qualityRequest = null; active.Dispose(); }
    }
    private void RefreshPlayerControls()
    {
        if (closed) return;
        if (PlayerPanel.Visibility != Visibility.Visible && !isFullscreen)
        {
            if (keepingDisplayAwake) { try { displayRequest.RequestRelease(); keepingDisplayAwake = false; } catch (Exception) { } }
            return;
        }
        var session = media.PlaybackSession;
        var playing = session.PlaybackState == MediaPlaybackState.Playing;
        var loadedCurrent = source is not null && current?.Id == loadedVideoId;
        PlayPauseButton.Content = new SymbolIcon(playing ? Symbol.Pause : Symbol.Play);
        PlayPauseButton.IsEnabled = loadedCurrent;
        PreviousButton.Visibility = NextButton.Visibility = VideoSurface.ActualWidth < 560 ? Visibility.Collapsed : Visibility.Visible;
        PlayerSpeed.Visibility = isPictureInPicture || VideoSurface.ActualWidth < 450 ? Visibility.Collapsed : Visibility.Visible;
        PlayerQuality.Visibility = isPictureInPicture ? Visibility.Collapsed : Visibility.Visible;
        SeekBackwardButton.Visibility = SeekForwardButton.Visibility = isPictureInPicture ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(PipButton, isPictureInPicture ? "Return to app (I / Esc)" : "Picture in picture (I)");
        SoundButton.Content = new SymbolIcon(media.IsMuted || media.Volume == 0 ? Symbol.Mute : Symbol.Volume);
        var duration = session.NaturalDuration.TotalSeconds;
        syncingPlayer = true;
        if (!seeking)
        {
            PlayerTimeline.Maximum = Math.Max(1, duration);
            PlayerTimeline.Value = Math.Clamp(session.Position.TotalSeconds, 0, PlayerTimeline.Maximum);
        }
        PlayerTimeline.IsEnabled = loadedCurrent && session.CanSeek && duration > 0;
        PlayerVolume.Value = media.Volume * 100;
        PlayerSpeed.SelectedIndex = Array.IndexOf(new[] { 0.5, 0.75, 1, 1.25, 1.5, 2 }, preferences.Speed);
        syncingPlayer = false;
        PlayerTime.Text = $"{PlaybackMath.Time(seeking ? PlayerTimeline.Value : session.Position.TotalSeconds)} / {PlaybackMath.Time(duration)}";
        ToolTipService.SetToolTip(PlayerQuality, $"Playing at {session.NaturalVideoWidth} × {session.NaturalVideoHeight}");
        if (!holdingPlayer && !playerMenuOpen && !seeking && DateTimeOffset.UtcNow - lastPlayerInput > TimeSpan.FromSeconds(3))
        {
            SetControlsVisible(false);
        }

        var awake = playing && PlayerPanel.Visibility == Visibility.Visible;
        if (awake != keepingDisplayAwake)
        {
            try { if (awake) displayRequest.RequestActive(); else displayRequest.RequestRelease(); keepingDisplayAwake = awake; } catch (Exception) { }
        }
    }
    private void ShowPlayerControls()
    {
        lastPlayerInput = DateTimeOffset.UtcNow;
        SetControlsVisible(true);
    }
    private void AnimatePage(FrameworkElement page)
    {
        if (!motionSettings.AnimationsEnabled) return;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(page, true);
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(page);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation("Opacity", fade);
        var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new System.Numerics.Vector3(0, 8, 0));
        slide.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        slide.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Translation", slide);
    }
    private void PlayerControlsGettingFocus(UIElement sender, GettingFocusEventArgs args) => ShowPlayerControls();
    private void SetControlsVisible(bool visible)
    {
        FullscreenHeading.Visibility = isFullscreen || isPictureInPicture ? Visibility.Visible : Visibility.Collapsed;
        PlayerControls.IsHitTestVisible = visible;
        FullscreenHeading.IsHitTestVisible = visible;
        if (controlsVisible == visible) return;
        controlsVisible = visible;
        foreach (var element in new UIElement[] { PlayerControls, FullscreenHeading })
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
            if (!motionSettings.AnimationsEnabled) { visual.StopAnimation("Opacity"); visual.Opacity = visible ? 1 : 0; continue; }
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1, visible ? 1 : 0);
            fade.Duration = TimeSpan.FromMilliseconds(visible ? 140 : 200);
            visual.StartAnimation("Opacity", fade);
        }
    }
    private void UpdateNaturalVideoRatio()
    {
        var session = media.PlaybackSession;
        if (session.NaturalVideoWidth > 0 && session.NaturalVideoHeight > 0)
            videoRatio = (double)session.NaturalVideoWidth / session.NaturalVideoHeight;
        UpdateVideoSize();
    }
    private void VideoSlotSizeChanged(object sender, SizeChangedEventArgs args) => UpdateVideoSize();
    private void UpdateVideoSize()
    {
        // Inline height depends on width, never on the description height.
        if (VideoSurfaceSlot.ActualWidth > 0) VideoSurfaceSlot.Height = VideoSurfaceSlot.ActualWidth / videoRatio;
    }
    private void RevealPlayerControls(object sender, PointerRoutedEventArgs args) { pointerInVideo = true; ShowPlayerControls(); }
    private void LeaveVideoSurface(object sender, PointerRoutedEventArgs args)
    {
        pointerInVideo = false; holdingPlayer = false;
        if (!playerMenuOpen && !seeking) SetControlsVisible(false);
    }
    private void HoldPlayerControls(object sender, PointerRoutedEventArgs args) { holdingPlayer = true; ShowPlayerControls(); }
    private void ReleasePlayerControls(object sender, PointerRoutedEventArgs args) { holdingPlayer = false; if (pointerInVideo) ShowPlayerControls(); }
    private void PlayerMenuOpened(object sender, object args) { playerMenuOpen = true; ShowPlayerControls(); }
    private void PlayerMenuClosed(object sender, object args) { playerMenuOpen = false; if (pointerInVideo) ShowPlayerControls(); else SetControlsVisible(false); }
    private void TimelineChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!ready || syncingPlayer || source is null) return;
        seeking = true; seekTimer.Stop(); seekTimer.Start(); ShowPlayerControls();
    }
    private void PlayerVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        if (!ready || syncingPlayer) return;
        media.IsMuted = false;
        VolumeChoice.Value = PlayerVolume.Value;
    }
    private void PlayerSpeedChanged(object sender, SelectionChangedEventArgs args) { if (ready && !syncingPlayer) SpeedChoice.SelectedIndex = PlayerSpeed.SelectedIndex; }
    private void TogglePlayPause(object sender, RoutedEventArgs args)
    {
        if (source is null || current?.Id != loadedVideoId) return;
        if (media.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) media.Pause(); else media.Play();
        ShowPlayerControls();
    }
    private void ToggleMute(object sender, RoutedEventArgs args) { media.IsMuted = !media.IsMuted; ShowPlayerControls(); }
    private void VideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        for (var node = args.OriginalSource as DependencyObject; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, PlayerControls) || ReferenceEquals(node, FullscreenHeading)) return;
            if (ReferenceEquals(node, VideoSurface)) { SetFullscreen(!isFullscreen); args.Handled = true; return; }
        }
    }
    private bool FallBackFromAdaptive()
    {
        if (adaptivePlayback is null || availableStream is null || current is null) return false;
        qualitySeek ??= media.PlaybackSession.Position.TotalSeconds;
        qualityPlay ??= playWhenOpened;
        syncingPlayer = true;
        PlayerQuality.SelectedIndex = 0;
        syncingPlayer = false;
        SetSource(MediaSource.CreateFromUri(availableStream.Url), current.Id);
        ShowMessage("The selected quality could not play. Continuing with the compatible stream.");
        return true;
    }
    private void TogglePictureInPicture(object sender, RoutedEventArgs args) => SetPictureInPicture(!isPictureInPicture);
    private void ReturnToWindow(object sender, RoutedEventArgs args)
    {
        if (isPictureInPicture) SetPictureInPicture(false); else SetFullscreen(false);
    }
    private void SetPictureInPicture(bool value)
    {
        if (closed || value == isPictureInPicture || value && (current is null || PlayerPanel.Visibility != Visibility.Visible)) return;
        if (value)
        {
            if (isFullscreen) SetFullscreen(false);
            pipPreviousPresenter = AppWindow.Presenter;
            pipPreviousBounds = new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            try
            {
                var compact = CompactOverlayPresenter.Create();
                compact.InitialSize = CompactOverlaySize.Medium;
                AppWindow.SetPresenter(compact);
            }
            catch (Exception) { pipPreviousPresenter = null; ShowMessage("Windows could not open picture in picture. Playback remains in this window.", true); return; }
            VideoSurfaceSlot.Children.Remove(VideoSurface);
            FullscreenHost.Children.Add(VideoSurface);
            FullscreenHost.Visibility = Visibility.Visible;
            Navigation.Visibility = AppTitleBar.Visibility = Visibility.Collapsed;
            Root.RowDefinitions[0].Height = new GridLength(0);
            isPictureInPicture = true;
            SetTitleBar(VideoPresentationTitle);
        }
        else
        {
            if (pipPreviousPresenter is not null) AppWindow.SetPresenter(pipPreviousPresenter); else AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored }) AppWindow.MoveAndResize(pipPreviousBounds);
            FullscreenHost.Children.Remove(VideoSurface);
            VideoSurfaceSlot.Children.Add(VideoSurface);
            FullscreenHost.Visibility = Visibility.Collapsed;
            Navigation.Visibility = AppTitleBar.Visibility = Visibility.Visible;
            Root.RowDefinitions[0].Height = new GridLength(32);
            isPictureInPicture = false;
            pipPreviousPresenter = null;
            SetTitleBar(AppTitleBar);
        }
        UpdateTitleBar();
        UpdateVideoSize();
        RefreshPlayerControls();
        ShowPlayerControls();
        Player.Focus(FocusState.Programmatic);
    }
    private void ToggleFullscreen(object sender, RoutedEventArgs args) => SetFullscreen(!isFullscreen);
    private void SetFullscreen(bool value)
    {
        if (closed || value == isFullscreen || value && PlayerPanel.Visibility != Visibility.Visible) return;
        if (value)
        {
            if (isPictureInPicture) SetPictureInPicture(false);
            previousPresenter = AppWindow.Presenter;
            VideoSurfaceSlot.Children.Remove(VideoSurface);
            FullscreenHost.Children.Add(VideoSurface);
            FullscreenHost.Visibility = Visibility.Visible;
            Navigation.Visibility = Visibility.Collapsed;
            AppTitleBar.Visibility = Visibility.Collapsed;
            Root.RowDefinitions[0].Height = new GridLength(0);
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            if (previousPresenter is not null) AppWindow.SetPresenter(previousPresenter); else AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            FullscreenHost.Children.Remove(VideoSurface);
            VideoSurfaceSlot.Children.Add(VideoSurface);
            FullscreenHost.Visibility = Visibility.Collapsed;
            Navigation.Visibility = Visibility.Visible;
            AppTitleBar.Visibility = Visibility.Visible;
            Root.RowDefinitions[0].Height = new GridLength(32);
            previousPresenter = null;
        }
        isFullscreen = value;
        UpdateVideoSize();
        FullscreenButton.Content = new SymbolIcon(value ? Symbol.BackToWindow : Symbol.FullScreen);
        ShowPlayerControls();
        Player.Focus(FocusState.Programmatic);
    }
    private void DisposeUnifiedPlayer()
    {
        playerTimer.Stop(); seekTimer.Stop(); qualityRequest?.Cancel();
        adaptivePlayback?.Dispose();
        if (keepingDisplayAwake) { try { displayRequest.RequestRelease(); } catch (Exception) { } }
    }
}
