using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CmlLib.Core;
using CitrineLauncher.Handlers;
using System;
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

            // Wire up Dispatcher unhandled exception handler
            Dispatcher.UIThread.UnhandledException += (s, e) =>
            {
                Debug.WriteLine($"Unhandled UI exception: {e.Exception}");
                e.Handled = true;
            };

            // 1. Load versions
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

            accountCombo.SelectionChanged += (s, e) =>
            {
                UpdateLaunchButtonState();
                UpdateTitleText();
                SaveSelectedUsername();
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
            StartParticleAnimation();

            this.Closed += (s, e) =>
            {
                _particleCts?.Cancel();
                // Unsubscribe game process event so it doesn't post to a dead window
                if (currentGameProcess != null)
                    currentGameProcess.Exited -= GameProcess_Exited;
            };
        }

        private void RefreshAccountList()
        {
            var accounts = Settings.Instance.Accounts;
            var selectedAccount = accountCombo.SelectedItem as Account;

            accountCombo.ItemsSource = null;
            accountCombo.ItemsSource = accounts;

            if (accounts.Count > 0)
            {
                if (selectedAccount != null && accounts.Contains(selectedAccount))
                {
                    accountCombo.SelectedItem = selectedAccount;
                }
                else
                {
                    var savedUsername = Settings.Instance.Username;
                    var match = accounts.FirstOrDefault(a => a.Username == savedUsername);
                    accountCombo.SelectedItem = match ?? accounts[0];
                }
            }
            else
            {
                accountCombo.SelectedItem = null;
            }

            UpdateLaunchButtonState();
            UpdateTitleText();
        }

        private void UpdateLaunchButtonState()
        {
            btnLaunch.IsEnabled = accountCombo.SelectedItem != null;
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

        private void AddAccountButton_Click(object? sender, RoutedEventArgs e)
        {
            SettingsHandler.ShowSettings(
                CenterPanel,
                (savedSettings) =>
                {
                    if (!savedSettings.ParticlesEnabled)
                        ParticleCanvas.Children.Clear();
                    RefreshAccountList();
                },
                "Accounts"
            );
        }

        private void BtnSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (SettingsHandler.IsOpen)
            {
                SettingsHandler.CloseSettings(CenterPanel);
                return;
            }
            SettingsHandler.ShowSettings(
                CenterPanel,
                (savedSettings) =>
                {
                    if (!savedSettings.ParticlesEnabled)
                        ParticleCanvas.Children.Clear();
                    RefreshAccountList();
                }
            );
        }

        private void BtnUpdate_Click(object? sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Update clicked - not implemented yet");
        }

        private void BtnSkins_Click(object? sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Skins clicked - not implemented yet");
        }
    }
}
