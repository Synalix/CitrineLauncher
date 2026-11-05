using Avalonia.Controls;
using Avalonia.Input;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using System;
using System.Linq;

namespace CitrineLauncher;

/// <summary>One management surface; task pages share navigation and the selected instance.</summary>
public partial class ManagementWorkspace : UserControl, IDisposable
{
    private static readonly string[] Categories = ["Instances", "New instance", "Mods", "Modrinth", "Accounts", "Skins", "General", "Appearance", "Performance"];
    private readonly SettingsPanel _settings = new();
    private NewInstanceView? _creation;
    private ModsView? _mods;
    private GameInstance? _modsInstance;
    private ModrinthBrowserView? _browser;
    private SkinsPanel? _skins;
    private GameInstance? _selectedInstance;
    private string _category = "Instances";
    private bool _updating, _disposed, _canEdit = true;
    private readonly ModrinthApi? _api;
    private readonly System.Net.Http.HttpClient? _http;
    public Control? CurrentPage => PageHost.Content as Control;
    public bool IsBusy => CurrentPage is WorkspacePage page && page.IsBusy || ReferenceEquals(CurrentPage, _settings) && _settings.IsBusy;
    public event EventHandler? CloseRequested;
    public event EventHandler? BusyChanged;
    public event Action<GameInstance?>? SelectedInstanceChanged;
    public event Action<GameInstance>? InstanceCreated;
    public event Action? InstancesChanged;
    public event EventHandler<Settings>? SettingsSaved;
    public ManagementWorkspace(GameInstance? selected = null, ModrinthApi? api = null, System.Net.Http.HttpClient? http = null)
    {
        InitializeComponent(); _selectedInstance = selected; _api = api; _http = http;
        _settings.SettingsSaved += (_, settings) => SettingsSaved?.Invoke(this, settings);
        _settings.BusyChanged += PageBusyChanged;
        _settings.CloseRequested += (_, _) => RequestClose();
        CategoryList.ItemsSource = Categories;
        RefreshInstances(selected);
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; RequestClose(); } };
        Navigate("Instances");
    }
    public void SetCanEdit(bool value) { _canEdit = value; }
    public void RefreshInstances(GameInstance? selected = null)
    {
        if (IsBusy) return;
        _updating = true;
        var instances = InstanceManager.Instance.Instances.ToArray();
        _selectedInstance = instances.FirstOrDefault(i => i.Id == (selected ?? _selectedInstance)?.Id) ?? instances.FirstOrDefault();
        InstanceSelector.ItemsSource = instances; InstanceSelector.SelectedItem = _selectedInstance;
        _updating = false;
    }
    public bool Navigate(string category)
    {
        if (_disposed) return false;
        if (IsBusy) { Notice("An operation is running. Cancel it or wait before changing categories."); RestoreCategory(); return false; }
        if (!_canEdit && category is "Instances" or "New instance" or "Mods" or "Modrinth" or "Performance")
        { Notice("Stop Minecraft before managing instances or files."); RestoreCategory(); return false; }
        ClearNotice();
        if (_browser != null) { _browser.Dispose(); _browser = null; }
        if (_skins != null) { PageHost.Content = null; _skins = null; }
        _category = category;
        _updating = true; CategoryList.SelectedItem = category; _updating = false;
        PageTitle.Text = category; InstanceSelector.IsVisible = category is "Instances" or "Mods";
        switch (category)
        {
            case "New instance": ShowCreation(); break;
            case "Mods": ShowMods(); break;
            case "Modrinth": ShowBrowser(null); break;
            case "Skins": _skins = new SkinsPanel(); ShowPage(_skins); break;
            case "Accounts":
            case "General":
            case "Appearance":
            case "Performance":
                _settings.ShowSection(category); ShowPage(_settings); break;
            default:
                var instances = new InstancesView(_selectedInstance);
                instances.CreateRequested += () => Navigate("New instance");
                instances.ModsRequested += instance => { SelectInstance(instance); Navigate("Mods"); };
                instances.SelectedInstanceChanged += SelectInstance;
                instances.InstancesChanged += () => { RefreshInstances(); InstancesChanged?.Invoke(); };
                ShowPage(instances); break;
        }
        return true;
    }
    private void ShowCreation()
    {
        if (_creation == null)
        {
            _creation = new NewInstanceView();
            _creation.Created += instance =>
            {
                RefreshInstances(instance); InstanceCreated?.Invoke(instance);
                var finished = _creation; _creation = null; Navigate("Instances"); finished?.Dispose();
            };
            _creation.BrowsePacksRequested += () => { SetCategory("Modrinth"); ShowBrowser(null); };
        }
        ShowPage(_creation);
    }
    private void ShowMods()
    {
        if (_selectedInstance == null)
        {
            var empty = new StackPanel { Spacing = 8 };
            empty.Children.Add(new TextBlock { Text = "Select or create an instance to manage its mods.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var button = new Button { Content = "New instance" }; button.Click += (_, _) => Navigate("New instance"); empty.Children.Add(button);
            ShowPage(empty); return;
        }
        if (_mods == null || !ReferenceEquals(_modsInstance, _selectedInstance))
        {
            _mods?.Dispose(); _modsInstance = _selectedInstance; _mods = new ModsView(_selectedInstance);
            _mods.BrowseRequested += () => { SetCategory("Modrinth"); ShowBrowser(_selectedInstance); };
        }
        _mods.Refresh(); ShowPage(_mods);
    }
    private void ShowBrowser(GameInstance? instance)
    {
        if (instance?.Loader == LoaderType.Vanilla) { Navigate("Mods"); return; }
        _browser?.Dispose(); _browser = new ModrinthBrowserView(instance?.GameVersion, instance, _api, _http);
        PageTitle.Text = instance == null ? "Modrinth packs" : "Find mods";
        InstanceSelector.IsVisible = instance != null;
        _browser.Completed += async result =>
        {
            if (_disposed) { if (result.TempFilePath.Length > 0) System.IO.File.Delete(result.TempFilePath); return; }
            if (instance != null) { _mods?.Refresh(); return; }
            // The downloaded pack becomes the creation form's owned temporary file.
            var browser = _browser; _browser = null;
            SetCategory("New instance"); ShowCreation();
            await _creation!.SelectPackAsync(result.TempFilePath, true); browser?.Dispose();
        };
        _browser.BackRequested += (_, _) => Navigate(instance == null ? "New instance" : "Mods");
        ShowPage(_browser);
    }
    private void SetCategory(string category)
    {
        _category = category; PageTitle.Text = category;
        _updating = true; CategoryList.SelectedItem = category; _updating = false;
    }
    private void ShowPage(Control page)
    {
        if (CurrentPage is WorkspacePage previous)
        {
            previous.BusyChanged -= PageBusyChanged;
            if (previous is InstancesView) previous.Dispose();
        }
        PageHost.Content = page;
        if (page is WorkspacePage next) { next.BusyChanged += PageBusyChanged; next.BackRequested -= PageBackRequested; next.BackRequested += PageBackRequested; }
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
    private void PageBackRequested(object? sender, EventArgs e) { if (ReferenceEquals(sender, _browser)) return; Navigate("Instances"); }
    private void PageBusyChanged(object? sender, EventArgs e)
    {
        InstanceSelector.IsEnabled = !IsBusy;
        if (!IsBusy) ClearNotice();
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }
    private void CategoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e) { if (!_updating && CategoryList.SelectedItem is string category) Navigate(category); }
    private void RestoreCategory() { _updating = true; CategoryList.SelectedItem = _category; _updating = false; }
    private void InstanceSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating || IsBusy) return;
        SelectInstance(InstanceSelector.SelectedItem as GameInstance);
        if (_category == "Mods") Navigate("Mods");
        else if (_category == "Instances") Navigate("Instances");
        else if (_category == "Modrinth") { _browser?.Dispose(); ShowBrowser(_selectedInstance); }
    }
    private void SelectInstance(GameInstance? instance)
    {
        _selectedInstance = instance; _updating = true; InstanceSelector.SelectedItem = instance; _updating = false;
        SelectedInstanceChanged?.Invoke(instance);
    }
    public bool RequestClose()
    {
        if (IsBusy)
        {
            if (CurrentPage is WorkspacePage page) page.CancelOperation();
            Notice("Finishing the current operation. Return when it has stopped."); return false;
        }
        CloseRequested?.Invoke(this, EventArgs.Empty); return true;
    }
    private void Return_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RequestClose();
    private void Notice(string text) { WorkspaceStatus.Text = text; WorkspaceStatus.IsVisible = true; }
    private void ClearNotice() => WorkspaceStatus.IsVisible = false;
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _browser?.Dispose(); _creation?.Dispose(); _mods?.Dispose(); PageHost.Content = null;
    }
}
