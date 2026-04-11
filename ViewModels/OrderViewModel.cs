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
        private readonly OrderService _orderService;
        private readonly BillingWindowService _billingWindowService;
        private readonly int _tableId;
        private bool _isBusy;
        private int? _orderId;
        private string _tableNumber = string.Empty;
        private TableStatus _tableStatus = TableStatus.Available;
        private string _selectedCategory = "All";
        private string _statusMessage = string.Empty;
        private bool _isStatusError;
        private decimal _taxPercentage = 5m;

        public OrderViewModel(OrderService orderService, BillingWindowService billingWindowService, int tableId)
        {
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _billingWindowService = billingWindowService ?? throw new ArgumentNullException(nameof(billingWindowService));
            _tableId = tableId;

            Categories = new ObservableCollection<string>();
            MenuItems = new ObservableCollection<MenuItem>();
            FilteredMenuItems = new ObservableCollection<MenuItem>();
            OrderItems = new ObservableCollection<OrderLineItemViewModel>();
            OrderItems.CollectionChanged += OnOrderItemsCollectionChanged;

            AddItemCommand = new RelayCommand(menuItem => AddItem(menuItem as MenuItem), menuItem => !IsBusy && menuItem is MenuItem);
            IncreaseQuantityCommand = new RelayCommand(item => IncreaseQuantity(item as OrderLineItemViewModel), item => !IsBusy && item is OrderLineItemViewModel);
            DecreaseQuantityCommand = new RelayCommand(item => DecreaseQuantity(item as OrderLineItemViewModel), item => !IsBusy && item is OrderLineItemViewModel);
            RemoveItemCommand = new RelayCommand(item => RemoveItem(item as OrderLineItemViewModel), item => !IsBusy && item is OrderLineItemViewModel);
            SaveOrderCommand = new RelayCommand(async _ => await SaveOrderAsync(), _ => CanSaveOrder);
            GenerateBillCommand = new RelayCommand(async _ => await GenerateBillAsync(), _ => CanGenerateBill);
        }

        public ObservableCollection<string> Categories { get; }

        public ObservableCollection<MenuItem> MenuItems { get; }

        public ObservableCollection<MenuItem> FilteredMenuItems { get; }

        public ObservableCollection<OrderLineItemViewModel> OrderItems { get; }

        public RelayCommand AddItemCommand { get; }

        public RelayCommand IncreaseQuantityCommand { get; }

        public RelayCommand DecreaseQuantityCommand { get; }

        public RelayCommand RemoveItemCommand { get; }

        public RelayCommand SaveOrderCommand { get; }

        public RelayCommand GenerateBillCommand { get; }

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

        public string WindowTitle => string.Format("Order - {0}", TableNumber);

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

        public bool CanGenerateBill => !IsBusy && OrderItems.Count > 0;

        public decimal Subtotal => decimal.Round(OrderItems.Sum(item => item.LineTotal), 2);

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

        public decimal Tax => decimal.Round(Subtotal * (TaxPercentage / 100m), 2);

        public decimal Total => Subtotal + Tax;

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                var table = await _orderService.GetTableAsync(_tableId);
                if (table == null)
                {
                    throw new InvalidOperationException("The selected table could not be loaded.");
                }

                TableNumber = table.TableNumber;
                TableStatus = table.Status;
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

                var order = await _orderService.GetActiveOrderForTableAsync(_tableId);
                OrderItems.Clear();
                _orderId = order?.Id;

                if (order != null)
                {
                    foreach (var item in order.Items.OrderBy(item => item.MenuItem?.Category).ThenBy(item => item.MenuItem?.Name))
                    {
                        AddLoadedItem(item);
                    }

                    SetStatus(string.Format("Loaded active order #{0}.", order.Id), false);
                }
                else
                {
                    ClearStatus();
                }

                NotifyTotalsChanged();
            }, "The order screen could not be loaded.");
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
                var newItem = CreateLineItem(menuItem.Id, menuItem.Name, menuItem.Category, menuItem.Price, 1);
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
                await _billingWindowService.ShowAsync(order.Id);
                await LoadAsync();
            }, "The billing screen could not be opened.");
        }

        private void ApplyCategoryFilter()
        {
            FilteredMenuItems.Clear();

            var items = SelectedCategory == "All"
                ? MenuItems
                : new ObservableCollection<MenuItem>(MenuItems.Where(item => item.Category == SelectedCategory));

            foreach (var item in items)
            {
                FilteredMenuItems.Add(item);
            }
        }

        private OrderLineItemViewModel CreateLineItem(int menuItemId, string name, string category, decimal unitPrice, int quantity)
        {
            return new OrderLineItemViewModel
            {
                MenuItemId = menuItemId,
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
                item.Quantity);

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
            catch (DbUpdateException)
            {
                SetStatus(fallbackMessage, true);
            }
            catch (Exception)
            {
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
                savedOrder = await _orderService.SaveTableOrderAsync(_tableId, items);
                _orderId = savedOrder.Id;
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
