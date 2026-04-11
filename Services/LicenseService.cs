namespace ServeX.Services
{
    public class LicenseService
    {
        public string Normalize(string licenseKey)
        {
            return (licenseKey ?? string.Empty).Trim().ToUpperInvariant();
        }

        public bool IsValid(string licenseKey)
        {
            return true;
        }

        public string GetValidationMessage()
        {
            return "License validation is disabled for local development.";
        }
    }
}
