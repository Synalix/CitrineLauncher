using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Threading;
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

        public static async void ShowSettings(Panel centerPanel, Action<Settings>? onSettingsSaved = null, string? initialTab = null)
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

            _currentPanel = new SettingsPanel()
            {
                Opacity = 0,
                RenderTransform = new ScaleTransform(0.8, 0.8),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
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

            // Small delay for layout pass to complete so Bounds are populated
            await Task.Delay(10);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Bug fix #15: use actual panel width/height instead of hardcoded 850/550
                double panelWidth = _currentPanel.Bounds.Width > 0 ? _currentPanel.Bounds.Width : 750;
                double panelHeight = _currentPanel.Bounds.Height > 0 ? _currentPanel.Bounds.Height : 500;

                var x = Math.Max(0, (centerPanel.Bounds.Width - panelWidth) / 2);
                var y = Math.Max(0, (centerPanel.Bounds.Height - panelHeight) / 2);

                _currentPanel.Margin = new Thickness(x, y, 0, 0);
            });

            // Animate in
            _currentPanel.Opacity = 1;
            if (_currentPanel.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }

            if (!string.IsNullOrEmpty(initialTab))
                _currentPanel.SwitchToTab(initialTab);
        }

        private static async Task HideSettings(Panel centerPanel)
        {
            if (_currentPanel == null) return;

            // Animate out
            _currentPanel.Opacity = 0;
            if (_currentPanel.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 0.8;
                scale.ScaleY = 0.8;
            }

            await Task.Delay(200);
            centerPanel.Children.Remove(_currentPanel);
            _currentPanel = null;
        }
    }
}
