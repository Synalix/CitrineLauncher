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
        private Account? _editingAccount;
        public bool IsBusy { get; private set; }
        public event EventHandler? BusyChanged;

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            AccountsSection.IsEnabled = !busy;
            PerformanceSection.IsEnabled = !busy;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        public SettingsPanel()
        {
            InitializeComponent();
            SetupControls();
            KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Escape && !IsBusy)
                {
                    e.Handled = true;
                    if (AccountEditor.IsVisible) CancelAccountEdit();
                    else if (_isEditingFolder) SetFolderEditMode(false);
                    else CloseRequested?.Invoke(this, EventArgs.Empty);
                }
            };
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

            AddOfflineButton.Click += (_, _) => BeginAccountEdit(null);
            SaveAccountButton.Click += (_, _) => SaveOfflineAccount();
            CancelAccountButton.Click += (_, _) => CancelAccountEdit();
            AccountsList.SelectionChanged += (_, _) => UpdateAccountActions();
            UsernameInput.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) { SaveOfflineAccount(); e.Handled = true; }
                else if (e.Key == Avalonia.Input.Key.Escape) { CancelAccountEdit(); e.Handled = true; }
            };

            // Microsoft account login
            AddMicrosoftButton.Click += async (s, e) =>
            {
                SetBusy(true);
                AccountErrorLabel.IsVisible = false;
                try
                {
                    // Authenticate once to discover the username
                    var tempAccount = new Account { Type = "Microsoft" };
                    var (username, session) = await MicrosoftAuth.AuthenticateAsync(tempAccount.Id);
                    if (!string.IsNullOrEmpty(username))
                    {
                        var existing = settings.Accounts.FirstOrDefault(a =>
                            a.Type == "Microsoft" &&
                            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            // Re-key the cache from temp account to existing account,
                            // reusing the session we just got instead of re-authenticating
                            MicrosoftAuth.ReKeyCache(tempAccount.Id, existing.Id);
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
                    AccountErrorLabel.Text = $"Microsoft sign-in failed: {ex.Message}";
                    AccountErrorLabel.IsVisible = true;
                }
                finally
                {
                    SetBusy(false);
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

                    var oldPath = settings.MinecraftPath;
                    if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Migrate existing data to new path
                        try
                        {
                            SetBusy(true);
                            MigrateData(oldPath, newPath);
                        }
                        catch (Exception ex)
                        {
                            FolderErrorLabel.Text = $"Failed to migrate data: {ex.Message}";
                            FolderErrorLabel.IsVisible = true;
                            return;
                        }
                        finally { SetBusy(false); }
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


        }

        private static void MigrateData(string fromPath, string toPath)
        {
            if (!System.IO.Directory.Exists(fromPath)) return;
            if (System.IO.Directory.Exists(toPath)) return;

            System.IO.Directory.CreateDirectory(toPath);

            var instancesSrc = System.IO.Path.Combine(fromPath, "instances");
            if (System.IO.Directory.Exists(instancesSrc))
                CopyDirectory(instancesSrc, System.IO.Path.Combine(toPath, "instances"));

            var versionsSrc = System.IO.Path.Combine(fromPath, "versions");
            if (System.IO.Directory.Exists(versionsSrc))
                CopyDirectory(versionsSrc, System.IO.Path.Combine(toPath, "versions"));

            var skinsSrc = System.IO.Path.Combine(fromPath, "citrine-skins");
            if (System.IO.Directory.Exists(skinsSrc))
                CopyDirectory(skinsSrc, System.IO.Path.Combine(toPath, "citrine-skins"));

            var authlibSrc = System.IO.Path.Combine(fromPath, "authlib-injector.jar");
            if (System.IO.File.Exists(authlibSrc))
                System.IO.File.Copy(authlibSrc, System.IO.Path.Combine(toPath, "authlib-injector.jar"), overwrite: true);
        }

        private static void CopyDirectory(string src, string dst)
        {
            System.IO.Directory.CreateDirectory(dst);
            foreach (var file in System.IO.Directory.GetFiles(src, "*", System.IO.SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(src, file);
                var destFile = System.IO.Path.Combine(dst, relative);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destFile)!);
                System.IO.File.Copy(file, destFile, overwrite: true);
            }
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
            ChangeFolderButton.Content = isEditing ? "Save" : "Change";
        }

        public void SwitchToTab(string tabName)
        {
            ShowSection(tabName);
        }

        public void ShowSection(string sectionName)
        {
            if (IsBusy) return;
            GeneralSection.IsVisible = sectionName == "General";
            AccountsSection.IsVisible = sectionName == "Accounts";
            AppearanceSection.IsVisible = sectionName == "Appearance";
            PerformanceSection.IsVisible = sectionName == "Performance";

            // Reset folder edit mode when switching sections so the UI doesn't stay in edit state
            if (_isEditingFolder)
                SetFolderEditMode(false);
        }

        private void UpdateAccountActions()
        {
            EditAccountButton.IsEnabled = AccountsList.SelectedItem is Account { Type: "Offline" };
            RemoveAccountButton.IsEnabled = AccountsList.SelectedItem is Account;
        }

        private void BeginAccountEdit(Account? account)
        {
            if (IsBusy) return;
            _editingAccount = account;
            UsernameInput.Text = account?.Username ?? string.Empty;
            SaveAccountButton.Content = account == null ? "Add" : "Save";
            AccountErrorLabel.IsVisible = false;
            AccountEditor.IsVisible = true;
            UsernameInput.Focus();
        }

        private void CancelAccountEdit()
        {
            _editingAccount = null;
            AccountEditor.IsVisible = false;
            AccountErrorLabel.IsVisible = false;
        }

        private void SaveOfflineAccount()
        {
            if (IsBusy) return;
            var username = UsernameInput.Text?.Trim() ?? string.Empty;
            string? error = username.Length < 3 || username.Length > 16
                ? "Username must be 3–16 characters."
                : !Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$")
                    ? "Only letters, numbers and underscores allowed." : null;
            var settings = Settings.Instance;
            if (error == null && settings.Accounts.Any(a => a != _editingAccount && a.Type == "Offline" &&
                string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
                error = "An offline account with this username already exists.";
            if (error != null)
            {
                AccountErrorLabel.Text = error;
                AccountErrorLabel.IsVisible = true;
                return;
            }
            Account savedAccount;
            if (_editingAccount is Account account)
            {
                savedAccount = account;
                var previousUsername = account.Username;
                if (!string.Equals(previousUsername, username, StringComparison.OrdinalIgnoreCase))
                {
                    OfflineSkinServer.Shared.Unregister(account);
                    account.Username = username;
                    if (string.Equals(settings.Username, previousUsername, StringComparison.OrdinalIgnoreCase))
                        settings.Username = username;
                    OfflineSkinServer.Shared.Register(account);
                }
            }
            else
            {
                savedAccount = new Account { Username = username, Type = "Offline" };
                settings.Accounts.Add(savedAccount);
            }
            settings.Save();
            RefreshAccountList();
            AccountsList.SelectedItem = savedAccount;
            CancelAccountEdit();
            SettingsSaved?.Invoke(this, settings);
        }

        private void EditMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (AccountsList.SelectedItem is Account { Type: "Offline" } account)
                BeginAccountEdit(account);
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;
            if (AccountsList.SelectedItem is Account selectedAccount)
            {
                // Unregister from skin server before removing
                if (selectedAccount.Type == "Offline")
                    OfflineSkinServer.Shared.Unregister(selectedAccount);
                else if (selectedAccount.Type == "Microsoft")
                    MicrosoftAuth.ClearCache(selectedAccount.Id);

                if (Settings.Instance.RemoveAccount(selectedAccount))
                {
                    if (_editingAccount == selectedAccount) CancelAccountEdit();
                    RefreshAccountList();
                    SettingsSaved?.Invoke(this, Settings.Instance);
                }
            }
        }


    }
}
