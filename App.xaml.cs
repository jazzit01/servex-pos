using System;
using System.Windows;
using ServeX.Services;
using ServeX.ViewModels;
using ServeX.Views;

namespace ServeX
{
    public partial class App : Application
    {
        private readonly DatabaseInitializationService _databaseInitializationService = new DatabaseInitializationService();

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                await _databaseInitializationService.InitializeAsync();

                var mainViewModel = new MainViewModel();
                await mainViewModel.LoadAsync();

                var mainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "ServeX could not start.\n\n" + exception.Message,
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
            }
        }
    }
}
