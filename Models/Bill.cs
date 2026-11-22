#nullable enable

namespace ServeX.Models
{
    public class Bill
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public decimal Subtotal { get; set; }

        public decimal Tax { get; set; }

        public decimal? RecordedTaxRate { get; set; }

        public string? RecordedTaxName { get; set; }

        public string? RecordedTaxNumber { get; set; }

        public string? RecordedLocale { get; set; }

        public decimal Discount { get; set; }

        public decimal Total { get; set; }

        public string? PaymentBreakdown { get; set; }

        public string? DiscountReason { get; set; }

        public string? PaymentReference { get; set; }

        public PaymentMethod? PaymentMethod { get; set; }

        public System.DateTime? PaidAt { get; set; }

        public Order? Order { get; set; }
    }
}
