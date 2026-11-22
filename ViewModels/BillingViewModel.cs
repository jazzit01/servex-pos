using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ServeX.Models;
using ServeX.Services;

namespace ServeX.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class BillingViewModel : ViewModelBase, IDataErrorInfo
    {
        private readonly LogService _logService;
        private readonly BillingService _billingService;
        private readonly ReceiptPrintService _receiptPrintService;
        private readonly int _orderId;
        private bool _isBusy;
        private string _tableNumber = string.Empty;
        private string _discountInput = "0";
        private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;
        private bool _isBilled;
        private ReceiptData _receiptData;
        private decimal _taxPercentage = 5m;

        public BillingViewModel(BillingService billingService, ReceiptPrintService receiptPrintService, int orderId, LogService logService)
        {
            _logService = logService;
            _billingService = billingService ?? throw new ArgumentNullException(nameof(billingService));
            _receiptPrintService = receiptPrintService ?? throw new ArgumentNullException(nameof(receiptPrintService));
            _orderId = orderId;

            Items = new ObservableCollection<BillingLineItemViewModel>();
            PaymentOptions = new ObservableCollection<PaymentMethod>
            {
                PaymentMethod.Cash,
                PaymentMethod.Card,
                PaymentMethod.Upi
            };

            ConfirmPaymentCommand = new AsyncRelayCommand(async _ => await ConfirmPaymentAsync(), _ => CanConfirmPayment);
            PrintReceiptCommand = new RelayCommand(_ => PrintReceipt(), _ => CanPrintReceipt);
        }

        public ObservableCollection<BillingLineItemViewModel> Items { get; }

        public ObservableCollection<PaymentMethod> PaymentOptions { get; }

        public AsyncRelayCommand ConfirmPaymentCommand { get; }

        public RelayCommand PrintReceiptCommand { get; }

        public string WindowTitle => string.Format("Billing - Order #{0}", _orderId);

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanConfirmPayment));
                    OnPropertyChanged(nameof(CanPrintReceipt));
                    NotifyCommandStates();
                }
            }
        }

        public string TableNumber
        {
            get => _tableNumber;
            private set => SetProperty(ref _tableNumber, value);
        }

        public string DiscountInput
        {
            get => _discountInput;
            set
            {
                if (SetProperty(ref _discountInput, value))
                {
                    OnPropertyChanged(nameof(Discount));
                    OnPropertyChanged(nameof(FinalTotal));
                    OnPropertyChanged(nameof(CanConfirmPayment));
                    OnPropertyChanged(nameof(DiscountInput));
                    NotifyCommandStates();
                }
            }
        }

        public PaymentMethod SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set
            {
                if (SetProperty(ref _selectedPaymentMethod, value))
                {
                    NotifyCommandStates();
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

        public bool IsBilled
        {
            get => _isBilled;
            private set
            {
                if (SetProperty(ref _isBilled, value))
                {
                    OnPropertyChanged(nameof(CanConfirmPayment));
                    OnPropertyChanged(nameof(CanPrintReceipt));
                    NotifyCommandStates();
                }
            }
        }

        public decimal Subtotal => OrderPricing.Round(Items.Sum(item => item.LineTotal));

        public decimal TaxPercentage
        {
            get => _taxPercentage;
            private set
            {
                if (SetProperty(ref _taxPercentage, value))
                {
                    OnPropertyChanged(nameof(TaxLabel));
                    OnPropertyChanged(nameof(Tax));
                    OnPropertyChanged(nameof(FinalTotal));
                }
            }
        }

        public string TaxLabel => string.Format("Tax ({0:0.##}%)", TaxPercentage);

        public decimal Tax => OrderPricing.Tax(Subtotal, TaxPercentage);

        public decimal Discount => TryParseDiscount(DiscountInput, out var discount) ? OrderPricing.Round(discount) : 0m;

        public decimal FinalTotal => Math.Max(0m, OrderPricing.Round(Subtotal + Tax - Discount));

        public bool HasItems => Items.Count > 0;

        public bool CanConfirmPayment => !IsBusy && !IsBilled && HasItems && string.IsNullOrEmpty(this[nameof(DiscountInput)]);

        public bool CanPrintReceipt => !IsBusy && IsBilled && _receiptData != null;

        public string Error => string.Empty;

        public async Task LoadAsync()
        {
            await ExecuteBusyActionAsync(async () =>
            {
                var orderData = await _billingService.GetBillingOrderAsync(_orderId);
                if (orderData?.Order == null)
                {
                    throw new InvalidOperationException("The order could not be loaded for billing.");
                }

                TaxPercentage = await _billingService.GetTaxPercentageAsync();
                TableNumber = orderData.Table?.TableNumber ?? "Parcel";
                Items.Clear();
                foreach (var item in orderData.Order.Items)
                {
                    Items.Add(new BillingLineItemViewModel
                    {
                        Name = item.MenuItem?.Name ?? "Item",
                        Quantity = item.Quantity,
                        UnitPrice = item.Price,
                        LineTotal = item.Price * item.Quantity
                    });
                }

                if (orderData.Order.Bill != null)
                {
                    DiscountInput = orderData.Order.Bill.Discount.ToString("0.##", CultureInfo.InvariantCulture);
                }
                else
                {
                    DiscountInput = "0";
                }

                if (orderData.Order.Bill?.PaymentMethod != null)
                {
                    SelectedPaymentMethod = orderData.Order.Bill.PaymentMethod.Value;
                }

                IsBilled = orderData.Order.Status is OrderStatus.Billed or OrderStatus.Paid || orderData.Order.Bill?.PaidAt != null;
                _receiptData = IsBilled ? _billingService.CreateReceiptData(orderData) : null;

                if (IsBilled)
                {
                    SetStatus("This order has already been billed.", false);
                }
                else
                {
                    ClearStatus();
                }

                OnPropertyChanged(nameof(Subtotal));
                OnPropertyChanged(nameof(Tax));
                OnPropertyChanged(nameof(Discount));
                OnPropertyChanged(nameof(FinalTotal));
                OnPropertyChanged(nameof(HasItems));
            }, "The billing screen could not be loaded.");
        }

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(DiscountInput):
                        if (string.IsNullOrWhiteSpace(DiscountInput))
                        {
                            return string.Empty;
                        }

                        if (!TryParseDiscount(DiscountInput, out var discount))
                        {
                            return "Enter a valid discount amount.";
                        }

                        if (discount < 0)
                        {
                            return "Discount cannot be negative.";
                        }

                        if (discount > Subtotal + Tax)
                        {
                            return "Discount cannot exceed the bill total.";
                        }

                        return string.Empty;

                    default:
                        return string.Empty;
                }
            }
        }

        private async Task ConfirmPaymentAsync()
        {
            if (!HasItems)
            {
                SetStatus("There are no billable items in this order.", true);
                return;
            }

            if (!string.IsNullOrEmpty(this[nameof(DiscountInput)]))
            {
                SetStatus(this[nameof(DiscountInput)], true);
                return;
            }

            await ExecuteBusyActionAsync(async () =>
            {
                var bill = await _billingService.ConfirmPaymentAsync(_orderId, Discount, SelectedPaymentMethod);
                var orderData = await _billingService.GetBillingOrderAsync(_orderId);
                _receiptData = _billingService.CreateReceiptData(orderData);
                DiscountInput = bill.Discount.ToString("0.##", CultureInfo.InvariantCulture);
                SelectedPaymentMethod = bill.PaymentMethod ?? SelectedPaymentMethod;
                IsBilled = true;
                SetStatus("Payment confirmed successfully. The table is now available.", false);
            }, "The payment could not be confirmed.");
        }

        [SupportedOSPlatform("windows")]
        private void PrintReceipt()
        {
            if (_receiptData == null)
            {
                SetStatus("Confirm payment before printing the receipt.", true);
                return;
            }

            try
            {
                var sent = _receiptPrintService.PrintReceipt(_receiptData);
                SetStatus(sent ? "Receipt sent to the printer." : "Printing cancelled.", false);
            }
            catch (ReceiptPrinterException exception)
            {
                SetStatus(exception.Message, true);
            }
            catch (Exception exception)
            {
                _logService.LogError("Receipt printing failed", exception);
                SetStatus("Receipt printing failed. Please check the printer and try again.", true);
            }
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
            ConfirmPaymentCommand.RaiseCanExecuteChanged();
            PrintReceiptCommand.RaiseCanExecuteChanged();
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

        private static bool TryParseDiscount(string value, out decimal discount)
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out discount) ||
                   decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out discount);
        }
    }
}
