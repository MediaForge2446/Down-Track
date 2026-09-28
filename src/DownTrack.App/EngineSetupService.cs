using System.Net.Http;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DownTrack;

public sealed class EngineSetupService
{
    public string EngineFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DownTrack", "bin");

    const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    const string YtDlpChecksumsUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS";
    const string FfmpegZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    const string FfmpegChecksumsUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256";
    const string DenoZipUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
    const string DenoChecksumsUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip.sha256sum";

    readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task EnsureAsync(IProgress<string>? status, IProgress<double>? progress, bool forceUpdate = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(EngineFolder);
        var yt = Path.Combine(EngineFolder, "yt-dlp.exe");
        var ffmpeg = Path.Combine(EngineFolder, "ffmpeg.exe");
        var ffprobe = Path.Combine(EngineFolder, "ffprobe.exe");
        var deno = Path.Combine(EngineFolder, "deno.exe");

        if (forceUpdate || !File.Exists(yt))
        {
            status?.Report("Setting up yt-dlp…");
            await DownloadVerifiedAsync(YtDlpUrl, YtDlpChecksumsUrl, yt, "yt-dlp.exe", progress, cancellationToken);
        }

        if (forceUpdate || !File.Exists(ffmpeg) || !File.Exists(ffprobe))
        {
            status?.Report("Setting up FFmpeg…");
            var zip = Path.Combine(Path.GetTempPath(), "DownTrack-ffmpeg.zip");
            try
            {
                await DownloadVerifiedAsync(FfmpegZipUrl, FfmpegChecksumsUrl, zip, "ffmpeg-release-essentials.zip", progress, cancellationToken);
                status?.Report("Installing FFmpeg…");
                var extract = Path.Combine(Path.GetTempPath(), "DownTrack-ffmpeg-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extract);
                ZipFile.ExtractToDirectory(zip, extract, true);

                var foundFfmpeg = Directory.EnumerateFiles(extract, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                var foundFfprobe = Directory.EnumerateFiles(extract, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (foundFfmpeg is null || foundFfprobe is null)
                    throw new InvalidOperationException("The FFmpeg package did not contain the required binaries.");

                File.Copy(foundFfmpeg, ffmpeg, true);
                File.Copy(foundFfprobe, ffprobe, true);
                Directory.Delete(extract, true);
            }
            finally
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch { }
            }
        }

        if (forceUpdate || !File.Exists(deno))
        {
            status?.Report("Setting up Deno…");
            var zip = Path.Combine(Path.GetTempPath(), "DownTrack-deno.zip");
            try
            {
                await DownloadVerifiedAsync(DenoZipUrl, DenoChecksumsUrl, zip, "deno-x86_64-pc-windows-msvc.zip", progress, cancellationToken);
                var extract = Path.Combine(Path.GetTempPath(), "DownTrack-deno-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extract);
                ZipFile.ExtractToDirectory(zip, extract, true);
                var found = Directory.EnumerateFiles(extract, "deno.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found is null) throw new InvalidOperationException("The Deno package did not contain deno.exe.");
                File.Copy(found, deno, true);
                Directory.Delete(extract, true);
            }
            finally { try { if (File.Exists(zip)) File.Delete(zip); } catch { } }
        }

        await File.WriteAllTextAsync(Path.Combine(EngineFolder, "engine.ready"), DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
        status?.Report("Media engine ready.");
        progress?.Report(1);
    }

    async Task DownloadVerifiedAsync(string url, string checksumUrl, string destination, string fileName, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var temp = destination + ".download";
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(temp);
            var buffer = new byte[1024 * 64];
            long readTotal = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;
                if (total > 0) progress?.Report(Math.Clamp((double)readTotal / total, 0, 1));
            }
            await output.FlushAsync(cancellationToken);

            using var sha = SHA256.Create();
            await using var verifyStream = File.OpenRead(temp);
            var actual = Convert.ToHexString(await sha.ComputeHashAsync(verifyStream, cancellationToken)).ToLowerInvariant();
            var checksums = await http.GetStringAsync(checksumUrl, cancellationToken);
            var match = Regex.Match(checksums, $@"(?i)([a-f0-9]{{64}})s+*?{Regex.Escape(fileName)}(?:s|$)");
            if (!match.Success || !string.Equals(actual, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Checksum verification failed for {fileName}.");

            File.Move(temp, destination, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
