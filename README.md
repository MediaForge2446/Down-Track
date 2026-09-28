# DownTrack

Clean-start native Windows 11 WPF desktop application.

## Current foundation

- .NET 8 + WPF, targeting win-x64.
- Windows 11 Fluent-inspired Light UI with native title bar and Mica backdrop when supported.
- WPF-UI 4.3.0 is referenced for the Fluent control/theme foundation.
- Three application surfaces: Library, Explorer and Settings.
- Floating Add Media experience with background metadata inspection and thumbnail preview.
- Persistent Pending Changes staging: media/file operations are not committed to disk until Save Changes.
- 20 language catalogs under src/DownTrack.App/Locales, loaded dynamically at startup.
- Hebrew and Arabic switch the interface to RTL.
- First-run media-engine bootstrap stores binaries in %LocalAppData%\DownTrack\bin.
- yt-dlp, FFmpeg, ffprobe and Deno are downloaded only when needed and their packages are checksum-verified before installation.
- yt-dlp / FFmpeg run as hidden background child processes; DownTrack intentionally never opens a CMD/Terminal window.

## Web Installer

The public bootstrapper is DownTrack-WebSetup.exe.

- Native AOT Win32 executable: no WPF, WinUI, Windows App SDK or .NET runtime is required just to start the installer.
- Direct release assets use GitHub's releases/latest/download permalink; the GitHub API is not used.
- The DownTrack ZIP is SHA-256 verified before extraction.
- The installer checks for .NET 8 Desktop Runtime and silently installs it only when missing.
- The application is installed to %LocalAppData%\Programs\DownTrack.
- User data and media state in %LocalAppData%\DownTrack are never replaced during application updates.
- Interrupted installs recover from the local backup directory before retrying.
- Start Menu and Desktop shortcuts are created automatically.
- WebSetup supports automatic OS language detection plus the 20 supported languages.
- WebSetup smoke-test mode (--smoke-test) exercises native window creation without contacting the network.

Stable release asset URLs:

https://github.com/MediaForge2446/Down-Track/releases/latest/download/DownTrack-WebSetup.exe
https://github.com/MediaForge2446/Down-Track/releases/latest/download/DownTrack-WebSetup.exe.sha256
https://github.com/MediaForge2446/Down-Track/releases/latest/download/DownTrack-win-x64.zip
https://github.com/MediaForge2446/Down-Track/releases/latest/download/DownTrack-win-x64.zip.sha256

## Media engine sources

yt-dlp is downloaded from the official yt-dlp GitHub release channel.

FFmpeg is downloaded from the Windows Essentials build published by gyan.dev, which is one of the Windows binary sources linked by the official FFmpeg download page.

Deno is downloaded from the official Deno GitHub release channel.

## Build

Open DownTrack.sln in Visual Studio 2022+ or use the GitHub Actions workflows.

The build workflow restores .NET 8, builds the solution, publishes DownTrack, publishes the Native AOT WebSetup, runs smoke tests, and uploads separate artifacts for the app, installer, and release preview.

The release workflow is tag-based (v1.2.3). It packages the framework-dependent DownTrack payload, creates SHA-256 sidecars and the version asset, publishes DownTrack-WebSetup.exe, creates or updates the GitHub release, and performs an end-to-end installer install on Windows.

## Runtime

The main DownTrack app remains framework-dependent and therefore requires the Microsoft .NET 8 Windows Desktop Runtime. The WebSetup bootstrapper itself is Native AOT and does not require .NET to open.
