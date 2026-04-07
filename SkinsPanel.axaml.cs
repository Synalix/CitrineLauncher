using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CitrineLauncher.Handlers;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebViewCore;

namespace CitrineLauncher
{
    public partial class SkinsPanel : UserControl
    {
        public event EventHandler? CloseRequested;

        private CancellationTokenSource _cts = new();
        private MinecraftProfile? _currentProfile;
        private MinecraftCape? _activeCape;
        private bool _capeEnabled;
        private string _currentModel = "classic";
        private bool _webViewReady;
        private IWebViewControl? _webViewControl;

        public SkinsPanel()
        {
            InitializeComponent();
            SetupControls();
        }

        private void SetupControls()
        {
            BackButton.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);

            var accounts = Settings.Instance.Accounts.ToArray();
            AccountCombo.ItemsSource = accounts;
            var savedUsername = Settings.Instance.Username;
            var defaultAccount = accounts.FirstOrDefault(a => a.Username == savedUsername)
                ?? accounts.FirstOrDefault();
            if (defaultAccount != null)
                AccountCombo.SelectedItem = defaultAccount;

            AccountCombo.SelectionChanged += (s, e) => LoadSelectedAccount();
            ClassicModelBtn.Click += (s, e) => SetModel("classic");
            SlimModelBtn.Click    += (s, e) => SetModel("slim");
            UploadSkinBtn.Click        += UploadSkin_Click;
            DownloadSkinBtn.Click      += DownloadSkin_Click;
            ToggleCapeBtn.Click        += ToggleCape_Click;
            CapesList.SelectionChanged += CapesList_SelectionChanged;
            this.Loaded += async (s, e) => await SetupWebView();
        }

        private async Task SetupWebView()
        {
            // Resolve the WebView control at runtime to avoid source-generator field dependency
            var webViewControl = this.FindControl<Control>("SkinWebView");
            _webViewControl = webViewControl as IWebViewControl;

            var htmlPath = SkinViewerHtml.WriteTempFile();
            var url = new Uri("file:///" + htmlPath.Replace("\\", "/"));
            _webViewControl?.Navigate(url);
            await Task.Delay(1500);
            _webViewReady = true;
            LoadSelectedAccount();
        }

        private void LoadSelectedAccount()
        {
            if (AccountCombo.SelectedItem is not Account account) return;
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
                var session = await MicrosoftAuth.GetSessionAsync();
                if (ct.IsCancellationRequested) return;
                _currentProfile = await SkinApiHandler.GetProfileAsync(session.AccessToken, ct);
                if (ct.IsCancellationRequested) return;

                var activeSkin = _currentProfile.Skins.FirstOrDefault(s => s.State == "ACTIVE");
                _activeCape  = _currentProfile.Capes.FirstOrDefault(c => c.State == "ACTIVE");
                _capeEnabled = _activeCape != null;

                SkinNameLabel.Text = activeSkin != null ? "Current skin" : "Default skin";
                var model = activeSkin?.Variant?.ToLower() == "slim" ? "slim" : "classic";
                _currentModel = model;
                SetModelButtons(model);

                if (activeSkin != null && _webViewReady)
                    await ExecuteViewerScript($"setSkin('{activeSkin.Url}', '{model}')");

                CapesList.SelectionChanged -= CapesList_SelectionChanged;
                CapesList.ItemsSource = _currentProfile.Capes.ToArray();
                if (_activeCape != null)
                    CapesList.SelectedItem = _currentProfile.Capes.FirstOrDefault(c => c.Id == _activeCape.Id);
                CapesList.SelectionChanged += CapesList_SelectionChanged;

                ToggleCapeBtn.Content = _capeEnabled ? "DISABLE CAPE" : "ENABLE CAPE";
                if (_activeCape != null && _webViewReady)
                    await ExecuteViewerScript($"setCape('{_activeCape.Url}')");
                else if (_webViewReady)
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

        private void LoadOfflineAccount(Account account)
        {
            ClearErrors();
            _currentProfile = null;
            _activeCape = null;
            CapesList.ItemsSource = null;

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
                _ = ExecuteViewerScript($"setSkin('{FileToDataUrl(account.SkinPath)}', '{model}')");
            }
            else
            {
                SkinNameLabel.Text = "Default skin";
                if (_webViewReady) _ = ExecuteViewerScript("loadDefault()");
            }
        }

        private async void UploadSkin_Click(object? sender, RoutedEventArgs e)
        {
            if (AccountCombo.SelectedItem is not Account account) return;
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
                await ExecuteViewerScript($"setSkin('{FileToDataUrl(filePath)}', '{_currentModel}')");
            }
            catch (Exception ex) { ShowSkinError($"Upload failed: {ex.Message}"); }
            finally { SetActionsEnabled(true); }
        }

        private async void DownloadSkin_Click(object? sender, RoutedEventArgs e)
        {
            if (AccountCombo.SelectedItem is not Account account) return;
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
        }

        private async void ToggleCape_Click(object? sender, RoutedEventArgs e)
        {
            if (AccountCombo.SelectedItem is not Account { Type: "Microsoft" }) return;
            ClearErrors();
            ToggleCapeBtn.IsEnabled = false;
            try
            {
                var session = await MicrosoftAuth.GetSessionAsync();
                if (_capeEnabled)
                {
                    await SkinApiHandler.DisableCapeAsync(session.AccessToken);
                    _capeEnabled = false;
                    _activeCape  = null;
                    ToggleCapeBtn.Content = "DISABLE CAPE";
                    await ExecuteViewerScript("clearCape()");
                }
                else
                {
                    var target = CapesList.SelectedItem as MinecraftCape ?? _currentProfile?.Capes.FirstOrDefault();
                    if (target == null) return;
                    await SkinApiHandler.SetActiveCapeAsync(session.AccessToken, target.Id);
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
            if (AccountCombo.SelectedItem is not Account { Type: "Microsoft" }) return;
            ClearErrors();
            try
            {
                var session = await MicrosoftAuth.GetSessionAsync();
                await SkinApiHandler.SetActiveCapeAsync(session.AccessToken, cape.Id);
                _activeCape = cape;
                await ExecuteViewerScript($"setCape('{cape.Url}')");
            }
            catch (Exception ex) { ShowCapeError($"Could not switch cape: {ex.Message}"); }
        }

        private void SetModel(string model)
        {
            _currentModel = model;
            SetModelButtons(model);
            if (AccountCombo.SelectedItem is Account { Type: "Offline" } account)
            {
                account.SkinModel = model;
                Settings.Instance.Save();
                if (_webViewReady && !string.IsNullOrEmpty(account.SkinPath))
                    _ = ExecuteViewerScript($"setSkin('{FileToDataUrl(account.SkinPath)}', '{model}')");
            }
            else if (AccountCombo.SelectedItem is Account && _currentProfile != null)
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
                if (_webViewControl != null)
                    await _webViewControl.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView script error: {ex.Message}");
            }
        }

        private static string FileToDataUrl(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            return "data:image/png;base64," + Convert.ToBase64String(bytes);
        }

        private static string? ValidateSkinFile(string path)
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return "Invalid file — must be a PNG image.";
            if (new FileInfo(path).Length > 1_000_000)
                return "File too large — skin PNG must be under 1MB.";
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
