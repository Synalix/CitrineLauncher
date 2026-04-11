using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using CmlLib.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CitrineLauncher
{
    public partial class NewInstanceDialog : Window
    {
        private readonly MinecraftLauncher _launcher;
        private string? _modpackPath;
        private string? _modpackName; // set when importing from Modrinth browse

        public NewInstanceDialog(MinecraftLauncher launcher)
        {
            InitializeComponent();
            _launcher = launcher;

            LoaderCombo.ItemsSource = new[] { "Vanilla", "Fabric" };
            LoaderCombo.SelectedIndex = 0;

            _ = LoadVersionsAsync();
        }

        private async Task LoadVersionsAsync()
        {
            try
            {
                CreateButton.IsEnabled = false;
                var versions = await _launcher.GetAllVersionsAsync();
                var releaseVersions = new List<string>();
                foreach (var v in versions)
                {
                    if (v.Type == "release" && !string.IsNullOrEmpty(v.Name))
                        releaseVersions.Add(v.Name);
                }
                Dispatcher.UIThread.Post(() =>
                {
                    VersionCombo.ItemsSource = releaseVersions;
                    if (releaseVersions.Count > 0)
                        VersionCombo.SelectedIndex = 0;
                    CreateButton.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ErrorText.Text = $"Failed to load versions: {ex.Message}";
                    ErrorText.IsVisible = true;
                    CreateButton.IsEnabled = true;
                });
            }
        }

        private void LoaderCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var isFabric = LoaderCombo.SelectedItem?.ToString() == "Fabric";
            ModpackRow.IsVisible = isFabric;
            NameInput.IsVisible = isFabric;

            // Clear modpack selection when switching away from Fabric
            if (!isFabric)
            {
                _modpackPath = null;
                _modpackName = null;
                ModpackPathBox.Text = string.Empty;
                NameInput.Text = string.Empty;
            }
        }

        private async void BrowseModpackButton_Click(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Modpack",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Modpack files") { Patterns = new[] { "*.zip", "*.mrpack" } }
                }
            });

            if (files.Count > 0)
            {
                _modpackPath = files[0].TryGetLocalPath();
                _modpackName = null;
                ModpackPathBox.Text = _modpackPath ?? string.Empty;
            }
        }

        private async void BrowseModrinthButton_Click(object? sender, RoutedEventArgs e)
        {
            var gameVersion = VersionCombo.SelectedItem?.ToString();
            var dialog = new ModrinthBrowseDialog(gameVersion);
            var result = await dialog.ShowDialog<ModrinthBrowseDialog.PickResult?>(this);
            if (result is null) return;

            _modpackPath = result.TempFilePath;
            _modpackName = result.PackName;
            ModpackPathBox.Text = result.PackName;

            // Pre-fill name from pack title if user hasn't typed one
            if (string.IsNullOrWhiteSpace(NameInput.Text))
                NameInput.Text = result.PackName;

            // Switch to the pack's game version if available
            if (!string.IsNullOrEmpty(result.GameVersion))
            {
                var items = VersionCombo.ItemsSource as IList<string>;
                if (items != null && items.Contains(result.GameVersion))
                    VersionCombo.SelectedItem = result.GameVersion;
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

        private async void CreateButton_Click(object? sender, RoutedEventArgs e)
        {
            ErrorText.IsVisible = false;
            StatusText.IsVisible = false;

            var gameVersion = VersionCombo.SelectedItem?.ToString() ?? string.Empty;
            var loaderStr = LoaderCombo.SelectedItem?.ToString() ?? "Vanilla";
            var loader = loaderStr == "Fabric" ? LoaderType.Fabric : LoaderType.Vanilla;

            if (string.IsNullOrEmpty(gameVersion))
            {
                ErrorText.Text = "Please select a Minecraft version.";
                ErrorText.IsVisible = true;
                return;
            }

            // Determine instance name
            string name;
            if (loader == LoaderType.Fabric)
            {
                // Use typed name, fall back to pack name, fall back to version
                name = NameInput.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name))
                    name = _modpackName ?? gameVersion;
            }
            else
            {
                // Vanilla: auto-name from version
                name = gameVersion;
            }

            string loaderVersion = string.Empty;

            if (loader == LoaderType.Fabric)
            {
                CreateButton.IsEnabled = false;
                StatusText.Text = "Resolving Fabric loader version...";
                StatusText.IsVisible = true;
                try
                {
                    var fabricVersions = await InstanceManager.GetFabricLoaderVersionsAsync(gameVersion);
                    if (fabricVersions.Count == 0)
                        throw new InvalidOperationException($"No Fabric loader found for Minecraft {gameVersion}.");
                    loaderVersion = fabricVersions[0];
                    StatusText.IsVisible = false;
                }
                catch (Exception ex)
                {
                    StatusText.IsVisible = false;
                    ErrorText.Text = $"Could not fetch Fabric loader: {ex.Message}";
                    ErrorText.IsVisible = true;
                    CreateButton.IsEnabled = true;
                    return;
                }
            }

            var instance = InstanceManager.Instance.CreateInstance(name, gameVersion, loader, loaderVersion);

            // Import modpack if one was selected
            if (!string.IsNullOrEmpty(_modpackPath))
            {
                CreateButton.IsEnabled = false;
                StatusText.Text = "Importing modpack...";
                StatusText.IsVisible = true;

                var result = await ModpackImporter.ImportFromZipAsync(_modpackPath, instance);

                StatusText.IsVisible = false;

                if (!result.Success)
                {
                    ErrorText.Text = $"Modpack import failed: {result.Message}";
                    ErrorText.IsVisible = true;
                    // Instance was created — still close so user isn't stuck
                }
                else if (result.DetectedGameVersion != null && result.DetectedGameVersion != gameVersion)
                {
                    StatusText.Text = $"Note: pack targets {result.DetectedGameVersion}";
                    StatusText.IsVisible = true;
                }
            }

            Close(instance);
        }
    }
}
