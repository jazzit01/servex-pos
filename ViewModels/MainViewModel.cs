using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using ServeX.Data;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class MainViewModel : ViewModelBase
    {
        private int _menuItemsCount;
        private int _tableCount;
        private int _roomCount;
        private int _orderCount;
        private int _selectedWorkspaceIndex;
        private readonly LogService _logService = new LogService();
        private readonly RelayCommand _navigateCommand;
        private readonly RelayCommand _goBackCommand;
        private string _restaurantName;
        private readonly string _currentUsername;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;

        public MainViewModel(AppSetting settings, User currentUser)
        {
            _restaurantName = settings?.RestaurantName ?? "ServeX";
            _currentUsername = currentUser?.Username ?? "Admin";

            var menuService = new MenuService();
            var orderService = new OrderService();
            var billingService = new BillingService();
            var receiptPrintService = new ReceiptPrintService();
            var billingWindowService = new BillingWindowService(billingService, receiptPrintService);
            var orderWindowService = new OrderWindowService(orderService, billingWindowService);
            var dashboardService = new DashboardService();
            var databaseBackupService = new DatabaseBackupService();
            var saveFileDialogService = new SaveFileDialogService();
            var applicationSettingsService = new ApplicationSettingsService();

            Dashboard = new DashboardViewModel(
                dashboardService,
                orderWindowService,
                GoToTables,
                GoToMenu,
                RefreshFromDashboardActionAsync);
            Menu = new MenuViewModel(menuService);
            Tables = new TablesViewModel(new TableService(), orderWindowService);
            Parcel = new ParcelViewModel(orderService, billingWindowService);
            Settings = new SettingsViewModel(
                applicationSettingsService,
                databaseBackupService,
                saveFileDialogService,
                UpdateRestaurantName);
            Menu.MenuItemsChanged += async (sender, args) => await RefreshWorkspaceAsync();
            Tables.TablesChanged += async (sender, args) => await RefreshWorkspaceAsync();

            _navigateCommand = new RelayCommand(async parameter => await NavigateToAsync(parameter), _ => true);
            _goBackCommand = new RelayCommand(_ => SelectedWorkspaceIndex = 0, _ => CanGoBack);

            SelectedWorkspaceIndex = 0;
        }

        public string Title => string.Format("{0} - ServeX", _restaurantName);

        public string RestaurantName => _restaurantName;

        public string CurrentUsername => _currentUsername;

        public string WelcomeMessage => string.Format("Signed in as {0} | Menu management and restaurant operations", _currentUsername);

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

        public RelayCommand NavigateCommand => _navigateCommand;

        public RelayCommand GoBackCommand => _goBackCommand;

        public DashboardViewModel Dashboard { get; }

        public MenuViewModel Menu { get; }

        public TablesViewModel Tables { get; }

        public ParcelViewModel Parcel { get; }

        public SettingsViewModel Settings { get; }

        public object CurrentWorkspace
        {
            get
            {
                switch (SelectedWorkspaceIndex)
                {
                    case 1:
                        return Menu;
                    case 2:
                        return Tables;
                    case 3:
                        return Parcel;
                    case 4:
                        return Settings;
                    default:
                        return Dashboard;
                }
            }
        }

        public int SelectedWorkspaceIndex
        {
            get => _selectedWorkspaceIndex;
            set
            {
                if (SetProperty(ref _selectedWorkspaceIndex, value))
                {
                    OnPropertyChanged(nameof(CurrentWorkspace));
                    OnPropertyChanged(nameof(CurrentSectionTitle));
                    OnPropertyChanged(nameof(CurrentSectionSubtitle));
                    OnPropertyChanged(nameof(CurrentSectionIconGlyph));
                    OnPropertyChanged(nameof(CanGoBack));
                    _goBackCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public int MenuItemsCount
        {
            get => _menuItemsCount;
            private set
            {
                if (SetProperty(ref _menuItemsCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public int TableCount
        {
            get => _tableCount;
            private set
            {
                if (SetProperty(ref _tableCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public int OrderCount
        {
            get => _orderCount;
            private set
            {
                if (SetProperty(ref _orderCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public int RoomCount
        {
            get => _roomCount;
            private set
            {
                if (SetProperty(ref _roomCount, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public string Summary => string.Format(
            "Menu Items: {0}   Tables: {1}   Rooms: {2}   Orders: {3}",
            MenuItemsCount,
            TableCount,
            RoomCount,
            OrderCount);

        public string CurrentSectionTitle
        {
            get
            {
                switch (SelectedWorkspaceIndex)
                {
                    case 1:
                        return "Menu management";
                    case 2:
                        return "Dining floor";
                    case 3:
                        return "Parcel desk";
                    case 4:
                        return "Settings and backups";
                    default:
                        return "Service dashboard";
                }
            }
        }

        public string CurrentSectionSubtitle
        {
            get
            {
                switch (SelectedWorkspaceIndex)
                {
                    case 1:
                        return "Maintain categories, pricing, availability, and item quality without leaving the service flow.";
                    case 2:
                        return "Monitor indoor and outdoor tables, inspect live status, and move directly into orders.";
                    case 3:
                        return "Handle takeaway orders with the same speed, totals, and billing flow as the floor.";
                    case 4:
                        return "Protect the database, confirm environment details, and keep day-to-day operations safe.";
                    default:
                        return "Track revenue, table activity, and recent orders from one calm operational overview.";
                }
            }
        }

        public string CurrentSectionIconGlyph
        {
            get
            {
                switch (SelectedWorkspaceIndex)
                {
                    case 1:
                        return "\uE8D2";
                    case 2:
                        return "\uE14C";
                    case 3:
                        return "\uE7BF";
                    case 4:
                        return "\uE713";
                    default:
                        return "\uE80F";
                }
            }
        }

        public bool CanGoBack => SelectedWorkspaceIndex != 0;

        public async Task LoadAsync()
        {
            await RefreshWorkspaceAsync();
        }

        private async Task LoadSummaryAsync()
        {
            using (var context = new ServeXDbContext())
            {
                MenuItemsCount = await context.MenuItems.CountAsync();
                TableCount = await context.Tables.CountAsync();
                RoomCount = await context.Rooms.CountAsync();
                OrderCount = await context.Orders.CountAsync();
            }
        }

        private async Task RefreshWorkspaceAsync()
        {
            await Dashboard.LoadAsync();
            await Menu.LoadAsync();
            await Tables.LoadAsync();
            await Parcel.LoadAsync();
            await Settings.LoadAsync();
            await LoadSummaryAsync();
        }

        private async Task RefreshFromDashboardActionAsync()
        {
            await Menu.LoadAsync();
            await Tables.LoadAsync();
            await Parcel.LoadAsync();
            await LoadSummaryAsync();
        }

        private void GoToTables()
        {
            SelectedWorkspaceIndex = 2;
        }

        private void GoToMenu()
        {
            SelectedWorkspaceIndex = 1;
        }

        private void UpdateRestaurantName(string restaurantName)
        {
            _restaurantName = string.IsNullOrWhiteSpace(restaurantName)
                ? "ServeX"
                : restaurantName;

            OnPropertyChanged(nameof(RestaurantName));
            OnPropertyChanged(nameof(Title));
        }

        private async Task NavigateToAsync(object parameter)
        {
            if (parameter == null)
            {
                return;
            }

            if (!int.TryParse(parameter.ToString(), out var targetIndex))
            {
                return;
            }

            try
            {
                if (targetIndex == 2)
                {
                    await Tables.LoadAsync();

                    if (Tables.IsStatusError)
                    {
                        SetStatus("Tables could not be loaded. Please try again.", true);
                    }
                    else if (Tables.IsEmpty)
                    {
                        SetStatus("No tables exist yet. Add the first table to begin.", false);
                    }
                    else
                    {
                        ClearStatus();
                    }
                }
                else
                {
                    ClearStatus();
                }

                SelectedWorkspaceIndex = targetIndex;
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Navigation to workspace failed", exception);
                SetStatus("That screen could not be opened right now.", true);
                MessageBox.Show(
                    "That screen could not be opened right now.",
                    "Navigation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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
    }
}
