using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Services;

namespace ServeX.ViewModels
{
    public class SetupViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly LogService _logService;
        private readonly ApplicationAccessService _applicationAccessService;
        private readonly Func<string, Task> _onSetupCompletedAsync;
        private bool _isBusy;
        private string _restaurantName = string.Empty;
        private string _address = string.Empty;
        private string _phone = string.Empty;
        private string _taxPercentageInput = "5";
        private string _adminUsername = string.Empty;
        private string _adminPassword = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public SetupViewModel(
            ApplicationAccessService applicationAccessService,
            Func<string, Task> onSetupCompletedAsync, LogService logService)
        {
            _logService = logService;
            _applicationAccessService = applicationAccessService ?? throw new ArgumentNullException(nameof(applicationAccessService));
            _onSetupCompletedAsync = onSetupCompletedAsync ?? throw new ArgumentNullException(nameof(onSetupCompletedAsync));

            SaveSetupCommand = new AsyncRelayCommand(async _ => await SaveSetupAsync(), _ => !IsBusy);
        }

        public AsyncRelayCommand SaveSetupCommand { get; }

        public string RestaurantName
        {
            get => _restaurantName;
            set
            {
                if (SetProperty(ref _restaurantName, value))
                {
                    NotifyValidationState();
                }
            }
        }

        public string Address
        {
            get => _address;
            set
            {
                if (SetProperty(ref _address, value))
                {
                    NotifyValidationState();
                }
            }
        }

        public string Phone
        {
            get => _phone;
            set
            {
                if (SetProperty(ref _phone, value))
                {
                    NotifyValidationState();
                }
            }
        }

        public string TaxPercentageInput
        {
            get => _taxPercentageInput;
            set
            {
                if (SetProperty(ref _taxPercentageInput, value))
                {
                    NotifyValidationState();
                }
            }
        }

        public string AdminUsername
        {
            get => _adminUsername;
            set
            {
                if (SetProperty(ref _adminUsername, value))
                {
                    NotifyValidationState();
                }
            }
        }

        public string AdminPassword
        {
            get => _adminPassword;
            set
            {
                if (SetProperty(ref _adminPassword, value))
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
                    OnPropertyChanged(nameof(CanSaveSetup));
                    SaveSetupCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanSaveSetup => !IsBusy;

        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(RestaurantName):
                        if (string.IsNullOrWhiteSpace(RestaurantName))
                        {
                            return "Restaurant name is required.";
                        }

                        if (RestaurantName.Trim().Length > 120)
                        {
                            return "Restaurant name must be 120 characters or fewer.";
                        }

                        return string.Empty;

                    case nameof(Address):
                        if (string.IsNullOrWhiteSpace(Address))
                        {
                            return "Address is required.";
                        }

                        if (Address.Trim().Length > 250)
                        {
                            return "Address must be 250 characters or fewer.";
                        }

                        return string.Empty;

                    case nameof(Phone):
                        if (string.IsNullOrWhiteSpace(Phone))
                        {
                            return "Phone number is required.";
                        }

                        if (Phone.Trim().Length > 30)
                        {
                            return "Phone number must be 30 characters or fewer.";
                        }

                        return string.Empty;

                    case nameof(TaxPercentageInput):
                        if (string.IsNullOrWhiteSpace(TaxPercentageInput))
                        {
                            return "Tax percentage is required.";
                        }

                        if (!TryParseTaxPercentage(TaxPercentageInput, out var taxPercentage))
                        {
                            return "Enter a valid tax percentage.";
                        }

                        if (taxPercentage < 0m || taxPercentage > 100m)
                        {
                            return "Tax percentage must be between 0 and 100.";
                        }

                        return string.Empty;

                    case nameof(AdminUsername):
                        if (string.IsNullOrWhiteSpace(AdminUsername))
                        {
                            return "Admin username is required.";
                        }

                        if (AdminUsername.Trim().Length < 3)
                        {
                            return "Admin username must be at least 3 characters.";
                        }

                        if (AdminUsername.Trim().Length > 50)
                        {
                            return "Admin username must be 50 characters or fewer.";
                        }

                        return string.Empty;

                    case nameof(AdminPassword):
                        if (string.IsNullOrWhiteSpace(AdminPassword))
                        {
                            return "Admin password is required.";
                        }

                        if (AdminPassword.Length < 4)
                        {
                            return "Admin password must be at least 4 characters.";
                        }

                        return string.Empty;

                    default:
                        return string.Empty;
                }
            }
        }

        private async Task SaveSetupAsync()
        {
            TouchValidation();
            if (!IsFormValid())
            {
                SetStatus(GetValidationSummary(), true);
                return;
            }

            if (!TryParseTaxPercentage(TaxPercentageInput, out var taxPercentage))
            {
                SetStatus("Enter a valid tax percentage before saving setup.", true);
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                await _applicationAccessService.SaveInitialSetupAsync(new InitialSetupRequest
                {
                    RestaurantName = RestaurantName.Trim(),
                    Address = Address.Trim(),
                    Phone = Phone.Trim(),
                    TaxPercentage = taxPercentage,
                    AdminUsername = AdminUsername.Trim(),
                    AdminPassword = AdminPassword
                });

                await _onSetupCompletedAsync(AdminUsername.Trim());
            }, "Setup could not be saved.");
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
            catch (DbUpdateException exception)
            {
                _logService.LogError(fallbackMessage, exception);
                SetStatus(fallbackMessage, true);
            }
            catch (Exception exception)
            {
                _logService.LogError(fallbackMessage, exception);
                SetStatus(fallbackMessage, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool IsFormValid()
        {
            return string.IsNullOrEmpty(this[nameof(RestaurantName)]) &&
                   string.IsNullOrEmpty(this[nameof(Address)]) &&
                   string.IsNullOrEmpty(this[nameof(Phone)]) &&
                   string.IsNullOrEmpty(this[nameof(TaxPercentageInput)]) &&
                   string.IsNullOrEmpty(this[nameof(AdminUsername)]) &&
                   string.IsNullOrEmpty(this[nameof(AdminPassword)]);
        }

        private string GetValidationSummary()
        {
            var errors = new[]
            {
                this[nameof(RestaurantName)],
                this[nameof(Address)],
                this[nameof(Phone)],
                this[nameof(TaxPercentageInput)],
                this[nameof(AdminUsername)],
                this[nameof(AdminPassword)]
            }
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToList();

            return errors.Any()
                ? string.Join(" ", errors)
                : "Please complete the setup details before continuing.";
        }

        private void NotifyValidationState()
        {
            OnPropertyChanged(nameof(CanSaveSetup));
            SaveSetupCommand.RaiseCanExecuteChanged();
        }

        private void TouchValidation()
        {
            OnPropertyChanged(nameof(RestaurantName));
            OnPropertyChanged(nameof(Address));
            OnPropertyChanged(nameof(Phone));
            OnPropertyChanged(nameof(TaxPercentageInput));
            OnPropertyChanged(nameof(AdminUsername));
            OnPropertyChanged(nameof(AdminPassword));
            NotifyValidationState();
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsStatusError = isError;
        }

        private static bool TryParseTaxPercentage(string value, out decimal taxPercentage)
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out taxPercentage) ||
                   decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out taxPercentage);
        }
    }
}
