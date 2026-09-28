# DownTrack

Clean-start native Windows 11 WPF desktop application.

## Current foundation

- .NET 8 + WPF, targeting win-x64.
- Windows 11 Fluent-inspired Light UI with native title bar and Mica backdrop when supported.
- WPF-UI 4.3.0 is referenced for the Fluent control/theme foundation.
- Three application surfaces: Library, Explorer and Settings.
- Floating Add Media experience with background metadata inspection and thumbnail preview.
- Persistent Pending Changes staging: media/file operations are not committed to disk until Save Changes.
- 20 language catalogs under `src/DownTrack.App/Locales`, loaded dynamically at startup.
- Hebrew and Arabic switch the interface to RTL.
- First-run media-engine bootstrap stores binaries in `%LocalAppData%\DownTrack\bin`.
- yt-dlp, FFmpeg, ffprobe and Deno are downloaded only when needed and their packages are checksum-verified before installation.
- yt-dlp / FFmpeg run as hidden background child processes; DownTrack intentionally never opens a CMD/Terminal window.

## Media engine sources

yt-dlp is downloaded from the official yt-dlp GitHub release channel.

FFmpeg is downloaded from the Windows Essentials build published by gyan.dev, which is one of the Windows binary sources linked by the official FFmpeg download page.

Deno is downloaded from the official Deno GitHub release channel.

## Build

Open `DownTrack.sln` in Visual Studio 2022+ or use the GitHub Actions workflow.

The CI workflow:
1. restores .NET 8
2. builds Release x64
3. publishes `win-x64`
4. uploads the published application as the `DownTrack-win-x64` artifact

For local development, no engine binaries need to be committed to the repository; the first-run engine service installs them into LocalAppData.