using CitrineLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    public class InstanceManager
    {
        public static readonly InstanceManager Instance = new InstanceManager();

        private static readonly HttpClient _http = new HttpClient
        {
            DefaultRequestHeaders = { { "User-Agent", "CitrineLauncher/1.0" } },
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Guards concurrent Fabric profile writes to prevent TOCTOU races
        private static readonly SemaphoreSlim _fabricInstallLock = new(1, 1);

        private static string InstancesRoot => Path.Combine(
            Settings.Instance.MinecraftPath, "instances");

        private readonly List<GameInstance> _instances = new();
        public IReadOnlyList<GameInstance> Instances => _instances;

        private InstanceManager() { }

        // Call once at startup
        public void Load()
        {
            _instances.Clear();

            Directory.CreateDirectory(InstancesRoot);

            foreach (var dir in Directory.GetDirectories(InstancesRoot))
            {
                var metaPath = Path.Combine(dir, "instance.json");
                if (!File.Exists(metaPath)) continue;

                try
                {
                    var json = File.ReadAllText(metaPath);
                    var inst = JsonSerializer.Deserialize<GameInstance>(json);
                    if (inst != null)
                    {
                        inst.InstanceDirectory = dir;
                        _instances.Add(inst);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"InstanceManager: failed to load {metaPath}: {ex.Message}");
                }
            }

            // Migration: if no instances exist but a last-played version is saved, create a default vanilla one
            if (_instances.Count == 0)
            {
                var lastVersion = Settings.Instance.LastVersion;
                if (!string.IsNullOrEmpty(lastVersion))
                {
                    var migrated = CreateInstance("Default", lastVersion, LoaderType.Vanilla, string.Empty);
                    System.Diagnostics.Debug.WriteLine($"InstanceManager: migrated vanilla instance '{lastVersion}'");
                }
            }
        }

        public GameInstance CreateInstance(string name, string gameVersion, LoaderType loader, string loaderVersion)
        {
            var inst = new GameInstance
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                GameVersion = gameVersion,
                Loader = loader,
                LoaderVersion = loaderVersion
            };

            var dir = Path.Combine(InstancesRoot, inst.Id);
            Directory.CreateDirectory(dir);
            inst.InstanceDirectory = dir;

            Save(inst);
            _instances.Add(inst);
            return inst;
        }

        public void DeleteInstance(GameInstance inst)
        {
            _instances.Remove(inst);
            if (Directory.Exists(inst.InstanceDirectory))
                Directory.Delete(inst.InstanceDirectory, recursive: true);
        }

        public void RenameInstance(GameInstance inst, string newName)
        {
            inst.Name = newName;
            Save(inst);
        }

        private void Save(GameInstance inst)
        {
            var metaPath = Path.Combine(inst.InstanceDirectory, "instance.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(inst, options));
        }

        // Returns latest stable Fabric loader version from Fabric meta API
        public static async Task<string> GetLatestFabricLoaderVersionAsync()
        {
            try
            {
                var json = await _http.GetStringAsync("https://meta.fabricmc.net/v2/versions/loader");
                using var doc = JsonDocument.Parse(json);
                var first = doc.RootElement.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Undefined)
                    throw new InvalidOperationException("Fabric API returned empty version list");
                return first.TryGetProperty("version", out var ver) ? ver.GetString() ?? string.Empty : string.Empty;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Failed to fetch Fabric versions: {ex.Message}", ex);
            }
        }

        // Returns Fabric loader versions compatible with a given game version
        public static async Task<List<string>> GetFabricLoaderVersionsAsync(string gameVersion)
        {
            try
            {
                var json = await _http.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}");
                using var doc = JsonDocument.Parse(json);
                var versions = new List<string>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (!el.TryGetProperty("loader", out var loader)) continue;
                    var v = loader.TryGetProperty("version", out var ver) ? ver.GetString() : null;
                    if (!string.IsNullOrEmpty(v)) versions.Add(v);
                }
                return versions;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Failed to fetch Fabric versions for {gameVersion}: {ex.Message}", ex);
            }
        }

        // Downloads and installs the Fabric profile JSON into the instance's minecraft path
        // Safe for concurrent calls - checks if already installed first
        public static async Task InstallFabricAsync(string gameVersion, string loaderVersion, string minecraftPath)
        {
            // Resolve the version ID first to check if already installed
            string profileJson;
            string versionId;
            try
            {
                var url = $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
                profileJson = await _http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(profileJson);
                if (!doc.RootElement.TryGetProperty("id", out var idProp))
                    throw new InvalidOperationException($"Fabric profile JSON missing required 'id' field for game {gameVersion}");
                versionId = idProp.GetString()!;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Failed to fetch Fabric profile from meta.fabricmc.net: {ex.Message}", ex);
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse Fabric profile: {ex.Message}", ex);
            }

            // Atomically check-and-install to prevent TOCTOU races between concurrent calls
            var versionFile = Path.Combine(minecraftPath, "versions", versionId, $"{versionId}.json");
            await _fabricInstallLock.WaitAsync();
            try
            {
                if (File.Exists(versionFile))
                    return;

                var versionDir = Path.Combine(minecraftPath, "versions", versionId);
                Directory.CreateDirectory(versionDir);
                File.WriteAllText(versionFile, profileJson);
            }
            finally
            {
                _fabricInstallLock.Release();
            }
        }
    }
}
