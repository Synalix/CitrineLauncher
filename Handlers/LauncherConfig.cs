using System;
using System.IO;
using System.Text.Json;

namespace CitrineLauncher.Handlers
{
    public static class LauncherConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CitrineLauncher",
            "launcher.json"
        );

        public class ConfigData
        {
            public string LastMinecraftPath { get; set; } = string.Empty;
        }

        public static ConfigData Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<ConfigData>(json) ?? new ConfigData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load launcher config: {ex.Message}");
            }
            return new ConfigData();
        }

        public static void Save(string lastMinecraftPath)
        {
            try
            {
                var data = new ConfigData { LastMinecraftPath = lastMinecraftPath };
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save launcher config: {ex.Message}");
            }
        }
    }
}