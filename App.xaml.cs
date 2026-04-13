using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using ServeX.Services;
using ServeX.ViewModels;
using ServeX.Views;

namespace ServeX
{
    [SupportedOSPlatform("windows")]
    public partial class App : Application
    {
        private readonly DatabaseInitializationService _databaseInitializationService = new DatabaseInitializationService();
        private readonly LicenseService _licenseService = new LicenseService();
        private readonly ApplicationSettingsService _applicationSettingsService = new ApplicationSettingsService();
        private readonly LogService _logService = new LogService();

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            try
            {
                await _databaseInitializationService.InitializeAsync();
                var applicationAccessService = new ApplicationAccessService(_licenseService);
                var startupFlow = await applicationAccessService.GetStartupFlowAsync();

                AuthWindow authWindow = null;
                var authViewModel = new AuthWindowViewModel(
                    applicationAccessService,
                    _applicationSettingsService,
                    _licenseService,
                    async user =>
                    {
                        var settings = await _applicationSettingsService.GetSettingsAsync();
                        if (settings == null)
                        {
                            throw new InvalidOperationException("ServeX settings could not be loaded after login.");
                        }

                        var mainViewModel = new MainViewModel(settings, user);
                        await mainViewModel.LoadAsync();

                        var mainWindow = new MainWindow
                        {
                            DataContext = mainViewModel
                        };

                        MainWindow = mainWindow;
                        mainWindow.Show();
                        authWindow?.Close();
                    });

                await authViewModel.InitializeAsync(startupFlow);

                authWindow = new AuthWindow
                {
                    DataContext = authViewModel
                };

                MainWindow = authWindow;
                authWindow.Show();
            }
            catch (Exception exception)
            {
                _logService.LogError("ServeX startup failed", exception);
                MessageBox.Show(
                    "ServeX could not start.\n\n" + exception.Message,
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _logService.LogError("Unhandled UI exception", e.Exception);
            e.Handled = true;

            MessageBox.Show(
                e.Exception.Message,
                "ServeX",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
