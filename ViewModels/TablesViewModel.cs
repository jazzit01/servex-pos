using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class TablesViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly DialogService _dialogs;
        private readonly LogService _logService;
        private readonly TableService _tableService;
        private readonly OrderWorkspaceService _orderWorkspaceService;
        private readonly RelayCommand _beginAddCommand;
        private readonly RelayCommand _editCommand;
        private readonly AsyncRelayCommand _deleteCommand;
        private readonly AsyncRelayCommand _saveCommand;
        private readonly RelayCommand _cancelCommand;
        private readonly RelayCommand _selectTableCommand;
        private readonly AsyncRelayCommand _openSelectedOrderCommand;
        private readonly AsyncRelayCommand _openFloorOrderCommand;
        private readonly RelayCommand _editSelectedCommand;
        private readonly AsyncRelayCommand _deleteSelectedCommand;
        private readonly RelayCommand _showIndoorTablesCommand;
        private readonly RelayCommand _showOutdoorTablesCommand;
        private readonly ObservableCollection<Table> _tables = new ObservableCollection<Table>();
        private bool _isBusy;
        private bool _isEditorOpen;
        private int? _editingTableId;
        private TableFloorItemViewModel _selectedTable;
        private string _editorTableNumber = string.Empty;
        private TableArea _editorArea = TableArea.Indoor;
        private TableStatus _editorStatus = TableStatus.Available;
        private TableArea _selectedAreaFilter = TableArea.Indoor;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public TablesViewModel(TableService tableService, OrderWorkspaceService orderWorkspaceService, LogService logService, DialogService dialogs)
        {
            _dialogs = dialogs;
            _logService = logService;
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _orderWorkspaceService = orderWorkspaceService ?? throw new ArgumentNullException(nameof(orderWorkspaceService));

            VisibleTables = new ObservableCollection<TableFloorItemViewModel>();

            AvailableAreas = new ObservableCollection<TableArea>
            {
                TableArea.Indoor,
                TableArea.Outdoor
            };

            AvailableStatuses = new ObservableCollection<TableStatus>
            {
                TableStatus.Available,
                TableStatus.Occupied,
                TableStatus.Reserved
            };

            _beginAddCommand = new RelayCommand(_ => OpenAddPanel(), _ => !IsBusy);
            _editCommand = new RelayCommand(table => OpenEditPanel(table as TableFloorItemViewModel), table => !IsBusy && table is TableFloorItemViewModel);
            _deleteCommand = new AsyncRelayCommand(async table => await DeleteAsync(table as TableFloorItemViewModel), table => CanDeleteTables && !IsBusy && table is TableFloorItemViewModel);
            _saveCommand = new AsyncRelayCommand(async _ => await SaveAsync(), _ => CanSave);
            _cancelCommand = new RelayCommand(_ => CloseEditor(), _ => !IsBusy);
            _selectTableCommand = new RelayCommand(table => SelectTable(table as TableFloorItemViewModel), table => !IsBusy && table is TableFloorItemViewModel);
            ChangeFloorStatusCommand = new AsyncRelayCommand(_ => ChangeFloorStatusAsync(),
                _ => !IsBusy && SelectedTable?.Status is TableStatus.Available or TableStatus.Reserved or TableStatus.Cleaning);
            _openSelectedOrderCommand = new AsyncRelayCommand(async _ => await OpenOrderScreenAsync(SelectedTable), _ => !IsBusy && SelectedTable != null);
            _openFloorOrderCommand = new AsyncRelayCommand(async table => await OpenOrderScreenAsync(table as TableFloorItemViewModel), table => !IsBusy && table is TableFloorItemViewModel);
            _editSelectedCommand = new RelayCommand(_ => OpenEditPanel(SelectedTable), _ => !IsBusy && SelectedTable != null);
            _deleteSelectedCommand = new AsyncRelayCommand(async _ => await DeleteAsync(SelectedTable), _ => CanDeleteTables && !IsBusy && SelectedTable != null);
            _showIndoorTablesCommand = new RelayCommand(_ => SelectedAreaFilter = TableArea.Indoor, _ => !IsBusy);
            _showOutdoorTablesCommand = new RelayCommand(_ => SelectedAreaFilter = TableArea.Outdoor, _ => !IsBusy);
        }

        public bool CanDeleteTables { get; set; } = true;

        public event EventHandler TablesChanged;

        public ObservableCollection<TableFloorItemViewModel> VisibleTables { get; }

        public ObservableCollection<TableArea> AvailableAreas { get; }

        public ObservableCollection<TableStatus> AvailableStatuses { get; }

        public RelayCommand BeginAddCommand => _beginAddCommand;

        public RelayCommand EditCommand => _editCommand;

        public AsyncRelayCommand DeleteCommand => _deleteCommand;

        public AsyncRelayCommand SaveCommand => _saveCommand;

        public RelayCommand CancelCommand => _cancelCommand;

        public RelayCommand SelectTableCommand => _selectTableCommand;


        public AsyncRelayCommand ChangeFloorStatusCommand { get; }

        public string FloorStatusAction => SelectedTable?.Status switch
        {
            TableStatus.Reserved => "Release reservation",
            TableStatus.Cleaning => "Mark clean",
            _ => "Reserve table"
        };

        public AsyncRelayCommand OpenSelectedOrderCommand => _openSelectedOrderCommand;

        public AsyncRelayCommand OpenFloorOrderCommand => _openFloorOrderCommand;

        public RelayCommand EditSelectedCommand => _editSelectedCommand;

        public AsyncRelayCommand DeleteSelectedCommand => _deleteSelectedCommand;

        public RelayCommand ShowIndoorTablesCommand => _showIndoorTablesCommand;

        public RelayCommand ShowOutdoorTablesCommand => _showOutdoorTablesCommand;

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

        public TableArea SelectedAreaFilter
        {
            get => _selectedAreaFilter;
            set
            {
                if (SetProperty(ref _selectedAreaFilter, value))
                {
                    ApplyAreaFilter();
                    OnPropertyChanged(nameof(IsIndoorFilterSelected));
                    OnPropertyChanged(nameof(IsOutdoorFilterSelected));
                    OnPropertyChanged(nameof(FloorLayoutTitle));
                    OnPropertyChanged(nameof(FloorLayoutSubtitle));
                    OnPropertyChanged(nameof(SelectedAreaLabel));
                }
            }
        }

        public bool IsIndoorFilterSelected => SelectedAreaFilter == TableArea.Indoor;

        public bool IsOutdoorFilterSelected => SelectedAreaFilter == TableArea.Outdoor;

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

        public string FloorLayoutTitle => SelectedAreaFilter == TableArea.Indoor
            ? "Indoor Floor"
            : "Outdoor Floor";

        public string FloorLayoutSubtitle => SelectedAreaFilter == TableArea.Indoor
            ? "Select a dining table to continue service, update details, or reopen the order."
            : "Track patio tables clearly so the outdoor floor stays as readable as the main room.";

        public string SelectedAreaLabel => SelectedAreaFilter.ToString();

        public string EditorTableNumber
        {
            get => _editorTableNumber;
            set
            {
                if (SetProperty(ref _editorTableNumber, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyValidationStates();
                    OnPropertyChanged(nameof(NewTableNameHint));
                }
            }
        }

        private string FinalEditorName => IsEditMode
            ? EditorTableNumber?.Trim() ?? string.Empty
            : TableService.FormatNewTableNumber(EditorTableNumber, EditorArea);

        public string NewTableNameHint => IsEditMode ? string.Empty : string.IsNullOrWhiteSpace(EditorTableNumber)
            ? $"Enter a number for {TableService.FormatNewTableNumber("6", EditorArea)}, or type a custom name."
            : $"Table name: {FinalEditorName}";

        public TableArea EditorArea
        {
            get => _editorArea;
            set
            {
                if (SetProperty(ref _editorArea, value))
                {
                    NotifyValidationStates();
                    OnPropertyChanged(nameof(NewTableNameHint));
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

        public TableFloorItemViewModel SelectedTable
        {
            get => _selectedTable;
            private set
            {
                if (SetProperty(ref _selectedTable, value))
                {
                    UpdateSelectionStates();
                    OnPropertyChanged(nameof(HasSelectedTable));
                    OnPropertyChanged(nameof(SelectedTableHeading));
                    OnPropertyChanged(nameof(FloorStatusAction));
                    ChangeFloorStatusCommand.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(SelectedTableAreaLabel));
                    OnPropertyChanged(nameof(SelectedTableStatusLabel));
                    OnPropertyChanged(nameof(SelectedTableStatusDescription));
                    OnPropertyChanged(nameof(SelectedGuestLabel));
                    OnPropertyChanged(nameof(SelectedServiceCaption));
                    OnPropertyChanged(nameof(SelectionHint));
                    NotifyCommandStates();
                }
            }
        }

        public bool HasSelectedTable => SelectedTable != null;

        public string SelectedTableHeading => SelectedTable == null
            ? "No table selected"
            : SelectedTable.Heading;

        public string SelectedTableAreaLabel => SelectedTable?.Area.ToString() ?? SelectedAreaFilter.ToString();

        public string SelectedTableStatusLabel => SelectedTable?.StatusLabel ?? "Waiting";

        public string SelectedGuestLabel => SelectedTable?.CustomerDisplayName ?? "Atithi";

        public string SelectedServiceCaption => SelectedTable?.ServiceCaption ?? "Choose a table from the floor to continue.";

        public string SelectedTableStatusDescription
        {
            get
            {
                if (SelectedTable == null)
                {
                    return "Choose a table on the floor to review its state and the next service action.";
                }

                switch (SelectedTable.Status)
                {
                    case TableStatus.Available:
                        return "Available and ready for the next guest.";
                    case TableStatus.Cleaning:
                        return "Payment is complete. Mark this table clean when it is ready.";
                    case TableStatus.Occupied:
                        return string.Format("Occupied and linked to {0}.", SelectedGuestLabel);
                    case TableStatus.Reserved:
                        return string.Format("Reserved and being held for {0}.", SelectedGuestLabel);
                    default:
                        return "Status is being tracked for this table.";
                }
            }
        }

        public string SelectionHint => SelectedTable == null
            ? "Click a table block in the floor layout to inspect it."
            : "Open the order, edit the table, or remove it using the actions below.";

        public int VisibleTablesCount => VisibleTables.Count;

        public int AvailableCount => VisibleTables.Count(table => table.Status == TableStatus.Available);

        public int OccupiedCount => VisibleTables.Count(table => table.Status == TableStatus.Occupied);

        public int ReservedCount => VisibleTables.Count(table => table.Status == TableStatus.Reserved);

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

        public bool IsEmpty => VisibleTables.Count == 0;

        public bool CanSave => !IsBusy && IsFormValid();

        public string Error => string.Empty;

        public async Task LoadAsync()
        {
            try
            {
                await ExecuteBusyActionAsync(async () =>
                {
                    await RefreshAsync();
                    if (IsEmpty)
                    {
                        SetStatus("No tables exist yet. Add the first table to begin mapping the floor.", false);
                    }
                    else
                    {
                        ClearStatus();
                    }
                }, "Tables could not be loaded.");
            }
            catch (Exception exception)
            {
                _logService.LogError("Tables view model load failed", exception);
                ResetTableCollections();
                SetStatus(GetDevelopmentSafeMessage("Tables could not be loaded.", exception), true);
            }
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

                        if (FinalEditorName.Length > 20)
                        {
                            return "Table number must be 20 characters or fewer.";
                        }

                        if (_tables.Any(table =>
                                table.Id != _editingTableId.GetValueOrDefault() &&
                                string.Equals(table.TableNumber, FinalEditorName, StringComparison.OrdinalIgnoreCase)))
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
                TableNumber = FinalEditorName,
                Area = EditorArea,
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
                    table = await _tableService.AddTableAsync(table);
                }

                await RefreshAsync(table.TableNumber);
                CloseEditor(false);
                SetStatus(successMessage, false);
                RaiseTablesChanged();
            }, "The table could not be saved. Make sure the table number is unique.");
        }

        private async Task DeleteAsync(TableFloorItemViewModel table)
        {
            if (table == null)
            {
                return;
            }

            if (!_dialogs.Confirm("Delete table", "Delete this table? This cannot be undone. Records linked to order history will be protected.")) return;

            await ExecuteBusyActionAsync(async () =>
            {
                await _tableService.DeleteTableAsync(table.Id);

                if (_editingTableId == table.Id)
                {
                    CloseEditor(false);
                }

                await RefreshAsync();
                SetStatus("Table deleted successfully.", false);
                RaiseTablesChanged();
            }, "The table could not be deleted. Tables linked to order history are protected.");
        }

        private async Task ChangeFloorStatusAsync()
        {
            var table = SelectedTable;
            if (table == null) return;
            var next = table.Status == TableStatus.Available ? TableStatus.Reserved : TableStatus.Available;
            await ExecuteBusyActionAsync(async () =>
            {
                await _tableService.ChangeFloorStatusAsync(table.Id, table.Status, next);
                await RefreshAsync(table.TableNumber);
                RaiseTablesChanged();
            }, "Could not update the table status. Refresh the floor and try again.");
        }

        private async Task OpenOrderScreenAsync(TableFloorItemViewModel table)
        {
            if (table == null)
            {
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                var rawTable = GetTableById(table.Id);
                if (rawTable == null)
                {
                    throw new InvalidOperationException("The selected table could not be found.");
                }

                await _orderWorkspaceService.ShowForTableAsync(rawTable);
                await RefreshAsync(rawTable.TableNumber);
                RaiseTablesChanged();
            }, "The order screen could not be opened.");
        }

        private void OpenAddPanel()
        {
            try
            {
                ResetEditor();
                _editingTableId = null;
                EditorArea = SelectedAreaFilter;
                EditorStatus = TableStatus.Available;
                IsEditorOpen = true;
                OnPropertyChanged(nameof(IsEditMode));
                OnPropertyChanged(nameof(NewTableNameHint));
                OnPropertyChanged(nameof(EditorTitle));
                ClearStatus();
                NotifyCommandStates();
            }
            catch (Exception exception)
            {
                _logService.LogError("Opening add table editor failed", exception);
                SetStatus(GetDevelopmentSafeMessage("The add table form could not be opened.", exception), true);
            }
        }

        private void OpenEditPanel(TableFloorItemViewModel table)
        {
            try
            {
                if (table == null)
                {
                    return;
                }

                _editingTableId = table.Id;
                EditorTableNumber = table.TableNumber;
                EditorArea = table.Area;
                EditorStatus = NormalizeEditableStatus(table.Status);
                IsEditorOpen = true;
                OnPropertyChanged(nameof(IsEditMode));
                OnPropertyChanged(nameof(NewTableNameHint));
                OnPropertyChanged(nameof(EditorTitle));
                ClearStatus();
                NotifyCommandStates();
            }
            catch (Exception exception)
            {
                _logService.LogError("Opening edit table editor failed", exception);
                SetStatus(GetDevelopmentSafeMessage("The selected table could not be opened for editing.", exception), true);
            }
        }

        private void SelectTable(TableFloorItemViewModel table)
        {
            try
            {
                if (table == null)
                {
                    return;
                }

                SelectedTable = table;
            }
            catch (Exception exception)
            {
                _logService.LogError("Selecting table from floor failed", exception);
                SetStatus(GetDevelopmentSafeMessage("The selected table could not be loaded.", exception), true);
            }
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

        private async Task RefreshAsync(string selectedTableNumber = null)
        {
            try
            {
                var items = await _tableService.GetTablesAsync() ?? new List<Table>();
                var activeGuestNames = await _tableService.GetActiveGuestNamesByTableAsync() ?? new Dictionary<int, string>();
                var previousSelectionId = SelectedTable?.Id;
                var preferredTableNumber = selectedTableNumber;

                _tables.Clear();
                foreach (var item in items.Where(item => item != null))
                {
                    _tables.Add(item);
                }

                ApplyAreaFilter(previousSelectionId, preferredTableNumber, activeGuestNames);
            }
            catch (Exception exception)
            {
                _logService.LogError("Refreshing tables failed", exception);
                ResetTableCollections();
                throw;
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

        private void ResetEditor()
        {
            _editingTableId = null;
            EditorTableNumber = string.Empty;
            EditorArea = TableArea.Indoor;
            EditorStatus = TableStatus.Available;
            TouchValidation();
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
        }

        private void ApplyAreaFilter(int? preferredSelectionId = null, string preferredTableNumber = null, IReadOnlyDictionary<int, string> activeGuestNames = null)
        {
            var selectionId = preferredSelectionId ?? SelectedTable?.Id;
            VisibleTables.Clear();

            foreach (var table in _tables
                         .Where(item => item.Area == SelectedAreaFilter)
                         .OrderBy(item => item.TableNumber))
            {
                var customerName = activeGuestNames != null && activeGuestNames.TryGetValue(table.Id, out var activeGuestName)
                    ? activeGuestName
                    : "Atithi";

                VisibleTables.Add(new TableFloorItemViewModel
                {
                    Id = table.Id,
                    TableNumber = table.TableNumber,
                    Area = table.Area,
                    Status = table.Status,
                    CustomerName = customerName,
                    HasActiveOrder = activeGuestNames != null && activeGuestNames.ContainsKey(table.Id)
                });
            }

            SynchronizeSelection(selectionId, preferredTableNumber);
            NotifySummaryProperties();
        }

        private void SynchronizeSelection(int? preferredSelectionId, string preferredTableNumber)
        {
            if (VisibleTables.Count == 0)
            {
                SelectedTable = null;
                return;
            }

            TableFloorItemViewModel nextSelection = null;

            if (!string.IsNullOrWhiteSpace(preferredTableNumber))
            {
                nextSelection = VisibleTables.FirstOrDefault(table =>
                    string.Equals(table.TableNumber, preferredTableNumber, StringComparison.OrdinalIgnoreCase));
            }

            if (nextSelection == null && preferredSelectionId.HasValue)
            {
                nextSelection = VisibleTables.FirstOrDefault(table => table.Id == preferredSelectionId.Value);
            }

            SelectedTable = nextSelection ?? VisibleTables.FirstOrDefault();
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
            _openSelectedOrderCommand.RaiseCanExecuteChanged();
            _openFloorOrderCommand.RaiseCanExecuteChanged();
            _editSelectedCommand.RaiseCanExecuteChanged();
            _deleteSelectedCommand.RaiseCanExecuteChanged();
            _showIndoorTablesCommand.RaiseCanExecuteChanged();
            _showOutdoorTablesCommand.RaiseCanExecuteChanged();
            ChangeFloorStatusCommand.RaiseCanExecuteChanged();
        }

        private void NotifySummaryProperties()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(VisibleTablesCount));
            OnPropertyChanged(nameof(AvailableCount));
            OnPropertyChanged(nameof(OccupiedCount));
            OnPropertyChanged(nameof(ReservedCount));
        }

        private void UpdateSelectionStates()
        {
            foreach (var table in VisibleTables)
            {
                table.IsSelected = SelectedTable != null && table.Id == SelectedTable.Id;
            }
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsStatusError = isError;
        }

        private void ResetTableCollections()
        {
            _tables.Clear();
            VisibleTables.Clear();
            SelectedTable = null;
            NotifySummaryProperties();
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

        private Table GetTableById(int tableId)
        {
            return _tables.FirstOrDefault(table => table.Id == tableId);
        }

        private static TableStatus NormalizeEditableStatus(TableStatus status)
        {
            return status == TableStatus.Occupied || status == TableStatus.Reserved
                ? status
                : TableStatus.Available;
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
