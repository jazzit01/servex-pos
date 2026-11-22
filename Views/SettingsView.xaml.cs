using System.Windows;
using System.Windows.Controls;
using ServeX.ViewModels;

namespace ServeX.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private async void ChangePasswordOnClick(object sender, RoutedEventArgs args)
        {
            if (DataContext is not SettingsViewModel settings || settings.IsBusy) return;
            var work = settings.ChangePasswordAsync(CurrentPasswordBox.Password, NewPasswordBox.Password, ConfirmPasswordBox.Password);
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            await work;
        }

        private async void AddStaffOnClick(object sender, RoutedEventArgs args)
        {
            if (DataContext is not SettingsViewModel settings || settings.IsBusy) return;
            var work = settings.AddStaffAsync(StaffPasswordBox.Password);
            StaffPasswordBox.Clear();
            await work;
        }
    }
}
