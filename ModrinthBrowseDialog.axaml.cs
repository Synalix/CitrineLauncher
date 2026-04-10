using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using CitrineLauncher.Handlers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace CitrineLauncher
{
    public partial class ModrinthBrowseDialog : Window
    {
        private readonly string? _gameVersion;
        private CancellationTokenSource? _cts;
        private DispatcherTimer? _debounceTimer;

        public record PickResult(string TempFilePath, string PackName, string? GameVersion);

        public ModrinthBrowseDialog(string? gameVersion = null)
        {
            InitializeComponent();
            _gameVersion = gameVersion;

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); RunSearch(); };
            SearchBox.TextChanged += SearchBox_TextChanged;
        }

        // ── Search ─────────────────────────────────────────────────────────────

        private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _debounceTimer?.Stop();
                RunSearch();
            }
        }

        private void SearchButton_Click(object? sender, RoutedEventArgs e) => RunSearch();

        private void RunSearch()
        {
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            _ = SearchAsync(query);
        }

        private async Task SearchAsync(string query)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            ImportButton.IsEnabled = false;
            ResultsList.ItemsSource = null;
            StatusText.Text = "Searching...";
            StatusText.IsVisible = true;
            SearchButton.IsEnabled = false;

            try
            {
                var results = await ModrinthClient.SearchAsync(query, _gameVersion, ct: ct);
                if (ct.IsCancellationRequested) return;

                var items = new List<ModrinthResultItem>();
                foreach (var r in results)
                    items.Add(new ModrinthResultItem(r));

                ResultsList.ItemsSource = items;
                StatusText.Text = results.Count == 0 ? "No results found." : string.Empty;
                StatusText.IsVisible = results.Count == 0;

                _ = LoadIconsAsync(items, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusText.Text = $"Search failed: {ex.Message}";
                StatusText.IsVisible = true;
            }
            finally
            {
                SearchButton.IsEnabled = true;
            }
        }

        private static readonly HttpClient _iconHttp = new HttpClient();

        private async Task LoadIconsAsync(List<ModrinthResultItem> items, CancellationToken ct)
        {
            foreach (var item in items)
            {
                if (ct.IsCancellationRequested) return;
                if (string.IsNullOrEmpty(item.Project.IconUrl)) continue;
                try
                {
                    var bytes = await _iconHttp.GetByteArrayAsync(item.Project.IconUrl, ct);
                    using var ms = new MemoryStream(bytes);
                    item.IconBitmap = new Bitmap(ms);
                }
                catch { /* icon is optional */ }
            }
        }

        // ── Selection ──────────────────────────────────────────────────────────

        private void ResultsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            ImportButton.IsEnabled = ResultsList.SelectedItem != null;
        }

        // ── Import ─────────────────────────────────────────────────────────────

        private async void ImportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ResultsList.SelectedItem is not ModrinthResultItem item) return;

            ImportButton.IsEnabled = false;
            SearchButton.IsEnabled = false;
            StatusText.Text = "Fetching version info...";
            StatusText.IsVisible = true;

            try
            {
                var version = await ModrinthClient.GetLatestVersionAsync(item.Project.ProjectId, _gameVersion);
                if (version is null)
                {
                    StatusText.Text = _gameVersion != null
                        ? $"No compatible version found for Minecraft {_gameVersion}."
                        : "No downloadable version found.";
                    ImportButton.IsEnabled = true;
                    SearchButton.IsEnabled = true;
                    return;
                }

                StatusText.Text = "Downloading...";
                var progress = new Progress<int>(p => StatusText.Text = $"Downloading... {p}%");
                var tempPath = await ModrinthClient.DownloadToTempAsync(version, progress);

                Close(new PickResult(tempPath, item.Title, version.GameVersion));
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Download failed: {ex.Message}";
                ImportButton.IsEnabled = true;
                SearchButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close(null);
        }
    }

    // Top-level so Avalonia compiled bindings (x:DataType) can resolve it without nested-type syntax
    public class ModrinthResultItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ModrinthClient.ModrinthProject Project { get; }
        public string Title => Project.Title;
        public string Description => Project.Description;
        public string DownloadsText => $"{Project.Downloads:N0} downloads";

        private Bitmap? _iconBitmap;
        public Bitmap? IconBitmap
        {
            get => _iconBitmap;
            set { _iconBitmap = value; Notify(); }
        }

        public ModrinthResultItem(ModrinthClient.ModrinthProject project)
        {
            Project = project;
        }
    }
}
