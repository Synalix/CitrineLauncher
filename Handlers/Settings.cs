using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

namespace CitrineLauncher.Handlers
{
    public class Account
    {
        public string Username { get; set; } = string.Empty;
        public string Type { get; set; } = "Offline"; // "Offline" or "Microsoft"
    }

    public class Settings : INotifyPropertyChanged
    {
        public static readonly string DefaultMinecraftPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CitrineLauncher"
        );

        private static string _currentSettingsPath = Path.Combine(DefaultMinecraftPath, "citrine.json");

        // Prevents Save() from firing on every property set during JSON deserialization
        [JsonIgnore]
        private bool _isLoading = false;

        private string _username = string.Empty;
        private int _maxRam = 2048;
        private int _minRam = 1024;
        private bool _useCustomMinRam = false;
        private bool _useCustomMaxRam = false;
        private bool _minimizeOnLaunch = false;
        private bool _showConsole = false;
        private string _theme = "Dark";
        private int _particleDensity = 50;
        private bool _particlesEnabled = true;
        private string _minecraftPath = DefaultMinecraftPath;
        private List<Account> _accounts = new List<Account>();

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); Save(); }
        }

        public int MaxRam
        {
            get => _maxRam;
            set { _maxRam = value; OnPropertyChanged(); Save(); }
        }

        public int MinRam
        {
            get => _minRam;
            set { _minRam = value; OnPropertyChanged(); Save(); }
        }

        public bool UseCustomMinRam
        {
            get => _useCustomMinRam;
            set { _useCustomMinRam = value; OnPropertyChanged(); Save(); }
        }

        public bool UseCustomMaxRam
        {
            get => _useCustomMaxRam;
            set { _useCustomMaxRam = value; OnPropertyChanged(); Save(); }
        }

        // Renamed from AutoCloseLauncher - it minimizes, not closes
        public bool MinimizeOnLaunch
        {
            get => _minimizeOnLaunch;
            set { _minimizeOnLaunch = value; OnPropertyChanged(); Save(); }
        }

        public bool ShowConsole
        {
            get => _showConsole;
            set { _showConsole = value; OnPropertyChanged(); Save(); }
        }

        public string Theme
        {
            get => _theme;
            set { _theme = value; OnPropertyChanged(); Save(); }
        }

        public int ParticleDensity
        {
            get => _particleDensity;
            set { _particleDensity = Math.Clamp(value, 0, 100); OnPropertyChanged(); Save(); }
        }

        public bool ParticlesEnabled
        {
            get => _particlesEnabled;
            set { _particlesEnabled = value; OnPropertyChanged(); Save(); }
        }

        public string MinecraftPath
        {
            get => _minecraftPath;
            set
            {
                if (_minecraftPath != value)
                {
                    _minecraftPath = value;
                    OnPropertyChanged();
                    if (!_isLoading)
                    {
                        LauncherConfig.Save(value);
                        Save();
                    }
                }
            }
        }

        public List<Account> Accounts
        {
            // Null-guard: JsonSerializer.Deserialize bypasses the constructor, so _accounts
            // can be null if the JSON contains "Accounts": null or the key is absent.
            get => _accounts ??= new List<Account>();
            set { _accounts = value ?? new List<Account>(); OnPropertyChanged(); Save(); }
        }

        // [JsonIgnore] prevents these from being written to the settings file
        [JsonIgnore]
        public int[] RamOptions { get; } = new[] { 512, 1024, 2048, 4096, 8192 };

        [JsonIgnore]
        public string[] ThemeOptions { get; } = new[] { "Dark", "Light" };

        private static Settings? _instance;
        public static Settings Instance => _instance ??= Load();

        public Settings() { }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Save()
        {
            // Don't save while we're loading from disk
            if (_isLoading) return;

            try
            {
                var directory = Path.GetDirectoryName(_currentSettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(_currentSettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private static Settings? TryLoadFromPath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    if (settings != null)
                    {
                        _currentSettingsPath = path;
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings from {path}: {ex.Message}");
            }
            return null;
        }

        private static Settings Load()
        {
            var config = LauncherConfig.Load();
            var pathsToTry = new[] { config.LastMinecraftPath, DefaultMinecraftPath }
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .Select(p => Path.Combine(p, "citrine.json"))
                .ToArray();

            foreach (var path in pathsToTry)
            {
                var settings = TryLoadFromPath(path);
                if (settings != null)
                {
                    // Mark as loading so setters don't trigger Save() during initialization
                    settings._isLoading = false;
                    LauncherConfig.Save(settings.MinecraftPath);
                    return settings;
                }
            }

            // No existing settings - create new with defaults
            var newSettings = new Settings();
            _currentSettingsPath = Path.Combine(DefaultMinecraftPath, "citrine.json");
            LauncherConfig.Save(DefaultMinecraftPath);
            newSettings.Save();
            return newSettings;
        }
    }
}