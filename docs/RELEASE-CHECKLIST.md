# 1.0 release gates

RC1 is a release candidate, not a declaration that these manual gates passed.

## Automated and local engine checks

- [ ] Windows GitHub Actions build and versioned ZIP/checksum succeed for the RC tag.
- [x] URL, metadata, cache, cancellation, retry, caption, portrait-quality, Takeout, and PiP geometry checks pass.
- [x] Native local and public video decode, seek, and speed checks pass on the RC source.
- [x] Live Shorts pages are distinct; portrait combined and available 1080p adaptive streams play.
- [x] Executable, XAML resources, icon, runtimes, helpers, installation instructions, and notices are packaged.
- [x] New icon preview inspected at small sizes on light and dark backgrounds.

These are development-machine and media-engine harness checks. They do not launch or validate the full WinUI window interactions. ZIP contents and SHA-256 were inspected locally; the tagged Actions run provides CI evidence separately.

## Manual Windows checks still required

- [ ] Extract and launch on a clean Windows 11 x64 machine without a preinstalled .NET/Windows App SDK runtime.
- [ ] Search, Home, channel pagination, local follows, and Watch later work.
- [ ] Takeout preview/cancel/import preserves existing data; refreshing loads uploads.
- [ ] Landscape, portrait, square, live, unavailable, and restricted-source behavior is clear.
- [ ] Fullscreen enter/exit and PiP transitions keep audio, position, and play/pause state.
- [ ] PiP minimizes to the taskbar and restores; video ratio and compact controls remain correct.
- [ ] Move between displays at 100%, 150%, and 200% scaling; test different work areas.
- [ ] Description expansion does not squeeze the video.
- [ ] Captions display, seek, wrap, and avoid controls in the main player, fullscreen, and PiP.
- [ ] Disconnect/reconnect the network during playback; one automatic recovery attempt occurs and then actionable errors appear.
- [ ] Keyboard focus, screen-reader labels, narrow windows, high contrast, light/dark themes, and reduced motion work.
- [ ] Upgrade from 0.9 preserves saved videos, follows, history, preferences, and local file references.

Only publish stable v1.0.0 after completing these gates. Do not infer clean-machine or desktop interaction results from a successful compile.
