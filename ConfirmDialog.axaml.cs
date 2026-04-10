using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CitrineLauncher
{
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string body)
        {
            InitializeComponent();
            TitleText.Text = title;
            BodyText.Text = body;
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
        private void ConfirmButton_Click(object? sender, RoutedEventArgs e) => Close(true);
    }
}
