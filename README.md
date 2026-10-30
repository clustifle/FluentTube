# FluentTube

A Windows 11 YouTube client built with **C#, .NET 10, and WinUI 3**. Native navigation, Mica backdrop, video cards, search, settings, and a local Watch later library. This is an early working foundation, not a finished YouTube replacement.

## Download

Open [Windows build](https://github.com/clustifle/FluentTube/actions/workflows/windows.yml), select a successful run, and download `FluentTube-win-x64` from Artifacts (GitHub sign-in required). Extract the entire ZIP, then run `FluentTube.exe`. Windows 11 x64 and Microsoft Edge WebView2 Runtime are required. .NET and Windows App SDK runtimes are bundled. Builds are unsigned; there is no installer yet.

## Use

- Paste a YouTube watch, Shorts, live, or youtu.be URL into the native search box to play without an API key.
- To discover popular videos or search YouTube, enable **YouTube Data API v3** in your Google Cloud project and enter its API key in Settings. Search consumes API quota. Never commit a key. Keys are encrypted for the current Windows user and stored under `%LOCALAPPDATA%\FluentTube`.
- Click a video card to watch. Save for later keeps a local list across restarts.
- Playback uses the official YouTube embedded player in **WebView2**. The player itself is web-based; the surrounding application is native WinUI 3. YouTube controls advertisements, embedding restrictions, availability, and sign-in requirements. Open on YouTube provides a browser fallback.

## Build

Install .NET 10 SDK and Visual Studio 2022/2026 Windows application development tools with a Windows SDK (22621 or later), then:

```powershell
dotnet publish src/FluentTube/FluentTube.csproj -c Release -p:Platform=x64 -o artifacts/FluentTube-win-x64
```

GitHub Actions performs the same publish on a Windows runner for every main-branch push and pull request. No API keys or signing credentials are needed to build.

## Current scope

Implemented: native shell, live API search/discovery, URL opening, embedded player, encrypted API-key settings, local saved videos, and CI artifacts.

Next: account OAuth, subscriptions, history, removing saved videos, ARM64, signed MSIX distribution, accessibility and playback testing. No stream extraction, downloads, or ad blocking. This independent project is not affiliated with YouTube or Google.
