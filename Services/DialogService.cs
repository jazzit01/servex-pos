using System.Windows;

namespace ServeX.Services
{
    public class DialogService
    {
        public bool Confirm(string title, string message)
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            return (owner == null
                ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)) == MessageBoxResult.Yes;
        }

        public void ShowInformation(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
