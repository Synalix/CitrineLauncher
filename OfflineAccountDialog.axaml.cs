using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Text.RegularExpressions;

namespace CitrineLauncher
{
    public partial class OfflineAccountDialog : Window
    {
        public OfflineAccountDialog()
        {
            InitializeComponent();
        }

        public OfflineAccountDialog(string? existingUsername) : this()
        {
            if (!string.IsNullOrEmpty(existingUsername))
                UsernameInput.Text = existingUsername;
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private void AddButton_Click(object? sender, RoutedEventArgs e)
        {
            var username = UsernameInput.Text?.Trim() ?? string.Empty;

            // Bug fix #18: validate username - Minecraft allows 3-16 alphanumeric + underscore
            if (username.Length < 3 || username.Length > 16)
            {
                ErrorText.Text = "Username must be 3–16 characters.";
                ErrorText.IsVisible = true;
                return;
            }

            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
            {
                ErrorText.Text = "Only letters, numbers and underscores allowed.";
                ErrorText.IsVisible = true;
                return;
            }

            Close(username);
        }
    }
}
