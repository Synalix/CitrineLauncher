using System;
using Avalonia;
using Avalonia.Threading;

namespace CitrineLauncher
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Improvement #22: catch unhandled exceptions so they don't silently kill the process
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fatal error: {ex}");
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
