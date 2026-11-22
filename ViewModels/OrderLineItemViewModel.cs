using System;

namespace ServeX.ViewModels
{
    public class OrderLineItemViewModel : ViewModelBase
    {
        private int _quantity;
        private decimal _unitPrice;

        public bool TaxExempt { get; set; }

        public event EventHandler QuantityChanged;

        public int MenuItemId { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }

        public decimal UnitPrice
        {
            get => _unitPrice;
            set
            {
                if (SetProperty(ref _unitPrice, value)) OnPropertyChanged(nameof(LineTotal));
            }
        }

        public int Quantity
        {
            get => _quantity;
            set
            {
                if (SetProperty(ref _quantity, value))
                {
                    OnPropertyChanged(nameof(LineTotal));
                    QuantityChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public decimal LineTotal => UnitPrice * Quantity;
    }
}
