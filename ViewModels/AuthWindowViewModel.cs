using System;
using System.Threading.Tasks;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    public class AuthWindowViewModel : ViewModelBase
    {
        private readonly ApplicationAccessService _applicationAccessService;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly LicenseService _licenseService;
        private readonly Func<User, Task> _onAuthenticatedAsync;
        private object _currentViewModel;
        private string _windowTitle = "ServeX";
        private string _panelTitle = "ServeX";
        private string _panelSubtitle = "Restaurant operations, billing, and service flow in one place.";

        public AuthWindowViewModel(
            ApplicationAccessService applicationAccessService,
            ApplicationSettingsService applicationSettingsService,
            LicenseService licenseService,
            Func<User, Task> onAuthenticatedAsync)
        {
            _applicationAccessService = applicationAccessService ?? throw new ArgumentNullException(nameof(applicationAccessService));
            _applicationSettingsService = applicationSettingsService ?? throw new ArgumentNullException(nameof(applicationSettingsService));
            _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
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

            await ShowLoginAsync();
        }

        private void ShowSetup()
        {
            WindowTitle = "ServeX Setup";
            PanelTitle = "First-time setup";
            PanelSubtitle = "Configure your restaurant and create the admin account. License is optional in local development.";
            CurrentViewModel = new SetupViewModel(_applicationAccessService, _licenseService, OnSetupCompletedAsync);
        }

        private async Task ShowLoginAsync(string preferredUsername = "", string statusMessage = "")
        {
            var settings = await _applicationSettingsService.GetSettingsAsync();
            var restaurantName = settings?.RestaurantName ?? "ServeX";

            WindowTitle = "ServeX Login";
            PanelTitle = restaurantName;
            PanelSubtitle = "Sign in to continue managing service, orders, billing, and the live floor.";
            CurrentViewModel = new LoginViewModel(
                _applicationAccessService,
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
