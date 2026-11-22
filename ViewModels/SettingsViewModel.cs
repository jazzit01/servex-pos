using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ServeX.Data;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class SettingsViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly DatabaseBackupService _databaseBackupService;
        private readonly SaveFileDialogService _saveFileDialogService;
        public event Action<string> RestaurantNameSaved;
        private readonly ServeX.Infrastructure.Persistence.DatabaseLocation _location;
        private readonly AsyncRelayCommand _saveSettingsCommand;
        private readonly AsyncRelayCommand _backupDatabaseCommand;
        private readonly LogService _logService;
        private readonly DialogService _dialogs;
        private bool _isBusy;
        private bool _isLoaded;
        private string _restaurantName = string.Empty;
        private string _phone = string.Empty;
        private string _taxPercentageInput = "5";
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public SettingsViewModel(
            ApplicationSettingsService applicationSettingsService,
            DatabaseBackupService databaseBackupService,
            SaveFileDialogService saveFileDialogService,
            ServeX.Infrastructure.Persistence.DatabaseLocation location, LogService logService, DialogService dialogs)
        {
            _logService = logService;
            _dialogs = dialogs;
            _applicationSettingsService = applicationSettingsService ?? throw new ArgumentNullException(nameof(applicationSettingsService));
            _databaseBackupService = databaseBackupService ?? throw new ArgumentNullException(nameof(databaseBackupService));
            _saveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
            _location = location;
            _saveSettingsCommand = new AsyncRelayCommand(async _ => await SaveSettingsAsync(), _ => !IsBusy);
            _backupDatabaseCommand = new AsyncRelayCommand(async _ => await BackupDatabaseAsync(), _ => !IsBusy);
            RestoreDatabaseCommand = new AsyncRelayCommand(_ => RestoreDatabaseAsync(), _ => !IsBusy);
        }

        public string DatabasePath => _location.FilePath;

        public string DatabaseFileName => Path.GetFileName(DatabasePath);

        public AsyncRelayCommand SaveSettingsCommand => _saveSettingsCommand;

        public AsyncRelayCommand BackupDatabaseCommand => _backupDatabaseCommand;

        public AsyncRelayCommand RestoreDatabaseCommand { get; }

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

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    _saveSettingsCommand.RaiseCanExecuteChanged();
                    _backupDatabaseCommand.RaiseCanExecuteChanged();
                    RestoreDatabaseCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            private set => SetProperty(ref _isLoaded, value);
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

                    default:
                        return string.Empty;
                }
            }
        }

        public async Task LoadAsync()
        {
            try
            {
                await ExecuteBusyActionAsync(async () =>
                {
                    var settings = await _applicationSettingsService.GetSettingsAsync()
                        ?? throw new InvalidOperationException("ServeX settings are missing. Setup must be completed first.");
                    ApplySettings(settings);
                    ClearStatus();
                    IsLoaded = true;
                }, "Restaurant settings could not be loaded.");
            }
            catch (Exception exception)
            {
                _logService.LogError("Settings view model load failed", exception);
                ApplySettings(null);
                SetStatus(GetDevelopmentSafeMessage("Restaurant settings could not be loaded.", exception), true);
            }
        }

        private async Task SaveSettingsAsync()
        {
            TouchValidation();
            if (!IsFormValid())
            {
                SetStatus(GetValidationSummary(), true);
                return;
            }

            if (!TryParseTaxPercentage(TaxPercentageInput, out var taxPercentage))
            {
                SetStatus("Enter a valid tax percentage before saving.", true);
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                var updatedSettings = await _applicationSettingsService.UpdateSettingsAsync(
                    RestaurantName,
                    Phone,
                    taxPercentage);

                ApplySettings(updatedSettings);
                RestaurantNameSaved?.Invoke(updatedSettings.RestaurantName);
                SetStatus("Settings saved successfully.", false);
                IsLoaded = true;
            }, "Settings could not be saved.");
        }

        private async Task BackupDatabaseAsync()
        {
            if (IsBusy)
            {
                return;
            }

            var suggestedFileName = string.Format(
                "servex-backup-{0:yyyyMMdd-HHmmss}.db",
                DateTime.Now);

            var destinationFilePath = _saveFileDialogService.PickDatabaseBackupPath(suggestedFileName);
            if (string.IsNullOrWhiteSpace(destinationFilePath))
            {
                SetStatus("Backup cancelled.", false);
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                var backupPath = await _databaseBackupService.BackupAsync(destinationFilePath);
                SetStatus("Backup created successfully at " + backupPath, false);
            }, "The database backup could not be created.");
        }

        private async Task RestoreDatabaseAsync()
        {
            var sourcePath = _saveFileDialogService.PickDatabaseRestoreSource();
            if (sourcePath == null || !_dialogs.Confirm("Restore database?",
                "ServeX will restart and restore the selected backup. Orders and settings saved after that backup will be absent from the restored database. A copy of the current database will be kept."))
                return;

            await ExecuteBusyActionAsync(async () =>
            {
                await _databaseBackupService.StageRestoreAsync(sourcePath);
                try
                {
                    ((App)System.Windows.Application.Current).RestartAfterRestore();
                }
                catch (Exception exception)
                {
                    _logService.LogError("ServeX could not restart after staging a restore", exception);
                    _dialogs.ShowInformation("Backup ready", "ServeX will close. Open it again to apply the staged backup.");
                    System.Windows.Application.Current.Shutdown();
                }
            }, "The backup could not be prepared. Choose a valid ServeX backup and try again.");
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
            catch (Exception exception)
            {
                _logService.LogError(fallbackMessage, exception);
                SetStatus(GetDevelopmentSafeMessage(fallbackMessage, exception), true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplySettings(AppSetting settings)
        {
            var safeSettings = settings ?? CreateDefaultSettings();

            RestaurantName = safeSettings.RestaurantName ?? string.Empty;
            Phone = safeSettings.Phone ?? string.Empty;
            TaxPercentageInput = safeSettings.TaxPercentage.ToString("0.##", CultureInfo.CurrentCulture);
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsStatusError = isError;
        }

        private void ClearStatus()
        {
            StatusMessage = string.Empty;
            IsStatusError = false;
        }

        private bool IsFormValid()
        {
            return string.IsNullOrEmpty(this[nameof(RestaurantName)]) &&
                   string.IsNullOrEmpty(this[nameof(Phone)]) &&
                   string.IsNullOrEmpty(this[nameof(TaxPercentageInput)]);
        }

        private string GetValidationSummary()
        {
            var errors = new[]
            {
                this[nameof(RestaurantName)],
                this[nameof(Phone)],
                this[nameof(TaxPercentageInput)]
            }
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToList();

            return errors.Any()
                ? string.Join(" ", errors)
                : "Please complete the restaurant settings before saving.";
        }

        private void NotifyValidationState()
        {
            _saveSettingsCommand.RaiseCanExecuteChanged();
        }

        private void TouchValidation()
        {
            OnPropertyChanged(nameof(RestaurantName));
            OnPropertyChanged(nameof(Phone));
            OnPropertyChanged(nameof(TaxPercentageInput));
            NotifyValidationState();
        }

        private static bool TryParseTaxPercentage(string value, out decimal taxPercentage)
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out taxPercentage) ||
                   decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out taxPercentage);
        }

        private static AppSetting CreateDefaultSettings()
        {
            return new AppSetting
            {
                RestaurantName = "ServeX",
                Phone = string.Empty,
                TaxPercentage = 5m
            };
        }

        private static string GetDevelopmentSafeMessage(string fallbackMessage, Exception exception)
        {
#if DEBUG
            return exception?.Message ?? fallbackMessage;
#else
            return fallbackMessage;
#endif
        }
    }
}
