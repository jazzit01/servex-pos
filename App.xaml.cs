using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ServeX.Application;
using ServeX.Infrastructure.Persistence;
using ServeX.Services;
using ServeX.Views;

namespace ServeX
{
    [SupportedOSPlatform("windows")]
    public partial class App : System.Windows.Application
    {
        private ServiceProvider _services;
        private LogService _logService = new LogService();
        private Mutex _instanceMutex;
        private bool _ownsMutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            try
            {
                _instanceMutex = new Mutex(false, @"Local\ServeX.POS");
                try { _ownsMutex = _instanceMutex.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsMutex = true; }
                if (!_ownsMutex)
                {
                    MessageBox.Show("ServeX is already running in this Windows session.", "ServeX", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                bool demo = e.Args.Contains("--demo");
#if !DEBUG
                if (demo) throw new InvalidOperationException("Demo startup is available only in a development build.");
#endif
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var location = demo
                    ? new DatabaseLocation(Path.Combine(local, "ServeX", "Demo", "servex-demo.db"))
                    : DatabaseLocation.Resolve(AppContext.BaseDirectory, local, Environment.GetEnvironmentVariable("SERVEX_DATABASE_PATH"));
                Directory.CreateDirectory(Path.GetDirectoryName(location.FilePath));
                _services = CompositionRoot.Build(location);
                _logService = _services.GetRequiredService<LogService>();
                _logService.LogInfo("Starting ServeX; database: " + location.FilePath + (demo ? " (explicit demo)" : ""));

                while (true)
                {
                    try
                    {
                        // SQLite's async API may perform synchronous I/O. Keep migration work off the UI dispatcher.
                        await Task.Run(() => _services.GetRequiredService<DatabaseInitializationService>().InitializeAsync());
                        break;
                    }
                    catch (Exception exception)
                    {
                        _logService.LogError("Database initialization failed", exception);
                        if (MessageBox.Show("The database could not be initialized. No replacement database will be used.\n\nCheck disk space, folder access and database availability, then choose Yes to retry.\nDiagnostics: " + LogService.LogFilePath,
                            "ServeX Database", MessageBoxButton.YesNo, MessageBoxImage.Error) != MessageBoxResult.Yes)
                        { Shutdown(-1); return; }
                    }
                }
                if (demo)
                    await _services.GetRequiredService<DemoDataService>().SeedEmptyDemoAsync();

                var access = _services.GetRequiredService<ApplicationAccessService>();
                var settingsService = _services.GetRequiredService<ApplicationSettingsService>();
                var factory = _services.GetRequiredService<PresentationFactory>();
                var flow = await access.GetStartupFlowAsync();
                AuthWindow authWindow = null;
                var authViewModel = factory.CreateAuth(async user =>
                {
                    try
                    {
                        var settings = await settingsService.GetSettingsAsync()
                            ?? throw new InvalidOperationException("ServeX settings could not be loaded after login.");
                        var mainViewModel = factory.CreateMain(settings, user);
                        await mainViewModel.LoadAsync();
                        var mainWindow = _services.GetRequiredService<MainWindow>();
                        mainWindow.DataContext = mainViewModel;
                        MainWindow = mainWindow;
                        mainWindow.Show();
                        authWindow?.Close();
                    }
                    catch (Exception exception)
                    {
                        _logService.LogError("Opening authenticated workspace failed", exception);
                        throw;
                    }
                });
                await authViewModel.InitializeAsync(flow);
                authWindow = _services.GetRequiredService<AuthWindow>();
                authWindow.DataContext = authViewModel;
                MainWindow = authWindow;
                authWindow.Show();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception exception)
            {
                _logService.LogError("ServeX startup failed", exception);
                MessageBox.Show("ServeX could not start.\n\n" + exception.Message + "\n\nDiagnostics: " + LogService.LogFilePath,
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _logService.LogError("Unhandled UI exception; shutting down", e.Exception);
            e.Handled = true;
            MessageBox.Show("An unexpected error occurred. ServeX will close to avoid further operations in an uncertain state.\nRestart ServeX and check your last operation before retrying.\n\nDiagnostics: " + LogService.LogFilePath,
                "ServeX Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
            _logService.LogError("Unhandled process exception", e.ExceptionObject as Exception);
        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            _logService.LogError("Unobserved task failure", e.Exception);
            e.SetObserved();
        }
        protected override void OnExit(ExitEventArgs e)
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            _services?.Dispose();
            if (_ownsMutex) _instanceMutex.ReleaseMutex();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
