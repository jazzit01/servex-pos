using System;

namespace ServeX.ViewModels
{
    public class OrderLineItemViewModel : ViewModelBase
    {
        private int _quantity;

        public event EventHandler QuantityChanged;

        public int MenuItemId { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }

        public decimal UnitPrice { get; set; }

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
