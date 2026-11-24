using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Windows;
using ServeX.Presentation.Navigation;

using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class MainViewModel : ViewModelBase, System.IDisposable
    {
        private int _menuItemsCount;
        private int _tableCount;
        private int _roomCount;
        private int _orderCount;
        private readonly WorkspaceNavigation _navigation;
        private readonly WorkspaceSummaryService _summaryService;
        private readonly LogService _logService;
        private readonly AsyncRelayCommand _navigateCommand;
        private readonly RelayCommand _goBackCommand;
        private string _restaurantName;
        private readonly string _currentUsername;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;
        private readonly System.EventHandler _workspaceChanged;
        private readonly System.ComponentModel.PropertyChangedEventHandler _navigationChanged;
        private bool _refreshing;
        private bool _refreshAgain;
        private ViewModelBase _detail;
        private readonly DialogService _dialogs;

        public MainViewModel(AppSetting settings, User currentUser, DashboardViewModel dashboard,
            MenuViewModel menu, TablesViewModel tables, ParcelViewModel parcel, SettingsViewModel settingsViewModel, HistoryViewModel history, RoomsViewModel rooms,
            WorkspaceNavigation navigation, WorkspaceSummaryService summaryService, LogService logService, DialogService dialogs,
            OpenOrdersViewModel orders, OrderWorkspaceService orderWorkspace)
        {
            _restaurantName = settings?.RestaurantName ?? "ServeX";
            MoneyLanguage = System.Windows.Markup.XmlLanguage.GetLanguage(ReceiptProfile.Culture(settings?.Locale).Name);
            _currentUsername = currentUser?.Username ?? "Admin";
            _navigation = navigation;
            _summaryService = summaryService;
            _logService = logService;
            _dialogs = dialogs;
            CanManage = currentUser?.Role != UserRole.Cashier;
            Dashboard = dashboard;
            Dashboard.CanManageMenu = CanManage;
            Menu = menu;
            Tables = tables;
            Tables.CanManageTables = CanManage;
            Tables.PropertyChanged += OnTableAreaChanged;
            Parcel = parcel;
            Settings = settingsViewModel;
            History = history;
            Orders = orders;
            Orders.OpenTakeaway = OpenTakeawayAsync;
            Orders.Resume = row => row.Type == OrderType.Parcel ? OpenTakeawayAsync(row.Id) : orderWorkspace.ResumeAsync(row);
            Rooms = rooms;
            Settings.CurrentUserId = currentUser?.Id ?? 0;
            SignOutCommand = new AsyncRelayCommand(_ => SignOutAsync());
            Dashboard.RefreshWorkspace = RefreshFromDashboardActionAsync;
            Settings.RestaurantNameSaved += UpdateRestaurantName;
            _workspaceChanged = async (sender, args) => await RefreshAfterChangeAsync();
            Menu.MenuItemsChanged += _workspaceChanged;
            Tables.TablesChanged += _workspaceChanged;
            _navigateCommand = new AsyncRelayCommand(NavigateToAsync, _ => CanLeaveDetail);
            FloorAreaCommand = new AsyncRelayCommand(async area =>
            {
                if (SelectedDestination == WorkspaceDestination.Tables && Tables.IsBusy) return;
                await NavigateToAsync(WorkspaceDestination.Tables);
                if (SelectedDestination == WorkspaceDestination.Tables && _detail == null && area is TableArea tableArea)
                    Tables.SelectedAreaFilter = tableArea;
            }, _ => CanLeaveDetail);
            _goBackCommand = new RelayCommand(_ => GoBack(), _ => CanGoBack && CanLeaveDetail);
            _navigationChanged = (sender, args) =>
            {
                if (args.PropertyName == nameof(WorkspaceNavigation.Detail))
                {
                    if (_detail != null) _detail.PropertyChanged -= OnDetailPropertyChanged;
                    _detail = _navigation.Detail;
                    if (_detail != null) _detail.PropertyChanged += OnDetailPropertyChanged;
                    OnPropertyChanged(nameof(BackButtonLabel));
                    OnPropertyChanged(nameof(ShowFloorTabs));
                    OnPropertyChanged(nameof(ShowSalesTabs));
                    OnPropertyChanged(nameof(ShowManageTabs));
                    RefreshNavigationCommands();
                }
                else if (args.PropertyName == nameof(WorkspaceNavigation.Current))
                {
                    Tables.IsSetupWorkspace = SelectedDestination == WorkspaceDestination.TableSetup;
                    OnPropertyChanged(nameof(SelectedDestination));
                    NotifyWorkspaceTabs();
                    OnPropertyChanged(nameof(IsFloorSection));
                    OnPropertyChanged(nameof(IsOrdersSection));
                    OnPropertyChanged(nameof(IsSalesSection));
                    OnPropertyChanged(nameof(IsManageSection));
                    OnPropertyChanged(nameof(ShowFloorTabs));
                    OnPropertyChanged(nameof(ShowSalesTabs));
                    OnPropertyChanged(nameof(ShowManageTabs));
                }
                else return;
                OnPropertyChanged(nameof(CurrentWorkspace));
                OnPropertyChanged(nameof(CurrentSectionTitle));
                OnPropertyChanged(nameof(CurrentSectionSubtitle));
                OnPropertyChanged(nameof(CurrentSectionIconGlyph));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(ShowBackButton));
                _goBackCommand.RaiseCanExecuteChanged();
            };
            _navigation.PropertyChanged += _navigationChanged;
        }

        public System.Func<Task> SignOut { get; set; } = () => Task.CompletedTask;

        public AsyncRelayCommand SignOutCommand { get; }

        private async Task SignOutAsync()
        {
            if (_detail != null || Dashboard.IsBusy || Menu.IsBusy || Tables.IsBusy || Parcel.IsBusy || Settings.IsBusy)
            {
                SetStatus("Finish the current action and return to the workspace before signing out.", true);
                return;
            }

            if (!_dialogs.Confirm("Sign out?", "Return to sign-in? Unsaved changes will be discarded.")) return;
            try
            {
                await SignOut();
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Sign-out failed", exception);
                SetStatus("Could not return to sign-in. Try again.", true);
            }
        }

        public bool CanManage { get; }

        public System.Windows.Markup.XmlLanguage MoneyLanguage { get; private set; }

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

        public AsyncRelayCommand NavigateCommand => _navigateCommand;

        public AsyncRelayCommand FloorAreaCommand { get; }

        public RelayCommand GoBackCommand => _goBackCommand;

        public DashboardViewModel Dashboard { get; }

        public MenuViewModel Menu { get; }

        public TablesViewModel Tables { get; }

        public ParcelViewModel Parcel { get; }

        public RoomsViewModel Rooms { get; }

        public HistoryViewModel History { get; }

        public OpenOrdersViewModel Orders { get; }

        public bool IsFloorSection => SelectedDestination is WorkspaceDestination.Tables or WorkspaceDestination.Rooms;
        public bool IsOrdersSection => SelectedDestination is WorkspaceDestination.Orders or WorkspaceDestination.Parcel;
        public bool IsSalesSection => SelectedDestination is WorkspaceDestination.History or WorkspaceDestination.Dashboard;
        public bool IsManageSection => SelectedDestination is WorkspaceDestination.Menu or WorkspaceDestination.Settings or WorkspaceDestination.TableSetup;
        public bool ShowFloorTabs => _detail == null && IsFloorSection;
        public bool ShowSalesTabs => _detail == null && IsSalesSection;
        public bool ShowManageTabs => _detail == null && IsManageSection;

        public bool IsIndoorTab => SelectedDestination == WorkspaceDestination.Tables && Tables.IsIndoorFilterSelected;
        public bool IsOutdoorTab => SelectedDestination == WorkspaceDestination.Tables && Tables.IsOutdoorFilterSelected;
        public bool IsRoomsTab => SelectedDestination == WorkspaceDestination.Rooms;
        public bool IsTransactionsTab => SelectedDestination == WorkspaceDestination.History;
        public bool IsOverviewTab => SelectedDestination == WorkspaceDestination.Dashboard;
        public bool IsMenuTab => SelectedDestination == WorkspaceDestination.Menu;
        public bool IsTableSetupTab => SelectedDestination == WorkspaceDestination.TableSetup;
        public bool IsSettingsTab => SelectedDestination == WorkspaceDestination.Settings;

        private void OnTableAreaChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(TablesViewModel.SelectedAreaFilter)) return;
            OnPropertyChanged(nameof(IsIndoorTab));
            OnPropertyChanged(nameof(IsOutdoorTab));
        }

        private void NotifyWorkspaceTabs()
        {
            OnPropertyChanged(nameof(IsIndoorTab));
            OnPropertyChanged(nameof(IsOutdoorTab));
            OnPropertyChanged(nameof(IsRoomsTab));
            OnPropertyChanged(nameof(IsTransactionsTab));
            OnPropertyChanged(nameof(IsOverviewTab));
            OnPropertyChanged(nameof(IsMenuTab));
            OnPropertyChanged(nameof(IsTableSetupTab));
            OnPropertyChanged(nameof(IsSettingsTab));
        }

        private async Task OpenTakeawayAsync(int? id)
        {
            if (id is int orderId)
            {
                if (!await Parcel.ResumeSavedOrderAsync(orderId))
                {
                    if (Parcel.IsStatusError) SetStatus(Parcel.StatusMessage, true);
                    return;
                }
            }
            else if (!Parcel.StartNewParcel()) return;

            ClearStatus();
            await _navigation.OpenDetailAsync(Parcel);
            Parcel.DiscardDraft();
        }

        public SettingsViewModel Settings { get; }

        public object CurrentWorkspace
        {
            get
            {
                if (_detail != null) return _detail;
                switch (SelectedDestination)
                {
                    case WorkspaceDestination.Menu:
                        return Menu;
                    case WorkspaceDestination.Tables:
                    case WorkspaceDestination.TableSetup:
                        return Tables;
                    case WorkspaceDestination.Orders:
                        return Orders;
                    case WorkspaceDestination.Parcel:
                        return Parcel;
                    case WorkspaceDestination.Rooms:
                        return Rooms;
                    case WorkspaceDestination.History:
                        return History;
                    case WorkspaceDestination.Settings:
                        return Settings;
                    default:
                        return Dashboard;
                }
            }
        }

        public WorkspaceDestination SelectedDestination => _navigation.Current;

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
                if (_detail is OrderViewModel order) return order.WindowTitle;
                if (_detail is BillingViewModel billing) return billing.WindowTitle;
                if (_detail is ParcelViewModel) return "Takeaway order";
                switch (SelectedDestination)
                {
                    case WorkspaceDestination.Menu:
                        return "Menu management";
                    case WorkspaceDestination.TableSetup:
                        return "Table setup";
                    case WorkspaceDestination.Orders:
                        return "Open orders";
                    case WorkspaceDestination.Tables:
                        return "Dining floor";
                    case WorkspaceDestination.Parcel:
                        return "Parcel desk";
                    case WorkspaceDestination.Rooms:
                        return "Rooms";
                    case WorkspaceDestination.History:
                        return "Sales";
                    case WorkspaceDestination.Settings:
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
                if (_detail is OrderViewModel order) return $"{order.LocationKind} service · {order.TableStatus}";
                if (_detail is BillingViewModel billing) return billing.TableNumber;
                return string.Empty;
            }
        }

        public string CurrentSectionIconGlyph
        {
            get
            {
                switch (SelectedDestination)
                {
                    case WorkspaceDestination.Menu:
                        return "\uE8D2";
                    case WorkspaceDestination.Tables:
                        return "\uE14C";
                    case WorkspaceDestination.Parcel:
                        return "\uE7BF";
                    case WorkspaceDestination.Settings:
                        return "\uE713";
                    default:
                        return "\uE80F";
                }
            }
        }

        public bool CanGoBack => _detail != null || _navigation.CanGoBack;

        public string BackButtonLabel => _detail != null ? "Back" : "Back to floor";

        public bool ShowBackButton => _detail != null;

        private bool CanLeaveDetail => _detail switch
        {
            OrderViewModel order => !order.IsBusy,
            BillingViewModel billing => !billing.IsBusy,
            ParcelViewModel parcel => !parcel.IsBusy,
            _ => true
        };

        private void OnDetailPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(OrderViewModel.IsBusy)) RefreshNavigationCommands();
            if (args.PropertyName == nameof(OrderViewModel.WindowTitle)) OnPropertyChanged(nameof(CurrentSectionTitle));
            if (args.PropertyName is nameof(OrderViewModel.TableStatus) or nameof(BillingViewModel.TableNumber))
                OnPropertyChanged(nameof(CurrentSectionSubtitle));
        }

        private void RefreshNavigationCommands()
        {
            _navigateCommand.RaiseCanExecuteChanged();
            FloorAreaCommand.RaiseCanExecuteChanged();
            _goBackCommand.RaiseCanExecuteChanged();
        }

        private bool ConfirmLeavingDetail() =>
            (_detail is not OrderViewModel { HasUnsavedChanges: true } &&
             _detail is not ParcelViewModel { HasUnsavedChanges: true }) ||
            _dialogs.Confirm("Leave this order?", "Unsaved changes to this order will be discarded.");

        private void GoBack()
        {
            if (!ConfirmLeavingDetail()) return;
            if (_detail != null) _navigation.CloseDetail();
            else _navigation.GoBack();
        }

        public async Task LoadAsync()
        {
            await RefreshWorkspaceAsync();
        }

        private async Task LoadSummaryAsync()
        {
            var summary = await _summaryService.LoadAsync();
            MenuItemsCount = summary.MenuItems;
            TableCount = summary.Tables;
            RoomCount = summary.Rooms;
            OrderCount = summary.Orders;
        }

        private async Task RefreshAfterChangeAsync()
        {
            if (_refreshing) { _refreshAgain = true; return; }
            _refreshing = true;
            try
            {
                do
                {
                    _refreshAgain = false;
                    // The originating workspace already refreshed its own data. Preserve editors
                    // and settings drafts; update only dependent overview/parcel data here.
                    await Dashboard.LoadAsync();
                    await Parcel.LoadAsync();
                    await LoadSummaryAsync();
                } while (_refreshAgain);
            }
            catch (System.Exception exception)
            {
                _logService.LogError("Workspace refresh failed", exception);
                SetStatus("Changes were saved, but the workspace could not be refreshed. Please try again.", true);
            }
            finally { _refreshing = false; }
        }

        private async Task RefreshWorkspaceAsync()
        {
            await Tables.LoadAsync();
            await Dashboard.LoadAsync();
            await Menu.LoadAsync();
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

        private void UpdateRestaurantName(string restaurantName)
        {
            _restaurantName = string.IsNullOrWhiteSpace(restaurantName)
                ? "ServeX"
                : restaurantName;

            MoneyLanguage = System.Windows.Markup.XmlLanguage.GetLanguage(ReceiptProfile.Culture(Settings.Locale).Name);
            OnPropertyChanged(nameof(MoneyLanguage));
            OnPropertyChanged(nameof(RestaurantName));
            OnPropertyChanged(nameof(Title));
        }

        private async Task NavigateToAsync(object parameter)
        {
            if (parameter is not WorkspaceDestination destination || !System.Enum.IsDefined(destination)) return;
            if (!CanManage && destination is WorkspaceDestination.Menu or WorkspaceDestination.Settings or WorkspaceDestination.TableSetup) return;
            if (!ConfirmLeavingDetail()) return;
            _navigation.CloseDetails();
            if (destination == SelectedDestination) return;

            try
            {
                if (destination == WorkspaceDestination.Tables)
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

                _navigation.Navigate(destination);
                if (destination == WorkspaceDestination.History) await History.RefreshAsync();
                if (destination == WorkspaceDestination.Orders) await Orders.LoadAsync();
                if (destination == WorkspaceDestination.Dashboard) await Dashboard.LoadAsync();
                if (destination == WorkspaceDestination.Rooms) await Rooms.LoadAsync();
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

        public void Dispose()
        {
            _navigation.PropertyChanged -= _navigationChanged;
            if (_detail != null) _detail.PropertyChanged -= OnDetailPropertyChanged;
            _navigation.CloseDetails();
            Orders.OpenTakeaway = _ => Task.CompletedTask;
            Orders.Resume = _ => Task.CompletedTask;
            Menu.MenuItemsChanged -= _workspaceChanged;
            Tables.TablesChanged -= _workspaceChanged;
            Tables.PropertyChanged -= OnTableAreaChanged;
            Settings.RestaurantNameSaved -= UpdateRestaurantName;
            Dashboard.RefreshWorkspace = () => Task.CompletedTask;
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
