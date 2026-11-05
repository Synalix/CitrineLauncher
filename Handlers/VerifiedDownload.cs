using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CitrineLauncher.Handlers;

/// <summary>Downloads into a sibling temporary file; publishes only after checksum verification.</summary>
public static class VerifiedDownload
{
    public static string ContainedPath(string root, string relative)
    {
        var normalized = relative.Replace('\\', '/');
        var parts = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(relative) || normalized.StartsWith('/') || parts.Any(p =>
            string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') ||
            p.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || p.Any(char.IsControl) ||
            IsReserved(p.Split('.')[0])))
            throw new InvalidDataException($"Unsafe file path: {relative}");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!path.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("File path escapes the instance folder.");
        // Existing junctions/symlinks must not redirect writes outside the instance.
        for (var current = Path.GetDirectoryName(path); current != null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked directories are not supported for mod installation.");
            if (Path.GetFullPath(current) == Path.GetFullPath(root)) break;
        }
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked files are not supported for mod installation.");
        return path;
    }

    private static bool IsReserved(string name) => name.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or
        "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
        "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";

    public static async Task<bool> MatchesAsync(string path, string? sha512, string? sha1, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return false;
        if (string.IsNullOrEmpty(sha512) && string.IsNullOrEmpty(sha1)) throw new InvalidDataException("Download has no checksum.");
        await using var stream = File.OpenRead(path);
        var actual = !string.IsNullOrEmpty(sha512) ? await SHA512.HashDataAsync(stream, ct) : await SHA1.HashDataAsync(stream, ct);
        return Convert.ToHexString(actual).Equals(sha512 ?? sha1, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task FetchAsync(HttpClient http, IEnumerable<string> urls, string destination,
        string? sha512, string? sha1, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        Exception? last = null;
        try
        {
            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                        throw new InvalidDataException("Downloads require an HTTPS URL.");
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    await using (var input = await response.Content.ReadAsStreamAsync(ct))
                    await using (var output = File.Create(temp))
                    {
                        var buffer = new byte[81920]; long count = 0; int read;
                        while ((read = await input.ReadAsync(buffer, ct)) != 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), ct); count += read;
                            if (response.Content.Headers.ContentLength is > 0 and var total)
                                progress?.Report((int)Math.Min(100, count * 100 / total));
                        }
                    }
                    if (!await MatchesAsync(temp, sha512, sha1, ct)) throw new InvalidDataException("Downloaded file checksum does not match.");
                    ct.ThrowIfCancellationRequested();
                    File.Move(temp, destination, overwrite: true);
                    progress?.Report(100);
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException && !ct.IsCancellationRequested) { last = ex; }
            }
            throw new InvalidDataException("No verified download was available. " + last?.Message, last);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
