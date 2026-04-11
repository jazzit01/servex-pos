using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ServeX.Data;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class SettingsViewModel : ViewModelBase
    {
        private readonly DatabaseBackupService _databaseBackupService;
        private readonly SaveFileDialogService _saveFileDialogService;
        private readonly RelayCommand _backupDatabaseCommand;
        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public SettingsViewModel(DatabaseBackupService databaseBackupService, SaveFileDialogService saveFileDialogService)
        {
            _databaseBackupService = databaseBackupService ?? throw new ArgumentNullException(nameof(databaseBackupService));
            _saveFileDialogService = saveFileDialogService ?? throw new ArgumentNullException(nameof(saveFileDialogService));
            _backupDatabaseCommand = new RelayCommand(async _ => await BackupDatabaseAsync(), _ => !IsBusy);
        }

        public string DatabasePath => ServeXDbContext.GetDatabaseFilePath();

        public string DatabaseFileName => Path.GetFileName(DatabasePath);

        public RelayCommand BackupDatabaseCommand => _backupDatabaseCommand;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    _backupDatabaseCommand.RaiseCanExecuteChanged();
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

        private async Task ExecuteBusyActionAsync(Func<Task> action, string fallbackMessage)
        {
            IsBusy = true;

            try
            {
                await action();
            }
            catch (Exception exception)
            {
                SetStatus(fallbackMessage + " " + exception.Message, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsStatusError = isError;
        }
    }
}
