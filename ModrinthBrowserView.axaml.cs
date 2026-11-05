using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher;

public partial class ModrinthBrowserView : WorkspacePage
{
    private readonly string? _gameVersion;
    private readonly ModrinthApi _api;
    private readonly System.Net.Http.HttpClient _http;
    private readonly GameInstance? _instance;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _searchCts, _versionCts;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly List<ModrinthResultItem> _items = [];
    private int _generation, _versionGeneration, _total;
    private bool _closed, _started;
    private CancellationTokenSource? _operation;
    public event Action<PickResult>? Completed;
    private string ProjectType => _instance == null ? "modpack" : "mod";
    public record PickResult(string TempFilePath, string PackName, string? GameVersion);
    public ModrinthBrowserView(string? gameVersion = null, GameInstance? instance = null, ModrinthApi? api = null, System.Net.Http.HttpClient? http = null)
    {
        InitializeComponent(); _gameVersion = gameVersion; _instance = instance;
        _api = api ?? ModrinthClient.Api; _http = http ?? ModrinthClient.Http;
        if (instance != null)
        {
            Heading.Text = "Find mods";
            Subtitle.Text = $"Fabric · Minecraft {instance.GameVersion} · required dependencies included";
            ImportButton.Content = "Install";
        }
        else Subtitle.Text = "Fabric modpacks · choose a pack version before downloading";
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunSearch(); };
        SearchBox.TextChanged += (_, _) =>
        {
            if (IsBusy) return;
            _searchCts?.Cancel(); _versionCts?.Cancel(); _generation++; _versionGeneration++;
            ImportButton.IsEnabled = false; LoadMoreButton.IsEnabled = false; _debounce.Stop(); _debounce.Start();
        };
        Loaded += (_, _) => { if (_started) return; _started = true; SearchBox.Focus(); RunSearch(); };
    }
    public override void CancelOperation() => _operation?.Cancel();
    public override void Dispose()
    {
        if (_closed) return;
        _closed = true; _debounce.Stop(); _lifetime.Cancel(); _operation?.Cancel(); _searchCts?.Cancel(); _versionCts?.Cancel();
        ResultsList.ItemsSource = null;
        foreach (var item in _items) item.IconBitmap?.Dispose(); _items.Clear();
    }
    private void SearchBox_KeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter && !IsBusy) { _debounce.Stop(); RunSearch(); e.Handled = true; } }
    private void SearchButton_Click(object? sender, RoutedEventArgs e) { _debounce.Stop(); RunSearch(); }
    private void ClearButton_Click(object? sender, RoutedEventArgs e) { SearchBox.Text = ""; _debounce.Stop(); RunSearch(); SearchBox.Focus(); }
    private void LoadMoreButton_Click(object? sender, RoutedEventArgs e) => RunSearch(true);
    private void RunSearch(bool append = false)
    {
        if (IsBusy || _closed) return;
        _debounce.Stop(); _searchCts?.Cancel(); _searchCts?.Dispose();
        _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var generation = ++_generation;
        _ = SearchAsync(SearchBox.Text?.Trim() ?? "", append, generation, _searchCts.Token);
    }
    private async Task SearchAsync(string query, bool append, int generation, CancellationToken ct)
    {
        LoadMoreButton.IsEnabled = false;
        if (!append)
        {
            _versionCts?.Cancel(); ++_versionGeneration;
            ResultsList.ItemsSource = null; VersionCombo.ItemsSource = null; ImportButton.IsEnabled = false;
            foreach (var item in _items) item.IconBitmap?.Dispose(); _items.Clear();
        }
        SetStatus("Searching…");
        try
        {
            var page = await _api.SearchAsync(query, _gameVersion, ProjectType, "fabric", append ? _items.Count : 0, ct);
            if (ct.IsCancellationRequested || generation != _generation || _closed) return;
            var added = new List<ModrinthResultItem>();
            foreach (var project in page.Projects) { var item = new ModrinthResultItem(project); _items.Add(item); added.Add(item); }
            _total = page.Total;
            var selected = ResultsList.SelectedItem;
            ResultsList.ItemsSource = _items.ToArray(); ResultsList.SelectedItem = selected;
            SetStatus(_items.Count == 0 ? "No compatible results. Try another search." : $"{_items.Count} of {_total} results");
            LoadMoreButton.IsVisible = _items.Count < _total; LoadMoreButton.IsEnabled = true;
            _ = LoadIconsAsync(added, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation && !_closed) { SetStatus("Search failed. Try again. " + ex.Message); LoadMoreButton.IsEnabled = true; } }
    }
    private async Task LoadIconsAsync(List<ModrinthResultItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested || _closed) return;
            if (!Uri.TryCreate(item.Project.IconUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https") continue;
            try
            {
                var bytes = await _http.GetByteArrayAsync(uri, ct);
                using var input = new MemoryStream(bytes); var bitmap = Bitmap.DecodeToWidth(input, 88);
                if (ct.IsCancellationRequested || _closed) { bitmap.Dispose(); return; }
                item.IconBitmap = bitmap;
            }
            catch (Exception) { /* Catalog artwork is optional. */ }
        }
    }
    private async void ResultsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (IsBusy) return;
        _versionCts?.Cancel(); _versionCts?.Dispose(); _versionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _versionCts.Token; var generation = ++_versionGeneration;
        VersionCombo.ItemsSource = null; ImportButton.IsEnabled = false;
        if (ResultsList.SelectedItem is not ModrinthResultItem item) return;
        SetStatus("Loading compatible versions…");
        try
        {
            var versions = await _api.GetVersionsAsync(item.Project.ProjectId, _gameVersion, ProjectType, "fabric", ct);
            if (ct.IsCancellationRequested || _closed || generation != _versionGeneration) return;
            VersionCombo.ItemsSource = versions; VersionCombo.SelectedIndex = versions.Count > 0 ? 0 : -1;
            SetStatus(versions.Count == 0 ? "No compatible downloadable version. Select another project." : "Choose a version, then " + (_instance == null ? "download the pack." : "install the mod."));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _versionGeneration && !_closed) SetStatus("Could not load versions. Select the project again. " + ex.Message); }
    }
    private void VersionCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ImportButton.IsEnabled = !IsBusy && VersionCombo.SelectedItem is ModrinthVersion;
    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (IsBusy || ResultsList.SelectedItem is not ModrinthResultItem item || VersionCombo.SelectedItem is not ModrinthVersion version) return;
        _operation?.Dispose(); _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsBusy = true; _debounce.Stop(); _searchCts?.Cancel();
        ImportButton.IsEnabled = false; SearchBox.IsEnabled = false; SearchButton.IsEnabled = false; ClearButton.IsEnabled = false;
        ResultsList.IsEnabled = false; VersionCombo.IsEnabled = false; LoadMoreButton.IsEnabled = false;
        string? temp = null;
        try
        {
            if (_instance != null)
            {
                await InstanceModManager.Shared.InstallAsync(_instance, version, _operation.Token, new Progress<string>(s => { if (!_closed) SetStatus(s); }));
                if (!_closed) { SetStatus($"Installed {item.Title} and required dependencies."); Completed?.Invoke(new PickResult("", item.Title, _gameVersion)); }
            }
            else
            {
                temp = Path.Combine(Path.GetTempPath(), "citrine_modrinth_" + Guid.NewGuid().ToString("N") + ".mrpack");
                SetStatus("Downloading pack…");
                await VerifiedDownload.FetchAsync(_http, [version.DownloadUrl], temp, version.Sha512, version.Sha1, _operation.Token,
                    new Progress<int>(p => { if (!_closed) SetStatus($"Downloading pack… {p}%"); }));
                // Validate supported dependencies before returning the selection.
                var manifest = await PackManifest.ReadArchiveAsync(temp, _operation.Token);
                if (!_closed) { var selected = temp; temp = null; IsBusy = false; Completed?.Invoke(new PickResult(selected, item.Title, manifest.GameVersion)); }
            }
        }
        catch (OperationCanceledException) { if (!_closed) SetStatus("Cancelled."); }
        catch (Exception ex) { if (!_closed) SetStatus("Installation failed. You can retry. " + ex.Message); }
        finally
        {
            if (temp != null) File.Delete(temp);
            IsBusy = false;
            if (!_closed)
            {
                SearchBox.IsEnabled = true; SearchButton.IsEnabled = true; ClearButton.IsEnabled = true; ResultsList.IsEnabled = true;
                VersionCombo.IsEnabled = true; LoadMoreButton.IsEnabled = true; ImportButton.IsEnabled = VersionCombo.SelectedItem != null;
            }
        }
    }
    private void SetStatus(string text) { StatusText.Text = text; StatusText.IsVisible = true; }
    private void CancelButton_Click(object? sender, RoutedEventArgs e) { if (IsBusy) CancelOperation(); else RequestBack(); }
}

public class ModrinthResultItem(ModrinthProject project) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public ModrinthProject Project { get; } = project;
    public string Title => Project.Title;
    public string Description => Project.Description;
    public string DownloadsText => $"{Project.Downloads:N0} downloads";
    private Bitmap? _icon;
    public Bitmap? IconBitmap { get => _icon; set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconBitmap))); } }
}
