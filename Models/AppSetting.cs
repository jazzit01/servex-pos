#nullable enable

namespace ServeX.Models
{
    public class AppSetting
    {
        public int Id { get; set; }

        public string RestaurantName { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Locale { get; set; } = "en-IN";
        public string TaxName { get; set; } = "Tax";
        public string? TaxNumber { get; set; }
        public int ReceiptWidth { get; set; } = 80;

        public decimal TaxPercentage { get; set; }

        public string LicenseKey { get; set; } = string.Empty;
    }
}
