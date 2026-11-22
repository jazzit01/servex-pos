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
        private readonly ApplicationAccessService _access;
        private readonly BusinessArchiveService _archives;
        private bool _isBusy;
        private bool _isLoaded;
        private string _restaurantName = string.Empty;
        private string _address = string.Empty;
        private string _phone = string.Empty;
        private string _taxPercentageInput = "5";
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public SettingsViewModel(
            ApplicationSettingsService applicationSettingsService,
            DatabaseBackupService databaseBackupService,
            SaveFileDialogService saveFileDialogService,
            ServeX.Infrastructure.Persistence.DatabaseLocation location, LogService logService, DialogService dialogs, ApplicationAccessService access, BusinessArchiveService archives)
        {
            _logService = logService;
            _dialogs = dialogs;
            _access = access;
            _archives = archives;
            _applicationSettingsService = applicationSettingsService ?? throw new ArgumentNullException(nameof(applicationSettingsService));
            _databaseBackupService = databaseBackupService ?? throw new ArgumentNullException(nameof(databaseBackupService));
            _saveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
            _location = location;
            _saveSettingsCommand = new AsyncRelayCommand(async _ => await SaveSettingsAsync(), _ => !IsBusy);
            _backupDatabaseCommand = new AsyncRelayCommand(async _ => await BackupDatabaseAsync(), _ => !IsBusy);
            ExportDataCommand = new AsyncRelayCommand(_ => SaveArchiveAsync(support: false), _ => !IsBusy);
            SupportBundleCommand = new AsyncRelayCommand(_ => SaveArchiveAsync(support: true), _ => !IsBusy);
            RestoreDatabaseCommand = new AsyncRelayCommand(_ => RestoreDatabaseAsync(), _ => !IsBusy);
        }

        public int CurrentUserId { get; set; }

        public UserRole[] StaffRoles { get; } = Enum.GetValues<UserRole>();

        public UserRole NewStaffRole { get; set; } = UserRole.Cashier;

        public string NewStaffName { get; set; }

        public Task AddStaffAsync(string password) => ExecuteBusyActionAsync(async () =>
        {
            await _access.CreateStaffAsync(NewStaffName, password, NewStaffRole);
            SetStatus("Staff account created.", false);
        }, "Could not create the account. Check the username is unique and the password has 8 to 128 characters.");

        public Task ChangePasswordAsync(string currentPassword, string password, string confirmation)
        {
            if (IsBusy) return Task.CompletedTask;
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 128)
            {
                SetStatus("Use a password between 8 and 128 characters.", true);
                return Task.CompletedTask;
            }

            if (password != confirmation)
            {
                SetStatus("The two new passwords must match.", true);
                return Task.CompletedTask;
            }

            return ExecuteBusyActionAsync(async () =>
            {
                var changed = await _access.ChangePasswordAsync(CurrentUserId, currentPassword, password);
                SetStatus(changed ? "Password updated." : "The current password is incorrect.", !changed);
            }, "The password could not be updated.");
        }

        public string[] CurrencyLocales => ReceiptProfile.Locales;

        public int[] PaperWidths { get; } = new[] { 58, 80 };

        public string Locale { get; set; } = "en-IN";

        public string TaxName { get; set; } = "Tax";

        public string TaxNumber { get; set; }

        public int ReceiptWidth { get; set; } = 80;

        public string AppVersion => typeof(SettingsViewModel).Assembly.GetName().Version.ToString(3);

        public string DatabasePath => _location.FilePath;

        public string DatabaseFileName => Path.GetFileName(DatabasePath);

        public AsyncRelayCommand SaveSettingsCommand => _saveSettingsCommand;

        public AsyncRelayCommand BackupDatabaseCommand => _backupDatabaseCommand;

        public AsyncRelayCommand ExportDataCommand { get; }

        public AsyncRelayCommand SupportBundleCommand { get; }

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

        public string Address
        {
            get => _address;
            set
            {
                if (SetProperty(ref _address, value)) NotifyValidationState();
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
                    ExportDataCommand.RaiseCanExecuteChanged();
                    SupportBundleCommand.RaiseCanExecuteChanged();
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

                    case nameof(Address):
                        if (string.IsNullOrWhiteSpace(Address)) return "Restaurant address is required.";
                        if (Address.Trim().Length > 250) return "Restaurant address must be 250 characters or fewer.";
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
                    taxPercentage, Address, new ReceiptProfile(Locale, TaxName, TaxNumber, ReceiptWidth));

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
                var size = new FileInfo(backupPath).Length;
                try
                {
                    _saveFileDialogService.RememberBackupFolder(backupPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logService.LogError("Could not remember the backup folder", exception);
                }
                SetStatus($"Backup saved to {backupPath} ({size:N0} bytes).", false);
            }, "The database backup could not be created.");
        }

        private async Task SaveArchiveAsync(bool support)
        {
            if (support && !_dialogs.Confirm("Create support bundle?",
                "This ZIP contains a database copy and logs, including business and account data. Share it only with someone you trust."))
                return;

            var title = support ? "Create support bundle" : "Export orders, bills, and menu";
            var prefix = support ? "servex-support" : "servex-export";
            var path = _saveFileDialogService.PickZipPath(title, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            if (path == null) return;

            await ExecuteBusyActionAsync(async () =>
            {
                if (support) await _archives.CreateSupportBundleAsync(path);
                else await _archives.ExportCsvAsync(path);
                SetStatus($"Archive saved to {path}.", false);
            }, "Could not save the archive. Choose a new file and try again.");
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
            Locale = safeSettings.Locale;
            TaxName = safeSettings.TaxName;
            TaxNumber = safeSettings.TaxNumber;
            ReceiptWidth = safeSettings.ReceiptWidth;
            OnPropertyChanged(nameof(Locale));
            OnPropertyChanged(nameof(TaxName));
            OnPropertyChanged(nameof(TaxNumber));
            OnPropertyChanged(nameof(ReceiptWidth));
            Address = safeSettings.Address ?? string.Empty;
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
                   string.IsNullOrEmpty(this[nameof(Address)]) &&
                   string.IsNullOrEmpty(this[nameof(Phone)]) &&
                   string.IsNullOrEmpty(this[nameof(TaxPercentageInput)]);
        }

        private string GetValidationSummary()
        {
            var errors = new[]
            {
                this[nameof(RestaurantName)],
                this[nameof(Address)],
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
            OnPropertyChanged(nameof(Address));
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
