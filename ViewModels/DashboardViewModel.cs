using System;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Services;
using ServeX.Presentation.Navigation;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class DashboardViewModel : ViewModelBase
    {
        private readonly LogService _logService;
        private readonly DashboardService _dashboardService;
        private readonly OrderWorkspaceService _orderWorkspaceService;
        private readonly Action _goToTables;
        private readonly Action _goToMenu;
        public Func<Task> RefreshWorkspace { private get; set; } = () => Task.CompletedTask;
        private bool _isBusy;
        private decimal _todayRevenue;
        private int _reservedTables;
        private int _occupiedTables;
        private int _availableTables;
        private int _activeOrders;
        private int _totalOrdersToday;
        private int _parcelOrdersCount;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public DashboardViewModel(
            DashboardService dashboardService,
            OrderWorkspaceService orderWorkspaceService,
            WorkspaceNavigation navigation, LogService logService)
        {
            _logService = logService;
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
            _orderWorkspaceService = orderWorkspaceService ?? throw new ArgumentNullException(nameof(orderWorkspaceService));
            _goToTables = () => navigation.Navigate(WorkspaceDestination.Tables);
            _goToMenu = () => navigation.Navigate(WorkspaceDestination.Menu);

            RecentOrders = new ObservableCollection<DashboardRecentOrder>();

            NewTableOrderCommand = new AsyncRelayCommand(async _ => await OpenNewTableOrderAsync(), _ => !IsBusy);
            GoToTablesCommand = new RelayCommand(_ => _goToTables(), _ => !IsBusy);
            GoToMenuCommand = new RelayCommand(_ => _goToMenu(), _ => !IsBusy && CanManageMenu);
        }

        public System.Collections.Generic.IList<HistoryRow> OpenOrders { get; private set; }

        public ObservableCollection<DashboardRecentOrder> RecentOrders { get; }

        public AsyncRelayCommand NewTableOrderCommand { get; }

        public RelayCommand GoToTablesCommand { get; }

        public bool CanManageMenu { get; set; } = true;

        public RelayCommand GoToMenuCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    NotifyCommandStates();
                }
            }
        }

        public decimal TodayRevenue
        {
            get => _todayRevenue;
            private set => SetProperty(ref _todayRevenue, value);
        }

        public int ReservedTables
        {
            get => _reservedTables;
            private set => SetProperty(ref _reservedTables, value);
        }

        public int OccupiedTables { get => _occupiedTables; private set => SetProperty(ref _occupiedTables, value); }
        public int AvailableTables { get => _availableTables; private set => SetProperty(ref _availableTables, value); }
        public int ActiveOrders { get => _activeOrders; private set => SetProperty(ref _activeOrders, value); }

        public int TotalOrdersToday
        {
            get => _totalOrdersToday;
            private set => SetProperty(ref _totalOrdersToday, value);
        }

        public int ParcelOrdersCount
        {
            get => _parcelOrdersCount;
            private set => SetProperty(ref _parcelOrdersCount, value);
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

        public bool HasRecentOrders => RecentOrders.Count > 0;

        public bool IsRecentOrdersEmpty => RecentOrders.Count == 0;

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                await LoadSnapshotAsync();
                ClearStatus();
            }, "The dashboard could not be loaded.");
        }

        private async Task OpenNewTableOrderAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                var table = await _dashboardService.GetPreferredTableForNewOrderAsync();
                if (table == null)
                {
                    SetStatus("No available tables are ready for a new order right now.", true);
                    return;
                }

                await _orderWorkspaceService.ShowForTableAsync(table);
                await RefreshWorkspace();
                await LoadSnapshotAsync();
                ClearStatus();
            }, "A new table order could not be started.");
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

        private void NotifyCommandStates()
        {
            NewTableOrderCommand.RaiseCanExecuteChanged();
            GoToTablesCommand.RaiseCanExecuteChanged();
            GoToMenuCommand.RaiseCanExecuteChanged();
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

        private async Task LoadSnapshotAsync()
        {
            var snapshot = await _dashboardService.GetSnapshotAsync();
            TodayRevenue = snapshot.TodayRevenue;
            ReservedTables = snapshot.ReservedTables;
            OccupiedTables = snapshot.OccupiedTables;
            AvailableTables = snapshot.AvailableTables;
            ActiveOrders = snapshot.ActiveOrders;
            OpenOrders = snapshot.OpenOrders;
            OnPropertyChanged(nameof(OpenOrders));
            TotalOrdersToday = snapshot.TotalOrdersToday;
            ParcelOrdersCount = snapshot.ParcelOrdersCount;

            RecentOrders.Clear();
            foreach (var order in snapshot.RecentOrders)
            {
                RecentOrders.Add(order);
            }

            OnPropertyChanged(nameof(HasRecentOrders));
            OnPropertyChanged(nameof(IsRecentOrdersEmpty));
        }
    }
}
