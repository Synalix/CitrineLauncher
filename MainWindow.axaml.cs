using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CmlLib.Core;
using CitrineLauncher.Handlers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CitrineLauncher
{
    public partial class MainWindow : Window
    {
        private MinecraftLauncher? launcher;
        private Process? currentGameProcess;
        private readonly Random _random = new Random();
        private CancellationTokenSource? _particleCts;

        public MainWindow()
        {
            InitializeComponent();

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            // Wire up Dispatcher unhandled exception handler
            Dispatcher.UIThread.UnhandledException += (s, e) =>
            {
                Debug.WriteLine($"Unhandled UI exception: {e.Exception}");
                e.Handled = true;
            };

            // 1. Load instances + versions
            InitializeLauncher();

            // 2. Setup account list
            RefreshAccountList();

            // 3. Setup events
            btnSettings.Click += BtnSettings_Click;
            btnLaunch.Click += BtnLaunch_Click;
            btnFolder.Click += BtnFolder_Click;
            btnUpdate.Click += BtnUpdate_Click;
            btnSkins.Click += BtnSkins_Click;
            addAccountButton.Click += AddAccountButton_Click;
            btnNewInstance.Click += BtnNewInstance_Click;
            btnDeleteInstance.Click += BtnDeleteInstance_Click;

            accountCombo.SelectionChanged += (s, e) =>
            {
                UpdateLaunchButtonState();
                UpdateTitleText();
                SaveSelectedUsername();
            };

            cbInstances.SelectionChanged += (s, e) =>
            {
                UpdateLaunchButtonState();
                btnDeleteInstance.IsEnabled = cbInstances.SelectedItem != null;
            };

            // 4. Window controls
            MinimizeButton.Click += (s, e) => WindowState = Avalonia.Controls.WindowState.Minimized;
            MaximizeButton.Click += (s, e) =>
            {
                WindowState = WindowState == Avalonia.Controls.WindowState.Maximized
                    ? Avalonia.Controls.WindowState.Normal
                    : Avalonia.Controls.WindowState.Maximized;
                UpdateMaximizeIcon();
            };
            CloseButton.Click += (s, e) => Close();

            // Bug fix #4: single handler on TitleBar - merged move+double-click into OnTitleBarPointerPressed
            TitleBar.PointerPressed += OnTitleBarPointerPressed;

            // 5. Effects
            ToggleParticlesButton.Click += ToggleParticlesButton_Click;
            ToggleParticlesButton.Opacity = Settings.Instance.ParticlesEnabled ? 1.0 : 0.5;
            Opened += (_, _) => StartParticleAnimation();

            this.Closed += (s, e) =>
            {
                Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
                _particleCts?.Cancel();
                // Unsubscribe game process event so it doesn't post to a dead window
                if (currentGameProcess != null)
                    currentGameProcess.Exited -= GameProcess_Exited;
            };
        }

        internal void RefreshInstanceList()
        {
            var instances = Handlers.InstanceManager.Instance.Instances;
            cbInstances.ItemsSource = null;
            cbInstances.ItemsSource = instances;
            if (instances.Count > 0)
                cbInstances.SelectedIndex = 0;
            UpdateLaunchButtonState();
            btnDeleteInstance.IsEnabled = cbInstances.SelectedItem != null;
        }

        private async void BtnNewInstance_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new NewInstanceDialog(launcher!);
            var result = await dialog.ShowDialog<CitrineLauncher.Models.GameInstance?>(this);
            if (result != null)
            {
                RefreshInstanceList();
                cbInstances.SelectedItem = result;
            }
        }

        private async void BtnDeleteInstance_Click(object? sender, RoutedEventArgs e)
        {
            if (cbInstances.SelectedItem is not CitrineLauncher.Models.GameInstance instance) return;

            var dialog = new ConfirmDialog($"Delete \"{instance.Name}\"?",
                "This will permanently delete the instance folder and all its contents.");
            var confirmed = await dialog.ShowDialog<bool>(this);

            if (confirmed)
            {
                Handlers.InstanceManager.Instance.DeleteInstance(instance);
                RefreshInstanceList();
            }
        }

        private void RefreshAccountList()
        {
            var accounts = Settings.Instance.Accounts.ToArray();
            var selectedAccount = accountCombo.SelectedItem as Account;
            var savedUsername = Settings.Instance.Username;

            accountCombo.ItemsSource = accounts;

            if (accounts.Length > 0)
            {
                var match = accounts.FirstOrDefault(a => a.Username == savedUsername);
                var fallbackSelection = selectedAccount != null && accounts.Contains(selectedAccount)
                    ? selectedAccount
                    : match ?? accounts[0];

                accountCombo.SelectedIndex = Array.IndexOf(accounts, fallbackSelection);
            }
            else
            {
                accountCombo.SelectedIndex = -1;
            }

            UpdateLaunchButtonState();
            UpdateTitleText();
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.MinecraftPath))
            {
                ConfigureLauncher(Settings.Instance.MinecraftPath);
            }
        }

        private void UpdateLaunchButtonState()
        {
            btnLaunch.IsEnabled = accountCombo.SelectedItem != null && cbInstances.SelectedItem != null;
        }

        private void UpdateTitleText()
        {
            TitleText.Text = accountCombo.SelectedItem is Account selected
                ? selected.Username
                : "citrine launcher";
        }

        private void SaveSelectedUsername()
        {
            Settings.Instance.Username = accountCombo.SelectedItem is Account selected
                ? selected.Username
                : string.Empty;
        }

        private void OpenSettings(string? initialTab = null)
        {
            if (Handlers.SkinsHandler.IsOpen)
                Handlers.SkinsHandler.HideSkins(CenterPanel);
            SettingsHandler.ShowSettings(CenterPanel, HandleSettingsSaved, initialTab);
        }

        private void HandleSettingsSaved(Settings savedSettings)
        {
            if (!savedSettings.ParticlesEnabled)
                ParticleCanvas.Children.Clear();

            RefreshAccountList();
        }

        private void AddAccountButton_Click(object? sender, RoutedEventArgs e)
        {
            OpenSettings("Accounts");
        }

        private void BtnSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (SettingsHandler.IsOpen)
            {
                SettingsHandler.CloseSettings(CenterPanel);
                return;
            }

            OpenSettings();
        }

        private void BtnUpdate_Click(object? sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Update clicked - not implemented yet");
        }

        private void BtnSkins_Click(object? sender, RoutedEventArgs e)
        {
            if (Handlers.SkinsHandler.IsOpen)
            {
                Handlers.SkinsHandler.HideSkins(CenterPanel);
                return;
            }

            Handlers.SkinsHandler.ShowSkins(CenterPanel);
        }
    }
}
