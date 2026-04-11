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
        private readonly LogService _logService = new LogService();
        private readonly TableService _tableService;
        private readonly OrderWindowService _orderWindowService;
        private readonly RelayCommand _beginAddCommand;
        private readonly RelayCommand _editCommand;
        private readonly RelayCommand _deleteCommand;
        private readonly RelayCommand _saveCommand;
        private readonly RelayCommand _cancelCommand;
        private readonly RelayCommand _selectTableCommand;
        private readonly RelayCommand _openSelectedOrderCommand;
        private readonly RelayCommand _editSelectedCommand;
        private readonly RelayCommand _deleteSelectedCommand;
        private readonly RelayCommand _showIndoorTablesCommand;
        private readonly RelayCommand _showOutdoorTablesCommand;
        private bool _isBusy;
        private bool _isEditorOpen;
        private int? _editingTableId;
        private Table _selectedTable;
        private string _editorTableNumber = string.Empty;
        private TableArea _editorArea = TableArea.Indoor;
        private TableStatus _editorStatus = TableStatus.Available;
        private TableArea _selectedAreaFilter = TableArea.Indoor;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public TablesViewModel(TableService tableService, OrderWindowService orderWindowService)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _orderWindowService = orderWindowService ?? throw new ArgumentNullException(nameof(orderWindowService));

            Tables = new ObservableCollection<Table>();
            Tables.CollectionChanged += OnTablesCollectionChanged;
            FilteredTables = new ObservableCollection<Table>();

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
            _editCommand = new RelayCommand(table => OpenEditPanel(table as Table), table => !IsBusy && table is Table);
            _deleteCommand = new RelayCommand(async table => await DeleteAsync(table as Table), table => !IsBusy && table is Table);
            _saveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanSave);
            _cancelCommand = new RelayCommand(_ => CloseEditor(), _ => !IsBusy);
            _selectTableCommand = new RelayCommand(table => SelectedTable = table as Table, table => table is Table);
            _openSelectedOrderCommand = new RelayCommand(async _ => await OpenOrderScreenAsync(SelectedTable), _ => !IsBusy && SelectedTable != null);
            _editSelectedCommand = new RelayCommand(_ => OpenEditPanel(SelectedTable), _ => !IsBusy && SelectedTable != null);
            _deleteSelectedCommand = new RelayCommand(async _ => await DeleteAsync(SelectedTable), _ => !IsBusy && SelectedTable != null);
            _showIndoorTablesCommand = new RelayCommand(_ => SelectedAreaFilter = TableArea.Indoor, _ => !IsBusy);
            _showOutdoorTablesCommand = new RelayCommand(_ => SelectedAreaFilter = TableArea.Outdoor, _ => !IsBusy);
        }

        public event EventHandler TablesChanged;

        public ObservableCollection<Table> Tables { get; }

        public ObservableCollection<Table> FilteredTables { get; }

        public ObservableCollection<TableArea> AvailableAreas { get; }

        public ObservableCollection<TableStatus> AvailableStatuses { get; }

        public RelayCommand BeginAddCommand => _beginAddCommand;

        public RelayCommand EditCommand => _editCommand;

        public RelayCommand DeleteCommand => _deleteCommand;

        public RelayCommand SaveCommand => _saveCommand;

        public RelayCommand CancelCommand => _cancelCommand;

        public RelayCommand SelectTableCommand => _selectTableCommand;

        public RelayCommand OpenSelectedOrderCommand => _openSelectedOrderCommand;

        public RelayCommand EditSelectedCommand => _editSelectedCommand;

        public RelayCommand DeleteSelectedCommand => _deleteSelectedCommand;

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

        public string FloorLayoutTitle => string.Format("{0} Floor", SelectedAreaFilter);

        public string FloorLayoutSubtitle => string.Format(
            "Choose a {0} table to inspect it, edit placement, or continue into service.",
            SelectedAreaFilter == TableArea.Indoor ? "dining room" : "patio");

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

        public TableArea EditorArea
        {
            get => _editorArea;
            set
            {
                if (SetProperty(ref _editorArea, value))
                {
                    OnPropertyChanged(nameof(CanSave));
                    NotifyCommandStates();
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

        public Table SelectedTable
        {
            get => _selectedTable;
            set
            {
                if (SetProperty(ref _selectedTable, value))
                {
                    OnPropertyChanged(nameof(HasSelectedTable));
                    OnPropertyChanged(nameof(SelectedTableHeading));
                    OnPropertyChanged(nameof(SelectedTableAreaLabel));
                    OnPropertyChanged(nameof(SelectedTableStatusLabel));
                    OnPropertyChanged(nameof(SelectedTableStatusDescription));
                    OnPropertyChanged(nameof(SelectionHint));
                    NotifyCommandStates();
                }
            }
        }

        public bool HasSelectedTable => SelectedTable != null;

        public string SelectedTableHeading => SelectedTable == null
            ? "No table selected"
            : string.Format("Table {0}", SelectedTable.TableNumber);

        public string SelectedTableAreaLabel => SelectedTable?.Area.ToString() ?? SelectedAreaFilter.ToString();

        public string SelectedTableStatusLabel => SelectedTable?.Status.ToString() ?? "Waiting";

        public string SelectedTableStatusDescription
        {
            get
            {
                if (SelectedTable == null)
                {
                    return "Choose a table on the floor to review its current state and next action.";
                }

                switch (SelectedTable.Status)
                {
                    case TableStatus.Available:
                        return "Ready for the next guest or a fresh service session.";
                    case TableStatus.Occupied:
                        return "An active service session is already in motion for this table.";
                    case TableStatus.Reserved:
                        return "Held aside for an upcoming party while staying visible to the team.";
                    default:
                        return "Status is being tracked for this table.";
                }
            }
        }

        public string SelectionHint => SelectedTable == null
            ? "Tap a table shape from the floor to begin."
            : "Use the actions below to open service, edit details, or remove the table.";

        public int AreaTableCount => FilteredTables.Count;

        public int AvailableCount => FilteredTables.Count(table => table.Status == TableStatus.Available);

        public int OccupiedCount => FilteredTables.Count(table => table.Status == TableStatus.Occupied);

        public int ReservedCount => FilteredTables.Count(table => table.Status == TableStatus.Reserved);

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

        public bool IsEmpty => FilteredTables.Count == 0;

        public bool CanSave => !IsBusy && IsFormValid();

        public string Error => string.Empty;

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                await RefreshAsync();
                if (IsEmpty)
                {
                    SetStatus("No tables exist yet. Add the first table to begin mapping the floor.", false);
                }
                else if (!IsStatusError)
                {
                    ClearStatus();
                }
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

                if (SelectedTable?.Id == table.Id)
                {
                    SelectedTable = null;
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
            EditorArea = SelectedAreaFilter;
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
            EditorArea = table.Area;
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
            var items = await _tableService.GetTablesAsync() ?? new System.Collections.Generic.List<Table>();
            Tables.Clear();
            foreach (var item in items.Where(item => item != null))
            {
                if (!System.Enum.IsDefined(typeof(TableArea), item.Area))
                {
                    item.Area = TableArea.Indoor;
                }

                if (!System.Enum.IsDefined(typeof(TableStatus), item.Status))
                {
                    item.Status = TableStatus.Available;
                }

                Tables.Add(item);
            }

            ApplyAreaFilter();
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
                _logService.LogError(fallbackMessage, null);
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

        private void ResetEditor()
        {
            _editingTableId = null;
            EditorTableNumber = string.Empty;
            EditorArea = SelectedAreaFilter;
            EditorStatus = TableStatus.Available;
            TouchValidation();
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(EditorTitle));
        }

        private void ApplyAreaFilter()
        {
            FilteredTables.Clear();

            foreach (var table in Tables.Where(item => item.Area == SelectedAreaFilter).OrderBy(item => item.TableNumber))
            {
                FilteredTables.Add(table);
            }

            SynchronizeSelection();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(AreaTableCount));
            OnPropertyChanged(nameof(AvailableCount));
            OnPropertyChanged(nameof(OccupiedCount));
            OnPropertyChanged(nameof(ReservedCount));
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
            _editSelectedCommand.RaiseCanExecuteChanged();
            _deleteSelectedCommand.RaiseCanExecuteChanged();
            _showIndoorTablesCommand.RaiseCanExecuteChanged();
            _showOutdoorTablesCommand.RaiseCanExecuteChanged();
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
            ApplyAreaFilter();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EditorTableNumber));
        }

        private void SynchronizeSelection()
        {
            if (FilteredTables.Count == 0)
            {
                SelectedTable = null;
                return;
            }

            if (SelectedTable != null)
            {
                var matchingTable = FilteredTables.FirstOrDefault(table => table.Id == SelectedTable.Id);
                if (matchingTable != null)
                {
                    SelectedTable = matchingTable;
                    return;
                }
            }

            SelectedTable = FilteredTables.FirstOrDefault();
        }
    }
}
