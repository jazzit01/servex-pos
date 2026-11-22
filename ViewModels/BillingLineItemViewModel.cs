namespace ServeX.ViewModels
{
    public class BillingLineItemViewModel : ViewModelBase
    {
        public bool TaxExempt { get; set; }

        public string Name { get; set; }

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal LineTotal { get; set; }
    }
}
