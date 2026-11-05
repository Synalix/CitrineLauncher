using CitrineLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers;

public record PackFile(string Path, string[] Downloads, string? Sha512, string? Sha1, bool Optional);
public record PackManifest(string Name, string GameVersion, LoaderType Loader, string LoaderVersion, List<PackFile> Files)
{
    public static PackManifest Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1 || root.GetProperty("game").GetString() != "minecraft")
            throw new InvalidDataException("Only Minecraft Modrinth format version 1 is supported.");
        var deps = root.GetProperty("dependencies");
        foreach (var dep in deps.EnumerateObject())
            if (dep.Name is not ("minecraft" or "fabric-loader"))
                throw new InvalidDataException($"This pack needs {dep.Name}. Citrine currently supports Vanilla and Fabric packs.");
        var mc = deps.GetProperty("minecraft").GetString();
        if (string.IsNullOrWhiteSpace(mc)) throw new InvalidDataException("Pack has no Minecraft version.");
        var fabric = deps.TryGetProperty("fabric-loader", out var fv) ? fv.GetString() : null;
        if (fabric != null && string.IsNullOrWhiteSpace(fabric)) throw new InvalidDataException("Pack has no Fabric loader version.");
        var files = new List<PackFile>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root.GetProperty("files").EnumerateArray())
        {
            if (entry.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var client) && client.GetString() == "unsupported") continue;
            var path = entry.GetProperty("path").GetString()!;
            ValidateContentPath(path);
            if (!paths.Add(path.Replace('\\', '/'))) throw new InvalidDataException("Duplicate pack path: " + path);
            var hashes = entry.GetProperty("hashes");
            var urls = entry.GetProperty("downloads").EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (urls.Length == 0) throw new InvalidDataException("Missing download URL: " + path);
            var sha512 = hashes.TryGetProperty("sha512", out var h512) ? h512.GetString() : null;
            var sha1 = hashes.TryGetProperty("sha1", out var h1) ? h1.GetString() : null;
            if (string.IsNullOrEmpty(sha512) && string.IsNullOrEmpty(sha1)) throw new InvalidDataException("Missing file checksum: " + path);
            var optional = entry.TryGetProperty("env", out env) && env.TryGetProperty("client", out client) && client.GetString() == "optional";
            files.Add(new(path, urls, sha512, sha1, optional));
        }
        return new(root.TryGetProperty("name", out var name) ? name.GetString() ?? "Modpack" : "Modpack", mc, fabric == null ? LoaderType.Vanilla : LoaderType.Fabric, fabric ?? "", files);
    }
    public static void ValidateContentPath(string path)
    {
        VerifiedDownload.ContainedPath(System.IO.Path.GetTempPath(), path);
        var first = path.Replace('\\', '/').Split('/')[0];
        if (first.Equals("instance.json", StringComparison.OrdinalIgnoreCase) || first.Equals("pack-meta.json", StringComparison.OrdinalIgnoreCase) || first.Equals("citrine-mods.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pack cannot replace launcher metadata.");
    }
    public static async Task<PackManifest> ReadArchiveAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("modrinth.index.json");
        if (entry == null) throw new InvalidDataException("Select a Modrinth .mrpack containing modrinth.index.json. CurseForge downloads are not supported.");
        using var reader = new StreamReader(entry.Open());
        return Parse(await reader.ReadToEndAsync(ct));
    }
}

public static class ModpackImporter
{
    public record ImportResult(bool Success, string Message, string? DetectedGameVersion = null);
    public static Task<ImportResult> ImportFromZipAsync(string path, GameInstance target, CancellationToken ct = default, IProgress<string>? progress = null, bool includeOptional = true) =>
        new PackInstaller(ModrinthClient.Http).ImportAsync(path, target, ct, progress, includeOptional);
}

/// <summary>Imports a complete client pack into a fresh instance using a staging directory.</summary>
public sealed class PackInstaller(HttpClient http)
{
    public async Task<ModpackImporter.ImportResult> ImportAsync(string path, GameInstance target, CancellationToken ct = default, IProgress<string>? progress = null, bool includeOptional = true)
    {
        var stage = target.InstanceDirectory + ".import-" + Guid.NewGuid().ToString("N");
        var backup = stage + ".previous";
        var published = false;
        try
        {
            var manifest = await PackManifest.ReadArchiveAsync(path, ct);
            if (manifest.GameVersion != target.GameVersion || manifest.Loader != target.Loader || manifest.LoaderVersion != target.LoaderVersion)
                throw new InvalidDataException("Instance Minecraft/loader version must match the pack manifest.");
            if (Directory.Exists(target.InstanceDirectory) && Directory.EnumerateFileSystemEntries(target.InstanceDirectory).Any(p => System.IO.Path.GetFileName(p) != "instance.json"))
                throw new InvalidDataException("Import requires a new, empty instance.");
            Directory.CreateDirectory(stage);
            var files = manifest.Files.Where(f => includeOptional || !f.Optional).ToList();
            var completed = 0;
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (file, token) =>
            {
                await VerifiedDownload.FetchAsync(http, file.Downloads, VerifiedDownload.ContainedPath(stage, file.Path), file.Sha512, file.Sha1, token);
                var count = Interlocked.Increment(ref completed);
                progress?.Report($"Downloaded {count}/{files.Count}: {file.Path}");
            });
            using (var zip = ZipFile.OpenRead(path))
                foreach (var prefix in new[] { "overrides/", "client-overrides/" })
                    foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
                    {
                        ct.ThrowIfCancellationRequested();
                        var relative = entry.FullName[prefix.Length..]; PackManifest.ValidateContentPath(relative);
                        if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("Pack contains a symbolic link.");
                        var destination = VerifiedDownload.ContainedPath(stage, relative);
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                        await using var input = entry.Open(); await using var output = File.Create(destination);
                        await input.CopyToAsync(output, ct);
                    }
            await File.WriteAllTextAsync(System.IO.Path.Combine(stage, "pack-meta.json"), JsonSerializer.Serialize(new { format = "modrinth", manifest.Name, manifest.GameVersion, manifest.LoaderVersion }), ct);
            var metadata = System.IO.Path.Combine(target.InstanceDirectory, "instance.json");
            if (File.Exists(metadata)) File.Copy(metadata, System.IO.Path.Combine(stage, "instance.json"));
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(target.InstanceDirectory)) Directory.Move(target.InstanceDirectory, backup);
            try { Directory.Move(stage, target.InstanceDirectory); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, target.InstanceDirectory); throw; }
            published = true;
            return new(true, $"Installed {manifest.Name}.", manifest.GameVersion);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(false, ex.Message); }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            if (published && Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }
}
