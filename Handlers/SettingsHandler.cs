using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using CitrineLauncher.Handlers;
using System;
using System.Threading.Tasks;

namespace CitrineLauncher
{
    public static class SettingsHandler
    {
        private static SettingsPanel? _currentPanel;

        public static bool IsOpen => _currentPanel?.GetVisualRoot() != null;

        public static void CloseSettings(Panel centerPanel)
        {
            _ = HideSettings(centerPanel);
        }

        public static void ShowSettings(Panel centerPanel, Action<Settings>? onSettingsSaved = null, string? initialTab = null)
        {
            // Bug fix #2: check VisualRoot != null so a stale reference doesn't block re-opening
            // Note: IsAttachedToVisualTree() was an IVisual extension method removed in Avalonia 11.
            // The Avalonia 11+ equivalent is checking VisualRoot, which is null when detached.
            if (_currentPanel != null)
            {
                if (_currentPanel.GetVisualRoot() != null)
                {
                    if (!string.IsNullOrEmpty(initialTab))
                        _currentPanel.SwitchToTab(initialTab);
                    return;
                }
                else
                {
                    // Panel was removed externally - clear the stale reference
                    _currentPanel = null;
                }
            }

            if (centerPanel == null) return;

            _currentPanel = new SettingsPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _currentPanel.CloseRequested += async (s, e) =>
            {
                await HideSettings(centerPanel);
            };

            _currentPanel.SettingsSaved += (s, savedSettings) =>
            {
                onSettingsSaved?.Invoke(savedSettings);
            };

            centerPanel.Children.Add(_currentPanel);
            _currentPanel.FindControl<Button>("GeneralNav")?.Focus();

            if (!string.IsNullOrEmpty(initialTab))
                _currentPanel.SwitchToTab(initialTab);
        }

        private static Task HideSettings(Panel centerPanel)
        {
            var panel = _currentPanel;
            _currentPanel = null;
            if (panel != null)
            {
                centerPanel.Children.Remove(panel);
                centerPanel.FindAncestorOfType<Window>()?.FindControl<Button>("btnSettings")?.Focus();
            }
            return Task.CompletedTask;
        }
    }
}
