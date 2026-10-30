# FluentTube privacy

FluentTube has no account system, app server, analytics, or telemetry. Searches, public feeds, video resolution, thumbnails, selected captions, and playback contact YouTube/Google directly. Opening external links contacts the selected site in your browser.

Settings, followed channels, Watch later references, recent history, playback positions, and cached browsing metadata are stored in `%LOCALAPPDATA%\FluentTube`. History keeps up to 200 unique videos. In-memory stream URLs expire and are not an account connection. Local videos are opened in place, not uploaded.

Takeout ZIP/CSV imports are read locally. Only validated channel IDs and names are merged into the local subscriptions. Other archive content is not extracted. Subscriptions do not sync to your Google account. Local subtitle files are also read locally; remote captions download only when selected.

Prepare videos before opening is enabled by default. It can request a stream before you click a video. Disable it in Playback settings to avoid these advance requests. No browser cookies or Google OAuth tokens are used.

On an unhandled error, a local startup-error log may contain exception details and local file paths. FluentTube does not upload it. Deleting the app's local data folder while the app is closed removes its library, preferences, caches, and logs.
