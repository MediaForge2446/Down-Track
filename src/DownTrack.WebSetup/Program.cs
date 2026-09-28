using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace DownTrack.WebSetup;

internal static class Program
{
    private const string LatestZipUrl =
        "https://github.com/MediaForge2446/Down-Track/releases/latest/download/DownTrack-win-x64.zip";
    private const string LatestShaUrl =
        "https://github.com/MediaForge2446/Down-Track/releases/latest/download/DownTrack-win-x64.zip.sha256";
    private const string LatestVersionUrl =
        "https://github.com/MediaForge2446/Down-Track/releases/latest/download/DownTrack-version.txt";
    private const string DesktopRuntimeUrl =
        "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";

    private const int Width = 460;
    private const int Height = 320;
    private const uint WmAppUpdate = 0x8001;
    private const uint WmCommand = 0x0111;
    private const uint WmPaint = 0x000F;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmNCHitTest = 0x0084;
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const nint HtClient = 1;
    private const nint HtCaption = 2;
    private const int SwShow = 5;
    private const uint WsPopup = 0x80000000;
    private const uint WsExAppWindow = 0x00040000;
    private const uint CsHRedraw = 0x0002;
    private const uint CsVRedraw = 0x0001;
    private const uint MFString = 0x00000000;
    private const uint MFRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRound = 2;
    private const uint ClsContextInprocServer = 1;
    private const int CoInitMultithreaded = 0;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    private static readonly string InstallRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
    private static readonly string InstallDir = Path.Combine(InstallRoot, "DownTrack");
    private static readonly string StagingDir = Path.Combine(InstallRoot, "DownTrack.new");
    private static readonly string BackupDir = Path.Combine(InstallRoot, "DownTrack.backup");
    private static readonly string DownloadZip = Path.Combine(InstallRoot, "DownTrack.download.zip");
    private static readonly string DownloadSha = Path.Combine(InstallRoot, "DownTrack.download.sha256");
    private static readonly string RuntimeInstaller = Path.Combine(InstallRoot, "DownTrack.desktop-runtime.exe");

    private static readonly HttpClient Http = CreateHttpClient();

    private static nint _hwnd;
    private static string _language = "en";
    private static string _statusKey = "getting_ready";
    private static string _errorMessage = "";
    private static double _progress;
    private static bool _failed;
    private static bool _retryVisible;
    private static bool _smokeTest;
    private static readonly object UiGate = new();

    private static readonly (string Code, string Name)[] Languages =
    {
        ("auto", "Automatic"),
        ("en", "English"),
        ("he", "עברית"),
        ("es", "Español"),
        ("fr", "Français"),
        ("de", "Deutsch"),
        ("it", "Italiano"),
        ("pt", "Português"),
        ("nl", "Nederlands"),
        ("pl", "Polski"),
        ("cs", "Čeština"),
        ("tr", "Türkçe"),
        ("uk", "Українська"),
        ("ru", "Русский"),
        ("ar", "العربية"),
        ("el", "Ελληνικά"),
        ("ro", "Română"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文")
    };

    private static unsafe void Main(string[] args)
    {
        _smokeTest = args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase));
        _language = _smokeTest ? "en" : DetectLanguage();

        if (!CreateMainWindow())
        {
            Environment.ExitCode = 1;
            return;
        }

        ShowWindow(_hwnd, SwShow);

        if (_smokeTest)
        {
            _ = SmokeExitAsync();
        }
        else
        {
            _ = RunInstallerAsync();
        }

        MSG msg;
        while (GetMessageW(&msg, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    private static async Task SmokeExitAsync()
    {
        await Task.Delay(1200);
        Environment.Exit(0);
    }

    private static async Task RunInstallerAsync()
    {
        try
        {
            RecoverInterruptedUpdate();

            SetStatus("checking_latest", 3);
            var latestVersion = await GetLatestVersionAsync(CancellationToken.None);
            var installedVersion = ReadInstalledVersion();

            if (latestVersion is null)
            {
                throw new InvalidOperationException("The latest release version could not be determined.");
            }

            if (IsInstalledVersionCurrent(installedVersion, latestVersion))
            {
                SetStatus("checking_runtime", 15);
                await EnsureDesktopRuntimeAsync();
                SetStatus("ready", 100);
                LaunchInstalledAppIfNeeded();
                await Task.Delay(700);
                PostMessageW(_hwnd, WmClose, nint.Zero, nint.Zero);
                return;
            }

            SetStatus("checking_runtime", 12);
            await EnsureDesktopRuntimeAsync();

            if (IsDownTrackRunning())
            {
                SetFailure(GetText("close_to_update"), retry: true);
                return;
            }

            Directory.CreateDirectory(InstallRoot);
            SafeDeleteDirectory(StagingDir);
            SafeDeleteFile(DownloadZip);
            SafeDeleteFile(DownloadSha);

            SetStatus("downloading", 20);
            await DownloadFileAsync(LatestZipUrl, DownloadZip, 20, 72);

            SetStatus("verifying", 76);
            var expectedHash = await ReadExpectedShaAsync(LatestShaUrl);
            var actualHash = await ComputeSha256Async(DownloadZip);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Checksum validation failed.");
            }

            SetStatus("installing", 82);
            Directory.CreateDirectory(StagingDir);
            ZipFile.ExtractToDirectory(DownloadZip, StagingDir, overwriteFiles: true);

            var stagedExe = Path.Combine(StagingDir, "DownTrack.exe");
            if (!File.Exists(stagedExe))
            {
                throw new InvalidOperationException("The downloaded package is missing DownTrack.exe.");
            }

            WriteText(Path.Combine(StagingDir, ".downtrack-version"), latestVersion);
            SetProgress(88);

            ReplaceInstallation();
            CreateShortcuts(Path.Combine(InstallDir, "DownTrack.exe"));

            SetStatus("ready", 100);
            LaunchInstalledAppIfNeeded();
            await Task.Delay(900);
            PostMessageW(_hwnd, WmClose, nint.Zero, nint.Zero);
        }
        catch
        {
            SetFailure(GetText("generic_error"), retry: true);
        }
    }

    private static async Task EnsureDesktopRuntimeAsync()
    {
        if (HasDesktopRuntime8())
        {
            SetProgress(18);
            return;
        }

        Directory.CreateDirectory(InstallRoot);
        SafeDeleteFile(RuntimeInstaller);

        SetStatus("installing_runtime", 13);
        await DownloadFileAsync(DesktopRuntimeUrl, RuntimeInstaller, 13, 35);

        var psi = new ProcessStartInfo
        {
            FileName = RuntimeInstaller,
            Arguments = "/install /quiet /norestart",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = InstallRoot
        };

        using var process = Process.Start(psi) ??
            throw new InvalidOperationException("The .NET Desktop Runtime installer could not start.");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && process.ExitCode != 3010)
        {
            throw new InvalidOperationException(
                $"The .NET Desktop Runtime installer returned exit code {process.ExitCode}.");
        }

        if (!HasDesktopRuntime8())
        {
            throw new InvalidOperationException(
                "The .NET Desktop Runtime is still unavailable after installation.");
        }

        SafeDeleteFile(RuntimeInstaller);
        SetProgress(18);
    }

