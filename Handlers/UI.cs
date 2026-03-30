using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Diagnostics;
using System.IO;

namespace CitrineLauncher
{
    public partial class MainWindow
    {
        // Bug fix #4: merged both PointerPressed handlers into one
        // Previously, two separate handlers meant double-click triggered both BeginMoveDrag AND maximize
        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (e.ClickCount == 2)
            {
                // Double click: toggle maximize
                WindowState = WindowState == Avalonia.Controls.WindowState.Maximized
                    ? Avalonia.Controls.WindowState.Normal
                    : Avalonia.Controls.WindowState.Maximized;
                UpdateMaximizeIcon();
            }
            else
            {
                // Single click: drag
                BeginMoveDrag(e);
            }
        }

        private void UpdateMaximizeIcon()
        {
            if (WindowState == Avalonia.Controls.WindowState.Maximized)
                MaximizePath.Data = Geometry.Parse("M2,2 L8,2 L8,8 L2,8 Z M4,4 L10,4 L10,10 L4,10 Z");
            else
                MaximizePath.Data = Geometry.Parse("M0,0 L12,0 L12,12 L0,12 Z");
        }

        private void BtnFolder_Click(object? sender, RoutedEventArgs e)
        {
            // Bug fix #1: use Settings path, not hardcoded field
            var path = Handlers.Settings.Instance.MinecraftPath;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            OpenFolder(path);
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", path);
                else if (OperatingSystem.IsMacOS()) Process.Start("open", path);
                else if (OperatingSystem.IsLinux()) Process.Start("xdg-open", path);
            }
            catch { }
        }
    }
}
