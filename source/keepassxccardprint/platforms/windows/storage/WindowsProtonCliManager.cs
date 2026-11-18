using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KeePassXC.CloudTransport;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Platforms.Windows.Storage;

/// <summary>Windows-specific CLI discovery and verified installation.
/// Uses Proton's release index and its per-architecture SHA-512 only.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsProtonCliManager
{
    private static readonly Uri Index = new("https://proton.me/download/drive/cli/index.html");
    private static readonly SemaphoreSlim Operations = new(1, 1);
    private const string Executable = "proton-drive.exe";
    private static string ManagedPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "keepassxccardprint", "tools", Executable);

    internal static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath.Trim().Trim('"')));
        foreach (var directory in new[] { AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "tools"), Path.GetDirectoryName(ManagedPath)! })
        {
            var candidate = Path.Combine(directory, Executable);
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(
            Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var dir = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                if (!Path.IsPathFullyQualified(dir)) continue;
                var path = Path.Combine(dir, Executable);
                if (File.Exists(path)) return path;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { }
        }
        return ManagedPath;
    }

    private sealed record Release(Version Version, Uri Download, string Sha512);
    private static Release ParseIndex(string html, Architecture architecture)
    {
        var platform = architecture switch
        {
            Architecture.X64 => "windows-x64",
            Architecture.Arm64 => "windows-arm64",
            _ => throw new PlatformNotSupportedException("Proton CLI Windows architecture is unsupported.")
        };
        var text = WebUtility.HtmlDecode(Regex.Replace(html, @"<[^>]*>", " "));
        var versionMatch = Regex.Match(text,
            @"Proton\s+Drive\s+CLI\s+(?<v>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
        if (!versionMatch.Success || !Version.TryParse(versionMatch.Groups["v"].Value, out var version))
            throw new InvalidDataException("Invalid official Proton CLI release index.");
        foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>.*?</tr>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var rowText = WebUtility.HtmlDecode(Regex.Replace(row.Value, @"<[^>]*>", " "));
            if (!rowText.Contains(architecture == Architecture.X64 ? "windows/x64" : "windows/arm64",
                    StringComparison.OrdinalIgnoreCase)) continue;
            var link = Regex.Match(row.Value, "href\\s*=\\s*['\\\"](?<url>[^'\\\"]+)['\\\"]",
                RegexOptions.IgnoreCase);
            var digest = Regex.Match(rowText, @"(?<![0-9a-f])(?<hash>[0-9a-f]{128})(?![0-9a-f])",
                RegexOptions.IgnoreCase);
            if (!link.Success || !digest.Success) break;
            var url = new Uri(Index, WebUtility.HtmlDecode(link.Groups["url"].Value));
            var expected = $"/download/drive/cli/{version}/{platform}/proton-drive.exe";
            if (url.Scheme != Uri.UriSchemeHttps || url.Host != "proton.me" || url.Port != 443 ||
                url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
                url.AbsolutePath != expected)
                throw new InvalidDataException("Unexpected Proton CLI download address.");
            return new Release(version, url, digest.Groups["hash"].Value.ToUpperInvariant());
        }
        throw new InvalidDataException("No matching signed-hash Proton CLI release found.");
    }
    private static async Task<Release> LatestAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        using var response = await http.GetAsync(Index, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1_000_000)
            throw new InvalidDataException("Proton release index is too large.");
        var html = await response.Content.ReadAsStringAsync(ct);
        if (html.Length > 1_000_000) throw new InvalidDataException("Proton release index is too large.");
        return ParseIndex(html, RuntimeInformation.ProcessArchitecture);
    }
    private static async Task<Version?> VersionAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("version");
        process.StartInfo.Environment["PROTON_DRIVE_LOG_LEVEL"] = "ERROR";
        if (!process.Start()) throw new IOException("Proton CLI did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var result = (await stdout) + " " + (await stderr);
            var found = Regex.Match(result, @"(?<!\d)(\d+\.\d+\.\d+)(?!\d)");
            if (process.ExitCode != 0 || !found.Success ||
                !Version.TryParse(found.Groups[1].Value, out var version))
                throw new InvalidDataException("Proton CLI did not report a valid version.");
            return version;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
    internal static async Task<ProtonCliStatus> CheckAsync(string? configured, CancellationToken ct)
    {
        var path = Resolve(configured);
        if (!File.Exists(path)) return new ProtonCliStatus("missing", Path: path);
        try
        {
            var installed = await VersionAsync(path, ct);
            var latest = await LatestAsync(ct);
            return new ProtonCliStatus(latest.Version > installed ? "available" : "current",
                installed?.ToString() ?? "", latest.Version.ToString(), path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { return new ProtonCliStatus("error", Path: path, Error: e.Message); }
    }
    internal static async Task<string> InstallAsync(string? configured, IProgress<int>? progress,
        CancellationToken ct)
    {
        await Operations.WaitAsync(ct);
        string? temp = null;
        try
        {
            var release = await LatestAsync(ct);
            var path = Resolve(configured);
            // Never overwrite a binary found on PATH (or next to a different app)
            // without explicit selection. Use this app's managed tools folder.
            if (string.IsNullOrWhiteSpace(configured)) path = ManagedPath;
            var current = await VersionAsync(path, ct);
            if (current is not null && current >= release.Version) return path;
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            temp = Path.Combine(folder, ".proton-cli-" + Guid.NewGuid().ToString("N") + ".exe");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(12) })
            using (var response = await http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                const long maxBytes = 450L * 1024 * 1024;
                if (response.Content.Headers.ContentLength is > maxBytes)
                    throw new InvalidDataException("Proton CLI download is too large.");
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 131072, FileOptions.Asynchronous);
                var buffer = new byte[131072]; long count = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    count += read;
                    if (count > maxBytes) throw new InvalidDataException("Proton CLI download is too large.");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report(response.Content.Headers.ContentLength is long total && total > 0
                        ? (int)Math.Clamp(count * 100 / total, 0, 100) : -1);
                }
                await target.FlushAsync(ct);
            }
            await using (var file = File.OpenRead(temp))
            {
                var digest = Convert.ToHexString(await SHA512.HashDataAsync(file, ct));
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(digest),
                    Convert.FromHexString(release.Sha512)))
                    throw new InvalidDataException("Proton CLI SHA-512 verification failed.");
            }
            if (await VersionAsync(temp, ct) != release.Version)
                throw new InvalidDataException("Downloaded Proton CLI version mismatch.");
            // Never replace an executable while the transport is using it.
            IDisposable? lease = null;
            for (var i = 0; i < 120 && lease is null; i++)
            {
                lease = ProtonCliLease.TryBeginUpdate();
                if (lease is null) await Task.Delay(500, ct);
            }
            if (lease is null) throw new TimeoutException("Proton CLI is in use; update postponed.");
            using (lease)
            {
                var backup = path + ".previous." + Guid.NewGuid().ToString("N");
                if (File.Exists(path))
                {
                    File.Replace(temp, path, backup);
                    temp = null;
                    try
                    {
                        if (await VersionAsync(path, CancellationToken.None) != release.Version)
                            throw new InvalidDataException("Installed Proton CLI did not pass version check.");
                    }
                    catch { File.Replace(backup, path, null); throw; }
                    try { File.Delete(backup); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                else
                {
                    File.Move(temp, path);
                    temp = null;
                    try
                    {
                        if (await VersionAsync(path, CancellationToken.None) != release.Version)
                            throw new InvalidDataException("Installed Proton CLI did not pass version check.");
                    }
                    catch { File.Delete(path); throw; }
                }
            }
            return path;
        }
        finally
        {
            if (temp is not null) try { File.Delete(temp); } catch (IOException) { }
            Operations.Release();
        }
    }
}
