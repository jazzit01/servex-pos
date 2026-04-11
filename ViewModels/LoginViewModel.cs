using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    public class LoginViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly ApplicationAccessService _applicationAccessService;
        private readonly Func<User, Task> _onLoginSucceededAsync;
        private bool _isBusy;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public LoginViewModel(
            ApplicationAccessService applicationAccessService,
            string restaurantName,
            Func<User, Task> onLoginSucceededAsync,
            string preferredUsername = "",
            string initialStatusMessage = "")
        {
            _applicationAccessService = applicationAccessService ?? throw new ArgumentNullException(nameof(applicationAccessService));
            _onLoginSucceededAsync = onLoginSucceededAsync ?? throw new ArgumentNullException(nameof(onLoginSucceededAsync));

            RestaurantName = restaurantName ?? "ServeX";
            _username = preferredUsername ?? string.Empty;
            _statusMessage = initialStatusMessage ?? string.Empty;
            LoginCommand = new RelayCommand(async _ => await LoginAsync(), _ => CanLogin);
        }

        public RelayCommand LoginCommand { get; }

        public string RestaurantName { get; }

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    NotifyValidationState();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                {
                    NotifyValidationState();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    OnPropertyChanged(nameof(HasStatusMessage));
                }
            }
        }

        public bool IsStatusError
        {
            get => _isStatusError;
            private set => SetProperty(ref _isStatusError, value);
        }

        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanLogin));
                    LoginCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanLogin => !IsBusy && IsFormValid();

        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(Username):
                        if (string.IsNullOrWhiteSpace(Username))
                        {
                            return "Username is required.";
                        }

                        return string.Empty;

                    case nameof(Password):
                        if (string.IsNullOrWhiteSpace(Password))
                        {
                            return "Password is required.";
                        }

                        return string.Empty;

                    default:
                        return string.Empty;
                }
            }
        }

        private async Task LoginAsync()
        {
            TouchValidation();
            if (!IsFormValid())
            {
                SetStatus("Enter your username and password to continue.", true);
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                var user = await _applicationAccessService.ValidateLoginAsync(Username, Password);
                if (user == null)
                {
                    SetStatus("Incorrect username or password.", true);
                    return;
                }

                await _onLoginSucceededAsync(user);
            }, "Login failed.");
        }

        private async Task ExecuteBusyActionAsync(Func<Task> action, string fallbackMessage)
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;

            try
            {
                await action();
            }
            catch (DbUpdateException)
            {
                SetStatus(fallbackMessage, true);
            }
            catch (Exception)
            {
                SetStatus(fallbackMessage, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool IsFormValid()
        {
            return string.IsNullOrEmpty(this[nameof(Username)]) &&
                   string.IsNullOrEmpty(this[nameof(Password)]);
        }

        private void NotifyValidationState()
        {
            OnPropertyChanged(nameof(CanLogin));
            LoginCommand.RaiseCanExecuteChanged();
        }

        private void TouchValidation()
        {
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(Password));
            NotifyValidationState();
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsStatusError = isError;
        }
    }
}
