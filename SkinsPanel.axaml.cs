using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CitrineLauncher.Handlers;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher
{
    public partial class SkinsPanel : UserControl
    {
        public event EventHandler? CloseRequested;

        private CancellationTokenSource _cts = new();
        private Account? _activeAccount;
        private MinecraftProfile? _currentProfile;
        private string? _cachedProfileKey; // "username|type"
        private MinecraftCape? _activeCape;
        private bool _capeEnabled;
        private string _currentModel = "classic";
        private bool _webViewReady;

        public SkinsPanel()
        {
            InitializeComponent();
            SetupControls();
        }

        private void SetupControls()
        {
            BackButton.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);

            ClassicModelBtn.Click += (s, e) => SetModel("classic");
            SlimModelBtn.Click    += (s, e) => SetModel("slim");
            UploadSkinBtn.Click        += UploadSkin_Click;
            DownloadSkinBtn.Click      += DownloadSkin_Click;
            ToggleCapeBtn.Click        += ToggleCape_Click;
            CapesList.SelectionChanged += CapesList_SelectionChanged;
            Settings.Instance.PropertyChanged += Settings_PropertyChanged;
            this.Loaded += (_, _) => SetupWebView();
            this.Unloaded += (_, _) =>
            {
                _cts.Cancel();
                _cts.Dispose();
                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
            };
        }

        private void SetupWebView()
        {
            SkinWebView.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess)
                    return;

                _webViewReady = true;
                LoadSelectedAccount();
            };

            var htmlPath = SkinViewerHtml.WriteTempFile();
            SkinWebView.Navigate(new Uri(htmlPath));
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Username) ||
                e.PropertyName == nameof(Settings.Accounts) ||
                e.PropertyName == nameof(Settings.MinecraftPath))
            {
                if (_webViewReady)
                    LoadSelectedAccount();
                else
                    UpdateSelectedAccountLabel(ResolveSelectedAccount());
            }
        }

        private Account? ResolveSelectedAccount()
        {
            var accounts = Settings.Instance.Accounts.ToArray();
            if (accounts.Length == 0)
                return null;

            var savedUsername = Settings.Instance.Username;
            var selectedAccount = accounts.FirstOrDefault(account =>
                string.Equals(account.Username, savedUsername, StringComparison.OrdinalIgnoreCase));

            return selectedAccount ?? accounts.FirstOrDefault();
        }

        private void UpdateSelectedAccountLabel(Account? account)
        {
            SelectedAccountLabel.Text = account == null
                ? "No account selected"
                : $"{account.Username} ({account.Type})";
        }

        private void LoadSelectedAccount()
        {
            var account = ResolveSelectedAccount();
            _activeAccount = account;
            UpdateSelectedAccountLabel(account);

            if (account == null)
            {
                _currentProfile = null;
                _activeCape = null;
                _capeEnabled = false;
                CapesSection.IsVisible = false;
                OfflineNotice.IsVisible = false;
                CapesList.ItemsSource = null;
                SkinNameLabel.Text = "No account selected";
                if (_webViewReady)
                    _ = ExecuteViewerScript("loadDefault()");
                return;
            }

            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            if (account.Type == "Microsoft")
            {
                CapesSection.IsVisible  = true;
                OfflineNotice.IsVisible = false;
                _ = LoadMicrosoftAccountAsync(account, ct);
            }
            else
            {
                CapesSection.IsVisible  = false;
                OfflineNotice.IsVisible = true;
                LoadOfflineAccount(account);
            }
        }

        private async Task LoadMicrosoftAccountAsync(Account account, CancellationToken ct)
        {
            SetLoading(true);
            ClearErrors();
            try
            {
                // Reuse cached profile only if username AND type match
                var profileKey = $"{account.Username}|{account.Type}";
                if (_currentProfile == null || _cachedProfileKey != profileKey)
                {
                    var session = await MicrosoftAuth.GetSessionAsync();
                    if (ct.IsCancellationRequested) return;
                    _currentProfile = await SkinApiHandler.GetProfileAsync(session.AccessToken, ct);
                    if (ct.IsCancellationRequested) return;
                    _cachedProfileKey = profileKey;
                }

                // Derive all state before touching the UI
                var activeSkin = _currentProfile.Skins.FirstOrDefault(s => s.State == "ACTIVE");
                var activeCape = _currentProfile.Capes.FirstOrDefault(c => c.State == "ACTIVE");
                var capeEnabled = activeCape != null;
                var model = activeSkin?.Variant?.ToLower() == "slim" ? "slim" : "classic";

                // Apply all UI updates at once
                _activeCape   = activeCape;
                _capeEnabled  = capeEnabled;
                _currentModel = model;

                SkinNameLabel.Text    = activeSkin != null ? "Current skin" : "Default skin";
                ToggleCapeBtn.Content = capeEnabled ? "DISABLE CAPE" : "ENABLE CAPE";
                SetModelButtons(model);

                CapesList.SelectionChanged -= CapesList_SelectionChanged;
                CapesList.ItemsSource = _currentProfile.Capes.ToArray();
                if (activeCape != null)
                    CapesList.SelectedItem = _currentProfile.Capes.FirstOrDefault(c => c.Id == activeCape.Id);
                CapesList.SelectionChanged += CapesList_SelectionChanged;

                if (!_webViewReady) return;

                if (activeSkin != null)
                    await ExecuteViewerScript($"setSkin('{activeSkin.Url}', '{model}')");
                else
                    await ExecuteViewerScript("loadDefault()");
                if (activeCape != null)
                    await ExecuteViewerScript($"setCape('{activeCape.Url}')");
                else
                    await ExecuteViewerScript("clearCape()");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) ShowSkinError($"Could not load skin data: {ex.Message}");
            }
            finally
            {
                if (!ct.IsCancellationRequested) SetLoading(false);
            }
        }

        private void InvalidateProfileCache()
        {
            _currentProfile = null;
            _cachedProfileKey = null;
        }

        private void LoadOfflineAccount(Account account)
        {
            ClearErrors();
            _currentProfile = null;
            _activeCape = null;
            _capeEnabled = false;
            CapesList.ItemsSource = null;
            if (_webViewReady) _ = ExecuteViewerScript("clearCape()");

            if (string.IsNullOrEmpty(account.SkinPath))
            {
                var fallbackPath = Path.Combine(Settings.Instance.MinecraftPath, "citrine-skins", $"{account.Username}.png");
                if (File.Exists(fallbackPath))
                {
                    account.SkinPath = fallbackPath;
                    Settings.Instance.Save();
                }
            }

            if (!string.IsNullOrEmpty(account.SkinPath) && !File.Exists(account.SkinPath))
            {
                account.SkinPath = string.Empty;
                Settings.Instance.Save();
            }

            var model = string.IsNullOrEmpty(account.SkinModel) ? "classic" : account.SkinModel;
            _currentModel = model;
            SetModelButtons(model);

            if (!string.IsNullOrEmpty(account.SkinPath) && _webViewReady)
            {
                SkinNameLabel.Text = Path.GetFileName(account.SkinPath);
                _ = LoadOfflineSkinAsync(account.SkinPath, model, _cts.Token);
            }
            else
            {
                SkinNameLabel.Text = "Default skin";
                if (_webViewReady) _ = ExecuteViewerScript("loadDefault()");
            }
        }

        private async void UploadSkin_Click(object? sender, RoutedEventArgs e)
        {
            var account = ResolveSelectedAccount();
            if (account == null) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Skin PNG",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }
            });

            if (files.Count == 0) return;
            var filePath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(filePath)) return;

            var error = ValidateSkinFile(filePath);
            if (error != null) { ShowSkinError(error); return; }

            ClearErrors();
            SetActionsEnabled(false);
            try
            {
                if (account.Type == "Microsoft")
                {
                    var session = await MicrosoftAuth.GetSessionAsync();
                    await SkinApiHandler.UploadSkinAsync(session.AccessToken, filePath, _currentModel);
                    InvalidateProfileCache();
                    SkinNameLabel.Text = "Current skin";
                }
                else
                {
                    var skinsDir = Path.Combine(Settings.Instance.MinecraftPath, "citrine-skins");
                    Directory.CreateDirectory(skinsDir);
                    var dest = Path.Combine(skinsDir, $"{account.Username}.png");
                    File.Copy(filePath, dest, overwrite: true);
                    account.SkinPath  = dest;
                    account.SkinModel = _currentModel;
                    Settings.Instance.Save();
                    SkinNameLabel.Text = Path.GetFileName(dest);
                }
                var dataUrl = await FileToDataUrl(filePath);
                await ExecuteViewerScript($"setSkin('{dataUrl}', '{_currentModel}')");
            }
            catch (Exception ex) { ShowSkinError($"Upload failed: {ex.Message}"); }
            finally { SetActionsEnabled(true); }
        }

        private async void DownloadSkin_Click(object? sender, RoutedEventArgs e)
        {
            var account = ResolveSelectedAccount();
            if (account == null) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Skin PNG",
                SuggestedFileName = $"{account.Username}_skin.png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }
            });

            if (file == null) return;
            var dest = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(dest)) return;

            SetActionsEnabled(false);
            try
            {
                if (account.Type == "Microsoft" && _currentProfile != null)
                {
                    var activeSkin = _currentProfile.Skins.FirstOrDefault(s => s.State == "ACTIVE");
                    if (activeSkin == null) { ShowSkinError("No active skin to download."); return; }
                    var bytes = await SkinApiHandler.DownloadSkinBytesAsync(activeSkin.Url);
                    await File.WriteAllBytesAsync(dest, bytes);
                }
                else if (!string.IsNullOrEmpty(account.SkinPath) && File.Exists(account.SkinPath))
                    File.Copy(account.SkinPath, dest, overwrite: true);
                else
                    ShowSkinError("No skin to download.");
            }
            catch (Exception ex) { ShowSkinError($"Download failed: {ex.Message}"); }
            finally { SetActionsEnabled(true); }
        }

        private async void ToggleCape_Click(object? sender, RoutedEventArgs e)
        {
            var account = ResolveSelectedAccount();
            if (account?.Type != "Microsoft") return;
            ClearErrors();
            ToggleCapeBtn.IsEnabled = false;
            try
            {
                var session = await MicrosoftAuth.GetSessionAsync();
                if (_capeEnabled)
                {
                    await SkinApiHandler.DisableCapeAsync(session.AccessToken);
                    InvalidateProfileCache();
                    _capeEnabled = false;
                    _activeCape  = null;
                    ToggleCapeBtn.Content = "ENABLE CAPE";
                    await ExecuteViewerScript("clearCape()");
                }
                else
                {
                    var target = CapesList.SelectedItem as MinecraftCape ?? _currentProfile?.Capes.FirstOrDefault();
                    if (target == null) return;
                    await SkinApiHandler.SetActiveCapeAsync(session.AccessToken, target.Id);
                    InvalidateProfileCache();
                    _activeCape  = target;
                    _capeEnabled = true;
                    ToggleCapeBtn.Content = "DISABLE CAPE";
                    await ExecuteViewerScript($"setCape('{target.Url}')");
                }
            }
            catch (Exception ex)
            {
                ShowCapeError($"Cape toggle failed: {ex.Message}");
                ToggleCapeBtn.Content = _capeEnabled ? "DISABLE CAPE" : "ENABLE CAPE";
            }
            finally { ToggleCapeBtn.IsEnabled = true; }
        }

        private async void CapesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_capeEnabled) return;
            if (CapesList.SelectedItem is not MinecraftCape cape) return;
            if (_activeCape?.Id == cape.Id) return;
            if (ResolveSelectedAccount()?.Type != "Microsoft") return;
            ClearErrors();
            try
            {
                var session = await MicrosoftAuth.GetSessionAsync();
                await SkinApiHandler.SetActiveCapeAsync(session.AccessToken, cape.Id);
                InvalidateProfileCache();
                _activeCape = cape;
                await ExecuteViewerScript($"setCape('{cape.Url}')");
            }
            catch (Exception ex) { ShowCapeError($"Could not switch cape: {ex.Message}"); }
        }

        private void SetModel(string model)
        {
            _currentModel = model;
            SetModelButtons(model);
            var account = ResolveSelectedAccount();
            if (account?.Type == "Offline")
            {
                account.SkinModel = model;
                Settings.Instance.Save();
                if (_webViewReady && !string.IsNullOrEmpty(account.SkinPath))
                    _ = LoadOfflineSkinAsync(account.SkinPath, model, _cts.Token);
            }
            else if (account != null && _currentProfile != null)
            {
                var skin = _currentProfile.Skins.FirstOrDefault(s => s.State == "ACTIVE");
                if (skin != null && _webViewReady)
                    _ = ExecuteViewerScript($"setSkin('{skin.Url}', '{model}')");
            }
        }

        private void SetModelButtons(string model)
        {
            if (model == "classic")
            {
                ClassicModelBtn.Classes.Add("active");
                SlimModelBtn.Classes.Remove("active");
            }
            else
            {
                SlimModelBtn.Classes.Add("active");
                ClassicModelBtn.Classes.Remove("active");
            }
        }

        private async Task ExecuteViewerScript(string script)
        {
            try
            {
                if (_webViewReady)
                    await SkinWebView.InvokeScript(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView script error: {ex.Message}");
            }
        }

        private async Task LoadOfflineSkinAsync(string skinPath, string model, CancellationToken ct)
        {
            try
            {
                var dataUrl = await FileToDataUrl(skinPath, ct);
                if (ct.IsCancellationRequested) return;
                await ExecuteViewerScript($"setSkin('{dataUrl}', '{model}')");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadOfflineSkinAsync error: {ex.Message}");
            }
        }

        private static async Task<string> FileToDataUrl(string filePath, CancellationToken ct = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            return "data:image/png;base64," + Convert.ToBase64String(bytes);
        }

        private static string? ValidateSkinFile(string path)
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return "Invalid file — must be a PNG image.";
            if (new FileInfo(path).Length > 1_000_000)
                return "File too large — skin PNG must be under 1MB.";

            // Verify PNG signature and image dimensions
            try
            {
                using var fs = File.OpenRead(path);

                // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
                Span<byte> sig = stackalloc byte[8];
                if (fs.Read(sig) < 8 ||
                    sig[0] != 0x89 || sig[1] != 0x50 || sig[2] != 0x4E || sig[3] != 0x47 ||
                    sig[4] != 0x0D || sig[5] != 0x0A || sig[6] != 0x1A || sig[7] != 0x0A)
                    return "Invalid file — not a valid PNG image.";

                // IHDR chunk: 4 bytes length, 4 bytes "IHDR", 4 bytes width, 4 bytes height
                Span<byte> ihdr = stackalloc byte[16];
                if (fs.Read(ihdr) < 16)
                    return "Invalid file — PNG header is truncated.";

                int width  = (ihdr[8]  << 24) | (ihdr[9]  << 16) | (ihdr[10] << 8) | ihdr[11];
                int height = (ihdr[12] << 24) | (ihdr[13] << 16) | (ihdr[14] << 8) | ihdr[15];

                bool validDimensions = (width == 64 && height == 64) || (width == 64 && height == 32);
                if (!validDimensions)
                    return $"Invalid skin dimensions ({width}x{height}) — must be 64x64 or 64x32.";
            }
            catch (Exception ex)
            {
                return $"Could not read file: {ex.Message}";
            }

            return null;
        }

        private void SetLoading(bool loading)
        {
            LoadingLabel.IsVisible = loading;
            SetActionsEnabled(!loading);
        }

        private void SetActionsEnabled(bool enabled)
        {
            UploadSkinBtn.IsEnabled   = enabled;
            DownloadSkinBtn.IsEnabled = enabled;
        }

        private void ShowSkinError(string msg) { SkinErrorLabel.Text = msg; SkinErrorLabel.IsVisible = true; }
        private void ShowCapeError(string msg) { CapeErrorLabel.Text = msg; CapeErrorLabel.IsVisible = true; }
        private void ClearErrors() { SkinErrorLabel.IsVisible = false; CapeErrorLabel.IsVisible = false; }
    }
}
