using System.Threading.Tasks;
using System.Runtime.Versioning;
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
        private readonly string _restaurantName;
        private readonly string _currentUsername;

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

            Dashboard = new DashboardViewModel(
                dashboardService,
                orderWindowService,
                GoToTables,
                GoToMenu,
                RefreshFromDashboardActionAsync);
            Menu = new MenuViewModel(menuService);
            Tables = new TablesViewModel(new TableService(), orderWindowService);
            Settings = new SettingsViewModel(databaseBackupService, saveFileDialogService);
            Menu.MenuItemsChanged += async (sender, args) => await RefreshWorkspaceAsync();
            Tables.TablesChanged += async (sender, args) => await RefreshWorkspaceAsync();

            SelectedWorkspaceIndex = 0;
        }

        public string Title => string.Format("{0} - ServeX", _restaurantName);

        public string WelcomeMessage => string.Format("Signed in as {0} | Menu management and restaurant operations", _currentUsername);

        public DashboardViewModel Dashboard { get; }

        public MenuViewModel Menu { get; }

        public TablesViewModel Tables { get; }

        public SettingsViewModel Settings { get; }

        public int SelectedWorkspaceIndex
        {
            get => _selectedWorkspaceIndex;
            set => SetProperty(ref _selectedWorkspaceIndex, value);
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
            await LoadSummaryAsync();
        }

        private async Task RefreshFromDashboardActionAsync()
        {
            await Menu.LoadAsync();
            await Tables.LoadAsync();
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
    }
}