    private static async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(
                LatestVersionUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var version = NormalizeVersion(content);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
        }
        catch
        {
            // Fallback below uses the redirect target of the direct asset URL.
        }

        return await GetVersionFromRedirectAsync(cancellationToken);
    }

    private static async Task<string?> GetVersionFromRedirectAsync(CancellationToken cancellationToken)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var uri = new Uri(LatestZipUrl);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400 &&
                response.Headers.Location is not null)
            {
                uri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(uri, response.Headers.Location);

                var redirectVersion = ExtractVersion(uri.AbsoluteUri);
                if (redirectVersion is not null)
                {
                    return redirectVersion;
                }

                continue;
            }

            var directVersion = ExtractVersion(uri.AbsoluteUri);
            if (response.IsSuccessStatusCode && directVersion is not null)
            {
                return directVersion;
            }

            break;
        }

        return null;
    }

    private static string? ExtractVersion(string value)
    {
        const string marker = "/releases/download/";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + marker.Length;
        var end = value.IndexOf('/', start);
        if (end <= start)
        {
            return null;
        }

        return NormalizeVersion(value[start..end]);
    }

    private static string NormalizeVersion(string value)
    {
        var candidate = value.Trim().TrimStart('v', 'V');
        var first = candidate.IndexOfAny(new[] { '\r', '\n', ' ', '\t' });
        if (first >= 0)
        {
            candidate = candidate[..first];
        }

        return candidate;
    }

    private static string? ReadInstalledVersion()
    {
        var marker = Path.Combine(InstallDir, ".downtrack-version");
        if (!File.Exists(marker))
        {
            return null;
        }

        try
        {
            return NormalizeVersion(File.ReadAllText(marker, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsInstalledVersionCurrent(string? installedVersion, string latestVersion)
    {
        if (installedVersion is null)
        {
            return false;
        }

        if (Version.TryParse(installedVersion, out var installed) &&
            Version.TryParse(latestVersion, out var latest))
        {
            return installed >= latest &&
                File.Exists(Path.Combine(InstallDir, "DownTrack.exe"));
        }

        return string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(Path.Combine(InstallDir, "DownTrack.exe"));
    }

    private static bool HasDesktopRuntime8()
    {
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var desktopPath = Path.Combine(
                programFiles,
                "dotnet",
                "shared",
                "Microsoft.WindowsDesktop.App");

            if (Directory.Exists(desktopPath) &&
                Directory.GetDirectories(
                    desktopPath,
                    "8.*",
                    SearchOption.TopDirectoryOnly).Length > 0)
            {
                return true;
            }
        }
        catch
        {
        }

        return RegistryHasDesktopRuntime(Registry.LocalMachine) ||
               RegistryHasDesktopRuntime(Registry.CurrentUser);
    }

    private static bool RegistryHasDesktopRuntime(RegistryKey root)
    {
        try
        {
            using var key = root.OpenSubKey(
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");

            return key?.GetSubKeyNames().Any(
                name => name.StartsWith("8.", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task DownloadFileAsync(string url, string destination, double start, double end)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var length = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            useAsync: true);

        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;

        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            total += read;

            if (length is > 0)
            {
                var fraction = Math.Clamp((double)total / length.Value, 0, 1);
                SetProgress(start + ((end - start) * fraction));
            }
        }

        SetProgress(end);
    }

    private static async Task<string> ReadExpectedShaAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(DownloadSha, content, Encoding.UTF8);

        foreach (var token in content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 64 && token.All(Uri.IsHexDigit))
            {
                return token;
            }
        }

        throw new InvalidOperationException("The release checksum file is invalid.");
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static void RecoverInterruptedUpdate()
    {
        try
        {
            if (!Directory.Exists(InstallDir) && Directory.Exists(BackupDir))
            {
                Directory.Move(BackupDir, InstallDir);
            }
            else if (Directory.Exists(InstallDir) && Directory.Exists(BackupDir))
            {
                SafeDeleteDirectory(BackupDir);
            }

            if (Directory.Exists(StagingDir))
            {
                SafeDeleteDirectory(StagingDir);
            }
        }
        catch
        {
        }
    }

    private static void ReplaceInstallation()
    {
        SafeDeleteDirectory(BackupDir);

        if (Directory.Exists(InstallDir))
        {
            Directory.Move(InstallDir, BackupDir);
        }

        try
        {
            Directory.Move(StagingDir, InstallDir);
            SafeDeleteDirectory(BackupDir);
        }
        catch
        {
            if (!Directory.Exists(InstallDir) && Directory.Exists(BackupDir))
            {
                Directory.Move(BackupDir, InstallDir);
            }

            throw;
        }

        SafeDeleteFile(DownloadZip);
        SafeDeleteFile(DownloadSha);
    }

    private static void CreateShortcuts(string exePath)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var startMenuDir = Path.Combine(programs, "DownTrack");
        Directory.CreateDirectory(startMenuDir);

        CreateShellLink(
            Path.Combine(startMenuDir, "DownTrack.lnk"),
            exePath,
            "DownTrack",
            Path.GetDirectoryName(exePath) ?? InstallDir);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop))
        {
            CreateShellLink(
                Path.Combine(desktop, "DownTrack.lnk"),
                exePath,
                "DownTrack",
                Path.GetDirectoryName(exePath) ?? InstallDir);
        }
    }

    private static unsafe void CreateShellLink(
        string shortcutPath,
        string targetPath,
        string description,
        string workingDirectory)
    {
        Guid clsidShellLink = new("00021401-0000-0000-C000-000000000046");
        Guid iidShellLink = new("000214F9-0000-0000-C000-000000000046");
        Guid iidPersistFile = new("0000010B-0000-0000-C000-000000000046");

        var init = CoInitializeEx(nint.Zero, CoInitMultithreaded);
        var shouldUninitialize = init == 0 || init == 1;

        try
        {
            nint shellLink = nint.Zero;
            nint persistFile = nint.Zero;

            Guid* clsid = &clsidShellLink;
            Guid* iid = &iidShellLink;
                var hr = CoCreateInstance(
                clsid,
                nint.Zero,
                ClsContextInprocServer,
                iid,
                out shellLink);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                var shellVtbl = Marshal.ReadIntPtr(shellLink);

                Guid* persistIid = &iidPersistFile;
                var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)
                    Marshal.ReadIntPtr(shellVtbl, 0 * nint.Size);
                hr = queryInterface(shellLink, persistIid, &persistFile);
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    var setPath = (delegate* unmanaged[Stdcall]<nint, char*, int>)
                        Marshal.ReadIntPtr(shellVtbl, 20 * nint.Size);
                    var setWorkingDirectory = (delegate* unmanaged[Stdcall]<nint, char*, int>)
                        Marshal.ReadIntPtr(shellVtbl, 9 * nint.Size);
                    var setDescription = (delegate* unmanaged[Stdcall]<nint, char*, int>)
                        Marshal.ReadIntPtr(shellVtbl, 7 * nint.Size);
                    var setIconLocation = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)
                        Marshal.ReadIntPtr(shellVtbl, 17 * nint.Size);

                    fixed (char* target = targetPath)
                    fixed (char* work = workingDirectory)
                    fixed (char* text = description)
                    {
                        Marshal.ThrowExceptionForHR(setPath(shellLink, target));
                        Marshal.ThrowExceptionForHR(setWorkingDirectory(shellLink, work));
                        Marshal.ThrowExceptionForHR(setDescription(shellLink, text));
                        Marshal.ThrowExceptionForHR(setIconLocation(shellLink, target, 0));
                    }

                    var persistVtbl = Marshal.ReadIntPtr(persistFile);
                    var save = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)
                        Marshal.ReadIntPtr(persistVtbl, 6 * nint.Size);

                    fixed (char* shortcut = shortcutPath)
                    {
                        Marshal.ThrowExceptionForHR(save(persistFile, shortcut, 1));
                    }
                }
                finally
                {
                    ReleaseComInterface(persistFile);
                }
            }
            finally
            {
                ReleaseComInterface(shellLink);
            }
        }
        finally
        {
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
        }
    }

    private static unsafe void ReleaseComInterface(nint ptr)
    {
        if (ptr == nint.Zero)
        {
            return;
        }

        var vtbl = Marshal.ReadIntPtr(ptr);
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)
            Marshal.ReadIntPtr(vtbl, 2 * nint.Size);
        _ = release(ptr);
    }

    private static void LaunchInstalledAppIfNeeded()
    {
        if (IsDownTrackRunning())
        {
            return;
        }

        var exe = Path.Combine(InstallDir, "DownTrack.exe");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("DownTrack.exe was not found after installation.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = InstallDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        _ = Process.Start(psi) ??
            throw new InvalidOperationException("DownTrack could not be launched.");
    }

    private static bool IsDownTrackRunning()
    {
        try
        {
            return Process.GetProcessesByName("DownTrack").Any(p =>
            {
                try
                {
                    return !p.HasExited;
                }
                catch
                {
                    return false;
                }
            });
        }
        catch
        {
            return false;
        }
    }

    private static void SetStatus(string key, double progress)
    {
        lock (UiGate)
        {
            _statusKey = key;
            _progress = Math.Clamp(progress, 0, 100);
            _failed = false;
            _retryVisible = false;
            _errorMessage = "";
        }

        PostMessageW(_hwnd, WmAppUpdate, nint.Zero, nint.Zero);
    }

    private static void SetProgress(double progress)
    {
        lock (UiGate)
        {
            _progress = Math.Clamp(progress, 0, 100);
        }

        PostMessageW(_hwnd, WmAppUpdate, nint.Zero, nint.Zero);
    }

    private static void SetFailure(string message, bool retry)
    {
        lock (UiGate)
        {
            _failed = true;
            _retryVisible = retry;
            _errorMessage = message;
            _statusKey = "";
        }

        PostMessageW(_hwnd, WmAppUpdate, nint.Zero, nint.Zero);
    }

    private static string GetText(string key)
    {
        var strings = StringsFor(_language);
        return key switch
        {
            "getting_ready" => strings.GettingReady,
            "checking_latest" => strings.CheckingLatest,
            "checking_runtime" => strings.CheckingRuntime,
            "installing_runtime" => strings.InstallingRuntime,
            "downloading" => strings.Downloading,
            "verifying" => strings.Verifying,
            "installing" => strings.Installing,
            "ready" => strings.Ready,
            "close_to_update" => strings.CloseToUpdate,
            "generic_error" => strings.GenericError,
            "retry" => strings.Retry,
            _ => "DownTrack"
        };
    }

    private static void Retry()
    {
        if (!_failed)
        {
            return;
        }

        lock (UiGate)
        {
            _failed = false;
            _retryVisible = false;
            _errorMessage = "";
            _progress = 0;
        }

        _ = RunInstallerAsync();
    }

    private static string DetectLanguage()
    {
        var buffer = new StringBuilder(85);
        var length = GetUserDefaultLocaleName(buffer, buffer.Capacity);

        if (length <= 0)
        {
            return "en";
        }

        var locale = buffer.ToString().ToLowerInvariant();
        if (locale.StartsWith("zh-tw", StringComparison.Ordinal))
        {
            return "zh-TW";
        }

        if (locale.StartsWith("zh", StringComparison.Ordinal))
        {
            return "zh-CN";
        }

        var separator = locale.IndexOf('-');
        var baseCode = separator > 0 ? locale[..separator] : locale;

        return Languages.Any(x => x.Code.Equals(baseCode, StringComparison.OrdinalIgnoreCase))
            ? baseCode
            : "en";
    }

    private static Strings StringsFor(string code) => code.ToLowerInvariant() switch
    {
        "he" => new(
            "מתכוננים ל‑DownTrack…",
            "בודקים אם קיימת הגרסה העדכנית…",
            "בודקים רכיבי Windows…",
            "מתקינים ברקע את רכיבי ‎.NET‎ הדרושים…",
            "מורידים את DownTrack העדכני…",
            "מאמתים את ההורדה…",
            "מתקינים את DownTrack…",
            "DownTrack מוכן. פותחים את הספרייה שלך…",
            "יש לסגור את DownTrack כדי להשלים את העדכון.",
            "לא הצלחנו להשלים את ההורדה. בדוק את החיבור ונסה שוב.",
            "נסה שוב"),
        "es" => new(
            "Preparando DownTrack…",
            "Buscando la versión más reciente…",
            "Comprobando componentes de Windows…",
            "Instalando los componentes de .NET necesarios…",
            "Descargando el DownTrack más reciente…",
            "Verificando la descarga…",
            "Instalando DownTrack…",
            "DownTrack está listo. Abriendo tu biblioteca…",
            "Cierra DownTrack para terminar la actualización.",
            "No pudimos completar la descarga. Comprueba tu conexión e inténtalo de nuevo.",
            "Intentar de nuevo"),
        "fr" => new(
            "Préparation de DownTrack…",
            "Recherche de la dernière version…",
            "Vérification des composants Windows…",
            "Installation des composants .NET requis…",
            "Téléchargement de la dernière version de DownTrack…",
            "Vérification du téléchargement…",
            "Installation de DownTrack…",
            "DownTrack est prêt. Ouverture de votre bibliothèque…",
            "Fermez DownTrack pour terminer la mise à jour.",
            "Nous n’avons pas pu terminer le téléchargement. Vérifiez votre connexion et réessayez.",
            "Réessayer"),
        "de" => new(
            "DownTrack wird vorbereitet…",
            "Nach der neuesten Version suchen…",
            "Windows-Komponenten werden geprüft…",
            "Erforderliche .NET-Komponenten werden installiert…",
            "Neueste DownTrack-Version wird heruntergeladen…",
            "Download wird überprüft…",
            "DownTrack wird installiert…",
            "DownTrack ist bereit. Bibliothek wird geöffnet…",
            "Bitte schließen Sie DownTrack, um das Update abzuschließen.",
            "Der Download konnte nicht abgeschlossen werden. Prüfen Sie Ihre Verbindung und versuchen Sie es erneut.",
            "Erneut versuchen"),
        "it" => new(
            "Preparazione di DownTrack…",
            "Ricerca dell'ultima versione…",
            "Controllo dei componenti Windows…",
            "Installazione dei componenti .NET richiesti…",
            "Download dell'ultima versione di DownTrack…",
            "Verifica del download…",
            "Installazione di DownTrack…",
            "DownTrack è pronto. Apertura della libreria…",
            "Chiudi DownTrack per completare l'aggiornamento.",
            "Non è stato possibile completare il download. Controlla la connessione e riprova.",
            "Riprova"),
        "pt" => new(
            "Preparando o DownTrack…",
            "Verificando a versão mais recente…",
            "Verificando os componentes do Windows…",
            "Instalando os componentes .NET necessários…",
            "Baixando a versão mais recente do DownTrack…",
            "Verificando o download…",
            "Instalando o DownTrack…",
            "O DownTrack está pronto. Abrindo sua biblioteca…",
            "Feche o DownTrack para concluir a atualização.",
            "Não foi possível concluir o download. Verifique sua conexão e tente novamente.",
            "Tentar novamente"),
        "nl" => new(
            "DownTrack wordt voorbereid…",
            "Controleren op de nieuwste versie…",
            "Windows-onderdelen controleren…",
            "Vereiste .NET-onderdelen installeren…",
            "Nieuwste DownTrack downloaden…",
            "Download controleren…",
            "DownTrack installeren…",
            "DownTrack is klaar. Je bibliotheek wordt geopend…",
            "Sluit DownTrack om de update te voltooien.",
            "De download kon niet worden voltooid. Controleer je verbinding en probeer het opnieuw.",
            "Opnieuw proberen"),
        "pl" => new(
            "Przygotowywanie DownTrack…",
            "Sprawdzanie najnowszej wersji…",
            "Sprawdzanie składników systemu Windows…",
            "Instalowanie wymaganych składników .NET…",
            "Pobieranie najnowszego DownTrack…",
            "Weryfikowanie pobrania…",
            "Instalowanie DownTrack…",
            "DownTrack jest gotowy. Otwieranie biblioteki…",
            "Zamknij DownTrack, aby dokończyć aktualizację.",
            "Nie udało się ukończyć pobierania. Sprawdź połączenie i spróbuj ponownie.",
            "Spróbuj ponownie"),
        "cs" => new(
            "Příprava DownTrack…",
            "Kontrola nejnovější verze…",
            "Kontrola součástí Windows…",
            "Instalace potřebných součástí .NET…",
            "Stahování nejnovějšího DownTrack…",
            "Ověřování stažení…",
            "Instalace DownTrack…",
            "DownTrack je připraven. Otevírá se vaše knihovna…",
            "Před dokončením aktualizace zavřete DownTrack.",
            "Stažení se nepodařilo dokončit. Zkontrolujte připojení a zkuste to znovu.",
            "Zkusit znovu"),
        "tr" => new(
            "DownTrack hazırlanıyor…",
            "En son sürüm kontrol ediliyor…",
            "Windows bileşenleri denetleniyor…",
            "Gerekli .NET bileşenleri kuruluyor…",
            "En yeni DownTrack indiriliyor…",
            "İndirme doğrulanıyor…",
            "DownTrack kuruluyor…",
            "DownTrack hazır. Kitaplığınız açılıyor…",
            "Güncellemeyi tamamlamak için DownTrack'i kapatın.",
            "İndirme tamamlanamadı. Bağlantınızı kontrol edip tekrar deneyin.",
            "Tekrar dene"),
        "uk" => new(
            "Підготовка DownTrack…",
            "Перевірка останньої версії…",
            "Перевірка компонентів Windows…",
            "Встановлення потрібних компонентів .NET…",
            "Завантаження найновішої версії DownTrack…",
            "Перевірка завантаження…",
            "Встановлення DownTrack…",
            "DownTrack готовий. Відкриваємо бібліотеку…",
            "Закрийте DownTrack, щоб завершити оновлення.",
            "Не вдалося завершити завантаження. Перевірте з’єднання та спробуйте ще раз.",
            "Спробувати ще раз"),
        "ru" => new(
            "Подготовка DownTrack…",
            "Проверка последней версии…",
            "Проверка компонентов Windows…",
            "Установка необходимых компонентов .NET…",
            "Загрузка последней версии DownTrack…",
            "Проверка загрузки…",
            "Установка DownTrack…",
            "DownTrack готов. Открываем вашу библиотеку…",
            "Закройте DownTrack, чтобы завершить обновление.",
            "Не удалось завершить загрузку. Проверьте подключение и повторите попытку.",
            "Повторить"),
        "ar" => new(
            "جارٍ تجهيز DownTrack…",
            "جارٍ التحقق من أحدث إصدار…",
            "جارٍ التحقق من مكونات Windows…",
            "جارٍ تثبيت مكونات ‎.NET‎ المطلوبة…",
            "جارٍ تنزيل أحدث إصدار من DownTrack…",
            "جارٍ التحقق من التنزيل…",
            "جارٍ تثبيت DownTrack…",
            "DownTrack جاهز. جارٍ فتح مكتبتك…",
            "أغلق DownTrack لإكمال التحديث.",
            "تعذر إكمال التنزيل. تحقق من اتصالك وحاول مرة أخرى.",
            "حاول مرة أخرى"),
        "el" => new(
            "Προετοιμασία του DownTrack…",
            "Έλεγχος για την πιο πρόσφατη έκδοση…",
            "Έλεγχος στοιχείων των Windows…",
            "Εγκατάσταση των απαιτούμενων στοιχείων .NET…",
            "Λήψη της πιο πρόσφατης έκδοσης DownTrack…",
            "Επαλήθευση λήψης…",
            "Εγκατάσταση του DownTrack…",
            "Το DownTrack είναι έτοιμο. Άνοιγμα της βιβλιοθήκης…",
            "Κλείστε το DownTrack για να ολοκληρωθεί η ενημέρωση.",
            "Δεν ήταν δυνατή η ολοκλήρωση της λήψης. Ελέγξτε τη σύνδεσή σας και δοκιμάστε ξανά.",
            "Δοκιμή ξανά"),
        "ro" => new(
            "Se pregătește DownTrack…",
            "Se verifică cea mai recentă versiune…",
            "Se verifică componentele Windows…",
            "Se instalează componentele .NET necesare…",
            "Se descarcă cea mai recentă versiune DownTrack…",
            "Se verifică descărcarea…",
            "Se instalează DownTrack…",
            "DownTrack este gata. Se deschide biblioteca…",
            "Închide DownTrack pentru a finaliza actualizarea.",
            "Descărcarea nu a putut fi finalizată. Verifică conexiunea și încearcă din nou.",
            "Încearcă din nou"),
        "ja" => new(
            "DownTrack を準備しています…",
            "最新バージョンを確認しています…",
            "Windows コンポーネントを確認しています…",
            "必要な .NET コンポーネントをインストールしています…",
            "最新の DownTrack をダウンロードしています…",
            "ダウンロードを確認しています…",
            "DownTrack をインストールしています…",
            "DownTrack の準備ができました。ライブラリを開いています…",
            "更新を完了するには DownTrack を閉じてください。",
            "ダウンロードを完了できませんでした。接続を確認して、もう一度お試しください。",
            "再試行"),
        "ko" => new(
            "DownTrack 준비 중…",
            "최신 버전을 확인하는 중…",
            "Windows 구성 요소를 확인하는 중…",
            "필요한 .NET 구성 요소를 설치하는 중…",
            "최신 DownTrack을 다운로드하는 중…",
            "다운로드를 확인하는 중…",
            "DownTrack을 설치하는 중…",
            "DownTrack이 준비되었습니다. 라이브러리를 여는 중…",
            "업데이트를 완료하려면 DownTrack을 닫아 주세요.",
            "다운로드를 완료하지 못했습니다. 연결을 확인하고 다시 시도해 주세요.",
            "다시 시도"),
        "zh-cn" => new(
            "正在准备 DownTrack…",
            "正在检查最新版本…",
            "正在检查 Windows 组件…",
            "正在安装所需的 .NET 组件…",
            "正在下载最新的 DownTrack…",
            "正在验证下载…",
            "正在安装 DownTrack…",
            "DownTrack 已准备就绪。正在打开您的媒体库…",
            "请关闭 DownTrack 以完成更新。",
            "无法完成下载。请检查网络连接后重试。",
            "重试"),
        "zh-tw" => new(
            "正在準備 DownTrack…",
            "正在檢查最新版本…",
            "正在檢查 Windows 元件…",
            "正在安裝必要的 .NET 元件…",
            "正在下載最新的 DownTrack…",
            "正在驗證下載…",
            "正在安裝 DownTrack…",
            "DownTrack 已準備就緒。正在開啟您的媒體庫…",
            "請關閉 DownTrack 以完成更新。",
            "無法完成下載。請檢查網路連線後再試一次。",
            "重試"),
        _ => new(
            "Getting DownTrack ready…",
            "Checking for the latest version…",
            "Checking Windows components…",
            "Installing required .NET components…",
            "Downloading the latest DownTrack…",
            "Verifying download…",
            "Installing DownTrack…",
            "DownTrack is ready. Opening your library…",
            "Please close DownTrack to finish updating.",
            "We couldn’t finish the download. Check your connection and try again.",
            "Try again")
    };

    private readonly record struct Strings(
        string GettingReady,
        string CheckingLatest,
        string CheckingRuntime,
        string InstallingRuntime,
        string Downloading,
        string Verifying,
        string Installing,
        string Ready,
        string CloseToUpdate,
        string GenericError,
        string Retry);

    private const int ControlBrand = 2001;
    private const int ControlTitle = 2002;
    private const int ControlSubtitle = 2003;
    private const int ControlLanguage = 2004;
    private const int ControlClose = 2005;
    private const int ControlStatus = 2006;
    private const int ControlProgress = 2007;
    private const int ControlFooter = 2008;
    private const int ControlRetry = 2009;

    private static nint _brandHwnd;
    private static nint _titleHwnd;
    private static nint _subtitleHwnd;
    private static nint _languageHwnd;
    private static nint _closeHwnd;
    private static nint _statusHwnd;
    private static nint _progressHwnd;
    private static nint _footerHwnd;
    private static nint _retryHwnd;
    private static nint _backgroundBrush;
    private static nint _accentBrush;
    private static nint _mutedBrush;
    private static nint _uiFont;

    private static unsafe bool CreateMainWindow()
    {
        var hInstance = GetModuleHandleW(nint.Zero);
        const string className = "DownTrackWebSetupWindow";

        fixed (char* classNamePtr = className)
        {
            WNDCLASSW wc = new()
            {
                style = CsHRedraw | CsVRedraw,
                lpfnWndProc = &WindowProc,
                hInstance = hInstance,
                hCursor = LoadCursorW(nint.Zero, new nint(32512)),
                hbrBackground = GetSysColorBrush(5),
                lpszClassName = classNamePtr
            };

            if (RegisterClassW(&wc) == 0)
            {
                return false;
            }

            var x = (GetSystemMetrics(SmCxScreen) - Width) / 2;
            var y = (GetSystemMetrics(SmCyScreen) - Height) / 2;

            _hwnd = CreateWindowExW(
                WsExAppWindow,
                className,
                "DownTrack Web Setup",
                WsPopup,
                x,
                y,
                Width,
                Height,
                nint.Zero,
                nint.Zero,
                hInstance,
                nint.Zero);
        }

        if (_hwnd == nint.Zero)
        {
            return false;
        }

        _backgroundBrush = CreateSolidBrush(ToColorRef(0xFFF8F9FC));
        _accentBrush = CreateSolidBrush(ToColorRef(0xFF6659E8));
        _mutedBrush = CreateSolidBrush(ToColorRef(0xFFF8F9FC));
        _uiFont = GetStockObject(17);

        SetRoundCorners(_hwnd);
        CreateChildControls(_hwnd);
        if (!_smokeTest)
        {
            RefreshControls();
        }
        return true;
    }

    private static nint CreateChildControls(nint parent)
    {
        const uint WsChild = 0x40000000;
        const uint WsVisible = 0x10000000;
        const uint SsCenter = 0x00000001;
        const uint BsPushButton = 0x00000000;

        _brandHwnd = CreateChild(parent, "STATIC", "D", 34, 24, 46, 42, ControlBrand, SsCenter);
        _titleHwnd = CreateChild(parent, "STATIC", "DownTrack", 92, 22, 190, 26, ControlTitle, 0);
        _subtitleHwnd = CreateChild(parent, "STATIC", "Web Setup", 94, 47, 170, 18, ControlSubtitle, 0);

        _languageHwnd = CreateChild(parent, "BUTTON", "EN", 320, 22, 66, 32, ControlLanguage, BsPushButton);
        _closeHwnd = CreateChild(parent, "BUTTON", "×", 398, 18, 40, 36, ControlClose, BsPushButton);

        _statusHwnd = CreateChild(parent, "STATIC", "", 32, 112, 396, 46, ControlStatus, SsCenter);
        _progressHwnd = CreateChild(parent, "msctls_progress32", "", 32, 174, 396, 10, ControlProgress, 0);
        _footerHwnd = CreateChild(parent, "STATIC", "DownTrack", 32, 268, 396, 22, ControlFooter, SsCenter);
        _retryHwnd = CreateChild(parent, "BUTTON", "Try again", 155, 224, 150, 40, ControlRetry, BsPushButton);

        if (_smokeTest)
        {
            return _hwnd;
        }

        SetWindowTheme(_languageHwnd, "Explorer", null);
        SetWindowTheme(_closeHwnd, "Explorer", null);
        SetWindowTheme(_retryHwnd, "Explorer", null);
        SetWindowTheme(_progressHwnd, "Explorer", null);

        ApplyFont(_brandHwnd);
        ApplyFont(_titleHwnd);
        ApplyFont(_subtitleHwnd);
        ApplyFont(_languageHwnd);
        ApplyFont(_closeHwnd);
        ApplyFont(_statusHwnd);
        ApplyFont(_progressHwnd);
        ApplyFont(_footerHwnd);
        ApplyFont(_retryHwnd);

        SendMessageW(_progressHwnd, 0x0401, 0, 100);
        ShowWindow(_retryHwnd, 0);
        return _hwnd;
    }

    private static nint CreateChild(
        nint parent,
        string className,
        string text,
        int x,
        int y,
        int width,
        int height,
        int id,
        uint style)
    {
        const uint WsChild = 0x40000000;
        const uint WsVisible = 0x10000000;

        return CreateWindowExW(
            0,
            className,
            text,
            WsChild | WsVisible | style,
            x,
            y,
            width,
            height,
            parent,
            (nint)id,
            GetModuleHandleW(nint.Zero),
            nint.Zero);
    }

    private static void ApplyFont(nint hwnd)
    {
        if (hwnd != nint.Zero)
        {
            SendMessageW(hwnd, 0x0030, _uiFont, 1);
        }
    }

    private static void RefreshControls()
    {
        if (_statusHwnd == nint.Zero)
        {
            return;
        }

        string status;
        string footer;
        string language;
        bool failed;
        double progress;

        lock (UiGate)
        {
            failed = _failed;
            progress = _progress;
            language = _language
                .ToUpperInvariant()
                .Replace("-CN", "", StringComparison.Ordinal)
                .Replace("-TW", "", StringComparison.Ordinal);

            status = failed
                ? _errorMessage
                : string.IsNullOrEmpty(_statusKey)
                    ? GetText("getting_ready")
                    : GetText(_statusKey);

            footer = failed
                ? GetText("retry")
                : "DownTrack";
        }

        SetWindowTextW(_statusHwnd, status);
        SetWindowTextW(_languageHwnd, language);
        SetWindowTextW(_footerHwnd, footer);

        SendMessageW(
            _progressHwnd,
            0x0402,
            (nint)Math.Clamp((int)Math.Round(progress), 0, 100),
            0);

        ShowWindow(_retryHwnd, failed ? 5 : 0);

        InvalidateRect(_hwnd, nint.Zero, 1);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmNCHitTest:
            {
                var x = (short)(long)lParam;
                var y = (short)((long)lParam >> 16);
                POINT point = new() { X = x, Y = y };
                ScreenToClient(hwnd, &point);

                if (point.Y <= 72)
                {
                    return HtCaption;
                }

                return HtClient;
            }

            case WmCommand:
            {
                var id = (ushort)((long)wParam & 0xFFFF);

                if (id == ControlClose)
                {
                    DestroyWindow(hwnd);
                    return nint.Zero;
                }

                if (id == ControlLanguage)
                {
                    ShowLanguageMenu(hwnd);
                    return nint.Zero;
                }

                if (id == ControlRetry)
                {
                    Retry();
                    return nint.Zero;
                }

                if (id is >= 1000 and < 2000)
                {
                    var index = id - 1000;
                    if (index < Languages.Length)
                    {
                        _language = Languages[index].Code == "auto"
                            ? DetectLanguage()
                            : Languages[index].Code;

                        RefreshControls();
                    }
                }

                return nint.Zero;
            }

            case 0x0138:
            {
                if (_smokeTest)
                {
                    return _mutedBrush;
                }

                var control = lParam;
                if (control == _brandHwnd)
                {
                    SetTextColor(wParam, ToColorRef(0xFFFFFFFF));
                    SetBkColor(wParam, ToColorRef(0xFF6659E8));
                    return _accentBrush;
                }

                SetTextColor(
                    wParam,
                    control == _subtitleHwnd || control == _footerHwnd
                        ? ToColorRef(0xFF7B8492)
                        : ToColorRef(0xFF1D2430));

                SetBkColor(wParam, ToColorRef(0xFFF8F9FC));
                return _mutedBrush;
            }

            case 0x0135:
            {
                if (_smokeTest)
                {
                    return _mutedBrush;
                }

                SetTextColor(wParam, ToColorRef(0xFF1D2430));
                SetBkColor(wParam, ToColorRef(0xFFF8F9FC));
                return _mutedBrush;
            }

            case WmAppUpdate:
                RefreshControls();
                return nint.Zero;

            case WmPaint:
            {
                PAINTSTRUCT ps;
                var hdc = BeginPaint(hwnd, &ps);
                EndPaint(hwnd, &ps);
                return nint.Zero;
            }

            case WmDestroy:
                PostQuitMessage(0);
                return nint.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static unsafe void ShowLanguageMenu(nint hwnd)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return;
        }

        try
        {
            for (var i = 0; i < Languages.Length; i++)
            {
                var item = Languages[i];
                AppendMenuW(menu, MFString, (nuint)(1000 + i), item.Name);
            }

            POINT point = new() { X = 320, Y = 54 };
            ClientToScreen(hwnd, &point);

            _ = TrackPopupMenu(
                menu,
                TpmReturnCmd | MFRightButton,
                point.X,
                point.Y,
                0,
                hwnd,
                nint.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static uint ToColorRef(uint argb)
    {
        var r = (argb >> 16) & 0xFF;
        var g = (argb >> 8) & 0xFF;
        var b = argb & 0xFF;
        return (b << 16) | (g << 8) | r;
    }

    private static unsafe void SetRoundCorners(nint hwnd)
    {
        try
        {
            var preference = DwmRound;
            DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
        }

        var region = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 24, 24);
        SetWindowRgn(hwnd, region, true);
    }

    private static void WriteText(string path, string content)
    {
        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class NativeBrush : IDisposable
    {
        public nint Handle { get; }

        private NativeBrush(nint handle) => Handle = handle;

        public static NativeBrush FromRgb(uint argb)
        {
            return new NativeBrush(
                CreateSolidBrush(ToColorRef(argb)));
        }

        public void Dispose()
        {
            DeleteObject(Handle);
        }
    }

    private sealed class NativePen : IDisposable
    {
        public nint Handle { get; }

        private NativePen(nint handle) => Handle = handle;

        public static NativePen FromRgb(uint argb)
        {
            return new NativePen(
                CreatePen(0, 1, ToColorRef(argb)));
        }

        public void Dispose()
        {
            DeleteObject(Handle);
        }
    }

    private sealed class NativeFont : IDisposable
    {
        public nint Handle { get; }

        private NativeFont(nint handle) => Handle = handle;

        public static unsafe NativeFont Create(int size, bool bold)
        {
            fixed (char* face = "Segoe UI Variable Text")
            {
                return new NativeFont(CreateFontW(
                    -size,
                    0,
                    0,
                    0,
                    bold ? 700 : 400,
                    0,
                    0,
                    0,
                    1,
                    0,
                    0,
                    5,
                    0,
                    face));
            }
        }

        public void Dispose()
        {
            DeleteObject(Handle);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 4
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("DownTrack-WebSetup/0.1");
        return client;
    }

    private struct POINT
    {
        public int X;
        public int Y;
    }

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public POINT Pt;
        public uint Private;
    }

    private unsafe struct PAINTSTRUCT
    {
        public nint Hdc;
        public int Erase;
        public RECT RcPaint;
        public int Restore;
        public int IncUpdate;
        public fixed byte Reserved[32];
    }

    private unsafe struct WNDCLASSW
    {
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe ushort RegisterClassW(WNDCLASSW* lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(
        nint hWnd,
        uint Msg,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern unsafe int GetMessageW(
        MSG* lpMsg,
        nint hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern unsafe nint DispatchMessageW(MSG* lpMsg);

    [DllImport("user32.dll")]
    private static extern unsafe int TranslateMessage(MSG* lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern nint PostMessageW(
        nint hWnd,
        uint Msg,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern int DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern int UpdateWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int InvalidateRect(
        nint hWnd,
        nint lpRect,
        int bErase);

    [DllImport("user32.dll")]
    private static extern unsafe int ScreenToClient(
        nint hWnd,
        POINT* lpPoint);

    [DllImport("user32.dll")]
    private static extern unsafe int ClientToScreen(
        nint hWnd,
        POINT* lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadCursorW(
        nint hInstance,
        nint lpCursorName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe nint BeginPaint(
        nint hWnd,
        PAINTSTRUCT* lpPaint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe int EndPaint(
        nint hWnd,
        PAINTSTRUCT* lpPaint);

    [DllImport("user32.dll")]
    private static extern unsafe int GetClientRect(
        nint hWnd,
        RECT* lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int TextOutW(
        nint hDC,
        int x,
        int y,
        string lpString,
        int c);

    [DllImport("user32.dll")]
    private static extern uint SetTextColor(
        nint hdc,
        uint colorRef);

    [DllImport("gdi32.dll")]
    private static extern uint SetBkColor(
        nint hdc,
        uint colorRef);

    [DllImport("user32.dll")]
    private static extern int SetBkMode(
        nint hdc,
        int mode);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint colorRef);

    [DllImport("gdi32.dll")]
    private static extern nint CreatePen(
        int style,
        int width,
        uint colorRef);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe nint CreateFontW(
        int cHeight,
        int cWidth,
        int cEscapement,
        int cOrientation,
        int cWeight,
        uint bItalic,
        uint bUnderline,
        uint bStrikeOut,
        uint iCharSet,
        uint iOutPrecision,
        uint iClipPrecision,
        uint iQuality,
        uint iPitchAndFamily,
        char* pszFaceName);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(
        nint hdc,
        nint hObject);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int fnObject);

    [DllImport("gdi32.dll")]
    private static extern int DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    private static extern int RoundRect(
        nint hdc,
        int left,
        int top,
        int right,
        int bottom,
        int width,
        int height);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int x1,
        int y1,
        int x2,
        int y2,
        int cx,
        int cy);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(
        nint hWnd,
        nint hRgn,
        bool bRedraw);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int size);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTextW(nint hWnd, string text);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    private static extern nint GetSysColorBrush(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int AppendMenuW(
        nint hMenu,
        uint uFlags,
        nuint uIdNewItem,
        string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        nint hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        nint hWnd,
        nint prcRect);

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandleW(nint lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserDefaultLocaleName(
        StringBuilder lpLocaleName,
        int cchLocaleName);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(
        nint pvReserved,
        int dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern unsafe int CoCreateInstance(
        Guid* rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        Guid* riid,
        out nint ppv);

    [DllImport("user32.dll")]
    private static extern unsafe int FillRect(
        nint hDC,
        RECT* lpRect,
        nint hBrush);
}
