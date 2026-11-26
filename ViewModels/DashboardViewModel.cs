using System.Linq;
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
        private readonly WorkspaceNavigation _navigation;
        public Func<Task> RefreshWorkspace { private get; set; } = () => Task.CompletedTask;

        /// <summary>
        /// Opens a workspace the same way the sidebar does (loading its data first). When the main shell has
        /// not supplied one, the dashboard falls back to plain navigation.
        /// </summary>
        public Func<WorkspaceDestination, Task> OpenDestination { private get; set; }

        /// <summary>Opens an unpaid order for editing or billing; supplied by the main shell.</summary>
        public Func<HistoryRow, Task> ResumeOrder { private get; set; } = _ => Task.CompletedTask;

        /// <summary>Starts a new takeaway order; supplied by the main shell.</summary>
        public Func<Task> OpenTakeaway { private get; set; } = () => Task.CompletedTask;

        private bool _isBusy;
        private string _todayDisplay = string.Empty;
        private string _lastUpdatedDisplay = string.Empty;
        private string _todaySalesDisplay = string.Empty;
        private string _averageOrderDisplay = "—";
        private int _paidBillsToday;
        private int _reservedTables;
        private int _occupiedTables;
        private int _availableTables;
        private int _totalTables;
        private int _activeOrders;
        private int _totalOrdersToday;
        private int _parcelOrdersCount;
        private int _staleOrderCount;
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
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

            RecentOrders = new ObservableCollection<DashboardRecentOrder>();
            OpenOrderRows = new ObservableCollection<DashboardOpenOrder>();
            TopItems = new ObservableCollection<DashboardTopItem>();
            PaymentRows = new ObservableCollection<DashboardPaymentRow>();

            NewTableOrderCommand = new AsyncRelayCommand(async _ => await OpenNewTableOrderAsync(), _ => !IsBusy);
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
            GoToTablesCommand = new AsyncRelayCommand(_ => OpenAsync(WorkspaceDestination.Tables), _ => !IsBusy);
            GoToMenuCommand = new AsyncRelayCommand(_ => OpenAsync(WorkspaceDestination.Menu), _ => !IsBusy && CanManageMenu);
            NewParcelCommand = new AsyncRelayCommand(async _ => await StartTakeawayAsync(), _ => !IsBusy);
            OpenOrdersCommand = new AsyncRelayCommand(_ => OpenAsync(WorkspaceDestination.Orders), _ => !IsBusy);
            SalesHistoryCommand = new AsyncRelayCommand(_ => OpenAsync(WorkspaceDestination.History), _ => !IsBusy);
            ResumeOrderCommand = new AsyncRelayCommand(async order => await ResumeOpenOrderAsync(order as DashboardOpenOrder),
                order => !IsBusy && order is DashboardOpenOrder);
        }

        public System.Collections.Generic.IList<HistoryRow> OpenOrders { get; private set; }

        public ObservableCollection<DashboardOpenOrder> OpenOrderRows { get; }

        public ObservableCollection<DashboardRecentOrder> RecentOrders { get; }

        public ObservableCollection<DashboardTopItem> TopItems { get; }

        public ObservableCollection<DashboardPaymentRow> PaymentRows { get; }

        public AsyncRelayCommand NewTableOrderCommand { get; }

        public AsyncRelayCommand RefreshCommand { get; }

        public AsyncRelayCommand GoToTablesCommand { get; }

        public AsyncRelayCommand NewParcelCommand { get; }

        public AsyncRelayCommand OpenOrdersCommand { get; }

        public AsyncRelayCommand SalesHistoryCommand { get; }

        public AsyncRelayCommand ResumeOrderCommand { get; }

        public bool CanManageMenu { get; set; } = true;

        public AsyncRelayCommand GoToMenuCommand { get; }

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

        public string TodayDisplay
        {
            get => _todayDisplay;
            private set => SetProperty(ref _todayDisplay, value);
        }

        public string LastUpdatedDisplay
        {
            get => _lastUpdatedDisplay;
            private set => SetProperty(ref _lastUpdatedDisplay, value);
        }

        public string TodaySalesDisplay
        {
            get => _todaySalesDisplay;
            private set => SetProperty(ref _todaySalesDisplay, value);
        }

        public string AverageOrderDisplay
        {
            get => _averageOrderDisplay;
            private set => SetProperty(ref _averageOrderDisplay, value);
        }

        public int PaidBillsToday
        {
            get => _paidBillsToday;
            private set
            {
                if (SetProperty(ref _paidBillsToday, value))
                {
                    OnPropertyChanged(nameof(PaidBillsDisplay));
                }
            }
        }

        // Card subtitles bind to an object-typed property, so the text is built here rather than with StringFormat.
        public string PaidBillsDisplay => PaidBillsToday == 1 ? "1 paid bill" : PaidBillsToday + " paid bills";

        public string TakeawayDisplay => ParcelOrdersCount + " takeaway";

        public int ReservedTables
        {
            get => _reservedTables;
            private set => SetProperty(ref _reservedTables, value);
        }

        public int OccupiedTables
        {
            get => _occupiedTables;
            private set
            {
                if (SetProperty(ref _occupiedTables, value))
                {
                    NotifyOccupancyChanged();
                }
            }
        }

        public int AvailableTables { get => _availableTables; private set => SetProperty(ref _availableTables, value); }

        public int TotalTables
        {
            get => _totalTables;
            private set
            {
                if (SetProperty(ref _totalTables, value))
                {
                    NotifyOccupancyChanged();
                }
            }
        }

        public int ActiveOrders { get => _activeOrders; private set => SetProperty(ref _activeOrders, value); }

        public double OccupancyPercent => TotalTables > 0 ? Math.Round(100d * OccupiedTables / TotalTables) : 0d;

        public string OccupancyDisplay => TotalTables > 0
            ? string.Format("{0} of {1} tables in use ({2:0}%)", OccupiedTables, TotalTables, OccupancyPercent)
            : "No tables set up yet";

        public int TotalOrdersToday
        {
            get => _totalOrdersToday;
            private set => SetProperty(ref _totalOrdersToday, value);
        }

        public int ParcelOrdersCount
        {
            get => _parcelOrdersCount;
            private set
            {
                if (SetProperty(ref _parcelOrdersCount, value))
                {
                    OnPropertyChanged(nameof(TakeawayDisplay));
                }
            }
        }

        public int StaleOrderCount
        {
            get => _staleOrderCount;
            private set
            {
                if (SetProperty(ref _staleOrderCount, value))
                {
                    OnPropertyChanged(nameof(HasStaleOrders));
                    OnPropertyChanged(nameof(StaleOrdersMessage));
                }
            }
        }

        public bool HasStaleOrders => StaleOrderCount > 0;

        public string StaleOrdersMessage => StaleOrderCount == 1
            ? "1 order has been open for over an hour and is still unpaid."
            : string.Format("{0} orders have been open for over an hour and are still unpaid.", StaleOrderCount);

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

        public bool HasOpenOrders => OpenOrderRows.Count > 0;

        public bool IsOpenOrdersEmpty => !HasOpenOrders;

        public bool HasRecentOrders => RecentOrders.Count > 0;

        public bool IsRecentOrdersEmpty => RecentOrders.Count == 0;

        public bool HasTopItems => TopItems.Count > 0;

        public bool IsTopItemsEmpty => TopItems.Count == 0;

        public bool HasPayments => PaymentRows.Count > 0;

        public bool IsPaymentsEmpty => PaymentRows.Count == 0;

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                await LoadSnapshotAsync();
                ClearStatus();
            }, "The dashboard could not be loaded.");
        }

        private Task OpenAsync(WorkspaceDestination destination)
        {
            if (OpenDestination != null)
            {
                return OpenDestination(destination);
            }

            _navigation.Navigate(destination);
            return Task.CompletedTask;
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

        private async Task StartTakeawayAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                await OpenTakeaway();
                await RefreshWorkspace();
                await LoadSnapshotAsync();
                ClearStatus();
            }, "A takeaway order could not be started.");
        }

        private async Task ResumeOpenOrderAsync(DashboardOpenOrder order)
        {
            if (order == null)
            {
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                await ResumeOrder(order.Row);
                await RefreshWorkspace();
                await LoadSnapshotAsync();
                ClearStatus();
            }, "That order could not be opened.");
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
            RefreshCommand.RaiseCanExecuteChanged();
            GoToTablesCommand.RaiseCanExecuteChanged();
            GoToMenuCommand.RaiseCanExecuteChanged();
            NewParcelCommand.RaiseCanExecuteChanged();
            OpenOrdersCommand.RaiseCanExecuteChanged();
            SalesHistoryCommand.RaiseCanExecuteChanged();
            ResumeOrderCommand.RaiseCanExecuteChanged();
        }

        private void NotifyOccupancyChanged()
        {
            OnPropertyChanged(nameof(OccupancyPercent));
            OnPropertyChanged(nameof(OccupancyDisplay));
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
            var nowLocal = ServeX.Services.BusinessTime.ToLocalTime(DateTime.UtcNow);

            TodayDisplay = nowLocal.ToString("dddd, d MMMM yyyy");
            LastUpdatedDisplay = "Updated " + nowLocal.ToString("HH:mm");
            TodaySalesDisplay = snapshot.TodaySales.Count == 1
                ? snapshot.TodaySales[0].DisplayAmount
                : string.Join(System.Environment.NewLine, snapshot.TodaySales.Select(total => $"{total.Currency} {total.DisplayAmount}"));
            PaidBillsToday = snapshot.PaidBillsToday;
            AverageOrderDisplay = snapshot.AverageOrderDisplay;
            ReservedTables = snapshot.ReservedTables;
            OccupiedTables = snapshot.OccupiedTables;
            AvailableTables = snapshot.AvailableTables;
            TotalTables = snapshot.TotalTables;
            ActiveOrders = snapshot.ActiveOrders;
            OpenOrders = snapshot.OpenOrders;
            OnPropertyChanged(nameof(OpenOrders));
            TotalOrdersToday = snapshot.TotalOrdersToday;
            ParcelOrdersCount = snapshot.ParcelOrdersCount;

            ReplaceAll(OpenOrderRows, snapshot.OpenOrders
                .Select(row => new DashboardOpenOrder(row, nowLocal))
                .OrderByDescending(order => order.Age));
            StaleOrderCount = OpenOrderRows.Count(order => order.IsStale);
            OnPropertyChanged(nameof(HasOpenOrders));
            OnPropertyChanged(nameof(IsOpenOrdersEmpty));

            ReplaceAll(RecentOrders, snapshot.RecentOrders);
            OnPropertyChanged(nameof(HasRecentOrders));
            OnPropertyChanged(nameof(IsRecentOrdersEmpty));

            ReplaceAll(TopItems, snapshot.TopItems);
            OnPropertyChanged(nameof(HasTopItems));
            OnPropertyChanged(nameof(IsTopItemsEmpty));

            ReplaceAll(PaymentRows, snapshot.Payments);
            OnPropertyChanged(nameof(HasPayments));
            OnPropertyChanged(nameof(IsPaymentsEmpty));
        }

        private static void ReplaceAll<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }
    }
}
