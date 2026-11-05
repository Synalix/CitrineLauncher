using CitrineLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers;

/// <summary>Rebuilds the required dependency closure when exact constraints replace provisional versions.</summary>
public sealed class ModDependencyResolver(ModrinthApi api)
{
    public async Task<Dictionary<string, ModrinthVersion>> ResolveAsync(GameInstance instance, ModrinthVersion selected,
        IReadOnlyList<InstalledMod> installed, CancellationToken ct)
    {
        var versions = new Dictionary<string, ModrinthVersion>();
        var projectVersions = new Dictionary<string, List<ModrinthVersion>>();
        var preferredPins = new Dictionary<string, string>();
        async Task<ModrinthVersion> ById(string id)
        {
            if (!versions.TryGetValue(id, out var value)) versions[id] = value = await api.GetVersionAsync(id, ct);
            return value;
        }
        versions[selected.VersionId] = selected;
        // Each pass discards the previous closure, including dependencies of replaced provisional versions.
        for (int pass = 0; pass < 64; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var resolved = new Dictionary<string, ModrinthVersion>();
            var constraints = new Dictionary<string, HashSet<string>>();
            void Pin(string project, string version)
            {
                if (!constraints.TryGetValue(project, out var ids)) constraints[project] = ids = new();
                ids.Add(version);
            }
            Pin(selected.ProjectId, selected.VersionId);
            async Task Visit(ModrinthVersion version)
            {
                if (resolved.ContainsKey(version.ProjectId)) return;
                if (!version.GameVersions.Contains(instance.GameVersion) || !version.Loaders.Contains("fabric"))
                    throw new InvalidDataException($"{version.VersionNumber} does not support Minecraft {instance.GameVersion} / Fabric.");
                if (resolved.Count >= 256) throw new InvalidDataException("Too many required dependencies.");
                resolved.Add(version.ProjectId, version);
                foreach (var dep in version.Dependencies.Where(d => d.Type == "required"))
                {
                    ModrinthVersion dependency;
                    if (dep.VersionId != null)
                    {
                        dependency = await ById(dep.VersionId);
                        if (dep.ProjectId != null && dep.ProjectId != dependency.ProjectId)
                            throw new InvalidDataException("Dependency version belongs to a different project.");
                        Pin(dependency.ProjectId, dependency.VersionId);
                    }
                    else if (dep.ProjectId != null)
                    {
                        if (preferredPins.TryGetValue(dep.ProjectId, out var pin)) dependency = await ById(pin);
                        else if (resolved.TryGetValue(dep.ProjectId, out var seen)) dependency = seen;
                        else
                        {
                            var current = installed.FirstOrDefault(m => m.Enabled && m.ProjectId == dep.ProjectId && m.VersionId != null);
                            if (current != null) dependency = await ById(current.VersionId!);
                            else
                            {
                                if (!projectVersions.TryGetValue(dep.ProjectId, out var candidates))
                                    projectVersions[dep.ProjectId] = candidates = await api.GetVersionsAsync(dep.ProjectId, instance.GameVersion, "mod", "fabric", ct);
                                dependency = candidates.FirstOrDefault() ?? throw new InvalidDataException("No compatible required dependency: " + dep.ProjectId);
                            }
                        }
                    }
                    else throw new InvalidDataException("Required dependency has no project or version.");
                    // Pins learned on a previous pass take precedence until this pass recalculates its constraints.
                    if (preferredPins.TryGetValue(dependency.ProjectId, out var preferred)) dependency = await ById(preferred);
                    await Visit(dependency);
                }
            }
            await Visit(selected);
            var changes = constraints.Where(c => c.Value.Count == 1 && resolved[c.Key].VersionId != c.Value.Single()).ToList();
            if (changes.Count == 0)
            {
                var conflict = constraints.FirstOrDefault(c => c.Value.Count > 1);
                if (conflict.Value != null) throw new InvalidDataException("Conflicting exact required versions for " + conflict.Key);
                return resolved;
            }
            preferredPins = constraints.Where(c => c.Value.Count == 1).ToDictionary(c => c.Key, c => c.Value.Single());
        }
        throw new InvalidDataException("Dependency constraints did not converge. Select a different mod version.");
    }
}
