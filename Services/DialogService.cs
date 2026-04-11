using System.Windows;

namespace ServeX.Services
{
    public class DialogService
    {
        public void ShowInformation(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
