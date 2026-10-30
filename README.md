# FluentTube

A Windows 11 YouTube client built with **C#, .NET 10, and WinUI 3**. Version 0.7 connects Home, channel browsing, local subscriptions, history, Watch later, and a watch queue in the standard Fluent/Mica interface. No API key or Google account is needed. This is a public-video client with local library features, not a complete signed-in YouTube replacement.

## Download

Open [Windows build](https://github.com/clustifle/FluentTube/actions/workflows/windows.yml), select the latest successful run, and download `FluentTube-win-x64` from Artifacts (GitHub sign-in required). Extract the **entire ZIP**, including `Tools`, and run `FluentTube.exe`.

Requires Windows 11 x64. .NET and Windows App SDK runtimes are bundled. Builds are unsigned; there is no installer yet. WebView2 and the YouTube embedded player are no longer used.

## Watch and import

- **Home** loads real videos at startup. With local follows, All shows recent uploads from your channels; without follows, it shows exploratory search results. Topic filters let you explore separately. This is not YouTube's signed-in recommendation algorithm.
- **Channels** open from a creator link, Add channel, or a pasted `@handle`/channel URL. Browse uploads and use Load more for subsequent pages. Search results also support Load more (up to ten pages).
- **Follow** on a channel page or watch page saves a local subscription. Followed channels appear in the sidebar; Subscriptions combines their recent uploads. YouTube feeds are used where available, with channel extraction as fallback. Home can show cached uploads while refreshing. Follows do not alter or sync Google account subscriptions.
- **History** records videos once playback starts and keeps the last 200 unique videos. Clear history removes this local list after confirmation. Playback positions are stored separately.
- The watch page shows the creator, description, and queue. Use Now playing to return after browsing. Right-click a video to play, play next, save, remove from Watch later, or open its channel. Video cards show duration, views, and dates when the source provides them.
- Type a topic, creator, or song to search. No Google Cloud project, API key, billing account, or sign-in needed.
- Paste a YouTube watch, Shorts, live, or youtu.be URL to open it in the native Windows player. yt-dlp resolves a combined audio/video stream; quality depends on what YouTube provides in this format (often 360p). Separate high-resolution audio/video merging is not implemented.
- Choose **Import video** to open a local file. MP4, MKV, WebM, MOV, AVI, WMV, M4V, MP3, and M4A appear in the picker; actual decoding depends on installed Windows codecs. Files are opened in place, not copied or uploaded.
- Native transport controls provide play/pause, seek, volume, playback speed, and full-window video. The player also offers back/forward ten seconds, previous/next in the current result or saved list, repeat, retry with a fresh stream, optional autoplay next, and remembered playback positions. Open the player command bar's overflow menu for keyboard shortcuts.
- Playback shortcuts: Space/K play or pause, J/L seek ten seconds, M mute, F full window, Escape exit full window, P/N previous/next. Typing and native control interactions retain their normal keys.
- Stream links are cached in memory for up to ten minutes, capped before their signed expiry; concurrent requests share one extraction. Reopening the current ready source resumes it directly. Hover preparation and optional next-video preparation can hide some of the lookup delay. Turn preparation off in Playback settings to avoid advance network requests. First-time YouTube lookup and media buffering still depend on the network; instant playback is not guaranteed.
- Save for later keeps video references locally, including imported file paths. Moving or deleting a local file makes its saved reference unavailable.
- Pass a YouTube URL or absolute local video path as the first command-line argument to open it directly.

YouTube search and stream resolution use unofficial access and may be throttled or break when YouTube changes. Private, age-restricted, DRM-protected, or blocked videos may be unavailable. Expired stream links are resolved again; failed playback invalidates its cached link, and Retry playback forces a fresh lookup. Ordinary resolution skips unneeded adaptive manifests, with full extraction retried if a combined format cannot be found. The native player removes the embedded-player error 153 path; it does not remove content restrictions. Open on YouTube is available as a fallback.

## Privacy and dependencies

Search and playback requests go directly to YouTube/Google. No FluentTube server, browser cookies, API key, or yt-dlp user configuration/plugins are used. Local data lives in `%LOCALAPPDATA%\FluentTube`. Old version 0.1 keys are no longer read or used.

The Windows build bundles pinned **yt-dlp 2026.10.26** and **Deno 2.9.8**, verified against SHA-256 hashes at build time. Deno supplies the JavaScript runtime required by yt-dlp's YouTube support. Dependency license notices are included in `Tools`. The app does not update executables at runtime; install a newer FluentTube build for updated helper support.

## Build

Install .NET 10 SDK and Windows application development tools with a Windows SDK (22621 or later), then:

```powershell
./scripts/Get-SearchHelper.ps1
dotnet run --project tests/FluentTube.Checks -c Release
dotnet publish src/FluentTube/FluentTube.csproj -c Release -p:Platform=x64 -o artifacts/FluentTube-win-x64
```

GitHub Actions checks URL parsing, metadata, literal process arguments, and stream validation; publishes on Windows; and verifies the executable, XAML resources, and helpers are included. No credentials are needed to build.

Planned: adaptive high-resolution playback, account OAuth, subscriptions, history, removing saved entries, ARM64, and signed MSIX distribution. FluentTube is independent and is not affiliated with YouTube, Google, or VLC.
