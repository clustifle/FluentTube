# FluentTube

A Windows 11 YouTube client built with **C#, .NET 10, and WinUI 3**. Version 0.4 introduces a complete interface redesign: a Mica Alt shell, custom draggable title bar, adaptive sidebar, Discover hero and topic shortcuts, refined video cards, a dedicated native player workspace, and grouped Settings. Native Windows playback, key-free search, local imports, and saved preferences are preserved. This is an early app, not a finished YouTube replacement.

## Download

Open [Windows build](https://github.com/clustifle/FluentTube/actions/workflows/windows.yml), select the latest successful run, and download `FluentTube-win-x64` from Artifacts (GitHub sign-in required). Extract the **entire ZIP**, including `Tools`, and run `FluentTube.exe`.

Requires Windows 11 x64. .NET and Windows App SDK runtimes are bundled. Builds are unsigned; there is no installer yet. WebView2 and the YouTube embedded player are no longer used.

## Watch and import

- Type a topic, creator, or song to search. No Google Cloud project, API key, billing account, or sign-in needed.
- Paste a YouTube watch, Shorts, live, or youtu.be URL to open it in the native Windows player. yt-dlp resolves a combined audio/video stream; quality depends on what YouTube provides in this format (often 360p). Separate high-resolution audio/video merging is not implemented.
- Choose **Import video** to open a local file. MP4, MKV, WebM, MOV, AVI, WMV, M4V, MP3, and M4A appear in the picker; actual decoding depends on installed Windows codecs. Files are opened in place, not copied or uploaded.
- Native transport controls provide play/pause, seek, volume, and fullscreen. Settings provide speed, repeat, autoplay, default volume, theme, accent color, compact cards, and startup behavior. Preferences save automatically and can be reset.
- Save for later keeps video references locally, including imported file paths. Moving or deleting a local file makes its saved reference unavailable.
- Pass a YouTube URL or absolute local video path as the first command-line argument to open it directly.

YouTube search and stream resolution use unofficial access and may be throttled or break when YouTube changes. Private, age-restricted, DRM-protected, or blocked videos may be unavailable. Direct stream URLs expire and are resolved again when reopening a video. The native player removes the embedded-player error 153 path; it does not remove content restrictions. Open on YouTube is available as a fallback.

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
