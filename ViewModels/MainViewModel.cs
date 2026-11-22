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
            MenuViewModel menu, TablesViewModel tables, ParcelViewModel parcel, SettingsViewModel settingsViewModel,
            WorkspaceNavigation navigation, WorkspaceSummaryService summaryService, LogService logService, DialogService dialogs)
        {
            _restaurantName = settings?.RestaurantName ?? "ServeX";
            _currentUsername = currentUser?.Username ?? "Admin";
            _navigation = navigation;
            _summaryService = summaryService;
            _logService = logService;
            _dialogs = dialogs;
            Dashboard = dashboard;
            Menu = menu;
            Tables = tables;
            Parcel = parcel;
            Settings = settingsViewModel;
            Dashboard.RefreshWorkspace = RefreshFromDashboardActionAsync;
            Settings.RestaurantNameSaved += UpdateRestaurantName;
            _workspaceChanged = async (sender, args) => await RefreshAfterChangeAsync();
            Menu.MenuItemsChanged += _workspaceChanged;
            Tables.TablesChanged += _workspaceChanged;
            _navigateCommand = new AsyncRelayCommand(NavigateToAsync, _ => CanLeaveDetail);
            _goBackCommand = new RelayCommand(_ => GoBack(), _ => CanGoBack && CanLeaveDetail);
            _navigationChanged = (sender, args) =>
            {
                if (args.PropertyName == nameof(WorkspaceNavigation.Detail))
                {
                    if (_detail != null) _detail.PropertyChanged -= OnDetailPropertyChanged;
                    _detail = _navigation.Detail;
                    if (_detail != null) _detail.PropertyChanged += OnDetailPropertyChanged;
                    OnPropertyChanged(nameof(BackButtonLabel));
                    RefreshNavigationCommands();
                }
                else if (args.PropertyName == nameof(WorkspaceNavigation.Current))
                {
                    OnPropertyChanged(nameof(SelectedDestination));
                }
                else return;
                OnPropertyChanged(nameof(CurrentWorkspace));
                OnPropertyChanged(nameof(CurrentSectionTitle));
                OnPropertyChanged(nameof(CurrentSectionSubtitle));
                OnPropertyChanged(nameof(CurrentSectionIconGlyph));
                OnPropertyChanged(nameof(CanGoBack));
                _goBackCommand.RaiseCanExecuteChanged();
            };
            _navigation.PropertyChanged += _navigationChanged;
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

        public AsyncRelayCommand NavigateCommand => _navigateCommand;

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
                if (_detail != null) return _detail;
                switch (SelectedDestination)
                {
                    case WorkspaceDestination.Menu:
                        return Menu;
                    case WorkspaceDestination.Tables:
                        return Tables;
                    case WorkspaceDestination.Parcel:
                        return Parcel;
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
                switch (SelectedDestination)
                {
                    case WorkspaceDestination.Menu:
                        return "Menu management";
                    case WorkspaceDestination.Tables:
                        return "Dining floor";
                    case WorkspaceDestination.Parcel:
                        return "Parcel desk";
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
                if (_detail is OrderViewModel order) return $"Table service · {order.TableStatus}";
                if (_detail is BillingViewModel billing) return billing.TableNumber;
                switch (SelectedDestination)
                {
                    case WorkspaceDestination.Menu:
                        return "Maintain categories, pricing, availability, and item quality without leaving the service flow.";
                    case WorkspaceDestination.Tables:
                        return "Monitor indoor and outdoor tables, inspect live status, and move directly into orders.";
                    case WorkspaceDestination.Parcel:
                        return "Handle takeaway orders with the same speed, totals, and billing flow as the floor.";
                    case WorkspaceDestination.Settings:
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

        public string BackButtonLabel => _detail != null ? "Back" : "Back to dashboard";

        private bool CanLeaveDetail => _detail switch
        {
            OrderViewModel order => !order.IsBusy,
            BillingViewModel billing => !billing.IsBusy,
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
            _goBackCommand.RaiseCanExecuteChanged();
        }

        private bool ConfirmLeavingDetail() =>
            _detail is not OrderViewModel { HasUnsavedChanges: true } ||
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
            if (parameter is not WorkspaceDestination destination || !System.Enum.IsDefined(destination)) return;
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
            Menu.MenuItemsChanged -= _workspaceChanged;
            Tables.TablesChanged -= _workspaceChanged;
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
