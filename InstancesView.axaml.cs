using Avalonia.Controls;
using Avalonia.Interactivity;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using System;
using System.Diagnostics;
using System.Linq;

namespace CitrineLauncher;

public partial class InstancesView : WorkspacePage
{
    public event Action? CreateRequested;
    public event Action<GameInstance>? ModsRequested;
    public event Action<GameInstance?>? SelectedInstanceChanged;
    public event Action? InstancesChanged;
    public InstancesView(GameInstance? selected = null) { InitializeComponent(); Refresh(selected); }
    public void Refresh(GameInstance? selected = null)
    {
        var items = InstanceManager.Instance.Instances.ToArray();
        selected ??= InstancesList.SelectedItem as GameInstance;
        InstancesList.ItemsSource = items;
        InstancesList.SelectedItem = items.FirstOrDefault(i => i.Id == selected?.Id) ?? items.FirstOrDefault();
        EmptyText.IsVisible = items.Length == 0; StatusText.Text = $"{items.Length} instances";
    }
    private void InstancesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var instance = InstancesList.SelectedItem as GameInstance;
        ModsButton.IsEnabled = FolderButton.IsEnabled = DeleteButton.IsEnabled = instance != null;
        SelectedInstanceChanged?.Invoke(instance);
    }
    private void New_Click(object? sender, RoutedEventArgs e) => CreateRequested?.Invoke();
    private void Mods_Click(object? sender, RoutedEventArgs e) { if (InstancesList.SelectedItem is GameInstance instance) ModsRequested?.Invoke(instance); }
    private void Folder_Click(object? sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not GameInstance instance) return;
        try { Process.Start(new ProcessStartInfo { FileName = instance.InstanceDirectory, UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = "Could not open folder. " + ex.Message; }
    }
    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (IsBusy || InstancesList.SelectedItem is not GameInstance instance || OwnerWindow is not Window owner) return;
        IsBusy = true;
        var deleted = false;
        try
        {
            if (!await new ConfirmDialog($"Delete {instance.Name}?", "Deletes this instance and its files permanently.").ShowDialog<bool>(owner)) return;
            InstanceManager.Instance.DeleteInstance(instance); deleted = true;
        }
        catch (Exception ex) { StatusText.Text = "Could not delete instance. " + ex.Message; }
        finally
        {
            IsBusy = false;
            if (deleted) { Refresh(); InstancesChanged?.Invoke(); }
        }
    }
}
