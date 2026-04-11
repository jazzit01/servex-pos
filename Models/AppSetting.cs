namespace ServeX.Models
{
    public class AppSetting
    {
        public int Id { get; set; }

        public string RestaurantName { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public decimal TaxPercentage { get; set; }

        public string LicenseKey { get; set; } = string.Empty;
    }
}
