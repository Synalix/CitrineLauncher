using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
            if (launcher == null)
            {
                Debug.WriteLine("Launch aborted: launcher not initialized");
                return;
            }
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
                await launcher.InstallAsync(instance.ResolvedVersion);

                var account = settings.Accounts.FirstOrDefault(a =>
                    string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

                MSession session;
                if (account?.Type == "Microsoft")
                {
                    var cached = await Handlers.MicrosoftAuth.GetSessionAsync(account.Id);
                    // If the cached session belongs to a different account, force re-auth
                    if (!string.Equals(cached.Username, username, StringComparison.OrdinalIgnoreCase))
                    {
                        Handlers.MicrosoftAuth.ClearCache(account.Id);
                        cached = await Handlers.MicrosoftAuth.GetSessionAsync(account.Id);
                        // Validate the re-authenticated session still matches
                        if (!string.Equals(cached.Username, username, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"Launch aborted: re-authenticated session '{cached.Username}' does not match selected account '{username}'");
                            lblStatus.Text = "Launch Failed — account mismatch";
                            btnLaunch.IsEnabled = true;
                            return;
                        }
                    }
                    session = cached;
                }
                else
                {
                    // Use the account's stable UUID so the skin server can identify it
                    var offlineUuid = account?.GetOrCreateOfflineUuid() ?? Guid.NewGuid().ToString("N");
                    session = new MSession(username, "access_token", offlineUuid)
                    {
                        UserType = "Mojang"
                    };
                }

                // Build extra JVM args — inject authlib-injector for offline accounts with a skin
                var extraJvmArgs = new System.Collections.Generic.List<MArgument>();
                if (account?.Type == "Offline")
                {
                    // If no explicit SkinPath but a fallback exists, use it
                    if (!string.IsNullOrEmpty(account.SkinPath) && !File.Exists(account.SkinPath))
                    {
                        account.SkinPath = string.Empty;
                    }
                    if (string.IsNullOrEmpty(account.SkinPath))
                    {
                        var fallbackPath = Path.Combine(settings.MinecraftPath, "citrine-skins", $"{account.Username}.png");
                        if (File.Exists(fallbackPath))
                            account.SkinPath = fallbackPath;
                    }

                    if (!string.IsNullOrEmpty(account.SkinPath) && File.Exists(account.SkinPath))
                    {
                        try
                        {
                            lblStatus.Text = "Downloading skin agent...";
                            await OfflineSkinServer.EnsureJarAsync();
                            OfflineSkinServer.Shared.Register(account);
                            var agentArg = $"-javaagent:{OfflineSkinServer.JarPath}={OfflineSkinServer.Shared.BaseUrl}";
                            extraJvmArgs.Add(new MArgument(agentArg));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Skin agent setup failed (continuing without it): {ex.Message}");
                        }
                    }
                }

                var launchOptions = new MLaunchOption
                {
                    Session = session,
                    MaximumRamMb = settings.MaxRam,
                    MinimumRamMb = settings.MinRam,
                    GameLauncherName = "CitrineLauncher",
                    ExtraJvmArguments = extraJvmArgs.Count > 0 ? extraJvmArgs : Enumerable.Empty<MArgument>(),
                    ExtraGameArguments = new MArgument[]
                    {
                        new("--gameDir"),
                        new(instance.InstanceDirectory)
                    }
                };

                var process = await launcher.BuildProcessAsync(instance.ResolvedVersion, launchOptions);

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
                currentGameProcess?.Dispose();
                currentGameProcess = null;
            });
        }
    }
}
