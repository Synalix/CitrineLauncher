using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using System;
using System.Linq;
using System.Threading;

namespace CitrineLauncher;

public partial class ModsView : WorkspacePage
{
    private readonly GameInstance _instance;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    public event Action? BrowseRequested;
    public ModsView(GameInstance instance)
    {
        InitializeComponent(); _instance = instance;
        Heading.Text = instance.Name;
        InstanceInfo.Text = $"Minecraft {instance.GameVersion} · {instance.Loader} {instance.LoaderVersion}";

        Refresh();
    }
    public void Refresh()
    {
        try
        {
            var selected = (ModsList.SelectedItem as InstalledMod)?.FileName;
            var mods = InstanceModManager.Shared.List(_instance); ModsList.ItemsSource = mods;
            ModsList.SelectedItem = mods.FirstOrDefault(m => m.FileName == selected);
            EmptyText.IsVisible = mods.Count == 0;
            StatusText.Text = _instance.Loader == LoaderType.Fabric ? $"{mods.Count} mod files. Removing a mod keeps its configuration and dependencies." : "Vanilla does not load JAR mods. Create a Fabric instance to install mods.";
            BrowseButton.IsEnabled = LocalButton.IsEnabled = _instance.Loader == LoaderType.Fabric;
            UpdateSelection();
        }
        catch (Exception ex) { StatusText.Text = "Could not read mods. " + ex.Message; }
    }
    private void UpdateSelection()
    {
        var mod = ModsList.SelectedItem as InstalledMod;
        ToggleButton.IsEnabled = RemoveButton.IsEnabled = !IsBusy && mod != null;
        ToggleButton.Content = mod?.Enabled == false ? "Enable" : "Disable";
    }
    private void ModsList_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void RefreshButton_Click(object? sender, RoutedEventArgs e) => Refresh();
    private void BrowseButton_Click(object? sender, RoutedEventArgs e) => BrowseRequested?.Invoke();
    public override void CancelOperation() => _operation?.Cancel();
    public override void Dispose() { _lifetime.Cancel(); _operation?.Cancel(); }
    private async void LocalButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add local mods",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("Minecraft mods") { Patterns = new[] { "*.jar" } } }
            });
            if (files.Count == 0) return;
            _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            IsBusy = true; Actions.IsEnabled = ModsList.IsEnabled = false; UpdateSelection(); StatusText.Text = "Adding local mods…";
            await InstanceModManager.Shared.AddLocalAsync(_instance, files.Select(f => f.TryGetLocalPath() ?? throw new InvalidOperationException("Select a local JAR file.")), _operation.Token);
            Refresh(); StatusText.Text = "Local mods added.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled. You can try again."; }
        catch (Exception ex) { StatusText.Text = "Could not add mods. " + ex.Message; }
        finally { IsBusy = false; Actions.IsEnabled = ModsList.IsEnabled = true; UpdateSelection(); }
    }
    private void ToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ModsList.SelectedItem is not InstalledMod mod) return;
        try { InstanceModManager.Shared.SetEnabled(_instance, mod, !mod.Enabled); Refresh(); }
        catch (Exception ex) { StatusText.Text = "Could not change mod. " + ex.Message; }
    }
    private async void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ModsList.SelectedItem is not InstalledMod mod) return;
        var dialog = new ConfirmDialog($"Remove {mod.Name}?", "This removes the mod file from this instance. Configuration and dependencies are kept.");
        if (OwnerWindow is not Window owner || !await dialog.ShowDialog<bool>(owner)) return;
        try { InstanceModManager.Shared.Remove(_instance, mod); Refresh(); StatusText.Text = "Mod removed."; }
        catch (Exception ex) { StatusText.Text = "Could not remove mod. " + ex.Message; }
    }
    private void CloseButton_Click(object? sender, RoutedEventArgs e) { if (IsBusy) CancelOperation(); else RequestBack(); }
}
