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

        private async void BrowseModpackButton_Click(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Modpack Zip",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Modpack ZIP") { Patterns = new[] { "*.zip" } }
                }
            });

            if (files.Count > 0)
            {
                _modpackPath = files[0].TryGetLocalPath();
                ModpackPathBox.Text = _modpackPath ?? string.Empty;
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

        private async void CreateButton_Click(object? sender, RoutedEventArgs e)
        {
            ErrorText.IsVisible = false;
            StatusText.IsVisible = false;

            var name = NameInput.Text?.Trim() ?? string.Empty;
            var gameVersion = VersionCombo.SelectedItem?.ToString() ?? string.Empty;
            var loaderStr = LoaderCombo.SelectedItem?.ToString() ?? "Vanilla";

            if (string.IsNullOrEmpty(name))
            {
                ErrorText.Text = "Instance name cannot be empty.";
                ErrorText.IsVisible = true;
                return;
            }

            if (string.IsNullOrEmpty(gameVersion))
            {
                ErrorText.Text = "Please select a Minecraft version.";
                ErrorText.IsVisible = true;
                return;
            }

            var loader = loaderStr == "Fabric" ? LoaderType.Fabric : LoaderType.Vanilla;
            string loaderVersion = string.Empty;

            if (loader == LoaderType.Fabric)
            {
                CreateButton.IsEnabled = false;
                StatusText.Text = "Resolving Fabric loader version...";
                StatusText.IsVisible = true;
                try
                {
                    loaderVersion = await InstanceManager.GetLatestFabricLoaderVersionAsync();
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
                    // Instance was created, so still close with it — user can fix manually
                }
                else if (result.DetectedGameVersion != null && result.DetectedGameVersion != gameVersion)
                {
                    // Non-fatal: inform but still proceed
                    StatusText.Text = $"Note: pack targets {result.DetectedGameVersion}";
                    StatusText.IsVisible = true;
                }
            }

            Close(instance);
        }
    }
}

