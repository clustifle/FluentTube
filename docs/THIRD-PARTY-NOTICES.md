# Bundled components

The portable package includes Microsoft .NET 10 and Windows App SDK runtime components, yt-dlp 2026.10.26 (directory distribution), and Deno 2.9.8. Runtime components and dependencies retain their respective licenses. Keep bundled license and notice files with the application.

- .NET: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- Windows App SDK: https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE
- yt-dlp and third-party distribution notices: https://github.com/yt-dlp/yt-dlp#license
- Deno: https://github.com/denoland/deno/blob/main/LICENSE.md

The yt-dlp directory distribution includes Python and other dependencies. Their bundled notices are in Tools/yt-dlp/_internal. Search the package for LICENSE, COPYING, and NOTICE files for individual component details. Helper downloads are pinned and checked by SHA-256 in scripts/Get-SearchHelper.ps1.

FluentTube is independent of YouTube, Google, Microsoft, and VLC. The original FluentTube icon artwork is stored under branding/; standard interface symbols use the Windows Segoe Fluent Icons font.
