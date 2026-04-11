using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class TablesViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly TableService _tableService;
        private readonly OrderWindowService _orderWindowService;
        private readonly RelayCommand _beginAddCommand;
        private readonly RelayCommand _editCommand;
        private readonly RelayCommand _deleteCommand;
        private readonly RelayCommand _saveCommand;
        private readonly RelayCommand _cancelCommand;
        private readonly RelayCommand _selectTableCommand;
        private bool _isBusy;
        private bool _isEditorOpen;
        private int? _editingTableId;
        private string _editorTableNumber = string.Empty;
        private TableStatus _editorStatus = TableStatus.Available;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public TablesViewModel(TableService tableService, OrderWindowService orderWindowService)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _orderWindowService = orderWindowService ?? throw new ArgumentNullException(nameof(orderWindowService));

            Tables = new ObservableCollection<Table>();
            Tables.CollectionChanged += OnTablesCollectionChanged;

            AvailableStatuses = new ObservableCollection<TableStatus>
            {
                TableStatus.Available,
                TableStatus.Occupied,
                TableStatus.Reserved
            };

            _beginAddCommand = new RelayCommand(_ => OpenAddPanel(), _ => !IsBusy);
            _editCommand = new RelayCommand(table => OpenEditPanel(table as Table), table => !IsBusy && table is Table);
            _deleteCommand = new RelayCommand(async table => await DeleteAsync(table as Table), table => !IsBusy && table is Table);
            _saveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanSave);
            _cancelCommand = new RelayCommand(_ => CloseEditor(), _ => !IsBusy);
            _selectTableCommand = new RelayCommand(async table => await OpenOrderScreenAsync(table as Table), table => !IsBusy && table is Table);
        }

        public event EventHandler TablesChanged;

        public ObservableCollection<Table> Tables { get; }

        public ObservableCollection<TableStatus> AvailableStatuses { get; }

        public RelayCommand BeginAddCommand => _beginAddCommand;

        public RelayCommand EditCommand => _editCommand;

        public RelayCommand DeleteCommand => _deleteCommand;

        public RelayCommand SaveCommand => _saveCommand;

        public RelayCommand CancelCommand => _cancelCommand;

        public RelayCommand SelectTableCommand => _selectTableCommand;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyCommandStates();
                }
            }
        }

        public bool IsEditorOpen
        {
            get => _isEditorOpen;
            private set
            {
                if (SetProperty(ref _isEditorOpen, value))
                {
                    OnPropertyChanged(nameof(EditorTitle));
                }
            }
        }

        public bool IsEditMode => _editingTableId.HasValue;

        public string EditorTitle => IsEditMode ? "Edit table" : "Add a table";

        public string EditorTableNumber
        {
            get => _editorTableNumber;
            set
            {
                if (SetProperty(ref _editorTableNumber, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyValidationStates();
                }
            }
        }

        public TableStatus EditorStatus
        {
            get => _editorStatus;
            set
            {
                if (SetProperty(ref _editorStatus, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyCommandStates();
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

        public bool IsEmpty => Tables.Count == 0;

        public bool CanSave => !IsBusy && IsFormValid();

        public string Error => string.Empty;

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                await RefreshAsync();
            }, "Tables could not be loaded.");
        }

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(EditorTableNumber):
                        if (string.IsNullOrWhiteSpace(EditorTableNumber))
                        {
                            return "Table number is required.";
                        }

                        if (EditorTableNumber.Trim().Length > 20)
                        {
                            return "Table number must be 20 characters or fewer.";
                        }

                        if (Tables.Any(table =>
                                table.Id != _editingTableId.GetValueOrDefault() &&
                                string.Equals(table.TableNumber, EditorTableNumber.Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            return "Table number must be unique.";
                        }

                        return string.Empty;

                    default:
                        return string.Empty;
                }
            }
        }

        private async Task SaveAsync()
        {
            TouchValidation();
            if (!IsFormValid())
            {
                SetStatus("Please fix the highlighted table details before saving.", true);
                return;
            }

            var table = new Table
            {
                Id = _editingTableId.GetValueOrDefault(),
                TableNumber = EditorTableNumber.Trim(),
                Status = EditorStatus
            };

            await ExecuteBusyActionAsync(async () =>
            {
                var successMessage = IsEditMode
                    ? "Table updated successfully."
                    : "Table added successfully.";

                if (IsEditMode)
                {
                    await _tableService.UpdateTableAsync(table);
                }
                else
                {
                    await _tableService.AddTableAsync(table);
                }

                await RefreshAsync();
                CloseEditor(false);
                SetStatus(successMessage, false);
                RaiseTablesChanged();
            }, "The table could not be saved. Make sure the table number is unique.");
        }

        private async Task DeleteAsync(Table table)
        {
            if (table == null)
            {
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                await _tableService.DeleteTableAsync(table.Id);
                if (_editingTableId == table.Id)
                {
                    CloseEditor();
                }

                await RefreshAsync();
                SetStatus("Table deleted successfully.", false);
                RaiseTablesChanged();
            }, "The table could not be deleted.");
        }

        private async Task OpenOrderScreenAsync(Table table)
        {
            if (table == null)
            {
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                await _orderWindowService.ShowForTableAsync(table);
                await RefreshAsync();
                RaiseTablesChanged();
            }, "The order screen could not be opened.");
        }

        private void OpenAddPanel()
        {
            ResetEditor();
            _editingTableId = null;
            EditorStatus = TableStatus.Available;
            IsEditorOpen = true;
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
            ClearStatus();
            NotifyCommandStates();
        }

        private void OpenEditPanel(Table table)
        {
            if (table == null)
            {
                return;
            }

            _editingTableId = table.Id;
            EditorTableNumber = table.TableNumber;
            EditorStatus = table.Status;
            IsEditorOpen = true;
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
            ClearStatus();
            NotifyCommandStates();
        }

        private void CloseEditor(bool clearStatus = true)
        {
            ResetEditor();
            IsEditorOpen = false;
            if (clearStatus)
            {
                ClearStatus();
            }

            NotifyCommandStates();
        }

        private async Task RefreshAsync()
        {
            var items = await _tableService.GetTablesAsync();
            Tables.Clear();
            foreach (var item in items)
            {
                Tables.Add(item);
            }
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

        private void ResetEditor()
        {
            _editingTableId = null;
            EditorTableNumber = string.Empty;
            EditorStatus = TableStatus.Available;
            TouchValidation();
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
        }

        private bool IsFormValid()
        {
            return string.IsNullOrEmpty(this[nameof(EditorTableNumber)]);
        }

        private void TouchValidation()
        {
            OnPropertyChanged(nameof(EditorTableNumber));
            NotifyCommandStates();
        }

        private void NotifyValidationStates()
        {
            OnPropertyChanged(nameof(EditorTableNumber));
            NotifyCommandStates();
        }

        private void NotifyCommandStates()
        {
            _beginAddCommand.RaiseCanExecuteChanged();
            _editCommand.RaiseCanExecuteChanged();
            _deleteCommand.RaiseCanExecuteChanged();
            _saveCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
            _selectTableCommand.RaiseCanExecuteChanged();
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

        private void RaiseTablesChanged()
        {
            TablesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnTablesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EditorTableNumber));
        }
    }
}
