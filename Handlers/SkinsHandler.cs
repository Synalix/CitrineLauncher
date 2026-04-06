using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CitrineLauncher.Handlers
{
    public static class SkinsHandler
    {
        private static SkinsPanel? _currentPanel;

        public static bool IsOpen => _currentPanel?.GetVisualRoot() != null;

        public static void ShowSkins(Panel centerPanel)
        {
            if (_currentPanel != null)
            {
                if (_currentPanel.GetVisualRoot() != null) return;
                _currentPanel = null;
            }

            if (centerPanel == null) return;

            _currentPanel = new SkinsPanel();
            _currentPanel.CloseRequested += (s, e) => HideSkins(centerPanel);
            centerPanel.Children.Add(_currentPanel);
        }

        public static void HideSkins(Panel centerPanel)
        {
            if (_currentPanel == null) return;
            centerPanel.Children.Remove(_currentPanel);
            _currentPanel = null;
        }
    }
}
