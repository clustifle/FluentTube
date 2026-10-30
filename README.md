# FluentTube

A Windows 11 YouTube client built with **C#, .NET 10, and WinUI 3**. **1.0 RC1** packages Home, channels, Shorts, Takeout subscriptions, captions, fullscreen, aspect-aware PiP, and native quality up to 1080p with an original Fluent icon and portable release assets. No API key or Google account is needed. This is a public-video client with local library features, not a complete signed-in YouTube replacement.

RC1 is a release candidate. Clean-machine launch and interactive Windows validation are still required before stable 1.0. See the [release checklist](docs/RELEASE-CHECKLIST.md), [release notes](docs/releases/v1.0.0-rc.1.md), [installation guide](docs/INSTALL.md), [privacy information](docs/PRIVACY.md), and [icon preview](branding/IconPreview.png).

## Download

Open [Windows build](https://github.com/clustifle/FluentTube/actions/workflows/windows.yml), select the latest successful run, and download `FluentTube-win-x64` from Artifacts (GitHub sign-in required). Extract the **entire ZIP**, including `Tools`, and run `FluentTube.exe`.

Requires Windows 11 x64. .NET and Windows App SDK runtimes are bundled. Builds are unsigned; there is no installer yet. WebView2 and the YouTube embedded player are no longer used.

The `FluentTube-release` Actions artifact contains a versioned portable ZIP and SHA-256 checksum. RC tags prepare a draft GitHub release with those assets; drafts are visible to repository maintainers and remain unpublished until the release gates are verified.

## Watch and import

- **Takeout subscriptions:** use Import subscriptions on the Subscriptions page or in Settings → Subscriptions. Choose a Google Takeout ZIP or its extracted `subscriptions.csv`, review the channel preview, and import. Existing follows and their names are preserved; duplicates and invalid channel rows are skipped with counts. Imported channels immediately appear in the sidebar and Shorts source selector. Choose Refresh in Subscriptions to load uploads. No Google login, API key, Cloud billing, or automatic account sync is involved. Files are processed locally; ZIP files are read directly without extracting other content. Subscription CSV files must be UTF-8 and under 8 MB.
- To export, open [Google Takeout](https://takeout.google.com/), deselect other products, select YouTube and YouTube Music, and under All YouTube data included select only subscriptions. Create a one-time ZIP export, download it, and select it in FluentTube. If your account exports localized filenames, extract the archive and select the subscription CSV directly.
- **Shorts** opens actual public Shorts from a channel's Shorts tab. Choose YouTube's channel or a locally followed channel from the source selector; channel pages also offer a Shorts / Videos switch. This is channel browsing, not YouTube's personalized infinite Shorts feed. Load more retrieves the next page (up to ten pages). Shorts links pasted into Search or passed at launch retain their Shorts presentation.
- Portrait videos fit the available viewport without stretching. Shorts have Previous / Next buttons, a position indicator, and Up / Down keyboard navigation. Settings → Playback → Advance to the next Short controls automatic progression through the loaded queue. Quality labels use the shorter dimension, including vertical 1080 × 1920 playback when supported.
- **Captions** lives under More playback options. Available creator VTT tracks and original auto captions are listed after quality discovery; captions download only when selected. YouTube can restrict caption requests independently of video playback. Open subtitle file supports UTF-8 VTT and SRT files under 4 MB. Captions follow seeking and speed changes and remain on the same video surface in fullscreen and PiP. Off clears them; changing videos resets the selection. Translation generation and caption editing are not included.
- **Copy video link**, **Clear upcoming videos**, and specific network/decoder errors with a Retry video action make sharing and queue recovery easier. Clearing upcoming videos preserves the current video and earlier queue entries.
- **Home** loads real videos at startup. With local follows, All shows recent uploads from your channels; without follows, it shows exploratory search results. Topic filters let you explore separately. This is not YouTube's signed-in recommendation algorithm.
- **Channels** open from a creator link, Add channel, or a pasted `@handle`/channel URL. Browse uploads and use Load more for subsequent pages. Search results also support Load more (up to ten pages).
- **Follow** on a channel page or watch page saves a local subscription. Followed channels appear in the sidebar; Subscriptions combines their recent uploads. YouTube feeds are used where available, with channel extraction as fallback. Home can show cached uploads while refreshing. Follows do not alter or sync Google account subscriptions.
- **History** records videos once playback starts and keeps the last 200 unique videos. Clear history removes this local list after confirmation. Playback positions are stored separately.
- The watch page shows the creator, description, and queue. Use Now playing to return after browsing. Right-click a video to play, play next, save, remove from Watch later, or open its channel. Video cards show duration, views, and dates when the source provides them.
- Type a topic, creator, or song to search. No Google Cloud project, API key, billing account, or sign-in needed.
- Paste a YouTube watch, Shorts, live, or youtu.be URL to open it in the native Windows player. Opening shows the thumbnail immediately while the initial stream resolves; moving over the video reveals its controls. It starts with the compatible combined stream; background quality discovery can upgrade to native adaptive playback. First-time network lookup and buffering still take time.
- The quality menu lists resolutions actually available for the video, up to 1080p. Auto uses supported adaptive H.264 variants; manual quality selects a fixed resolution. YouTube HLS audio and video tracks are combined by Windows' adaptive player without downloading or merging the entire video. Switching preserves playback position and play/pause state. Videos without compatible adaptive formats retain the combined stream. Local files use Original quality.
- Choose **Import video** to open a local file. MP4, MKV, WebM, MOV, AVI, WMV, M4V, MP3, and M4A appear in the picker; actual decoding depends on installed Windows codecs. Files are opened in place, not copied or uploaded.
- One overlay bar inside the video provides play/pause, timeline/time display, volume/mute, speed, quality, previous/next, ten-second seeking, fullscreen, repeat, retry, queue, and shortcuts. Controls fade in on hover, movement, or keyboard focus, hide on pointer exit, and fade out after three seconds without interaction, including when paused. Menus and active control interactions keep them visible. Windows animation preferences are respected. Library actions and channel details remain below the video. The watch page scrolls; video height follows the source aspect ratio and available width, so expanding Description does not shrink the player. Pages use subtle Fluent entrance motion.
- Fullscreen uses the Windows fullscreen presenter and moves the same video surface and controls to the window root. The sidebar, title bar, page chrome, and taskbar are excluded. Exiting restores the previous presenter without replacing the media source. Use the fullscreen button, **F**, or double-click the video; **Escape** exits.
- **Picture in picture** uses Windows compact overlay to keep this same native video above other apps. Use the PiP button, More → Picture in picture, or **I**; move the mini window by its title, and use Return to app or **Escape** to restore FluentTube. Use − / + to scale PiP from 75% to 200%, within the display work area. The PiP header hides the video name and keeps a blank drag area. The window adapts to each video's aspect ratio (portrait, square, or landscape), within the current display's work area, and updates if the source ratio or display scaling changes. A dedicated 32-pixel control strip contains small play/pause and mute buttons, a timeline, and elapsed time. The small top buttons minimize PiP or return to the main app; restore a minimized window from FluentTube's taskbar icon. Minimizing keeps playback running and releases the display-awake request. Quality, speed, queue, and advanced menus remain in the main player. Playback, volume, position, and quality are retained because no new media source is opened.
- Playback shortcuts: Space/K play or pause, J/L seek ten seconds, M mute, F fullscreen, I picture in picture, Escape return to app, P/N previous/next. Typing and native control interactions retain their normal keys.
- Stream links are cached in memory for up to ten minutes, capped before their signed expiry; concurrent requests share one extraction. Repeat opens also reuse compatible streams from quality metadata, and a short hover-exit grace period preserves extraction across a click. Reopening the current ready source resumes it directly. Preparing the first video in a list, hovered videos, and optional next autoplay video can hide some lookup delay. Turn preparation off in Playback settings to avoid advance network requests. First-time YouTube lookup and media buffering still depend on the network; instant playback is not guaranteed.
- Save for later keeps video references locally, including imported file paths. Moving or deleting a local file makes its saved reference unavailable.
- Pass a YouTube URL or absolute local video path as the first command-line argument to open it directly.

YouTube search and stream resolution use unofficial access and may be throttled or break when YouTube changes. Private, age-restricted, DRM-protected, or blocked videos may be unavailable. Expired stream links are resolved again; failed playback invalidates its cached link, and Retry playback forces a fresh lookup. Ordinary resolution skips unneeded adaptive manifests, with full extraction retried if a combined format cannot be found. The native player removes the embedded-player error 153 path; it does not remove content restrictions. Open on YouTube is available as a fallback.

## Privacy and dependencies

Closing the main window or PiP ends the application rather than leaving it running in the background. Shutdown saves the last playback position when possible, cancels browsing/preparation/captions, stops owned yt-dlp helper trees, releases the native player and display-awake request, and exits WinUI. Minimizing PiP remains separate and keeps playback running.

Search and playback requests go directly to YouTube/Google. No FluentTube server, browser cookies, API key, or yt-dlp user configuration/plugins are used. Local data lives in `%LOCALAPPDATA%\FluentTube`. Old version 0.1 keys are no longer read or used.

The Windows build bundles the pinned **yt-dlp 2026.10.26 directory distribution** and **Deno 2.9.8**, verified against SHA-256 hashes at build time. The directory distribution avoids the standalone executable's repeated extraction overhead; keep its `_internal` directory intact. Deno supplies the JavaScript runtime required by yt-dlp's YouTube support. Dependency license notices are included in `Tools`. The app does not update executables at runtime; install a newer FluentTube build for updated helper support.

## Build

Install .NET 10 SDK and Windows application development tools with a Windows SDK (22621 or later), then:

```powershell
./scripts/Get-SearchHelper.ps1
dotnet run --project tests/FluentTube.Checks -c Release
dotnet publish src/FluentTube/FluentTube.csproj -c Release -p:Platform=x64 -o artifacts/FluentTube-win-x64
./scripts/Package-Release.ps1
```

GitHub Actions checks URL parsing, metadata, literal process arguments, and stream validation; publishes on Windows; and verifies the executable, XAML resources, and helpers are included. No credentials are needed to build.

Checks also cover Shorts classification and page bounds, portrait 1080p manifests, and VTT/SRT timing, overlapping cues, and caption URL validation. An optional Windows integration check exercises live Shorts pages, native playback, seeking, speed changes, and available portrait 1080p streams: `dotnet run --project tests/FluentTube.PlaybackChecks -c Release -- --shorts '@YouTube'`.

Planned: account OAuth/sync, ARM64, and signed MSIX distribution. FluentTube is independent and is not affiliated with YouTube, Google, or VLC.
