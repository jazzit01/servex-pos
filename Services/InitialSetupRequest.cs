namespace ServeX.Services
{
    public class InitialSetupRequest
    {
        public string RestaurantName { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public decimal TaxPercentage { get; set; }

        public string AdminUsername { get; set; } = string.Empty;

        public string AdminPassword { get; set; } = string.Empty;

        public string LicenseKey { get; set; } = string.Empty;
    }
}
