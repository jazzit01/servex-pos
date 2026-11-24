using System;
using System.Collections.Generic;
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
    public class ParcelViewModel : ViewModelBase
    {
        private readonly DialogService _dialogs;
        private readonly LogService _logService;
        private readonly KitchenWorkspaceService _kitchen;
        private readonly OrderService _orderService;
        private readonly BillingWorkspaceService _billingWorkspaceService;
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyCategoryFilter(); }
        }
        public bool IsMenuEmpty => FilteredMenuItems.Count == 0;

        private bool _isBusy;
        private int? _orderId;
        private (int MenuItemId, int Quantity, decimal Price)[] _savedItems = Array.Empty<(int, int, decimal)>();
        private string _savedCustomer = string.Empty;

        public bool HasUnsavedChanges => (CustomerName ?? string.Empty) != _savedCustomer ||
            !_savedItems.SequenceEqual(CurrentItems());

        private IEnumerable<(int MenuItemId, int Quantity, decimal Price)> CurrentItems() =>
            OrderItems.Select(item => (item.MenuItemId, item.Quantity, item.UnitPrice));

        private void RecordSavedDraft()
        {
            _savedItems = CurrentItems().ToArray();
            _savedCustomer = CustomerName ?? string.Empty;
        }
        private int _revision;
        private string _customerName = string.Empty;
        private string _selectedCategory = "All";
        private string _statusMessage = string.Empty;
        private bool _isStatusError;
        private decimal _taxPercentage = 5m;

        public ParcelViewModel(OrderService orderService, BillingWorkspaceService billingWorkspaceService, LogService logService, DialogService dialogs, KitchenWorkspaceService kitchen)
        {
            _dialogs = dialogs;
            _logService = logService;
            _kitchen = kitchen;
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _billingWorkspaceService = billingWorkspaceService ?? throw new ArgumentNullException(nameof(billingWorkspaceService));

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
            SendKitchenCommand = new AsyncRelayCommand(_ => SendKitchenAsync(), _ => !IsBusy && _orderId.HasValue);
            MarkServedCommand = new AsyncRelayCommand(_ => MarkServedAsync(), _ => !IsBusy && _orderId.HasValue);
            CancelOrderCommand = new AsyncRelayCommand(_ => CancelOrderAsync(), _ => !IsBusy && _orderId.HasValue);
            GenerateBillCommand = new AsyncRelayCommand(async _ => await GenerateBillAsync(), _ => CanGenerateBill);
            NewParcelCommand = new RelayCommand(_ => StartNewParcel(), _ => !IsBusy);
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

        public AsyncRelayCommand SendKitchenCommand { get; }
        public AsyncRelayCommand MarkServedCommand { get; }

        public AsyncRelayCommand CancelOrderCommand { get; }

        public AsyncRelayCommand GenerateBillCommand { get; }

        public RelayCommand NewParcelCommand { get; }

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

        public string CustomerName
        {
            get => _customerName;
            set
            {
                SetProperty(ref _customerName, value);
            }
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

        public bool CanGenerateBill => !IsBusy && OrderItems.Count > 0;

        public decimal Subtotal => OrderPricing.Round(OrderItems.Sum(item => item.LineTotal));

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

        public async Task<bool> ResumeSavedOrderAsync(int orderId)
        {
            if (IsBusy) return false;
            if (HasUnsavedChanges &&
                !_dialogs.Confirm("Load saved parcel?", "Replace the current parcel draft with the saved order? Unsaved changes will be discarded."))
                return false;

            var resumed = false;
            await ExecuteBusyActionAsync(async () =>
            {
                var order = await _orderService.GetOpenParcelAsync(orderId);
                if (order == null)
                {
                    SetStatus("This parcel is no longer open. Refresh Orders.", true);
                    return;
                }

                TaxPercentage = await _orderService.GetTaxPercentageAsync();
                _orderId = order.Id;
                _revision = order.Revision;
                CustomerName = order.CustomerName ?? string.Empty;
                OrderItems.Clear();
                foreach (var item in order.Items)
                    OrderItems.Add(CreateLineItem(item.MenuItemId, item.MenuItem?.Name ?? "Menu item",
                        item.MenuItem?.Category ?? string.Empty, item.Price, item.Quantity, item.TaxExempt));
                RecordSavedDraft();
                NotifyTotalsChanged();
                SetStatus($"Resumed parcel order #{order.Id}.", false);
                resumed = true;
            }, "Could not reopen the parcel order.");
            return resumed;
        }

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                TaxPercentage = await _orderService.GetTaxPercentageAsync();

                var menuItems = await _orderService.GetMenuItemsAsync();
                MenuItems.Clear();
                foreach (var menuItem in menuItems)
                {
                    MenuItems.Add(menuItem);
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

                NotifyTotalsChanged();
            }, "The parcel screen could not be loaded.");
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
                OrderItems.Add(CreateLineItem(menuItem.Id, menuItem.Name, menuItem.Category, menuItem.Price, 1, menuItem.TaxExempt));
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
                ResetDraft(false);
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

                var refreshedOrder = await _orderService.GetOrderAsync(order.Id);
                if (refreshedOrder?.Bill?.PaidAt != null || refreshedOrder?.Status == OrderStatus.Paid)
                {
                    ResetDraft(false);
                    SetStatus("Parcel order billed successfully. Ready for the next parcel.", false);
                }
            }, "The billing screen could not be opened.");
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
            NewParcelCommand.RaiseCanExecuteChanged();
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
                SetStatus("Add at least one item before saving the parcel order.", true);
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
                savedOrder = await _orderService.SaveParcelOrderAsync(_orderId, CustomerName, items, _revision);
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
                CustomerName = savedOrder.CustomerName ?? string.Empty;
                RecordSavedDraft();

                if (showSuccessMessage)
                {
                    SetStatus($"Parcel order #{savedOrder.Id} saved.", false);
                }
            }, "The parcel order could not be saved.");

            return savedOrder;
        }

        internal bool StartNewParcel()
        {
            if (IsBusy || (HasUnsavedChanges && !_dialogs.Confirm("Start a new takeaway?", "Discard the unsaved changes to this order?"))) return false;
            ResetDraft(true);
            return true;
        }

        internal void DiscardDraft() => ResetDraft(false);

        private void ResetDraft(bool showMessage)
        {
            _orderId = null;
            _revision = 0;
            CustomerName = string.Empty;
            OrderItems.Clear();
            RecordSavedDraft();
            NotifyTotalsChanged();

            if (showMessage)
            {
                SetStatus("Started a new parcel order.", false);
            }
        }
    }
}
