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
        private readonly OperatorSession _session;
        private readonly BillingService _billingService;
        private readonly ReceiptPrintService _receiptPrintService;
        private readonly int _orderId;
        private int _revision;
        private bool _isBusy;
        private string _tableNumber = string.Empty;
        private string _discountInput = "0";
        private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;
        private bool _isBilled;
        private string _paymentReference;
        private ReceiptData _receiptData;
        private decimal _taxPercentage = 5m;
        private string _taxName = "Tax";

        public BillingViewModel(BillingService billingService, ReceiptPrintService receiptPrintService, int orderId, LogService logService, OperatorSession session)
        {
            _logService = logService;
            _session = session;
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
            PreviewReceiptCommand = new RelayCommand(_ => ToggleReceiptPreview(), _ => CanPrintReceipt);
            PrintReceiptCommand = new RelayCommand(_ => PrintReceipt(), _ => CanPrintReceipt);
        }

        public ObservableCollection<BillingLineItemViewModel> Items { get; }

        public ObservableCollection<PaymentMethod> PaymentOptions { get; }

        public RelayCommand PreviewReceiptCommand { get; }

        private string _receiptPreview = string.Empty;
        public string ReceiptPreview
        {
            get => _receiptPreview;
            private set
            {
                if (SetProperty(ref _receiptPreview, value)) OnPropertyChanged(nameof(ShowReceiptPreview));
            }
        }

        public bool ShowReceiptPreview => !string.IsNullOrEmpty(ReceiptPreview);

        private void ToggleReceiptPreview()
        {
            if (ShowReceiptPreview) { ReceiptPreview = string.Empty; return; }
            var receipt = _receiptData;
            var culture = ReceiptProfile.Culture(receipt.Locale);
            var text = new System.Text.StringBuilder();
            text.AppendLine(receipt.RestaurantName).AppendLine(receipt.RestaurantAddress).AppendLine(receipt.RestaurantPhone);
            text.AppendLine($"Order #{receipt.OrderId} · {receipt.TableNumber}");
            text.AppendLine($"{BusinessTime.ToLocalTime(receipt.PaidAt):g} · {receipt.PaymentMethod}");
            foreach (var item in receipt.Items)
                text.AppendLine($"{item.Quantity} × {item.Name}   {item.LineTotal.ToString("C2", culture)}");
            text.AppendLine($"Subtotal: {receipt.Subtotal.ToString("C2", culture)}");
            var tax = receipt.TaxRate.HasValue ? $"{receipt.TaxName} ({receipt.TaxRate:0.##}%)" : receipt.TaxName;
            text.AppendLine($"{tax}: {receipt.Tax.ToString("C2", culture)}");
            text.AppendLine($"Discount: {receipt.Discount.ToString("C2", culture)}");
            if (!string.IsNullOrEmpty(receipt.DiscountReason)) text.AppendLine(receipt.DiscountReason);
            foreach (var paid in receipt.Payments)
                text.AppendLine($"{paid.Method}: {paid.Amount.ToString("C2", culture)} {paid.Reference}");
            text.Append($"Total: {receipt.Total.ToString("C2", culture)}");
            ReceiptPreview = text.ToString();
        }

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
                    OnPropertyChanged(nameof(CanEditDiscount));
                    OnPropertyChanged(nameof(CanEditPayment));
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
                    OnPropertyChanged(nameof(CanEditDiscount));
                    OnPropertyChanged(nameof(CanEditPayment));
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
                    OnPropertyChanged(nameof(CanEditDiscount));
                    OnPropertyChanged(nameof(CanEditPayment));
                    OnPropertyChanged(nameof(CanConfirmPayment));
                    OnPropertyChanged(nameof(CanPrintReceipt));
                    NotifyCommandStates();
                }
            }
        }

        public string PaymentReference
        {
            get => _paymentReference;
            set => SetProperty(ref _paymentReference, value);
        }

        private bool _percentDiscount;
        public bool PercentDiscount
        {
            get => _percentDiscount;
            set
            {
                if (!SetProperty(ref _percentDiscount, value)) return;
                OnPropertyChanged(nameof(Discount));
                OnPropertyChanged(nameof(FinalTotal));
                OnPropertyChanged("Item[]");
                NotifyCommandStates();
            }
        }

        public string DiscountReason { get; set; }
        internal string ManagerPassword { get; set; }
        public bool SplitPayment { get; set; }
        public string FirstPaymentAmount { get; set; }
        public PaymentMethod SecondPaymentMethod { get; set; } = PaymentMethod.Card;
        public string SecondPaymentReference { get; set; }

        public bool ReadOnly { get; set; }

        public bool CanEditDiscount => CanEditPayment && _session.MayManage;

        public bool CanEditPayment => !ReadOnly && !IsBilled && !IsBusy;

        public decimal Subtotal => _receiptData?.Subtotal ?? OrderPricing.Round(Items.Sum(item => item.LineTotal));

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

        public string TaxLabel => _receiptData?.TaxRate is decimal rate ? $"{_receiptData.TaxName} ({rate:0.##}%)" :
            _receiptData != null ? _receiptData.TaxName : $"{_taxName} ({TaxPercentage:0.##}%)";

        public decimal Tax => _receiptData?.Tax ?? OrderPricing.Tax(Items.Where(item => !item.TaxExempt).Sum(item => item.LineTotal), TaxPercentage);

        public decimal Discount => _receiptData?.Discount ?? (TryParseDiscount(DiscountInput, out var discount)
            ? OrderPricing.Round(PercentDiscount ? (Subtotal + Tax) * discount / 100m : discount)
            : 0m);

        public decimal FinalTotal => _receiptData?.Total ?? Math.Max(0m, OrderPricing.Round(Subtotal + Tax - Discount));

        public bool HasItems => Items.Count > 0;

        public bool CanConfirmPayment => CanEditPayment && HasItems && string.IsNullOrEmpty(this[nameof(DiscountInput)]);

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

                _revision = orderData.Order.Revision;
                _taxName = orderData.Restaurant?.TaxName ?? "Tax";
                TaxPercentage = orderData.Restaurant?.TaxPercentage ?? 5m;
                TableNumber = orderData.Table?.TableNumber ?? orderData.RoomNumber ?? BillingService.GetParcelLabel(orderData.Order.CustomerName);
                Items.Clear();
                foreach (var item in orderData.Order.Items)
                {
                    Items.Add(new BillingLineItemViewModel
                    {
                        TaxExempt = item.TaxExempt,
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

                PaymentReference = orderData.Order.Bill?.PaymentReference;
                DiscountReason = orderData.Order.Bill?.DiscountReason;
                OnPropertyChanged(nameof(DiscountReason));
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

                OnPropertyChanged(nameof(TaxLabel));
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

                        if (discount > (PercentDiscount ? 100m : Subtotal + Tax))
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

            var managerPassword = ManagerPassword;
            ManagerPassword = null;
            TryParseDiscount(DiscountInput, out var discount);
            PaymentSplit split = null;
            if (SplitPayment)
            {
                if (!TryParseDiscount(FirstPaymentAmount, out var first))
                {
                    SetStatus("Enter the amount received for the first payment.", true);
                    return;
                }
                split = new PaymentSplit(first, SecondPaymentMethod, SecondPaymentReference);
            }

            await ExecuteBusyActionAsync(async () =>
            {
                var options = new DiscountOptions(PercentDiscount, DiscountReason, managerPassword);
                var bill = await _billingService.ConfirmPaymentAsync(_orderId, discount, SelectedPaymentMethod, PaymentReference, split, options, _revision);
                var orderData = await _billingService.GetBillingOrderAsync(_orderId);
                _receiptData = _billingService.CreateReceiptData(orderData);
                PercentDiscount = false;
                DiscountInput = bill.Discount.ToString("0.##", CultureInfo.InvariantCulture);
                SelectedPaymentMethod = bill.PaymentMethod ?? SelectedPaymentMethod;
                IsBilled = true;
                OnPropertyChanged(nameof(TaxLabel));
                OnPropertyChanged(nameof(Subtotal));
                OnPropertyChanged(nameof(Tax));
                OnPropertyChanged(nameof(FinalTotal));
                SetStatus("Payment confirmed.", false);
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
            PreviewReceiptCommand.RaiseCanExecuteChanged();
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
