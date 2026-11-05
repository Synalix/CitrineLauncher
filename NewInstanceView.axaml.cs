using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher;

public partial class NewInstanceView : WorkspacePage
{
    private string? _packPath, _ownedTemp;
    private PackManifest? _manifest;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed;
    private CancellationTokenSource? _operation;
    public event Action<GameInstance>? Created;
    public event Action? BrowsePacksRequested;
    public NewInstanceView()
    {
        InitializeComponent();
        LoaderCombo.ItemsSource = new[] { "Vanilla", "Fabric" }; LoaderCombo.SelectedIndex = 0;

        _ = LoadVersionsAsync();
    }
    public override void CancelOperation() => _operation?.Cancel();
    public override void Dispose()
    {
        if (_closed) return;
        _closed = true; _lifetime.Cancel(); _operation?.Cancel(); CleanupTemp();
    }
    private void CleanupTemp() { if (_ownedTemp != null) { try { File.Delete(_ownedTemp); } catch (IOException) { } _ownedTemp = null; } }
    private async Task LoadVersionsAsync()
    {
        try
        {
            var versions = await GameVersionCache.GetReleaseVersionsAsync();
            if (_closed || _manifest != null) return;
            VersionCombo.ItemsSource = versions; VersionCombo.SelectedIndex = versions.Count > 0 ? 0 : -1;
        }
        catch (Exception ex) { if (!_closed) ShowError("Could not load Minecraft versions. You can still select a Modrinth pack. " + ex.Message); }
    }
    public async Task SelectPackAsync(string path, bool owned)
    {
        try
        {
            var manifest = await PackManifest.ReadArchiveAsync(path, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            CleanupTemp(); _ownedTemp = owned ? path : null; _packPath = path; _manifest = manifest;
            ModpackPathBox.Text = manifest.Name; NameInput.Text = manifest.Name;
            VersionCombo.ItemsSource = new[] { manifest.GameVersion }; VersionCombo.SelectedIndex = 0;
            LoaderCombo.SelectedItem = manifest.Loader == LoaderType.Fabric ? "Fabric" : "Vanilla";
            VersionCombo.IsEnabled = false; LoaderCombo.IsEnabled = false;
            IncludeOptionalCheck.IsVisible = manifest.Files.Any(f => f.Optional);
            StatusText.Text = $"Minecraft {manifest.GameVersion} · {manifest.Loader} {manifest.LoaderVersion}"; StatusText.IsVisible = true;
            ErrorText.IsVisible = false;
        }
        catch (Exception ex)
        {
            if (owned) File.Delete(path);
            if (!_closed) ShowError("Cannot select pack. " + ex.Message);
        }
    }
    private async void BrowseModpackButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Modrinth pack",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Modrinth packs") { Patterns = new[] { "*.mrpack", "*.zip" } } }
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is string path) await SelectPackAsync(path, false);
        }
        catch (Exception ex) { ShowError("Could not open pack. " + ex.Message); }
    }
    private void BrowseModrinthButton_Click(object? sender, RoutedEventArgs e) => BrowsePacksRequested?.Invoke();
    private async void ClearPackButton_Click(object? sender, RoutedEventArgs e)
    {
        CleanupTemp(); _packPath = null; _manifest = null; ModpackPathBox.Text = ""; NameInput.Text = "";
        LoaderCombo.IsEnabled = true; VersionCombo.IsEnabled = true; IncludeOptionalCheck.IsVisible = false; StatusText.IsVisible = false;
        await LoadVersionsAsync();
    }
    private void CancelButton_Click(object? sender, RoutedEventArgs e) { if (IsBusy) CancelOperation(); else RequestBack(); }
    private void ShowError(string text) { ErrorText.Text = text; ErrorText.IsVisible = true; }
    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        ErrorText.IsVisible = false;
        if (_closed) return;
        _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var game = _manifest?.GameVersion ?? VersionCombo.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(game)) { ShowError("Select a Minecraft version or a Modrinth pack."); VersionCombo.Focus(); return; }
        var loader = _manifest?.Loader ?? (LoaderCombo.SelectedItem?.ToString() == "Fabric" ? LoaderType.Fabric : LoaderType.Vanilla);
        var name = string.IsNullOrWhiteSpace(NameInput.Text) ? _manifest?.Name ?? game : NameInput.Text.Trim();
        GameInstance? instance = null; IsBusy = true; FormPanel.IsEnabled = false; CreateButton.IsEnabled = false;
        try
        {
            var loaderVersion = _manifest?.LoaderVersion ?? "";
            if (loader == LoaderType.Fabric && _manifest == null)
            {
                StatusText.Text = "Resolving Fabric loader…"; StatusText.IsVisible = true;
                var versions = await InstanceManager.GetFabricLoaderVersionsAsync(game);
                loaderVersion = versions.FirstOrDefault() ?? throw new InvalidOperationException("No compatible Fabric loader is available.");
            }
            _operation.Token.ThrowIfCancellationRequested();
            instance = InstanceManager.Instance.CreateInstance(name, game, loader, loaderVersion);
            if (_packPath != null)
            {
                StatusText.IsVisible = true;
                var result = await ModpackImporter.ImportFromZipAsync(_packPath, instance, _operation.Token,
                    new Progress<string>(s => { if (!_closed) StatusText.Text = s; }), IncludeOptionalCheck.IsChecked != false);
                if (!result.Success) throw new InvalidDataException(result.Message);
            }
            var created = instance; instance = null; IsBusy = false; Created?.Invoke(created);
        }
        catch (OperationCanceledException) { ShowError("Creation cancelled. You can try again."); }
        catch (Exception ex) { ShowError("Could not create instance. " + ex.Message); }
        finally
        {
            if (instance != null) InstanceManager.Instance.DeleteInstance(instance);
            IsBusy = false;
            if (!_closed) { FormPanel.IsEnabled = true; CreateButton.IsEnabled = true; StatusText.IsVisible = false; }
        }
    }
}
