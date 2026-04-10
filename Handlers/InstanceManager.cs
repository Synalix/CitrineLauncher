using CitrineLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    public class InstanceManager
    {
        public static readonly InstanceManager Instance = new InstanceManager();

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
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "CitrineLauncher/1.0");
            var json = await http.GetStringAsync("https://meta.fabricmc.net/v2/versions/loader");
            using var doc = JsonDocument.Parse(json);
            // Array of loader versions; first is latest
            var first = doc.RootElement.EnumerateArray().First();
            return first.GetProperty("version").GetString() ?? string.Empty;
        }

        // Returns Fabric loader versions compatible with a given game version
        public static async Task<List<string>> GetFabricLoaderVersionsAsync(string gameVersion)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "CitrineLauncher/1.0");
            var json = await http.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}");
            using var doc = JsonDocument.Parse(json);
            var versions = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var v = el.GetProperty("loader").GetProperty("version").GetString();
                if (!string.IsNullOrEmpty(v)) versions.Add(v);
            }
            return versions;
        }

        // Downloads and installs the Fabric profile JSON into the instance's minecraft path
        public static async Task InstallFabricAsync(string gameVersion, string loaderVersion, string minecraftPath)
        {
            // Fabric provides a ready-made version JSON via its meta API
            var url = $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "CitrineLauncher/1.0");
            var profileJson = await http.GetStringAsync(url);

            // The version id from the profile JSON
            using var doc = JsonDocument.Parse(profileJson);
            var versionId = doc.RootElement.GetProperty("id").GetString()!;

            // Write profile JSON to <minecraftPath>/versions/<versionId>/<versionId>.json
            var versionDir = Path.Combine(minecraftPath, "versions", versionId);
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, $"{versionId}.json"), profileJson);
        }
    }
}
