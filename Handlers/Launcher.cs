using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CitrineLauncher.Models;
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
            var currentLauncher = ConfigureLauncher(Handlers.Settings.Instance.MinecraftPath);

            // Load instances — this also does migration from LastVersion if needed
            Handlers.InstanceManager.Instance.Load();

            Dispatcher.UIThread.Post(() =>
            {
                RefreshInstanceList();
            });
        }

        private async void BtnLaunch_Click(object? sender, RoutedEventArgs e)
        {
            string username = Handlers.Settings.Instance.Username;

            if (string.IsNullOrEmpty(username) || cbInstances.SelectedItem is not GameInstance instance) return;
            btnLaunch.IsEnabled = false;

            try
            {
                var settings = Handlers.Settings.Instance;

                // Save last-used version for future migration fallback
                settings.LastVersion = instance.GameVersion;

                // If Fabric: install the Fabric profile JSON before CmlLib's InstallAsync
                if (instance.Loader == LoaderType.Fabric)
                {
                    lblStatus.Text = "Installing Fabric...";
                    await Handlers.InstanceManager.InstallFabricAsync(
                        instance.GameVersion,
                        instance.LoaderVersion,
                        settings.MinecraftPath);
                }

                lblStatus.Text = "Installing game files...";
                await launcher!.InstallAsync(instance.ResolvedVersion);

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
                    MinimumRamMb = settings.MinRam,
                    GameLauncherName = "CitrineLauncher",
                    // Per-instance game directory: mods/configs stay isolated per instance
                    ExtraGameArguments = new CmlLib.Core.ProcessBuilder.MArgument[]
                    {
                        new("--gameDir"),
                        new(instance.InstanceDirectory)
                    }
                };

                var process = await launcher!.BuildProcessAsync(instance.ResolvedVersion, launchOptions);

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

                if (settings.MinimizeOnLaunch)
                    WindowState = Avalonia.Controls.WindowState.Minimized;
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
            if (!IsLoaded) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (!IsLoaded) return;
                lblStatus.Text = "Ready to Play";
                btnLaunch.IsEnabled = true;
                currentGameProcess = null;
            });
        }
    }
}
