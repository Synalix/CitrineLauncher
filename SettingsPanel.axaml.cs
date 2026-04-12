using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CitrineLauncher.Handlers;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CitrineLauncher
{
    public partial class SettingsPanel : UserControl
    {
        public event EventHandler? CloseRequested;
        public event EventHandler<Settings>? SettingsSaved;

        private bool _isEditingFolder = false;

        public SettingsPanel()
        {
            InitializeComponent();
            SetupControls();
        }

        private void SetupControls()
        {
            var settings = Settings.Instance;
            DataContext = settings;

            // Populate account list
            AccountsList.ItemsSource = settings.Accounts.ToArray();

            // RAM custom toggle visibility
            UpdateRamVisibility();

            MinRamCustomCheck.IsCheckedChanged += (s, e) =>
            {
                settings.UseCustomMinRam = MinRamCustomCheck.IsChecked ?? false;
                UpdateRamVisibility();
            };

            MaxRamCustomCheck.IsCheckedChanged += (s, e) =>
            {
                settings.UseCustomMaxRam = MaxRamCustomCheck.IsChecked ?? false;
                UpdateRamVisibility();
            };

            // Custom RAM text boxes save on lost focus
            MinRamTextBox.LostFocus += (s, e) =>
            {
                if (int.TryParse(MinRamTextBox.Text, out int val) && val >= 256)
                {
                    settings.MinRam = val;
                    // Clamp MaxRam so it stays >= MinRam
                    if (settings.MaxRam < val)
                    {
                        settings.MaxRam = val;
                        MaxRamTextBox.Text = val.ToString();
                    }
                    RamErrorLabel.IsVisible = false;
                }
                else
                {
                    RamErrorLabel.Text = "Minimum RAM must be a number ≥ 256 MB.";
                    RamErrorLabel.IsVisible = true;
                    MinRamTextBox.Text = settings.MinRam.ToString();
                }
            };

            MaxRamTextBox.LostFocus += (s, e) =>
            {
                if (int.TryParse(MaxRamTextBox.Text, out int val) && val >= 256)
                {
                    if (val < settings.MinRam)
                    {
                        RamErrorLabel.Text = $"Maximum RAM must be ≥ minimum RAM ({settings.MinRam} MB).";
                        RamErrorLabel.IsVisible = true;
                        MaxRamTextBox.Text = settings.MaxRam.ToString();
                        return;
                    }
                    settings.MaxRam = val;
                    RamErrorLabel.IsVisible = false;
                }
                else
                {
                    RamErrorLabel.Text = "Maximum RAM must be a number ≥ 256 MB.";
                    RamErrorLabel.IsVisible = true;
                    MaxRamTextBox.Text = settings.MaxRam.ToString();
                }
            };

            // Show folder path (read-only display - not a TwoWay binding to avoid writing on every keystroke, bug #7)
            FolderPathTextBlock.Text = settings.MinecraftPath;

            // Add offline account
            AddOfflineButton.Click += async (s, e) =>
            {
                var dialog = new OfflineAccountDialog();
                var parentWindow = this.VisualRoot as Window;
                if (parentWindow != null)
                {
                    var result = await dialog.ShowDialog<string?>(parentWindow);
                    if (!string.IsNullOrEmpty(result))
                    {
                        if (settings.Accounts.Any(account =>
                                account.Type == "Offline" &&
                                string.Equals(account.Username, result, StringComparison.OrdinalIgnoreCase)))
                        {
                            return;
                        }

                        settings.Accounts.Add(new Account { Username = result, Type = "Offline" });
                        RefreshAccountList();
                        settings.Save();
                        // Notify MainWindow
                        SettingsSaved?.Invoke(this, settings);
                    }
                }
            };

            // Microsoft account login
            AddMicrosoftButton.Click += async (s, e) =>
            {
                AddMicrosoftButton.IsEnabled = false;
                try
                {
                    // Bug 3: resolve any existing account first so the session is cached under its stable Id.
                    // We do a temporary auth to discover the username, then re-auth under the correct id
                    // if the account already exists, discarding the temp cache entry.
                    var tempAccount = new Account { Type = "Microsoft" };
                    var (username, _) = await MicrosoftAuth.AuthenticateAsync(tempAccount.Id);
                    if (!string.IsNullOrEmpty(username))
                    {
                        var existing = settings.Accounts.FirstOrDefault(a =>
                            a.Type == "Microsoft" &&
                            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            // Re-auth under the persisted account's stable id so the session cache is correct
                            MicrosoftAuth.ClearCache(tempAccount.Id);
                            await MicrosoftAuth.AuthenticateAsync(existing.Id);
                        }
                        else
                        {
                            tempAccount.Username = username;
                            settings.Accounts.Add(tempAccount);
                            RefreshAccountList();
                        }

                        settings.Username = username;
                        settings.Save();
                        SettingsSaved?.Invoke(this, settings);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Microsoft auth error: {ex.Message}");
                }
                finally
                {
                    AddMicrosoftButton.IsEnabled = true;
                }
            };

            ChangeFolderButton.Click += (s, e) =>
            {
                if (!_isEditingFolder)
                {
                    FolderErrorLabel.IsVisible = false;
                    SetFolderEditMode(true);
                    FolderPathTextBox.Text = settings.MinecraftPath;
                    FolderPathTextBox.Focus();
                }
                else
                {
                    var newPath = FolderPathTextBox.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(newPath))
                    {
                        FolderErrorLabel.Text = "Path cannot be empty.";
                        FolderErrorLabel.IsVisible = true;
                        return;
                    }
                    if (!System.IO.Path.IsPathRooted(newPath))
                    {
                        FolderErrorLabel.Text = "Path must be an absolute path (e.g. C:\\Games\\Minecraft).";
                        FolderErrorLabel.IsVisible = true;
                        return;
                    }
                    FolderErrorLabel.IsVisible = false;
                    settings.MinecraftPath = newPath;
                    FolderPathTextBlock.Text = newPath;
                    SetFolderEditMode(false);
                }
            };

            FolderPathTextBox.KeyDown += (s, e) =>
            {
                if (_isEditingFolder && e.Key == Avalonia.Input.Key.Escape)
                {
                    SetFolderEditMode(false);
                    e.Handled = true;
                }
            };

            // Navigation buttons
            GeneralNav.Click     += (s, e) => ShowSection("General");
            AccountsNav.Click    += (s, e) => ShowSection("Accounts");
            AppearanceNav.Click  += (s, e) => ShowSection("Appearance");
            PerformanceNav.Click += (s, e) => ShowSection("Performance");

            // Close button
            CloseButton.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateRamVisibility()
        {
            var settings = Settings.Instance;

            MinRamCombo.IsVisible = !settings.UseCustomMinRam;
            MinRamTextBox.IsVisible = settings.UseCustomMinRam;

            MaxRamCombo.IsVisible = !settings.UseCustomMaxRam;
            MaxRamTextBox.IsVisible = settings.UseCustomMaxRam;
        }

        private void RefreshAccountList()
        {
            AccountsList.SelectedIndex = -1;
            AccountsList.ItemsSource = null;
            AccountsList.ItemsSource = Settings.Instance.Accounts.ToArray();
        }

        private void SetFolderEditMode(bool isEditing)
        {
            _isEditingFolder = isEditing;
            FolderPathTextBlock.IsVisible = !isEditing;
            FolderPathTextBox.IsVisible = isEditing;
            ChangeFolderButton.Content = isEditing ? "SAVE" : "CHANGE";
        }

        public void SwitchToTab(string tabName)
        {
            ShowSection(tabName);
        }

        public void ShowSection(string sectionName)
        {
            GeneralSection.IsVisible    = sectionName == "General";
            AccountsSection.IsVisible   = sectionName == "Accounts";
            AppearanceSection.IsVisible = sectionName == "Appearance";
            PerformanceSection.IsVisible = sectionName == "Performance";
        }

        // Bug fix #9: made async, uses await instead of ContinueWith
        private async void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsList.SelectedItem is Account selectedAccount && selectedAccount.Type == "Offline")
            {
                var previousUsername = selectedAccount.Username;
                var dialog = new OfflineAccountDialog(selectedAccount.Username);
                var parentWindow = this.VisualRoot as Window;
                if (parentWindow != null)
                {
                    var result = await dialog.ShowDialog<string?>(parentWindow);
                    if (result != null && !string.Equals(result, previousUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        // Bug 1: reject rename if another offline account already has this username
                        if (Settings.Instance.Accounts.Any(a =>
                                a != selectedAccount &&
                                a.Type == "Offline" &&
                                string.Equals(a.Username, result, StringComparison.OrdinalIgnoreCase)))
                            return;

                        // Bug 2: unregister old name from skin server before renaming
                        OfflineSkinServer.Shared.Unregister(selectedAccount);

                        selectedAccount.Username = result;
                        if (string.Equals(Settings.Instance.Username, previousUsername, StringComparison.OrdinalIgnoreCase))
                            Settings.Instance.Username = result;

                        // Re-register under the new username
                        OfflineSkinServer.Shared.Register(selectedAccount);

                        Settings.Instance.Save();
                        RefreshAccountList();
                        SettingsSaved?.Invoke(this, Settings.Instance);
                    }
                }
            }
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsList.SelectedItem is Account selectedAccount)
            {
                // Bug 2: unregister from skin server before removing
                if (selectedAccount.Type == "Offline")
                    OfflineSkinServer.Shared.Unregister(selectedAccount);

                if (Settings.Instance.RemoveAccount(selectedAccount))
                {
                    RefreshAccountList();
                    SettingsSaved?.Invoke(this, Settings.Instance);
                }
            }
        }


    }
}
