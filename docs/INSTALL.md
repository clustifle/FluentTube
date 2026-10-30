# Install FluentTube 1.0 RC1

This release candidate is an unsigned, portable Windows 11 x64 application. .NET 10 and Windows App SDK runtimes are bundled. No Google Cloud setup or WebView2 is required.

1. Download the versioned Windows ZIP and its SHA-256 checksum from the GitHub release.
2. Extract the entire ZIP into a permanent folder you can write to. Do not run the executable from inside the ZIP.
3. Open `FluentTube.exe`. Keep `Assets`, `Tools`, and the other bundled files beside it.
4. Optionally create a shortcut to this executable. The new icon appears in the executable, window, and taskbar.

For a development build, the same ZIP is available through GitHub Actions artifacts. Downloading Actions artifacts may require signing in to GitHub.

## Updating

For builds from this checkout, use `scripts/Update-CurrentBuild.ps1`. The stable local application path is `artifacts/FluentTube-current/FluentTube.exe`. The script keeps only the current local application build and current release archive after a successful update; it does not remove local app data.

Close FluentTube, verify the new release ZIP's checksum, and replace the contents of your existing application folder. Keep your shortcut pointed at that folder's executable. Your library and preferences stay in `%LOCALAPPDATA%\FluentTube`; replacing the application folder does not remove them. Keep a copy of this data folder before upgrading if you want a rollback backup.

## First use

- Search for a video or paste a YouTube/Shorts URL.
- Import subscriptions from Google Takeout under Subscriptions or Settings → Subscriptions.
- Open a local file with Open file.
- Press F for fullscreen, I for Picture-in-Picture, and Esc to return.

## Troubleshooting

- A missing helper or resources message usually means the ZIP was not fully extracted.
- If launch fails, inspect `%LOCALAPPDATA%\FluentTube\startup-error.txt` when it exists. Remove private paths and account information before sharing logs.
- Video availability, network restrictions, YouTube verification requests, and installed Windows codecs can affect playback. Retry video refreshes the stream URL; Open on YouTube opens your browser.
- This build is unsigned. Windows may show its normal publisher warning; check the source and published checksum before choosing whether to run it.

RC1 still requires clean-machine installation and interactive fullscreen/PiP/multi-monitor validation before the stable release.
