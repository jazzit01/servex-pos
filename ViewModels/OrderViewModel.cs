using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class OrderViewModel : ViewModelBase
    {
        private readonly LogService _logService;
        private readonly KitchenWorkspaceService _kitchen;
        private readonly TableService _tables;
        private readonly DialogService _dialogs;
        private readonly OrderService _orderService;
        private readonly BillingWorkspaceService _billingWorkspaceService;
        private readonly int _tableId;
        private readonly OrderType _locationType;
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyCategoryFilter(); }
        }
        public bool IsMenuEmpty => FilteredMenuItems.Count == 0;

        private bool _isBusy;
        private bool _hasBlockingError;
        private int? _orderId;
        private int _revision;
        private (int MenuItemId, int Quantity)[] _savedItems = Array.Empty<(int, int)>();
        private string _tableNumber = string.Empty;
        private TableStatus _tableStatus = TableStatus.Available;
        private string _selectedCategory = "All";
        private string _statusMessage = string.Empty;
        private bool _isStatusError;
        private decimal _taxPercentage = 5m;

        public OrderViewModel(OrderService orderService, BillingWorkspaceService billingWorkspaceService, int tableId, LogService logService, DialogService dialogs, KitchenWorkspaceService kitchen, TableService tables, OrderType locationType = OrderType.Table)
        {
            _logService = logService;
            _kitchen = kitchen;
            _tables = tables;
            _dialogs = dialogs;
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _billingWorkspaceService = billingWorkspaceService ?? throw new ArgumentNullException(nameof(billingWorkspaceService));
            _tableId = tableId;
            _locationType = locationType;

            Categories = new ObservableCollection<string>();
            MenuItems = new ObservableCollection<MenuItem>();
            FilteredMenuItems = new ObservableCollection<MenuItem>();
            OrderItems = new ObservableCollection<OrderLineItemViewModel>();
            OrderItems.CollectionChanged += OnOrderItemsCollectionChanged;

            AddItemCommand = new RelayCommand(menuItem => AddItem(menuItem as MenuItem), menuItem => !IsBusy && menuItem is MenuItem);
            IncreaseQuantityCommand = new RelayCommand(item => IncreaseQuantity(item as OrderLineItemViewModel), item => !IsBusy && item is OrderLineItemViewModel);
            DecreaseQuantityCommand = new RelayCommand(item => DecreaseQuantity(item as OrderLineItemViewModel), item => !IsBusy && item is OrderLineItemViewModel);
            RemoveItemCommand = new RelayCommand(item => RemoveItem(item as OrderLineItemViewModel), item => !IsBusy && item is OrderLineItemViewModel);
            SaveOrderCommand = new AsyncRelayCommand(async _ => await SaveOrderAsync(), _ => CanSaveOrder);
            TransferCommand = new AsyncRelayCommand(_ => TransferAsync(), _ => !IsBusy && CanTransfer && _orderId.HasValue);
            SendKitchenCommand = new AsyncRelayCommand(_ => SendKitchenAsync(), _ => !IsBusy && _orderId.HasValue);
            MarkServedCommand = new AsyncRelayCommand(_ => MarkServedAsync(), _ => !IsBusy && _orderId.HasValue);
            CancelOrderCommand = new AsyncRelayCommand(_ => CancelOrderAsync(), _ => !IsBusy && _orderId.HasValue);
            GenerateBillCommand = new AsyncRelayCommand(async _ => await GenerateBillAsync(), _ => CanGenerateBill);
        }

        public ObservableCollection<string> Categories { get; }

        public ObservableCollection<MenuItem> MenuItems { get; }

        public ObservableCollection<MenuItem> FilteredMenuItems { get; }

        public ObservableCollection<OrderLineItemViewModel> OrderItems { get; }

        public RelayCommand AddItemCommand { get; }

        public RelayCommand IncreaseQuantityCommand { get; }

        public RelayCommand DecreaseQuantityCommand { get; }

        public RelayCommand RemoveItemCommand { get; }

        public AsyncRelayCommand SaveOrderCommand { get; }

        public bool CanTransfer => _locationType == OrderType.Table;
        public System.Collections.Generic.IReadOnlyList<Table> TransferTargets { get; private set; } = Array.Empty<Table>();
        public Table TransferTarget { get; set; }
        public AsyncRelayCommand TransferCommand { get; }

        public AsyncRelayCommand SendKitchenCommand { get; }
        public AsyncRelayCommand MarkServedCommand { get; }

        public AsyncRelayCommand CancelOrderCommand { get; }

        public AsyncRelayCommand GenerateBillCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanSaveOrder));
                    NotifyCommandStates();
                }
            }
        }

        public string WindowTitle => $"Order - {TableNumber}";

        public string LocationKind => _locationType == OrderType.Room ? "Room" : "Table";

        public string TableNumber
        {
            get => _tableNumber;
            private set
            {
                if (SetProperty(ref _tableNumber, value))
                {
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        public TableStatus TableStatus
        {
            get => _tableStatus;
            private set => SetProperty(ref _tableStatus, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    ApplyCategoryFilter();
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

        public bool HasOrderItems => OrderItems.Count > 0;

        public bool IsEmptyOrder => OrderItems.Count == 0;

        public bool CanSaveOrder => !IsBusy && OrderItems.Count > 0;

        private (int MenuItemId, int Quantity)[] CurrentItems() =>
            OrderItems.Select(item => (item.MenuItemId, item.Quantity)).OrderBy(item => item.MenuItemId).ToArray();

        public bool HasUnsavedChanges => !_savedItems.SequenceEqual(CurrentItems());

        public bool CanGenerateBill => !IsBusy && OrderItems.Count > 0;

        public bool HasBlockingError
        {
            get => _hasBlockingError;
            private set => SetProperty(ref _hasBlockingError, value);
        }

        public decimal Subtotal => OrderPricing.Round(OrderItems.Sum(item => item.LineTotal));

        internal bool AllowReservedTable { get; set; }

        public decimal TaxPercentage
        {
            get => _taxPercentage;
            private set
            {
                if (SetProperty(ref _taxPercentage, value))
                {
                    OnPropertyChanged(nameof(TaxLabel));
                    NotifyTotalsChanged();
                }
            }
        }

        public string TaxLabel => string.Format("Tax ({0:0.##}%)", TaxPercentage);

        public decimal Tax => OrderPricing.Tax(OrderItems.Where(item => !item.TaxExempt).Sum(item => item.LineTotal), TaxPercentage);

        public decimal Total => Subtotal + Tax;

        public async Task LoadAsync()
        {
            HasBlockingError = false;

            await ExecuteBusyActionAsync(async () =>
            {
                var table = await _orderService.GetOrderLocationAsync(_tableId, _locationType);
                if (table == null)
                {
                    throw new InvalidOperationException("The selected table could not be loaded.");
                }

                TableNumber = table.TableNumber;
                TableStatus = table.Status;
                if (CanTransfer)
                {
                    TransferTargets = (await _tables.GetTablesAsync()).Where(target => target.Id != _tableId &&
                        target.Status is TableStatus.Available or TableStatus.Occupied).ToList();
                    OnPropertyChanged(nameof(TransferTargets));
                }
                TaxPercentage = await _orderService.GetTaxPercentageAsync();

                var menuItems = await _orderService.GetMenuItemsAsync() ?? new System.Collections.Generic.List<MenuItem>();
                MenuItems.Clear();
                foreach (var menuItem in menuItems)
                {
                    if (menuItem != null)
                    {
                        MenuItems.Add(menuItem);
                    }
                }

                Categories.Clear();
                Categories.Add("All");
                foreach (var category in menuItems
                             .Select(item => item.Category)
                             .Where(category => !string.IsNullOrWhiteSpace(category))
                             .Distinct())
                {
                    Categories.Add(category);
                }

                if (!Categories.Contains(SelectedCategory))
                {
                    SelectedCategory = "All";
                }
                else
                {
                    ApplyCategoryFilter();
                }

                var order = await _orderService.GetActiveOrderForTableAsync(_tableId, _locationType);
                OrderItems.Clear();
                _orderId = order?.Id;
                _revision = order?.Revision ?? 0;

                if (order != null)
                {
                    foreach (var item in (order.Items ?? Array.Empty<OrderItem>()).OrderBy(item => item.MenuItem?.Category).ThenBy(item => item.MenuItem?.Name))
                    {
                        AddLoadedItem(item);
                    }

                    SetStatus(string.Format("Loaded active order #{0}.", order.Id), false);
                }
                else
                {
                    ClearStatus();
                }

                _savedItems = CurrentItems();
                NotifyTotalsChanged();
            }, "The order screen could not be loaded.");

            if (string.IsNullOrWhiteSpace(TableNumber))
            {
                HasBlockingError = true;
                if (!HasStatusMessage)
                {
                    SetStatus("The order screen could not be loaded.", true);
                }
            }
        }

        private void AddItem(MenuItem menuItem)
        {
            if (menuItem == null)
            {
                return;
            }

            if (!menuItem.IsAvailable)
            {
                SetStatus("This item is currently unavailable.", true);
                return;
            }

            var existing = OrderItems.FirstOrDefault(item => item.MenuItemId == menuItem.Id);
            if (existing != null)
            {
                existing.Quantity += 1;
            }
            else
            {
                var newItem = CreateLineItem(menuItem.Id, menuItem.Name, menuItem.Category, menuItem.Price, 1, menuItem.TaxExempt);
                OrderItems.Add(newItem);
            }

            ClearStatus();
            NotifyTotalsChanged();
        }

        private void IncreaseQuantity(OrderLineItemViewModel item)
        {
            if (item == null)
            {
                return;
            }

            item.Quantity += 1;
            NotifyTotalsChanged();
        }

        private void DecreaseQuantity(OrderLineItemViewModel item)
        {
            if (item == null)
            {
                return;
            }

            if (item.Quantity <= 1)
            {
                RemoveItem(item);
                return;
            }

            item.Quantity -= 1;
            NotifyTotalsChanged();
        }

        private void RemoveItem(OrderLineItemViewModel item)
        {
            if (item == null)
            {
                return;
            }

            OrderItems.Remove(item);
            NotifyTotalsChanged();
        }

        private async Task TransferAsync()
        {
            var destination = TransferTarget;
            if (destination == null)
            {
                SetStatus("Select a destination table first.", true);
                return;
            }
            if (HasUnsavedChanges)
            {
                SetStatus("Save the current items before moving the order.", true);
                return;
            }

            var merge = destination.Status == TableStatus.Occupied;
            if (!_dialogs.Confirm(merge ? "Merge table orders?" : "Move table order?",
                $"Move the saved items to {destination.TableNumber}?")) return;
            await ExecuteBusyActionAsync(async () =>
            {
                await _orderService.TransferTableOrderAsync(_orderId.Value, _revision, destination.Id, merge);
                _orderId = null;
                _revision = 0;
                OrderItems.Clear();
                _savedItems = CurrentItems();
                TableStatus = TableStatus.Available;
                NotifyTotalsChanged();
                SetStatus($"Order moved to {destination.TableNumber}. Open that table to continue.", false);
            }, "Could not move the order. Refresh the dining floor and try again.");
        }

        private Task SendKitchenAsync() => ExecuteBusyActionAsync(async () =>
        {
            var result = await _kitchen.SendAsync(_orderId.Value, _revision);
            _revision = result.Revision;
            SetStatus(result.Message, false);
        }, "Could not finish sending the ticket. If it printed, check with the kitchen before retrying.");

        private Task MarkServedAsync() => ExecuteBusyActionAsync(async () =>
        {
            _revision = await _kitchen.MarkServedAsync(_orderId.Value, _revision);
            SetStatus("Order marked served.", false);
        }, "Could not mark the order served. Send new items to the kitchen first.");

        private async Task CancelOrderAsync()
        {
            if (_orderId is not int orderId || !_dialogs.Confirm("Cancel order?",
                $"Cancel order #{orderId}? The order will remain in the records, and unsaved changes will be discarded."))
                return;

            await ExecuteBusyActionAsync(async () =>
            {
                await _orderService.CancelOrderAsync(orderId);
                _orderId = null;
                _revision = 0;
            _revision = 0;
                OrderItems.Clear();
                _savedItems = CurrentItems();
                TableStatus = (await _orderService.GetOrderLocationAsync(_tableId, _locationType))?.Status ?? TableStatus;
                NotifyTotalsChanged();
                SetStatus($"Order #{orderId} cancelled.", false);
            }, "This order could not be cancelled. Reopen it to check its current status.");
        }

        private async Task SaveOrderAsync()
        {
            await PersistOrderAsync(true);
        }

        private async Task GenerateBillAsync()
        {
            var order = await PersistOrderAsync(false);
            if (order == null)
            {
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                await _billingWorkspaceService.ShowAsync(order.Id);
            }, "The billing screen could not be opened.");
            await LoadAsync();
        }

        private void ApplyCategoryFilter()
        {
            FilteredMenuItems.Clear();
            var query = (SearchText ?? string.Empty).Trim();
            foreach (var item in MenuItems.Where(item =>
                (SelectedCategory == "All" || item.Category == SelectedCategory) &&
                (query.Length == 0 || (item.Name ?? string.Empty).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 (item.Category ?? string.Empty).Contains(query, StringComparison.CurrentCultureIgnoreCase))))
                FilteredMenuItems.Add(item);
            OnPropertyChanged(nameof(IsMenuEmpty));
        }

        private OrderLineItemViewModel CreateLineItem(int menuItemId, string name, string category, decimal unitPrice, int quantity, bool taxExempt)
        {
            return new OrderLineItemViewModel
            {
                MenuItemId = menuItemId,
                TaxExempt = taxExempt,
                Name = name,
                Category = category,
                UnitPrice = unitPrice,
                Quantity = quantity
            };
        }

        private void AddLoadedItem(OrderItem item)
        {
            var menuItem = item.MenuItem;
            var lineItem = CreateLineItem(
                item.MenuItemId,
                menuItem?.Name ?? "Menu Item",
                menuItem?.Category ?? string.Empty,
                item.Price,
                item.Quantity, item.TaxExempt);

            OrderItems.Add(lineItem);
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
                HasBlockingError = true;
                SetStatus(fallbackMessage, true);
            }
            catch (Exception exception)
            {
                _logService.LogError(fallbackMessage, exception);
                HasBlockingError = true;
                SetStatus(fallbackMessage, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void NotifyTotalsChanged()
        {
            OnPropertyChanged(nameof(Subtotal));
            OnPropertyChanged(nameof(Tax));
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(HasOrderItems));
            OnPropertyChanged(nameof(IsEmptyOrder));
            OnPropertyChanged(nameof(CanSaveOrder));
            OnPropertyChanged(nameof(CanGenerateBill));
            NotifyCommandStates();
        }

        private void NotifyCommandStates()
        {
            AddItemCommand.RaiseCanExecuteChanged();
            IncreaseQuantityCommand.RaiseCanExecuteChanged();
            DecreaseQuantityCommand.RaiseCanExecuteChanged();
            RemoveItemCommand.RaiseCanExecuteChanged();
            SaveOrderCommand.RaiseCanExecuteChanged();
            GenerateBillCommand.RaiseCanExecuteChanged();
            CancelOrderCommand.RaiseCanExecuteChanged();
            SendKitchenCommand.RaiseCanExecuteChanged();
            MarkServedCommand.RaiseCanExecuteChanged();
            TransferCommand.RaiseCanExecuteChanged();
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

        private void OnLineItemQuantityChanged(object sender, EventArgs e)
        {
            NotifyTotalsChanged();
        }

        private void OnOrderItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (OrderLineItemViewModel item in e.NewItems)
                {
                    item.QuantityChanged += OnLineItemQuantityChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (OrderLineItemViewModel item in e.OldItems)
                {
                    item.QuantityChanged -= OnLineItemQuantityChanged;
                }
            }

            NotifyTotalsChanged();
        }

        private async Task<Order> PersistOrderAsync(bool showSuccessMessage)
        {
            if (!OrderItems.Any())
            {
                SetStatus("Add at least one item before saving the order.", true);
                return null;
            }

            var items = OrderItems.Select(item => new OrderSaveItem
            {
                MenuItemId = item.MenuItemId,
                Quantity = item.Quantity,
                Price = item.UnitPrice
            }).ToList();

            Order savedOrder = null;
            await ExecuteBusyActionAsync(async () =>
            {
                savedOrder = await _orderService.SaveTableOrderAsync(_tableId, items, _orderId, _revision, AllowReservedTable, _locationType);
                _orderId = savedOrder.Id;
                _revision = savedOrder.Revision;
                foreach (var savedItem in savedOrder.Items)
                {
                    var matching = OrderItems.Where(item => item.MenuItemId == savedItem.MenuItemId).ToArray();
                    foreach (var duplicate in matching.Skip(1)) OrderItems.Remove(duplicate);
                    matching[0].TaxExempt = savedItem.TaxExempt;
                    matching[0].UnitPrice = savedItem.Price;
                    matching[0].Quantity = savedItem.Quantity;
                }
                NotifyTotalsChanged();
                _savedItems = CurrentItems();
                TableStatus = TableStatus.Occupied;

                if (showSuccessMessage)
                {
                    SetStatus(string.Format("Order #{0} saved successfully.", savedOrder.Id), false);
                }
            }, "The order could not be saved.");

            return savedOrder;
        }
    }
}
