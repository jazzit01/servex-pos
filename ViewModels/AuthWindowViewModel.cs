using System;
using System.Threading.Tasks;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class AuthWindowViewModel : ViewModelBase
    {
        private readonly ServeX.Application.PresentationFactory _factory;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly Func<User, Task> _onAuthenticatedAsync;
        private object _currentViewModel;
        private string _windowTitle = "ServeX";
        private string _panelTitle = "ServeX";
        private string _panelSubtitle = "Restaurant operations, billing, and service flow in one place.";

        public AuthWindowViewModel(
            ApplicationSettingsService applicationSettingsService,
            Func<User, Task> onAuthenticatedAsync, ServeX.Application.PresentationFactory factory)
        {
            _factory = factory;
            _applicationSettingsService = applicationSettingsService ?? throw new ArgumentNullException(nameof(applicationSettingsService));
            _onAuthenticatedAsync = onAuthenticatedAsync ?? throw new ArgumentNullException(nameof(onAuthenticatedAsync));
        }

        public object CurrentViewModel
        {
            get => _currentViewModel;
            private set => SetProperty(ref _currentViewModel, value);
        }

        public string WindowTitle
        {
            get => _windowTitle;
            private set => SetProperty(ref _windowTitle, value);
        }

        public string PanelTitle
        {
            get => _panelTitle;
            private set => SetProperty(ref _panelTitle, value);
        }

        public string PanelSubtitle
        {
            get => _panelSubtitle;
            private set => SetProperty(ref _panelSubtitle, value);
        }

        public async Task InitializeAsync(StartupFlow startupFlow)
        {
            if (startupFlow == StartupFlow.Setup)
            {
                ShowSetup();
                return;
            }

            if (startupFlow != StartupFlow.Login)
                throw new InvalidOperationException("Startup access is blocked. Check the database and application configuration.");
            await ShowLoginAsync();
        }

        private void ShowSetup()
        {
            WindowTitle = "ServeX Setup";
            PanelTitle = "First-time setup";
            PanelSubtitle = "Configure your restaurant and create the admin account.";
            CurrentViewModel = _factory.CreateSetup(OnSetupCompletedAsync);
        }

        private async Task ShowLoginAsync(string preferredUsername = "", string statusMessage = "")
        {
            var settings = await _applicationSettingsService.GetSettingsAsync();
            var restaurantName = settings?.RestaurantName ?? "ServeX";

            WindowTitle = "ServeX Login";
            PanelTitle = restaurantName;
            PanelSubtitle = "Sign in to continue managing service, orders, billing, and the live floor.";
            CurrentViewModel = _factory.CreateLogin(
                restaurantName,
                _onAuthenticatedAsync,
                preferredUsername,
                statusMessage);
        }

        private async Task OnSetupCompletedAsync(string preferredUsername)
        {
            await ShowLoginAsync(preferredUsername, "Setup complete. Sign in with your new admin account.");
        }
    }
}
