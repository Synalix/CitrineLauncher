using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CitrineLauncher
{
    public partial class MainWindow
    {
        private MinecraftLauncher ConfigureLauncher(string minecraftPath)
        {
            var path = new MinecraftPath(minecraftPath);
            launcher = new MinecraftLauncher(path);

            launcher.FileProgressChanged += (s, args) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (pbDownload != null && lblStatus != null)
                    {
                        pbDownload.Maximum = args.TotalTasks;
                        pbDownload.Value = args.ProgressedTasks;
                        lblStatus.Text = $"{args.Name} ({args.ProgressedTasks}/{args.TotalTasks})";
                    }
                });
            };

            return launcher;
        }

        private async void InitializeLauncher()
        {
            // Bug fix #1: always read from Settings so custom paths work
            var currentLauncher = ConfigureLauncher(Handlers.Settings.Instance.MinecraftPath);

            try
            {
                var versions = await currentLauncher.GetAllVersionsAsync();

                Dispatcher.UIThread.Post(() =>
                {
                    cbVersions.Items.Clear();
                    foreach (var v in versions)
                    {
                        if (v.Type == "release" && !string.IsNullOrEmpty(v.Name))
                        {
                            cbVersions.Items.Add(v.Name);
                        }
                    }

                    if (cbVersions.Items.Count > 0)
                        cbVersions.SelectedIndex = 0;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    lblStatus.Text = "Error loading versions";
                });

                Debug.WriteLine($"Launcher Error: {ex.Message}");
            }
        }

        private async void BtnLaunch_Click(object? sender, RoutedEventArgs e)
        {
            string username = Handlers.Settings.Instance.Username;

            if (string.IsNullOrEmpty(username) || cbVersions.SelectedItem == null) return;
            string selectedVersion = cbVersions.SelectedItem.ToString()!;
            btnLaunch.IsEnabled = false;

            try
            {
                var settings = Handlers.Settings.Instance;

                // Bug fix: removed redundant Task.Run wrapper - InstallAsync is already async
                await launcher!.InstallAsync(selectedVersion);

                var account = settings.Accounts.FirstOrDefault(a =>
                    string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

                MSession session;
                if (account?.Type == "Microsoft")
                    session = await Handlers.MicrosoftAuth.GetSessionAsync();
                else
                    session = MSession.CreateOfflineSession(username);

                var launchOptions = new MLaunchOption
                {
                    Session = session,
                    MaximumRamMb = settings.MaxRam,
                    // Bug fix #10: MinRam was saved but never used
                    MinimumRamMb = settings.MinRam
                };

                var process = await launcher!.BuildProcessAsync(selectedVersion, launchOptions);

                // Bug fix #11: apply ShowConsole setting
                if (!settings.ShowConsole)
                {
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                }

                currentGameProcess = process;
                currentGameProcess.EnableRaisingEvents = true;
                currentGameProcess.Exited += GameProcess_Exited;
                process.Start();
                lblStatus.Text = "Game Running...";

                // Bug fix #23: was called AutoCloseLauncher but actually minimized
                if (settings.MinimizeOnLaunch)
                {
                    WindowState = Avalonia.Controls.WindowState.Minimized;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Launch error: {ex.Message}");
                lblStatus.Text = "Launch Failed";
                btnLaunch.IsEnabled = true;
            }
        }

        private void GameProcess_Exited(object? sender, EventArgs e)
        {
            // Bug fix #3: check window is still alive before posting to UI
            if (!IsLoaded) return;

            Dispatcher.UIThread.Post(() =>
            {
                // Double-check after the post as well
                if (!IsLoaded) return;
                lblStatus.Text = "Ready to Play";
                btnLaunch.IsEnabled = true;
                currentGameProcess = null;
            });
        }
    }
}
