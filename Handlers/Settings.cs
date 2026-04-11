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
        public string SkinPath { get; set; } = string.Empty;  // offline only: local PNG path
        public string SkinModel { get; set; } = "classic";    // "classic" or "slim"
        // Stable UUID for offline accounts — generated once and persisted so the skin server
        // always returns the same profile for this account regardless of username changes.
        public string OfflineUuid { get; set; } = string.Empty;

        public string GetOrCreateOfflineUuid()
        {
            if (string.IsNullOrEmpty(OfflineUuid))
                OfflineUuid = Guid.NewGuid().ToString("N"); // 32-char hex, no dashes
            return OfflineUuid;
        }
    }

    public class Settings : INotifyPropertyChanged
    {
        public static readonly string DefaultMinecraftPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CitrineLauncher"
        );

        private static string _currentSettingsPath = Path.Combine(DefaultMinecraftPath, "citrine.json");

        // Prevents Save() from firing while JSON deserialization is populating the object
        [JsonIgnore]
        private bool _isLoading = true;

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
        private string _lastVersion = string.Empty;

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public int MaxRam
        {
            get => _maxRam;
            set => SetProperty(ref _maxRam, value);
        }

        public int MinRam
        {
            get => _minRam;
            set => SetProperty(ref _minRam, value);
        }

        public bool UseCustomMinRam
        {
            get => _useCustomMinRam;
            set => SetProperty(ref _useCustomMinRam, value);
        }

        public bool UseCustomMaxRam
        {
            get => _useCustomMaxRam;
            set => SetProperty(ref _useCustomMaxRam, value);
        }

        // Renamed from AutoCloseLauncher - it minimizes, not closes
        public bool MinimizeOnLaunch
        {
            get => _minimizeOnLaunch;
            set => SetProperty(ref _minimizeOnLaunch, value);
        }

        public bool ShowConsole
        {
            get => _showConsole;
            set => SetProperty(ref _showConsole, value);
        }

        public string Theme
        {
            get => _theme;
            set => SetProperty(ref _theme, value);
        }

        public int ParticleDensity
        {
            get => _particleDensity;
            set => SetProperty(ref _particleDensity, Math.Clamp(value, 0, 100));
        }

        public bool ParticlesEnabled
        {
            get => _particlesEnabled;
            set => SetProperty(ref _particlesEnabled, value);
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

        // Last selected version — used once for migration to instance model, then ignored
        public string LastVersion
        {
            get => _lastVersion;
            set => SetProperty(ref _lastVersion, value);
        }

        public List<Account> Accounts
        {
            // Null-guard: JsonSerializer.Deserialize bypasses the constructor, so _accounts
            // can be null if the JSON contains "Accounts": null or the key is absent.
            get => _accounts ??= new List<Account>();
            set => SetProperty(ref _accounts, value ?? new List<Account>());
        }

        public bool RemoveAccount(Account account)
        {
            if (!_accounts.Remove(account))
                return false;

            if (_accounts.Count == 0)
            {
                if (!string.IsNullOrEmpty(_username))
                {
                    _username = string.Empty;
                    OnPropertyChanged(nameof(Username));
                }
            }
            else if (string.Equals(_username, account.Username, StringComparison.OrdinalIgnoreCase))
            {
                _username = _accounts[0].Username;
                OnPropertyChanged(nameof(Username));
            }

            OnPropertyChanged(nameof(Accounts));
            Save();
            return true;
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

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            Save();
            return true;
        }

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
                        settings._isLoading = false;
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
            var pathsToTry = new[] { DefaultMinecraftPath, config.LastMinecraftPath }
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
            newSettings._isLoading = false;
            newSettings.Save();
            return newSettings;
        }
    }
}