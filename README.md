# FluentTube

A Windows 11 YouTube client built with **C#, .NET 10, and WinUI 3**. Native navigation, Mica backdrop, video cards, search, settings, and a local Watch later library. Version 0.2 searches without an API key or Google Cloud billing account. This is an early working foundation, not a finished YouTube replacement.

## Download

Open [Windows build](https://github.com/clustifle/FluentTube/actions/workflows/windows.yml), select a successful run, and download `FluentTube-win-x64` from Artifacts (GitHub sign-in required). Extract the entire ZIP, then run `FluentTube.exe`. Windows 11 x64 and Microsoft Edge WebView2 Runtime are required. .NET and Windows App SDK runtimes are bundled. Builds are unsigned; there is no installer yet.

## Use

- Paste a YouTube watch, Shorts, live, or youtu.be URL into the native search box to play without an API key.
- Type a topic, creator, or song to search with the bundled **yt-dlp** helper. No API key, Google Cloud project, billing account, or sign-in is required. Discover is a starting screen, not a personalized or trending feed.
- Click a video card to watch. Save for later keeps a local list across restarts.
- Playback uses the official YouTube embedded player in **WebView2**. The player itself is web-based; the surrounding application is native WinUI 3. YouTube controls advertisements, embedding restrictions, availability, and sign-in requirements. Open on YouTube provides a browser fallback.
- The player is enclosed in a local HTML page mapped to an HTTPS origin with WebView2 virtual-host mapping. This supplies a consistent browser-generated Referer and matching IFrame API origin to address error 153. No remote site is hosted or required for that local page. Other YouTube restrictions can still prevent playback; player errors appear in the native status bar.
- Search uses unofficial access and can be throttled or break when YouTube changes. Install a newer app build for updated yt-dlp support. Search only reads metadata; it does not download videos, load browser cookies, or use your yt-dlp configuration/plugins.

## Build

Install .NET 10 SDK and Visual Studio 2022/2026 Windows application development tools with a Windows SDK (22621 or later), then:

```powershell
./scripts/Get-SearchHelper.ps1
dotnet publish src/FluentTube/FluentTube.csproj -c Release -p:Platform=x64 -o artifacts/FluentTube-win-x64
```

GitHub Actions verifies the pinned yt-dlp 2026.10.26 executable against its SHA-256 checksum, bundles its license notices, checks URL/metadata handling, publishes on Windows, and verifies the player HTML and search helper are included. No API keys or signing credentials are needed to build. The app never downloads executable updates at runtime.

## Current scope

Implemented: native shell, API-key-free search, URL opening, embedded player with HTTPS enclosing page and error reporting, local saved videos, and CI artifacts. Local data lives in `%LOCALAPPDATA%\FluentTube`. A key saved by version 0.1 is no longer read or used.

Next: account OAuth, subscriptions, history, removing saved videos, ARM64, signed MSIX distribution, accessibility and playback testing. No stream extraction, downloads, or ad blocking. This independent project is not affiliated with YouTube or Google.
