using System;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class DashboardViewModel : ViewModelBase
    {
        private readonly DashboardService _dashboardService;
        private readonly OrderWindowService _orderWindowService;
        private readonly Action _goToTables;
        private readonly Action _goToMenu;
        private readonly Func<Task> _refreshWorkspace;
        private bool _isBusy;
        private decimal _todayRevenue;
        private int _activeTables;
        private int _totalOrdersToday;
        private int _parcelOrdersCount;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public DashboardViewModel(
            DashboardService dashboardService,
            OrderWindowService orderWindowService,
            Action goToTables,
            Action goToMenu,
            Func<Task> refreshWorkspace)
        {
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
            _orderWindowService = orderWindowService ?? throw new ArgumentNullException(nameof(orderWindowService));
            _goToTables = goToTables ?? throw new ArgumentNullException(nameof(goToTables));
            _goToMenu = goToMenu ?? throw new ArgumentNullException(nameof(goToMenu));
            _refreshWorkspace = refreshWorkspace ?? throw new ArgumentNullException(nameof(refreshWorkspace));

            RecentOrders = new ObservableCollection<DashboardRecentOrder>();

            NewTableOrderCommand = new RelayCommand(async _ => await OpenNewTableOrderAsync(), _ => !IsBusy);
            GoToTablesCommand = new RelayCommand(_ => _goToTables(), _ => !IsBusy);
            GoToMenuCommand = new RelayCommand(_ => _goToMenu(), _ => !IsBusy);
        }

        public ObservableCollection<DashboardRecentOrder> RecentOrders { get; }

        public RelayCommand NewTableOrderCommand { get; }

        public RelayCommand GoToTablesCommand { get; }

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

        public int ActiveTables
        {
            get => _activeTables;
            private set => SetProperty(ref _activeTables, value);
        }

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

                await _orderWindowService.ShowForTableAsync(table);
                await _refreshWorkspace();
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
            catch (DbUpdateException)
            {
                SetStatus(fallbackMessage, true);
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
            ActiveTables = snapshot.ActiveTables;
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
