using CitrineLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers;

/// <summary>Per-instance mod files; stages complete dependency graphs before publishing any JAR.</summary>
public sealed class InstanceModManager(ModrinthApi api, HttpClient http)
{
    public static InstanceModManager Shared { get; } = new(ModrinthClient.Api, ModrinthClient.Http);
    private static string ModsRoot(GameInstance instance) => Path.Combine(instance.InstanceDirectory, "mods");
    private static string MetadataPath(GameInstance instance) => VerifiedDownload.ContainedPath(instance.InstanceDirectory, "citrine-mods.json");
    private static Dictionary<string, InstalledMod> ReadMetadata(GameInstance instance)
    {
        var path = MetadataPath(instance);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, InstalledMod>>(File.ReadAllText(path)) ?? new()
            : new();
    }
    private static void WriteMetadata(GameInstance instance, Dictionary<string, InstalledMod> data)
    {
        var path = MetadataPath(instance); var temp = path + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public List<InstalledMod> List(GameInstance instance)
    {
        var metadata = ReadMetadata(instance); var root = ModsRoot(instance);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var name = Path.GetFileName(p); var enabled = !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var canonical = enabled ? name : name[..^9];
                return metadata.TryGetValue(canonical, out var entry) ? entry with { FileName = name, Enabled = enabled } : new InstalledMod(name, canonical, enabled, null, null);
            }).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    private static string ModPath(GameInstance instance, string name)
    {
        if (name.Contains('/') || name.Contains('\\') || !(name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Select a JAR in this instance's mods folder.");
        return VerifiedDownload.ContainedPath(instance.InstanceDirectory, "mods/" + name);
    }
    private static void RequireFabric(GameInstance instance)
    {
        if (instance.Loader != LoaderType.Fabric) throw new InvalidOperationException("This instance uses Vanilla. Create a Fabric instance to install mods.");
    }
    public void SetEnabled(GameInstance instance, InstalledMod mod, bool enabled)
    {
        var path = ModPath(instance, mod.FileName);
        var next = enabled ? (mod.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? mod.FileName[..^9] : mod.FileName) : mod.FileName + ".disabled";
        if (mod.Enabled == enabled) return;
        File.Move(path, ModPath(instance, next));
    }
    public void Remove(GameInstance instance, InstalledMod mod)
    {
        var path = ModPath(instance, mod.FileName);
        // Metadata is advisory; a stale entry cannot make an absent file appear in the list.
        var metadata = ReadMetadata(instance);
        File.Delete(path);
        metadata.Remove(mod.Enabled ? mod.FileName : mod.FileName[..^9]); WriteMetadata(instance, metadata);
    }
    public async Task AddLocalAsync(GameInstance instance, IEnumerable<string> paths, CancellationToken ct = default)
    {
        RequireFabric(instance);
        var stage = instance.InstanceDirectory + ".mods-" + Guid.NewGuid().ToString("N");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(stage);
            foreach (var source in paths)
            {
                ct.ThrowIfCancellationRequested(); var name = Path.GetFileName(source);
                if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select .jar mod files.");
                ModPath(instance, name);
                if (!names.Add(name)) throw new IOException("Duplicate mod filename: " + name);
                await using var input = File.OpenRead(source); await using var output = File.Create(VerifiedDownload.ContainedPath(stage, name));
                await input.CopyToAsync(output, ct);
            }
            Publish(instance, stage, names.ToList(), ReadMetadata(instance), ct);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public async Task InstallAsync(GameInstance instance, ModrinthVersion selected, CancellationToken ct = default, IProgress<string>? progress = null)
    {
        RequireFabric(instance);
        var installed = List(instance);
        var resolved = await new ModDependencyResolver(api).ResolveAsync(instance, selected, installed, ct);
        var finalVersions = installed.Where(m => m.Enabled).Select(m => (m.ProjectId, m.VersionId))
            .Concat(resolved.Values.Select(v => (ProjectId: (string?)v.ProjectId, VersionId: (string?)v.VersionId))).ToList();
        static bool Conflicts(ModIncompatibility dependency, string? project, string? id) =>
            (dependency.ProjectId == null || dependency.ProjectId == project) &&
            (dependency.VersionId == null || dependency.VersionId == id) &&
            (dependency.ProjectId != null || dependency.VersionId != null);
        static ModIncompatibility[] Incompatibilities(ModrinthVersion version) => version.Dependencies
            .Where(d => d.Type == "incompatible").Select(d => new ModIncompatibility(d.ProjectId, d.VersionId)).ToArray();
        void Check(string name, ModIncompatibility[] dependencies)
        {
            if (dependencies.Any(d => finalVersions.Any(v => Conflicts(d, v.ProjectId, v.VersionId))))
                throw new InvalidDataException("An installed or required mod is incompatible with " + name + ". Disable or remove the conflicting mod first.");
        }
        foreach (var version in resolved.Values) Check(version.FileName, Incompatibilities(version));
        foreach (var mod in installed.Where(m => m.Enabled && m.VersionId != null))
        {
            // Earlier metadata may lack declarations. Retrieve them instead of assuming compatibility.
            var dependencies = mod.Incompatibilities ?? Incompatibilities(await api.GetVersionAsync(mod.VersionId!, ct));
            Check(mod.Name, dependencies);
        }
        var stage = instance.InstanceDirectory + ".mods-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage); var metadata = ReadMetadata(instance); var names = new List<string>();
            foreach (var version in resolved.Values)
            {
                var existing = installed.FirstOrDefault(m => m.ProjectId == version.ProjectId);
                if (existing != null && existing.VersionId != version.VersionId)
                    throw new InvalidDataException($"{existing.Name} is already installed with a different version. Remove it before changing versions.");
                if (existing != null && !existing.Enabled) throw new InvalidDataException($"Enable {existing.Name} before installing this mod.");
                if (existing != null && await VerifiedDownload.MatchesAsync(ModPath(instance, existing.FileName), version.Sha512, version.Sha1, ct)) continue;
                ModPath(instance, version.FileName);
                if (names.Contains(version.FileName, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Dependencies use the same filename.");
                progress?.Report("Downloading " + version.FileName);
                await VerifiedDownload.FetchAsync(http, [version.DownloadUrl], VerifiedDownload.ContainedPath(stage, version.FileName), version.Sha512, version.Sha1, ct);
                names.Add(version.FileName);
                metadata[version.FileName] = new(version.FileName, version.FileName, true, version.ProjectId, version.VersionId, Incompatibilities(version));
            }
            Publish(instance, stage, names, metadata, ct);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    private static void Publish(GameInstance instance, string stage, List<string> names, Dictionary<string, InstalledMod> metadata, CancellationToken ct)
    {
        // Validate the whole write set before moving anything. Disabled variants count as collisions.
        var existing = Directory.Exists(ModsRoot(instance)) ? Directory.GetFiles(ModsRoot(instance)).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
            if (existing.Contains(name) || existing.Contains(name + ".disabled")) throw new IOException($"{name} already exists. Remove the existing mod first.");
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ModsRoot(instance)); var moved = new List<string>();
        try
        {
            foreach (var name in names)
            {
                ct.ThrowIfCancellationRequested(); var path = ModPath(instance, name);
                File.Move(VerifiedDownload.ContainedPath(stage, name), path); moved.Add(path);
            }
            WriteMetadata(instance, metadata);
        }
        catch { foreach (var path in moved) File.Delete(path); throw; }
    }
}
